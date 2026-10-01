using System;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// What a scheduled event does when its time comes. Later phases add kinds such as a building finishing,
    /// troops being recruited, or an army arriving.
    /// </summary>
    public enum EventKind
    {
        None = 0,
        /// <summary>A building upgrade finishes. A = the build order's id.</summary>
        BuildingComplete = 1,
        /// <summary>One unit of a recruitment batch finishes training. A = the recruit order's id.</summary>
        UnitTrained = 2,
        /// <summary>A barbarian village upgrades one of its buildings on its own.</summary>
        BarbarianGrowth = 3,
        /// <summary>Troops on the march arrive (an attack lands, support arrives, or troops get home). A = the command's id.</summary>
        CommandArrives = 4,
        /// <summary>A rival lord takes its turn: builds, recruits, raids and attacks. A = the player's id.</summary>
        AiThink = 5,
        /// <summary>The settled circle widens a step, and new barbarians and lords appear round its edge.</summary>
        WorldGrowth = 6,
        /// <summary>A unit's research at the smithy finishes. A = the research order's id.</summary>
        ResearchComplete = 7,
        /// <summary>A market offer runs out: its reserved resources go back to its village. A = the offer's id.</summary>
        OfferExpires = 8,
        /// <summary>An inactive player's village builds one more level on its own.</summary>
        InactiveGrowth = 9,
        /// <summary>An inactive player's account is closed: their villages go barbarian. A = the player's id.</summary>
        InactiveLeaves = 10,
        /// <summary>Twice a game day on a diplomacy world: tribes form, recruit, make pacts and war, and strain or break.</summary>
        TribeTick = 11,
        /// <summary>Every quarter of a game hour: the Account Manager's round of the player's villages.</summary>
        ManagerTick = 12,
    }

    /// <summary>
    /// Something that will happen at a set game time. A plain data record (a kind plus a few numbers) rather than
    /// a class hierarchy, so the whole queue saves and loads as simple JSON.
    /// </summary>
    [Serializable]
    public struct ScheduledEvent
    {
        /// <summary>Game time, in seconds since the world began.</summary>
        public double Time;
        /// <summary>Order of scheduling; breaks ties so events at the same time run in the order they were added.</summary>
        public long Sequence;
        public EventKind Kind;
        public int VillageId;
        /// <summary>Kind-specific values, e.g. a building type and level.</summary>
        public int A, B;
    }
}
