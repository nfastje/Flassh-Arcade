using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// A building's own screen, opened by clicking it in the village, as in Tribal Wars: the Headquarters lists
    /// every building's upgrade (new buildings included) and renames the village; the barracks, stable, workshop
    /// and academy recruit; the smithy researches units; the market sends resources and trades; the rally point
    /// shows troops at home and on the march and sends them out; every other building says what it does.
    /// </summary>
    public class BuildingWindow
    {
        public VisualElement Root { get; }
        public bool IsOpen => Root.style.display == DisplayStyle.Flex;
        /// <summary>The building whose screen is showing, if any.</summary>
        public BuildingType? Building { get; private set; }

        readonly MedievalWorldConquestGame game;
        readonly Label title, description;
        readonly ScrollView body;
        readonly HeadquartersView headquarters;
        readonly Dictionary<BuildingType, RecruitView> recruitment = new Dictionary<BuildingType, RecruitView>();
        /// <summary>The Recruit screen: the barracks, stable and workshop together.</summary>
        readonly RecruitView recruitAll;
        bool showingRecruitAll;
        readonly RallyPointView rallyPoint;
        readonly SmithyView smithy;
        readonly MarketView market;
        readonly InfoView info;

        static readonly BuildingType[] TrainingBuildings = { BuildingType.Barracks, BuildingType.Stable, BuildingType.Workshop, BuildingType.Academy };

        /// <param name="sendTo">Opens the send-troops dialog for the village at a map field, if there is one there.</param>
        /// <param name="links">Where player and village names lead.</param>
        public BuildingWindow(MedievalWorldConquestGame game, Func<int, int, bool> sendTo, UiLinks links)
        {
            this.game = game;
            Root = Element("screen", "centered", "dim");
            Root.style.top = GameUI.TopBarHeight; // below the top bar, which stays in view
            var panel = Element("panel", "window");
            Root.Add(panel);

            var header = Element("window-header");
            title = Text("", "window-title");
            header.Add(title);
            header.Add(ButtonWith("Close", Close, "btn", "btn--small"));
            panel.Add(header);
            description = Text("", "row-info", "window-description");
            panel.Add(description);

            body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("window-body");
            panel.Add(body);

            headquarters = new HeadquartersView(game);
            foreach (var b in TrainingBuildings) recruitment[b] = new RecruitView(game, b);
            recruitAll = new RecruitView(game, BuildingType.Barracks, BuildingType.Stable, BuildingType.Workshop);
            rallyPoint = new RallyPointView(game, sendTo, links);
            smithy = new SmithyView(game);
            market = new MarketView(game, links);
            info = new InfoView(game);
            Show(Root, false);
        }

        /// <summary>The building to mark in the village (none for the Recruit screen, which is all of them).</summary>
        public BuildingType? Highlight => showingRecruitAll ? null : Building;

        /// <summary>Opens the Recruit screen: every unit of the barracks, stable and workshop, as in Tribal Wars.</summary>
        public void OpenRecruitAll()
        {
            Building = BuildingType.Barracks;
            showingRecruitAll = true;
            body.Clear();
            body.Add(recruitAll.Root);
            Show(Root, true);
        }

        public void Open(BuildingType type)
        {
            Building = type;
            showingRecruitAll = false;
            body.Clear();
            if (type == BuildingType.Headquarters) body.Add(headquarters.Root);
            else if (recruitment.TryGetValue(type, out var recruit)) body.Add(recruit.Root);
            else if (type == BuildingType.RallyPoint) body.Add(rallyPoint.Root);
            else if (type == BuildingType.Smithy) body.Add(smithy.Root);
            else if (type == BuildingType.Market) body.Add(market.Root);
            else body.Add(info.Root);
            headquarters.OnOpen();
            Show(Root, true);
        }

        public void Close()
        {
            Building = null;
            Show(Root, false);
        }

        public void Refresh(World world, Village v)
        {
            if (!IsOpen || !Building.HasValue || v == null) return;
            if (showingRecruitAll)
            {
                SetText(title, "Recruit");
                SetText(description, "Every unit your barracks, stable and workshop train, in one place.");
                recruitAll.Refresh(world, v);
                return;
            }
            var type = Building.Value;
            var def = Buildings.Get(type);
            int level = v.Level(type);
            SetText(title, level > 0 ? $"{def.Name} (level {level})" : $"{def.Name} (not built yet)");
            SetText(description, def.Description);

            if (type == BuildingType.Headquarters) headquarters.Refresh(world, v);
            else if (recruitment.TryGetValue(type, out var recruit)) recruit.Refresh(world, v);
            else if (type == BuildingType.RallyPoint) rallyPoint.Refresh(world, v);
            else if (type == BuildingType.Smithy) smithy.Refresh(world, v);
            else if (type == BuildingType.Market) market.Refresh(world, v);
            else info.Refresh(world, v, type);
        }

        /// <summary>Opens the market with a village filled in as where to send resources.</summary>
        public void OpenMarketTo(int x, int y)
        {
            Open(BuildingType.Market);
            market.SetTarget(x, y);
        }
    }

    // -------------------------------------------------------------------- other buildings

    /// <summary>Any other building: what it does now and next, and its upgrade.</summary>
    class InfoView
    {
        public VisualElement Root { get; }
        readonly Label effect, points;
        readonly UpgradeBox upgrade;

        public InfoView(MedievalWorldConquestGame game)
        {
            Root = Element("window-section");
            effect = Text("", "detail-effect");
            Root.Add(effect);
            points = Text("", "row-info");
            Root.Add(points);
            upgrade = new UpgradeBox(game);
            Root.Add(upgrade.Root);
            Root.Add(ButtonWith("Open the Headquarters (all upgrades)", () => game.OpenBuilding(BuildingType.Headquarters), "btn", "btn--small", "link-btn"));
        }

        public void Refresh(World world, Village v, BuildingType type)
        {
            var def = Buildings.Get(type);
            int level = v.Level(type), next = v.NextLevel(type);
            SetText(effect, BuildingText.Effect(world, type, level, next <= def.MaxLevel ? next : (int?)null));
            SetText(points, $"Worth {Buildings.PointsAtLevel(type, level):N0} village points.");
            upgrade.Refresh(world, v, type);
        }
    }
}
