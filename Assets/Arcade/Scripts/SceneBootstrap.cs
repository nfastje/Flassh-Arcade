using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FlasshArcade
{
    /// <summary>
    /// Adds the right controller to each scene as it loads (the home menu, or a game from the catalog),
    /// so scenes need no setup of their own.
    /// </summary>
    static class SceneBootstrap
    {
        // Play mode starts without a domain reload in this project, so drop last session's subscription first.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => SceneManager.sceneLoaded -= OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Subscribe()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // Covers the first scene even if its load event fired before the subscription took effect.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void SetUpFirstScene() => SetUp(SceneManager.GetActiveScene());

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SetUp(scene);

        static void SetUp(Scene scene)
        {
            var controller = ControllerFor(scene.name);
            if (controller == null || Object.FindAnyObjectByType(controller) != null) return;

            var go = new GameObject(controller.Name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent(controller);
        }

        static Type ControllerFor(string sceneName)
        {
            if (sceneName == ArcadeCatalog.HomeScene) return typeof(HomeMenu);
            return ArcadeCatalog.FindByScene(sceneName)?.Controller;
        }
    }
}
