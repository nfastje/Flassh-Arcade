using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>The world map: finding villages, distances, how long armies take to travel, and barbarian villages.</summary>
    public partial class World
    {
        public const string BarbarianName = "Barbarian Village";

        public TerrainType TerrainAt(int x, int y) => Terrain.At(Settings.Seed, x, y);

        // ---------------------------------------------------------------- finding villages

        // Lookups by id, by field and by area, built from the village list when first needed (not saved). Villages
        // are only ever added (conquest changes their owner), so the index is rebuilt if the list's length changes
        // behind its back, e.g. in tests.
        [NonSerialized] Dictionary<int, Village> villagesById;
        [NonSerialized] Dictionary<int, Village> villagesByField;
        [NonSerialized] List<Village>[] villageBuckets;
        [NonSerialized] int indexedVillages = -1;
        /// <summary>The map is split into square buckets this many fields across, for finding villages near a point.</summary>
        const int BucketSize = 10;
        static int BucketsAcross => (MapSize + BucketSize - 1) / BucketSize;

        void EnsureVillageIndex()
        {
            if (villagesById != null && indexedVillages == Villages.Count) return;
            villagesById = new Dictionary<int, Village>(Villages.Count);
            villagesByField = new Dictionary<int, Village>(Villages.Count);
            villageBuckets = new List<Village>[BucketsAcross * BucketsAcross];
            indexedVillages = 0;
            foreach (var v in Villages) Index(v);
        }

        void Index(Village v)
        {
            villagesById[v.Id] = v;
            villagesByField[FieldKey(v.X, v.Y)] = v;
            int b = BucketOf(v.X, v.Y);
            if (b >= 0) (villageBuckets[b] ?? (villageBuckets[b] = new List<Village>())).Add(v);
            indexedVillages++;
        }

        /// <summary>Forgets the index, e.g. after villages have been moved.</summary>
        void InvalidateVillageIndex() => villagesById = null;

        static int FieldKey(int x, int y) => y * 4096 + x;

        static int BucketOf(int x, int y)
        {
            if (x < 0 || y < 0 || x >= MapSize || y >= MapSize) return -1;
            return y / BucketSize * BucketsAcross + x / BucketSize;
        }

        /// <summary>Adds a new village to the world.</summary>
        public void AddVillage(Village v)
        {
            v.StockTime = Now; // its stores are as of now
            EnsureVillageIndex();
            Villages.Add(v);
            Index(v);
            if (villagesByOwner != null) OwnerList(v.OwnerId).Add(v);
        }

        // Each owner's villages (barbarians' under -1), in the order they were founded or taken. Rebuilt at the
        // start of every advance (so changes made from outside are picked up) and kept in step by SetOwner.
        [NonSerialized] Dictionary<int, List<Village>> villagesByOwner;

        void RebuildOwnerIndex()
        {
            villagesByOwner = new Dictionary<int, List<Village>>();
            foreach (var v in Villages) OwnerList(v.OwnerId).Add(v);
        }

        List<Village> OwnerList(int ownerId)
        {
            if (!villagesByOwner.TryGetValue(ownerId, out var list)) villagesByOwner[ownerId] = list = new List<Village>();
            return list;
        }

        /// <summary>A player's villages (-1: the barbarians'). Don't modify the list.</summary>
        public List<Village> VillagesOf(int ownerId)
        {
            if (villagesByOwner == null) RebuildOwnerIndex();
            return OwnerList(ownerId);
        }

        /// <summary>Hands a village to a new owner (-1 for the barbarians).</summary>
        void SetOwner(Village v, int ownerId)
        {
            if (villagesByOwner != null && v.OwnerId != ownerId)
            {
                OwnerList(v.OwnerId).Remove(v);
                OwnerList(ownerId).Add(v);
            }
            // Its old owner's market offers go with them (their goods were set aside, so nothing comes back).
            if (v.OwnerId != ownerId) Offers.RemoveAll(o => o.VillageId == v.Id);
            v.OwnerId = ownerId;
        }

        /// <summary>The village on a map field, if any.</summary>
        public Village VillageAt(int x, int y)
        {
            EnsureVillageIndex();
            return villagesByField.TryGetValue(FieldKey(x, y), out var v) ? v : null;
        }

        public Village FindVillage(int id)
        {
            EnsureVillageIndex();
            return villagesById.TryGetValue(id, out var v) ? v : null;
        }

        /// <summary>Every village within <paramref name="radius"/> fields of a point (added to <paramref name="into"/>).</summary>
        public List<Village> VillagesNear(int x, int y, double radius, List<Village> into = null)
        {
            EnsureVillageIndex();
            into = into ?? new List<Village>();
            int bx0 = Math.Max(0, (int)Math.Floor((x - radius) / BucketSize)), bx1 = Math.Min(BucketsAcross - 1, (int)Math.Floor((x + radius) / BucketSize));
            int by0 = Math.Max(0, (int)Math.Floor((y - radius) / BucketSize)), by1 = Math.Min(BucketsAcross - 1, (int)Math.Floor((y + radius) / BucketSize));
            double r2 = radius * radius;
            for (int by = by0; by <= by1; by++)
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    var bucket = villageBuckets[by * BucketsAcross + bx];
                    if (bucket == null) continue;
                    foreach (var v in bucket)
                        if ((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y) <= r2) into.Add(v);
                }
            return into;
        }

        /// <summary>
        /// The map is split into 10 × 10 continents of 25 × 25 fields, numbered like Tribal Wars': the tens digit
        /// counts up the map, the units across it (K00 in the corner at 0|0, K55 in the middle).
        /// </summary>
        public const int ContinentSize = 25;

        public static int ContinentOf(int x, int y) => Math.Max(0, Math.Min(9, y / ContinentSize)) * 10 + Math.Max(0, Math.Min(9, x / ContinentSize));

        /// <summary>For example "K55".</summary>
        public static string ContinentName(int x, int y) => $"K{ContinentOf(x, y):00}";

        /// <summary>Straight-line distance in map fields, as in Tribal Wars.</summary>
        public static double Distance(Village a, Village b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Game seconds for a unit (or an army whose slowest unit is this) to travel between two villages.</summary>
        public static double TravelSeconds(Village from, Village to, UnitType slowest) =>
            Distance(from, to) * Units.Get(slowest).MinutesPerField * 60;

        // ---------------------------------------------------------------- barbarian growth

        /// <summary>Game hours between a barbarian village's growth steps (each one picks its own wait in this range).</summary>
        public const double BarbarianGrowthMinHours = 8, BarbarianGrowthMaxHours = 24;

        /// <summary>How far barbarians grow each building on their own, and how likely each is to be picked.</summary>
        static readonly (BuildingType building, int cap, double weight)[] BarbarianGrowth =
        {
            (BuildingType.TimberCamp, 25, 3), (BuildingType.ClayPit, 25, 3), (BuildingType.IronMine, 25, 3),
            (BuildingType.Warehouse, 22, 2), (BuildingType.Farm, 20, 2), (BuildingType.Headquarters, 20, 1.5),
            (BuildingType.Barracks, 15, 1), (BuildingType.Wall, 15, 1), (BuildingType.Stable, 10, 0.5),
            (BuildingType.Workshop, 5, 0.25),
        };

        /// <summary>The highest level a barbarian village grows a building to by itself.</summary>
        public static int BarbarianGrowthCap(BuildingType type)
        {
            foreach (var g in BarbarianGrowth)
                if (g.building == type) return g.cap;
            return 0;
        }

        /// <summary>
        /// A repeatable random number for a village's growth: from the world seed, the village and its growth step,
        /// so growth is the same every time without saving any random-number state.
        /// </summary>
        double GrowthRandom(Village v, int salt) => Terrain.Hash(Settings.Seed ^ 0x5bd1e995, v.Id, v.GrowthSteps * 2 + salt);

        void ScheduleBarbarianGrowth(Village v)
        {
            double hours = BarbarianGrowthMinHours + (BarbarianGrowthMaxHours - BarbarianGrowthMinHours) * GrowthRandom(v, 0);
            Schedule(hours * 3600, EventKind.BarbarianGrowth, v.Id);
        }

        /// <summary>A barbarian village upgrades one building (weighted towards its economy), then waits for the next time.</summary>
        void GrowBarbarian(ScheduledEvent e)
        {
            var v = FindVillage(e.VillageId);
            if (v == null || !v.IsBarbarian) return; // conquered since: it stops growing on its own

            double total = 0;
            foreach (var g in BarbarianGrowth)
                if (v.Level(g.building) < g.cap) total += g.weight;
            if (total <= 0) return; // fully grown: no more growth events

            double pick = GrowthRandom(v, 1) * total;
            foreach (var g in BarbarianGrowth)
            {
                if (v.Level(g.building) >= g.cap) continue;
                pick -= g.weight;
                if (pick > 0) continue;
                Touch(v); // production up to now at the old levels
                v.Levels[(int)g.building]++;
                break;
            }
            v.GrowthSteps++;
            ScheduleBarbarianGrowth(v);
        }

        /// <param name="development">0 for a village barely started, 1 for a well-built one.</param>
        static void SetUpBarbarian(Village v, double development, Random rng)
        {
            v.SetUpAsNew();
            int Roll(int min, double max) => rng.Next(min, Math.Max(min, (int)Math.Round(max)) + 1);
            double size = 2 + development * 8;

            v.Levels[(int)BuildingType.Headquarters] = Roll(1, size * 0.5);
            v.Levels[(int)BuildingType.TimberCamp] = Roll(1, size);
            v.Levels[(int)BuildingType.ClayPit] = Roll(1, size);
            v.Levels[(int)BuildingType.IronMine] = Roll(1, size);
            v.Levels[(int)BuildingType.Farm] = Roll(1, size * 0.6);
            v.Levels[(int)BuildingType.Warehouse] = Roll(1, size * 0.8);
            v.Levels[(int)BuildingType.Wall] = rng.NextDouble() < development ? Roll(1, size * 0.4) : 0;
            // Some have military buildings too, which count for a lot of points.
            v.Levels[(int)BuildingType.Barracks] = rng.NextDouble() < 0.3 + development * 0.5 ? Roll(1, size * 0.5) : 0;
            v.Levels[(int)BuildingType.Stable] = rng.NextDouble() < development * 0.3 ? Roll(1, size * 0.3) : 0;
            v.Levels[(int)BuildingType.Workshop] = rng.NextDouble() < development * 0.1 ? 1 : 0;

            // Part-full storehouses: something worth raiding from the start.
            int cap = v.StorageCapacity;
            v.Wood = rng.NextDouble() * cap * 0.5;
            v.Clay = rng.NextDouble() * cap * 0.5;
            v.Iron = rng.NextDouble() * cap * 0.5;
            // No troops: as in Tribal Wars, barbarians never train any. Only the villagers (and the wall) defend.
        }
    }
}
