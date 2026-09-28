using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The Map tab's side panel: what's under the cursor, and details of the selected village, including how long
    /// each kind of unit would take to get there from the player's village.
    /// </summary>
    public class MapPanel
    {
        public const float Width = 340f;

        public VisualElement Root { get; }
        /// <summary>The village selected on the map, set by the game.</summary>
        public int? SelectedVillageId;
        /// <summary>The map field under the mouse, set by the game (null when off the map or over the UI).</summary>
        public Vector2Int? Hover;
        /// <summary>The part of the map in view, in fields, set by the game (for the minimap's frame).</summary>
        public Rect ViewInFields;

        readonly Label hover, ownerName, title, relation, position, protection, travelTitle;
        readonly MiniMap miniMap;
        readonly VisualElement card, travel;
        readonly List<Label> travelTimes = new List<Label>();
        readonly Button attack, support;
        readonly VisualElement tooltip;
        readonly Label tooltipOwner, tooltipName, tooltipInfo;

        /// <param name="openSendDialog">Opens the send-troops dialog for a target village.</param>
        public MapPanel(MedievalWorldConquestGame game, Action<int> openSendDialog)
        {
            Root = Element("screen");
            Root.pickingMode = PickingMode.Ignore;

            var side = Element("side-panel");
            side.style.width = Width;
            Root.Add(side);

            // The top section keeps its size; only the village details below it scroll, so nothing up here moves
            // when a village is selected.
            var header = Element("map-header");
            header.Add(Text("World Map", "heading"));
            header.Add(Text("Drag to move · scroll to zoom · click a village for details. Arrow keys / WASD also move.", "row-info"));
            hover = Text("", "row-level", "map-hover");
            header.Add(hover);
            header.Add(ButtonWith("Go to my village", () => game.CenterMapOnHome(), "btn", "btn--small", "map-home-btn"));
            var legend = Element("map-legend");
            legend.Add(LegendEntry("Your villages", MapView.PlayerColor));
            legend.Add(LegendEntry("Other lords (a colour each)", MapView.RivalColors[0], MapView.RivalColors[1], MapView.RivalColors[2]));
            legend.Add(LegendEntry("Barbarian villages", MapView.BarbarianColor));
            legend.Add(Text("Villages grow on the map at 300, 1,000, 3,000, 9,000 and 11,000 points.", "row-info", "legend-note"));
            header.Add(legend);
            side.Add(header);

            var details = new ScrollView(ScrollViewMode.Vertical);
            details.AddToClassList("map-details");
            side.Add(details);

            // Who owns it, the village, then where it is and how big: the same order as the tooltip.
            card = Element("detail", "map-card");
            ownerName = Text("", "detail-title");
            title = Text("", "row-title", "card-village");
            position = Text("", "detail-text");
            relation = Text("", "detail-text");
            protection = Text("", "detail-text", "protection-text");
            card.Add(ownerName);
            card.Add(title);
            card.Add(position);
            card.Add(relation);
            card.Add(protection);

            // Units that march at the same pace share a line, which keeps the card short enough not to scroll.
            travel = Element();
            travelTitle = Text("", "row-title", "map-travel-title");
            travel.Add(travelTitle);
            foreach (var pace in Paces)
            {
                var line = Element("row-header", "travel-line");
                line.Add(Text(string.Join(", ", pace.ConvertAll(t => Units.Get(t).Name)), "row-info", "travel-units"));
                var time = Text("", "row-level");
                travelTimes.Add(time);
                line.Add(time);
                travel.Add(line);
            }
            card.Add(travel);

            var actions = Element("option-row");
            attack = ButtonWith("Attack", () => { if (SelectedVillageId.HasValue) openSendDialog(SelectedVillageId.Value); }, "btn", "btn--small", "map-action");
            support = ButtonWith("Support", () => { if (SelectedVillageId.HasValue) openSendDialog(SelectedVillageId.Value); }, "btn", "btn--small", "map-action");
            actions.Add(attack);
            actions.Add(support);
            card.Add(actions);
            details.Add(card);

            // The little box that follows the cursor over a village.
            tooltip = Element("map-tooltip");
            tooltip.pickingMode = PickingMode.Ignore;
            tooltipOwner = Text("", "tooltip-owner");
            tooltipName = Text("", "tooltip-title");
            tooltipInfo = Text("", "tooltip-text");
            tooltipOwner.pickingMode = tooltipName.pickingMode = tooltipInfo.pickingMode = PickingMode.Ignore;
            tooltip.Add(tooltipOwner);
            tooltip.Add(tooltipName);
            tooltip.Add(tooltipInfo);

            // The whole world in miniature, bottom left; click or drag on it to move the map there.
            miniMap = new MiniMap(game.CenterMapOn);
            Root.Add(miniMap.Root);
            Root.Add(tooltip);
            Show(tooltip, false);
        }

        /// <summary>Unit types grouped by marching pace, in unit order.</summary>
        static readonly List<List<UnitType>> Paces = GroupByPace();

        static List<List<UnitType>> GroupByPace()
        {
            var groups = new List<List<UnitType>>();
            foreach (var u in Units.Definitions)
            {
                var group = groups.Find(g => Units.Get(g[0]).MinutesPerField == u.MinutesPerField);
                if (group == null) groups.Add(group = new List<UnitType>());
                group.Add(u.Type);
            }
            return groups;
        }

        /// <summary>
        /// Shows who owns the village, its name, and its coordinates and points next to the cursor, or hides the box
        /// when <paramref name="village"/> is null. <paramref name="screen"/> is the mouse position in screen pixels.
        /// </summary>
        public void ShowTooltip(World world, Village village, Vector2 screen)
        {
            Show(tooltip, village != null && Root.panel != null);
            if (village == null || Root.panel == null) return;

            SetText(tooltipOwner, world.OwnerName(village));
            tooltipOwner.style.color = MapView.OwnerColor(world, village);
            SetText(tooltipName, village.Name);
            SetText(tooltipInfo, $"({village.X}|{village.Y})  ·  {village.Points:N0} points");

            // Screen pixels count up from the bottom; the panel counts down from the top.
            var p = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, Screen.height - screen.y));
            float width = tooltip.layout.width > 0 ? tooltip.layout.width : 180f;
            float room = Root.layout.width - Width;
            tooltip.style.left = p.x + 18f + width > room ? p.x - 12f - width : p.x + 18f;
            tooltip.style.top = p.y + 14f;
        }

        /// <summary>A legend line; several colours make a striped swatch.</summary>
        static VisualElement LegendEntry(string label, params Color[] colors)
        {
            var row = Element("legend-entry");
            var swatch = Element("legend-swatch");
            if (colors.Length == 1) swatch.style.backgroundColor = colors[0];
            else
            {
                swatch.style.flexDirection = FlexDirection.Row;
                swatch.style.overflow = Overflow.Hidden;
                foreach (var c in colors)
                {
                    var stripe = new VisualElement();
                    stripe.style.flexGrow = 1;
                    stripe.style.backgroundColor = c;
                    swatch.Add(stripe);
                }
            }
            row.Add(swatch);
            row.Add(Text(label, "row-info"));
            return row;
        }

        public void Refresh(World world)
        {
            miniMap.Refresh(world, ViewInFields);

            // Keep showing the last field hovered when the cursor leaves the map, so nothing below jumps around.
            if (Hover.HasValue)
            {
                var h = Hover.Value;
                var here = world.VillageAt(h.x, h.y);
                string what = here != null ? here.Name : world.TerrainAt(h.x, h.y).ToString();
                SetText(hover, $"Field ({h.x}|{h.y})  ·  {what}");
            }
            else if (string.IsNullOrEmpty(hover.text)) SetText(hover, "Point at the map to see a field.");

            var v = SelectedVillageId.HasValue ? world.FindVillage(SelectedVillageId.Value) : null;
            Show(card, v != null);
            if (v == null) return;

            var home = world.PlayerVillage;
            bool isHome = v == home;
            SetText(ownerName, world.OwnerName(v));
            SetText(title, v.Name);
            SetText(relation, v.IsBarbarian ? "A barbarian village: nobody's, free for the taking"
                : v.OwnerId == world.HumanPlayer?.Id ? (isHome ? "Your village" : "One of your villages")
                : "A rival lord's village");
            var lord = world.FindPlayer(v.OwnerId);
            bool shielded = !v.IsBarbarian && lord != null && world.IsProtected(lord.Id);
            Show(protection, shielded);
            if (shielded) SetText(protection, $"Under beginner protection for {Real(world, lord.ProtectedUntil - world.Now)}: it can't be attacked yet.");
            SetText(position, $"({v.X}|{v.Y})  ·  {v.Points:N0} points  ·  {world.TerrainAt(v.X, v.Y)}");

            // Attack anyone else's village; support any village but the one the troops are in.
            bool own = v.OwnerId == home.OwnerId;
            Show(attack, !own);
            Show(support, !isHome);

            Show(travel, !isHome);
            if (!isHome)
            {
                SetText(travelTitle, $"Travel time from {home.Name} ({World.Distance(home, v):0.0} fields)");
                for (int i = 0; i < Paces.Count; i++)
                    SetText(travelTimes[i], Real(world, World.TravelSeconds(home, v, Paces[i][0])));
            }
        }
    }
}
