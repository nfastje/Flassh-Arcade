using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    [Serializable]
    public class Player
    {
        public int Id;
        public string Name;
        public bool IsHuman;
        /// <summary>Villages owned by this player can't be attacked by other players before this game time.</summary>
        public double ProtectedUntil;

        // ---- rival lords (AI) only
        public AiPersonality Personality;
        /// <summary>Which colour the lord's villages have on the map.</summary>
        public int ColorIndex;
        /// <summary>How many times the lord has taken its turn (drives its repeatable random choices).</summary>
        public int ThinkCount;
        /// <summary>Resources spent so far on buildings and on troops, to keep the lord's spending in balance.</summary>
        public long SpentOnBuildings, SpentOnTroops;
        /// <summary>When the lord was last attacked by another player, and by whom (-1: never).</summary>
        public double LastAttackedAt = -1;
        public int LastAttackerId = -1;
        /// <summary>What the lord remembers about other villages.</summary>
        public List<AiNote> Notes = new List<AiNote>();
    }


    /// <summary>
    /// The whole game state, and the rules that move it forward in time. Plain C# with no Unity objects, so it can
    /// be saved as JSON, tested outside Play mode, and fast-forwarded (e.g. to catch up on time spent away).
    ///
    /// Time only moves through <see cref="AdvanceTo"/>: scheduled events run in time order, and continuous change
    /// (like resource production, from Phase 1) is accrued in the gaps between them.
    /// </summary>
    [Serializable]
    public partial class World
    {
        /// <summary>
        /// Save format version. 2 added the village economy (buildings, resources, build queue);
        /// 3 added the military (barracks, stable, workshop, troops, recruitment);
        /// 4 added the wall and the world map (barbarian villages);
        /// 5 added Tribal Wars-style village points and barbarian growth;
        /// 6 added combat (commands, support, reports, barbarian garrisons);
        /// 7 added catapult targets and removed barbarian garrisons again;
        /// 8 added rival lords (computer players) and beginner protection;
        /// 9 grew the map to 250 x 250 and made the world spread outward over time, with lords arriving as it does.
        /// </summary>
        public const int CurrentVersion = 9;
        public const double SecondsPerDay = 24 * 60 * 60;
        /// <summary>A new world starts at dawn on day 1.</summary>
        public const double StartTime = 6 * 60 * 60;
        public const int MapSize = 250;

        public int Version = CurrentVersion;
        public WorldSettings Settings = new WorldSettings();
        /// <summary>Game time, in seconds since midnight before day 1.</summary>
        public double Now;
        public List<Player> Players = new List<Player>();
        public List<Village> Villages = new List<Village>();
        public EventQueue Events = new EventQueue();

        /// <summary>Raised after each event is applied. Not saved.</summary>
        [NonSerialized] public Action<ScheduledEvent> EventApplied;

        static readonly string[] VillageNames =
        {
            "Ashford", "Brightwater", "Oakhollow", "Stonebridge", "Thornbury", "Ravenmoor", "Eastmere", "Wolfden",
            "Kingsbrook", "Hollowell", "Marshgate", "Fernhill", "Coldwater", "Highcliff", "Elmstead", "Blackmoor",
        };

        /// <summary>The name the human player goes by if they didn't give one.</summary>
        public const string DefaultPlayerName = "Player";

        /// <summary>
        /// Creates a world with the human player's first village in the middle of the map, the starting circle of
        /// barbarians and lords round it, and the world set to grow outward from there.
        /// </summary>
        public static World CreateNew(WorldSettings settings)
        {
            var rng = new Random(settings.Seed);
            var world = new World { Settings = settings, Now = StartTime };
            string name = string.IsNullOrWhiteSpace(settings.PlayerName) ? DefaultPlayerName : settings.PlayerName.Trim();
            world.Players.Add(new Player { Id = 0, Name = name, IsHuman = true, ProtectedUntil = world.ProtectionEnd });
            var village = new Village
            {
                Id = 0,
                Name = VillageNames[rng.Next(VillageNames.Length)],
                X = MapSize / 2,
                Y = MapSize / 2,
                OwnerId = 0,
            };
            village.SetUpAsNew();
            world.AddVillage(village);
            world.NextVillageId = 1;
            world.SettleStartingArea();
            world.ScheduleWorldGrowth();
            return world;
        }

        /// <summary>Brings a world loaded from an older save format up to date.</summary>
        public void UpgradeFrom(int savedVersion)
        {
            // Before version 9 every village's stores were always up to date; they are as of now.
            if (savedVersion < 9)
                foreach (var v in Villages) v.StockTime = Now;

            if (savedVersion < 2)
            {
                // Phase 0 villages had no economy: give them a new village's buildings and resources.
                foreach (var v in Villages)
                    if (v.Levels == null || v.Levels.Length == 0 || Array.TrueForAll(v.Levels, l => l == 0))
                        v.SetUpAsNew();
            }

            // Grow per-building and per-unit arrays when later versions add buildings (v3: barracks, stable,
            // workshop) or units; new entries start at 0.
            foreach (var v in Villages)
            {
                v.Levels = Resized(v.Levels, Buildings.Count);
                v.Troops = Resized(v.Troops, Units.Count);
                if (v.Recruitment == null) v.Recruitment = new List<RecruitOrder>();
                if (v.Queue == null) v.Queue = new List<BuildOrder>();
                if (v.Supports == null) v.Supports = new List<SupportGroup>();
            }
            if (Commands == null) Commands = new List<Command>();
            if (Reports == null) Reports = new List<BattleReport>();

            // Version 6 gave barbarians garrisons that re-armed as they grew. Barbarians don't train troops, so
            // disband them (support stationed there by players stays).
            if (savedVersion < 7)
                foreach (var v in Villages)
                    if (v.IsBarbarian)
                    {
                        Array.Clear(v.Troops, 0, v.Troops.Length);
                        v.Recruitment.Clear();
                    }

            // Barbarians from before version 5 didn't grow; start them growing (new ones are scheduled as they're made).
            if (savedVersion >= 4 && savedVersion < 5)
                foreach (var v in Villages)
                    if (v.IsBarbarian) ScheduleBarbarianGrowth(v);

            // Worlds from before the rival lords arrived give everyone a fresh spell of protection, so the lords
            // who now start arriving aren't attacked the moment they settle (nor attack at once).
            foreach (var p in Players)
                if (p.Notes == null) p.Notes = new List<AiNote>();
            bool hadRivals = Players.Exists(p => !p.IsHuman);
            if (savedVersion < 8)
                foreach (var p in Players) p.ProtectedUntil = Now + Settings.ProtectionDays * SecondsPerDay;

            if (savedVersion < 9) UpgradeToGrowingWorld(savedVersion, hadRivals);
            Version = CurrentVersion;
        }

        /// <summary>The map size before version 9, when the world was a fixed 100 x 100 fields.</summary>
        const int OldMapSize = 100;

        /// <summary>
        /// Version 9 made the map 250 x 250 and the world grow outward over time. The old map is moved into the
        /// middle of the new one (so every distance stays the same), and the circle carries on growing from the
        /// edge of the old map.
        /// </summary>
        void UpgradeToGrowingWorld(int savedVersion, bool hadRivals)
        {
            int shift = (MapSize - OldMapSize) / 2;
            foreach (var v in Villages)
            {
                v.X += shift;
                v.Y += shift;
            }
            InvalidateVillageIndex();
            foreach (var v in Villages) NextVillageId = Math.Max(NextVillageId, v.Id + 1);
            LordsSpawned = Players.FindAll(p => !p.IsHuman).Count;
            // A version-8 world with no rivals had them switched off; keep it that way.
            if (savedVersion == 8 && !hadRivals) Settings.RivalDensity = 0;

            if (!Villages.Exists(v => v.IsBarbarian))
                SettleStartingArea(); // from before the map existed: settle round the player now
            else
            {
                SpawnRadius = OldMapSize / 2.0;
                // Worlds that never had rivals get their first lords round the player, as a new world would.
                if (!hadRivals && Settings.RivalDensity > 0)
                {
                    var rng = new Random(Settings.Seed * 7 + 5);
                    int wanted = Math.Max(1, (int)Math.Round(InitialLords * Settings.RivalDensity));
                    for (int i = 0; i < wanted; i++)
                        if (TryFindSpot(rng, () => 8 + rng.NextDouble() * 20, out int x, out int y)) SpawnLord(x, y, rng);
                }
            }
            bool growing = false;
            foreach (var e in Events.Pending) growing |= e.Kind == EventKind.WorldGrowth;
            if (!growing) ScheduleWorldGrowth();
        }

        static int[] Resized(int[] values, int length)
        {
            if (values != null && values.Length >= length) return values;
            var resized = new int[length];
            if (values != null) Array.Copy(values, resized, values.Length);
            return resized;
        }

        public Player HumanPlayer => Players.Find(p => p.IsHuman);

        [NonSerialized] Village playerVillage;

        /// <summary>The human player's first village (Phase 0 has only one).</summary>
        public Village PlayerVillage
        {
            get
            {
                var human = HumanPlayer;
                if (human == null) return null;
                // Asked for many times a frame by the UI, so remembered while it stays the player's.
                if (playerVillage == null || playerVillage.OwnerId != human.Id)
                    playerVillage = Villages.Find(v => v.OwnerId == human.Id);
                return playerVillage;
            }
        }

        /// <summary>Schedules an event <paramref name="delay"/> game seconds from now.</summary>
        public ScheduledEvent Schedule(double delay, EventKind kind, int villageId = -1, int a = 0, int b = 0) =>
            Events.Push(new ScheduledEvent { Time = Now + Math.Max(0, delay), Kind = kind, VillageId = villageId, A = a, B = b });

        /// <summary>Moves the world forward by real seconds, scaled by the world speed.</summary>
        public void AdvanceByRealSeconds(double realSeconds) => AdvanceTo(Now + Math.Max(0, realSeconds) * Settings.Speed);

        /// <summary>
        /// Runs the world up to game time <paramref name="target"/>: every event due by then is applied in order
        /// (including ones scheduled along the way). Production (continuous change) is added to a village's stores
        /// whenever an event needs them, and to every village's at the end, so the world always looks up to date.
        /// </summary>
        public void AdvanceTo(double target)
        {
            while (Events.Count > 0 && Events.Peek().Time <= target)
            {
                var e = Events.Pop();
                Now = Math.Max(Now, e.Time);
                Apply(e);
            }
            Now = Math.Max(Now, target);
            foreach (var v in Villages) Touch(v);
        }

        /// <summary>
        /// Brings a village's stores up to date: adds what its mines have produced since they were last updated,
        /// up to the warehouse's capacity. Call before reading or changing its stock, and before changing a
        /// building that affects production or storage (so the old rate applies up to now).
        /// </summary>
        public void Touch(Village v)
        {
            double seconds = Now - v.StockTime;
            if (seconds <= 0) return;
            v.StockTime = Now;
            double cap = v.StorageCapacity;
            if (v.Wood >= cap && v.Clay >= cap && v.Iron >= cap) return; // full up: nothing to add
            foreach (ResourceType r in ResourceTypes)
            {
                double stock = v.Stock(r);
                if (stock >= cap) continue; // full (or over, after a refund): production is wasted
                v.SetStock(r, Math.Min(cap, stock + v.ProductionPerHour(r) * seconds / 3600));
            }
        }

        static readonly ResourceType[] ResourceTypes = { ResourceType.Wood, ResourceType.Clay, ResourceType.Iron };

        void Apply(ScheduledEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.BuildingComplete:
                    CompleteBuild(e);
                    break;
                case EventKind.UnitTrained:
                    TrainUnit(e);
                    break;
                case EventKind.BarbarianGrowth:
                    GrowBarbarian(e);
                    break;
                case EventKind.CommandArrives:
                    CommandArrives(e);
                    break;
                case EventKind.AiThink:
                    AiThink(e);
                    break;
                case EventKind.WorldGrowth:
                    GrowWorld(e);
                    break;
            }
            EventApplied?.Invoke(e);
        }

        // ---------------------------------------------------------------- time display

        /// <summary>Day number, starting at 1.</summary>
        public static int DayOf(double time) => (int)(time / SecondsPerDay) + 1;

        /// <summary>For example "Day 3, 14:05".</summary>
        public static string FormatClock(double time)
        {
            double inDay = time % SecondsPerDay;
            int hours = (int)(inDay / 3600), minutes = (int)(inDay % 3600 / 60);
            return $"Day {DayOf(time)}, {hours:00}:{minutes:00}";
        }

        /// <summary>A duration in words, for example "2h 14m" or "45s".</summary>
        public static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            if (span.TotalMinutes >= 1) return $"{span.Minutes}m {span.Seconds}s";
            return $"{span.Seconds}s";
        }
    }
}
