using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FlasshArcade
{
    /// <summary>
    /// Stand-in for a game that hasn't been made yet: shows its title from the catalog and a way back to the menu.
    /// Each placeholder game subclasses this, so building the real game means replacing that one class.
    /// </summary>
    public abstract class ComingSoonScreen : MonoBehaviour
    {
        ArcadeGame game;
        GUIStyle title, subtitle, button;
        float guiScale = -1f;

        protected virtual void Awake()
        {
            game = ArcadeCatalog.FindByScene(SceneManager.GetActiveScene().name)
                ?? new ArcadeGame(GetType().Name, "", "", GetType(), Color.white);
            ArcadeGui.SetBackground(new Color(0.03f, 0.02f, 0.08f));
        }

        protected virtual void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Arcade.LoadHome();
        }

        void EnsureStyles()
        {
            float s = Screen.height / 720f;
            if (title != null && Mathf.Approximately(s, guiScale)) return;
            guiScale = s;

            title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(64 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            subtitle = new GUIStyle(title) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Normal };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold };
        }

        protected virtual void OnGUI()
        {
            EnsureStyles();
            float s = guiScale, w = Screen.width, h = Screen.height;

            ArcadeGui.Text(new Rect(0, h * 0.3f, w, 80f * s), game.Title.ToUpper(), title, game.Accent);
            ArcadeGui.Text(new Rect(0, h * 0.3f + 90f * s, w, 40f * s), "Coming soon", subtitle, new Color(0.8f, 0.82f, 0.95f));
            if (GUI.Button(new Rect(w / 2f - 130f * s, h * 0.3f + 170f * s, 260f * s, 48f * s), "Main Menu", button))
                Arcade.LoadHome();
        }
    }
}
