using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// Hover tooltips. UI Toolkit only draws an element's <c>tooltip</c> in the Editor's own windows, never in a
    /// running game, so the game shows them itself: one box that follows the pointer, showing the tooltip of the
    /// element under it (or of the nearest parent that has one) after a moment's hover. An element with a
    /// <see cref="TroopTip"/> in its <c>userData</c> gets its troops as unit icons with counts instead.
    /// </summary>
    public partial class GameUI
    {
        const long TooltipDelayMs = 350;

        VisualElement tooltipBox, tooltipTroops;
        Label tooltipText, tooltipNote;
        readonly VisualElement[] tooltipUnitCells = new VisualElement[Units.Count];
        readonly Label[] tooltipUnitCounts = new Label[Units.Count];
        VisualElement tooltipOwner;
        string tooltipShown;
        Vector2 tooltipPointer;
        IVisualElementScheduledItem tooltipTimer;

        void BuildTooltips()
        {
            tooltipBox = Element("ui-tooltip");
            tooltipBox.pickingMode = PickingMode.Ignore;
            tooltipText = Text("", "ui-tooltip-text");
            tooltipBox.Add(tooltipText);
            tooltipTroops = Element("ui-tooltip-troops");
            foreach (var type in Units.InDisplayOrder)
            {
                var cell = Element("ui-tooltip-unit");
                cell.Add(Icons.Element(Icons.Unit(type), 20));
                tooltipUnitCounts[(int)type] = Text("", "ui-tooltip-count");
                cell.Add(tooltipUnitCounts[(int)type]);
                tooltipUnitCells[(int)type] = cell;
                tooltipTroops.Add(cell);
            }
            tooltipBox.Add(tooltipTroops);
            tooltipNote = Text("", "ui-tooltip-text", "ui-tooltip-note");
            tooltipBox.Add(tooltipNote);
            foreach (var e in tooltipBox.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            Show(tooltipBox, false);
            root.Add(tooltipBox);
            root.RegisterCallback<PointerMoveEvent>(OnPointerMoveForTooltip, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(_ => HideTooltip(), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
            tooltipBox.RegisterCallback<GeometryChangedEvent>(_ => PlaceTooltip());
        }

        /// <summary>The element, or its nearest parent, that has a tooltip (null: none).</summary>
        static VisualElement TooltipOwner(VisualElement e)
        {
            for (; e != null; e = e.parent)
                if (!string.IsNullOrEmpty(e.tooltip)) return e;
            return null;
        }

        void OnPointerMoveForTooltip(PointerMoveEvent e)
        {
            tooltipPointer = e.position;
            var owner = TooltipOwner(e.target as VisualElement);
            if (owner == tooltipOwner)
            {
                if (TooltipShowing) PlaceTooltip();
                return;
            }
            HideTooltip();
            tooltipOwner = owner;
            if (owner != null) tooltipTimer = root.schedule.Execute(ShowTooltipNow).StartingIn(TooltipDelayMs);
        }

        bool TooltipShowing => tooltipBox.style.display == DisplayStyle.Flex;

        void ShowTooltipNow()
        {
            if (tooltipOwner == null || tooltipOwner.panel == null || string.IsNullOrEmpty(tooltipOwner.tooltip)) return;
            tooltipShown = null;
            FillTooltip();
            Show(tooltipBox, true);
            tooltipBox.BringToFront();
            PlaceTooltip();
        }

        /// <summary>The owner's tooltip into the box: plain text, or a troop tip's title, unit icons and note.</summary>
        void FillTooltip()
        {
            var tip = tooltipOwner.userData as TroopTip;
            string key = tip != null ? tip.Key : tooltipOwner.tooltip;
            if (key == tooltipShown) return;
            tooltipShown = key;
            if (tip == null)
            {
                SetText(tooltipText, tooltipOwner.tooltip);
                Show(tooltipText, true);
                Show(tooltipTroops, false);
                Show(tooltipNote, false);
                return;
            }
            SetText(tooltipText, tip.Title ?? "");
            Show(tooltipText, !string.IsNullOrEmpty(tip.Title));
            bool any = false;
            for (int i = 0; i < Units.Count; i++)
            {
                int n = tip.Troops != null && i < tip.Troops.Length ? tip.Troops[i] : 0;
                Show(tooltipUnitCells[i], n > 0);
                if (n > 0) SetText(tooltipUnitCounts[i], $"{n:N0}");
                any |= n > 0;
            }
            Show(tooltipTroops, any);
            SetText(tooltipNote, tip.Note ?? "");
            Show(tooltipNote, !string.IsNullOrEmpty(tip.Note));
        }

        void HideTooltip()
        {
            tooltipTimer?.Pause();
            tooltipTimer = null;
            tooltipOwner = null;
            Show(tooltipBox, false);
        }

        /// <summary>Keeps a showing tooltip current: its contents as they change, and gone if its element is.</summary>
        void RefreshTooltip()
        {
            if (!TooltipShowing) return;
            if (tooltipOwner == null || tooltipOwner.panel == null || tooltipOwner.resolvedStyle.display == DisplayStyle.None ||
                string.IsNullOrEmpty(tooltipOwner.tooltip))
            {
                HideTooltip();
                return;
            }
            FillTooltip();
        }

        /// <summary>Below and to the right of the pointer, flipped to the other side near the screen's edges.</summary>
        void PlaceTooltip()
        {
            if (!TooltipShowing) return;
            float w = tooltipBox.layout.width, h = tooltipBox.layout.height;
            float maxX = root.layout.width, maxY = root.layout.height;
            var p = tooltipPointer + new Vector2(14f, 20f);
            if (!float.IsNaN(w) && !float.IsNaN(maxX))
            {
                if (p.x + w > maxX - 4f) p.x = Mathf.Max(4f, tooltipPointer.x - w - 8f);
                if (p.y + h > maxY - 4f) p.y = Mathf.Max(4f, tooltipPointer.y - h - 8f);
            }
            if (Mathf.Abs(tooltipBox.resolvedStyle.left - p.x) > 0.5f) tooltipBox.style.left = p.x;
            if (Mathf.Abs(tooltipBox.resolvedStyle.top - p.y) > 0.5f) tooltipBox.style.top = p.y;
        }
    }
}
