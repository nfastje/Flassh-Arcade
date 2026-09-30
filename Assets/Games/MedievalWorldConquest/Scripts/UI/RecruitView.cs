using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
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
}
