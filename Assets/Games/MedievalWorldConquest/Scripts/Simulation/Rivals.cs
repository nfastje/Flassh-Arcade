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
        /// <summary>On average one barbarian village per this many fields of settled land, and one lord per this many (at normal density).</summary>
        public const double FieldsPerBarbarian = 40, FieldsPerLord = 300;
        /// <summary>Lords already settled round the starting circle when a world begins (at normal density).</summary>
        public const int InitialLords = 4;
        /// <summary>The circle stops growing once it covers the whole map, corners included.</summary>
        public static double MaxSpawnRadius => MapSize / 2.0 * 1.42;

        /// <summary>How far out from the centre villages have appeared so far.</summary>
        public double SpawnRadius;
        /// <summary>Fractions of a village owed by the circle's growth so far (so small steps still add up).</summary>
        public double BarbarianBacklog, LordBacklog;
        public int GrowthTicks, LordsSpawned, NextVillageId;

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

        public Player FindPlayer(int id) => Players.Find(p => p.Id == id);

        /// <summary>Whether a player's villages are still under beginner protection.</summary>
        public bool IsProtected(int playerId)
        {
            var p = FindPlayer(playerId);
            return p != null && Now < p.ProtectedUntil;
        }

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
            foreach (var p in Players) byId[p.Id] = new Ranking { Player = p };
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
        /// Fills the starting circle round the player's village with barbarians and settles the first lords round
        /// its edge, then sets the circle growing.
        /// </summary>
        void SettleStartingArea()
        {
            var rng = new Random(Settings.Seed * 7 + 3);
            GrowTo(StartRadius, fillDisk: true);
            int wanted = Settings.RivalDensity <= 0 ? 0 : Math.Max(1, (int)Math.Round(InitialLords * Settings.RivalDensity));
            for (int i = LordsSpawned; i < wanted; i++)
                if (TryFindSpot(rng, () => StartRadius + (rng.NextDouble() * 2 - 1) * RingSpread, out int x, out int y))
                    SpawnLord(x, y, rng);
        }

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
            BarbarianBacklog += area / FieldsPerBarbarian;
            LordBacklog += area * Math.Max(0, Settings.RivalDensity) / FieldsPerLord;
            SpawnRadius = radius;

            var rng = GrowthRng();
            Func<double> barbarianRadius = fillDisk
                ? (Func<double>)(() => radius * Math.Sqrt(rng.NextDouble()))
                : () => radius + (rng.NextDouble() * 2 - 1) * RingSpread;
            Func<double> lordRadius = () => radius + (rng.NextDouble() * 2 - 1) * RingSpread;

            for (; BarbarianBacklog >= 1; BarbarianBacklog--)
                if (TryFindSpot(rng, barbarianRadius, out int x, out int y)) SpawnBarbarian(x, y, rng);
            for (; LordBacklog >= 1; LordBacklog--)
                if (TryFindSpot(rng, lordRadius, out int x, out int y)) SpawnLord(x, y, rng);
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

        /// <summary>A new rival lord settles a new village, with a fresh spell of beginner protection.</summary>
        void SpawnLord(int x, int y, Random rng)
        {
            int id = 0;
            foreach (var p in Players) id = Math.Max(id, p.Id + 1);
            var lord = new Player
            {
                Id = id,
                Name = NewLordName(rng),
                IsHuman = false,
                Personality = (AiPersonality)(1 + (LordsSpawned + (Settings.Seed & 3)) % 4),
                ColorIndex = LordsSpawned,
                ProtectedUntil = Now + Settings.ProtectionDays * SecondsPerDay,
            };
            Players.Add(lord);
            LordsSpawned++;

            var village = new Village { Id = NewVillageId(), Name = NewPlaceName(rng), X = x, Y = y, OwnerId = lord.Id };
            village.SetUpAsNew();
            AddVillage(village);
            // Lords take their first turns a few minutes apart.
            ScheduleAiThink(lord, 60 + rng.NextDouble() * 600);
        }

        string NewLordName(Random rng)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                string name = $"{LordNames[rng.Next(LordNames.Length)]} {Epithets[rng.Next(Epithets.Length)]}";
                if (!Players.Exists(p => p.Name == name)) return name;
            }
            return $"{LordNames[rng.Next(LordNames.Length)]} the {LordsSpawned + 1}th";
        }

        /// <summary>A village name not yet used in this world, like "Crowhurst" or "Stonebrook".</summary>
        public string NewPlaceName(Random rng)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                string name = PlacePrefixes[rng.Next(PlacePrefixes.Length)] + PlaceSuffixes[rng.Next(PlaceSuffixes.Length)];
                if (!Villages.Exists(v => v.Name == name)) return name;
            }
            return $"New {PlacePrefixes[rng.Next(PlacePrefixes.Length)]}{PlaceSuffixes[rng.Next(PlaceSuffixes.Length)]}";
        }
    }
}
