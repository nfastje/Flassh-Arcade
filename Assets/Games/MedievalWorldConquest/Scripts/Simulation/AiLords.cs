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
    }
}
