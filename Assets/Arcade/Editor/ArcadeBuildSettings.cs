using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlasshArcade;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the build settings in step with <see cref="ArcadeCatalog"/>, whenever scripts reload:
/// the product name (which names the built .exe), and the scene list, with the home scene first (it's what
/// a build opens) followed by every game's scene. Scenes added by hand are kept after these.
/// </summary>
[InitializeOnLoad]
static class ArcadeBuildSettings
{
    static ArcadeBuildSettings() => EditorApplication.delayCall += Sync;

    [MenuItem("Tools/Flassh Arcade/Sync Build Settings")]
    static void Sync()
    {
        if (PlayerSettings.productName != ArcadeCatalog.ArcadeName)
            PlayerSettings.productName = ArcadeCatalog.ArcadeName;

        var wanted = new List<string> { ArcadeCatalog.HomeScene };
        wanted.AddRange(ArcadeCatalog.Games.Select(g => g.SceneName));

        var scenes = new List<EditorBuildSettingsScene>();
        foreach (var name in wanted)
        {
            string path = FindScene(name);
            if (path == null)
                Debug.LogWarning($"{ArcadeCatalog.ArcadeName}: no scene named '{name}' found; it won't be in builds.");
            else
                scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        scenes.AddRange(EditorBuildSettings.scenes.Where(existing =>
            File.Exists(existing.path) && scenes.All(s => s.path != existing.path)));

        bool unchanged = scenes.Select(s => s.path + s.enabled)
            .SequenceEqual(EditorBuildSettings.scenes.Select(s => s.path + s.enabled));
        if (!unchanged) EditorBuildSettings.scenes = scenes.ToArray();
    }

    static string FindScene(string name) =>
        AssetDatabase.FindAssets($"{name} t:Scene")
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
}
