using System;

namespace MedievalWorldConquest.Simulation
{
    public enum BuildingType
    {
        Headquarters = 0,
        TimberCamp = 1,
        ClayPit = 2,
        IronMine = 3,
        Farm = 4,
        Warehouse = 5,
        Barracks = 6,
        Stable = 7,
        Workshop = 8,
        Wall = 9,
        Academy = 10,
        RallyPoint = 11,
        Smithy = 12,
        Market = 13,
        HidingPlace = 14,
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
        /// <summary>Level 1 price; each further level multiplies each resource by its own factor, as in Tribal Wars.</summary>
        public int BaseWood, BaseClay, BaseIron;
        public double WoodFactor, ClayFactor, IronFactor;
        /// <summary>Tribal Wars' base build time ("build_time"), in game seconds; see <see cref="Buildings.BuildSeconds"/>.</summary>
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
    /// times per level, production and capacities that grow geometrically, and a Headquarters that speeds up building.
    /// </summary>
    public static class Buildings
    {
        public const double TimeFactor = 1.2;               // each level takes 20% longer than the last
        public const double HeadquartersSpeedup = 1.05;         // each Headquarters level builds 5% faster
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
                Type = BuildingType.Headquarters, Name = "Headquarters",
                Description = "The heart of the village. Unlocks new buildings and speeds up all construction.",
                MaxLevel = 30, BaseWood = 90, BaseClay = 80, BaseIron = 70, WoodFactor = 1.26, ClayFactor = 1.275, IronFactor = 1.26, BaseSeconds = 900,
                BasePopulation = 5, PopulationFactor = 1.17, StartingLevel = 1, PointsBase = 10,
            },
            new BuildingDef
            {
                Type = BuildingType.TimberCamp, Name = "Timber Camp", Produces = ResourceType.Wood,
                Description = "Woodcutters fell the surrounding forest for timber.",
                MaxLevel = 30, BaseWood = 50, BaseClay = 60, BaseIron = 40, WoodFactor = 1.25, ClayFactor = 1.275, IronFactor = 1.245, BaseSeconds = 900,
                BasePopulation = 5, PopulationFactor = 1.155, StartingLevel = 1, PointsBase = 6,
            },
            new BuildingDef
            {
                Type = BuildingType.ClayPit, Name = "Clay Pit", Produces = ResourceType.Clay,
                Description = "Workers dig clay for bricks and pottery.",
                MaxLevel = 30, BaseWood = 65, BaseClay = 50, BaseIron = 40, WoodFactor = 1.27, ClayFactor = 1.265, IronFactor = 1.24, BaseSeconds = 900,
                BasePopulation = 10, PopulationFactor = 1.14, StartingLevel = 1, PointsBase = 6,
            },
            new BuildingDef
            {
                Type = BuildingType.IronMine, Name = "Iron Mine", Produces = ResourceType.Iron,
                Description = "Miners dig ore from the hills and smelt it into iron.",
                MaxLevel = 30, BaseWood = 75, BaseClay = 65, BaseIron = 70, WoodFactor = 1.252, ClayFactor = 1.275, IronFactor = 1.24, BaseSeconds = 1080,
                BasePopulation = 10, PopulationFactor = 1.17, StartingLevel = 1, PointsBase = 6,
            },
            new BuildingDef
            {
                Type = BuildingType.Farm, Name = "Farm",
                Description = "Feeds the village. Raises the population limit for buildings (and, later, troops).",
                MaxLevel = 30, BaseWood = 45, BaseClay = 40, BaseIron = 30, WoodFactor = 1.3, ClayFactor = 1.32, IronFactor = 1.29, BaseSeconds = 1200,
                BasePopulation = 0, PopulationFactor = 1, StartingLevel = 1, PointsBase = 5,
            },
            new BuildingDef
            {
                Type = BuildingType.Warehouse, Name = "Warehouse",
                Description = "Stores wood, clay and iron. Production stops when it's full.",
                MaxLevel = 30, BaseWood = 60, BaseClay = 50, BaseIron = 40, WoodFactor = 1.265, ClayFactor = 1.27, IronFactor = 1.245, BaseSeconds = 1020,
                BasePopulation = 0, PopulationFactor = 1, StartingLevel = 1, PointsBase = 6,
            },
            new BuildingDef
            {
                Type = BuildingType.Barracks, Name = "Barracks",
                Description = "Trains infantry: spearmen, swordsmen, axemen and archers (each researched at the smithy first, except spearmen). Higher levels train faster.",
                MaxLevel = 25, BaseWood = 200, BaseClay = 170, BaseIron = 90, WoodFactor = 1.26, ClayFactor = 1.28, IronFactor = 1.26, BaseSeconds = 1800,
                BasePopulation = 7, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 16, TrainingSpeedup = 1.06,
                Requires = new[] { new Requirement(BuildingType.Headquarters, 3) },
            },
            new BuildingDef
            {
                Type = BuildingType.Stable, Name = "Stable",
                Description = "Trains cavalry: scouts, light cavalry, mounted archers and heavy cavalry, once researched at the smithy. Higher levels train faster.",
                MaxLevel = 20, BaseWood = 270, BaseClay = 240, BaseIron = 260, WoodFactor = 1.26, ClayFactor = 1.28, IronFactor = 1.26, BaseSeconds = 6000,
                BasePopulation = 8, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 20, TrainingSpeedup = 1.06,
                Requires = new[] { new Requirement(BuildingType.Headquarters, 10), new Requirement(BuildingType.Barracks, 5), new Requirement(BuildingType.Smithy, 5) },
            },
            new BuildingDef
            {
                Type = BuildingType.Workshop, Name = "Workshop",
                Description = "Builds siege engines, once researched at the smithy: rams to break walls and catapults to smash buildings.",
                MaxLevel = 15, BaseWood = 300, BaseClay = 240, BaseIron = 260, WoodFactor = 1.26, ClayFactor = 1.28, IronFactor = 1.26, BaseSeconds = 6000,
                BasePopulation = 8, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 24, TrainingSpeedup = 1.06,
                Requires = new[] { new Requirement(BuildingType.Headquarters, 10), new Requirement(BuildingType.Smithy, 10) },
            },
            new BuildingDef
            {
                Type = BuildingType.Wall, Name = "Wall",
                Description = "A palisade, and later a stone wall, around the village. Each level makes defenders fight harder.",
                MaxLevel = 20, BaseWood = 50, BaseClay = 100, BaseIron = 20, WoodFactor = 1.26, ClayFactor = 1.275, IronFactor = 1.26, BaseSeconds = 3600,
                BasePopulation = 5, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 8,
                Requires = new[] { new Requirement(BuildingType.Barracks, 1) },
            },
            new BuildingDef
            {
                // As in Tribal Wars (coin worlds): one level only, 15,000 wood, 25,000 clay and 10,000 iron, a very long
                // build, 512 points, and Headquarters 20, Smithy 20 and Market 10.
                Type = BuildingType.Academy, Name = "Academy",
                Description = "Trains noblemen, who win villages over: each one who survives a victorious attack lowers the village's loyalty, and at zero it's yours.",
                MaxLevel = 1, BaseWood = 15000, BaseClay = 25000, BaseIron = 10000, WoodFactor = 2, ClayFactor = 2, IronFactor = 2, BaseSeconds = 586994,
                BasePopulation = 80, PopulationFactor = 1, StartingLevel = 0, PointsBase = 512,
                Requires = new[] { new Requirement(BuildingType.Headquarters, 20), new Requirement(BuildingType.Smithy, 20), new Requirement(BuildingType.Market, 10) },
            },
            new BuildingDef
            {
                // As in Tribal Wars: every village starts with one; a single level, no workers, no points.
                Type = BuildingType.RallyPoint, Name = "Rally Point",
                Description = "Where your troops muster: send attacks and support from here, and follow your troops on the march.",
                MaxLevel = 1, BaseWood = 10, BaseClay = 40, BaseIron = 30, WoodFactor = 1.26, ClayFactor = 1.275, IronFactor = 1.26, BaseSeconds = 10860,
                BasePopulation = 0, PopulationFactor = 1, StartingLevel = 1, PointsBase = 0,
            },
            // The three below are Tribal Wars' own figures (build_time, costs and factors, population, points, requirements).
            new BuildingDef
            {
                Type = BuildingType.Smithy, Name = "Smithy",
                Description = "Blacksmiths forge the weapons for new kinds of troops: research a unit here before you can train it. Higher levels research faster and unlock more.",
                MaxLevel = 20, BaseWood = 220, BaseClay = 180, BaseIron = 240, WoodFactor = 1.26, ClayFactor = 1.275, IronFactor = 1.26, BaseSeconds = 6000,
                BasePopulation = 20, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 19,
                Requires = new[] { new Requirement(BuildingType.Headquarters, 5), new Requirement(BuildingType.Barracks, 1) },
            },
            new BuildingDef
            {
                Type = BuildingType.Market, Name = "Market",
                Description = "Merchants carry resources between villages, 1,000 each: send them to your other villages, or trade with other lords.",
                MaxLevel = 25, BaseWood = 100, BaseClay = 100, BaseIron = 100, WoodFactor = 1.26, ClayFactor = 1.275, IronFactor = 1.26, BaseSeconds = 2700,
                BasePopulation = 20, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 10,
                Requires = new[] { new Requirement(BuildingType.Headquarters, 3), new Requirement(BuildingType.Warehouse, 2) },
            },
            new BuildingDef
            {
                Type = BuildingType.HidingPlace, Name = "Hiding Place",
                Description = "A secret cellar. Raiders can't find the resources hidden in it: they only carry off what's stored above that.",
                MaxLevel = 10, BaseWood = 50, BaseClay = 60, BaseIron = 50, WoodFactor = 1.25, ClayFactor = 1.25, IronFactor = 1.25, BaseSeconds = 1800,
                BasePopulation = 2, PopulationFactor = 1.17, StartingLevel = 0, PointsBase = 5,
            },
        };

        /// <summary>Tribal Wars' hiding place: a third more each level, from 150 at level 1 to 2,000 at 10.</summary>
        static readonly int[] HiddenByLevel = { 0, 150, 200, 267, 356, 474, 632, 843, 1125, 1500, 2000 };

        /// <summary>How much of each resource a hiding place of this level hides from plunderers.</summary>
        public static int HiddenCapacity(int level) => HiddenByLevel[Math.Max(0, Math.Min(HiddenByLevel.Length - 1, level))];

        /// <summary>Tribal Wars' merchants per market level: one a level up to 10, then ever more, to 235 at level 25.</summary>
        static readonly int[] MerchantsByLevel = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 14, 19, 26, 35, 46, 59, 74, 91, 110, 131, 154, 179, 206, 235 };

        /// <summary>How many merchants a market of this level keeps.</summary>
        public static int Merchants(int level) => MerchantsByLevel[Math.Max(0, Math.Min(MerchantsByLevel.Length - 1, level))];

        /// <summary>How much one merchant carries.</summary>
        public const int MerchantCarry = 1000;

        /// <summary>Each smithy level researches this much faster.</summary>
        public const double ResearchSpeedup = 1.05;

        /// <summary>Each wall level makes defenders about 3.7% stronger (so level 20 roughly doubles them).</summary>
        public const double WallBonusPerLevel = 1.037;

        /// <summary>Multiplier on defenders' strength from a wall of this level (1 with no wall).</summary>
        public static double WallDefenseMultiplier(int level) => Math.Pow(WallBonusPerLevel, Math.Max(0, level));

        /// <summary>
        /// The lowest level catapults can knock a building down to. A village always keeps its Headquarters, farm and
        /// warehouse, as in Tribal Wars; everything else can be razed.
        /// </summary>
        public static int LowestLevel(BuildingType type) =>
            type == BuildingType.Headquarters || type == BuildingType.Farm || type == BuildingType.Warehouse ? 1 : 0;

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
            int Price(int basePrice, double factor) => (int)Math.Round(basePrice * Math.Pow(factor, level - 1));
            return new Cost(Price(d.BaseWood, d.WoodFactor), Price(d.BaseClay, d.ClayFactor), Price(d.BaseIron, d.IronFactor), PopulationOfLevel(type, level));
        }

        /// <summary>The extra population an upgrade to <paramref name="level"/> takes (the step up from the level below).</summary>
        public static int PopulationOfLevel(BuildingType type, int level) =>
            level <= 0 ? 0 : PopulationAtLevel(type, level) - PopulationAtLevel(type, level - 1);

        /// <summary>
        /// Population a building at <paramref name="level"/> takes in all, as in Tribal Wars: its base value at level 1,
        /// times its factor for each level above that (the total at that level, not a sum over the levels).
        /// </summary>
        public static int PopulationAtLevel(BuildingType type, int level)
        {
            var d = Get(type);
            return level <= 0 ? 0 : (int)Math.Round(d.BasePopulation * Math.Pow(d.PopulationFactor, level - 1));
        }

        /// <summary>
        /// Game seconds to build <paramref name="level"/> with the given Headquarters level: Tribal Wars' formula.
        /// The first two levels are quick; from level 3 on, times grow by about 20% a level; and each Headquarters
        /// level cuts every build by about 5%:
        /// build_time × 1.18 × 1.2^(level − 1 − 14/(level − 1)) × 1.05^(−Headquarters level), with levels 1 and 2 using
        /// 1.2^(−13).
        /// </summary>
        public static double BuildSeconds(BuildingType type, int level, int headquartersLevel)
        {
            double exponent = level <= 2 ? -13 : level - 1 - 14.0 / (level - 1);
            return Get(type).BaseSeconds * 1.18 * Math.Pow(TimeFactor, exponent) * Math.Pow(HeadquartersSpeedup, -Math.Max(0, headquartersLevel));
        }

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
