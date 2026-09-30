using System;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>How the world's clock behaves while the game is closed.</summary>
    public enum TimeMode
    {
        /// <summary>The world keeps running while the game is closed; the missed time is simulated on load.</summary>
        RealTime = 0,
        /// <summary>Time only passes while the game is open.</summary>
        PausedWhenClosed = 1,
    }

    /// <summary>Options chosen when a world is created. They don't change during play.</summary>
    [Serializable]
    public class WorldSettings
    {
        /// <summary>Game seconds per real second. 1 is the pace of the original browser games.</summary>
        public float Speed = 5f;
        public TimeMode TimeMode = TimeMode.RealTime;
        public int Seed;
        /// <summary>Fraction of all villages the player must own to win.</summary>
        public float ConquestGoal = 0.6f;
        /// <summary>
        /// How thickly rival lords (computer players) settle as the world grows: 0 for none, 1 normal, 2 twice as
        /// many. There's no fixed number: new ones keep arriving as the world widens.
        /// </summary>
        public float RivalDensity = 1f;
        public AiSkill RivalSkill = AiSkill.Normal;
        /// <summary>The human player's name, shown on their villages.</summary>
        public string PlayerName = "";
        /// <summary>Game days at the start during which no player's villages can be attacked by another player.</summary>
        public float ProtectionDays = 3f;
        /// <summary>
        /// Whether noblemen need gold coins, as on Tribal Wars' coin worlds (dearer noblemen, and each one needs a
        /// noble slot bought with ever more coins), rather than a flat price. See <see cref="World.UnitCost"/>.
        /// </summary>
        public bool GoldCoins;
        /// <summary>
        /// Whether the world has tribes and diplomacy (a simulated MMO): lords band together, make pacts and war,
        /// and a bloc of allied tribes can win the world. Off, it's every lord for themselves.
        /// </summary>
        public bool Diplomacy;
    }

    /// <summary>How well the rival lords play.</summary>
    public enum AiSkill
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }
}
