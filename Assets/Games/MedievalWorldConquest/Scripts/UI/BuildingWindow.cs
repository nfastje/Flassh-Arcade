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

    // -------------------------------------------------------------------- shared pieces

    /// <summary>What a building costs to upgrade (with icons), how long it takes, and why it can't be yet.</summary>
    class CostLine
    {
        public VisualElement Root { get; }
        readonly Label wood, clay, iron, population, time;

        public CostLine()
        {
            Root = Element("cost-line");
            wood = Part(Icons.Wood);
            clay = Part(Icons.Clay);
            iron = Part(Icons.Iron);
            population = Part(Icons.Population);
            time = Text("", "cost-time");
            Root.Add(time);
        }

        Label Part(UnityEngine.Texture2D icon)
        {
            Root.Add(Icons.Element(icon, 16, "cost-icon"));
            var label = Text("", "cost-value");
            Root.Add(label);
            return label;
        }

        public void Set(World world, Cost cost, double seconds)
        {
            SetText(wood, $"{cost.Wood:N0}");
            SetText(clay, $"{cost.Clay:N0}");
            SetText(iron, $"{cost.Iron:N0}");
            SetText(population, $"{cost.Population:N0}");
            // (Something that happens at once, like minting a coin, shows no time.)
            Show(time, seconds > 0);
            SetText(time, Real(world, seconds));
        }
    }

    /// <summary>Wording about buildings shared by the windows.</summary>
    static class BuildingText
    {
        public static string Reason(World world, BuildCheck check) => check.Status switch
        {
            BuildStatus.Ok => "",
            BuildStatus.MaxLevel => "Fully upgraded.",
            BuildStatus.QueueFull => $"The construction queue is full ({World.MaxBuildQueue} at a time).",
            BuildStatus.NeedsBuilding => $"Needs {Buildings.Get(check.Required.Building).Name} level {check.Required.Level}.",
            BuildStatus.FarmTooSmall => "Farm too small: not enough people to work it. Upgrade the Farm.",
            BuildStatus.WarehouseTooSmall => "Warehouse too small to hold the cost. Upgrade the Warehouse.",
            BuildStatus.NotEnoughResources => $"Not enough resources: ready in {Real(world, check.AffordableIn)}.",
            _ => "",
        };

        /// <summary>
        /// Every requirement not yet met, with the village's level of each in brackets: "Needs Headquarters 20 (15),
        /// Smithy 20 (3) and Market 10 (0)." Empty if all are met.
        /// </summary>
        public static string Needs(Requirement[] requires, Village v)
        {
            var parts = new List<string>();
            foreach (var r in requires)
            {
                int now = v.Level(r.Building);
                if (now < r.Level) parts.Add($"{Buildings.Get(r.Building).Name} {r.Level} ({now})");
            }
            if (parts.Count == 0) return "";
            string last = parts[parts.Count - 1];
            return "Needs " + (parts.Count == 1 ? last : string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + last) + ".";
        }

        /// <summary>What a building does at its current level, and at the next one.</summary>
        public static string Effect(World world, BuildingType type, int level, int? next)
        {
            string Then(string now, Func<int, string> at) => next.HasValue ? $"{now}  →  {at(next.Value)} at level {next.Value}" : now;
            float speed = world.Settings.Speed;
            switch (type)
            {
                case BuildingType.TimberCamp:
                case BuildingType.ClayPit:
                case BuildingType.IronMine:
                    string res = type == BuildingType.TimberCamp ? "wood" : type == BuildingType.ClayPit ? "clay" : "iron";
                    return Then($"Produces {Buildings.ProductionPerHour(level) * speed:N0} {res} per hour", l => $"{Buildings.ProductionPerHour(l) * speed:N0}");
                case BuildingType.Warehouse:
                    return Then($"Stores {Buildings.StorageCapacity(level):N0} of each resource", l => $"{Buildings.StorageCapacity(l):N0}");
                case BuildingType.Farm:
                    return Then($"Feeds {Buildings.FarmCapacity(level):N0} people", l => $"{Buildings.FarmCapacity(l):N0}");
                case BuildingType.Headquarters:
                    double Faster(int l) => (1 - Math.Pow(Buildings.HeadquartersSpeedup, -l)) * 100;
                    return Then($"Construction {Faster(level):0}% faster", l => $"{Faster(l):0}%");
                case BuildingType.Wall:
                    double Bonus(int l) => (Buildings.WallDefenseMultiplier(l) - 1) * 100;
                    return Then(level > 0 ? $"Defenders fight {Bonus(level):0}% harder" : "No wall yet", l => $"+{Bonus(l):0}%");
                case BuildingType.Barracks:
                case BuildingType.Stable:
                case BuildingType.Workshop:
                    double Training(int l) => (1 - Math.Pow(Units.RecruitSpeedup, -(l - 1))) * 100;
                    string trains = level > 0 ? $"Trains {Training(level):0}% faster than at level 1" : "Not built yet";
                    return Then(trains, l => $"{Training(l):0}%") + Unlocks(type, next);
                case BuildingType.Smithy:
                    double Research(int l) => (1 - Math.Pow(Buildings.ResearchSpeedup, -(l - 1))) * 100;
                    string researches = level > 0 ? $"Researches {Research(level):0}% faster than at level 1" : "Not built yet";
                    return Then(researches, l => $"{Research(l):0}%") + Unlocks(type, next);
                case BuildingType.Market:
                    return Then($"{Buildings.Merchants(level):N0} merchants, carrying {Buildings.Merchants(level) * Buildings.MerchantCarry:N0} resources in all",
                        l => $"{Buildings.Merchants(l):N0} merchants");
                case BuildingType.HidingPlace:
                    return Then(level > 0 ? $"Hides {Buildings.HiddenCapacity(level):N0} of each resource from raiders" : "Nothing hidden yet",
                        l => $"{Buildings.HiddenCapacity(l):N0}");
                case BuildingType.Academy:
                    return level > 0 ? "Trains noblemen." : "Unlocks noblemen, who win other villages over.";
                case BuildingType.RallyPoint:
                    return "Sends and receives your troops.";
                default:
                    return "";
            }
        }

        /// <summary>"Level 5 opens up: Archer": units that can be trained or researched once a building reaches its next level.</summary>
        static string Unlocks(BuildingType type, int? next)
        {
            if (!next.HasValue) return "";
            var trained = new List<string>();
            var researched = new List<string>();
            foreach (var u in Units.Definitions)
            {
                if (!u.NeedsResearch && u.Building == type && u.RequiredLevel == next.Value) trained.Add(u.Name);
                if (Array.Exists(u.ResearchRequires, r => r.Building == type && r.Level == next.Value)) researched.Add(u.Name);
            }
            string text = "";
            if (trained.Count > 0) text += $"\nLevel {next.Value} unlocks: {string.Join(", ", trained)}";
            if (researched.Count > 0) text += $"\nLevel {next.Value} lets the smithy research: {string.Join(", ", researched)}";
            return text;
        }

        /// <summary>Why merchants can't go, in words.</summary>
        public static string Trade(TradeStatus status) => status switch
        {
            TradeStatus.Ok => "",
            TradeStatus.NoMarket => "You need a market first.",
            TradeStatus.NothingToSend => "Choose some resources.",
            TradeStatus.SameVillage => "That's this village.",
            TradeStatus.InvalidTarget => "There's no village there.",
            TradeStatus.NotEnoughResources => "Not enough resources.",
            TradeStatus.NotEnoughMerchants => "Not enough merchants at home.",
            TradeStatus.UnfairRatio => "Offers can ask at most twice what they give, and give at most twice what they ask.",
            TradeStatus.OwnOffer => "That's your own offer.",
            TradeStatus.OfferGone => "That offer is gone.",
            _ => "",
        };
    }

    /// <summary>A building's upgrade: cost line, button and the reason it's blocked, if it is.</summary>
    class UpgradeBox
    {
        public VisualElement Root { get; }
        readonly CostLine cost = new CostLine();
        readonly Button button;
        readonly Label reason;
        BuildingType type;

        public UpgradeBox(MedievalWorldConquestGame game)
        {
            Root = Element("upgrade-box");
            Root.Add(cost.Root);
            button = ButtonWith("", () => game.QueueBuild(type), "btn", "btn--small", "build-btn");
            Root.Add(button);
            reason = Text("", "row-reason");
            Root.Add(reason);
        }

        public void Refresh(World world, Village v, BuildingType building)
        {
            type = building;
            var check = world.CheckBuild(v, building);
            bool maxed = check.Status == BuildStatus.MaxLevel;
            Show(cost.Root, !maxed);
            Show(button, !maxed);
            if (!maxed)
            {
                cost.Set(world, check.Cost, check.Seconds);
                SetText(button, check.TargetLevel == 1 ? "Build" : $"Upgrade to level {check.TargetLevel}");
                button.SetEnabled(check.Status == BuildStatus.Ok);
            }
            // (Everything a building still needs at once, not one requirement at a time.)
            SetText(reason, check.Status == BuildStatus.NeedsBuilding
                ? BuildingText.Needs(Buildings.Get(building).Requires, v)
                : BuildingText.Reason(world, check));
        }
    }

    // -------------------------------------------------------------------- headquarters

    /// <summary>
    /// A queue shown as a fixed number of slots (one for each order it can hold), used or free, so the screen below
    /// it never moves as orders come and go. Each used slot: what, how long, a progress bar and a cancel button.
    /// </summary>
    class QueueSlots
    {
        public VisualElement Root { get; }
        readonly List<(VisualElement slot, Label title, Label time, VisualElement bar, VisualElement fill, Button cancel)> slots =
            new List<(VisualElement, Label, Label, VisualElement, VisualElement, Button)>();
        readonly Action[] cancels;

        /// <param name="compact">Slimmer slots, for screens with several queues.</param>
        public QueueSlots(int count, string cancelText, bool compact = false)
        {
            Root = Element("queue-list");
            cancels = new Action[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var slot = Element("queue-slot");
                if (compact) slot.AddToClassList("queue-slot--compact");
                var text = Element("queue-slot-text");
                var line = Element("row-header");
                var title = Text("", "row-title");
                var time = Text("", "row-level");
                line.Add(title);
                line.Add(time);
                text.Add(line);
                var bar = Element("progress");
                var fill = Element("progress-fill");
                bar.Add(fill);
                text.Add(bar);
                slot.Add(text);
                var cancel = ButtonWith(cancelText, () => cancels[index]?.Invoke(), "btn", "btn--small", "cancel-btn", "queue-cancel");
                slot.Add(cancel);
                slots.Add((slot, title, time, bar, fill, cancel));
                Root.Add(slot);
            }
        }

        public int Count => slots.Count;

        /// <summary>A slot in use. <paramref name="progress"/> from 0 to 1; <paramref name="cancel"/> null for no cancel button.</summary>
        public void SetUsed(int i, string title, string time, double progress, Action cancel)
        {
            var s = slots[i];
            s.slot.EnableInClassList("queue-slot--empty", false);
            SetText(s.title, title);
            SetText(s.time, time);
            // Hidden rather than removed, so every slot keeps its size.
            s.bar.style.visibility = Visibility.Visible;
            s.fill.style.width = Length.Percent((float)(100 * Math.Max(0, Math.Min(1, progress))));
            cancels[i] = cancel;
            s.cancel.style.visibility = cancel != null ? Visibility.Visible : Visibility.Hidden;
        }

        /// <summary>A free slot, with a word about it.</summary>
        public void SetFree(int i, string text)
        {
            var s = slots[i];
            s.slot.EnableInClassList("queue-slot--empty", true);
            SetText(s.title, text);
            SetText(s.time, "");
            s.bar.style.visibility = Visibility.Hidden;
            cancels[i] = null;
            s.cancel.style.visibility = Visibility.Hidden;
        }
    }

    /// <summary>The Headquarters: rename the village, the construction queue, and every building's next upgrade.</summary>
    class HeadquartersView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly TextField name;
        readonly Label queueTitle;
        readonly QueueSlots queueSlots;
        readonly Dictionary<BuildingType, (Label level, Label effect, UpgradeBox upgrade)> rows = new Dictionary<BuildingType, (Label, Label, UpgradeBox)>();
        Village shownFor;

        public HeadquartersView(MedievalWorldConquestGame game)
        {
            this.game = game;
            Root = Element("window-section");

            // Renaming the village.
            var nameRow = Element("name-row", "rename-row");
            nameRow.Add(Text("Village name", "row-title"));
            name = new TextField { maxLength = World.MaxVillageNameLength };
            name.AddToClassList("rename-field");
            nameRow.Add(name);
            nameRow.Add(ButtonWith("Rename", () => game.RenameVillage(name.value), "btn", "btn--small"));
            Root.Add(nameRow);

            // The queue always takes the same room (a slot for each order it can hold), so starting a build
            // doesn't push the list of buildings down.
            queueTitle = Text("", "heading");
            Root.Add(queueTitle);
            queueSlots = new QueueSlots(World.MaxBuildQueue, "Cancel (full refund)");
            Root.Add(queueSlots.Root);

            Root.Add(Text("Buildings", "heading"));
            foreach (var def in Buildings.Definitions)
            {
                var row = Element("build-row", "hq-row");
                var left = Element("hq-row-name");
                var header = Element("row-header");
                header.Add(Text(def.Name, "row-title"));
                var level = Text("", "row-level");
                header.Add(level);
                left.Add(header);
                var effect = Text("", "row-info");
                left.Add(effect);
                row.Add(left);
                var upgrade = new UpgradeBox(game);
                row.Add(upgrade.Root);
                rows[def.Type] = (level, effect, upgrade);
                Root.Add(row);
            }
        }

        /// <summary>Called when the window opens, so the name box starts with the village's current name.</summary>
        public void OnOpen() => shownFor = null;

        public void Refresh(World world, Village v)
        {
            if (shownFor != v)
            {
                shownFor = v;
                name.SetValueWithoutNotify(v.Name);
            }

            RefreshQueue(world, v);
            foreach (var def in Buildings.Definitions)
            {
                var (level, effect, upgrade) = rows[def.Type];
                int now = v.Level(def.Type), queued = v.QueuedCount(def.Type), next = v.NextLevel(def.Type);
                SetText(level, queued > 0 ? $"level {now} (+{queued} queued)" : now > 0 ? $"level {now}" : "not built");
                SetText(effect, BuildingText.Effect(world, def.Type, now, next <= def.MaxLevel ? next : (int?)null));
                upgrade.Refresh(world, v, def.Type);
            }
        }

        void RefreshQueue(World world, Village v)
        {
            SetText(queueTitle, $"Construction ({v.Queue.Count}/{World.MaxBuildQueue})");
            for (int i = 0; i < queueSlots.Count; i++)
            {
                if (i >= v.Queue.Count)
                {
                    queueSlots.SetFree(i, i == 0 ? "Nothing being built. Choose an upgrade below." : "Free slot");
                    continue;
                }
                var order = v.Queue[i];
                // Only the last order can be canceled: the ones after depend on the levels before them.
                Action cancel = i == v.Queue.Count - 1 ? game.CancelLastBuild : (Action)null;
                string title = $"{Buildings.Get(order.Type).Name} → level {order.Level}";
                if (order.Started)
                {
                    double left = Math.Max(0, order.FinishTime - world.Now);
                    queueSlots.SetUsed(i, title, Real(world, left), 1 - left / order.Seconds, cancel);
                }
                else queueSlots.SetUsed(i, title, $"waiting · {Real(world, order.Seconds)}", 0, cancel);
            }
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

    // -------------------------------------------------------------------- recruitment

    /// <summary>
    /// A training building (its level, queue, and each unit it trains), or, as Tribal Wars' Recruit screen, the
    /// barracks, stable and workshop together: every unit they train, and all their queues, in one place. Type how
    /// many to train, or click "(max …)" for as many as can be afforded.
    /// </summary>
    class RecruitView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly BuildingType building;
        readonly BuildingType[] buildings;
        /// <summary>Whether this is the screen for all the training buildings at once (no building of its own).</summary>
        readonly bool combined;
        readonly List<UnitDef> units = new List<UnitDef>();
        readonly Label effect, locked;
        readonly Dictionary<BuildingType, (Label heading, QueueSlots slots)> sections = new Dictionary<BuildingType, (Label, QueueSlots)>();
        readonly UpgradeBox upgrade;
        readonly Dictionary<UnitType, (VisualElement row, Label home, IntegerField amount, Button max, Label cost, Button recruit, Label reason)> rows =
            new Dictionary<UnitType, (VisualElement, Label, IntegerField, Button, Label, Button, Label)>();
        readonly int[] counts = new int[Units.Count];
        readonly VisualElement coinBox;
        readonly Label coinSummary, coinReason;
        readonly CostLine coinCost;
        readonly Button mintOne, mintMax;

        public RecruitView(MedievalWorldConquestGame game, params BuildingType[] buildings)
        {
            this.game = game;
            this.buildings = buildings;
            building = buildings[0];
            combined = buildings.Length > 1;
            foreach (var b in buildings) units.AddRange(Units.TrainedAt(b));
            Root = Element("window-section");
            if (!combined)
            {
                effect = Text("", "detail-effect");
                Root.Add(effect);
                locked = Text("", "row-reason");
                Root.Add(locked);
                upgrade = new UpgradeBox(game);
                Root.Add(upgrade.Root);
            }

            // The academy on a gold-coin world: minting coins for noble slots.
            if (building == BuildingType.Academy && !combined)
            {
                coinBox = Element("coin-box");
                coinBox.Add(Text("Gold coins", "heading"));
                coinSummary = Text("", "row-info");
                coinBox.Add(coinSummary);
                var mint = Element("build-row", "hq-row");
                var left = Element("hq-row-name");
                left.Add(Text("Mint a gold coin", "row-title"));
                coinCost = new CostLine();
                left.Add(coinCost.Root);
                mint.Add(left);
                var buttons = Element("upgrade-box");
                mintOne = ButtonWith("Mint 1", () => game.MintCoins(1), "btn", "btn--small", "build-btn");
                mintMax = ButtonWith("Mint all you can", () => game.MintCoins(int.MaxValue), "btn", "btn--small", "build-btn");
                buttons.Add(mintOne);
                buttons.Add(mintMax);
                coinReason = Text("", "row-reason");
                buttons.Add(coinReason);
                mint.Add(buttons);
                coinBox.Add(mint);
                Root.Add(coinBox);
            }

            // Each building: what it's training (a slot for every batch it can queue, used or free), then its units.
            foreach (var b in buildings)
            {
                var heading = Text("", "heading");
                Root.Add(heading);
                var slots = new QueueSlots(World.MaxRecruitQueue, "Cancel (refund untrained)", compact: true);
                Root.Add(slots.Root);
                sections[b] = (heading, slots);
                if (!combined) Root.Add(Text("Recruit", "heading"));
                foreach (var u in Units.TrainedAt(b)) Root.Add(UnitRow(u));
            }
        }

        VisualElement UnitRow(UnitDef u)
        {
            var type = u.Type;
            var row = Element("build-row", "unit-row");
            var header = Element("unit-header");
            header.Add(Icons.Element(Icons.Unit(type), 28, "unit-icon"));
            var names = Element("unit-names");
            names.Add(Text(u.Name, "row-title"));
            var home = Text("", "row-level");
            names.Add(home);
            header.Add(names);
            row.Add(header);
            row.Add(Text(u.Description, "row-info"));
            row.Add(Text($"Attack {u.Attack} · Defense {u.DefenseInfantry} / {u.DefenseCavalry} / {u.DefenseArcher} (inf / cav / arch) · Carries {u.Carry}", "row-info", "unit-stats"));
            var cost = Text("", "row-info");
            row.Add(cost);

            // How many: typed, or "(max …)" for all that can be afforded.
            var controls = Element("unit-controls");
            var amount = new IntegerField { value = 0 };
            amount.AddToClassList("amount-field");
            amount.AddToClassList("send-amount");
            amount.RegisterValueChangedCallback(e => counts[(int)type] = Math.Max(0, e.newValue));
            controls.Add(amount);
            var max = Link("", () => counts[(int)type] = int.MaxValue, "send-all");
            max.tooltip = "As many as you can afford";
            controls.Add(max);
            var recruit = ButtonWith("", () =>
            {
                if (game.Recruit(type, counts[(int)type])) counts[(int)type] = 0;
            }, "btn", "btn--small", "recruit-btn");
            controls.Add(recruit);
            row.Add(controls);
            var reason = Text("", "row-reason");
            row.Add(reason);
            rows[type] = (row, home, amount, max, cost, recruit, reason);
            return row;
        }

        public void Refresh(World world, Village v)
        {
            if (!combined)
            {
                var def = Buildings.Get(building);
                int level = v.Level(building), next = v.NextLevel(building);
                SetText(effect, BuildingText.Effect(world, building, level, next <= def.MaxLevel ? next : (int?)null));
                Show(locked, level == 0);
                if (level == 0)
                {
                    var needs = Array.ConvertAll(def.Requires, r => $"{Buildings.Get(r.Building).Name} {r.Level}");
                    SetText(locked, needs.Length > 0 ? $"Not built yet (needs {string.Join(" and ", needs)})." : "Not built yet.");
                }
                upgrade.Refresh(world, v, building);
            }
            if (coinBox != null) RefreshCoins(world, v);
            RefreshQueue(world, v);
            foreach (var u in units) RefreshUnit(world, v, u);
        }

        /// <summary>Coins minted, noble slots in use and free, and what the next slot needs.</summary>
        void RefreshCoins(World world, Village v)
        {
            Show(coinBox, world.Settings.GoldCoins);
            if (!world.Settings.GoldCoins) return;
            var human = world.HumanPlayer;
            int coins = human?.Coins ?? 0, slots = World.SlotsFor(coins), used = human == null ? 0 : world.NobleSlotsUsed(human);
            int toNext = World.CoinsForSlots(slots + 1) - coins;
            SetText(coinSummary,
                $"Coins minted: {coins:N0}  ·  noble slots: {used:N0} used of {slots:N0}  ·  {toNext:N0} more coin{(toNext == 1 ? "" : "s")} for the next slot.\n" +
                "Each nobleman needs a free slot, and so does every village you hold beyond your first. Each slot takes one more coin than the last (1, 3, 6, 10… in all).");
            var check = world.CheckMint(v, 1);
            coinCost.Set(world, World.CoinCost, 0);
            mintOne.SetEnabled(check.Status == MintStatus.Ok);
            mintMax.SetEnabled(check.Status == MintStatus.Ok);
            SetText(mintMax, check.MaxAffordable > 1 ? $"Mint {check.MaxAffordable:N0}" : "Mint all you can");
            SetText(coinReason, check.Status switch
            {
                MintStatus.NoAcademy => "Build the academy to mint coins.",
                MintStatus.NotEnoughResources => double.IsInfinity(check.AffordableIn)
                    ? "A coin costs more than your warehouse holds."
                    : $"Not enough resources: ready in {Real(world, check.AffordableIn)}.",
                _ => "",
            });
        }

        /// <summary>Each building's heading and what it's training, a slot per batch.</summary>
        void RefreshQueue(World world, Village v)
        {
            foreach (var b in buildings)
            {
                var (heading, slots) = sections[b];
                var orders = v.Recruitment.FindAll(o => o.Building == b);
                int level = v.Level(b);
                SetText(heading, combined
                    ? $"{Buildings.Get(b).Name}  ·  {(level > 0 ? $"level {level}" : "not built")}  ·  training {orders.Count}/{World.MaxRecruitQueue}"
                    : $"Training ({orders.Count}/{World.MaxRecruitQueue})");
                for (int i = 0; i < slots.Count; i++)
                {
                    if (i >= orders.Count)
                    {
                        slots.SetFree(i, i > 0 ? "Free slot" : level > 0 ? "Nobody in training." : "Not built yet.");
                        continue;
                    }
                    var o = orders[i];
                    int id = o.Id;
                    string time = o.Started
                        ? $"next {Real(world, Math.Max(0, o.NextAt - world.Now))} · all {Real(world, Math.Max(0, o.FinishTime(world.Now) - world.Now))}"
                        : $"waiting · {Real(world, o.Remaining * o.SecondsEach)}";
                    slots.SetUsed(i, $"{Units.Get(o.Unit).Name}  {o.Done}/{o.Total}", time, (double)o.Done / o.Total, () => game.CancelRecruit(id));
                }
            }
        }

        void RefreshUnit(World world, Village v, UnitDef u)
        {
            var (row, home, amount, maxLink, cost, recruit, reason) = rows[u.Type];
            int max = world.MaxAffordable(v, u.Type);
            ref int count = ref counts[(int)u.Type];
            if (count == int.MaxValue) count = max; // "(max …)" clicked: what's affordable now
            if (amount.value != count) amount.SetValueWithoutNotify(count);
            SetText(maxLink, $"(max {max:N0})");
            SetText(home, $"{v.TroopCount(u.Type):N0} at home");

            var check = world.CheckRecruit(v, u.Type, Math.Max(1, count));
            var c = world.UnitCost(u.Type);
            SetText(cost, $"Each: wood {c.Wood} · clay {c.Clay} · iron {c.Iron} · {c.Population} pop · {Real(world, check.SecondsEach)}  ·  {u.MinutesPerField / world.Settings.Speed:0.#} min per field");
            recruit.text = count > 0 ? $"Recruit {count:N0} ({Real(world, check.SecondsEach * count)})" : "Recruit";
            recruit.SetEnabled(count > 0 && check.Status == RecruitStatus.Ok);
            row.EnableInClassList("unit-row--locked", check.Status == RecruitStatus.NeedsBuilding || check.Status == RecruitStatus.NeedsResearch);
            int room = v.FreePopulation / Math.Max(1, c.Population);
            SetText(reason, count == 0 && check.Status == RecruitStatus.Ok ? $"You can afford up to {max:N0}." : check.Status switch
            {
                RecruitStatus.Ok => "",
                RecruitStatus.NeedsBuilding => $"Needs {Buildings.Get(check.Required.Building).Name} level {check.Required.Level}.",
                RecruitStatus.NeedsResearch => v.IsBeingResearched(u.Type) ? "Being researched at the smithy." : "Research it at the smithy first.",
                RecruitStatus.NeedsCoins => "No free noble slot: mint more gold coins above.",
                RecruitStatus.QueueFull => $"This building's queue is full ({World.MaxRecruitQueue} batches).",
                RecruitStatus.FarmTooSmall => $"Not enough population: room for {room:N0} more. Upgrade the Farm.",
                RecruitStatus.NotEnoughResources => double.IsInfinity(check.AffordableIn)
                    ? "Costs more than your warehouse holds: recruit fewer at a time."
                    : $"Not enough resources: affordable in {Real(world, check.AffordableIn)} (up to {max:N0} now).",
                _ => "",
            });
        }
    }

    // -------------------------------------------------------------------- rally point

    /// <summary>
    /// The rally point: the troops at home and their strength, sending troops to any map field, and every troop
    /// movement (attacks, support, returns, incoming attacks, and support stationed elsewhere, which can be recalled).
    /// </summary>
    class RallyPointView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly Func<int, int, bool> sendTo;
        readonly Label[] homeCounts = new Label[Units.Count];
        readonly Label strength, sendMessage;
        readonly IntegerField targetX, targetY;
        readonly ScrollView movements;
        readonly UiLinks links;
        readonly List<MovementRow> rows = new List<MovementRow>();
        string movementsSignature;

        public RallyPointView(MedievalWorldConquestGame game, Func<int, int, bool> sendTo, UiLinks links)
        {
            this.game = game;
            this.sendTo = sendTo;
            this.links = links;
            Root = Element("window-section");

            Root.Add(Text("Troops at home", "heading"));
            var grid = Element("troop-grid");
            foreach (var type in Units.InDisplayOrder)
            {
                int i = (int)type;
                var cell = Element("troop-grid-cell");
                cell.Add(Icons.Element(Icons.Unit(type), 22));
                homeCounts[i] = Text("", "garrison-count");
                cell.Add(homeCounts[i]);
                cell.tooltip = Units.Get(type).Name;
                grid.Add(cell);
            }
            Root.Add(grid);
            strength = Text("", "row-info");
            Root.Add(strength);

            Root.Add(Text("Send troops", "heading"));
            var send = Element("send-to-row");
            send.Add(Text("Target field", "row-title"));
            targetX = new IntegerField { value = World.MapSize / 2 };
            targetY = new IntegerField { value = World.MapSize / 2 };
            targetX.AddToClassList("coord-field");
            targetY.AddToClassList("coord-field");
            send.Add(targetX);
            send.Add(Text("|", "row-title"));
            send.Add(targetY);
            send.Add(ButtonWith("Choose troops…", () =>
            {
                if (!sendTo(targetX.value, targetY.value)) SetText(sendMessage, $"There's no village at ({targetX.value}|{targetY.value}).");
                else SetText(sendMessage, "");
            }, "btn", "btn--small"));
            Root.Add(send);
            sendMessage = Text("You can also pick a village on the Map and choose Attack or Support.", "row-info");
            Root.Add(sendMessage);

            Root.Add(Text("Troop movements", "heading"));
            movements = new ScrollView(ScrollViewMode.Vertical);
            movements.AddToClassList("movements");
            Root.Add(movements);
        }

        public void Refresh(World world, Village v)
        {
            long attack = 0, defInf = 0, defCav = 0, carry = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units.Get((UnitType)i);
                int n = v.TroopCount(u.Type);
                SetText(homeCounts[i], n.ToString("N0"));
                homeCounts[i].EnableInClassList("garrison-count--none", n == 0);
                attack += (long)n * u.Attack;
                defInf += (long)n * u.DefenseInfantry;
                defCav += (long)n * u.DefenseCavalry;
                carry += (long)n * u.Carry;
            }
            SetText(strength, $"Attack {attack:N0} · defense {defInf:N0} vs infantry, {defCav:N0} vs cavalry · carries {carry:N0} · troop population {v.TroopPopulation:N0} (free {v.FreePopulation:N0})");
            RefreshMovements(world);
        }

        void RefreshMovements(World world)
        {
            var human = world.HumanPlayer;
            if (human == null) return;
            var commands = world.MovementsFor(human.Id);
            var stationed = world.Villages.FindAll(x => x.Supports.Exists(g => g.OwnerId == human.Id));

            string signature = string.Join(",", commands.ConvertAll(c => c.Id.ToString())) + "|" + string.Join(",", stationed.ConvertAll(x => x.Id.ToString()));
            if (signature != movementsSignature)
            {
                movementsSignature = signature;
                movements.Clear();
                rows.Clear();
                if (commands.Count == 0 && stationed.Count == 0) movements.Add(Text("No troops on the move.", "row-info"));
                foreach (var c in commands)
                {
                    // Each movement names its villages and players as links.
                    var row = new MovementRow(links);
                    rows.Add(row);
                    movements.Add(row.Root);
                }
                foreach (var host in stationed)
                    foreach (var g in host.Supports)
                    {
                        if (g.OwnerId != human.Id) continue;
                        int hostId = host.Id, fromId = g.FromVillageId;
                        var item = Element("queue-item", "recruit-item");
                        item.Add(Text($"Supporting {host.Name} ({host.X}|{host.Y}): {TroopSummary(g.Troops)}", "row-info"));
                        item.Add(ButtonWith("Recall", () => game.Recall(hostId, fromId), "btn", "btn--small", "cancel-btn"));
                        movements.Add(item);
                    }
            }

            for (int i = 0; i < rows.Count && i < commands.Count; i++) rows[i].Update(world, commands[i]);
        }

        static string TroopSummary(int[] troops)
        {
            var parts = new List<string>();
            for (int i = 0; i < troops.Length && i < Units.Count; i++)
                if (troops[i] > 0) parts.Add($"{troops[i]:N0} {Units.Get((UnitType)i).Name}");
            return parts.Count > 0 ? string.Join(", ", parts) : "no troops";
        }
    }
}
