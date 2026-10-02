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

        /// <summary>The boards, each in a column of its own side by side (and the tribes' after them), so none stack up into a long scroll.</summary>
        static readonly (StatKind kind, string title, int column)[] Boards =
        {
            (StatKind.Loot, "Resources plundered", 0),
            (StatKind.DefeatedAttacking, "Defeated attacking", 1),
            (StatKind.DefeatedDefending, "Defeated defending", 2),
            (StatKind.VillagesConquered, "Villages conquered", 3),
        };

        public StatsPanel(UiLinks links)
        {
            this.links = links;
            Root = Element("stats");
            var periods = Element("option-row", "ranking-pager");
            string[] names = { "Today", "This week", "All time", "Last ten days" };
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

            body.Add(WorldStrip(world));

            var players = new List<Player>();
            foreach (var p in world.Players)
                if (!p.Quit && p.Personality != AiPersonality.Inactive || p.IsHuman) players.Add(p);
            var columnRow = Element("stats-columns");
            var columns = new VisualElement[Boards.Length + (world.Diplomacy ? 1 : 0)];
            for (int i = 0; i < columns.Length; i++) columnRow.Add(columns[i] = Element("stats-column"));
            body.Add(columnRow);
            foreach (var (kind, title, column) in Boards) AddBoard(world, players, kind, title, columns[column]);
            if (world.Diplomacy) AddTribes(world, columns[Boards.Length]);
            AddRecords(world);
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
            row.Add(Figure(value));
            return row;
        }

        /// <summary>A board's figure, short (12.3M, 845k) to leave the names room; the exact one on hover.</summary>
        static Label Figure(long value)
        {
            var label = Text(Short(value), "ranking-number");
            if (value >= 10000) label.tooltip = $"{value:N0}";
            return label;
        }

        static string Short(long v) =>
            v >= 10_000_000 ? $"{v / 1_000_000.0:0.#}M" : v >= 1_000_000 ? $"{v / 1_000_000.0:0.##}M"
            : v >= 100_000 ? $"{v / 1000.0:0}k" : v >= 10_000 ? $"{v / 1000.0:0.#}k" : $"{v:N0}";

        /// <summary>The realm as it stands: lords left, barbarian villages, tribes and how they stand, the leading side.</summary>
        static VisualElement WorldStrip(World world)
        {
            var strip = Element("stats-strip");
            void Add(string label, string value)
            {
                var cell = Element("stats-strip-cell");
                cell.Add(Text(value, "stats-strip-value"));
                cell.Add(Text(label, "row-level"));
                strip.Add(cell);
            }
            int lords = 0;
            foreach (var p in world.Players)
                if (!p.IsHuman && !p.Quit && p.Personality != AiPersonality.Inactive && world.VillagesOf(p.Id).Count > 0) lords++;
            int barbarians = 0;
            foreach (var v in world.Villages) if (v.IsBarbarian) barbarians++;
            Add("villages", $"{world.Villages.Count:N0}");
            Add("barbarian", $"{barbarians:N0}");
            Add("lords left", $"{lords:N0}");
            if (world.Diplomacy)
            {
                var tribes = world.ActiveTribes();
                int allies = 0, pacts = 0, wars = 0;
                foreach (var r in world.Relations)
                    if (r.Kind == RelationKind.Ally) allies++;
                    else if (r.Kind == RelationKind.NonAggression) pacts++;
                    else if (r.Kind == RelationKind.Enemy) wars++;
                double lead = 0;
                foreach (var t in tribes) lead = System.Math.Max(lead, world.BlocShare(t));
                Add("tribes", $"{tribes.Count:N0}");
                Add("alliances", $"{allies:N0}");
                Add("pacts", $"{pacts:N0}");
                Add("wars", $"{wars:N0}");
                Add("leading side", $"{lead:P1}");
            }
            return strip;
        }

        /// <summary>The realm's records: the biggest haul, the bloodiest battle, the most conquests in a day, the largest village.</summary>
        void AddRecords(World world)
        {
            body.Add(Text("Records", "row-title", "stats-heading"));
            var box = Element("stats-records");
            var r = world.Records ?? new RealmRecords();
            void Add(string title, string text)
            {
                var cell = Element("stats-record");
                cell.Add(Text(title, "row-title"));
                cell.Add(Text(text, "row-info"));
                box.Add(cell);
            }
            string Against(RealmRecord x) => string.IsNullOrEmpty(x.Against) ? "" : $" against {x.Against}";
            Add("Biggest haul", r.BiggestHaul.Day == 0 ? "None yet." : $"{r.BiggestHaul.Value:N0} resources, by {r.BiggestHaul.Who}{Against(r.BiggestHaul)} at {r.BiggestHaul.Where}, day {r.BiggestHaul.Day}");
            Add("Bloodiest battle", r.BloodiestBattle.Day == 0 ? "None yet." : $"{r.BloodiestBattle.Value:N0} troops killed: {r.BloodiestBattle.Who}{Against(r.BloodiestBattle)} at {r.BloodiestBattle.Where}, day {r.BloodiestBattle.Day}");
            Add("Most conquests in a day", r.MostConquestsInADay.Day == 0 ? "None yet." : $"{r.MostConquestsInADay.Value:N0} villages, by {r.MostConquestsInADay.Who} on day {r.MostConquestsInADay.Day}");
            Village largest = null;
            foreach (var v in world.Villages) if (largest == null || v.Points > largest.Points) largest = v;
            Add("Largest village", largest == null ? "None yet." : $"{largest.Name} ({largest.X}|{largest.Y}), {largest.Points:N0} points, held by {world.OwnerName(largest)}");
            body.Add(box);
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
                row.Add(Figure(value));
                column.Add(row);
            }
        }

        /// <summary>The realm's counts for each of the last ten days.</summary>
        void AddHistory(World world)
        {
            body.Add(Text("The realm, day by day", "row-title", "stats-heading"));
            var header = Element("ranking-row", "ranking-header");
            header.Add(Text("Day", "ranking-rank", "stats-day"));
            header.Add(Text("Plundered", "ranking-number", "stats-wide"));
            header.Add(Text("Defeated attacking", "ranking-number", "stats-wide"));
            header.Add(Text("Defeated defending", "ranking-number", "stats-wide"));
            header.Add(Text("Conquered", "ranking-number", "stats-wide"));
            body.Add(header);
            foreach (var d in world.RecentDays(10))
            {
                int day = d.Day;
                long Value(StatKind k) => (int)k < d.Values.Length ? d.Values[(int)k] : 0;
                var row = Element("ranking-row");
                row.Add(Text(day == World.DayOf(world.Now) ? $"{day} (today)" : $"{day}", "ranking-rank", "stats-day"));
                row.Add(Text($"{Value(StatKind.Loot):N0}", "ranking-number", "stats-wide"));
                row.Add(Text($"{Value(StatKind.DefeatedAttacking):N0}", "ranking-number", "stats-wide"));
                row.Add(Text($"{Value(StatKind.DefeatedDefending):N0}", "ranking-number", "stats-wide"));
                row.Add(Text($"{Value(StatKind.VillagesConquered):N0}", "ranking-number", "stats-wide"));
                body.Add(row);
            }
        }
    }
}
