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
            Root.style.top = 55; // below the top bar, which stays in view
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
            rallyPoint = new RallyPointView(game, sendTo, links);
            smithy = new SmithyView(game);
            market = new MarketView(game, links);
            info = new InfoView(game);
            Show(Root, false);
        }

        public void Open(BuildingType type)
        {
            Building = type;
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
            SetText(reason, BuildingText.Reason(world, check));
        }
    }

    // -------------------------------------------------------------------- headquarters

    /// <summary>The Headquarters: rename the village, the construction queue, and every building's next upgrade.</summary>
    class HeadquartersView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly TextField name;
        readonly Label queueTitle;
        readonly VisualElement queueList;
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
            queueList = Element("queue-list");
            for (int i = 0; i < World.MaxBuildQueue; i++)
            {
                var slot = Element("queue-slot");
                var text = Element("queue-slot-text");
                var line = Element("row-header");
                line.Add(Text("", "row-title"));
                line.Add(Text("", "row-level"));
                text.Add(line);
                var bar = Element("progress");
                bar.Add(Element("progress-fill"));
                text.Add(bar);
                slot.Add(text);
                slot.Add(ButtonWith("Cancel (full refund)", () => game.CancelLastBuild(), "btn", "btn--small", "cancel-btn", "queue-cancel"));
                queueList.Add(slot);
            }
            Root.Add(queueList);

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
            for (int i = 0; i < queueList.childCount; i++)
            {
                var slot = queueList[i];
                var title = (Label)slot[0][0][0];
                var time = (Label)slot[0][0][1];
                var bar = slot[0][1];
                var fill = bar[0];
                var cancel = slot[1];
                bool used = i < v.Queue.Count;
                slot.EnableInClassList("queue-slot--empty", !used);
                // Hidden rather than removed, so every slot keeps its size.
                bar.style.visibility = used ? Visibility.Visible : Visibility.Hidden;
                cancel.style.visibility = used && i == v.Queue.Count - 1 ? Visibility.Visible : Visibility.Hidden;
                if (!used)
                {
                    SetText(title, i == 0 ? "Nothing being built. Choose an upgrade below." : "Free slot");
                    SetText(time, "");
                    continue;
                }
                var order = v.Queue[i];
                SetText(title, $"{Buildings.Get(order.Type).Name} → level {order.Level}");
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

    /// <summary>A training building: its queue, and each unit it trains with a quantity picker.</summary>
    class RecruitView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly BuildingType building;
        readonly Label effect, locked;
        readonly VisualElement queue;
        readonly UpgradeBox upgrade;
        readonly Dictionary<UnitType, (VisualElement row, Label home, Label count, Label cost, Button recruit, Label reason)> rows =
            new Dictionary<UnitType, (VisualElement, Label, Label, Label, Button, Label)>();
        readonly int[] counts = new int[Units.Count];
        readonly VisualElement coinBox;
        readonly Label coinSummary, coinReason;
        readonly CostLine coinCost;
        readonly Button mintOne, mintMax;

        public RecruitView(MedievalWorldConquestGame game, BuildingType building)
        {
            this.game = game;
            this.building = building;
            Root = Element("window-section");
            effect = Text("", "detail-effect");
            Root.Add(effect);
            locked = Text("", "row-reason");
            Root.Add(locked);
            upgrade = new UpgradeBox(game);
            Root.Add(upgrade.Root);

            // The academy on a gold-coin world: minting coins for noble slots.
            if (building == BuildingType.Academy)
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

            Root.Add(Text("Training", "heading"));
            queue = Element("queue-list");
            Root.Add(queue);

            Root.Add(Text("Recruit", "heading"));
            foreach (var u in Units.TrainedAt(building)) Root.Add(UnitRow(u));
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

            var controls = Element("unit-controls");
            controls.Add(ButtonWith("-10", () => Adjust(type, -10), "btn", "btn--small", "count-btn"));
            controls.Add(ButtonWith("-1", () => Adjust(type, -1), "btn", "btn--small", "count-btn"));
            var count = Text("0", "unit-count");
            controls.Add(count);
            controls.Add(ButtonWith("+1", () => Adjust(type, 1), "btn", "btn--small", "count-btn"));
            controls.Add(ButtonWith("+10", () => Adjust(type, 10), "btn", "btn--small", "count-btn"));
            controls.Add(ButtonWith("Max", () => counts[(int)type] = int.MaxValue, "btn", "btn--small", "count-btn"));
            var recruit = ButtonWith("", () =>
            {
                if (game.Recruit(type, counts[(int)type])) counts[(int)type] = 0;
            }, "btn", "btn--small", "recruit-btn");
            controls.Add(recruit);
            row.Add(controls);
            var reason = Text("", "row-reason");
            row.Add(reason);
            rows[type] = (row, home, count, cost, recruit, reason);
            return row;
        }

        void Adjust(UnitType type, int delta)
        {
            long value = (long)counts[(int)type] + delta;
            counts[(int)type] = (int)Math.Max(0, Math.Min(int.MaxValue, value));
        }

        public void Refresh(World world, Village v)
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
            if (coinBox != null) RefreshCoins(world, v);
            RefreshQueue(world, v);
            foreach (var u in Units.TrainedAt(building)) RefreshUnit(world, v, u);
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

        void RefreshQueue(World world, Village v)
        {
            var orders = v.Recruitment.FindAll(o => o.Building == building);
            string signature = string.Join(",", orders.ConvertAll(o => o.Id.ToString()));
            if ((string)queue.userData != signature)
            {
                queue.userData = signature;
                queue.Clear();
                if (orders.Count == 0) queue.Add(Text("Nobody in training.", "row-info"));
                foreach (var order in orders)
                {
                    int id = order.Id;
                    var item = Element("queue-item", "recruit-item");
                    var line = Element("row-header");
                    line.Add(Text("", "row-title"));
                    line.Add(Text("", "row-level"));
                    item.Add(line);
                    var bar = Element("progress");
                    bar.Add(Element("progress-fill"));
                    item.Add(bar);
                    item.Add(ButtonWith("Cancel (refund untrained)", () => game.CancelRecruit(id), "btn", "btn--small", "cancel-btn"));
                    queue.Add(item);
                }
            }

            for (int i = 0; i < orders.Count && i < queue.childCount; i++)
            {
                var o = orders[i];
                var line = queue[i][0];
                SetText((Label)line[0], $"{Units.Get(o.Unit).Name}  {o.Done}/{o.Total}");
                SetText((Label)line[1], o.Started
                    ? $"next {Real(world, Math.Max(0, o.NextAt - world.Now))} · all {Real(world, Math.Max(0, o.FinishTime(world.Now) - world.Now))}"
                    : $"waiting · {Real(world, o.Remaining * o.SecondsEach)}");
                queue[i].Q<VisualElement>(className: "progress-fill").style.width = Length.Percent(100f * o.Done / o.Total);
            }
        }

        void RefreshUnit(World world, Village v, UnitDef u)
        {
            var (row, home, countLabel, cost, recruit, reason) = rows[u.Type];
            int max = world.MaxAffordable(v, u.Type);
            ref int count = ref counts[(int)u.Type];
            if (count == int.MaxValue) count = max; // "Max" pressed: what's affordable now
            SetText(countLabel, count.ToString("N0"));
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
