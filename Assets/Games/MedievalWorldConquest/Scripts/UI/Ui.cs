using System;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;

namespace MedievalWorldConquest
{
    /// <summary>
    /// A tooltip with troops in it, shown as unit icons with their counts: put one in an element's
    /// <c>userData</c> (and keep a plain <c>tooltip</c> as well, which marks it as having one).
    /// </summary>
    public class TroopTip
    {
        public string Title, Note;
        public int[] Troops;

        public string Key
        {
            get
            {
                var key = new System.Text.StringBuilder(Title).Append('|').Append(Note).Append('|');
                if (Troops != null) foreach (int n in Troops) key.Append(n).Append(',');
                return key.ToString();
            }
        }
    }

    /// <summary>
    /// A small parchment checkbox, styled like the report cards (UI Toolkit's own Toggle keeps its theme's look):
    /// click to tick or untick.
    /// </summary>
    public class CheckBox : VisualElement
    {
        readonly VisualElement mark;

        public bool Value { get; private set; }

        /// <summary>Raised when the player ticks or unticks it (not by <see cref="SetValueWithoutNotify"/>).</summary>
        public event Action<bool> Changed;

        public CheckBox()
        {
            AddToClassList("check-box");
            mark = Icons.Element(Icons.Check, 12, "check-box-mark");
            Add(mark);
            RegisterCallback<ClickEvent>(_ =>
            {
                if (!enabledInHierarchy) return;
                SetValueWithoutNotify(!Value);
                Changed?.Invoke(Value);
            });
            SetValueWithoutNotify(false);
        }

        public void SetValueWithoutNotify(bool on)
        {
            Value = on;
            EnableInClassList("check-box--checked", on);
            mark.style.visibility = on ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// <summary>
    /// A number whose digits never move: each character in a cell of its own, digits all the same width (the
    /// font's aren't), right-aligned. For figures that change all the time, like the stores in the top bar.
    /// Text styles (size, color, weight) come from the element's classes, which the cells inherit.
    /// </summary>
    public class SteadyNumber : VisualElement
    {
        string shown;

        public SteadyNumber(params string[] classes)
        {
            AddToClassList("steady-number");
            foreach (var c in classes) AddToClassList(c);
            pickingMode = PickingMode.Ignore;
        }

        public void SetText(string text)
        {
            if (text == shown) return;
            // Cells are reused; only the ones that changed are touched.
            for (int i = 0; i < text.Length; i++)
            {
                Label cell;
                if (i < childCount) cell = (Label)this[i];
                else
                {
                    cell = new Label { pickingMode = PickingMode.Ignore };
                    cell.AddToClassList("steady-cell");
                    Add(cell);
                }
                string ch = text[i].ToString();
                if (cell.text != ch)
                {
                    cell.text = ch;
                    cell.EnableInClassList("steady-cell--narrow", !char.IsDigit(text[i]));
                }
            }
            while (childCount > text.Length) RemoveAt(childCount - 1);
            shown = text;
        }
    }

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
