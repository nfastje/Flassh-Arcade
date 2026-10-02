using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The villages overview, like Tribal Wars' overviews: every village the player holds on one screen, in one of
    /// two modes with fixed columns that line up from village to village. Production: stores, storage, farm and
    /// what's being built. Troops: a column for each kind of unit, with how many are at home. Both show points and
    /// any attacks on their way. A village's name switches to it; the pencil beside it renames it.
    /// </summary>
    public class OverviewPanel
    {
        public VisualElement Root { get; }

        readonly Action<int> switchTo;
        readonly Action<int, string> rename;
        readonly ScrollView list;
        /// <summary>The villages' lines, inside the list.</summary>
        readonly VisualElement rowsBox;
        readonly Label summaryVillages;
        readonly VisualElement summaryStores, summaryTroops;
        readonly Label[] summaryStock = new Label[3];
        readonly VisualElement[] summaryUnitCells = new VisualElement[Units.Count];
        readonly Label[] summaryUnits = new Label[Units.Count];
        readonly Button showProduction, showTroops;
        readonly VisualElement productionHeader, troopsHeader;
        readonly List<Row> rows = new List<Row>();
        string signature;
        float nextRefresh;
        bool troopsShown;

        /// <summary>One village's line: its cells, updated in place.</summary>
        class Row
        {
            public VisualElement Root, Production, Troops, NameLine;
            public Button Name, Pencil;
            /// <summary>The box the new name is typed in, while renaming (null otherwise).</summary>
            public VisualElement Renaming;
            public Label Where, Points, Wood, Clay, Iron, Storage, Farm, Building, BuildingTime, Incoming;
            /// <summary>The units in training, as icons with counts (rebuilt when that changes).</summary>
            public VisualElement Recruiting;
            public string RecruitingShown;
            public readonly Label[] Units = new Label[Simulation.Units.Count];
            public int VillageId;
        }

        /// <param name="switchTo">Makes a village the current one and shows it.</param>
        /// <param name="rename">Renames a village.</param>
        public OverviewPanel(Action<int> switchTo, Action<int, string> rename)
        {
            this.switchTo = switchTo;
            this.rename = rename;
            Root = Element("army", "overview");
            var column = Element("overview-column");
            column.Add(Text("Villages", "heading"));

            var modes = Element("option-row", "ranking-pager");
            showProduction = ButtonWith("Production", () => ShowMode(false), "option");
            showTroops = ButtonWith("Troops", () => ShowMode(true), "option");
            modes.Add(showProduction);
            modes.Add(showTroops);
            column.Add(modes);

            // The totals across every village: stores, or troops at home, as icons with their counts.
            var summary = Element("overview-summary");
            summaryVillages = Text("", "row-info", "overview-summary-text");
            summary.Add(summaryVillages);
            summaryStores = Element("overview-summary-group");
            summaryStores.Add(Text("in store altogether:", "row-info", "overview-summary-text"));
            for (int i = 0; i < 3; i++)
            {
                var chip = Element("overview-summary-chip");
                chip.Add(Icons.Element(Icons.Resource((ResourceType)i), 16));
                summaryStock[i] = Text("", "row-info", "overview-summary-count");
                chip.Add(summaryStock[i]);
                summaryStores.Add(chip);
            }
            summary.Add(summaryStores);
            summaryTroops = Element("overview-summary-group");
            summaryTroops.Add(Text("at home altogether:", "row-info", "overview-summary-text"));
            foreach (var type in Units.InDisplayOrder)
            {
                var chip = Element("overview-summary-chip");
                chip.tooltip = Units.Get(type).Name;
                chip.Add(Icons.Element(Icons.Unit(type), 16));
                summaryUnits[(int)type] = Text("", "row-info", "overview-summary-count");
                chip.Add(summaryUnits[(int)type]);
                summaryUnitCells[(int)type] = chip;
                summaryTroops.Add(chip);
            }
            summary.Add(summaryTroops);
            column.Add(summary);

            // The column headings take the same cell style as the rows below, so every column lines up.
            var header = Element("overview-row", "ranking-header");
            header.Add(Text("Village", "overview-cell", "overview-name"));
            header.Add(Text("Points", "overview-cell", "overview-number"));
            productionHeader = Element("overview-group");
            foreach (var icon in new[] { Icons.Wood, Icons.Clay, Icons.Iron, Icons.Storage, Icons.Population })
            {
                var cell = Element("overview-number", "overview-icon-cell");
                if (icon == Icons.Population) cell.AddToClassList("overview-farm");
                cell.Add(Icons.Element(icon, 18));
                productionHeader.Add(cell);
            }
            // (Built like the cells under them, a box with the text inside, so title and contents start at the same place.)
            var constructionTitle = Element("overview-building");
            constructionTitle.Add(Text("Construction", "overview-cell"));
            productionHeader.Add(constructionTitle);
            var recruitingTitle = Element("overview-recruiting", "overview-recruiting-units");
            recruitingTitle.Add(Text("Recruiting", "overview-cell"));
            productionHeader.Add(recruitingTitle);
            header.Add(productionHeader);
            troopsHeader = Element("overview-group");
            foreach (var type in Units.InDisplayOrder)
            {
                var cell = Element("overview-unit-col", "overview-icon-cell");
                cell.tooltip = Units.Get(type).Name;
                cell.Add(Icons.Element(Icons.Unit(type), 20));
                troopsHeader.Add(cell);
            }
            header.Add(troopsHeader);
            header.Add(Text("Incoming", "overview-cell", "overview-incoming"));

            // The heading stays put above the scrolling list of villages.
            column.Add(header);
            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ranking-list");
            rowsBox = Element();
            list.Add(rowsBox);
            column.Add(list);
            Root.Add(column);
            ShowMode(false);
        }

        void ShowMode(bool troops)
        {
            troopsShown = troops;
            showProduction.EnableInClassList("option--selected", !troops);
            showTroops.EnableInClassList("option--selected", troops);
            Show(productionHeader, !troops);
            Show(troopsHeader, troops);
            foreach (var row in rows)
            {
                Show(row.Production, !troops);
                Show(row.Troops, troops);
            }
            nextRefresh = 0;
        }

        public void Refresh(World world)
        {
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + 0.5f;
            var own = world.HumanVillagesByName();

            // New rows when villages are won or lost, or a new name changes the order; otherwise the cells are just updated.
            string now = string.Join(",", own.ConvertAll(v => v.Id.ToString()));
            if (now != signature)
            {
                signature = now;
                rowsBox.Clear();
                rows.Clear();
                foreach (var v in own) rows.Add(MakeRow(v.Id));
            }

            var human = world.HumanPlayer;
            var incoming = human != null ? world.IncomingAttacks(human.Id) : new List<Command>();
            long wood = 0, clay = 0, iron = 0;
            var troops = new long[Units.Count];
            for (int i = 0; i < rows.Count && i < own.Count; i++)
            {
                var v = own[i];
                var row = rows[i];
                SetText(row.Name, v.Name);
                row.Name.tooltip = v.Name;
                SetText(row.Where, $"({v.X}|{v.Y})");
                row.Where.tooltip = $"Continent {World.ContinentName(v.X, v.Y)}";
                SetText(row.Points, $"{v.Points:N0}");
                if (troopsShown) ShowTroops(row, v);
                else ShowProduction(world, row, v);
                int attacks = incoming.FindAll(c => c.ToVillageId == v.Id).Count;
                SetText(row.Incoming, attacks > 0 ? $"{attacks}" : "–");
                row.Incoming.EnableInClassList("overview-attacked", attacks > 0);
                row.Root.EnableInClassList("ranking-row--you", v == world.PlayerVillage);
                wood += (long)v.Wood;
                clay += (long)v.Clay;
                iron += (long)v.Iron;
                for (int u = 0; u < Units.Count; u++) troops[u] += v.TroopCount((UnitType)u);
            }

            SetText(summaryVillages, own.Count == 0 ? "You hold no villages." : $"{own.Count:N0} village{(own.Count == 1 ? "" : "s")}  ·");
            Show(summaryStores, own.Count > 0 && !troopsShown);
            Show(summaryTroops, own.Count > 0 && troopsShown);
            SetText(summaryStock[0], $"{wood:N0}");
            SetText(summaryStock[1], $"{clay:N0}");
            SetText(summaryStock[2], $"{iron:N0}");
            for (int u = 0; u < Units.Count; u++)
            {
                Show(summaryUnitCells[u], troops[u] > 0);
                SetText(summaryUnits[u], $"{troops[u]:N0}");
            }
        }

        void ShowProduction(World world, Row row, Village v)
        {
            int cap = v.StorageCapacity;
            Stock(row.Wood, v.Wood, cap);
            Stock(row.Clay, v.Clay, cap);
            Stock(row.Iron, v.Iron, cap);
            SetText(row.Storage, $"{cap:N0}");
            SetText(row.Farm, $"{v.PopulationUsed:N0}/{v.PopulationCapacity:N0}");
            row.Farm.EnableInClassList("overview-full", v.PopulationUsed >= v.PopulationCapacity);
            ShowRecruiting(row, v);
            // What's being built on one line; how long it has left (and how many more are queued) under it.
            if (v.Queue.Count == 0)
            {
                SetText(row.Building, "–");
                SetText(row.BuildingTime, "");
                return;
            }
            var first = v.Queue[0];
            SetText(row.Building, $"{Buildings.Get(first.Type).Name} {first.Level}");
            SetText(row.BuildingTime, (first.Started ? Real(world, Math.Max(0, first.FinishTime - world.Now)) : "waiting")
                                      + (v.Queue.Count > 1 ? $"  (+{v.Queue.Count - 1} queued)" : ""));
        }

        /// <summary>
        /// The units in training in a village, by kind, as an icon and how many each (in Tribal Wars' order), so a
        /// busy village never runs over into the next column; the full list is in the tooltip.
        /// </summary>
        /// <summary>The most kinds of unit the Recruiting column shows (the rest as "+N", all of them in the tooltip).</summary>
        const int MaxRecruitingShown = 3;

        static void ShowRecruiting(Row row, Village v)
        {
            var training = new int[Units.Count];
            foreach (var o in v.Recruitment) training[(int)o.Unit] += o.Remaining;
            string key = string.Join(",", training);
            if (key == row.RecruitingShown) return;
            row.RecruitingShown = key;
            row.Recruiting.Clear();
            var names = new List<string>();
            foreach (var type in Units.InDisplayOrder)
            {
                int n = training[(int)type];
                if (n <= 0) continue;
                names.Add($"{n:N0} {Units.Get(type).Name}");
                // One line only (the row has room for no more): the first few kinds, then "+N" for the rest.
                if (names.Count > MaxRecruitingShown) continue;
                var cell = Element("overview-recruit-unit");
                cell.Add(Icons.Element(Icons.Unit(type), 16));
                cell.Add(Text($"{n:N0}", "overview-cell", "overview-recruit-count"));
                row.Recruiting.Add(cell);
            }
            if (names.Count > MaxRecruitingShown) row.Recruiting.Add(Text($"+{names.Count - MaxRecruitingShown}", "overview-cell", "overview-recruit-more"));
            if (names.Count == 0) row.Recruiting.Add(Text("–", "overview-cell"));
            row.Recruiting.tooltip = names.Count == 0 ? "" : "In training: " + string.Join(", ", names);
        }

        /// <summary>Swaps the name for a box to type the new one in, as in the Manager: Enter or OK renames, ✕ leaves it be.</summary>
        void StartRename(Row row)
        {
            if (row.Renaming != null) return;
            var field = new TextField { maxLength = World.MaxVillageNameLength, value = row.Name.text };
            field.AddToClassList("rename-field");
            field.AddToClassList("manager-rename-field");
            var box = Element("overview-rename");
            box.Add(field);
            void Done(bool apply)
            {
                if (row.Renaming == null) return;
                if (apply) rename(row.VillageId, field.value);
                row.NameLine.Remove(row.Renaming);
                row.Renaming = null;
                Show(row.Name, true);
                Show(row.Pencil, true);
                Show(row.Where, true);
                nextRefresh = 0; // the new name (and its place in the order) at once
            }
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == UnityEngine.KeyCode.Return || e.keyCode == UnityEngine.KeyCode.KeypadEnter) Done(true);
                else if (e.keyCode == UnityEngine.KeyCode.Escape) Done(false);
            }, TrickleDown.TrickleDown);
            box.Add(ButtonWith("OK", () => Done(true), "btn", "btn--small", "manager-rename-btn"));
            box.Add(ButtonWith("✕", () => Done(false), "btn", "btn--small", "manager-rename-btn"));
            Show(row.Name, false);
            Show(row.Pencil, false);
            Show(row.Where, false);
            row.Renaming = box;
            row.NameLine.Add(box);
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectAll();
            });
        }

        static void ShowTroops(Row row, Village v)
        {
            for (int u = 0; u < Units.Count; u++)
            {
                var cell = row.Units[u];
                if (cell == null) continue;
                int n = v.TroopCount((UnitType)u);
                SetText(cell, $"{n:N0}");
                cell.EnableInClassList("overview-zero", n == 0);
            }
        }

        Row MakeRow(int villageId)
        {
            // (Every village's line is the same height in both modes, so switching doesn't move the list.)
            var row = new Row { VillageId = villageId, Root = Element("overview-row", "overview-line") };
            // The name and its pencil, then the coordinates (the continent on hover, leaving the name more room).
            var name = Element("overview-name");
            row.NameLine = Element("overview-name-line");
            row.Name = Link("", () => switchTo(villageId), "manager-village-link");
            row.Pencil = new Button(() => StartRename(row)) { tooltip = "Rename" };
            row.Pencil.AddToClassList("pencil-btn");
            row.Pencil.Add(Icons.Element(Icons.Pencil, 16));
            row.NameLine.Add(row.Name);
            row.NameLine.Add(row.Pencil);
            row.Where = Text("", "row-level", "manager-coords");
            name.Add(row.NameLine);
            name.Add(row.Where);
            row.Root.Add(name);
            row.Points = Cell(row.Root, "overview-number");

            row.Production = Element("overview-group");
            row.Wood = Cell(row.Production, "overview-number");
            row.Clay = Cell(row.Production, "overview-number");
            row.Iron = Cell(row.Production, "overview-number");
            row.Storage = Cell(row.Production, "overview-number");
            row.Farm = Cell(row.Production, "overview-number");
            row.Farm.AddToClassList("overview-farm");
            var building = Element("overview-building");
            row.Building = Text("", "overview-cell");
            row.BuildingTime = Text("", "overview-cell", "overview-subline");
            building.Add(row.Building);
            building.Add(row.BuildingTime);
            row.Production.Add(building);
            row.Recruiting = Element("overview-recruiting", "overview-recruiting-units");
            row.Production.Add(row.Recruiting);
            row.Root.Add(row.Production);

            row.Troops = Element("overview-group");
            foreach (var type in Units.InDisplayOrder)
                row.Units[(int)type] = Cell(row.Troops, "overview-unit-col");
            row.Root.Add(row.Troops);

            row.Incoming = Cell(row.Root, "overview-incoming");
            Show(row.Production, !troopsShown);
            Show(row.Troops, troopsShown);
            rowsBox.Add(row.Root);
            return row;
        }

        static Label Cell(VisualElement row, string cls)
        {
            var label = Text("", "overview-cell", cls);
            row.Add(label);
            return label;
        }

        static void Stock(Label label, double amount, int cap)
        {
            SetText(label, $"{Math.Floor(amount):N0}");
            label.EnableInClassList("overview-full", amount >= cap);
        }
    }
}
