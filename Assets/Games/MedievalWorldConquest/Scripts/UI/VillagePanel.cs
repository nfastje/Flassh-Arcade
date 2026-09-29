using System;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The village view, laid out like Tribal Wars' classic overview: a green level square on every building
    /// (with a countdown on the one being upgraded), and a pane on the right showing the village's production and
    /// the units at home. Clicking a building (or its square) opens its own screen.
    /// </summary>
    public class VillagePanel
    {
        /// <summary>Width of the pane on the right, in reference pixels (1280 x 720).</summary>
        public const float PaneWidth = 250f;

        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly Camera cam;
        readonly Button[] badges = new Button[Buildings.Count];
        readonly Label[] badgeTimers = new Label[Buildings.Count];
        readonly Label[] production = new Label[3];
        readonly VisualElement unitList, protectionBox;
        readonly Label protection;
        string unitSignature;

        public VillagePanel(MedievalWorldConquestGame game, Camera cam)
        {
            this.game = game;
            this.cam = cam;
            Root = Element("screen");
            Root.pickingMode = PickingMode.Ignore;

            // The level squares, placed over the buildings each frame.
            var badgeLayer = Element("screen");
            badgeLayer.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < badges.Length; i++)
            {
                var type = (BuildingType)i;
                var badge = ButtonWith("", () => game.OpenBuilding(type), "level-badge");
                badge.tooltip = Buildings.Get(type).Name;
                badges[i] = badge;
                badgeLayer.Add(badge);
                var timer = Text("", "level-timer");
                timer.pickingMode = PickingMode.Ignore;
                badgeTimers[i] = timer;
                badgeLayer.Add(timer);
            }
            Root.Add(badgeLayer);

            // The pane on the right.
            var pane = Element("village-pane");
            pane.style.width = PaneWidth;

            var productionBox = Box(pane, "Production");
            for (int i = 0; i < 3; i++)
            {
                var r = (ResourceType)i;
                var line = Element("pane-line");
                line.Add(Icons.Element(Icons.Resource(r), 18, "pane-icon"));
                line.Add(Text(r.ToString(), "pane-name"));
                production[i] = Text("", "pane-value");
                line.Add(production[i]);
                productionBox.Add(line);
            }

            var unitsBox = Box(pane, "Units");
            unitList = Element();
            unitsBox.Add(unitList);
            var links = Element("pane-links");
            links.Add(ButtonWith("» recruit", () => game.OpenBuilding(BuildingType.Barracks), "pane-link"));
            links.Add(ButtonWith("» rally point", () => game.OpenBuilding(BuildingType.RallyPoint), "pane-link"));
            unitsBox.Add(links);

            protectionBox = Box(pane, "Beginner protection");
            protection = Text("", "row-info");
            protectionBox.Add(protection);

            Root.Add(pane);
        }

        static VisualElement Box(VisualElement pane, string title)
        {
            var box = Element("pane-box");
            box.Add(Text(title, "pane-title"));
            pane.Add(box);
            return box;
        }

        public void Refresh(World world, Village v)
        {
            float speed = world.Settings.Speed;
            for (int i = 0; i < 3; i++)
                SetText(production[i], $"{v.ProductionPerHour((ResourceType)i) * speed:N0} per hour");

            RefreshUnits(v);

            var human = world.HumanPlayer;
            bool shielded = human != null && world.IsProtected(human.Id) && world.Players.Count > 1;
            Show(protectionBox, shielded);
            if (shielded) SetText(protection, $"Nobody can attack you for another {Real(world, human.ProtectedUntil - world.Now)}.");

            RefreshBadges(world, v);
        }

        /// <summary>Only the units the village has at home, as in Tribal Wars ("657 Spear fighters").</summary>
        void RefreshUnits(Village v)
        {
            string signature = "";
            for (int i = 0; i < Units.Count; i++) signature += v.TroopCount((UnitType)i) + ",";
            if (signature == unitSignature) return;
            unitSignature = signature;

            unitList.Clear();
            bool any = false;
            foreach (var type in Units.InDisplayOrder)
            {
                int n = v.TroopCount(type);
                if (n == 0) continue;
                any = true;
                var line = Element("pane-line");
                line.Add(Icons.Element(Icons.Unit(type), 18, "pane-icon"));
                line.Add(Text($"{n:N0}", "pane-count"));
                line.Add(Text(Units.Get(type).Name, "pane-name"));
                unitList.Add(line);
            }
            if (!any) unitList.Add(Text("No troops at home.", "row-info"));
        }

        void RefreshBadges(World world, Village v)
        {
            var panel = Root.panel;
            if (panel == null) return;
            for (int i = 0; i < badges.Length; i++)
            {
                var type = (BuildingType)i;
                int level = v.Level(type);
                bool building = v.Queue.Count > 0 && v.Queue[0].Type == type && v.Queue[0].Started;
                // A square for every building that stands (or is going up); empty plots have their signpost.
                Show(badges[i], level > 0 || building);
                Show(badgeTimers[i], building);
                SetText(badges[i], level > 0 ? level.ToString() : "…"); // going up for the first time
                if (building) SetText(badgeTimers[i], Real(world, Math.Max(0, v.Queue[0].FinishTime - world.Now)));

                // On the building, a little above its base.
                var worldPos = (Vector3)VillageView.PlotOf(type) + new Vector3(0f, 0.35f, 0f);
                Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPos, cam);
                badges[i].style.left = p.x;
                badges[i].style.top = p.y;
                badgeTimers[i].style.left = p.x;
                badgeTimers[i].style.top = p.y + 22;
            }
        }
    }
}
