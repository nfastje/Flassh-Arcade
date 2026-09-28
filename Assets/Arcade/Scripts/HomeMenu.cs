using UnityEngine;
using UnityEngine.InputSystem;
using static FlasshArcade.ArcadeGui;

namespace FlasshArcade
{
    /// <summary>
    /// The arcade's home page: one card per game in <see cref="ArcadeCatalog"/>, plus Quit.
    /// Click a card, or use the arrow keys and Enter.
    /// </summary>
    public class HomeMenu : MonoBehaviour
    {
        const int StarCount = 90;

        Vector3[] stars; // x, y in 0..1 screen space; z = twinkle phase
        int selected;

        GUIStyle title, subtitle, cardTitle, cardText, cardHint, button;
        float guiScale = -1f;

        void Awake()
        {
            SetBackground(new Color(0.03f, 0.02f, 0.08f));

            stars = new Vector3[StarCount];
            for (int i = 0; i < StarCount; i++)
                stars[i] = new Vector3(Random.value, Random.value, Random.value * 10f);
        }

        void Update()
        {
            var kb = Keyboard.current;
            int count = ArcadeCatalog.Games.Length;
            if (kb == null || count == 0) return;

            if (kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) selected = (selected + 1) % count;
            if (kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) selected = (selected - 1 + count) % count;
            if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Arcade.Play(ArcadeCatalog.Games[selected]);
        }

        // ---------------------------------------------------------------- drawing

        void EnsureStyles()
        {
            float s = Screen.height / 720f;
            if (title != null && Mathf.Approximately(s, guiScale)) return;
            guiScale = s;

            var label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(72 * s) };
            subtitle = new GUIStyle(label) { fontSize = Mathf.RoundToInt(22 * s), fontStyle = FontStyle.Normal };
            cardTitle = new GUIStyle(label) { fontSize = Mathf.RoundToInt(30 * s), alignment = TextAnchor.UpperLeft };
            cardText = new GUIStyle(label) { fontSize = Mathf.RoundToInt(17 * s), fontStyle = FontStyle.Normal, alignment = TextAnchor.UpperLeft, wordWrap = true };
            cardHint = new GUIStyle(label) { fontSize = Mathf.RoundToInt(18 * s), alignment = TextAnchor.LowerRight };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold };
        }

        void OnGUI()
        {
            EnsureStyles();
            float s = guiScale, w = Screen.width, h = Screen.height;

            DrawStars(s, w, h);

            Text(new Rect(0, h * 0.1f, w, 90f * s), ArcadeCatalog.ArcadeName.ToUpper(), title, new Color(1f, 0.85f, 0.4f));
            Text(new Rect(0, h * 0.1f + 90f * s, w, 30f * s), "Choose a game", subtitle, new Color(0.75f, 0.8f, 0.95f));

            var games = ArcadeCatalog.Games;
            float cardW = 360f * s, cardH = 170f * s, gap = 24f * s;
            int perRow = Mathf.Clamp((int)((w - 40f * s + gap) / (cardW + gap)), 1, Mathf.Max(1, games.Length));
            int rows = Mathf.CeilToInt(games.Length / (float)perRow);
            float top = h * 0.36f;

            for (int i = 0; i < games.Length; i++)
            {
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, games.Length - row * perRow);
                float rowWidth = inRow * cardW + (inRow - 1) * gap;
                var rect = new Rect((w - rowWidth) / 2f + col * (cardW + gap), top + row * (cardH + gap), cardW, cardH);
                if (DrawCard(rect, i)) Arcade.Play(games[i]);
            }

            float quitY = Mathf.Min(top + rows * (cardH + gap) + 16f * s, h - 70f * s);
            if (GUI.Button(new Rect(w / 2f - 110f * s, quitY, 220f * s, 48f * s), "Quit", button)) Arcade.Quit();
        }

        bool DrawCard(Rect r, int index)
        {
            var game = ArcadeCatalog.Games[index];
            float s = guiScale;

            if (r.Contains(Event.current.mousePosition)) selected = index;
            bool lit = index == selected;

            Fill(r, new Color(1f, 1f, 1f, lit ? 0.13f : 0.06f));
            Fill(new Rect(r.x, r.y, 6f * s, r.height), game.Accent);
            if (lit) Outline(r, 2f * s, new Color(game.Accent.r, game.Accent.g, game.Accent.b, 0.85f));

            float pad = 24f * s;
            Text(new Rect(r.x + pad, r.y + 16f * s, r.width - pad * 1.5f, 40f * s), game.Title, cardTitle, game.Accent);
            Text(new Rect(r.x + pad, r.y + 60f * s, r.width - pad * 1.5f, r.height - 96f * s), game.Description, cardText, new Color(0.85f, 0.87f, 0.95f), false);
            Text(new Rect(r.x, r.y, r.width - 18f * s, r.height - 12f * s), game.ComingSoon ? "COMING SOON" : "PLAY  >",
                cardHint, lit ? game.Accent : new Color(0.6f, 0.6f, 0.7f));

            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        void DrawStars(float s, float w, float h)
        {
            float t = Time.unscaledTime;
            foreach (var star in stars)
            {
                float twinkle = 0.25f + 0.55f * (0.5f + 0.5f * Mathf.Sin(t * 1.5f + star.z));
                float size = (1.5f + star.z * 0.15f) * s;
                float y = Mathf.Repeat(star.y + t * 0.004f, 1f);
                Fill(new Rect(star.x * w, y * h, size, size), new Color(1f, 1f, 1f, twinkle));
            }
        }
    }
}
