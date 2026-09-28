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
        /// <summary>Game seconds to train one with its building at level 1.</summary>
        public double BaseSeconds;
        public int Attack;
        public int DefenseInfantry, DefenseCavalry, DefenseArcher;
        /// <summary>Minutes to cross one map field (lower is faster). An army moves at its slowest unit's pace.</summary>
        public double MinutesPerField;
        /// <summary>How much loot one unit can carry home.</summary>
        public int Carry;
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
                Building = BuildingType.Barracks, RequiredLevel = 1, Cost = new Cost(50, 30, 10, 1), BaseSeconds = 680,
                Attack = 10, DefenseInfantry = 15, DefenseCavalry = 45, DefenseArcher = 20, MinutesPerField = 18, Carry = 25,
            },
            new UnitDef
            {
                Type = UnitType.Swordsman, Name = "Swordsman", Class = UnitClass.Infantry,
                Description = "Heavily armoured defender, strong against infantry. Slow on the march.",
                Building = BuildingType.Barracks, RequiredLevel = 1, Cost = new Cost(30, 30, 70, 1), BaseSeconds = 1000,
                Attack = 25, DefenseInfantry = 50, DefenseCavalry = 15, DefenseArcher = 40, MinutesPerField = 22, Carry = 15,
            },
            new UnitDef
            {
                Type = UnitType.Axeman, Name = "Axeman", Class = UnitClass.Infantry,
                Description = "Hard-hitting attacker. Poor at defending.",
                Building = BuildingType.Barracks, RequiredLevel = 2, Cost = new Cost(60, 30, 40, 1), BaseSeconds = 880,
                Attack = 40, DefenseInfantry = 10, DefenseCavalry = 5, DefenseArcher = 10, MinutesPerField = 18, Carry = 10,
            },
            new UnitDef
            {
                Type = UnitType.Archer, Name = "Archer", Class = UnitClass.Archer,
                Description = "Defends well from behind walls, and attacks as archers, which few units defend against well.",
                Building = BuildingType.Barracks, RequiredLevel = 5, Cost = new Cost(100, 30, 60, 1), BaseSeconds = 1200,
                Attack = 15, DefenseInfantry = 50, DefenseCavalry = 40, DefenseArcher = 5, MinutesPerField = 18, Carry = 10,
            },
            new UnitDef
            {
                Type = UnitType.Scout, Name = "Scout", Class = UnitClass.Cavalry,
                Description = "Fast rider that spies on other villages. Doesn't fight.",
                Building = BuildingType.Stable, RequiredLevel = 1, Cost = new Cost(50, 50, 20, 2), BaseSeconds = 600,
                Attack = 0, DefenseInfantry = 2, DefenseCavalry = 1, DefenseArcher = 2, MinutesPerField = 9, Carry = 0,
            },
            new UnitDef
            {
                Type = UnitType.LightCavalry, Name = "Light Cavalry", Class = UnitClass.Cavalry,
                Description = "Fast raider that carries off plenty of loot. Weak on defence.",
                Building = BuildingType.Stable, RequiredLevel = 3, Cost = new Cost(125, 100, 250, 4), BaseSeconds = 1200,
                Attack = 130, DefenseInfantry = 30, DefenseCavalry = 40, DefenseArcher = 30, MinutesPerField = 10, Carry = 80,
            },
            new UnitDef
            {
                Type = UnitType.HeavyCavalry, Name = "Heavy Cavalry", Class = UnitClass.Cavalry,
                Description = "Expensive elite rider, strong at both attack and defence.",
                Building = BuildingType.Stable, RequiredLevel = 10, Cost = new Cost(200, 150, 600, 6), BaseSeconds = 2400,
                Attack = 150, DefenseInfantry = 200, DefenseCavalry = 80, DefenseArcher = 180, MinutesPerField = 11, Carry = 50,
            },
            new UnitDef
            {
                Type = UnitType.Ram, Name = "Ram", Class = UnitClass.Siege,
                Description = "Batters down an enemy's wall during an attack. Very slow.",
                Building = BuildingType.Workshop, RequiredLevel = 1, Cost = new Cost(300, 200, 200, 5), BaseSeconds = 3200,
                Attack = 2, DefenseInfantry = 20, DefenseCavalry = 50, DefenseArcher = 20, MinutesPerField = 30, Carry = 0,
            },
            new UnitDef
            {
                Type = UnitType.Catapult, Name = "Catapult", Class = UnitClass.Siege,
                Description = "Hurls stones to damage an enemy's buildings. Very slow.",
                Building = BuildingType.Workshop, RequiredLevel = 2, Cost = new Cost(320, 400, 100, 8), BaseSeconds = 4800,
                Attack = 100, DefenseInfantry = 100, DefenseCavalry = 50, DefenseArcher = 100, MinutesPerField = 30, Carry = 0,
            },
        };

        public static int Count => All.Length;

        public static UnitDef Get(UnitType type) => All[(int)type];

        public static UnitDef[] Definitions => (UnitDef[])All.Clone();

        /// <summary>The units a building trains, in list order.</summary>
        public static UnitDef[] TrainedAt(BuildingType building) => Array.FindAll(All, u => u.Building == building);

        /// <summary>Game seconds to train one unit with its building at the given level.</summary>
        public static double SecondsToTrain(UnitType type, int buildingLevel)
        {
            var u = Get(type);
            double speedup = Buildings.Get(u.Building).TrainingSpeedup;
            return u.BaseSeconds / Math.Pow(speedup, Math.Max(0, buildingLevel - 1));
        }
    }
}
