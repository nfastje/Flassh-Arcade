using System;

namespace MedievalWorldConquest.Simulation
{
    public enum UnitType
    {
        Spearman = 0,
        Swordsman = 1,
        Axeman = 2,
        Archer = 3,
        Scout = 4,
        LightCavalry = 5,
        HeavyCavalry = 6,
        Ram = 7,
        Catapult = 8,
        Nobleman = 9,
        /// <summary>Added after the others (so older saves keep their numbering); listed after light cavalry.</summary>
        MountedArcher = 10,
    }

    /// <summary>What kind of attack a unit makes; defenders use their matching defence value against it (from Phase 4).</summary>
    public enum UnitClass
    {
        Infantry,
        Cavalry,
        Archer,
        Siege,
    }

    /// <summary>The fixed stats of one kind of unit.</summary>
    public class UnitDef
    {
        public UnitType Type;
        public string Name;
        public string Description;
        public UnitClass Class;
        /// <summary>The building that trains it, and the level that building needs.</summary>
        public BuildingType Building;
        public int RequiredLevel;
        /// <summary>Price of one unit, including the population it takes up for as long as it lives.</summary>
        public Cost Cost;
        /// <summary>Tribal Wars' base recruitment time ("build_time"), in game seconds; see <see cref="Units.SecondsToTrain"/>.</summary>
        public double BaseSeconds;
        public int Attack;
        public int DefenseInfantry, DefenseCavalry, DefenseArcher;
        /// <summary>Minutes to cross one map field (lower is faster). An army moves at its slowest unit's pace.</summary>
        public double MinutesPerField;
        /// <summary>How much loot one unit can carry home.</summary>
        public int Carry;
        /// <summary>
        /// What researching it at the smithy costs, and the buildings that needs, as in Tribal Wars' simple research.
        /// Spearmen and noblemen need no research (a zero cost).
        /// </summary>
        public Cost ResearchCost;
        public Requirement[] ResearchRequires = new Requirement[0];

        public bool NeedsResearch => ResearchCost.Wood + ResearchCost.Clay + ResearchCost.Iron > 0;
    }

    /// <summary>Every unit's stats, following Tribal Wars' classic balance.</summary>
    public static class Units
    {
        static readonly UnitDef[] All =
        {
            new UnitDef
            {
                Type = UnitType.Spearman, Name = "Spearman", Class = UnitClass.Infantry,
                Description = "Cheap, sturdy defender, especially strong against cavalry.",
                Building = BuildingType.Barracks, RequiredLevel = 1, Cost = new Cost(50, 30, 10, 1), BaseSeconds = 1020,
                Attack = 10, DefenseInfantry = 15, DefenseCavalry = 45, DefenseArcher = 20, MinutesPerField = 18, Carry = 25,
            },
            new UnitDef
            {
                Type = UnitType.Swordsman, Name = "Swordsman", Class = UnitClass.Infantry,
                Description = "Heavily armoured defender, strong against infantry. Slow on the march.",
                Building = BuildingType.Barracks, RequiredLevel = 1, Cost = new Cost(30, 30, 70, 1), BaseSeconds = 1500,
                Attack = 25, DefenseInfantry = 50, DefenseCavalry = 25, DefenseArcher = 40, MinutesPerField = 22, Carry = 15,
                ResearchCost = new Cost(900, 800, 780), ResearchRequires = new[] { new Requirement(BuildingType.Smithy, 1) },
            },
            new UnitDef
            {
                Type = UnitType.Axeman, Name = "Axeman", Class = UnitClass.Infantry,
                Description = "Hard-hitting attacker. Poor at defending.",
                Building = BuildingType.Barracks, RequiredLevel = 1, Cost = new Cost(60, 30, 40, 1), BaseSeconds = 1320,
                Attack = 40, DefenseInfantry = 10, DefenseCavalry = 5, DefenseArcher = 10, MinutesPerField = 18, Carry = 10,
                ResearchCost = new Cost(700, 840, 820), ResearchRequires = new[] { new Requirement(BuildingType.Smithy, 2) },
            },
            new UnitDef
            {
                Type = UnitType.Archer, Name = "Archer", Class = UnitClass.Archer,
                Description = "Defends well from behind walls, and attacks as archers, which few units defend against well.",
                Building = BuildingType.Barracks, RequiredLevel = 5, Cost = new Cost(100, 30, 60, 1), BaseSeconds = 1800,
                Attack = 15, DefenseInfantry = 50, DefenseCavalry = 40, DefenseArcher = 5, MinutesPerField = 18, Carry = 10,
                ResearchCost = new Cost(640, 560, 740), ResearchRequires = new[] { new Requirement(BuildingType.Barracks, 5), new Requirement(BuildingType.Smithy, 5) },
            },
            new UnitDef
            {
                Type = UnitType.Scout, Name = "Scout", Class = UnitClass.Cavalry,
                Description = "Fast rider that spies on other villages. Doesn't fight.",
                Building = BuildingType.Stable, RequiredLevel = 1, Cost = new Cost(50, 50, 20, 2), BaseSeconds = 900,
                Attack = 0, DefenseInfantry = 2, DefenseCavalry = 1, DefenseArcher = 2, MinutesPerField = 9, Carry = 0,
                ResearchCost = new Cost(560, 480, 480), ResearchRequires = new[] { new Requirement(BuildingType.Stable, 1) },
            },
            new UnitDef
            {
                Type = UnitType.LightCavalry, Name = "Light Cavalry", Class = UnitClass.Cavalry,
                Description = "Fast raider that carries off plenty of loot. Weak on defence.",
                Building = BuildingType.Stable, RequiredLevel = 3, Cost = new Cost(125, 100, 250, 4), BaseSeconds = 1800,
                Attack = 130, DefenseInfantry = 30, DefenseCavalry = 40, DefenseArcher = 30, MinutesPerField = 10, Carry = 80,
                ResearchCost = new Cost(2200, 2400, 2000), ResearchRequires = new[] { new Requirement(BuildingType.Stable, 3) },
            },
            new UnitDef
            {
                Type = UnitType.HeavyCavalry, Name = "Heavy Cavalry", Class = UnitClass.Cavalry,
                Description = "Expensive elite rider, strong at both attack and defence.",
                Building = BuildingType.Stable, RequiredLevel = 10, Cost = new Cost(200, 150, 600, 6), BaseSeconds = 3600,
                Attack = 150, DefenseInfantry = 200, DefenseCavalry = 80, DefenseArcher = 180, MinutesPerField = 11, Carry = 50,
                ResearchCost = new Cost(3000, 2400, 2000), ResearchRequires = new[] { new Requirement(BuildingType.Stable, 10), new Requirement(BuildingType.Smithy, 15) },
            },
            new UnitDef
            {
                Type = UnitType.Ram, Name = "Ram", Class = UnitClass.Siege,
                Description = "Batters down an enemy's wall during an attack. Very slow.",
                Building = BuildingType.Workshop, RequiredLevel = 1, Cost = new Cost(300, 200, 200, 5), BaseSeconds = 4800,
                Attack = 2, DefenseInfantry = 20, DefenseCavalry = 50, DefenseArcher = 20, MinutesPerField = 30, Carry = 0,
                ResearchCost = new Cost(1200, 1600, 800), ResearchRequires = new[] { new Requirement(BuildingType.Workshop, 1) },
            },
            new UnitDef
            {
                Type = UnitType.Catapult, Name = "Catapult", Class = UnitClass.Siege,
                Description = "Hurls stones to damage an enemy's buildings. Very slow.",
                Building = BuildingType.Workshop, RequiredLevel = 2, Cost = new Cost(320, 400, 100, 8), BaseSeconds = 7200,
                Attack = 100, DefenseInfantry = 100, DefenseCavalry = 50, DefenseArcher = 100, MinutesPerField = 30, Carry = 0,
                ResearchCost = new Cost(1600, 2000, 1200), ResearchRequires = new[] { new Requirement(BuildingType.Workshop, 2), new Requirement(BuildingType.Smithy, 12) },
            },
            new UnitDef
            {
                Type = UnitType.Nobleman, Name = "Nobleman", Class = UnitClass.Infantry,
                Description = "Wins villages over. Each one who survives a victorious attack lowers the village's loyalty by 20 to 35; at zero the village is yours. Very slow and very dear.",
                Building = BuildingType.Academy, RequiredLevel = 1, Cost = new Cost(20000, 25000, 20000, 100), BaseSeconds = 18000,
                Attack = 30, DefenseInfantry = 100, DefenseCavalry = 50, DefenseArcher = 100, MinutesPerField = 35, Carry = 0,
            },
            new UnitDef
            {
                // Tribal Wars' mounted archer: a fast rider that attacks as an archer, which few defenders stand up to.
                Type = UnitType.MountedArcher, Name = "Mounted Archer", Class = UnitClass.Archer,
                Description = "Fast rider who attacks as an archer, which spearmen and swordsmen defend against badly. Weak on defence.",
                Building = BuildingType.Stable, RequiredLevel = 5, Cost = new Cost(250, 100, 150, 5), BaseSeconds = 2700,
                Attack = 120, DefenseInfantry = 40, DefenseCavalry = 30, DefenseArcher = 50, MinutesPerField = 10, Carry = 50,
                ResearchCost = new Cost(3000, 2400, 2000), ResearchRequires = new[] { new Requirement(BuildingType.Stable, 5) },
            },
        };

        /// <summary>Every unit in the order Tribal Wars lists them: infantry, cavalry, siege, then the nobleman.</summary>
        public static readonly UnitType[] InDisplayOrder =
        {
            UnitType.Spearman, UnitType.Swordsman, UnitType.Axeman, UnitType.Archer, UnitType.Scout,
            UnitType.LightCavalry, UnitType.MountedArcher, UnitType.HeavyCavalry, UnitType.Ram, UnitType.Catapult,
            UnitType.Nobleman,
        };

        public static int Count => All.Length;

        public static UnitDef Get(UnitType type) => All[(int)type];

        public static UnitDef[] Definitions => (UnitDef[])All.Clone();

        /// <summary>The units a building trains, in list order.</summary>
        public static UnitDef[] TrainedAt(BuildingType building) =>
            Array.FindAll(Array.ConvertAll(InDisplayOrder, Get), u => u.Building == building);

        /// <summary>
        /// Game seconds to train one unit with its building at the given level, by Tribal Wars' formula:
        /// 2/3 × build_time × 1.06^(−building level). (The academy has one level, so it's always level 1.)
        /// </summary>
        public static double SecondsToTrain(UnitType type, int buildingLevel)
        {
            var u = Get(type);
            return 2.0 / 3.0 * u.BaseSeconds * Math.Pow(RecruitSpeedup, -Math.Max(1, buildingLevel));
        }

        /// <summary>Each level of a training building trains about 6% faster (Tribal Wars' 1.06).</summary>
        public const double RecruitSpeedup = 1.06;

        /// <summary>
        /// Game seconds to research a unit at a smithy of the given level: two seconds per resource it costs, about
        /// 5% quicker for each smithy level above the first.
        /// </summary>
        public static double ResearchSeconds(UnitType type, int smithyLevel)
        {
            var c = Get(type).ResearchCost;
            return 2.0 * (c.Wood + c.Clay + c.Iron) * Math.Pow(Buildings.ResearchSpeedup, -Math.Max(0, smithyLevel - 1));
        }

        /// <summary>The first requirement for researching a unit that the village hasn't met yet, if any.</summary>
        public static Requirement? UnmetResearchRequirement(UnitType type, Village v)
        {
            foreach (var r in Get(type).ResearchRequires)
                if (v.Level(r.Building) < r.Level) return r;
            return null;
        }
    }
}
