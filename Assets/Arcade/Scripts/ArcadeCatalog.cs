using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlasshArcade
{
    /// <summary>One game in the arcade: its own scene, plus the component that runs it.</summary>
    public readonly struct ArcadeGame
    {
        public readonly string Title;
        public readonly string Description;
        public readonly string SceneName;
        public readonly Type Controller;
        public readonly Color Accent;

        /// <summary>Placeholder games show a "coming soon" screen instead of gameplay.</summary>
        public bool ComingSoon => typeof(ComingSoonScreen).IsAssignableFrom(Controller);

        public ArcadeGame(string title, string description, string sceneName, Type controller, Color accent)
        {
            Title = title;
            Description = description;
            SceneName = sceneName;
            Controller = controller;
            Accent = accent;
        }
    }

    /// <summary>
    /// Every game in the arcade. To add a game: create Assets/Games/[Name]/Scenes/[Name].unity, write a controller
    /// MonoBehaviour in Assets/Games/[Name]/Scripts that builds the game at runtime, and add an entry here.
    /// The home page lists it and the build scene list picks it up automatically.
    /// </summary>
    public static class ArcadeCatalog
    {
        /// <summary>Two s's on purpose. Also the product name, so builds are "Flassh Arcade.exe".</summary>
        public const string ArcadeName = "Flassh Arcade";
        public const string HomeScene = "Home";

        public static readonly ArcadeGame[] Games =
        {
            new ArcadeGame(
                "Planet Crasher",
                "Crash into anything smaller to grow from asteroid to black hole. Touch anything bigger and you're crushed.",
                "PlanetCrasher",
                typeof(PlanetCrasher.GameManager),
                new Color(1f, 0.75f, 0.3f)),
            new ArcadeGame(
                "Little Fishy",
                "Start as a little goldfish and eat your way up. Touch a bigger fish and you're lunch.",
                "LittleFishy",
                typeof(LittleFishy.LittleFishyGame),
                new Color(0.35f, 0.85f, 1f)),
            new ArcadeGame(
                "Towering Survival",
                "Blocks rain from the sky and lava rises from below. Climb as high as you can.",
                "ToweringSurvival",
                typeof(ToweringSurvival.ToweringSurvivalGame),
                new Color(0.55f, 1f, 0.45f)),
            new ArcadeGame(
                "Medieval World Conquest",
                "Build a village, raise an army and conquer the realm. (Early development)",
                "MedievalWorldConquest",
                typeof(MedievalWorldConquest.MedievalWorldConquestGame),
                new Color(0.9f, 0.72f, 0.4f)),
        };

        /// <summary>The catalog entry for a scene, or null if the scene isn't a game.</summary>
        public static ArcadeGame? FindByScene(string sceneName)
        {
            foreach (var game in Games)
                if (game.SceneName == sceneName) return game;
            return null;
        }
    }

    public static class Arcade
    {
        public static void LoadHome() => SceneManager.LoadScene(ArcadeCatalog.HomeScene);

        public static void Play(ArcadeGame game) => SceneManager.LoadScene(game.SceneName);

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
