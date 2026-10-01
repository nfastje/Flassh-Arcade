using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// World statistics, as in Tribal Wars: today's, this week's or all-time leaders in plunder, troops defeated
    /// attacking and defending, and villages conquered (with the player's own place), the tribes that conquered
    /// most, and the whole realm's counts for the last few days. Shown in the Ranking tab.
    /// </summary>
    public class StatsPanel
    {
        const int Shown = 10;

        public VisualElement Root { get; }

        /// <summary>A different tab was chosen: the Ranking tab redraws at once rather than at its next refresh.</summary>
        public event System.Action Changed;

        readonly UiLinks links;
        readonly Button[] periodButtons = new Button[4];
        readonly ScrollView body;
        StatPeriod period = StatPeriod.Today;
        /// <summary>The last seven days' tab (instead of a period's leaders).</summary>
        bool history;
        string signature;

        /// <summary>The boards, in three columns side by side: resources, troops, villages.</summary>
        static readonly (StatKind kind, string title, int column)[] Boards =
        {
            (StatKind.Loot, "Resources plundered", 0),
            (StatKind.DefeatedAttacking, "Troops defeated attacking", 1),
            (StatKind.DefeatedDefending, "Troops defeated defending", 1),
            (StatKind.VillagesConquered, "Villages conquered", 2),
        };

        public StatsPanel(UiLinks links)
        {
            this.links = links;
            Root = Element("stats");
            var periods = Element("option-row", "ranking-pager");
            string[] names = { "Today", "This week", "All time", "Last seven days" };
            for (int i = 0; i < 4; i++)
            {
                int tab = i;
                periodButtons[i] = ButtonWith(names[i], () =>
                {
                    history = tab == 3;
                    if (!history) period = (StatPeriod)tab;
                    signature = null;
                    Changed?.Invoke();
                }, "option");
                periods.Add(periodButtons[i]);
            }
            Root.Add(periods);
            body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("ranking-list");
            Root.Add(body);
        }

        public void Refresh(World world)
        {
            for (int i = 0; i < 4; i++) periodButtons[i].EnableInClassList("option--selected", history ? i == 3 : (int)period == i);
            var key = new System.Text.StringBuilder().Append(history ? -1 : (int)period).Append('|').Append(world.StatsDay).Append('|');
            foreach (long v in world.WorldStats) key.Append(v).Append(',');
            string now = key.ToString();
            if (now == signature) return;
            signature = now;

            body.Clear();
            if (history)
            {
                AddHistory(world);
                return;
            }
            string when = period == StatPeriod.Today ? "today" : period == StatPeriod.ThisWeek ? "this week" : "since the world began";
            body.Add(Text($"The realm {when}", "row-title", "stats-heading"));
            body.Add(Text(
                $"{world.WorldStatOf(StatKind.VillagesConquered, period):N0} villages conquered  ·  " +
                $"{world.WorldStatOf(StatKind.Loot, period):N0} resources plundered  ·  " +
                $"{world.WorldStatOf(StatKind.TroopsLost, period):N0} troops fallen", "row-info"));

            var players = new List<Player>();
            foreach (var p in world.Players)
                if (!p.Quit && p.Personality != AiPersonality.Inactive || p.IsHuman) players.Add(p);
            var columnRow = Element("stats-columns");
            var columns = new VisualElement[3];
            for (int i = 0; i < 3; i++) columnRow.Add(columns[i] = Element("stats-column"));
            body.Add(columnRow);
            foreach (var (kind, title, column) in Boards) AddBoard(world, players, kind, title, columns[column]);
            if (world.Diplomacy) AddTribes(world, columns[2]);
        }

        /// <summary>The top players for one statistic, and the player's own place if they're further down.</summary>
        void AddBoard(World world, List<Player> players, StatKind kind, string title, VisualElement column)
        {
            column.Add(Text(title, "row-title", "stats-heading"));
            var values = new List<(Player p, long value)>();
            foreach (var p in players)
            {
                long v = world.StatOf(p, kind, period);
                if (v > 0 || p.IsHuman) values.Add((p, v));
            }
            values.Sort((a, b) => b.value.CompareTo(a.value));
            int shown = 0;
            for (int i = 0; i < values.Count && shown < Shown; i++)
            {
                if (values[i].value <= 0) break;
                column.Add(Row(world, i + 1, values[i].p, values[i].value));
                shown++;
            }
            if (shown == 0) column.Add(Text("Nobody yet.", "row-level"));
            int you = values.FindIndex(x => x.p.IsHuman);
            if (you >= shown && you >= 0) column.Add(Row(world, you + 1, values[you].p, values[you].value));
        }

        VisualElement Row(World world, int rank, Player p, long value)
        {
            var row = Element("ranking-row");
            row.EnableInClassList("ranking-row--you", p.IsHuman);
            row.Add(Text(value > 0 ? $"{rank:N0}" : "–", "ranking-rank"));
            var name = Element("ranking-name");
            int id = p.Id;
            name.Add(Link(p.IsHuman ? $"{p.Name} (you)" : p.Name, () => links.OpenPlayer(id), "ranking-link"));
            var tribe = world.TribeOf(p);
            if (tribe != null)
            {
                int tid = tribe.Id;
                name.Add(Link($"[{tribe.Tag}]", () => links.OpenTribe(tid)));
            }
            row.Add(name);
            row.Add(Text($"{value:N0}", "ranking-number"));
            return row;
        }

        /// <summary>The tribes that conquered most (their members' counts, whenever they earned them).</summary>
        void AddTribes(World world, VisualElement column)
        {
            column.Add(Text("Tribes: villages conquered", "row-title", "stats-heading"));
            var tribes = new List<(Tribe t, long value)>();
            foreach (var t in world.ActiveTribes())
            {
                long v = world.StatOf(t, StatKind.VillagesConquered, period);
                if (v > 0) tribes.Add((t, v));
            }
            tribes.Sort((a, b) => b.value.CompareTo(a.value));
            if (tribes.Count == 0) column.Add(Text("Nobody yet.", "row-level"));
            var mine = world.TribeOf(world.HumanPlayer);
            for (int i = 0; i < tribes.Count && i < 5; i++)
            {
                var (t, value) = tribes[i];
                var row = Element("ranking-row");
                row.EnableInClassList("ranking-row--you", t == mine);
                row.Add(Text($"{i + 1:N0}", "ranking-rank"));
                int tid = t.Id;
                row.Add(Link($"[{t.Tag}] {t.Name}", () => links.OpenTribe(tid), "ranking-link", "ranking-name"));
                row.Add(Text($"{value:N0}", "ranking-number"));
                column.Add(row);
            }
        }

        /// <summary>The realm's counts for each of the last seven days.</summary>
        void AddHistory(World world)
        {
            body.Add(Text("The realm, day by day", "row-title", "stats-heading"));
            var header = Element("ranking-row", "ranking-header");
            header.Add(Text("Day", "ranking-rank", "stats-day"));
            header.Add(Text("Plundered", "ranking-number", "stats-wide"));
            header.Add(Text("Troops fallen", "ranking-number", "stats-wide"));
            header.Add(Text("Conquered", "ranking-number"));
            body.Add(header);
            foreach (var d in world.RecentDays(7))
            {
                int day = d.Day;
                long loot = d.Values[(int)StatKind.Loot], fallen = d.Values[(int)StatKind.TroopsLost], conquered = d.Values[(int)StatKind.VillagesConquered];
                var row = Element("ranking-row");
                row.Add(Text(day == World.DayOf(world.Now) ? $"{day} (today)" : $"{day}", "ranking-rank", "stats-day"));
                row.Add(Text($"{loot:N0}", "ranking-number", "stats-wide"));
                row.Add(Text($"{fallen:N0}", "ranking-number", "stats-wide"));
                row.Add(Text($"{conquered:N0}", "ranking-number"));
                body.Add(row);
            }
        }
    }
}
