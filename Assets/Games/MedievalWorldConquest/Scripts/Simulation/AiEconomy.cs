using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Lords' economy: build plans and spending, training troops, research, and shipping surplus to their academies.
    /// </summary>
    public partial class World
    {
        // Army tuning (switches for the balance simulations; the defaults are the game's). Since 2026-10-01 (user's
        // choice, after the army simulations): coins and noblemen don't count as army spending, and the record fades
        // over five days, so lords keep real armies in every village and rebuild what they lose.
        /// <summary>Whether gold coins and noblemen count towards a lord's spending on its army.</summary>
        public static bool ExpansionIsArmySpending = false;
        /// <summary>Game days over which a lord's record of spending fades by half (0: never: a lifetime total).</summary>
        public static double LordSpendingMemoryDays = 5;
        /// <summary>
        /// Each village of a lord's keeps at least this share of its farm's room (what its buildings leave) filled
        /// with its own troops, training them before anything else (0: no such floor).
        /// </summary>
        public static double GarrisonFloor = 0;

        /// <summary>
        /// Spends the village's resources on buildings and troops, keeping troops to the personality's share of all
        /// spending over time (whichever side is behind gets first call on the stores).
        /// </summary>
        void AiSpend(Player lord, Village v, AiStyle style)
        {
            double share = Math.Min(0.8, style.MilitaryShare * SkillMilitary);
            bool canTrain = v.Level(BuildingType.Barracks) > 0;
            if (LordSpendingMemoryDays > 0)
            {
                double fade = Math.Pow(0.5, Math.Max(0, Now - lord.SpentAt) / (LordSpendingMemoryDays * SecondsPerDay));
                lord.SpentOnBuildings *= fade;
                lord.SpentOnTroops *= fade;
                lord.SpentAt = Now;
            }
            bool troopsDue = canTrain && lord.SpentOnTroops < share * (lord.SpentOnBuildings + lord.SpentOnTroops);

            // A village short of its garrison trains troops before anything else.
            if (canTrain && GarrisonFloor > 0 && v.TroopPopulation < GarrisonFloor * Math.Max(0, v.PopulationCapacity - v.BuildingPopulation))
                AiRecruit(lord, v, style, 1, 2);

            // With an academy, noblemen come first: the lord saves up for them rather than spending on anything
            // else, unless its stores are about to overflow.
            // On a gold-coin world, a nobleman needs a free slot first: coins are minted (and saved up for) the same
            // way, and they come before the army's share: expansion runs on them.
            if ((troopsDue || Settings.GoldCoins) && WantsNobles(v))
            {
                if (FreeNobleSlots(lord) <= 0)
                {
                    var coin = MintCoins(v, Math.Max(1, CheckMint(v, 1).MaxAffordable));
                    if (coin.Status == MintStatus.Ok) SpentOnExpansion(lord, coin.Total);
                    else if (coin.Status == MintStatus.NotEnoughResources && !double.IsInfinity(coin.AffordableIn) && FullestStock(v) < 0.9) return;
                }
                else
                {
                    var noble = Recruit(v, UnitType.Nobleman, 1);
                    if (noble.Status == RecruitStatus.Ok) SpentOnExpansion(lord, noble.Total);
                    else if (noble.Status == RecruitStatus.NotEnoughResources && !double.IsInfinity(noble.AffordableIn) && FullestStock(v) < 0.9) return;
                }
            }

            // Between campaigns, an academy puts its surplus into coins: the slots the next conquests will need.
            else if (Settings.GoldCoins && style.Expands && v.Level(BuildingType.Academy) > 0)
            {
                int spare = int.MaxValue;
                foreach (ResourceType r in ResourceTypes)
                    spare = Math.Min(spare, (int)((v.Stock(r) - 0.4 * v.StorageCapacity) / CoinCost.Get(r)));
                if (spare >= 1)
                {
                    var coin = MintCoins(v, spare);
                    if (coin.Status == MintStatus.Ok) SpentOnExpansion(lord, coin.Total);
                }
            }

            // Research comes before everything else: it's cheap next to what it opens up, so the lord saves up for
            // it. (Even with other stores overflowing: what it's short of is usually wood, which everything else
            // would use up.)
            if (canTrain && AiResearch(lord, v, style)) return;
            if (troopsDue) AiRecruit(lord, v, style, share, 3);
            bool saving = AiBuild(lord, v);
            // Stores almost full with nothing more to build right now: troops rather than waste.
            if (canTrain && !troopsDue && (!saving || v.Queue.Count >= MaxBuildQueue) && FullestStock(v) > 0.85)
                AiRecruit(lord, v, style, 1, 1);
        }

        /// <summary>Records what a lord spent on coins or a nobleman.</summary>
        static void SpentOnExpansion(Player lord, Cost cost)
        {
            double total = (double)cost.Wood + cost.Clay + cost.Iron;
            lord.SpentOnExpansion += total;
            if (ExpansionIsArmySpending) lord.SpentOnTroops += total;
        }

        static double FullestStock(Village v) => Math.Max(v.Wood, Math.Max(v.Clay, v.Iron)) / Math.Max(1, v.StorageCapacity);

        /// <summary>Queues buildings from the plan. Returns whether it's saving up for the next one.</summary>
        bool AiBuild(Player lord, Village v)
        {
            while (v.Queue.Count < MaxBuildQueue)
            {
                var type = AiNextBuilding(lord, v);
                if (type == null) return false;
                var check = QueueBuild(v, type.Value);
                if (check.Status == BuildStatus.NotEnoughResources) return true;
                if (check.Status != BuildStatus.Ok) return false;
                lord.SpentOnBuildings += check.Cost.Wood + check.Cost.Clay + check.Cost.Iron;
            }
            return false;
        }

        /// <summary>The next building to upgrade: the farm if people are running short, otherwise the next step of the plan.</summary>
        BuildingType? AiNextBuilding(Player lord, Village v)
        {
            // Room for more people first: without it nothing else can grow (and a nobleman needs a hundred).
            double roomNeeded = Math.Max(Math.Max(20, v.PopulationCapacity * 0.08), WantsNobles(v) ? 110 : 0);
            if (v.QueuedCount(BuildingType.Farm) == 0 && v.FreePopulation < roomNeeded
                && v.Level(BuildingType.Farm) < Buildings.Get(BuildingType.Farm).MaxLevel)
            {
                // A farm dearer than the warehouse holds needs a bigger warehouse first (or the village stalls for good).
                if (CheckBuild(v, BuildingType.Farm).Status != BuildStatus.WarehouseTooSmall) return BuildingType.Farm;
                if (v.QueuedCount(BuildingType.Warehouse) == 0) return BuildingType.Warehouse;
                return null;
            }

            // Once it can, an academy, then a warehouse big enough to save up for a nobleman.
            if (StyleOf(lord.Personality).Expands && v.Level(BuildingType.Academy) == 0 && v.QueuedCount(BuildingType.Academy) == 0 && Buildings.UnmetRequirement(BuildingType.Academy, v) == null)
            {
                var status = CheckBuild(v, BuildingType.Academy).Status;
                if (status == BuildStatus.WarehouseTooSmall) return v.QueuedCount(BuildingType.Warehouse) == 0 ? BuildingType.Warehouse : (BuildingType?)null;
                if (status == BuildStatus.FarmTooSmall) return v.QueuedCount(BuildingType.Farm) == 0 ? BuildingType.Farm : (BuildingType?)null;
                if (status == BuildStatus.Ok || status == BuildStatus.NotEnoughResources) return BuildingType.Academy;
            }
            if (WantsNobles(v) && v.StorageCapacity < NobleCostMax && v.QueuedCount(BuildingType.Warehouse) == 0)
                return BuildingType.Warehouse;

            // The plan, from the first step not yet reached (everything before it is built or queued).
            var plan = PlanFor(lord.Personality);
            int cap = StageCapOf(lord);
            int first = PlanProgress(lord, v, plan);
            for (int i = first; i < plan.Count; i++)
            {
                var (type, level, stage) = plan[i];
                if (stage > cap) break;
                if (v.Level(type) + v.QueuedCount(type) >= level) continue;
                switch (CheckBuild(v, type).Status)
                {
                    case BuildStatus.NeedsBuilding:
                    case BuildStatus.MaxLevel:
                        continue; // come back to it once it's unlocked
                    case BuildStatus.WarehouseTooSmall:
                        return v.QueuedCount(BuildingType.Warehouse) == 0 ? BuildingType.Warehouse : (BuildingType?)null;
                    case BuildStatus.FarmTooSmall:
                        return v.QueuedCount(BuildingType.Farm) == 0 ? BuildingType.Farm : (BuildingType?)null;
                    default:
                        return type;
                }
            }
            return null;
        }

        /// <summary>How many stages a build plan has; a lord's own limit may stop it sooner.</summary>
        public const int PlanStages = 40;

        /// <summary>The plan stage after which a lord who expands goes straight for the academy (mines at about 12). (Tuning value.)</summary>
        public static int AcademyRushStage = 12;

        static readonly Dictionary<AiPersonality, List<(BuildingType type, int level, int stage)>> Plans =
            new Dictionary<AiPersonality, List<(BuildingType type, int level, int stage)>>();

        /// <summary>How far a lord's build plan goes: its own limit (noobs each have one), or its personality's.</summary>
        static int StageCapOf(Player lord) => lord.StageCap > 0 ? lord.StageCap : StyleOf(lord.Personality).MaxStage;

        // For each village, the first step of its owner's plan it hadn't reached when last looked at (not saved).
        // Buildings only go down when catapults hit them, which forgets the village's place (see ForgetPlanProgress).
        [NonSerialized] Dictionary<int, (AiPersonality personality, int index)> planProgress;

        /// <summary>The first step of the plan the village hasn't built or queued, remembered between turns.</summary>
        int PlanProgress(Player lord, Village v, List<(BuildingType type, int level, int stage)> plan)
        {
            if (planProgress == null) planProgress = new Dictionary<int, (AiPersonality, int)>();
            int index = planProgress.TryGetValue(v.Id, out var known) && known.personality == lord.Personality ? known.index : 0;
            while (index < plan.Count && v.Level(plan[index].type) + v.QueuedCount(plan[index].type) >= plan[index].level) index++;
            planProgress[v.Id] = (lord.Personality, index);
            return index;
        }

        /// <summary>Forgets how far a village had got with its plan (after catapults knock a building down).</summary>
        void ForgetPlanProgress(Village v) => planProgress?.Remove(v.Id);

        /// <summary>Whether the village has built (or queued) everything its lord's plan will ever ask for.</summary>
        bool PlanDone(Player lord, Village v)
        {
            var plan = PlanFor(lord.Personality);
            int index = PlanProgress(lord, v, plan);
            return index >= plan.Count || plan[index].stage > StageCapOf(lord);
        }

        /// <summary>Resource buildings first at each stage, then the rest.</summary>
        static readonly BuildingType[] PlanOrder =
        {
            BuildingType.TimberCamp, BuildingType.ClayPit, BuildingType.IronMine, BuildingType.Farm, BuildingType.Warehouse,
            BuildingType.Headquarters, BuildingType.Barracks, BuildingType.Smithy, BuildingType.Wall, BuildingType.Stable,
            BuildingType.Workshop, BuildingType.Market, BuildingType.HidingPlace,
        };

        /// <summary>
        /// A personality's build plan: one step per building level, in order. At stage s every building is taken to
        /// its ratio × s, so the village grows evenly in the personality's proportions.
        /// </summary>
        static List<(BuildingType type, int level, int stage)> PlanFor(AiPersonality personality)
        {
            lock (Plans)
            {
                if (Plans.TryGetValue(personality, out var cached)) return cached;
                var style = StyleOf(personality);
                var plan = new List<(BuildingType type, int level, int stage)>();
                var reached = new int[Buildings.Count];
                for (int b = 0; b < Buildings.Count; b++) reached[b] = Buildings.Get((BuildingType)b).StartingLevel;
                int stage = 0;
                void Reach(BuildingType type, int target)
                {
                    int b = (int)type;
                    while (reached[b] < Math.Min(Buildings.Get(type).MaxLevel, target)) plan.Add((type, ++reached[b], stage));
                }
                for (stage = 1; stage <= PlanStages; stage++)
                {
                    foreach (var type in PlanOrder) Reach(type, (int)Math.Floor(style.BuildRatios[(int)type] * stage + 0.5));
                    // A regular player's opening, as in Tribal Wars: once the mines are going, straight to a smithy
                    // that can research axemen, for raiding.
                    if (stage == 3 && style.Expands)
                    {
                        Reach(BuildingType.Headquarters, 3);
                        Reach(BuildingType.Barracks, 1);
                        Reach(BuildingType.Headquarters, 5);
                        Reach(BuildingType.Smithy, 2);
                    }
                    // And, as Tribal Wars players do once their mines are going, a rush for the academy: its
                    // Headquarters, smithy and market before most else.
                    if (stage == AcademyRushStage && style.Expands)
                    {
                        Reach(BuildingType.Headquarters, 20);
                        Reach(BuildingType.Smithy, 20);
                        Reach(BuildingType.Market, 10);
                    }
                }
                Plans[personality] = plan;
                return plan;
            }
        }

        /// <summary>Trains up to <paramref name="batches"/> batches, stopping once troops have had their share of spending.</summary>
        void AiRecruit(Player lord, Village v, AiStyle style, double share, int batches)
        {
            for (int i = 0; i < batches; i++)
            {
                if (v.TroopPopulation >= (long)style.TroopPopulationPerStage * StageCapOf(lord)) return;
                var unit = AiPickUnit(lord, v, style);
                if (unit == null) return;
                var def = Units.Get(unit.Value);
                int count = Math.Min(MaxAffordable(v, unit.Value), 5 + v.Level(def.Building) * 4);
                if (count <= 0) return; // saving up for them
                var check = Recruit(v, unit.Value, count);
                if (check.Status != RecruitStatus.Ok) return;
                lord.SpentOnTroops += check.Total.Wood + check.Total.Clay + check.Total.Iron;
                if (lord.SpentOnTroops >= share * (lord.SpentOnBuildings + lord.SpentOnTroops)) return;
            }
        }

        /// <summary>The unit the army is shortest of, compared with the personality's mix (more defenders after being attacked).</summary>
        UnitType? AiPickUnit(Player lord, Village v, AiStyle style)
        {
            var army = ArmyOf(v);
            int total = 0;
            foreach (int n in army) total += n;
            bool threatened = lord.LastAttackedAt >= 0 && Now - lord.LastAttackedAt < SecondsPerDay;

            UnitType? best = null;
            double bestNeed = double.MinValue;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units.Get((UnitType)i);
                double weight = style.UnitWeights[i];
                if (threatened && (u.Type == UnitType.Spearman || u.Type == UnitType.Swordsman)) weight = weight * 2 + 0.2;
                // Units it can't afford yet still count: it saves up for them rather than settling for cheap ones.
                if (weight <= 0 || v.Level(u.Building) < u.RequiredLevel || !v.IsResearched(u.Type)) continue;
                if (QueuedBatches(v, u.Building) >= MaxRecruitQueue) continue;
                double need = weight * (total + 10) - army[i];
                if (need > bestNeed)
                {
                    bestNeed = need;
                    best = u.Type;
                }
            }
            return best;
        }

        /// <summary>
        /// Researches, at the smithy, the next unit the lord likes to have that it can research now. Returns whether
        /// it's saving up for one.
        /// </summary>
        bool AiResearch(Player lord, Village v, AiStyle style)
        {
            if (v.Level(BuildingType.Smithy) <= 0 || v.Researching.Count > 0) return false;
            for (int i = 0; i < Units.Count; i++)
            {
                if (style.UnitWeights[i] <= 0 || v.IsResearched((UnitType)i)) continue;
                var check = StartResearch(v, (UnitType)i);
                if (check.Status == ResearchStatus.NotEnoughResources) return !double.IsInfinity(check.AffordableIn);
                if (check.Status != ResearchStatus.Ok) continue;
                lord.SpentOnBuildings += check.Cost.Wood + check.Cost.Clay + check.Cost.Iron;
                return false; // one at a time
            }
            return false;
        }

        /// <summary>
        /// All of a village's own troops: at home, in training, out on attacks or support or heading home, and (if
        /// <paramref name="stationed"/>) supporting other villages. (Lords skip the last: finding them means looking
        /// through every village, too slow for every lord's turn.)
        /// </summary>
        int[] ArmyOf(Village v, bool stationed = false)
        {
            var army = new int[Units.Count];
            for (int i = 0; i < Units.Count; i++) army[i] = v.TroopCount((UnitType)i);
            foreach (var o in v.Recruitment) army[(int)o.Unit] += o.Remaining;
            foreach (var c in CommandsOf(v.OwnerId))
            {
                bool ours = c.Kind == CommandKind.Return ? c.ToVillageId == v.Id : c.FromVillageId == v.Id;
                if (ours)
                    for (int i = 0; i < Units.Count && i < c.Troops.Length; i++) army[i] += c.Troops[i];
            }
            if (stationed)
                foreach (var host in Villages)
                    foreach (var g in host.Supports)
                        if (g.FromVillageId == v.Id && g.OwnerId == v.OwnerId)
                            for (int i = 0; i < Units.Count && i < g.Troops.Length; i++) army[i] += g.Troops[i];
            return army;
        }

        /// <summary>How far (in fields) a lord's villages send resources to its academy.</summary>
        public const double AiShippingRange = 40;

        /// <summary>
        /// As Tribal Wars players do, a lord's villages without an academy send their surplus by merchant to the
        /// nearest one that has one, where it becomes coins (or noblemen): always on a gold-coin world, and on a flat
        /// one while that academy is saving for noblemen. Each keeps enough back to go on growing. A satellite lord
        /// in the endgame feeds the nearest academy of its faction's winning side instead (its own included).
        /// </summary>
        void AiShipToAcademy(Player lord, Village v, AiStyle style)
        {
            bool feeding = FactionsFormed && IsSatellite(TribeOf(lord));
            if (v.Level(BuildingType.Market) <= 0 || (!feeding && (!style.Expands || v.Level(BuildingType.Academy) > 0))) return;
            // Only true surplus: the village first builds as fast as it can (its queue full) unless its plan is done
            // or its stores are overflowing, and it keeps back what its own next building needs.
            if (v.Queue.Count < MaxBuildQueue && !PlanDone(lord, v) && FullestStock(v) < 0.9) return;
            Village academy = feeding ? CoreAcademyNear(lord, v) : null;
            double nearest = AiShippingRange;
            if (!feeding)
                foreach (var own in VillagesOf(lord.Id))
                {
                    if (own == v || own.Level(BuildingType.Academy) <= 0) continue;
                    double d = Distance(v, own);
                    if (d <= nearest)
                    {
                        nearest = d;
                        academy = own;
                    }
                }
            if (academy == null || (!feeding && !Settings.GoldCoins && !WantsNobles(academy))) return;
            int merchants = MerchantsFree(v);
            if (merchants <= 0) return;

            Touch(academy);
            int capacity = merchants * Buildings.MerchantCarry;
            var goods = new int[3];
            int total = 0;
            // Keep back what its next building (and its own academy, once it can build one) will cost.
            var next = AiNextBuilding(lord, v);
            var reserve = next.HasValue ? Buildings.CostOf(next.Value, v.NextLevel(next.Value)) : default;
            if (Buildings.UnmetRequirement(BuildingType.Academy, v) == null)
            {
                var academyCost = Buildings.CostOf(BuildingType.Academy, 1);
                reserve = new Cost(Math.Max(reserve.Wood, academyCost.Wood), Math.Max(reserve.Clay, academyCost.Clay), Math.Max(reserve.Iron, academyCost.Iron));
            }
            foreach (ResourceType r in ResourceTypes)
            {
                double surplus = v.Stock(r) - Math.Max(0.4 * v.StorageCapacity, reserve.Get(r));
                double room = academy.StorageCapacity - academy.Stock(r);
                goods[(int)r] = (int)Math.Max(0, Math.Min(surplus, room));
                total += goods[(int)r];
            }
            if (total < 2000) return;
            if (total > capacity)
                for (int i = 0; i < 3; i++) goods[i] = (int)((long)goods[i] * capacity / total);
            SendResources(v, academy, new Cost(goods[0], goods[1], goods[2]));
        }

        /// <summary>The nearest academy (within shipping range) of a lord on the winning side of this satellite lord's faction.</summary>
        Village CoreAcademyNear(Player lord, Village v)
        {
            var mine = TribeOf(lord);
            Village best = null;
            double nearest = double.MaxValue;
            foreach (var u in VillagesNear(v.X, v.Y, AiShippingRange))
            {
                if (u.IsBarbarian || u.OwnerId == lord.Id || u.Level(BuildingType.Academy) <= 0) continue;
                var t = TribeOf(u.OwnerId);
                if (t == null || t.FactionId != mine.FactionId || !OnCoreSide(t)) continue;
                double d = Distance(v, u);
                if (d < nearest)
                {
                    nearest = d;
                    best = u;
                }
            }
            return best;
        }
    }
}
