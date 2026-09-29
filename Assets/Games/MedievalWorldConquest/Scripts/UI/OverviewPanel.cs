using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The villages overview, like Tribal Wars' combined overview: every village the player holds on one screen,
    /// with its stores, farm, what's being built, the troops at home and any attacks on their way. A village's
    /// name switches to it.
    /// </summary>
    public class OverviewPanel
    {
        public VisualElement Root { get; }

        readonly Action<int> switchTo;
        readonly ScrollView list;
        readonly Label summary;
        readonly List<Row> rows = new List<Row>();
        string signature;
        float nextRefresh;

        /// <summary>One village's line: its cells, updated in place.</summary>
        class Row
        {
            public VisualElement Root;
            public Button Name;
            public Label Where, Points, Wood, Clay, Iron, Storage, Farm, Building, Troops, Incoming;
            public int VillageId;
        }

        /// <param name="switchTo">Makes a village the current one and shows it.</param>
        public OverviewPanel(Action<int> switchTo)
        {
            this.switchTo = switchTo;
            Root = Element("army", "overview");
            var column = Element("overview-column");
            column.Add(Text("Villages", "heading"));
            summary = Text("", "row-info");
            column.Add(summary);

            var header = Element("overview-row", "ranking-header");
            header.Add(Text("Village", "overview-name"));
            header.Add(Text("Points", "overview-number"));
            foreach (var icon in new[] { Icons.Wood, Icons.Clay, Icons.Iron, Icons.Storage, Icons.Population })
            {
                var cell = Element("overview-number", "overview-icon-cell");
                cell.Add(Icons.Element(icon, 18));
                header.Add(cell);
            }
            header.Add(Text("Construction", "overview-building"));
            header.Add(Text("Troops at home", "overview-troops"));
            header.Add(Text("Incoming", "overview-incoming"));
            column.Add(header);

            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ranking-list");
            column.Add(list);
            Root.Add(column);
        }

        public void Refresh(World world)
        {
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + 0.5f;
            var own = world.HumanVillages();

            // New rows when villages are won or lost; otherwise the cells are just updated.
            string now = string.Join(",", own.ConvertAll(v => v.Id.ToString()));
            if (now != signature)
            {
                signature = now;
                list.Clear();
                rows.Clear();
                foreach (var v in own) rows.Add(MakeRow(v.Id));
            }

            var human = world.HumanPlayer;
            var incoming = human != null ? world.IncomingAttacks(human.Id) : new List<Command>();
            long wood = 0, clay = 0, iron = 0;
            for (int i = 0; i < rows.Count && i < own.Count; i++)
            {
                var v = own[i];
                var row = rows[i];
                int cap = v.StorageCapacity;
                SetText(row.Name, v.Name);
                SetText(row.Where, $"({v.X}|{v.Y}) {World.ContinentName(v.X, v.Y)}");
                SetText(row.Points, $"{v.Points:N0}");
                Stock(row.Wood, v.Wood, cap);
                Stock(row.Clay, v.Clay, cap);
                Stock(row.Iron, v.Iron, cap);
                SetText(row.Storage, $"{cap:N0}");
                SetText(row.Farm, $"{v.PopulationUsed:N0}/{v.PopulationCapacity:N0}");
                row.Farm.EnableInClassList("overview-full", v.PopulationUsed >= v.PopulationCapacity);
                SetText(row.Building, v.Queue.Count == 0 ? "–"
                    : $"{Buildings.Get(v.Queue[0].Type).Name} {v.Queue[0].Level}" + (v.Queue[0].Started ? $" · {Real(world, Math.Max(0, v.Queue[0].FinishTime - world.Now))}" : "")
                      + (v.Queue.Count > 1 ? $" (+{v.Queue.Count - 1})" : ""));
                SetText(row.Troops, TroopSummary(v));
                int attacks = incoming.FindAll(c => c.ToVillageId == v.Id).Count;
                SetText(row.Incoming, attacks > 0 ? $"{attacks}" : "–");
                row.Incoming.EnableInClassList("overview-attacked", attacks > 0);
                row.Root.EnableInClassList("ranking-row--you", v == world.PlayerVillage);
                wood += (long)v.Wood;
                clay += (long)v.Clay;
                iron += (long)v.Iron;
            }
            SetText(summary, own.Count == 0 ? "You hold no villages."
                : $"{own.Count:N0} village{(own.Count == 1 ? "" : "s")}  ·  in store altogether: wood {wood:N0} · clay {clay:N0} · iron {iron:N0}. Click a village's name to go there.");
        }

        Row MakeRow(int villageId)
        {
            var row = new Row { VillageId = villageId, Root = Element("overview-row") };
            var name = Element("overview-name");
            row.Name = Link("", () => switchTo(villageId));
            row.Where = Text("", "row-level");
            name.Add(row.Name);
            name.Add(row.Where);
            row.Root.Add(name);
            row.Points = Cell(row.Root, "overview-number");
            row.Wood = Cell(row.Root, "overview-number");
            row.Clay = Cell(row.Root, "overview-number");
            row.Iron = Cell(row.Root, "overview-number");
            row.Storage = Cell(row.Root, "overview-number");
            row.Farm = Cell(row.Root, "overview-number");
            row.Building = Cell(row.Root, "overview-building");
            row.Troops = Cell(row.Root, "overview-troops");
            row.Incoming = Cell(row.Root, "overview-incoming");
            list.Add(row.Root);
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

        /// <summary>"120 Spearman · 40 Axeman …": the troops at home, briefly.</summary>
        static string TroopSummary(Village v)
        {
            var parts = new List<string>();
            foreach (var type in Units.InDisplayOrder)
            {
                int n = v.TroopCount(type);
                if (n > 0) parts.Add($"{n:N0} {Units.Get(type).Name}");
            }
            return parts.Count == 0 ? "–" : string.Join(" · ", parts);
        }
    }
}
