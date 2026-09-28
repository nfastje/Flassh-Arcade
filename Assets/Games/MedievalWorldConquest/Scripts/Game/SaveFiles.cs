using System;
using System.IO;
using MedievalWorldConquest.Simulation;
using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>Reads and writes the world save on disk (one save slot for now).</summary>
    static class SaveFiles
    {
        static string Folder => Path.Combine(Application.persistentDataPath, "MedievalWorldConquest");
        static string SavePath => Path.Combine(Folder, "world.json");

        public static bool Exists => File.Exists(SavePath);

        /// <summary>Writes to a temporary file first and then swaps it in, so a crash mid-save can't corrupt the save.</summary>
        public static void Save(World world)
        {
            Directory.CreateDirectory(Folder);
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, SaveGame.ToJson(world, DateTime.UtcNow));
            if (File.Exists(SavePath)) File.Replace(temp, SavePath, null);
            else File.Move(temp, SavePath);
        }

        /// <summary>Loads the save, or returns null with a reason if there isn't a readable one.</summary>
        public static World Load(out DateTime savedAtUtc, out string error)
        {
            savedAtUtc = default;
            error = null;
            if (!Exists)
            {
                error = "No saved world.";
                return null;
            }
            try
            {
                return SaveGame.FromJson(File.ReadAllText(SavePath), out savedAtUtc);
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
            {
                error = e.Message;
                return null;
            }
        }
    }
}
