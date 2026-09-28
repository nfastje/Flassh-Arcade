using UnityEngine;

namespace FlasshArcade
{
    /// <summary>Small IMGUI drawing helpers shared by the arcade's menus.</summary>
    public static class ArcadeGui
    {
        /// <summary>Draws text, by default with a drop shadow.</summary>
        public static void Text(Rect r, string text, GUIStyle style, Color color, bool shadow = true)
        {
            if (shadow)
            {
                SetTextColor(style, new Color(0f, 0f, 0f, 0.8f * color.a));
                GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            }
            SetTextColor(style, color);
            GUI.Label(r, text, style);
        }

        // Labels switch to their hover colour under the mouse, so every state must share the colour
        // or the shadow copy lights up and the text appears doubled.
        static void SetTextColor(GUIStyle style, Color color) =>
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;

        public static void Fill(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        public static void Outline(Rect r, float thickness, Color color)
        {
            Fill(new Rect(r.x, r.y, r.width, thickness), color);
            Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            Fill(new Rect(r.x, r.y, thickness, r.height), color);
            Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        public static void SetBackground(Color color)
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = color;
        }
    }
}
