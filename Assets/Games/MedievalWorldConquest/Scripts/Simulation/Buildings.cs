using System;

namespace MedievalWorldConquest.Simulation
{
    public enum BuildingType
    {
        TownHall = 0,
        TimberCamp = 1,
        ClayPit = 2,
        IronMine = 3,
        Farm = 4,
        Warehouse = 5,
        Barracks = 6,
        Stable = 7,
        Workshop = 8,
        Wall = 9,
    }

    /// <summary>A building that must reach a level before something else can be built or trained.</summary>
    public struct Requirement
    {
        public BuildingType Building;
        public int Level;

        public Requirement(BuildingType building, int level)
        {
            Building = building;
            Level = level;
        }
    }

    public enum ResourceType
    {
        Wood = 0,
        Clay = 1,
        Iron = 2,
    }

    /// <summary>An amount of wood, clay, iron and population, e.g. the price of a building level.</summary>
    [Serializable]
    public struct Cost
    {
        public int Wood, Clay, Iron, Population;

        public Cost(int wood, int clay, int iron, int population = 0)
        {
            Wood = wood;
            Clay = clay;
            Iron = iron;
            Population = population;
        }

        public int Get(ResourceType r) => r == ResourceType.Wood ? Wood : r == ResourceType.Clay ? Clay : Iron;
    }

    /// <summary>The fixed rules for one kind of building: its name, costs, build times and effects per level.</summary>
    public class BuildingDef
    {
        public BuildingType Type;
        public string Name;
        public string Description;
        public int MaxLevel;
        /// <summary>Level 1 price; each further level multiplies it by <see cref="CostFactor"/>.</summary>
        public int BaseWood, BaseClay, BaseIron;
        public double CostFactor;
        /// <summary>Game seconds to build level 1 with a level-1 Town Hall; each further level multiplies it by <see cref="Buildings.TimeFactor"/>.</summary>
        public double BaseSeconds;
        /// <summary>Population taken by level 1; each further level adds its own share, growing by <see cref="PopulationFactor"/>.</summary>
        public int BasePopulation;
        public double PopulationFactor;
        /// <summary>Buildings (and levels) needed before this can be built.</summary>
        public Requirement[] Requires = new Requirement[0];
        /// <summary>For training buildings: each level trains this much faster than the last.</summary>
        public double TrainingSpeedup = 1;
        /// <summary>Village points for level 1; each further level is worth <see cref="Buildings.PointsFactor"/> times the last.</summary>
        public int PointsBase;
        /// <summary>What a new village starts with.</summary>
        public int StartingLevel;
        /// <summary>Resource produced, for the timber camp, clay pit and iron mine.</summary>
        public ResourceType? Produces;
    }

    /// <summary>
    /// Every building's definition and the formulas behind them, modelled on Tribal Wars: exponential costs and
    /// times per level, production and capacities that grow geometrically, and a Town Hall that speeds up building.
    /// </summary>
    public static class Buildings
    {
        public const double TimeFactor = 1.2;               // each level takes 20% longer than the last
        public const double TownHallSpeedup = 1.05;         // each Town Hall level builds 5% faster
        public const double BaseProductionPerHour = 5;      // a village trickles this much of each resource even with no mine
        public const double ProductionLevel1 = 30;          // per hour, at world speed 1
        public const double ProductionFactor = 1.163118;
        public const double StorageLevel1 = 1000;
        public const double StorageFactor = 1.2294934;
        public const double FarmLevel1 = 240;
        public const double FarmFactor = 1.172103;

        static readonly BuildingDef[] All =
        {
            new BuildingDef
            {
                Type = BuildingType.TownHall, Name = "Town Hall",
                Description = "The heart of the village. Unlocks new buildings and speeds up all construction.",
                MaxLevel = 30, BaseWood = 90, BaseClay = 80, BaseIron = 70, CostFactor = 1.26, BaseSeconds = 90,
                BasePopulation = 5, PopulationFactor = 1.17, StartingLevel = 1, PointsBase = 10,
            },
            new BuildingDef
            {
                Type = BuildingType.TimberCamp, Name = "Timber Camp", Produces = ResourceType.Wood,
                Description = "Woodcutters fell the surrounding forest for timber.",
                MaxLevel = 30, BaseWood = 50, BaseClay = 60, BaseIron = 40, CostFactor = 1.26, BaseSeconds = 75,
                BasePopulation = 5, PopulationFactor = 1.155, StartingLevel = 1, PointsBase = 6,
            },
            new BuildingDef
            {
                Type = BuildingType.ClayPit, Name = "Clay Pit", Produces = ResourceType.Clay,
                Description = "Workers dig clay for bricks and pottery.",
                MaxLevel = 30, BaseWood = 65, BaseClay = 50, BaseIron = 40, CostFactor = 1.26, BaseSeconds = 75,
                BasePopulation = 10, PopulationFactor = 1.14, StartingLevel = 1, PointsBase = 6,
            },
            new BuildingDef
            {
                Type = BuildingType.IronMine, Name = "Iron Mine", Produces = ResourceType.Iron,
                Description = "Miners dig ore from the hills and smelt it into iron.",
                MaxLevel = 30, BaseWood = 75, BaseClay = 65, BaseIron = 70, CostFactor = 1.26, BaseSeconds = 90,
                BasePopulation = 10, PopulationFactor = 1.17, StartingLevel = 1, PointsBase = 6,
            },
            new BuildingDef
            {
                Type = BuildingType.Farm, Name = "Farm",
                Description = "Feeds the village. Raises the population limit for buildings (and, later, troops).",
                MaxLevel = 30, BaseWood = 45, BaseClay = 40, BaseIron = 30, CostFactor = 1.3, BaseSeconds = 100,
                BasePopulation = 0, PopulationFactor = 1, StartingLevel = 1, PointsBase = 5,
            },
            new BuildingDef
            {
                Type = BuildingType.Warehouse, Name = "Warehouse",
                Description = "Stores wood, clay and iron. Production stops when it's full.",
                MaxLevel = 30, BaseWood = 60, BaseClay = 50, BaseIron = 40, CostFactor = 1.265, BaseSeconds = 85,
                BasePopulation = 0, PopulationFactor = 1, StartingLevel = 1, PointsBase = 6,
            },
            new BuildingDef
            {
                Type = BuildingType.Barracks, Name = "Barracks",
                Description = "Trains infantry: spearmen, swordsmen, axemen and archers. Higher levels train faster and unlock more units.",
                MaxLevel = 25, BaseWood = 200, BaseClay = 170, BaseIron = 90, CostFactor = 1.26, BaseSeconds = 180,
                BasePopulation = 7, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 16, TrainingSpeedup = 1.06,
                Requires = new[] { new Requirement(BuildingType.TownHall, 3) },
            },
            new BuildingDef
            {
                Type = BuildingType.Stable, Name = "Stable",
                Description = "Trains cavalry: scouts, light cavalry and heavy cavalry. Higher levels train faster and unlock more units.",
                MaxLevel = 20, BaseWood = 270, BaseClay = 240, BaseIron = 260, CostFactor = 1.26, BaseSeconds = 360,
                BasePopulation = 8, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 20, TrainingSpeedup = 1.06,
                Requires = new[] { new Requirement(BuildingType.TownHall, 10), new Requirement(BuildingType.Barracks, 5) },
            },
            new BuildingDef
            {
                Type = BuildingType.Workshop, Name = "Workshop",
                Description = "Builds siege engines: rams to break walls and catapults to smash buildings.",
                MaxLevel = 15, BaseWood = 300, BaseClay = 240, BaseIron = 260, CostFactor = 1.26, BaseSeconds = 450,
                BasePopulation = 8, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 24, TrainingSpeedup = 1.06,
                Requires = new[] { new Requirement(BuildingType.TownHall, 10), new Requirement(BuildingType.Barracks, 10) },
            },
            new BuildingDef
            {
                Type = BuildingType.Wall, Name = "Wall",
                Description = "A palisade, and later a stone wall, around the village. Each level makes defenders fight harder.",
                MaxLevel = 20, BaseWood = 50, BaseClay = 100, BaseIron = 20, CostFactor = 1.26, BaseSeconds = 60,
                BasePopulation = 5, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 8,
                Requires = new[] { new Requirement(BuildingType.Barracks, 1) },
            },
        };

        /// <summary>Each wall level makes defenders about 3.7% stronger (so level 20 roughly doubles them).</summary>
        public const double WallBonusPerLevel = 1.037;

        /// <summary>Multiplier on defenders' strength from a wall of this level (1 with no wall).</summary>
        public static double WallDefenseMultiplier(int level) => Math.Pow(WallBonusPerLevel, Math.Max(0, level));

        /// <summary>
        /// The lowest level catapults can knock a building down to. A village always keeps its Town Hall, farm and
        /// warehouse, as in Tribal Wars; everything else can be razed.
        /// </summary>
        public static int LowestLevel(BuildingType type) =>
            type == BuildingType.TownHall || type == BuildingType.Farm || type == BuildingType.Warehouse ? 1 : 0;

        /// <summary>Each level of a building is worth this many times the previous one in village points.</summary>
        public const double PointsFactor = 1.2;

        /// <summary>Points gained by building this level (the step up from the level below).</summary>
        public static int PointsOfLevel(BuildingType type, int level) =>
            level <= 0 ? 0 : PointsAtLevel(type, level) - PointsAtLevel(type, level - 1);

        // Points per building and level, worked out once: villages' points are summed often (the map shows
        // hundreds of them).
        static int[][] pointsTable;

        /// <summary>
        /// A building's contribution to its village's points, exactly as in Tribal Wars: its base value at level 1,
        /// times 1.2 for each level above that. Points are a rough measure of how developed, and so how strong, a
        /// village is.
        /// </summary>
        public static int PointsAtLevel(BuildingType type, int level)
        {
            if (level <= 0) return 0;
            if (pointsTable == null)
            {
                var table = new int[All.Length][];
                foreach (var d in All)
                {
                    var row = new int[d.MaxLevel + 1];
                    for (int l = 1; l <= d.MaxLevel; l++) row[l] = PointsFormula(d, l);
                    table[(int)d.Type] = row;
                }
                pointsTable = table;
            }
            var levels = pointsTable[(int)type];
            return level < levels.Length ? levels[level] : PointsFormula(Get(type), level);
        }

        static int PointsFormula(BuildingDef d, int level) => (int)Math.Round(d.PointsBase * Math.Pow(PointsFactor, level - 1));

        /// <summary>The first requirement the village hasn't met yet, if any.</summary>
        public static Requirement? UnmetRequirement(BuildingType type, Village v)
        {
            foreach (var r in Get(type).Requires)
                if (v.Level(r.Building) < r.Level) return r;
            return null;
        }

        public static int Count => All.Length;

        public static BuildingDef Get(BuildingType type) => All[(int)type];

        public static BuildingDef[] Definitions => (BuildingDef[])All.Clone();

        /// <summary>Price of upgrading to <paramref name="level"/> (population is that level's share only).</summary>
        public static Cost CostOf(BuildingType type, int level)
        {
            var d = Get(type);
            double f = Math.Pow(d.CostFactor, level - 1);
            return new Cost((int)Math.Round(d.BaseWood * f), (int)Math.Round(d.BaseClay * f), (int)Math.Round(d.BaseIron * f), PopulationOfLevel(type, level));
        }

        /// <summary>Population taken by one level of a building.</summary>
        public static int PopulationOfLevel(BuildingType type, int level)
        {
            var d = Get(type);
            return level <= 0 ? 0 : (int)Math.Round(d.BasePopulation * Math.Pow(d.PopulationFactor, level - 1));
        }

        /// <summary>Population taken by a building at <paramref name="level"/> (all its levels together).</summary>
        public static int PopulationAtLevel(BuildingType type, int level)
        {
            int total = 0;
            for (int l = 1; l <= level; l++) total += PopulationOfLevel(type, l);
            return total;
        }

        /// <summary>Game seconds to build <paramref name="level"/> with the given Town Hall level.</summary>
        public static double BuildSeconds(BuildingType type, int level, int townHallLevel) =>
            Get(type).BaseSeconds * Math.Pow(TimeFactor, level - 1) / Math.Pow(TownHallSpeedup, Math.Max(0, townHallLevel - 1));

        // Production and capacities by level, worked out once: every village's stores are topped up between every
        // pair of events, so these are looked up very often.
        const int TabulatedLevels = 41;
        static readonly double[] productionTable = Tabulate(l => l <= 0 ? BaseProductionPerHour : ProductionLevel1 * Math.Pow(ProductionFactor, l - 1));
        static readonly double[] storageTable = Tabulate(l => Math.Round(StorageLevel1 * Math.Pow(StorageFactor, Math.Max(0, l - 1))));
        static readonly double[] farmTable = Tabulate(l => Math.Round(FarmLevel1 * Math.Pow(FarmFactor, Math.Max(0, l - 1))));

        static double[] Tabulate(Func<int, double> f)
        {
            var table = new double[TabulatedLevels];
            for (int l = 0; l < TabulatedLevels; l++) table[l] = f(l);
            return table;
        }

        static double Lookup(double[] table, int level) => table[Math.Max(0, Math.Min(TabulatedLevels - 1, level))];

        /// <summary>Resources per hour (at world speed 1) from a mine of this level.</summary>
        public static double ProductionPerHour(int level) => Lookup(productionTable, level);

        /// <summary>How much of each resource a warehouse of this level holds.</summary>
        public static int StorageCapacity(int level) => (int)Lookup(storageTable, level);

        /// <summary>Population limit from a farm of this level.</summary>
        public static int FarmCapacity(int level) => (int)Lookup(farmTable, level);
    }
}
