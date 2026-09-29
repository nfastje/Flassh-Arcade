using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>A rival lord's style of play.</summary>
    public enum AiPersonality
    {
        /// <summary>Not a computer player (the human).</summary>
        None = 0,
        /// <summary>Fast cavalry, lots of farming, picks on the weak.</summary>
        Raider = 1,
        /// <summary>Walls and defensive troops; rarely attacks anyone.</summary>
        Defender = 2,
        /// <summary>A bit of everything.</summary>
        Balanced = 3,
        /// <summary>Big offensive armies with siege engines; attacks players often.</summary>
        Warlord = 4,
        /// <summary>
        /// A newcomer who barely knows how to play: slow to act, few troops, never attacks. Early-game prey, and if
        /// raided often enough in a short time, gives up and leaves a barbarian village behind.
        /// </summary>
        Noob = 5,
        /// <summary>
        /// A player who has stopped playing: their village builds itself up at random to a set size (as if they
        /// played a little before leaving), then stays as it is. It never takes turns, so it costs next to nothing
        /// to run, but it fills the map. Two weeks after it stops, the player is gone for good and the village goes
        /// barbarian, as Tribal Wars closes inactive accounts.
        /// </summary>
        Inactive = 6,
    }

    /// <summary>What a rival lord remembers about one village it has raided, scouted or attacked.</summary>
    [Serializable]
    public class AiNote
    {
        public int VillageId;
        /// <summary>Don't raid it again before this (its stores need time to fill).</summary>
        public double NextRaidAt;
        /// <summary>An attack on it failed: stay away until then.</summary>
        public double AvoidUntil;
        /// <summary>When its defenders were last seen, or -1 if never.</summary>
        public double SeenAt = -1;
        /// <summary>The defenders (and wall) seen then.</summary>
        public int[] SeenTroops;
        public int SeenWall;
        /// <summary>
        /// When the lord last had an idea of the village's plunder (its scouts counted it, or a raid emptied it or
        /// came back full), or -1 if never; and how much could be carried off then, apart from what's hidden.
        /// </summary>
        public double LootSeenAt = -1;
        public int LootWood, LootClay, LootIron;
        /// <summary>The building levels its scouts last saw (for the mines, warehouse and hiding place), or null.</summary>
        public int[] SeenLevels;
    }

    /// <summary>A player's standing in the rankings.</summary>
    public struct Ranking
    {
        public Player Player;
        public int Points, Villages;
    }

    /// <summary>
    /// How the world fills up, as in Tribal Wars: it starts as a small settled circle round the middle of the map,
    /// and the circle keeps widening as the days go by. New barbarian villages and new rival lords appear round
    /// its edge (give or take a few fields), so the middle holds the oldest, biggest lords and the frontier the
    /// newcomers, and there's no fixed number of lords: they keep arriving until the map is full.
    /// Also: beginner protection and the rankings.
    /// </summary>
    public partial class World
    {
        /// <summary>The settled circle's radius (in fields) when a world begins, and how much it widens each game day.</summary>
        public const double StartRadius = 12, RadiusPerDay = 1.5;
        /// <summary>New villages appear within this many fields either side of the circle's edge.</summary>
        public const double RingSpread = 4;
        /// <summary>Game hours between the circle's steps outward.</summary>
        public const double GrowthIntervalHours = 2;
        /// <summary>On average one new village (a lord's or a barbarian one) per this many fields of settled land.</summary>
        public const double FieldsPerSettlement = 40;
        /// <summary>
        /// Of the lords who settle, the share who are regular players (a Raider, Defender, Balanced or Warlord);
        /// the rest are noobs. Barbarians are rare at first: most barbarian villages are left behind by noobs who quit.
        /// </summary>
        public const double RegularLordShare = 0.35;
        /// <summary>Regular lords already settled round the starting circle when a world begins (at normal density), and how close they may be to the player.</summary>
        public const int InitialLords = 3;
        public const double InitialLordMinDistance = 10;
        /// <summary>The circle stops growing once it covers the whole map, corners included.</summary>
        public static double MaxSpawnRadius => MapSize / 2.0 * 1.42;

        /// <summary>How far out from the centre villages have appeared so far.</summary>
        public double SpawnRadius;
        /// <summary>The fraction of a village owed by the circle's growth so far (so small steps still add up).</summary>
        public double SettlementBacklog;
        public int GrowthTicks, LordsSpawned, RegularsSpawned, NextVillageId;

        static readonly string[] LordNames =
        {
            "Aldric", "Brenna", "Cedric", "Isolde", "Godfrey", "Rowena", "Osric", "Matilda", "Leofric", "Edith",
            "Baldwin", "Gisela", "Tancred", "Ysolde", "Wulfric", "Aveline", "Roderick", "Sibyl", "Hereward", "Maud",
            "Percival", "Elspeth", "Gareth", "Ottilie", "Alaric", "Beatrix", "Conrad", "Dagny", "Eadric", "Fenna",
            "Gunther", "Hilda",
        };

        static readonly string[] Epithets =
        {
            "the Bold", "the Grim", "Ironhand", "the Fair", "the Red", "Oakheart", "the Wise", "the Cruel",
            "Stormborn", "the Just", "Longsword", "the Young", "the Old", "Blackthorn", "the Proud", "Hawkeye",
            "the Quiet", "Wolfsbane", "the Tall", "Goldtooth", "the Pious", "Stoneface", "the Swift", "Ravenhair",
        };

        static readonly string[] PlacePrefixes =
        {
            "Ash", "Briar", "Crow", "Dun", "Elder", "Fox", "Grey", "Harrow", "Iron", "Kestrel", "Lark", "Mill",
            "North", "Oxen", "Pine", "Queen", "Red", "Salt", "Tallow", "Under", "Vale", "West", "Yarrow", "Oak",
            "Stone", "Thorn", "Raven", "Wolf", "Elm", "Black", "White", "Gold", "Hawk", "Eagle", "Bramble", "Fallow",
            "Deep", "High", "Low", "Cold",
        };

        static readonly string[] PlaceSuffixes =
        {
            "ford", "wick", "hurst", "mere", "holt", "ley", "water", "gate", "bridge", "hill", "keep", "brook",
            "wold", "dale", "field", "haven", "moor", "stead", "ton", "by", "combe", "den", "well", "march",
        };

        /// <summary>When the beginner protection given at the start of a new world runs out.</summary>
        public double ProtectionEnd => StartTime + Settings.ProtectionDays * SecondsPerDay;

        // Players by id, built when first needed (not saved). Players are only ever added, so the index is rebuilt
        // whenever the list has grown.
        [NonSerialized] Dictionary<int, Player> playersById;

        public Player FindPlayer(int id)
        {
            if (playersById == null || playersById.Count != Players.Count)
            {
                playersById = new Dictionary<int, Player>(Players.Count);
                foreach (var p in Players) playersById[p.Id] = p;
            }
            return playersById.TryGetValue(id, out var player) ? player : null;
        }

        /// <summary>Whether a player's villages are still under beginner protection.</summary>
        public bool IsProtected(int playerId)
        {
            var p = FindPlayer(playerId);
            return p != null && Now < p.ProtectedUntil;
        }

        /// <summary>The share of new villages that are lords' rather than barbarian: 90% at normal density.</summary>
        double LordShare => Math.Min(0.95, 0.9 * Math.Max(0, Settings.RivalDensity));

        /// <summary>The name of a village's owner, for display.</summary>
        public string OwnerName(Village v) => v.IsBarbarian ? "Barbarians" : FindPlayer(v.OwnerId)?.Name ?? "Unknown";

        public int PointsOf(Player p)
        {
            int points = 0;
            foreach (var v in Villages)
                if (v.OwnerId == p.Id) points += v.Points;
            return points;
        }

        /// <summary>Every player, best first: by points, then by number of villages.</summary>
        public List<Ranking> Rankings()
        {
            var byId = new Dictionary<int, Ranking>();
            foreach (var p in Players)
                if (!p.Quit) byId[p.Id] = new Ranking { Player = p };
            foreach (var v in Villages)
                if (byId.TryGetValue(v.OwnerId, out var r))
                {
                    r.Points += v.Points;
                    r.Villages++;
                    byId[v.OwnerId] = r;
                }
            var list = new List<Ranking>(byId.Values);
            list.Sort((a, b) => a.Points != b.Points ? b.Points.CompareTo(a.Points) : b.Villages.CompareTo(a.Villages));
            return list;
        }

        // ---------------------------------------------------------------- the growing world

        /// <summary>
        /// Fills the starting circle round the player's village with newcomers (mostly noobs, the odd barbarian
        /// village), then settles a few regular lords round its edge, not right next to the player, and sets the
        /// circle growing.
        /// </summary>
        void SettleStartingArea()
        {
            var rng = new Random(Settings.Seed * 7 + 3);
            GrowTo(StartRadius, fillDisk: true);
            if (Settings.RivalDensity <= 0) return;
            int wanted = Math.Max(1, Math.Min(5, (int)Math.Round(InitialLords * Settings.RivalDensity)));
            var centre = MapSize / 2.0;
            for (int i = 0; i < wanted; i++)
                if (TryFindSpot(rng, () => InitialLordMinDistance + rng.NextDouble() * (StartRadius + RingSpread - InitialLordMinDistance), out int x, out int y))
                    SpawnLord(x, y, rng, RegularPersonality());
        }

        /// <summary>
        /// How far a noob gets before it stops growing, from a random number 0..1: most stall small (stage 5, the
        /// least, is about 150 points and just gets a barracks; stage 9 about 230; stage 19 about 800), and one in
        /// ten never stops, just grows slowly.
        /// </summary>
        public static int NoobStageCap(double r) => r >= 0.9 ? PlanStages : 5 + (int)(14 * Math.Pow(r / 0.9, 1.5));

        /// <summary>The next regular personality in turn, so each kind turns up about as often as the others.</summary>
        AiPersonality RegularPersonality() => (AiPersonality)(1 + (RegularsSpawned++ + (Settings.Seed & 3)) % 4);

        void ScheduleWorldGrowth() => Schedule(GrowthIntervalHours * 3600, EventKind.WorldGrowth);

        void GrowWorld(ScheduledEvent e)
        {
            GrowthTicks++;
            GrowTo(Math.Min(MaxSpawnRadius, SpawnRadius + RadiusPerDay * GrowthIntervalHours / 24), fillDisk: false);
            if (SpawnRadius < MaxSpawnRadius) ScheduleWorldGrowth();
        }

        /// <summary>A repeatable random-number generator for this step of the world's growth.</summary>
        Random GrowthRng() => new Random(unchecked(Settings.Seed * 486187739 + GrowthTicks * 16777619 + 11));

        /// <summary>
        /// Widens the settled circle to <paramref name="radius"/>, adding villages in proportion to the new land:
        /// scattered over the whole disc at the start, round the edge afterwards.
        /// </summary>
        void GrowTo(double radius, bool fillDisk)
        {
            double old = SpawnRadius;
            if (radius <= old) return;
            double area = Math.PI * (radius * radius - old * old);
            SettlementBacklog += area / FieldsPerSettlement;
            SpawnRadius = radius;

            var rng = GrowthRng();
            Func<double> where = fillDisk
                ? (Func<double>)(() => radius * Math.Sqrt(rng.NextDouble()))
                : () => radius + (rng.NextDouble() * 2 - 1) * RingSpread;

            // Each new village is a lord's (mostly noobs, some regular players) or, now and then, a barbarian one.
            // Right round the player at the start they're all noobs: the regular lords there are placed separately,
            // at a distance.
            for (; SettlementBacklog >= 1; SettlementBacklog--)
            {
                if (!TryFindSpot(rng, where, out int x, out int y)) continue;
                if (rng.NextDouble() >= LordShare) SpawnBarbarian(x, y, rng);
                else SpawnLord(x, y, rng, !fillDisk && rng.NextDouble() < RegularLordShare ? RegularPersonality() : AiPersonality.Noob);
            }
            // Between them, inactive players and more barbarians.
            FillIn(area, rng, where);
        }

        /// <summary>
        /// A free field of land at a distance from the centre drawn from <paramref name="radius"/>, in any direction.
        /// Villages may be right next to each other, just not on the same field.
        /// </summary>
        bool TryFindSpot(Random rng, Func<double> radius, out int x, out int y)
        {
            double centre = MapSize / 2.0;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                double angle = rng.NextDouble() * Math.PI * 2, r = Math.Abs(radius());
                x = (int)Math.Round(centre + Math.Cos(angle) * r);
                y = (int)Math.Round(centre + Math.Sin(angle) * r);
                if (x < 1 || y < 1 || x > MapSize - 2 || y > MapSize - 2) continue;
                if (TerrainAt(x, y) == TerrainType.Water || VillageAt(x, y) != null) continue;
                return true;
            }
            x = y = -1;
            return false; // off the map or all water that way: this village never appears
        }

        int NewVillageId()
        {
            while (FindVillage(NextVillageId) != null) NextVillageId++;
            return NextVillageId++;
        }

        void SpawnBarbarian(int x, int y, Random rng)
        {
            var village = new Village { Id = NewVillageId(), Name = BarbarianName, X = x, Y = y, OwnerId = -1 };
            // Young and small: they grow on their own from here.
            SetUpBarbarian(village, rng.NextDouble() * 0.4, rng);
            AddVillage(village);
            ScheduleBarbarianGrowth(village);
        }

        /// <summary>A new computer player (a lord, or an inactive player), with a fresh spell of beginner protection.</summary>
        Player NewPlayer(AiPersonality personality, Random rng)
        {
            int id = 0;
            foreach (var p in Players) id = Math.Max(id, p.Id + 1);
            var player = new Player
            {
                Id = id,
                Name = NewLordName(rng),
                IsHuman = false,
                Personality = personality,
                ColorIndex = LordsSpawned,
                ProtectedUntil = Now + Settings.ProtectionDays * SecondsPerDay,
            };
            Players.Add(player);
            LordsSpawned++;
            return player;
        }

        /// <summary>A new rival lord settles a new village, with a fresh spell of beginner protection.</summary>
        void SpawnLord(int x, int y, Random rng, AiPersonality personality)
        {
            var lord = NewPlayer(personality, rng);
            if (personality == AiPersonality.Noob) lord.StageCap = NoobStageCap(rng.NextDouble());
            var village = new Village { Id = NewVillageId(), Name = NewPlaceName(rng), X = x, Y = y, OwnerId = lord.Id };
            village.SetUpAsNew();
            AddVillage(village);
            // Lords take their first turns a few minutes apart.
            ScheduleAiThink(lord, 60 + rng.NextDouble() * 600);
        }

        static readonly string[] PlaceQualifiers =
        {
            "Upper", "Lower", "Great", "Little", "East", "West", "North", "South", "Old", "New", "Far", "Nether",
        };

        // Names in use, so thousands of players and villages can each get their own quickly (not saved; rebuilt
        // when first needed).
        [NonSerialized] HashSet<string> playerNames, placeNames;

        string NewLordName(Random rng)
        {
            if (playerNames == null) playerNames = new HashSet<string>(Players.ConvertAll(p => p.Name));
            for (int attempt = 0; attempt < 60; attempt++)
            {
                string first = LordNames[rng.Next(LordNames.Length)];
                // "Aldric the Bold" at first; once those run short, "Aldric of Crowhurst" too.
                string name = attempt < 20
                    ? $"{first} {Epithets[rng.Next(Epithets.Length)]}"
                    : $"{first} of {PlacePrefixes[rng.Next(PlacePrefixes.Length)]}{PlaceSuffixes[rng.Next(PlaceSuffixes.Length)]}";
                if (playerNames.Add(name)) return name;
            }
            string fallback = $"{LordNames[rng.Next(LordNames.Length)]} the {LordsSpawned + 1}th";
            playerNames.Add(fallback);
            return fallback;
        }

        /// <summary>A village name not yet used in this world, like "Crowhurst", "Stonebrook" or "Little Ashford".</summary>
        public string NewPlaceName(Random rng)
        {
            if (placeNames == null) placeNames = new HashSet<string>(Villages.ConvertAll(v => v.Name));
            for (int attempt = 0; attempt < 60; attempt++)
            {
                string name = PlacePrefixes[rng.Next(PlacePrefixes.Length)] + PlaceSuffixes[rng.Next(PlaceSuffixes.Length)];
                if (attempt >= 20) name = $"{PlaceQualifiers[rng.Next(PlaceQualifiers.Length)]} {name}";
                if (placeNames.Add(name)) return name;
            }
            return $"New {PlacePrefixes[rng.Next(PlacePrefixes.Length)]}{PlaceSuffixes[rng.Next(PlaceSuffixes.Length)]}";
        }
    }
}
