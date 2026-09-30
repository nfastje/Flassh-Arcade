using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>How one personality plays: what it builds, what it trains, how much goes on troops, how warlike it is.</summary>
    class AiStyle
    {
        /// <summary>Per <see cref="BuildingType"/>: at each stage of growth a building is taken to about ratio × stage.</summary>
        public double[] BuildRatios;
        /// <summary>Per <see cref="UnitType"/>: the share of the army the lord wants of each unit.</summary>
        public double[] UnitWeights;
        /// <summary>Fraction of all spending that goes on troops.</summary>
        public double MilitaryShare;
        /// <summary>
        /// Chance, each turn, that the lord looks for a rival (a player who fights back: another lord, or the human)
        /// to make war on. Barbarians, noobs and inactive players aren't war: everyone but the noobs farms them.
        /// </summary>
        public double Aggression;
        /// <summary>
        /// The units it farms with, in the order it picks them (fastest carriers first). Spearmen and swordsmen
        /// among them only go out up to <see cref="HomeGuardOut"/> of those at home: the rest stay to defend.
        /// </summary>
        public UnitType[] RaidWith = new UnitType[0];
        public double HomeGuardOut;
        /// <summary>Whether it sends noblemen against other players' villages too, not just barbarians'.</summary>
        public bool ConquersPlayers;
        /// <summary>Whether it builds an academy and noblemen at all.</summary>
        public bool Expands = true;
        /// <summary>How much longer than a regular lord it takes between turns.</summary>
        public double TurnMultiplier = 1;
        /// <summary>
        /// How far its build plan goes (stage × ratio is each building's target), unless the lord has its own limit
        /// (<see cref="Player.StageCap"/>), and the most troops (by population) it keeps per stage of that limit.
        /// </summary>
        public int MaxStage = World.PlanStages, TroopPopulationPerStage = int.MaxValue;
    }

    /// <summary>
    /// The rival lords' thinking. Each lord takes a turn every few game minutes (an <see cref="EventKind.AiThink"/>
    /// event) and plays by exactly the same rules as the player, through the same calls: it queues buildings from a
    /// build plan, trains troops to keep its army in its preferred mix, raids nearby barbarians for resources, and,
    /// once beginner protection is over, scouts and attacks other players it thinks it can beat. It only knows
    /// another player's defenses from what its own scouts and battles have seen; barbarian villages it simply knows.
    /// </summary>
    public partial class World
    {
        /// <summary>How far (in fields) lords go to raid barbarians and to attack players.</summary>
        public const double AiRaidRange = 14, AiAttackRange = 25;
        /// <summary>Game hours a lord leaves a raided barbarian village to refill, after its troops are back.</summary>
        public const double AiRaidRestHours = 3;
        /// <summary>How many of the nearest barbarian villages a lord looks at for raiding each turn.</summary>
        public const int AiRaidLooks = 12;
        /// <summary>A lord doesn't go to war with less attack than this at home.</summary>
        public const int AiMinAttackPower = 1000;

        // Build ratios per building, in BuildingType order (the academy and rally point are handled separately):
        //                     HQ    Wood Clay Iron  Farm  Ware Barr  Stab  Work  Wall Acad Rally Smith Mark Hide
        static readonly AiStyle RaiderStyle = new AiStyle
        {
            BuildRatios = new[] { 0.75, 1.0, 1.0, 1.0, 0.75, 0.8, 0.55, 0.55, 0.15, 0.3, 0, 0, 0.75, 0.4, 0.1 },
            //                     Spear Sword Axe  Archer Scout LCav HCav Ram  Cat  Noble (trained separately) Mounted archer
            UnitWeights = new[] { 0.15, 0.0, 0.3, 0.0, 0.05, 0.35, 0.05, 0.0, 0.0, 0.0, 0.1 },
            MilitaryShare = 0.45, Aggression = 0.1, ConquersPlayers = true,
            RaidWith = new[] { UnitType.LightCavalry, UnitType.MountedArcher, UnitType.HeavyCavalry, UnitType.Axeman },
        };

        static readonly AiStyle DefenderStyle = new AiStyle
        {
            BuildRatios = new[] { 0.6, 1.0, 1.0, 0.9, 0.75, 0.8, 0.6, 0.35, 0.0, 0.9, 0, 0, 0.6, 0.3, 0.25 },
            // Defenders keep light cavalry to farm with and send half their spearmen out too (they carry 25 each),
            // and put a little more into building than the others: they grow by economy rather than by war.
            UnitWeights = new[] { 0.4, 0.24, 0.0, 0.13, 0.05, 0.15, 0.03, 0.0, 0.0, 0.0, 0.0 },
            MilitaryShare = 0.3, Aggression = 0.01,
            RaidWith = new[] { UnitType.LightCavalry, UnitType.Spearman }, HomeGuardOut = 0.5,
        };

        static readonly AiStyle BalancedStyle = new AiStyle
        {
            BuildRatios = new[] { 0.7, 1.0, 1.0, 0.95, 0.75, 0.8, 0.5, 0.4, 0.15, 0.55, 0, 0, 0.7, 0.35, 0.15 },
            UnitWeights = new[] { 0.3, 0.15, 0.25, 0.05, 0.04, 0.12, 0.06, 0.0, 0.0, 0.0, 0.03 },
            MilitaryShare = 0.38, Aggression = 0.06, ConquersPlayers = true,
            RaidWith = new[] { UnitType.LightCavalry, UnitType.MountedArcher, UnitType.HeavyCavalry, UnitType.Spearman, UnitType.Axeman }, HomeGuardOut = 0.25,
        };

        static readonly AiStyle WarlordStyle = new AiStyle
        {
            BuildRatios = new[] { 0.75, 1.0, 1.0, 1.0, 0.75, 0.8, 0.6, 0.45, 0.35, 0.35, 0, 0, 0.75, 0.35, 0.1 },
            UnitWeights = new[] { 0.12, 0.03, 0.4, 0.0, 0.05, 0.2, 0.05, 0.07, 0.03, 0.0, 0.05 },
            MilitaryShare = 0.5, Aggression = 0.2, ConquersPlayers = true,
            RaidWith = new[] { UnitType.LightCavalry, UnitType.MountedArcher, UnitType.HeavyCavalry, UnitType.Axeman },
        };

        /// <summary>A newcomer: builds slowly (a turn every hour or so), trains a handful of defenders, never attacks or expands.</summary>
        static readonly AiStyle NoobStyle = new AiStyle
        {
            // (Noobs build their Headquarters, smithy and market further than they used to, so the villages they leave
            // behind are nearer academy-ready for whoever takes them.)
            BuildRatios = new[] { 0.7, 1.0, 1.0, 0.9, 0.75, 0.8, 0.4, 0.2, 0.0, 0.3, 0, 0, 0.55, 0.3, 0.5 },
            UnitWeights = new[] { 0.6, 0.3, 0.1, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 },
            MilitaryShare = 0.15, Aggression = 0, ConquersPlayers = false,
            Expands = false, TurnMultiplier = 8, MaxStage = 10, TroopPopulationPerStage = 15,
        };

        static AiStyle StyleOf(AiPersonality p) =>
            p == AiPersonality.Raider ? RaiderStyle : p == AiPersonality.Defender ? DefenderStyle : p == AiPersonality.Warlord ? WarlordStyle
            : p == AiPersonality.Noob ? NoobStyle : BalancedStyle;

        /// <summary>A noob who loses this many fights in its villages within this many game hours gives up.</summary>
        public const int NoobQuitHits = 3;
        public const double NoobQuitWindowHours = 48;

        static readonly UnitType[] OffensiveUnits = { UnitType.Axeman, UnitType.LightCavalry, UnitType.MountedArcher, UnitType.HeavyCavalry, UnitType.Ram, UnitType.Catapult };
        static readonly int[] NoTroops = new int[Units.Count];

        // ---------------------------------------------------------------- skill

        double SkillThinkMinutes => Settings.RivalSkill == AiSkill.Easy ? 30 : Settings.RivalSkill == AiSkill.Hard ? 8 : 15;
        double SkillMilitary => Settings.RivalSkill == AiSkill.Easy ? 0.75 : Settings.RivalSkill == AiSkill.Hard ? 1.15 : 1;
        double SkillAggression => Settings.RivalSkill == AiSkill.Easy ? 0.5 : Settings.RivalSkill == AiSkill.Hard ? 1.3 : 1;
        /// <summary>How much stronger than the expected defense a lord wants its attack to be (at the worst luck).</summary>
        double SkillAttackMargin => Settings.RivalSkill == AiSkill.Easy ? 1.6 : Settings.RivalSkill == AiSkill.Hard ? 1.15 : 1.3;
        int SkillMaxRaids => Settings.RivalSkill == AiSkill.Easy ? 1 : Settings.RivalSkill == AiSkill.Hard ? 5 : 3;
        /// <summary>The chance a warlike lord's real ram or noble attack on a player comes with fakes (none on Easy).</summary>
        double SkillFakeChance => FakeRate * (Settings.RivalSkill == AiSkill.Easy ? 0 : Settings.RivalSkill == AiSkill.Hard ? 0.6 : 0.35);

        /// <summary>
        /// How much lords fake (a scale on the skill's rate; 0: never). Half: at the full rate, fakes cost real
        /// attacks their rams and pulled support about enough to stall one world in five (simulated).
        /// </summary>
        public static double FakeRate = 0.5;
        /// <summary>Game days a warlike lord waits between fakes sent on their own.</summary>
        double SkillFakeGapDays => Settings.RivalSkill == AiSkill.Hard ? 1.5 : 3;

        // ---------------------------------------------------------------- turns

        void ScheduleAiThink(Player lord, double delay) => Schedule(delay, EventKind.AiThink, -1, lord.Id);

        /// <summary>A repeatable random number for a lord's turn, from the world seed, the lord, its turn and a salt.</summary>
        double AiRandom(Player lord, int salt) => Terrain.Hash(Settings.Seed ^ 0x3C6EF372, lord.Id * 7919 + salt, lord.ThinkCount);

        /// <summary>
        /// Game seconds until the lord's next turn. At high world speeds turns are spaced by real time instead
        /// (about every 15 real seconds), which keeps catching up on a long absence quick.
        /// </summary>
        /// <remarks>
        /// Turns also lengthen as the lord's villages grow (twice as long at Headquarters 8, 6 times at 20, up to 8
        /// times): its buildings then take hours, so there's less to decide, and a world full of big lords stays
        /// quick to run.
        /// </remarks>
        double AiThinkSeconds(Player lord, List<Village> own)
        {
            int headquarters = 1;
            bool done = true;
            foreach (var v in own)
            {
                headquarters = Math.Max(headquarters, v.Level(BuildingType.Headquarters));
                done &= PlanDone(lord, v);
            }
            double pace = Math.Min(8, 1 + Math.Max(0, headquarters - 5) / 3.0);
            // A lord (in practice a noob) that has built all it ever will has little left to decide.
            if (done) pace *= 4;
            return Math.Max(SkillThinkMinutes * 60, 15 * Settings.Speed) * pace * StyleOf(lord.Personality).TurnMultiplier * (0.8 + 0.4 * AiRandom(lord, 3));
        }

        void AiThink(ScheduledEvent e)
        {
            var lord = FindPlayer(e.A);
            if (lord == null || lord.IsHuman || lord.Quit) return;
            lord.ThinkCount++;
            var own = new List<Village>(VillagesOf(lord.Id)); // a copy: conquests may change it
            if (own.Count == 0) return; // no villages left: the lord is out of the game

            var style = StyleOf(lord.Personality);
            if (Diplomacy) AiRecallSupport(lord);
            foreach (var v in own)
            {
                Touch(v);
                if (Diplomacy) AiSupportTribe(lord, v, style);
                AiSpend(lord, v, style);
                AiConquer(lord, v, style); // a nobleman train first, then the main army; raiders go with what's left
                AiAttack(lord, v, style);
                AiRaid(lord, v, style);
                AiShipToAcademy(lord, v, style);
                AiTrade(lord, v);
            }
            ScheduleAiThink(lord, AiThinkSeconds(lord, own));
        }

        AiNote NoteFor(Player lord, int villageId, bool create)
        {
            // Looked up many times a turn, so each lord's notes are indexed by village (not saved; rebuilt as needed).
            if (lord.NotesByVillage == null || lord.NotesByVillage.Count != lord.Notes.Count)
            {
                lord.NotesByVillage = new Dictionary<int, AiNote>();
                foreach (var n in lord.Notes) lord.NotesByVillage[n.VillageId] = n;
            }
            if (lord.NotesByVillage.TryGetValue(villageId, out var note) || !create) return note;
            note = new AiNote { VillageId = villageId };
            lord.Notes.Add(note);
            lord.NotesByVillage[villageId] = note;
            return note;
        }

        // ---------------------------------------------------------------- economy

        /// <summary>
        /// Spends the village's resources on buildings and troops, keeping troops to the personality's share of all
        /// spending over time (whichever side is behind gets first call on the stores).
        /// </summary>
        void AiSpend(Player lord, Village v, AiStyle style)
        {
            double share = Math.Min(0.8, style.MilitaryShare * SkillMilitary);
            bool canTrain = v.Level(BuildingType.Barracks) > 0;
            bool troopsDue = canTrain && lord.SpentOnTroops < share * (lord.SpentOnBuildings + lord.SpentOnTroops);

            // With an academy, noblemen come first: the lord saves up for them rather than spending on anything
            // else, unless its stores are about to overflow.
            // On a gold-coin world, a nobleman needs a free slot first: coins are minted (and saved up for) the same
            // way, and they come before the army's share: expansion runs on them.
            if ((troopsDue || Settings.GoldCoins) && WantsNobles(v))
            {
                if (FreeNobleSlots(lord) <= 0)
                {
                    var coin = MintCoins(v, Math.Max(1, CheckMint(v, 1).MaxAffordable));
                    if (coin.Status == MintStatus.Ok) lord.SpentOnTroops += coin.Total.Wood + coin.Total.Clay + coin.Total.Iron;
                    else if (coin.Status == MintStatus.NotEnoughResources && !double.IsInfinity(coin.AffordableIn) && FullestStock(v) < 0.9) return;
                }
                else
                {
                    var noble = Recruit(v, UnitType.Nobleman, 1);
                    if (noble.Status == RecruitStatus.Ok) lord.SpentOnTroops += noble.Total.Wood + noble.Total.Clay + noble.Total.Iron;
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
                    if (coin.Status == MintStatus.Ok) lord.SpentOnTroops += coin.Total.Wood + coin.Total.Clay + coin.Total.Iron;
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

        // ---------------------------------------------------------------- troops

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

        /// <summary>All of a village's own troops: at home, in training, and out on attacks or heading home.</summary>
        int[] ArmyOf(Village v)
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
            return army;
        }

        // ---------------------------------------------------------------- farming

        /// <summary>
        /// Game hours a sighting of a village's defenders stays good enough to raid on without a scout along to
        /// check again: a player's (who may have trained more since), and a barbarian village's (which can't).
        /// </summary>
        public const double AiFreshHoursPlayer = 12, AiFreshHoursBarbarian = 48;
        /// <summary>The least plunder a lord expects before a raid is worth sending.</summary>
        public const int AiMinHaul = 100;
        /// <summary>At most this many villages scouted per turn by a lord looking for new farms.</summary>
        public const int AiScoutRunsPerTurn = 2;

        /// <summary>
        /// Villages a lord farms rather than makes war on: barbarians, and players who are easy prey (noobs and
        /// inactive players), once they're out of beginner protection.
        /// </summary>
        bool IsFarm(Player lord, Village t)
        {
            if (t.IsBarbarian) return true;
            if (t.OwnerId == lord.Id || IsProtected(t.OwnerId) || AreFriendly(lord.Id, t.OwnerId)) return false;
            var owner = FindPlayer(t.OwnerId);
            return owner != null && (owner.Personality == AiPersonality.Noob || owner.Personality == AiPersonality.Inactive);
        }

        /// <summary>
        /// Farming, as a Tribal Wars player does it, by every personality but the noobs: raids on the nearest
        /// barbarian, noob and inactive villages, each sized to carry off what the lord expects to find there and
        /// to beat the defenders it expects. It only knows what its scouts and earlier raids have shown it: a
        /// village it knows nothing about is scouted first, and a scout rides along whenever what it knows is
        /// getting old. With no scouts (or before it has researched them) it farms barbarians blind, and learns
        /// the hard way which ones have troops.
        /// </summary>
        void AiRaid(Player lord, Village v, AiStyle style)
        {
            if (style.RaidWith.Length == 0) return;
            var available = new int[Units.Count];
            foreach (var type in style.RaidWith)
            {
                int home = v.TroopCount(type);
                bool guard = type == UnitType.Spearman || type == UnitType.Swordsman;
                // Of the home guard, only its share of all of them may be out at once, counting those already out.
                available[(int)type] = guard ? Math.Max(0, Math.Min(home, (int)((home + Away(v, type)) * style.HomeGuardOut) - Away(v, type))) : home;
            }
            int scouts = v.TroopCount(UnitType.Scout);
            if (Battle.CarryCapacity(available) < AiMinHaul && scouts == 0) return;

            var targets = Emptied(ref raidTargets);
            foreach (var t in VillagesNear(v.X, v.Y, AiRaidRange, Emptied(ref nearby)))
                if (IsFarm(lord, t)) targets.Add((t, Distance(v, t)));
            targets.Sort((a, b) => a.distance.CompareTo(b.distance));
            var underAttack = AttackTargets(lord);

            int raids = 0, scoutRuns = 0, looked = 0;
            foreach (var (target, _) in targets)
            {
                if (raids >= SkillMaxRaids) break;
                var note = NoteFor(lord, target.Id, false);
                if (note != null && (note.NextRaidAt > Now || note.AvoidUntil > Now)) continue;
                if (underAttack.Contains(target.Id)) continue;
                // The nearest dozen worth a look are plenty to choose from (and a crowded map has many more).
                if (looked++ >= AiRaidLooks) break;

                bool seen = note != null && note.SeenAt >= 0 && note.SeenTroops != null;
                double hoursOld = seen ? (Now - note.SeenAt) / 3600 : double.PositiveInfinity;
                bool fresh = hoursOld <= (target.IsBarbarian ? AiFreshHoursBarbarian : AiFreshHoursPlayer);

                // Never seen: scouts go first if there are any; players are never raided blind.
                if (!seen && scouts > 0)
                {
                    if (scoutRuns >= AiScoutRunsPerTurn) continue;
                    var look = new int[Units.Count];
                    look[(int)UnitType.Scout] = target.IsBarbarian ? 1 : 2;
                    if (look[(int)UnitType.Scout] > scouts) continue;
                    var run = Send(v, target, look, CommandKind.Attack);
                    if (run == null) continue;
                    scouts -= look[(int)UnitType.Scout];
                    scoutRuns++;
                    // Wait for the report before deciding.
                    NoteFor(lord, target.Id, true).NextRaidAt = Now + 2 * (run.ArriveTime - Now);
                    continue;
                }
                if (!seen && !target.IsBarbarian) continue;
                if (Battle.CarryCapacity(available) < AiMinHaul) continue;

                double haul = ExpectedHaul(note, target);
                if (haul < AiMinHaul) continue;
                var defenders = seen ? ExpectedDefenders(note, target) : NoTroops;
                int wall = seen ? note.SeenWall : GuessWall(target) / 2;
                var party = RaidParty(style, available, haul, defenders, wall);
                if (party == null) continue;
                // A scout rides along to bring back fresh news whenever what the lord knows is getting old.
                if (!fresh && scouts > 0)
                {
                    party[(int)UnitType.Scout] = 1;
                    scouts--;
                }
                var command = Send(v, target, party, CommandKind.Attack);
                if (command == null) continue;
                for (int i = 0; i < Units.Count; i++) if (i != (int)UnitType.Scout) available[i] -= party[i];
                NoteFor(lord, target.Id, true).NextRaidAt = Now + 2 * (command.ArriveTime - Now) + AiRaidRestHours * 3600;
                raids++;
            }
        }

        // ---------------------------------------------------------------- feeding the academy

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

        // ---------------------------------------------------------------- helping the tribe

        /// <summary>How far (in fields) lords send support to tribe mates under attack, and the most helpers per attack.</summary>
        public const double AiSupportRange = 20;
        public const int AiMaxSupporters = 3;
        static readonly UnitType[] DefensiveUnits = { UnitType.Spearman, UnitType.Swordsman, UnitType.Archer, UnitType.HeavyCavalry };

        /// <summary>
        /// A tribe mate's village is under attack: a lord near enough to get there first sends part of its defenders
        /// (defenders send more, warlords less), and calls them home once the danger has passed.
        /// </summary>
        void AiSupportTribe(Player lord, Village v, AiStyle style)
        {
            var tribe = TribeOf(lord);
            if (tribe == null || tribe.HelpCalls.Count == 0) return;
            double share = lord.Personality == AiPersonality.Defender ? 0.5 : lord.Personality == AiPersonality.Warlord ? 0.15 : lord.Personality == AiPersonality.Noob ? 0.2 : 0.3;
            foreach (var call in tribe.HelpCalls)
            {
                if (call.OwnerId == lord.Id || call.Supporters >= AiMaxSupporters || call.ArriveTime <= Now) continue;
                var host = FindVillage(call.VillageId);
                if (host == null || host.OwnerId != call.OwnerId || Distance(v, host) > AiSupportRange) continue;
                var party = new int[Units.Count];
                int total = 0;
                foreach (var type in DefensiveUnits)
                {
                    party[(int)type] = (int)(v.TroopCount(type) * share);
                    total += party[(int)type];
                }
                if (total < 20) return; // too little to matter (and to spare)
                var slowest = SlowestUnit(party);
                if (slowest == null || Now + TravelSeconds(v, host, slowest.Value) > call.ArriveTime) continue; // too late to help
                if (Send(v, host, party, CommandKind.Support) == null) continue;
                lord.SupportPlacements.Add(new SupportPlacement { HostId = host.Id, FromId = v.Id, Until = call.ArriveTime + 3600 });
                return; // one at a time
            }
        }

        /// <summary>Calls home support sent to tribe mates once the attacks it was sent against are over.</summary>
        void AiRecallSupport(Player lord)
        {
            for (int i = lord.SupportPlacements.Count - 1; i >= 0; i--)
            {
                var p = lord.SupportPlacements[i];
                if (p.Until > Now) continue;
                lord.SupportPlacements.RemoveAt(i);
                var host = FindVillage(p.HostId);
                if (host != null) Recall(host, p.FromId);
            }
        }

        /// <summary>A village's own troops of a kind out on attacks or on their way home.</summary>
        int Away(Village v, UnitType type)
        {
            int n = 0;
            foreach (var c in CommandsOf(v.OwnerId))
                if ((c.Kind == CommandKind.Attack && c.FromVillageId == v.Id) || (c.Kind == CommandKind.Return && c.ToVillageId == v.Id))
                    n += c.Troops[(int)type];
            return n;
        }

        /// <summary>
        /// The defenders a lord expects at a village, from what it last saw there: a player's may have trained more
        /// since (the older the sighting, the more), a barbarian village's can't. Either way it allows its safety
        /// margin.
        /// </summary>
        int[] ExpectedDefenders(AiNote note, Village t)
        {
            double days = Math.Max(0, Now - note.SeenAt) / SecondsPerDay;
            bool trains = !t.IsBarbarian && FindPlayer(t.OwnerId)?.Personality != AiPersonality.Inactive;
            double factor = (trains ? 1 + 0.3 * days : 1) * SkillAttackMargin;
            var expected = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < note.SeenTroops.Length; i++) expected[i] = (int)Math.Ceiling(note.SeenTroops[i] * factor);
            return expected;
        }

        /// <summary>
        /// How much a lord expects to carry off from a village: what was there to take when it last knew (from its
        /// scouts, or an earlier raid), plus what the village's mines have made since, up to what its warehouse
        /// holds, less what its hiding place keeps. The mines and warehouse are the ones its scouts saw, or a guess
        /// from the village's size. A village it knows nothing about is guessed at half a day's production.
        /// </summary>
        double ExpectedHaul(AiNote note, Village t)
        {
            var levels = note?.SeenLevels;
            int guess = Math.Min(25, 1 + t.Points / 60);
            int Level(BuildingType type, int fallback) => levels != null && (int)type < levels.Length ? levels[(int)type] : fallback;
            double cap = Buildings.StorageCapacity(Level(BuildingType.Warehouse, Math.Min(20, guess))) - Buildings.HiddenCapacity(Level(BuildingType.HidingPlace, 0));
            if (cap <= 0) return 0;
            bool known = note != null && note.LootSeenAt >= 0;
            double hours = known ? Math.Max(0, Now - note.LootSeenAt) / 3600 : 12;
            double haul = 0;
            foreach (ResourceType r in ResourceTypes)
            {
                double made = Buildings.ProductionPerHour(Level(Village.MineFor(r), guess)) * hours;
                double had = !known ? 0 : r == ResourceType.Wood ? note.LootWood : r == ResourceType.Clay ? note.LootClay : note.LootIron;
                haul += Math.Min(cap, had + made);
            }
            return haul;
        }

        /// <summary>
        /// Enough raiders (in the personality's order) to carry off <paramref name="haul"/> and beat the expected
        /// defenders and wall even with the worst luck, or null if the troops available can't do it.
        /// </summary>
        static int[] RaidParty(AiStyle style, int[] available, double haul, int[] defenders, int wall)
        {
            var party = new int[Units.Count];
            int wanted = (int)Math.Min(haul, 20000), carry = 0;
            foreach (var type in style.RaidWith)
            {
                int each = Units.Get(type).Carry;
                int take = Math.Min(available[(int)type], (int)Math.Ceiling(Math.Max(0, wanted - carry) / (double)each));
                party[(int)type] = take;
                carry += take * each;
            }
            if (carry < Math.Min(AiMinHaul, wanted)) return null;
            if (Battle.Fight(party, defenders, wall, -Battle.MaxLuck).AttackerWon) return party;
            // Too few to win: all the raiders on hand, if that does it (they'll just come back with room to spare).
            foreach (var type in style.RaidWith) party[(int)type] = available[(int)type];
            return Battle.Fight(party, defenders, wall, -Battle.MaxLuck).AttackerWon ? party : null;
        }
        // ---------------------------------------------------------------- war on other players

        /// <summary>
        /// Now and then, looks for another player's village in reach that it can beat (as far as it knows), scouting
        /// it first if it can, then sends its whole offensive army, with catapults aimed at the wall or barracks.
        /// </summary>
        void AiAttack(Player lord, Village v, AiStyle style)
        {
            // A tribe's named target draws its members in, whatever their mood; otherwise war is a matter of temperament.
            AiLoneFake(lord, v);
            var tribe = Diplomacy ? TribeOf(lord) : null;
            var target = tribe != null && tribe.TargetUntil > Now ? FindVillage(tribe.TargetVillageId) : null;
            bool tribeOp = target != null && Distance(v, target) <= AiAttackRange && AiRandom(lord, 13) < 0.5;
            if (!tribeOp && AiRandom(lord, 11) >= style.Aggression * SkillAggression) return;

            var offense = new int[Units.Count];
            int power = 0;
            foreach (var type in OffensiveUnits)
            {
                offense[(int)type] = v.TroopCount(type);
                power += offense[(int)type] * Units.Get(type).Attack;
            }
            if (power < AiMinAttackPower) return;

            // The most tempting target it can beat, as far as it knows: close, bigger (more to plunder), and all the
            // more if they attacked us. Villages it knows nothing about are scouted first when it has scouts;
            // otherwise it judges them by a cautious guess from their size.
            // Candidates are ranked by how tempting they are first, and only then checked (a battle worked out in
            // advance each) from the most tempting down, stopping at the first it can beat: the same choice as
            // checking them all, for a fraction of the work in a crowded neighborhood.
            bool hasScouts = v.TroopCount(UnitType.Scout) > 0;
            var underAttack = AttackTargets(lord);
            var attackCandidates = Emptied(ref this.attackCandidates);
            foreach (var t in VillagesNear(v.X, v.Y, AiAttackRange, Emptied(ref nearby)))
            {
                // War is for players who fight back; barbarians, noobs and inactive players are farmed instead.
                if (t.IsBarbarian || t.OwnerId == lord.Id || IsProtected(t.OwnerId) || IsFarm(lord, t) || AreFriendly(lord.Id, t.OwnerId)) continue;
                var note = NoteFor(lord, t.Id, false);
                if (note != null && (note.AvoidUntil > Now || note.NextRaidAt > Now)) continue;
                if (underAttack.Contains(t.Id)) continue;
                bool scoutFirst = !Known(note) && hasScouts;
                // Near and big enough to be worth it, but the biggest aren't singled out: size counts for less and less.
                double score = (20 * Math.Sqrt(t.Points) + 100) / (2 + Distance(v, t));
                if (scoutFirst) score *= 0.5; // a sure thing beats a maybe
                if (t.OwnerId == lord.LastAttackerId) score *= 2;
                if (AtWar(lord.Id, t.OwnerId)) score *= 2;     // the tribe's enemies first
                if (t == target) score *= 4;                   // and the tribe's target above all
                attackCandidates.Add((t, score));
            }
            attackCandidates.Sort((a, b) => b.score.CompareTo(a.score));

            Village best = null;
            bool bestNeedsScouting = false;
            foreach (var (t, _) in attackCandidates)
            {
                // What the lord or its tribe mates have seen there.
                var note = Diplomacy ? SharedSighting(lord, t.Id).note : NoteFor(lord, t.Id, false);
                bool known = Known(note);
                if (known && !Beatable(offense, note.SeenTroops, note.SeenAt, note.SeenWall)) continue;
                if (!known && !hasScouts && !Beatable(offense, GuessDefenders(t), Now, GuessWall(t))) continue;
                best = t;
                bestNeedsScouting = !known && hasScouts;
                break;
            }
            if (best == null) return;

            if (bestNeedsScouting)
            {
                // Look before leaping: scouts now, the army on a later turn if the coast is clear.
                var scouts = new int[Units.Count];
                scouts[(int)UnitType.Scout] = Math.Min(3, v.TroopCount(UnitType.Scout));
                Send(v, best, scouts, CommandKind.Attack);
                return;
            }

            var seen = Diplomacy ? SharedSighting(lord, best.Id).note : NoteFor(lord, best.Id, false);
            int wall = Known(seen) ? seen.SeenWall : GuessWall(best);
            var aim = wall > 0 && offense[(int)UnitType.Ram] == 0 ? BuildingType.Wall : BuildingType.Barracks;
            var command = Send(v, best, offense, CommandKind.Attack, aim);
            // Give the village time to recover before the next attack, so the lord doesn't hammer it non-stop.
            if (command != null) NoteFor(lord, best.Id, true).NextRaidAt = Now + 2 * (command.ArriveTime - Now) + 6 * 3600;
            // A ram attack is worth hiding among fakes.
            if (command != null && AttackSpeeds.IsDangerous(AttackSpeeds.Of(command.Troops))) MaybeFakes(lord, best);
        }

        // ---------------------------------------------------------------- fakes

        /// <summary>
        /// Warlike lords hide a real ram or noble attack on a player among fakes, as Tribal Wars players did: 1 to 3
        /// single rams (or catapults), from any of their villages that has one, at other villages of the same player
        /// or their tribe nearby. Defenders see only the speed, so each looks as dangerous as the real one.
        /// </summary>
        void MaybeFakes(Player lord, Village real)
        {
            if (!Warlike(lord) || real.IsBarbarian || AiRandom(lord, 17) >= SkillFakeChance) return;
            int count = 1 + (int)(AiRandom(lord, 18) * 3);
            var victims = new HashSet<int> { real.OwnerId };
            var theirTribe = Diplomacy ? TribeOf(real.OwnerId) : null;
            if (theirTribe != null) victims.UnionWith(theirTribe.Members);
            foreach (var t in VillagesNear(real.X, real.Y, 15))
            {
                if (count == 0) break;
                if (t == real || t.IsBarbarian || !victims.Contains(t.OwnerId) || IsProtected(t.OwnerId) || AreFriendly(lord.Id, t.OwnerId)) continue;
                if (SendFake(lord, t)) count--;
            }
        }

        /// <summary>
        /// Now and then, a warlike lord sends a fake on its own at an enemy (on a world with tribes: one its tribe is
        /// at war with, or the tribe's target; without tribes: a player it's already attacking).
        /// </summary>
        void AiLoneFake(Player lord, Village v)
        {
            if (!Warlike(lord) || SkillFakeChance <= 0 || (lord.LastFakeAt >= 0 && Now - lord.LastFakeAt < SkillFakeGapDays * SecondsPerDay)) return;
            if (AiRandom(lord, 19) >= 0.1 * FakeRate) return;
            var attacking = new HashSet<int>();
            if (!Diplomacy)
                foreach (var c in CommandsOf(lord.Id))
                    if (c.Kind == CommandKind.Attack && FindVillage(c.ToVillageId) is Village to && !to.IsBarbarian) attacking.Add(to.OwnerId);
            foreach (var t in VillagesNear(v.X, v.Y, AiAttackRange))
            {
                if (t.IsBarbarian || t.OwnerId == lord.Id || IsProtected(t.OwnerId) || AreFriendly(lord.Id, t.OwnerId)) continue;
                bool enemy = Diplomacy ? AtWar(lord.Id, t.OwnerId) || IsTribeTarget(lord, t) : attacking.Contains(t.OwnerId);
                if (!enemy || !SendFake(lord, t)) continue;
                lord.LastFakeAt = Now;
                return;
            }
        }

        /// <summary>Sends one ram (or catapult) at a village from the nearest of the lord's villages that has one in reach.</summary>
        bool SendFake(Player lord, Village target)
        {
            Village from = null;
            double nearest = AiAttackRange;
            foreach (var u in VillagesOf(lord.Id))
            {
                if (u.TroopCount(UnitType.Ram) + u.TroopCount(UnitType.Catapult) == 0) continue;
                double d = Distance(u, target);
                if (d <= nearest)
                {
                    nearest = d;
                    from = u;
                }
            }
            if (from == null) return false;
            var fake = new int[Units.Count];
            fake[from.TroopCount(UnitType.Ram) > 0 ? (int)UnitType.Ram : (int)UnitType.Catapult] = 1;
            return Send(from, target, fake, CommandKind.Attack) != null;
        }

        static int GuessWall(Village v) => Math.Min(20, v.Points / 60);

        // Scratch lists reused from turn to turn (not saved; made when first needed), so busy worlds don't churn memory.
        [NonSerialized] List<(Village village, double score)> attackCandidates;
        [NonSerialized] List<(Village village, double distance)> raidTargets;
        [NonSerialized] List<Village> nearby;

        static List<T> Emptied<T>(ref List<T> list)
        {
            if (list == null) list = new List<T>();
            list.Clear();
            return list;
        }

        // ---------------------------------------------------------------- conquest

        /// <summary>How far (in fields) lords send noblemen, and how many they gather before sending them.</summary>
        public const double AiConquestRange = 15;
        public const int AiNoblesWanted = 4, AiNoblesWantedWithCoins = 2;

        /// <summary>
        /// How many noblemen a lord gathers before setting out: four at a flat price; on a gold-coin world, where
        /// every slot costs more, two (survivors come home, and it goes back until the village is won).
        /// </summary>
        int NoblesWanted => Settings.GoldCoins ? AiNoblesWantedWithCoins : AiNoblesWanted;
        /// <summary>The dearest resource in a nobleman's price: a warehouse must hold this much to save up for one.</summary>
        int NobleCostMax
        {
            get
            {
                var c = UnitCost(UnitType.Nobleman);
                return Math.Max(c.Wood, Math.Max(c.Clay, c.Iron));
            }
        }

        /// <summary>Whether a village has an academy and fewer noblemen (at home, training or marching) than a lord gathers.</summary>
        bool WantsNobles(Village v) =>
            v.Level(BuildingType.Academy) > 0 && ArmyOf(v)[(int)UnitType.Nobleman] < NoblesWanted
            && StyleOf(FindPlayer(v.OwnerId)?.Personality ?? AiPersonality.None).Expands;

        /// <summary>
        /// With a full set of noblemen at home, sends them as a noble train with the village's offensive troops to
        /// win over a village nearby: a barbarian one, or (for most personalities) a player's it knows or guesses it
        /// can beat. Sticks with its target until it's taken or an attack on it fails. A village a satellite of its
        /// faction has handed over comes first, then the tribe's target: for those, any noblemen at home go at once.
        /// </summary>
        void AiConquer(Player lord, Village v, AiStyle style)
        {
            int nobles = v.TroopCount(UnitType.Nobleman);
            if (!style.Expands || nobles == 0) return;
            var handed = HandedOverInReach(lord, v);
            var tribeTarget = handed ?? TribeTargetInReach(lord, v, style);
            if (nobles < NoblesWanted && tribeTarget == null) return;
            var underAttack = AttackTargets(lord);

            var target = tribeTarget ?? FindVillage(lord.ConquestTargetId);
            if (tribeTarget == null && (target == null || !ConquestTargetOk(lord, v, target, style)))
            {
                target = PickConquestTarget(lord, v, style, underAttack);
                lord.ConquestTargetId = target?.Id ?? -1;
            }
            if (target == null || underAttack.Contains(target.Id)) return;

            var army = new int[Units.Count];
            foreach (var type in OffensiveUnits) army[(int)type] = v.TroopCount(type);
            army[(int)UnitType.Nobleman] = nobles;
            // (What tribe mates have seen counts too: their attacks clear the way.)
            var note = Diplomacy ? SharedSighting(lord, target.Id).note : NoteFor(lord, target.Id, false);
            bool known = Known(note);
            if (target == handed)
            {
                // Its defenders stand aside: only the villagers and the wall to get past, so each nobleman takes just
                // the escort that gets him through, and the army stays home.
                var sizing = (int[])army.Clone();
                sizing[(int)UnitType.Nobleman] = Math.Max(2, nobles);
                var escort = SplitTrain(sizing, TrainEscort.Minimal, target.Level(BuildingType.Wall))[1];
                var party = new int[Units.Count];
                for (int i = 0; i < Units.Count; i++) party[i] = Math.Min(army[i], escort[i] * nobles);
                party[(int)UnitType.Nobleman] = nobles;
                SendTrain(v, target, party, TrainEscort.Minimal, BuildingType.Wall);
                return;
            }
            // The noblemen only go where the lord has seen lately (its farming scouts usually have); with no scouts
            // at all, it trusts that a barbarian village is empty and guesses at a player's.
            if (!known && v.TroopCount(UnitType.Scout) > 0 && !underAttack.Contains(target.Id))
            {
                var look = new int[Units.Count];
                look[(int)UnitType.Scout] = 2;
                Send(v, target, look, CommandKind.Attack);
                return;
            }
            var expected = known ? note.SeenTroops : target.IsBarbarian ? NoTroops : GuessDefenders(target);
            int wall = known ? note.SeenWall : GuessWall(target);
            // The noblemen have to live through it, so the lord wants an easy win (less so on the tribe's target).
            if (!Beatable(army, expected, known ? note.SeenAt : Now, wall, maxLoss: target == tribeTarget ? 0.5 : 0.35)) return;
            var train = SendTrain(v, target, army, TrainEscort.Minimal, BuildingType.Wall);
            // A noble train at a player is worth hiding among fakes.
            if (train != null && !target.IsBarbarian) MaybeFakes(lord, target);
        }

        /// <summary>A village handed over to this lord, near enough for this village's noblemen and not already under its attack.</summary>
        Village HandedOverInReach(Player lord, Village v)
        {
            if (!FactionsFormed) return null;
            foreach (var t in VillagesNear(v.X, v.Y, FeedRange))
                if (FedTo(t, lord.Id, Now) && t.OwnerId != lord.Id) return t;
            return null;
        }

        /// <summary>The tribe's target, if it's one this village's noblemen can go for.</summary>
        Village TribeTargetInReach(Player lord, Village v, AiStyle style)
        {
            var tribe = Diplomacy ? TribeOf(lord) : null;
            if (tribe == null || tribe.TargetUntil <= Now) return null;
            var t = FindVillage(tribe.TargetVillageId);
            return t != null && ConquestTargetOk(lord, v, t, style) ? t : null;
        }

        bool ConquestTargetOk(Player lord, Village from, Village t, AiStyle style)
        {
            if (t.OwnerId == lord.Id || Distance(from, t) > ConquestRangeOf(lord)) return false;
            // (Once the barbarian land is nearly gone, every lord who expands turns on other players, as does anyone
            // whose tribe has named the village its target.)
            if (!t.IsBarbarian && ((!style.ConquersPlayers && !LateGame && !IsTribeTarget(lord, t)) || IsProtected(t.OwnerId) || AreFriendly(lord.Id, t.OwnerId))) return false;
            var note = NoteFor(lord, t.Id, false);
            return note == null || note.AvoidUntil <= Now;
        }

        /// <summary>Whether a village is the target the lord's tribe has named (and still means).</summary>
        bool IsTribeTarget(Player lord, Village t)
        {
            var tribe = Diplomacy ? TribeOf(lord) : null;
            return tribe != null && tribe.TargetVillageId == t.Id && tribe.TargetUntil > Now;
        }

        /// <summary>The late game: barbarian villages are under a tenth of the world, so lords turn on each other.</summary>
        bool LateGame => VillagesOf(-1).Count < 0.1 * Villages.Count && WorldLocked;

        /// <summary>
        /// How far a lord reaches for villages to win over: a consolidator (spread 0) no further than 10 fields, a
        /// spreader (spread 1) up to 22.
        /// </summary>
        static double ConquestRangeOf(Player lord) => 10 + 12 * Math.Max(0, Math.Min(1, lord.Spread));

        /// <summary>
        /// The most tempting village to win over: well built and close (barbarians' first: they're easier). How much
        /// "close" matters is the lord's nature: a consolidator wants what's next door, a spreader weighs a rich
        /// village further off almost as highly.
        /// </summary>
        Village PickConquestTarget(Player lord, Village v, AiStyle style, HashSet<int> underAttack)
        {
            Village best = null;
            double bestScore = 0;
            double nearness = 0.6 + 1.2 * (1 - Math.Max(0, Math.Min(1, lord.Spread)));
            bool late = LateGame;
            int own = PointsOf(lord);
            foreach (var t in VillagesNear(v.X, v.Y, ConquestRangeOf(lord)))
            {
                if (underAttack.Contains(t.Id) || !ConquestTargetOk(lord, v, t, style)) continue;
                double score = (t.Points + 50) / Math.Pow(1 + Distance(v, t), nearness);
                if (!t.IsBarbarian)
                {
                    // Players' villages are harder, until there's little else left; the weak, and enemies, first.
                    score *= late ? 1.2 : 0.6;
                    int theirs = PointsOf(FindPlayer(t.OwnerId));
                    score *= 1 + 0.3 * Math.Min(3, own / (double)Math.Max(1, theirs));
                    if (AtWar(lord.Id, t.OwnerId)) score *= 1.5;
                    // The tribe's target: its members' attacks are wearing it down, and the noblemen follow.
                    if (IsTribeTarget(lord, t)) score *= 3;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>The villages a lord's attacks (or scouts) are already marching on.</summary>
        HashSet<int> AttackTargets(Player lord)
        {
            var targets = new HashSet<int>();
            foreach (var c in CommandsOf(lord.Id))
                if (c.Kind == CommandKind.Attack) targets.Add(c.ToVillageId);
            return targets;
        }

        /// <summary>Whether the lord's sighting of a village's defenders is recent enough to act on.</summary>
        bool Known(AiNote note) => note != null && note.SeenAt >= 0 && note.SeenTroops != null && Now - note.SeenAt < 2 * SecondsPerDay;

        /// <summary>
        /// Whether an attack beats the defenders seen at <paramref name="seenAt"/> (assumed to have grown a little since),
        /// by the skill's safety margin, at the worst luck, without losing most of the army.
        /// </summary>
        bool Beatable(int[] offense, int[] seenTroops, double seenAt, int wall, double maxLoss = 0.7)
        {
            double growth = (1 + 0.3 * Math.Max(0, Now - seenAt) / SecondsPerDay) * SkillAttackMargin;
            var expected = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < seenTroops.Length; i++) expected[i] = (int)Math.Ceiling(seenTroops[i] * growth);
            var result = Battle.Fight(offense, expected, wall, -Battle.MaxLuck);
            return result.AttackerWon && result.AttackerLossFraction < maxLoss;
        }

        /// <summary>A cautious guess at a village's defenders from its points, for lords with no scouts.</summary>
        static int[] GuessDefenders(Village v)
        {
            var guess = new int[Units.Count];
            guess[(int)UnitType.Spearman] = (int)(v.Points * 0.5);
            guess[(int)UnitType.Swordsman] = (int)(v.Points * 0.25);
            return guess;
        }

        /// <summary>
        /// After a battle: an attacking lord remembers what it saw of the defenders (if anyone lived to tell, or its
        /// scouts got through) and stays away for a while if it lost; a defending lord remembers who attacked it.
        /// </summary>
        void AiLearnFromBattle(Command command, Village target, BattleResult result, int[] defenders, int[] defenderLost,
            bool anySurvivors, Cost loot, int lootCapacity, BattleReport report)
        {
            bool fought = false;
            for (int i = 0; i < Units.Count && i < command.Troops.Length; i++)
                if ((UnitType)i != UnitType.Scout && command.Troops[i] > 0) fought = true;

            var attacker = FindPlayer(command.OwnerId);
            if (attacker != null && !attacker.IsHuman)
            {
                var note = NoteFor(attacker, target.Id, true);
                if (anySurvivors || result.Scouted)
                {
                    note.SeenAt = Now;
                    note.SeenTroops = new int[Units.Count];
                    for (int i = 0; i < Units.Count; i++) note.SeenTroops[i] = Math.Max(0, defenders[i] - defenderLost[i]);
                    note.SeenWall = target.Level(BuildingType.Wall);
                }
                if (fought && !result.AttackerWon) note.AvoidUntil = Now + (target.IsBarbarian ? 2 : 1) * SecondsPerDay;
                // Scouts sent alone who didn't get through: the village has scouts of its own. Leave it be a while.
                if (!fought && !result.Scouted) note.AvoidUntil = Now + SecondsPerDay;

                // What there is to plunder: counted by scouts that got through (with the buildings, so it can
                // work out the mines and hiding place), or learned from the haul: a raid that came back with room
                // to spare emptied the place; a full one means about as much again was left.
                int hidden = 0;
                if (result.Scouted && report.ScoutedLevels != null)
                {
                    note.SeenLevels = (int[])report.ScoutedLevels.Clone();
                    hidden = Buildings.HiddenCapacity(note.SeenLevels[(int)BuildingType.HidingPlace]);
                    note.LootSeenAt = Now;
                    note.LootWood = Math.Max(0, report.ScoutedResources.Wood - hidden);
                    note.LootClay = Math.Max(0, report.ScoutedResources.Clay - hidden);
                    note.LootIron = Math.Max(0, report.ScoutedResources.Iron - hidden);
                }
                else if (fought && result.AttackerWon && anySurvivors)
                {
                    bool full = loot.Wood + loot.Clay + loot.Iron >= lootCapacity;
                    note.LootSeenAt = Now;
                    note.LootWood = full ? loot.Wood : 0;
                    note.LootClay = full ? loot.Clay : 0;
                    note.LootIron = full ? loot.Iron : 0;
                }
                // A player's village that was hardly worth the trip is left alone for a day.
                int taken = loot.Wood + loot.Clay + loot.Iron;
                if (fought && result.AttackerWon && !target.IsBarbarian && taken < 0.3 * lootCapacity)
                    note.NextRaidAt = Math.Max(note.NextRaidAt, Now + (command.ArriveTime - command.DepartTime) + SecondsPerDay);
            }

            var defender = target.IsBarbarian ? null : FindPlayer(target.OwnerId);
            if (defender != null && !defender.IsHuman && fought && command.OwnerId != defender.Id)
            {
                defender.LastAttackedAt = Now;
                defender.LastAttackerId = command.OwnerId;
                // A noob beaten too often in too short a time gives up.
                if (defender.Personality == AiPersonality.Noob && result.AttackerWon)
                {
                    defender.HitTimes.Add(Now);
                    defender.HitTimes.RemoveAll(t => Now - t > NoobQuitWindowHours * 3600);
                    if (defender.HitTimes.Count >= NoobQuitHits) QuitLord(defender);
                }
            }
        }
    }
}
