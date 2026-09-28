using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The Army tab: the garrison (troops at home, with their combined strength) and, for each training building,
    /// its queue and every unit it trains, with a quantity picker and the reason if it can't be recruited.
    /// </summary>
    public class ArmyPanel
    {
        static readonly BuildingType[] TrainingBuildings = { BuildingType.Barracks, BuildingType.Stable, BuildingType.Workshop };

        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly int[] counts = new int[Units.Count];

        class UnitRow
        {
            public VisualElement Element;
            public Label Count, Cost, Reason;
            public Button Recruit;
        }

        class Section
        {
            public BuildingType Building;
            public Label Title, Locked;
            public VisualElement Units, Queue;
        }

        readonly UnitRow[] unitRows = new UnitRow[Units.Count];
        readonly List<Section> sections = new List<Section>();
        readonly Label[] garrisonCounts = new Label[Units.Count];
        Label totalAttack, totalDefense, totalCarry, troopPopulation;

        public ArmyPanel(MedievalWorldConquestGame game)
        {
            this.game = game;
            Root = Element("army");
            Root.Add(BuildGarrison());
            Root.Add(BuildRecruitment());
        }

        // ---------------------------------------------------------------- garrison

        VisualElement BuildGarrison()
        {
            var column = Element("army-garrison");
            column.Add(Text("Garrison", "heading"));
            column.Add(Text("Troops at home in this village.", "row-info"));

            foreach (var u in Units.Definitions)
            {
                var line = Element("row-header", "garrison-line");
                line.Add(Text(u.Name, "row-title"));
                garrisonCounts[(int)u.Type] = Text("0", "garrison-count");
                line.Add(garrisonCounts[(int)u.Type]);
                column.Add(line);
            }

            column.Add(Element("divider"));
            totalAttack = Text("", "row-info");
            totalDefense = Text("", "row-info");
            totalCarry = Text("", "row-info");
            troopPopulation = Text("", "row-info");
            column.Add(totalAttack);
            column.Add(totalDefense);
            column.Add(totalCarry);
            column.Add(troopPopulation);

            column.Add(Text("Troop movements", "heading"));
            movements = new ScrollView(ScrollViewMode.Vertical);
            movements.AddToClassList("movements");
            column.Add(movements);
            return column;
        }

        ScrollView movements;
        string movementsSignature;

        /// <summary>
        /// Attacks and support on the march, troops heading home, attacks incoming, and support stationed elsewhere
        /// (which can be recalled). Rebuilt when commands change; countdowns update in place.
        /// </summary>
        void RefreshMovements(World world)
        {
            var human = world.HumanPlayer;
            if (human == null) return;
            var commands = world.MovementsFor(human.Id);
            var stationed = world.Villages.FindAll(v => v.Supports.Exists(g => g.OwnerId == human.Id));

            string signature = string.Join(",", commands.ConvertAll(c => c.Id.ToString())) + "|" +
                               string.Join(",", stationed.ConvertAll(v => v.Id.ToString()));
            if (signature != movementsSignature)
            {
                movementsSignature = signature;
                movements.Clear();
                if (commands.Count == 0 && stationed.Count == 0)
                    movements.Add(Text("No troops on the move. Send an attack from the Map tab.", "row-info"));
                foreach (var c in commands)
                {
                    var item = Element("queue-item", "recruit-item", "movement");
                    item.userData = c.Id;
                    item.Add(Text("", "row-title", "movement-title"));
                    item.Add(Text("", "row-info", "movement-time"));
                    movements.Add(item);
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

            foreach (var child in movements.Children())
            {
                if (!(child.userData is int id)) continue;
                var c = world.Commands.Find(x => x.Id == id);
                if (c == null) continue;
                var target = world.FindVillage(c.ToVillageId);
                var origin = world.FindVillage(c.FromVillageId);
                bool incoming = c.OwnerId != human.Id;
                string what = c.Kind switch
                {
                    CommandKind.Attack when incoming => $"INCOMING attack by {(origin != null ? world.OwnerName(origin) : "?")} from {origin?.Name} ({origin?.X}|{origin?.Y})",
                    CommandKind.Attack => $"Attack on {target?.Name} ({target?.X}|{target?.Y})",
                    CommandKind.Support => $"Support to {target?.Name} ({target?.X}|{target?.Y})",
                    _ => $"Returning from {origin?.Name} ({origin?.X}|{origin?.Y})",
                };
                int loot = c.Loot.Wood + c.Loot.Clay + c.Loot.Iron;
                string detail = $"{(incoming ? "Hits" : "Arrives")} in {Real(world, Math.Max(0, c.ArriveTime - world.Now))}" +
                                (incoming ? "" : $"  ·  {TroopSummary(c.Troops)}") +
                                (loot > 0 ? $"  ·  carrying {loot:N0} loot" : "");
                SetText(child.Q<Label>(className: "movement-title"), what);
                SetText(child.Q<Label>(className: "movement-time"), detail);
                child.EnableInClassList("movement--incoming", incoming);
            }
        }

        static string TroopSummary(int[] troops)
        {
            var parts = new List<string>();
            for (int i = 0; i < troops.Length && i < Units.Count; i++)
                if (troops[i] > 0) parts.Add($"{troops[i]:N0} {Units.Get((UnitType)i).Name}");
            return parts.Count > 0 ? string.Join(", ", parts) : "no troops";
        }

        // ---------------------------------------------------------------- recruitment

        VisualElement BuildRecruitment()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("army-recruit");

            foreach (var building in TrainingBuildings)
            {
                var section = new Section { Building = building };
                var box = Element("army-section");
                section.Title = Text("", "heading");
                box.Add(section.Title);
                section.Locked = Text("", "row-reason");
                box.Add(section.Locked);
                section.Queue = Element();
                box.Add(section.Queue);
                section.Units = Element();
                foreach (var u in Units.TrainedAt(building)) section.Units.Add(BuildUnitRow(u));
                box.Add(section.Units);
                sections.Add(section);
                scroll.Add(box);
            }
            return scroll;
        }

        VisualElement BuildUnitRow(UnitDef u)
        {
            var type = u.Type;
            var row = new UnitRow { Element = Element("build-row", "unit-row") };

            var header = Element("row-header");
            header.Add(Text(u.Name, "row-title"));
            header.Add(Text(ClassName(u.Class), "row-level"));
            row.Element.Add(header);
            row.Element.Add(Text(u.Description, "row-info"));
            row.Element.Add(Text(
                $"Attack {u.Attack} · Defence {u.DefenseInfantry} / {u.DefenseCavalry} / {u.DefenseArcher} (inf / cav / arch) · Carries {u.Carry}",
                "row-info", "unit-stats"));
            row.Cost = Text("", "row-info");
            row.Element.Add(row.Cost);

            var controls = Element("unit-controls");
            controls.Add(ButtonWith("-10", () => Adjust(type, -10), "btn", "btn--small", "count-btn"));
            controls.Add(ButtonWith("-1", () => Adjust(type, -1), "btn", "btn--small", "count-btn"));
            row.Count = Text("0", "unit-count");
            controls.Add(row.Count);
            controls.Add(ButtonWith("+1", () => Adjust(type, 1), "btn", "btn--small", "count-btn"));
            controls.Add(ButtonWith("+10", () => Adjust(type, 10), "btn", "btn--small", "count-btn"));
            controls.Add(ButtonWith("Max", () => counts[(int)type] = int.MaxValue, "btn", "btn--small", "count-btn"));
            row.Recruit = ButtonWith("", () =>
            {
                if (game.Recruit(type, counts[(int)type])) counts[(int)type] = 0;
            }, "btn", "btn--small", "recruit-btn");
            controls.Add(row.Recruit);
            row.Element.Add(controls);

            row.Reason = Text("", "row-reason");
            row.Element.Add(row.Reason);
            unitRows[(int)type] = row;
            return row.Element;
        }

        void Adjust(UnitType type, int delta)
        {
            long value = (long)counts[(int)type] + delta;
            counts[(int)type] = (int)Math.Max(0, Math.Min(int.MaxValue, value));
        }

        static string ClassName(UnitClass c) => c switch
        {
            UnitClass.Infantry => "Infantry",
            UnitClass.Cavalry => "Cavalry",
            UnitClass.Archer => "Archer",
            _ => "Siege",
        };

        // ---------------------------------------------------------------- refresh

        public void Refresh(World world, Village v)
        {
            RefreshGarrison(world, v);
            RefreshMovements(world);
            foreach (var section in sections) RefreshSection(world, v, section);
        }

        void RefreshGarrison(World world, Village v)
        {
            long attack = 0, defInf = 0, defCav = 0, defArch = 0, carry = 0;
            foreach (var u in Units.Definitions)
            {
                int n = v.TroopCount(u.Type);
                SetText(garrisonCounts[(int)u.Type], n.ToString("N0"));
                garrisonCounts[(int)u.Type].EnableInClassList("garrison-count--none", n == 0);
                attack += (long)n * u.Attack;
                defInf += (long)n * u.DefenseInfantry;
                defCav += (long)n * u.DefenseCavalry;
                defArch += (long)n * u.DefenseArcher;
                carry += (long)n * u.Carry;
            }
            SetText(totalAttack, $"Attack strength: {attack:N0}");
            SetText(totalDefense, $"Defence: {defInf:N0} vs infantry · {defCav:N0} vs cavalry · {defArch:N0} vs archers");
            SetText(totalCarry, $"Loot capacity: {carry:N0}");
            SetText(troopPopulation, $"Troop population: {v.TroopPopulation:N0} (free: {v.FreePopulation:N0})");
        }

        void RefreshSection(World world, Village v, Section s)
        {
            var def = Buildings.Get(s.Building);
            int level = v.Level(s.Building);
            SetText(s.Title, level > 0 ? $"{def.Name}  ·  level {level}" : def.Name);

            if (level == 0)
            {
                var needs = Array.ConvertAll(def.Requires, r => $"{Buildings.Get(r.Building).Name} {r.Level}");
                SetText(s.Locked, $"Not built yet. Build it in the Village tab (needs {string.Join(" and ", needs)}).");
                Show(s.Locked, true);
            }
            else Show(s.Locked, false);

            RefreshQueue(world, v, s);
            foreach (var u in Units.TrainedAt(s.Building)) RefreshUnitRow(world, v, u);
        }

        void RefreshQueue(World world, Village v, Section s)
        {
            var orders = v.Recruitment.FindAll(o => o.Building == s.Building);

            // Rebuild when the set of orders changes; update the numbers in place otherwise.
            string signature = string.Join(",", orders.ConvertAll(o => o.Id.ToString()));
            if ((string)s.Queue.userData != signature)
            {
                s.Queue.userData = signature;
                s.Queue.Clear();
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
                    s.Queue.Add(item);
                }
            }

            for (int i = 0; i < orders.Count && i < s.Queue.childCount; i++)
            {
                var o = orders[i];
                var line = s.Queue[i][0];
                SetText((Label)line[0], $"{Units.Get(o.Unit).Name}  {o.Done}/{o.Total}");
                SetText((Label)line[1], o.Started
                    ? $"next {Real(world, Math.Max(0, o.NextAt - world.Now))} · all {Real(world, Math.Max(0, o.FinishTime(world.Now) - world.Now))}"
                    : $"waiting · {Real(world, o.Remaining * o.SecondsEach)}");
                var fill = s.Queue[i].Q<VisualElement>(className: "progress-fill");
                fill.style.width = Length.Percent(100f * o.Done / o.Total);
            }
        }

        void RefreshUnitRow(World world, Village v, UnitDef u)
        {
            var row = unitRows[(int)u.Type];
            int max = World.MaxAffordable(v, u.Type);
            ref int count = ref counts[(int)u.Type];
            if (count == int.MaxValue) count = max; // "Max" pressed: fill in what's affordable now
            SetText(row.Count, count.ToString("N0"));

            var check = world.CheckRecruit(v, u.Type, Math.Max(1, count));
            var c = u.Cost;
            SetText(row.Cost, $"Each: Wood {c.Wood} · Clay {c.Clay} · Iron {c.Iron} · Pop {c.Population} · {Real(world, check.SecondsEach)}  ·  Speed {u.MinutesPerField / world.Settings.Speed:0.#} min per field");

            row.Recruit.text = count > 0 ? $"Recruit {count:N0} ({Real(world, check.SecondsEach * count)})" : "Recruit";
            bool ok = count > 0 && check.Status == RecruitStatus.Ok;
            row.Recruit.SetEnabled(ok);
            row.Element.EnableInClassList("unit-row--locked", check.Status == RecruitStatus.NeedsBuilding);
            int room = v.FreePopulation / Math.Max(1, u.Cost.Population);
            SetText(row.Reason, count == 0 && check.Status == RecruitStatus.Ok ? $"You can afford up to {max:N0}." : ReasonText(world, check, max, room));
        }

        static string ReasonText(World world, RecruitCheck check, int max, int room) => check.Status switch
        {
            RecruitStatus.Ok => "",
            RecruitStatus.NeedsBuilding => $"Needs {Buildings.Get(check.Required.Building).Name} level {check.Required.Level}.",
            RecruitStatus.QueueFull => $"This building's queue is full ({World.MaxRecruitQueue} batches).",
            RecruitStatus.FarmTooSmall => $"Not enough population: room for {room:N0} more. Upgrade the Farm.",
            RecruitStatus.NotEnoughResources => double.IsInfinity(check.AffordableIn)
                ? "Costs more than your warehouse holds: recruit fewer at a time."
                : $"Not enough resources: affordable in {Real(world, check.AffordableIn)} (up to {max:N0} now).",
            _ => "",
        };
    }
}
