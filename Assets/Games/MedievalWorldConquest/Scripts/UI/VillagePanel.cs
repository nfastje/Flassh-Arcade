using System;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The Village tab: the building list (with costs, times and why an upgrade is blocked), details of the selected
    /// building, the construction queue, and a clickable level badge under each building in the illustrated village.
    /// Durations and rates are shown in real time, i.e. already divided by the world speed.
    /// </summary>
    public class VillagePanel
    {
        /// <summary>Width of the building list on the right, in reference pixels (1280 x 720).</summary>
        public const float ListWidth = 380f;

        public VisualElement Root { get; }
        public BuildingType? Selected { get; private set; }

        readonly MedievalWorldConquestGame game;
        readonly Camera cam;

        class Row
        {
            public VisualElement Element;
            public Label Level, Info, Reason;
            public Button Upgrade;
        }

        readonly Row[] rows = new Row[Buildings.Count];
        readonly Button[] badges = new Button[Buildings.Count];
        Label detailTitle, detailText, detailEffect, detailCost, detailReason, queueTitle;
        Button detailUpgrade;
        VisualElement detail, queueList, badgeLayer;

        public VillagePanel(MedievalWorldConquestGame game, Camera cam)
        {
            this.game = game;
            this.cam = cam;
            Root = new VisualElement();
            Root.AddToClassList("screen");
            Root.pickingMode = PickingMode.Ignore;

            BuildBadges();
            BuildQueue();
            BuildList();
        }

        // ---------------------------------------------------------------- building list

        void BuildList()
        {
            var side = new VisualElement();
            side.AddToClassList("side-panel");
            side.style.width = ListWidth;

            detail = new VisualElement();
            detail.AddToClassList("detail");
            detailTitle = Text("", "detail-title");
            detailText = Text("", "detail-text");
            detailEffect = Text("", "detail-effect");
            detail.Add(detailTitle);
            detail.Add(detailText);
            detail.Add(detailEffect);
            // Upgrade straight from here, without hunting for the building's row in the list.
            detailCost = Text("", "row-info");
            detail.Add(detailCost);
            detailUpgrade = new Button(() =>
            {
                if (Selected.HasValue) game.QueueBuild(Selected.Value);
            });
            detailUpgrade.AddToClassList("btn");
            detailUpgrade.AddToClassList("btn--small");
            detailUpgrade.AddToClassList("build-btn");
            detail.Add(detailUpgrade);
            detailReason = Text("", "row-reason");
            detail.Add(detailReason);
            side.Add(detail);

            side.Add(Text("Buildings", "heading"));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("build-list");
            foreach (var def in Buildings.Definitions)
            {
                var type = def.Type;
                var row = new Row { Element = new VisualElement() };
                row.Element.AddToClassList("build-row");
                row.Element.RegisterCallback<ClickEvent>(_ => Select(type));

                var header = new VisualElement();
                header.AddToClassList("row-header");
                header.Add(Text(def.Name, "row-title"));
                row.Level = Text("", "row-level");
                header.Add(row.Level);
                row.Element.Add(header);

                row.Info = Text("", "row-info");
                row.Element.Add(row.Info);

                row.Upgrade = new Button(() =>
                {
                    Select(type);
                    game.QueueBuild(type);
                });
                row.Upgrade.AddToClassList("btn");
                row.Upgrade.AddToClassList("btn--small");
                row.Upgrade.AddToClassList("build-btn");
                row.Element.Add(row.Upgrade);

                row.Reason = Text("", "row-reason");
                row.Element.Add(row.Reason);

                rows[(int)type] = row;
                scroll.Add(row.Element);
            }
            side.Add(scroll);
            Root.Add(side);
        }

        public void Select(BuildingType? type)
        {
            Selected = type;
            for (int i = 0; i < rows.Length; i++)
                rows[i].Element.EnableInClassList("build-row--selected", type.HasValue && (int)type.Value == i);
        }

        // ---------------------------------------------------------------- queue

        void BuildQueue()
        {
            var panel = new VisualElement();
            panel.AddToClassList("queue-panel");
            queueTitle = Text("", "heading");
            panel.Add(queueTitle);
            queueList = new VisualElement();
            panel.Add(queueList);
            Root.Add(panel);
        }

        void RefreshQueue(World world, Village v)
        {
            SetText(queueTitle, $"Construction ({v.Queue.Count}/{World.MaxBuildQueue})");

            // Rebuild only when the queue's contents change; update countdowns in place otherwise.
            string signature = "";
            foreach (var o in v.Queue) signature += o.Id + ",";
            if ((string)queueList.userData != signature)
            {
                queueList.userData = signature;
                queueList.Clear();
                if (v.Queue.Count == 0) queueList.Add(Text("Nothing being built. Pick an upgrade from the list.", "row-info"));
                for (int i = 0; i < v.Queue.Count; i++)
                {
                    var order = v.Queue[i];
                    var item = new VisualElement();
                    item.AddToClassList("queue-item");
                    var line = new VisualElement();
                    line.AddToClassList("row-header");
                    line.Add(Text($"{Buildings.Get(order.Type).Name} → level {order.Level}", "row-title"));
                    var time = Text("", "row-level");
                    line.Add(time);
                    item.Add(line);
                    var bar = new VisualElement();
                    bar.AddToClassList("progress");
                    var fill = new VisualElement();
                    fill.AddToClassList("progress-fill");
                    bar.Add(fill);
                    item.Add(bar);
                    if (i == v.Queue.Count - 1)
                    {
                        var cancel = new Button(() => game.CancelLastBuild()) { text = "Cancel (full refund)" };
                        cancel.AddToClassList("btn");
                        cancel.AddToClassList("btn--small");
                        cancel.AddToClassList("cancel-btn");
                        item.Add(cancel);
                    }
                    queueList.Add(item);
                }
            }

            for (int i = 0; i < v.Queue.Count && i < queueList.childCount; i++)
            {
                var order = v.Queue[i];
                var item = queueList[i];
                var time = item.Q<VisualElement>(className: "row-header")[1] as Label;
                var fill = item.Q<VisualElement>(className: "progress-fill");
                if (order.Started)
                {
                    double left = Math.Max(0, order.FinishTime - world.Now);
                    SetText(time, Real(world, left));
                    fill.style.width = Length.Percent((float)(100 * (1 - left / order.Seconds)));
                }
                else
                {
                    SetText(time, $"waiting · {Real(world, order.Seconds)}");
                    fill.style.width = Length.Percent(0);
                }
            }
        }

        // ---------------------------------------------------------------- badges

        void BuildBadges()
        {
            badgeLayer = new VisualElement();
            badgeLayer.AddToClassList("screen");
            badgeLayer.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < badges.Length; i++)
            {
                var type = (BuildingType)i;
                var badge = new Button(() => Select(type));
                badge.AddToClassList("badge");
                badges[i] = badge;
                badgeLayer.Add(badge);
            }
            Root.Add(badgeLayer);
        }

        void RefreshBadges(Village v)
        {
            var panel = Root.panel;
            if (panel == null) return;
            for (int i = 0; i < badges.Length; i++)
            {
                var type = (BuildingType)i;
                int level = v.Level(type);
                string name = Buildings.Get(type).Name;
                badges[i].text = level > 0 ? $"{name} {level}" : $"{name} (build)";
                badges[i].EnableInClassList("badge--selected", Selected == type);

                // Just below the building's base, centred on it.
                var world = (Vector3)VillageView.PlotOf(type) + new Vector3(0f, -0.1f, 0f);
                Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, cam);
                badges[i].style.left = p.x;
                badges[i].style.top = p.y;
            }
        }

        // ---------------------------------------------------------------- refresh

        public void Refresh(World world, Village v)
        {
            foreach (var def in Buildings.Definitions)
            {
                var row = rows[(int)def.Type];
                var check = world.CheckBuild(v, def.Type);
                int level = v.Level(def.Type);
                int queued = v.QueuedCount(def.Type);
                SetText(row.Level, queued > 0 ? $"Level {level} (+{queued} queued)" : $"Level {level}");
                ShowUpgrade(world, check, row.Info, row.Upgrade, row.Reason);
            }

            RefreshDetail(world, v);
            RefreshQueue(world, v);
            RefreshBadges(v);
        }

        /// <summary>Fills in a building's next upgrade (cost line, button and blocking reason), for a list row or the details box.</summary>
        static void ShowUpgrade(World world, BuildCheck check, Label info, Button upgrade, Label reason)
        {
            if (check.Status == BuildStatus.MaxLevel)
            {
                SetText(info, "Fully upgraded.");
                Show(upgrade, false);
                SetText(reason, "");
                return;
            }

            var c = check.Cost;
            SetText(info, $"Wood {c.Wood:N0} · Clay {c.Clay:N0} · Iron {c.Iron:N0} · Pop {c.Population} · {Real(world, check.Seconds)}");
            Show(upgrade, true);
            SetText(upgrade, $"Upgrade to level {check.TargetLevel}");
            upgrade.SetEnabled(check.Status == BuildStatus.Ok);
            SetText(reason, ReasonText(world, check));
        }

        static string ReasonText(World world, BuildCheck check) => check.Status switch
        {
            BuildStatus.Ok => "",
            BuildStatus.QueueFull => $"The construction queue is full ({World.MaxBuildQueue} at a time).",
            BuildStatus.NeedsBuilding => $"Needs {Buildings.Get(check.Required.Building).Name} level {check.Required.Level}.",
            BuildStatus.FarmTooSmall => "Farm too small: not enough people to work it. Upgrade the Farm.",
            BuildStatus.WarehouseTooSmall => "Warehouse too small to hold the cost. Upgrade the Warehouse.",
            BuildStatus.NotEnoughResources => $"Not enough resources: ready in {Real(world, check.AffordableIn)}.",
            _ => "",
        };

        void RefreshDetail(World world, Village v)
        {
            Show(detail, Selected.HasValue);
            if (!Selected.HasValue) return;

            var type = Selected.Value;
            var def = Buildings.Get(type);
            int level = v.Level(type), next = v.NextLevel(type);
            SetText(detailTitle, $"{def.Name}  ·  level {level}");
            int points = Buildings.PointsAtLevel(type, level);
            string nextPoints = next <= def.MaxLevel ? $" (+{Buildings.PointsOfLevel(type, next)} at level {next})" : "";
            SetText(detailText, $"{def.Description}\nWorth {points:N0} village points{nextPoints}.");
            SetText(detailEffect, EffectText(world, type, level, next <= def.MaxLevel ? next : (int?)null));
            ShowUpgrade(world, world.CheckBuild(v, type), detailCost, detailUpgrade, detailReason);
        }

        /// <summary>What a building does at its current level, and at the next one.</summary>
        static string EffectText(World world, BuildingType type, int level, int? next)
        {
            string Then(string now, Func<int, string> at) => next.HasValue ? $"{now}  →  {at(next.Value)} at level {next.Value}" : now;
            float speed = world.Settings.Speed;
            switch (type)
            {
                case BuildingType.TimberCamp:
                case BuildingType.ClayPit:
                case BuildingType.IronMine:
                    string res = type == BuildingType.TimberCamp ? "wood" : type == BuildingType.ClayPit ? "clay" : "iron";
                    return Then($"Produces {Buildings.ProductionPerHour(level) * speed:N0} {res}/h", l => $"{Buildings.ProductionPerHour(l) * speed:N0}/h");
                case BuildingType.Warehouse:
                    return Then($"Stores {Buildings.StorageCapacity(level):N0} of each resource", l => $"{Buildings.StorageCapacity(l):N0}");
                case BuildingType.Farm:
                    return Then($"Supports {Buildings.FarmCapacity(level):N0} people", l => $"{Buildings.FarmCapacity(l):N0}");
                case BuildingType.TownHall:
                    double Faster(int l) => (1 - 1 / Math.Pow(Buildings.TownHallSpeedup, Math.Max(0, l - 1))) * 100;
                    return Then($"Construction {Faster(level):0}% faster", l => $"{Faster(l):0}%");
                case BuildingType.Wall:
                    double Bonus(int l) => (Buildings.WallDefenseMultiplier(l) - 1) * 100;
                    return Then(level > 0 ? $"Defenders +{Bonus(level):0}% stronger" : "No wall yet", l => $"+{Bonus(l):0}%");
                case BuildingType.Barracks:
                case BuildingType.Stable:
                case BuildingType.Workshop:
                    double speedup = Buildings.Get(type).TrainingSpeedup;
                    double Training(int l) => (1 - 1 / Math.Pow(speedup, Math.Max(0, l - 1))) * 100;
                    string trains = level > 0 ? $"Training {Training(level):0}% faster" : "Not built yet";
                    string unlocks = "";
                    if (next.HasValue)
                    {
                        var newUnits = Array.FindAll(Units.TrainedAt(type), u => u.RequiredLevel == next.Value);
                        if (newUnits.Length > 0) unlocks = $"\nLevel {next.Value} unlocks: {string.Join(", ", Array.ConvertAll(newUnits, u => u.Name))}";
                    }
                    return Then(trains, l => $"{Training(l):0}%") + unlocks;
                default:
                    return "";
            }
        }
    }
}
