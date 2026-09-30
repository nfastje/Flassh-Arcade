using System;
using System.IO;
using MedievalWorldConquest.Simulation;
using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>
    /// A few lines about a saved world, kept in a small file beside it so the start screen can list every slot
    /// without loading whole worlds.
    /// </summary>
    [Serializable]
    public class SaveSummary
    {
        public string PlayerName, VillageName, Skill;
        public int Day, Villages, Points, Rank, Lords, Seed;
        public float Speed;
        public TimeMode TimeMode;
        public bool GoldCoins, Diplomacy, Won, Lost;
        public long SavedAtUtcTicks;

        public static SaveSummary Of(World world, DateTime savedAtUtc)
        {
            var human = world.HumanPlayer;
            var rankings = world.Rankings();
            return new SaveSummary
            {
                PlayerName = human?.Name ?? World.DefaultPlayerName,
                VillageName = world.PlayerVillage?.Name ?? "",
                Skill = world.Settings.RivalSkill.ToString(),
                Day = World.DayOf(world.Now),
                Villages = world.HumanVillages().Count,
                Points = human != null ? world.PointsOf(human) : 0,
                Rank = rankings.FindIndex(r => r.Player.IsHuman) + 1,
                Lords = rankings.Count,
                Seed = world.Settings.Seed,
                Speed = world.Settings.Speed,
                TimeMode = world.Settings.TimeMode,
                GoldCoins = world.Settings.GoldCoins,
                Diplomacy = world.Settings.Diplomacy,
                Won = world.Won,
                Lost = world.LostToBloc,
                SavedAtUtcTicks = savedAtUtc.Ticks,
            };
        }
    }

    /// <summary>Reads and writes the world saves on disk: <see cref="Slots"/> slots, each a world of its own.</summary>
    static class SaveFiles
    {
        /// <summary>How many worlds a player can keep at once.</summary>
        public const int Slots = 3;

        static string Folder => Path.Combine(Application.persistentDataPath, "MedievalWorldConquest");
        // (The first slot keeps the name the single save always had, so an existing world turns up in it.)
        static string SavePath(int slot) => Path.Combine(Folder, slot == 0 ? "world.json" : $"world{slot + 1}.json");
        static string SummaryPath(int slot) => SavePath(slot) + ".summary";

        public static bool Exists(int slot) => File.Exists(SavePath(slot));

        /// <summary>Writes to a temporary file first and then swaps it in, so a crash mid-save can't corrupt the save.</summary>
        public static void Save(World world, int slot)
        {
            Directory.CreateDirectory(Folder);
            var now = DateTime.UtcNow;
            Replace(SavePath(slot), SaveGame.ToJson(world, now));
            Replace(SummaryPath(slot), JsonUtility.ToJson(SaveSummary.Of(world, now)));
        }

        static void Replace(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        /// <summary>Loads a slot's world, or returns null with a reason if there isn't a readable one.</summary>
        public static World Load(int slot, out DateTime savedAtUtc, out string error)
        {
            savedAtUtc = default;
            error = null;
            if (!Exists(slot))
            {
                error = "No saved world.";
                return null;
            }
            try
            {
                return SaveGame.FromJson(File.ReadAllText(SavePath(slot)), out savedAtUtc);
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>
        /// A slot's summary (null if it's empty). A save from before summaries were kept is read once to make one.
        /// If the save can't be read, <paramref name="error"/> says why.
        /// </summary>
        public static SaveSummary Summary(int slot, out string error)
        {
            error = null;
            if (!Exists(slot)) return null;
            try
            {
                if (File.Exists(SummaryPath(slot)))
                {
                    var summary = JsonUtility.FromJson<SaveSummary>(File.ReadAllText(SummaryPath(slot)));
                    if (summary != null) return summary;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException)
            {
                // Fall through and rebuild it from the save.
            }
            var world = Load(slot, out DateTime savedAt, out error);
            if (world == null) return null;
            var made = SaveSummary.Of(world, savedAt);
            try
            {
                Replace(SummaryPath(slot), JsonUtility.ToJson(made));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Not being able to cache it is no reason not to show it.
            }
            return made;
        }

        /// <summary>Deletes a slot's world for good.</summary>
        public static void Delete(int slot)
        {
            if (File.Exists(SavePath(slot))) File.Delete(SavePath(slot));
            if (File.Exists(SummaryPath(slot))) File.Delete(SummaryPath(slot));
        }
    }
}
