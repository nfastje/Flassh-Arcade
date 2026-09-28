using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>A queued building upgrade. Only the first order in a village's queue is under construction.</summary>
    [Serializable]
    public class BuildOrder
    {
        public int Id;
        public BuildingType Type;
        /// <summary>The level this order builds.</summary>
        public int Level;
        /// <summary>Game seconds it takes, fixed when queued (using the Town Hall level at that time).</summary>
        public double Seconds;
        /// <summary>When it finishes, once construction has started; 0 while still waiting its turn.</summary>
        public double FinishTime;
        /// <summary>What was paid, refunded if cancelled.</summary>
        public Cost Paid;

        public bool Started => FinishTime > 0;
    }

    /// <summary>
    /// A batch of units being trained. Each training building works through its own orders in turn, one unit at a
    /// time; units join the garrison as they finish.
    /// </summary>
    [Serializable]
    public class RecruitOrder
    {
        public int Id;
        public UnitType Unit;
        /// <summary>The building whose queue this is in.</summary>
        public BuildingType Building;
        public int Total;
        public int Done;
        /// <summary>Game seconds per unit, fixed when ordered (using the building's level at that time).</summary>
        public double SecondsEach;
        /// <summary>When the next unit finishes, once this order is being worked on; 0 while waiting its turn.</summary>
        public double NextAt;

        public int Remaining => Total - Done;
        public bool Started => NextAt > 0;
        /// <summary>When the whole batch will be done: from its next unit if started, otherwise as if it started at <paramref name="now"/>.</summary>
        public double FinishTime(double now) => Started ? NextAt + (Remaining - 1) * SecondsEach : now + Remaining * SecondsEach;
    }

    [Serializable]
    public class Village
    {
        public int Id;
        public string Name;
        /// <summary>Position on the world map grid.</summary>
        public int X, Y;
        /// <summary>Owning player's id, or -1 for an ownerless (barbarian) village.</summary>
        public int OwnerId;

        /// <summary>Current level of each building, indexed by <see cref="BuildingType"/>.</summary>
        public int[] Levels = new int[Buildings.Count];
        public double Wood, Clay, Iron;
        /// <summary>
        /// The game time the stores above were last brought up to date. Production is added when something needs
        /// the stock (see <see cref="World.Touch"/>), not at every event, which keeps big worlds fast.
        /// </summary>
        public double StockTime;
        public List<BuildOrder> Queue = new List<BuildOrder>();
        public int NextOrderId = 1;
        /// <summary>For barbarian villages: how many times they've grown on their own (drives which building grows next).</summary>
        public int GrowthSteps;

        /// <summary>Troops at home, indexed by <see cref="UnitType"/>.</summary>
        public int[] Troops = new int[Units.Count];
        /// <summary>All training orders, in the order they were placed (each building works through its own in turn).</summary>
        public List<RecruitOrder> Recruitment = new List<RecruitOrder>();

        public int TroopCount(UnitType type) => Troops != null && (int)type < Troops.Length ? Troops[(int)type] : 0;

        /// <summary>Troops from other villages stationed here to help defend it.</summary>
        public List<SupportGroup> Supports = new List<SupportGroup>();
        /// <summary>
        /// Population of this village's troops that are away (marching, or supporting another village). They still
        /// count against this village's farm, as in Tribal Wars.
        /// </summary>
        public int AwayPopulation;

        public int Level(BuildingType type) => Levels != null && (int)type < Levels.Length ? Levels[(int)type] : 0;

        public double Stock(ResourceType r) => r == ResourceType.Wood ? Wood : r == ResourceType.Clay ? Clay : Iron;

        public void SetStock(ResourceType r, double amount)
        {
            if (r == ResourceType.Wood) Wood = amount;
            else if (r == ResourceType.Clay) Clay = amount;
            else Iron = amount;
        }

        /// <summary>How developed the village is: the sum of every building's points.</summary>
        public int Points
        {
            get
            {
                int points = 0;
                for (int i = 0; i < Buildings.Count; i++) points += Buildings.PointsAtLevel((BuildingType)i, Level((BuildingType)i));
                return points;
            }
        }

        public bool IsBarbarian => OwnerId < 0;

        public int StorageCapacity => Buildings.StorageCapacity(Level(BuildingType.Warehouse));

        public int PopulationCapacity => Buildings.FarmCapacity(Level(BuildingType.Farm));

        /// <summary>
        /// Population taken by buildings (counting queued levels, whose workers are already hired) and by troops
        /// (counting those still in training, who are already paid for).
        /// </summary>
        public int PopulationUsed => BuildingPopulation + TroopPopulation;

        public int BuildingPopulation
        {
            get
            {
                int used = 0;
                for (int i = 0; i < Buildings.Count; i++)
                {
                    var type = (BuildingType)i;
                    used += Buildings.PopulationAtLevel(type, Level(type) + QueuedCount(type));
                }
                return used;
            }
        }

        public int TroopPopulation
        {
            get
            {
                int used = 0;
                for (int i = 0; i < Units.Count; i++) used += TroopCount((UnitType)i) * Units.Get((UnitType)i).Cost.Population;
                foreach (var o in Recruitment) used += o.Remaining * Units.Get(o.Unit).Cost.Population;
                return used + AwayPopulation;
            }
        }

        public int FreePopulation => Math.Max(0, PopulationCapacity - PopulationUsed);

        public static BuildingType MineFor(ResourceType r) =>
            r == ResourceType.Wood ? BuildingType.TimberCamp : r == ResourceType.Clay ? BuildingType.ClayPit : BuildingType.IronMine;

        /// <summary>Resources per hour of game time.</summary>
        public double ProductionPerHour(ResourceType r) => Buildings.ProductionPerHour(Level(MineFor(r)));

        public int QueuedCount(BuildingType type)
        {
            int n = 0;
            foreach (var o in Queue)
                if (o.Type == type) n++;
            return n;
        }

        /// <summary>The level the next upgrade of this building would build (after anything already queued).</summary>
        public int NextLevel(BuildingType type) => Level(type) + QueuedCount(type) + 1;

        public bool CanAfford(Cost cost) => Wood >= cost.Wood && Clay >= cost.Clay && Iron >= cost.Iron;

        /// <summary>Sets up a brand-new village: starting buildings and resources.</summary>
        public void SetUpAsNew()
        {
            Levels = new int[Buildings.Count];
            foreach (var d in Buildings.Definitions) Levels[(int)d.Type] = d.StartingLevel;
            Wood = Clay = Iron = 500;
            Queue = new List<BuildOrder>();
            NextOrderId = 1;
            Troops = new int[Units.Count];
            Recruitment = new List<RecruitOrder>();
            Supports = new List<SupportGroup>();
            AwayPopulation = 0;
        }
    }
}
