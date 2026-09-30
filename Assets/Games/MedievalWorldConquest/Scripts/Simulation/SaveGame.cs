using System;
using UnityEngine;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>Turns a <see cref="World"/> into save-file text and back, and works out time missed while the game was closed.</summary>
    public static class SaveGame
    {
        /// <summary>Real-time worlds catch up at most this much, so a save left for months doesn't take ages to load.</summary>
        public static readonly double MaxCatchUpSeconds = TimeSpan.FromDays(30).TotalSeconds;

        [Serializable]
        class SaveFile
        {
            public int Version;
            public long SavedAtUtcTicks;
            public World World;
        }

        public static string ToJson(World world, DateTime savedAtUtc) =>
            JsonUtility.ToJson(new SaveFile { Version = World.CurrentVersion, SavedAtUtcTicks = savedAtUtc.Ticks, World = world }, true);

        /// <summary>Reads a save. Throws <see cref="FormatException"/> if the text isn't a readable save.</summary>
        public static World FromJson(string json, out DateTime savedAtUtc)
        {
            SaveFile file;
            try
            {
                file = JsonUtility.FromJson<SaveFile>(json);
            }
            catch (ArgumentException e)
            {
                throw new FormatException("The save file isn't valid JSON.", e);
            }
            // Unity's JSON reader fills in missing objects instead of leaving them null, so a real save is recognized
            // by its version number (always 1 or more) and by having players in its world.
            if (file == null || file.Version <= 0 || file.World == null || file.World.Players.Count == 0)
                throw new FormatException("This isn't a Medieval World Conquest save.");
            if (file.Version > World.CurrentVersion)
                throw new FormatException($"The save is from a newer version of the game (save version {file.Version}).");

            savedAtUtc = new DateTime(file.SavedAtUtcTicks, DateTimeKind.Utc);
            file.World.UpgradeFrom(file.Version);
            return file.World;
        }

        /// <summary>
        /// Real seconds the world should catch up on after loading: the time since it was saved for a real-time
        /// world (capped), or none for a world that pauses while closed. A clock that went backwards counts as none.
        /// </summary>
        public static double CatchUpRealSeconds(World world, DateTime savedAtUtc, DateTime nowUtc)
        {
            if (world.Settings.TimeMode != TimeMode.RealTime) return 0;
            double away = (nowUtc - savedAtUtc).TotalSeconds;
            return Math.Min(Math.Max(0, away), MaxCatchUpSeconds);
        }
    }
}
