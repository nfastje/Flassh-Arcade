using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The Ranking tab: every player by points, a page of <see cref="PageSize"/> at a time, as in Tribal Wars.
    /// Step through the pages, jump to the top or to your own rank, or come here from a player's profile to see
    /// them in the list (their row is marked, like yours). Also the tribes' ranking (on diplomacy worlds) and the
    /// world's statistics.
    /// </summary>
    public class RankingPanel
    {
        const int PageSize = 50;
        /// <summary>Real seconds between refreshes: points change all the time, and a list that reshuffles every second is hard to read.</summary>
        const float RefreshSeconds = 5f;

        public VisualElement Root { get; }

        readonly UiLinks links;
        readonly ScrollView list;
        readonly Label summary, pageLabel;
        readonly Button first, previous, next, showPlayers, showTribes, showStats;
        readonly VisualElement pager, header;
        readonly Label headerName, headerMembers, headerBloc, headerTribe, headerConquered, headerOda, headerOdd;
        readonly StatsPanel stats;
        readonly VisualElement column;
        /// <summary>On diplomacy worlds: the tribes' ranking rather than the players'; or the statistics.</summary>
        bool tribesShown, statsShown;
        float nextRefresh;
        // The rows are made once and filled in place on each refresh (rebuilding hundreds of elements every few
        // seconds made the tab slow): a page's worth for the players, and as many as there have been tribes.
        readonly VisualElement playerRows, tribeRows;
        readonly List<RankRow> players = new List<RankRow>();
        readonly List<RankRow> tribes = new List<RankRow>();

        /// <summary>One line of the ranking, reused for whoever's at that place now.</summary>
        class RankRow
        {
            public VisualElement Root, Swatch;
            public Label Rank, Note, Members, Bloc, Villages, Points, Conquered, Oda, Odd;
            public Button Name, Tag;
            public int Id = -1, TribeId = -1;
        }
        int pageStart;
        /// <summary>A player to mark and bring into view (from their profile), or -1.</summary>
        int focusId = -1;
        bool focusPending;

        public RankingPanel(UiLinks links)
        {
            this.links = links;
            Root = Element("army", "ranking");
            column = Element("ranking-column");
            column.Add(Text("Ranking", "heading"));

            // Players, tribes (on diplomacy worlds) or statistics: right under the heading, so they never move.
            var switcher = Element("option-row", "ranking-pager");
            showPlayers = ButtonWith("Players", () => { tribesShown = statsShown = false; Redraw(); }, "option");
            showTribes = ButtonWith("Tribes", () => { tribesShown = true; statsShown = false; Redraw(); }, "option");
            showStats = ButtonWith("Statistics", () => { statsShown = true; tribesShown = false; Redraw(); }, "option");
            switcher.Add(showPlayers);
            switcher.Add(showTribes);
            switcher.Add(showStats);
            column.Add(switcher);
            // The summary always takes the same room, so the pager below it stays put too.
            summary = Text("", "row-info", "ranking-summary");
            column.Add(summary);

            pager = Element("option-row", "ranking-pager");
            first = ButtonWith("Top", () => GoTo(0), "btn", "btn--small");
            previous = ButtonWith("« Previous", () => GoTo(pageStart - PageSize), "btn", "btn--small");
            pageLabel = Text("", "row-level", "ranking-page");
            next = ButtonWith("Next »", () => GoTo(pageStart + PageSize), "btn", "btn--small");
            pager.Add(first);
            pager.Add(previous);
            pager.Add(pageLabel);
            pager.Add(next);
            pager.Add(ButtonWith("Your rank", () => ShowPlayer(-1), "btn", "btn--small"));
            column.Add(pager);

            header = Element("ranking-row", "ranking-header");
            header.Add(Text("#", "ranking-rank"));
            headerName = Text("Lord", "ranking-name");
            header.Add(headerName);
            headerTribe = Text("Tribe", "ranking-tribe");
            header.Add(headerTribe);
            // The tribes' own columns: members, and the share their side holds with its allies.
            headerMembers = Text("Members", "ranking-number", "ranking-narrow");
            header.Add(headerMembers);
            headerBloc = Text("With allies", "ranking-number");
            headerBloc.tooltip = "The share of the goal's villages the tribe holds together with its allies";
            header.Add(headerBloc);
            header.Add(Text("Villages", "ranking-number"));
            header.Add(Text("Points", "ranking-number"));
            // The players' fighting record (all time), as in Tribal Wars' ranking.
            headerConquered = Text("Conquered", "ranking-number");
            headerConquered.tooltip = "Villages conquered, all time";
            header.Add(headerConquered);
            headerOda = Text("ODA", "ranking-number");
            headerOda.tooltip = "Opponents defeated attacking: enemy troops killed by this player's attacks, all time";
            header.Add(headerOda);
            headerOdd = Text("ODD", "ranking-number");
            headerOdd.tooltip = "Opponents defeated defending: attacking troops killed in this player's villages, all time";
            header.Add(headerOdd);
            column.Add(header);

            // The scrollbar is always there, and the header leaves the same room on its right, so every heading
            // sits over its column.
            list = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible };
            list.AddToClassList("ranking-list");
            list.verticalScroller.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float bar = list.verticalScroller.layout.width;
                if (!float.IsNaN(bar) && Math.Abs(header.resolvedStyle.paddingRight - (8 + bar)) > 0.5f) header.style.paddingRight = 8 + bar;
            });
            playerRows = Element();
            tribeRows = Element();
            list.Add(playerRows);
            list.Add(tribeRows);
            for (int i = 0; i < PageSize; i++)
            {
                var row = NewRow(true);
                players.Add(row);
                playerRows.Add(row.Root);
            }
            column.Add(list);
            stats = new StatsPanel(links);
            stats.Changed += Redraw;
            column.Add(stats.Root);
            Root.Add(column);

            // Opens on the player's own page.
            focusPending = true;
        }

        /// <summary>Every tribe by points: members, villages, and the share its bloc (with allies) holds.</summary>
        void RefreshTribes(World world)
        {
            var tribes = world.ActiveTribes();
            var strength = new Dictionary<int, (int points, int villages)>();
            foreach (var t in tribes) strength[t.Id] = world.TribeStrength(t);
            tribes.Sort((a, b) => strength[b.Id].points.CompareTo(strength[a.Id].points));
            var mine = world.TribeOf(world.HumanPlayer);

            while (this.tribes.Count < tribes.Count)
            {
                var row = NewRow(false);
                this.tribes.Add(row);
                tribeRows.Add(row.Root);
            }
            for (int i = 0; i < this.tribes.Count; i++)
            {
                var row = this.tribes[i];
                Show(row.Root, i < tribes.Count);
                if (i >= tribes.Count) continue;
                var t = tribes[i];
                row.Id = t.Id;
                row.Root.EnableInClassList("ranking-row--you", t == mine);
                SetText(row.Rank, $"{i + 1:N0}");
                row.Swatch.style.backgroundColor = t == mine ? MapView.TribeMateColor : MapView.TribeColor(t);
                SetText(row.Name, $"[{t.Tag}] {t.Name}");
                SetText(row.Members, $"{t.Members.Count:N0}");
                SetText(row.Bloc, $"{world.BlocShare(t):P1}");
                SetText(row.Villages, $"{strength[t.Id].villages:N0}");
                SetText(row.Points, $"{strength[t.Id].points:N0}");
                SetText(row.Oda, $"{world.StatOf(t, StatKind.DefeatedAttacking, StatPeriod.AllTime):N0}");
                SetText(row.Odd, $"{world.StatOf(t, StatKind.DefeatedDefending, StatPeriod.AllTime):N0}");
            }            SetText(summary, tribes.Count == 0 ? "No tribes have formed yet."
                : $"{tribes.Count:N0} tribes. A tribe and up to two allies holding {world.Settings.ConquestGoal:P0} of the {world.GoalVillagesLabel} for {World.HoldDays:0} days win." +
                  (mine != null ? $" Your side holds {world.BlocShare(mine):P1}." : "") + (world.HoldTribeId != -1 ? " " + HoldStatus(world) : ""));
        }

        /// <summary>Who is holding the goal right now, and when they'd win (diplomacy worlds).</summary>
        public static string HoldStatus(World world)
        {
            if (!world.Diplomacy || world.HoldTribeId == -1) return "";
            string when = World.FormatClock(world.HoldEnds);
            if (world.IsHumanSide(world.HoldTribeId)) return $"Your side holds the goal: keep it until {when} to win.";
            var t = world.FindTribe(world.HoldTribeId);
            return $"{t?.Name} [{t?.Tag}] holds the goal and wins on {when} unless their grip is broken.";
        }

        /// <summary>Turns to the page with this player on it and marks their row (-1: the player's own page).</summary>
        public void ShowPlayer(int playerId)
        {
            focusId = playerId;
            focusPending = true;
            Redraw();
        }

        void GoTo(int start)
        {
            pageStart = Math.Max(0, start);
            Redraw();
        }

        /// <summary>Refreshes at once (after a click), rather than at the next tick.</summary>
        void Redraw() => nextRefresh = 0;

        /// <summary>An empty row: a player's (name, tribe tag, a note if fallen) or a tribe's (name, members).</summary>
        RankRow NewRow(bool player)
        {
            var row = new RankRow { Root = Element("ranking-row") };
            row.Rank = Text("", "ranking-rank");
            row.Root.Add(row.Rank);
            var name = Element("ranking-name");
            row.Swatch = Element("legend-swatch");
            name.Add(row.Swatch);
            row.Name = Link("", () =>
            {
                if (row.Id < 0) return;
                if (player) links.OpenPlayer(row.Id);
                else links.OpenTribe(row.Id);
            }, "ranking-link");
            name.Add(row.Name);
            if (player)
            {
                row.Note = Text("(fallen)", "row-reason");
                name.Add(row.Note);
            }
            row.Root.Add(name);
            if (player)
            {
                // The tribe in its own column.
                var tribeCell = Element("ranking-tribe");
                row.Tag = Link("", () => { if (row.TribeId >= 0) links.OpenTribe(row.TribeId); });
                tribeCell.Add(row.Tag);
                row.Root.Add(tribeCell);
            }
            if (!player)
            {
                row.Members = Text("", "ranking-number", "ranking-narrow");
                row.Root.Add(row.Members);
                row.Bloc = Text("", "ranking-number");
                row.Root.Add(row.Bloc);
            }
            row.Villages = Text("", "ranking-number");
            row.Root.Add(row.Villages);
            row.Points = Text("", "ranking-number");
            row.Root.Add(row.Points);
            if (player)
            {
                row.Conquered = Text("", "ranking-number");
                row.Root.Add(row.Conquered);
            }
            // Both tables: opponents defeated attacking and defending (a tribe's: its members' together).
            row.Oda = Text("", "ranking-number");
            row.Root.Add(row.Oda);
            row.Odd = Text("", "ranking-number");
            row.Root.Add(row.Odd);
            return row;
        }

        public void Refresh(World world)
        {
            // Hundreds of lords' points change all the time: once a second is plenty.
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + RefreshSeconds;
            Show(showTribes, world.Diplomacy);
            if (!world.Diplomacy) tribesShown = false;
            showPlayers.EnableInClassList("option--selected", !tribesShown && !statsShown);
            showTribes.EnableInClassList("option--selected", tribesShown);
            showStats.EnableInClassList("option--selected", statsShown);
            Show(pager, !tribesShown && !statsShown);
            Show(header, !statsShown);
            Show(list, !statsShown);
            Show(stats.Root, statsShown);
            if (statsShown)
            {
                SetText(summary, "The realm's statistics, as they stand.");
                stats.Refresh(world);
                return;
            }
            Show(playerRows, !tribesShown);
            Show(tribeRows, tribesShown);
            SetText(headerName, tribesShown ? "Tribe" : "Lord");
            Show(headerMembers, tribesShown);
            Show(headerBloc, tribesShown);
            foreach (var h in new[] { headerTribe, headerConquered }) Show(h, !tribesShown);
            if (tribesShown)
            {
                RefreshTribes(world);
                return;
            }

            var rankings = world.Rankings();
            int you = rankings.FindIndex(r => r.Player.IsHuman);

            if (focusPending)
            {
                focusPending = false;
                int index = focusId >= 0 ? rankings.FindIndex(r => r.Player.Id == focusId) : you;
                if (index >= 0) pageStart = index / PageSize * PageSize;
            }
            pageStart = Math.Max(0, Math.Min(pageStart, Math.Max(0, rankings.Count - 1) / PageSize * PageSize));
            int end = Math.Min(rankings.Count, pageStart + PageSize);

            SetText(pageLabel, rankings.Count == 0 ? "" : $"{pageStart + 1:N0}–{end:N0} of {rankings.Count:N0}");
            first.SetEnabled(pageStart > 0);
            previous.SetEnabled(pageStart > 0);
            next.SetEnabled(end < rankings.Count);

            for (int k = 0; k < PageSize; k++)
            {
                var row = players[k];
                int i = pageStart + k;
                Show(row.Root, i < end);
                if (i >= end) continue;
                var r = rankings[i];
                row.Id = r.Player.Id;
                row.Root.EnableInClassList("ranking-row--you", r.Player.IsHuman);
                row.Root.EnableInClassList("ranking-row--focus", r.Player.Id == focusId && !r.Player.IsHuman);
                SetText(row.Rank, $"{i + 1:N0}");
                row.Swatch.style.backgroundColor = r.Player.IsHuman
                    ? MapView.PlayerColor
                    : MapView.RivalColors[r.Player.ColorIndex % MapView.RivalColors.Length];
                SetText(row.Name, r.Player.IsHuman ? $"{r.Player.Name} (you)" : r.Player.Name);
                var tribe = world.TribeOf(r.Player);
                row.TribeId = tribe?.Id ?? -1;
                Show(row.Tag, tribe != null);
                if (tribe != null) SetText(row.Tag, $"[{tribe.Tag}]");
                Show(row.Note, r.Villages == 0);
                SetText(row.Villages, $"{r.Villages:N0}");
                SetText(row.Points, $"{r.Points:N0}");
                SetText(row.Conquered, $"{world.StatOf(r.Player, StatKind.VillagesConquered, StatPeriod.AllTime):N0}");
                SetText(row.Oda, $"{world.StatOf(r.Player, StatKind.DefeatedAttacking, StatPeriod.AllTime):N0}");
                SetText(row.Odd, $"{world.StatOf(r.Player, StatKind.DefeatedDefending, StatPeriod.AllTime):N0}");
            }
            int own = world.HumanVillages().Count;
            string goal = $"You hold {own:N0} of the {world.GoalVillageCount:N0} {world.GoalVillagesLabel} ({world.HumanShare:P1}). " +
                          $"Win by holding {world.Settings.ConquestGoal:P0} of them (barbarian villages {(World.GoalOverAllVillages ? "included" : "don't count")})" +
                          (world.Diplomacy ? $" for {World.HoldDays:0} days, with your tribe and up to two allies or alone." : ".");
            SetText(summary, rankings.Count <= 1
                ? $"There are no rival lords in this world yet. {goal}"
                : $"You are ranked {you + 1:N0} of {rankings.Count:N0}. {goal}");
        }
    }
}
