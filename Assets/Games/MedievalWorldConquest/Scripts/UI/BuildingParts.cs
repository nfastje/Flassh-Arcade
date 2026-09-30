using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
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
}
