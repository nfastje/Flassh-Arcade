using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>Support a lord sent to a tribe mate, and when to call it home.</summary>
    [Serializable]
    public class SupportPlacement
    {
        public int HostId, FromId;
        public double Until;
    }

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
        /// <summary>Which color the lord's villages have on the map.</summary>
        public int ColorIndex;
        /// <summary>How many times the lord has taken its turn (drives its repeatable random choices).</summary>
        public int ThinkCount;
        /// <summary>
        /// Resources spent on buildings and on troops, to keep the lord's spending in balance (lately, if
        /// <see cref="World.LordSpendingMemoryDays"/> is set: then they fade, as of <see cref="SpentAt"/>).
        /// </summary>
        public double SpentOnBuildings, SpentOnTroops, SpentAt;
        /// <summary>Resources spent on gold coins and noblemen (counted apart from the army unless <see cref="World.ExpansionIsArmySpending"/>).</summary>
        public double SpentOnExpansion;
        /// <summary>When the lord was last attacked by another player, and by whom (-1: never).</summary>
        public double LastAttackedAt = -1;
        public int LastAttackerId = -1;
        /// <summary>What the lord remembers about other villages.</summary>
        public List<AiNote> Notes = new List<AiNote>();
        /// <summary>The village the lord means to win over with its noblemen, or -1.</summary>
        public int ConquestTargetId = -1;
        /// <summary>The most villages the lord has held (checked twice a day), and when they last lost one to another player (-1: never).</summary>
        public int PeakVillages;
        public double LostVillageAt = -1;
        /// <summary>When the lord last sent a fake on its own (not alongside a real attack); -1: never.</summary>
        public double LastFakeAt = -1;
        /// <summary>The lord's statistics (by <see cref="StatKind"/>): all time, and where they stood when the day and the week began.</summary>
        public long[] Stats = new long[World.StatKinds], StatsDayStart = new long[World.StatKinds], StatsWeekStart = new long[World.StatKinds];
        /// <summary>For noobs: when they recently lost fights in their villages (too many in a short time and they quit).</summary>
        public List<double> HitTimes = new List<double>();
        /// <summary>Whether the lord has given up: its villages went barbarian and it no longer plays.</summary>
        public bool Quit;
        /// <summary>Gold coins minted so far (on coin worlds): they buy noble slots.</summary>
        public int Coins;

        // ---- tribes (diplomacy worlds)
        /// <summary>The player's tribe, or -1.</summary>
        public int TribeId = -1;
        public double JoinedTribeAt;
        /// <summary>
        /// How happy the player is in their tribe, 0 to 100 (for the human: how their tribe sees them). Help given
        /// and received raises it; being left alone under attack, or leaving others to it, lowers it.
        /// </summary>
        public double Satisfaction = 60;
        /// <summary>How far others trust the player, around 0: broken pacts and attacks on friends lower it; it recovers slowly.</summary>
        public double Reputation;
        /// <summary>Support this lord has sent to help tribe mates, to call home once the danger has passed.</summary>
        public List<SupportPlacement> SupportPlacements = new List<SupportPlacement>();
        /// <summary>For the human: the tribe they've asked to join (-1: none), and lords they've invited to theirs.</summary>
        public int AskedToJoinTribe = -1;
        public List<int> Invited = new List<int>();
        /// <summary>
        /// How far a lord likes to spread, 0 to 1: a consolidator (near 0) wins villages close to its own, keeping a
        /// tight, defensible core; a spreader (near 1) reaches further for the richest prizes and ends up scattered.
        /// </summary>
        public double Spread = 0.5;
        /// <summary>For noobs: how far their build plan goes before they stop growing (0: their personality's usual).</summary>
        public int StageCap;
        /// <summary>For inactive players: the village points their village grows to before they stop for good.</summary>
        public int TargetPoints;
        /// <summary>The notes above, by village (not saved).</summary>
        [NonSerialized] public Dictionary<int, AiNote> NotesByVillage;
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
        /// 9 grew the map to 250 x 250 and made the world spread outward over time, with lords arriving as it does;
        /// 10 added conquest: the academy, noblemen, loyalty, owning several villages, and winning or losing;
        /// 11 matched Tribal Wars' build and training times, and made most newcomers lords (many of them noobs who
        /// can quit, leaving barbarian villages behind);
        /// 12 added the rally point, village renaming, Tribal Wars' building population and single-field ponds;
        /// 13 added the mounted archer;
        /// 14 added the smithy (research), the market (merchants and offers) and the hiding place;
        /// 15 added inactive players and more barbarian villages to fill the map;
        /// 16 gave each noob its own limit on how far it grows;
        /// 17 added the gold-coin option for noblemen (older worlds keep the flat price);
        /// 18 added tribes, diplomacy and messages (an option for new worlds; older worlds stay free-for-all);
        /// 19 added the dominance ending (holding the goal), the world lock and how far each lord likes to spread;
        /// 20 added the factions' endgame (villages handed over), noble trains and world statistics;
        /// 21 added quests, tips, time played, the Account Manager and the Loot Assistant.
        /// </summary>
        public const int CurrentVersion = 22;

        /// <summary>The longest name a village can be given.</summary>
        public const int MaxVillageNameLength = 32;

        /// <summary>
        /// Renames one of the player's villages. The name is trimmed and cut to <see cref="MaxVillageNameLength"/>
        /// characters. Returns whether it changed.
        /// </summary>
        public bool RenameVillage(Village v, string name)
        {
            if (v == null || v.OwnerId != HumanPlayer?.Id) return false;
            name = (name ?? "").Trim();
            if (name.Length == 0) return false;
            if (name.Length > MaxVillageNameLength) name = name.Substring(0, MaxVillageNameLength).TrimEnd();
            if (name == v.Name) return false;
            v.Name = name;
            return true;
        }
        public const double SecondsPerDay = 24 * 60 * 60;
        /// <summary>A new world starts at dawn on day 1.</summary>
        public const double StartTime = 6 * 60 * 60;
        public const int MapSize = 250;

        public int Version = CurrentVersion;
        public WorldSettings Settings = new WorldSettings();
        /// <summary>Game time, in seconds since midnight before day 1.</summary>
        public double Now;
        /// <summary>Real seconds the player has spent in this world (for their lifetime record).</summary>
        public double PlayedSeconds;
        /// <summary>
        /// Set while the world catches up on time the game was closed (not saved): the player wasn't there, so
        /// tribe mates don't ask them for help, and can't hold it against them.
        /// </summary>
        [NonSerialized] public bool PlayerAway;
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
            if (settings.Diplomacy) world.ScheduleTribeTick();
            world.ScheduleManagerTick();
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
                v.Research = Resized(v.Research, Units.Count);
                if (v.Researching == null) v.Researching = new List<ResearchOrder>();
                if (v.Recruitment == null) v.Recruitment = new List<RecruitOrder>();
                if (v.Queue == null) v.Queue = new List<BuildOrder>();
                if (v.Supports == null) v.Supports = new List<SupportGroup>();
            }
            if (Commands == null) Commands = new List<Command>();
            if (Reports == null) Reports = new List<BattleReport>();
            if (ArchivedReports == null) ArchivedReports = new List<BattleReport>();
            if (Offers == null) Offers = new List<MarketOffer>();
            // Troops on the march or stationed elsewhere need room for new units too (v13: the mounted archer).
            foreach (var c in Commands) c.Troops = Resized(c.Troops, Units.Count);
            foreach (var v in Villages)
                foreach (var g in v.Supports) g.Troops = Resized(g.Troops, Units.Count);

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

            // Loyalty arrived with conquest: every village starts fully loyal.
            if (savedVersion < 10)
            {
                foreach (var v in Villages) v.Loyalty = MaxLoyalty;
                foreach (var p in Players) p.ConquestTargetId = -1;
                CurrentVillageId = PlayerVillage?.Id ?? 0;
            }
            foreach (var p in Players)
            {
                if (p.HitTimes == null) p.HitTimes = new List<double>();
                if (p.SupportPlacements == null) p.SupportPlacements = new List<SupportPlacement>();
                if (p.Invited == null) p.Invited = new List<int>();
                if (savedVersion < 18)
                {
                    p.TribeId = -1;
                    p.AskedToJoinTribe = -1;
                    p.Satisfaction = 60;
                }
            }
            if (Tribes == null) Tribes = new List<Tribe>();
            if (Relations == null) Relations = new List<TribeRelation>();
            if (Messages == null) Messages = new List<Message>();
            if (HoldBloc == null) HoldBloc = new List<int>();
            // The goal used to be 60% (of the villages players rule); every world now plays for half the realm.
            if (Math.Abs(Settings.ConquestGoal - 0.6f) < 1e-4f) Settings.ConquestGoal = WorldSettings.StandardGoal;
            // Version 20: factions, handed-over villages and statistics (counted from now on).
            if (savedVersion < 20)
            {
                foreach (var v in Villages) v.FedTo = -1;
                foreach (var t in Tribes) t.FactionId = -1;
                foreach (var c in Commands) c.TargetOwnerId = -2;
                StatsDay = DayOf(Now);
            }
            if (WorldStats == null || WorldStats.Length != StatKinds) WorldStats = Resized(WorldStats, StatKinds);
            if (WorldStatsDayStart == null || WorldStatsDayStart.Length != StatKinds) WorldStatsDayStart = Resized(WorldStatsDayStart, StatKinds);
            if (StatHistory == null) StatHistory = new List<DayStats>();
            if (Records == null) Records = new RealmRecords();
            if (TipsShown == null) TipsShown = new List<int>();
            // Version 21: the Account Manager (nothing managed yet, but its rounds start).
            if (ManagedVillages == null) ManagedVillages = new List<ManagedVillage>();
            if (CustomTemplates == null) CustomTemplates = new List<BuildTemplate>();
            foreach (var m in ManagedVillages) m.TroopTargets = Resized(m.TroopTargets, Units.Count);
            foreach (var m in ManagedVillages) if (m.TroopTemplate == null) m.TroopTemplate = "";
            if (CustomTroopTemplates == null) CustomTroopTemplates = new List<TroopTemplate>();
            // Version 22: the Account Manager splits spending between buildings and troops.
            if (savedVersion < 22)
                foreach (var m in ManagedVillages)
                {
                    m.TroopShare = DefaultTroopShare;
                    m.SpentAt = Now;
                }
            bool managing = false;
            foreach (var e in Events.Pending) managing |= e.Kind == EventKind.ManagerTick;
            if (!managing) ScheduleManagerTick();
            // The Loot Assistant: unlocked for anyone already past the first raid quest.
            if (LootTargets == null) LootTargets = new List<LootTarget>();
            if (!LootAssistantUnlocked && QuestIndex > Array.FindIndex(QuestLine, q => q.Title == LootAssistantQuest)) UnlockLootAssistant();
            if (LootAssistantUnlocked) EnsureLootTemplates();
            foreach (var p in Players) EnsureStats(p);
            // Every village has a rally point, as in Tribal Wars.
            if (savedVersion < 12)
                foreach (var v in Villages) v.Levels[(int)BuildingType.RallyPoint] = Math.Max(1, v.Levels[(int)BuildingType.RallyPoint]);
            // Before the smithy, a unit only needed its building: villages keep every unit they could train then
            // (and any they have), so no army suddenly can't be reinforced.
            if (savedVersion < 14)
                foreach (var v in Villages)
                    for (int i = 0; i < Units.Count; i++)
                    {
                        var u = Units.Get((UnitType)i);
                        if (v.Level(u.Building) >= u.RequiredLevel || v.Troops[i] > 0) v.Research[i] = 1;
                    }
            // Inactive players and the extra barbarians arrived in version 15: fill in the land already settled.
            if (savedVersion < 15 && savedVersion >= 9) FillInSettledLand();
            // Noobs all used to stop at the same size: give each its own limit (some will start growing again).
            foreach (var p in Players)
                if (p.Personality == AiPersonality.Noob && p.StageCap == 0)
                    p.StageCap = NoobStageCap(Terrain.Hash(Settings.Seed ^ 0x68E31DA4, p.Id, 16));
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
                        if (TryFindSpot(rng, () => 8 + rng.NextDouble() * 20, out int x, out int y)) SpawnLord(x, y, rng, RegularPersonality());
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

        /// <summary>The village the player has chosen to look at and give orders from.</summary>
        public int CurrentVillageId;

        [NonSerialized] Village playerVillage;

        /// <summary>
        /// The human player's current village: the one they chose, or, if they've lost it (or never chose), their
        /// first. Null once they have no villages left.
        /// </summary>
        public Village PlayerVillage
        {
            get
            {
                var human = HumanPlayer;
                if (human == null) return null;
                // Asked for many times a frame by the UI, so remembered while it stays the player's current one.
                if (playerVillage == null || playerVillage.OwnerId != human.Id || playerVillage.Id != CurrentVillageId)
                {
                    var chosen = FindVillage(CurrentVillageId);
                    playerVillage = chosen != null && chosen.OwnerId == human.Id ? chosen : Villages.Find(v => v.OwnerId == human.Id);
                    if (playerVillage != null) CurrentVillageId = playerVillage.Id;
                }
                return playerVillage;
            }
        }

        /// <summary>All the human player's villages, oldest first.</summary>
        public List<Village> HumanVillages()
        {
            var human = HumanPlayer;
            return human == null ? new List<Village>() : new List<Village>(VillagesOf(human.Id));
        }

        /// <summary>
        /// All the human player's villages in order of name, as the overview, the Account Manager and the village
        /// arrows list them (numbers inside names in number order: "Village 2" before "Village 10"; oldest first
        /// among equals).
        /// </summary>
        public List<Village> HumanVillagesByName()
        {
            var list = HumanVillages();
            var age = new Dictionary<Village, int>();
            for (int i = 0; i < list.Count; i++) age[list[i]] = i;
            list.Sort((a, b) =>
            {
                int byName = CompareNames(a.Name, b.Name);
                return byName != 0 ? byName : age[a].CompareTo(age[b]);
            });
            return list;
        }

        /// <summary>Compares names as people read them: letters ignoring case, and runs of digits by their value.</summary>
        public static int CompareNames(string a, string b)
        {
            a ??= "";
            b ??= "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int digits = string.CompareOrdinal(na, nb);
                    if (digits != 0) return digits;
                    continue;
                }
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        /// <summary>Makes one of the player's villages the current one. Returns whether it's theirs.</summary>
        public bool SelectVillage(int villageId)
        {
            var v = FindVillage(villageId);
            if (v == null || v.OwnerId != HumanPlayer?.Id) return false;
            CurrentVillageId = villageId;
            return true;
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
            RebuildOwnerIndex();
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
        /// Brings a village up to date: the units whose training has finished join its garrison, its loyalty
        /// recovers, and its stores get what its mines have produced since they were last updated (up to the
        /// warehouse's capacity). Call before reading or changing its stock, and before changing a
        /// building that affects production or storage (so the old rate applies up to now).
        /// </summary>
        public void Touch(Village v)
        {
            CatchUpRecruitment(v);
            double seconds = Now - v.StockTime;
            if (seconds <= 0) return;
            v.StockTime = Now;
            if (v.Loyalty < MaxLoyalty) v.Loyalty = Math.Min(MaxLoyalty, v.Loyalty + LoyaltyPerHour * seconds / 3600);
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
                case EventKind.ResearchComplete:
                    CompleteResearch(e);
                    break;
                case EventKind.OfferExpires:
                    ExpireOffer(e);
                    break;
                case EventKind.InactiveGrowth:
                    GrowInactive(e);
                    break;
                case EventKind.InactiveLeaves:
                    InactiveLeaves(e);
                    break;
                case EventKind.TribeTick:
                    TribeTick(e);
                    break;
                case EventKind.ManagerTick:
                    ManagerTick(e);
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
