using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// What sits over the world map, with nothing covering the map itself: the tooltip that follows the cursor
    /// over a village, and the minimap with a "go to my village" button, bottom left. (Clicking a village opens its
    /// window: see <see cref="VillageWindow"/>.)
    /// </summary>
    public class MapPanel
    {
        public VisualElement Root { get; }
        /// <summary>The part of the map in view, in fields, set by the game (for the minimap's frame).</summary>
        public Rect ViewInFields;

        readonly MiniMap miniMap;
        readonly VisualElement tooltip;
        readonly Label tooltipOwner, tooltipName, tooltipInfo;

        public MapPanel(MedievalWorldConquestGame game)
        {
            Root = Element("screen");
            Root.pickingMode = PickingMode.Ignore;

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

            // The whole world in miniature, bottom left, with a button to find home; click or drag on it to move the
            // map there.
            var corner = Element("map-corner");
            corner.Add(ButtonWith("Go to my village", () => game.CenterMapOnHome(), "btn", "btn--small", "map-home-btn"));
            miniMap = new MiniMap(game.CenterMapOn);
            corner.Add(miniMap.Root);
            Root.Add(corner);
            Root.Add(tooltip);
            Show(tooltip, false);
        }

        /// <summary>
        /// Shows who owns the village, its name, and its coordinates and points next to the cursor, or hides the box
        /// when <paramref name="village"/> is null. <paramref name="screen"/> is the mouse position in screen pixels.
        /// </summary>
        public void ShowTooltip(World world, Village village, Vector2 screen)
        {
            bool show = village != null && Root.panel != null;
            Show(tooltip, show);
            if (!show) return;

            SetText(tooltipOwner, world.OwnerName(village));
            tooltipOwner.style.color = MapView.OwnerColor(world, village);
            SetText(tooltipName, village.Name);
            SetText(tooltipInfo, $"({village.X}|{village.Y}) {World.ContinentName(village.X, village.Y)}  ·  {village.Points:N0} points");

            // Screen pixels count up from the bottom; the panel counts down from the top.
            var p = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, Screen.height - screen.y));
            float width = tooltip.layout.width > 0 ? tooltip.layout.width : 180f;
            tooltip.style.left = p.x + 18f + width > Root.layout.width ? p.x - 12f - width : p.x + 18f;
            tooltip.style.top = p.y + 14f;
        }

        public void Refresh(World world) => miniMap.Refresh(world, ViewInFields);

        // Continent numbers over the map, one per 25 × 25 block in view (made as needed and reused).
        readonly System.Collections.Generic.List<Label> continentLabels = new System.Collections.Generic.List<Label>();
        VisualElement continentLayer;

        /// <summary>The zoom (camera size) from which continent numbers show: only once whole continents are in view.</summary>
        const float ContinentLabelZoom = 18f;

        /// <summary>
        /// Writes each continent's number (K00 to K99) faintly in the middle of its block, as Tribal Wars' map does,
        /// when zoomed out far enough to see whole continents.
        /// </summary>
        public void ShowContinents(Camera cam, float zoom)
        {
            if (Root.panel == null) return;
            if (continentLayer == null)
            {
                continentLayer = Element("screen");
                continentLayer.pickingMode = PickingMode.Ignore;
                Root.Insert(0, continentLayer); // under the tooltip and minimap
            }
            int used = 0;
            if (zoom >= ContinentLabelZoom)
            {
                int size = World.ContinentSize;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(ViewInFields.xMin / size)), x1 = Mathf.Min(9, Mathf.FloorToInt(ViewInFields.xMax / size));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(ViewInFields.yMin / size)), y1 = Mathf.Min(9, Mathf.FloorToInt(ViewInFields.yMax / size));
                for (int by = y0; by <= y1; by++)
                    for (int bx = x0; bx <= x1; bx++)
                    {
                        if (used == continentLabels.Count)
                        {
                            var made = Text("", "continent-label");
                            made.pickingMode = PickingMode.Ignore;
                            continentLabels.Add(made);
                            continentLayer.Add(made);
                        }
                        var label = continentLabels[used++];
                        SetText(label, $"K{by * 10 + bx:00}");
                        var centre = MapView.FromFields(new Vector2((bx + 0.5f) * size, (by + 0.5f) * size));
                        Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, centre, cam);
                        label.style.left = p.x;
                        label.style.top = p.y;
                        Show(label, true);
                    }
            }
            for (int i = used; i < continentLabels.Count; i++) Show(continentLabels[i], false);
        }
    }
}