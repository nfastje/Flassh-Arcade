using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    // -------------------------------------------------------------------- smithy

    /// <summary>The smithy: its research queue, and every unit that needs researching with its cost or what it still needs.</summary>
    class SmithyView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly Label effect;
        readonly UpgradeBox upgrade;
        readonly VisualElement queue;
        readonly Dictionary<UnitType, (Label state, CostLine cost, Button research, Label reason)> rows =
            new Dictionary<UnitType, (Label, CostLine, Button, Label)>();

        public SmithyView(MedievalWorldConquestGame game)
        {
            this.game = game;
            Root = Element("window-section");
            effect = Text("", "detail-effect");
            Root.Add(effect);
            upgrade = new UpgradeBox(game);
            Root.Add(upgrade.Root);

            Root.Add(Text("Researching", "heading"));
            queue = Element("queue-list");
            Root.Add(queue);

            Root.Add(Text("Research", "heading"));
            Root.Add(Text("Spearmen need no research, and noblemen come from the academy. Every other unit is researched here once; after that the village can train as many as it likes.", "row-info"));
            foreach (var type in Units.InDisplayOrder)
            {
                var u = Units.Get(type);
                if (!u.NeedsResearch) continue;
                var row = Element("build-row", "hq-row");
                var left = Element("hq-row-name");
                var header = Element("unit-header");
                header.Add(Icons.Element(Icons.Unit(type), 28, "unit-icon"));
                var names = Element("unit-names");
                names.Add(Text(u.Name, "row-title"));
                var state = Text("", "row-level");
                names.Add(state);
                header.Add(names);
                left.Add(header);
                row.Add(left);

                var box = Element("upgrade-box");
                var cost = new CostLine();
                box.Add(cost.Root);
                var research = ButtonWith("Research", () => game.StartResearch(type), "btn", "btn--small", "build-btn");
                box.Add(research);
                var reason = Text("", "row-reason");
                box.Add(reason);
                row.Add(box);
                rows[type] = (state, cost, research, reason);
                Root.Add(row);
            }
        }

        public void Refresh(World world, Village v)
        {
            var def = Buildings.Get(BuildingType.Smithy);
            int level = v.Level(BuildingType.Smithy), next = v.NextLevel(BuildingType.Smithy);
            SetText(effect, BuildingText.Effect(world, BuildingType.Smithy, level, next <= def.MaxLevel ? next : (int?)null));
            upgrade.Refresh(world, v, BuildingType.Smithy);
            RefreshQueue(world, v);

            foreach (var pair in rows)
            {
                var (state, cost, research, reason) = pair.Value;
                var check = world.CheckResearch(v, pair.Key);
                bool done = check.Status == ResearchStatus.AlreadyResearched;
                SetText(state, done ? "researched" : check.Status == ResearchStatus.InProgress ? "being researched" : "not researched");
                Show(cost.Root, !done && check.Status != ResearchStatus.InProgress);
                Show(research, !done && check.Status != ResearchStatus.InProgress);
                cost.Set(world, check.Cost, check.Seconds);
                research.SetEnabled(check.Status == ResearchStatus.Ok);
                SetText(reason, check.Status switch
                {
                    ResearchStatus.NeedsBuilding => $"Needs {Buildings.Get(check.Required.Building).Name} level {check.Required.Level}.",
                    ResearchStatus.QueueFull => $"The smithy's queue is full ({World.MaxResearchQueue} at a time).",
                    ResearchStatus.NotEnoughResources => double.IsInfinity(check.AffordableIn)
                        ? "Costs more than your warehouse holds."
                        : $"Not enough resources: ready in {Real(world, check.AffordableIn)}.",
                    _ => "",
                });
            }
        }

        void RefreshQueue(World world, Village v)
        {
            string signature = string.Join(",", v.Researching.ConvertAll(o => o.Id.ToString()));
            if ((string)queue.userData != signature)
            {
                queue.userData = signature;
                queue.Clear();
                if (v.Researching.Count == 0) queue.Add(Text("Nothing being researched.", "row-info"));
                foreach (var order in v.Researching)
                {
                    int id = order.Id;
                    var item = Element("queue-item", "recruit-item");
                    var line = Element("row-header");
                    line.Add(Text(Units.Get(order.Unit).Name, "row-title"));
                    line.Add(Text("", "row-level"));
                    item.Add(line);
                    var bar = Element("progress");
                    bar.Add(Element("progress-fill"));
                    item.Add(bar);
                    item.Add(ButtonWith("Cancel (full refund)", () => game.CancelResearch(id), "btn", "btn--small", "cancel-btn"));
                    queue.Add(item);
                }
            }

            for (int i = 0; i < v.Researching.Count && i < queue.childCount; i++)
            {
                var order = v.Researching[i];
                var time = (Label)queue[i][0][1];
                var fill = queue[i].Q<VisualElement>(className: "progress-fill");
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

    // -------------------------------------------------------------------- market

    /// <summary>
    /// The market: sending resources to any village by its coordinates, other lords' offers to take, and the
    /// player's own offers.
    /// </summary>
    class MarketView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly UiLinks links;
        readonly Label effect, merchants, sendMessage, offerMessage;
        readonly UpgradeBox upgrade;
        readonly IntegerField targetX, targetY, sendWood, sendClay, sendIron;
        readonly IntegerField sellAmount, buyAmount, lots;
        readonly Button[] sellPicks = new Button[3], buyPicks = new Button[3];
        ResourceType sell = ResourceType.Wood, buy = ResourceType.Clay;
        readonly VisualElement othersList, ownList;
        string othersSignature, ownSignature;


        public MarketView(MedievalWorldConquestGame game, UiLinks links)
        {
            this.game = game;
            this.links = links;
            Root = Element("window-section");
            effect = Text("", "detail-effect");
            Root.Add(effect);
            merchants = Text("", "row-info");
            Root.Add(merchants);
            upgrade = new UpgradeBox(game);
            Root.Add(upgrade.Root);

            // Sending resources.
            Root.Add(Text("Send resources", "heading"));
            var to = Element("send-to-row");
            to.Add(Text("To village", "row-title"));
            targetX = Field(to, World.MapSize / 2);
            to.Add(Text("|", "row-title"));
            targetY = Field(to, World.MapSize / 2);
            Root.Add(to);
            var goods = Element("send-to-row");
            sendWood = Amount(goods, Icons.Wood);
            sendClay = Amount(goods, Icons.Clay);
            sendIron = Amount(goods, Icons.Iron);
            goods.Add(ButtonWith("Send", Send, "btn", "btn--small"));
            Root.Add(goods);
            sendMessage = Text("Merchants carry 1,000 each. Anything the warehouse there can't hold is lost.", "row-info");
            Root.Add(sendMessage);

            // Other lords' offers.
            Root.Add(Text("Offers from other lords", "heading"));
            othersList = Element("queue-list");
            Root.Add(othersList);

            // The player's own offers: what to give, what for, and how many times over, a row each.
            Root.Add(Text("Your offers", "heading"));
            var give = Element("send-to-row", "offer-row");
            give.Add(Text("Give", "row-title", "offer-label"));
            sellAmount = Field(give, 1000);
            Picks(give, sellPicks, r => { sell = r; ShowPicks(); });
            Root.Add(give);
            var get = Element("send-to-row", "offer-row");
            get.Add(Text("For", "row-title", "offer-label"));
            buyAmount = Field(get, 1000);
            Picks(get, buyPicks, r => { buy = r; ShowPicks(); });
            Root.Add(get);
            var times = Element("send-to-row", "offer-row");
            times.Add(Text("Times", "row-title", "offer-label"));
            lots = Field(times, 1);
            times.Add(ButtonWith("Post offer", Post, "btn", "btn--small"));
            Root.Add(times);
            ShowPicks();
            offerMessage = Text($"What you give is set aside until the offer is taken, withdrawn, or runs out after {World.OfferHours:0} game hours.", "row-info");
            Root.Add(offerMessage);
            ownList = Element("queue-list");
            Root.Add(ownList);
        }

        static IntegerField Field(VisualElement row, int value)
        {
            var f = new IntegerField { value = value };
            f.AddToClassList("coord-field");
            row.Add(f);
            return f;
        }

        static IntegerField Amount(VisualElement row, UnityEngine.Texture2D icon)
        {
            row.Add(Icons.Element(icon, 18, "cost-icon"));
            var f = new IntegerField { value = 0 };
            f.AddToClassList("amount-field");
            row.Add(f);
            return f;
        }

        /// <summary>A button for each resource (its icon and name), one of them chosen.</summary>
        static void Picks(VisualElement row, Button[] picks, Action<ResourceType> choose)
        {
            for (int i = 0; i < 3; i++)
            {
                var r = (ResourceType)i;
                var b = new Button(() => choose(r));
                b.AddToClassList("resource-pick");
                b.Add(Icons.Element(Icons.Resource(r), 16, "cost-icon"));
                b.Add(Text(r.ToString(), "resource-pick-label"));
                picks[i] = b;
                row.Add(b);
            }
        }

        void ShowPicks()
        {
            for (int i = 0; i < 3; i++)
            {
                sellPicks[i].EnableInClassList("resource-pick--selected", (int)sell == i);
                buyPicks[i].EnableInClassList("resource-pick--selected", (int)buy == i);
            }
        }

        /// <summary>Fills in where to send resources (from a village's window).</summary>
        public void SetTarget(int x, int y)
        {
            targetX.value = x;
            targetY.value = y;
            SetText(sendMessage, "Choose how much to send.");
        }

        void Send()
        {
            var goods = new Cost(Math.Max(0, sendWood.value), Math.Max(0, sendClay.value), Math.Max(0, sendIron.value));
            string problem = game.SendResources(targetX.value, targetY.value, goods);
            SetText(sendMessage, problem ?? "Merchants are on their way.");
            if (problem == null)
            {
                sendWood.value = sendClay.value = sendIron.value = 0;
            }
        }

        void Post()
        {
            string problem = game.PostOffer(sell, sellAmount.value, buy, buyAmount.value, lots.value);
            SetText(offerMessage, problem ?? "Offer posted.");
        }

        public void Refresh(World world, Village v)
        {
            var def = Buildings.Get(BuildingType.Market);
            int level = v.Level(BuildingType.Market), next = v.NextLevel(BuildingType.Market);
            SetText(effect, BuildingText.Effect(world, BuildingType.Market, level, next <= def.MaxLevel ? next : (int?)null));
            SetText(merchants, level > 0 ? $"Merchants at home: {world.MerchantsFree(v):N0} of {World.MerchantsTotal(v):N0}." : "Build a market to hire merchants.");
            upgrade.Refresh(world, v, BuildingType.Market);
            RefreshOthers(world, v);
            RefreshOwn(world, v);
        }

        void RefreshOthers(World world, Village v)
        {
            // The nearest open offers from other players, nearest first.
            var offers = world.Offers.FindAll(o => o.OwnerId != v.OwnerId && world.FindVillage(o.VillageId) != null);
            offers.Sort((a, b) => World.Distance(v, world.FindVillage(a.VillageId)).CompareTo(World.Distance(v, world.FindVillage(b.VillageId))));
            if (offers.Count > 12) offers.RemoveRange(12, offers.Count - 12);

            string signature = v.Id + ":" + string.Join(",", offers.ConvertAll(o => $"{o.Id}x{o.Lots}"));
            if (signature == othersSignature)
            {
                // Just whether each can be taken right now.
                for (int i = 0; i < offers.Count && i < othersList.childCount; i++)
                {
                    var accept = othersList[i].Q<Button>(className: "accept-btn");
                    var status = world.CheckAccept(v, offers[i], 1);
                    accept?.SetEnabled(status == TradeStatus.Ok);
                    var reason = othersList[i].Q<Label>(className: "row-reason");
                    if (reason != null) SetText(reason, BuildingText.Trade(status));
                }
                return;
            }
            othersSignature = signature;
            othersList.Clear();
            if (offers.Count == 0) othersList.Add(Text("No offers right now. Lords with a market put up offers when their stores are lopsided.", "row-info"));
            foreach (var o in offers)
            {
                var seller = world.FindVillage(o.VillageId);
                var owner = world.FindPlayer(o.OwnerId);
                int id = o.Id;
                var row = Element("build-row", "hq-row");
                var left = Element("hq-row-name");
                var line = Element("link-line");
                if (owner != null) line.Add(Link(owner.Name, () => links.OpenPlayer(owner.Id), "link--owner"));
                line.Add(Link($"{seller.Name} ({seller.X}|{seller.Y})", () => links.OpenVillage(seller.Id)));
                line.Add(Text($"{World.Distance(v, seller):0.0} fields · {Real(world, World.MerchantSeconds(seller, v))} away", "row-level"));
                left.Add(line);
                left.Add(Trade(o));
                row.Add(left);
                var box = Element("upgrade-box");
                box.Add(ButtonWith($"Accept 1 of {o.Lots}", () => game.AcceptOffer(id, 1), "btn", "btn--small", "build-btn", "accept-btn"));
                box.Add(Text("", "row-reason"));
                row.Add(box);
                othersList.Add(row);
            }
        }

        void RefreshOwn(World world, Village v)
        {
            var offers = world.Offers.FindAll(o => o.VillageId == v.Id);
            string signature = v.Id + ":" + string.Join(",", offers.ConvertAll(o => $"{o.Id}x{o.Lots}"));
            if (signature == ownSignature) return;
            ownSignature = signature;
            ownList.Clear();
            if (offers.Count == 0) ownList.Add(Text("None from this village.", "row-info"));
            foreach (var o in offers)
            {
                int id = o.Id;
                var row = Element("build-row", "hq-row");
                var left = Element("hq-row-name");
                left.Add(Trade(o));
                left.Add(Text($"{o.Lots} left · runs out {World.FormatClock(o.Expires)}", "row-level"));
                row.Add(left);
                row.Add(ButtonWith("Withdraw", () => game.WithdrawOffer(id), "btn", "btn--small", "cancel-btn"));
                ownList.Add(row);
            }
        }

        /// <summary>"Gives [wood] 1,000 for [clay] 1,000", with resource icons.</summary>
        static VisualElement Trade(MarketOffer o)
        {
            var line = Element("cost-line");
            line.Add(Text("Gives", "row-info"));
            line.Add(Icons.Element(Icons.Resource(o.Sell), 16, "cost-icon"));
            line.Add(Text($"{o.SellAmount:N0}", "cost-value"));
            line.Add(Text("  for", "row-info"));
            line.Add(Icons.Element(Icons.Resource(o.Buy), 16, "cost-icon"));
            line.Add(Text($"{o.BuyAmount:N0}", "cost-value"));
            return line;
        }
    }
}
