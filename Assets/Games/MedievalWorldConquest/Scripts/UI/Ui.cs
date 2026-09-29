using System;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;

namespace MedievalWorldConquest
{
    /// <summary>Small helpers shared by the game's UI Toolkit screens.</summary>
    static class Ui
    {
        public static VisualElement Element(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static Button ButtonWith(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        /// <summary>Clickable text, like a link on a web page: a player's or village's name that opens its window.</summary>
        public static Button Link(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("link");
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        public static void Show(VisualElement e, bool visible) => e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Sets a label's text only if it changed, so refreshing every frame doesn't force relayouts.</summary>
        public static void SetText(TextElement label, string text)
        {
            if (label.text != text) label.text = text;
        }

        /// <summary>A game-time duration shown as the real time the player will wait (divided by the world speed).</summary>
        public static string Real(World world, double gameSeconds) => World.FormatDuration(gameSeconds / world.Settings.Speed);
    }
}
