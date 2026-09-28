using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>How one personality plays: what it builds, what it trains, how much goes on troops, how warlike it is.</summary>
    class AiStyle
    {
        /// <summary>Per <see cref="BuildingType"/>: at each stage of growth a building is taken to about ratio Ã— stage.</summary>
        public double[] BuildRatios;
        /// <summary>Per <see cref="UnitType"/>: the share of the army the lord wants of each unit.</summary>
        public double[] UnitWeights;
        /// <summary>Fraction of all spending that goes on troops.</summary>
        public double MilitaryShare;
        /// <summary>Chance, each turn, that the lord looks for another player to attack.</summary>
        public double Aggression;
        /// <summary>Whether it raids barbarians with axemen as well as cavalry.</summary>
        public bool RaidsWithInfantry;
    }

    /// <summary>
    /// The rival lords' thinking. Each lord takes a turn every few game minutes (an <see cref="EventKind.AiThink"/>
    /// event) and plays by exactly the same rules as the player, through the same calls: it queues buildings from a
    /// build plan, trains troops to keep its army in its preferred mix, raids nearby barbarians for resources, and,
    /// once beginner protection is over, scouts and attacks other players it thinks it can beat. It only knows
    /// another player's defences from what its own scouts and battles have seen; barbarian villages it simply knows.
    /// </summary>
    public partial class World
    {
        /// <summary>How far (in fields) lords go to raid barbarians and to attack players.</summary>
        public const double AiRaidRange = 14, AiAttackRange = 25;
        /// <summary>Game hours a lord leaves a raided barbarian village to refill, after its troops are back.</summary>
        public const double AiRaidRestHours = 3;
        /// <summary>A lord doesn't go to war with less attack than this at home.</summary>
        public const int AiMinAttackPower = 1000;

        //                                                     TH    Wood  Clay  Iron  Farm  Ware  Barr  Stab  Work  Wall
        static readonly AiStyle RaiderStyle = new AiStyle
        {
            BuildRatios = new[] { 0.75, 1.0, 1.0, 1.0, 0.75, 0.8, 0.55, 0.55, 0.15, 0.3 },
            //                     Spear Sword Axe  Archer Scout LCav HCav Ram  Catapult
            UnitWeights = new[] { 0.15, 0.0, 0.3, 0.0, 0.05, 0.45, 0.05, 0.0, 0.0 },
            MilitaryShare = 0.45, Aggression = 0.35, RaidsWithInfantry = true,
        };

        static readonly AiStyle DefenderStyle = new AiStyle
        {
            BuildRatios = new[] { 0.6, 1.0, 1.0, 0.9, 0.75, 0.8, 0.6, 0.35, 0.0, 0.9 },
            UnitWeights = new[] { 0.45, 0.3, 0.0, 0.15, 0.04, 0.03, 0.03, 0.0, 0.0 },
            MilitaryShare = 0.35, Aggression = 0.03, RaidsWithInfantry = false,
        };

        static readonly AiStyle BalancedStyle = new AiStyle
        {
            BuildRatios = new[] { 0.7, 1.0, 1.0, 0.95, 0.75, 0.8, 0.5, 0.4, 0.15, 0.55 },
            UnitWeights = new[] { 0.3, 0.15, 0.25, 0.05, 0.04, 0.15, 0.06, 0.0, 0.0 },
            MilitaryShare = 0.38, Aggression = 0.15, RaidsWithInfantry = true,
        };

        static readonly AiStyle WarlordStyle = new AiStyle
        {
            BuildRatios = new[] { 0.75, 1.0, 1.0, 1.0, 0.75, 0.8, 0.6, 0.45, 0.35, 0.35 },
            UnitWeights = new[] { 0.12, 0.03, 0.4, 0.0, 0.05, 0.25, 0.05, 0.07, 0.03 },
            MilitaryShare = 0.5, Aggression = 0.45, RaidsWithInfantry = true,
        };

        static AiStyle StyleOf(AiPersonality p) =>
            p == AiPersonality.Raider ? RaiderStyle : p == AiPersonality.Defender ? DefenderStyle : p == AiPersonality.Warlord ? WarlordStyle : BalancedStyle;

        static readonly UnitType[] RaidUnits = { UnitType.LightCavalry, UnitType.HeavyCavalry, UnitType.Axeman };
        static readonly UnitType[] OffensiveUnits = { UnitType.Axeman, UnitType.LightCavalry, UnitType.HeavyCavalry, UnitType.Ram, UnitType.Catapult };
        static readonly int[] NoTroops = new int[Units.Count];

        // ---------------------------------------------------------------- skill

        double SkillThinkMinutes => Settings.RivalSkill == AiSkill.Easy ? 30 : Settings.RivalSkill == AiSkill.Hard ? 8 : 15;
        double SkillMilitary => Settings.RivalSkill == AiSkill.Easy ? 0.75 : Settings.RivalSkill == AiSkill.Hard ? 1.15 : 1;
        double SkillAggression => Settings.RivalSkill == AiSkill.Easy ? 0.5 : Settings.RivalSkill == AiSkill.Hard ? 1.3 : 1;
        /// <summary>How much stronger than the expected defence a lord wants its attack to be (at the worst luck).</summary>
        double SkillAttackMargin => Settings.RivalSkill == AiSkill.Easy ? 1.6 : Settings.RivalSkill == AiSkill.Hard ? 1.15 : 1.3;
        int SkillMaxRaids => Settings.RivalSkill == AiSkill.Easy ? 1 : Settings.RivalSkill == AiSkill.Hard ? 5 : 3;

        // ---------------------------------------------------------------- turns

        void ScheduleAiThink(Player lord, double delay) => Schedule(delay, EventKind.AiThink, -1, lord.Id);

        /// <summary>A repeatable random number for a lord's turn, from the world seed, the lord, its turn and a salt.</summary>
        double AiRandom(Player lord, int salt) => Terrain.Hash(Settings.Seed ^ 0x3C6EF372, lord.Id * 7919 + salt, lord.ThinkCount);

        /// <summary>
        /// Game seconds until the lord's next turn. At high world speeds turns are spaced by real time instead
        /// (about every 15 real seconds), which keeps catching up on a long absence quick.
        /// </summary>
        double AiThinkSeconds(Player lord) =>
            Math.Max(SkillThinkMinutes * 60, 15 * Settings.Speed) * (0.8 + 0.4 * AiRandom(lord, 3));

        void AiThink(ScheduledEvent e)
        {
            var lord = FindPlayer(e.A);
            if (lord == null || lord.IsHuman) return;
            lord.ThinkCount++;
            var own = Villages.FindAll(v => v.OwnerId == lord.Id);
            if (own.Count == 0) return; // no villages left: the lord is out of the game

            var style = StyleOf(lord.Personality);
            foreach (var v in own)
            {
                Touch(v);
                AiSpend(lord, v, style);
                AiAttack(lord, v, style); // the main army first; raiders go with what's left
                AiRaid(lord, v, style);
            }
            ScheduleAiThink(lord, AiThinkSeconds(lord));
        }

        AiNote NoteFor(Player lord, int villageId, bool create)
        {
            var note = lord.Notes.Find(n => n.VillageId == villageId);
            if (note == null && create) lord.Notes.Add(note = new AiNote { VillageId = villageId });
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
            if (v.QueuedCount(BuildingType.Farm) == 0 && v.FreePopulation < Math.Max(20, v.PopulationCapacity * 0.08)
                && v.Level(BuildingType.Farm) < Buildings.Get(BuildingType.Farm).MaxLevel)
                return BuildingType.Farm;

            foreach (var (type, level) in PlanFor(lord.Personality))
            {
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

        static readonly Dictionary<AiPersonality, List<(BuildingType type, int level)>> Plans =
            new Dictionary<AiPersonality, List<(BuildingType type, int level)>>();

        /// <summary>Resource buildings first at each stage, then the rest.</summary>
        static readonly BuildingType[] PlanOrder =
        {
            BuildingType.TimberCamp, BuildingType.ClayPit, BuildingType.IronMine, BuildingType.Farm, BuildingType.Warehouse,
            BuildingType.TownHall, BuildingType.Barracks, BuildingType.Wall, BuildingType.Stable, BuildingType.Workshop,
        };

        /// <summary>
        /// A personality's build plan: one step per building level, in order. At stage s every building is taken to
        /// its ratio Ã— s, so the village grows evenly in the personality's proportions.
        /// </summary>
        static List<(BuildingType type, int level)> PlanFor(AiPersonality personality)
        {
            lock (Plans)
            {
                if (Plans.TryGetValue(personality, out var cached)) return cached;
                var style = StyleOf(personality);
                var plan = new List<(BuildingType type, int level)>();
                var reached = new int[Buildings.Count];
                for (int b = 0; b < Buildings.Count; b++) reached[b] = Buildings.Get((BuildingType)b).StartingLevel;
                for (int stage = 1; stage <= 40; stage++)
                    foreach (var type in PlanOrder)
                    {
                        int b = (int)type;
                        int target = Math.Min(Buildings.Get(type).MaxLevel, (int)Math.Floor(style.BuildRatios[b] * stage + 0.5));
                        while (reached[b] < target) plan.Add((type, ++reached[b]));
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
                if (weight <= 0 || v.Level(u.Building) < u.RequiredLevel) continue;
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

        // ---------------------------------------------------------------- raiding barbarians

        /// <summary>Sends cavalry (and axemen) to empty the stores of the nearest barbarian villages worth raiding.</summary>
        void AiRaid(Player lord, Village v, AiStyle style)
        {
            var available = new int[Units.Count];
            available[(int)UnitType.LightCavalry] = v.TroopCount(UnitType.LightCavalry);
            if (lord.Personality != AiPersonality.Defender) available[(int)UnitType.HeavyCavalry] = v.TroopCount(UnitType.HeavyCavalry);
            if (style.RaidsWithInfantry) available[(int)UnitType.Axeman] = v.TroopCount(UnitType.Axeman);
            if (Battle.CarryCapacity(available) < 100) return;

            var targets = new List<(Village village, double distance)>();
            foreach (var t in VillagesNear(v.X, v.Y, AiRaidRange))
                if (t.IsBarbarian) targets.Add((t, Distance(v, t)));
            targets.Sort((a, b) => a.distance.CompareTo(b.distance));
            var underAttack = AttackTargets(lord);

            int raids = 0;
            foreach (var (target, _) in targets)
            {
                if (raids >= SkillMaxRaids || Battle.CarryCapacity(available) < 100) break;
                var note = NoteFor(lord, target.Id, false);
                if (note != null && (note.NextRaidAt > Now || note.AvoidUntil > Now)) continue;
                Touch(target);
                double stock = target.Wood + target.Clay + target.Iron;
                if (stock < 150 || underAttack.Contains(target.Id)) continue;

                var party = RaidParty(available, stock, target.Level(BuildingType.Wall));
                if (party == null) continue;
                var command = Send(v, target, party, CommandKind.Attack);
                if (command == null) continue;
                for (int i = 0; i < Units.Count; i++) available[i] -= party[i];
                NoteFor(lord, target.Id, true).NextRaidAt = Now + 2 * (command.ArriveTime - Now) + AiRaidRestHours * 3600;
                raids++;
            }
        }

        /// <summary>
        /// Enough raiders (fastest carriers first) to carry off <paramref name="stock"/> and beat the villagers and
        /// wall even with the worst luck, or null if the troops available can't do it.
        /// </summary>
        static int[] RaidParty(int[] available, double stock, int wall)
        {
            var party = new int[Units.Count];
            int wanted = (int)Math.Min(stock, 20000), carry = 0;
            foreach (var type in RaidUnits)
            {
                int each = Units.Get(type).Carry;
                int take = Math.Min(available[(int)type], (int)Math.Ceiling(Math.Max(0, wanted - carry) / (double)each));
                party[(int)type] = take;
                carry += take * each;
            }
            if (carry < Math.Min(100, wanted)) return null;
            return Battle.Fight(party, NoTroops, wall, -Battle.MaxLuck).AttackerWon ? party : null;
        }

        // ---------------------------------------------------------------- war on other players

        /// <summary>
        /// Now and then, looks for another player's village in reach that it can beat (as far as it knows), scouting
        /// it first if it can, then sends its whole offensive army, with catapults aimed at the wall or barracks.
        /// </summary>
        void AiAttack(Player lord, Village v, AiStyle style)
        {
            if (AiRandom(lord, 11) >= style.Aggression * SkillAggression) return;

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
            bool hasScouts = v.TroopCount(UnitType.Scout) > 0;
            Village best = null;
            bool bestNeedsScouting = false;
            double bestScore = 0;
            var underAttack = AttackTargets(lord);
            foreach (var t in VillagesNear(v.X, v.Y, AiAttackRange))
            {
                if (t.IsBarbarian || t.OwnerId == lord.Id || IsProtected(t.OwnerId)) continue;
                double d = Distance(v, t);
                var note = NoteFor(lord, t.Id, false);
                if (note != null && (note.AvoidUntil > Now || note.NextRaidAt > Now)) continue;
                if (underAttack.Contains(t.Id)) continue;

                bool known = Known(note);
                bool scoutFirst = !known && hasScouts;
                if (known && !Beatable(offense, note.SeenTroops, note.SeenAt, note.SeenWall)) continue;
                if (!known && !hasScouts && !Beatable(offense, GuessDefenders(t), Now, GuessWall(t))) continue;

                double score = (t.Points + 100) / (2 + d);
                if (scoutFirst) score *= 0.5; // a sure thing beats a maybe
                if (t.OwnerId == lord.LastAttackerId) score *= 2;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                    bestNeedsScouting = scoutFirst;
                }
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

            var seen = NoteFor(lord, best.Id, false);
            int wall = Known(seen) ? seen.SeenWall : GuessWall(best);
            var aim = wall > 0 && offense[(int)UnitType.Ram] == 0 ? BuildingType.Wall : BuildingType.Barracks;
            var command = Send(v, best, offense, CommandKind.Attack, aim);
            // Give the village time to recover before the next attack, so the lord doesn't hammer it non-stop.
            if (command != null) NoteFor(lord, best.Id, true).NextRaidAt = Now + 2 * (command.ArriveTime - Now) + 6 * 3600;
        }

        static int GuessWall(Village v) => Math.Min(20, v.Points / 60);

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
        bool Beatable(int[] offense, int[] seenTroops, double seenAt, int wall)
        {
            double growth = (1 + 0.3 * Math.Max(0, Now - seenAt) / SecondsPerDay) * SkillAttackMargin;
            var expected = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < seenTroops.Length; i++) expected[i] = (int)Math.Ceiling(seenTroops[i] * growth);
            var result = Battle.Fight(offense, expected, wall, -Battle.MaxLuck);
            return result.AttackerWon && result.AttackerLossFraction < 0.7;
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
            bool anySurvivors, Cost loot, int lootCapacity)
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
            }
        }
    }
}
