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

        public VisualElement Root { get; }

        readonly UiLinks links;
        readonly ScrollView list;
        readonly Label summary, pageLabel;
        readonly Button first, previous, next, showPlayers, showTribes, showStats;
        readonly VisualElement pager, header;
        readonly StatsPanel stats;
        /// <summary>On diplomacy worlds: the tribes' ranking rather than the players'; or the statistics.</summary>
        bool tribesShown, statsShown;
        string signature;
        float nextRefresh;
        int pageStart;
        /// <summary>A player to mark and bring into view (from their profile), or -1.</summary>
        int focusId = -1;
        bool focusPending;

        public RankingPanel(UiLinks links)
        {
            this.links = links;
            Root = Element("army", "ranking");
            var column = Element("ranking-column");
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
            header.Add(Text("Lord", "ranking-name"));
            header.Add(Text("Villages", "ranking-number"));
            header.Add(Text("Points", "ranking-number"));
            column.Add(header);

            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ranking-list");
            column.Add(list);
            stats = new StatsPanel(links);
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

            var now = new System.Text.StringBuilder("tribes|");
            foreach (var t in tribes) now.Append(t.Id).Append(':').Append(strength[t.Id].points).Append(':').Append(t.Members.Count).Append(',');
            string key = now.ToString();
            if (key == signature) return;
            signature = key;

            list.Clear();
            for (int i = 0; i < tribes.Count; i++)
            {
                var t = tribes[i];
                var row = Element("ranking-row");
                row.EnableInClassList("ranking-row--you", t == mine);
                row.Add(Text($"{i + 1:N0}", "ranking-rank"));
                var name = Element("ranking-name");
                var swatch = Element("legend-swatch");
                swatch.style.backgroundColor = t == mine ? MapView.TribeMateColor : MapView.TribeColor(t);
                name.Add(swatch);
                int id = t.Id;
                name.Add(Link($"[{t.Tag}] {t.Name}", () => links.OpenTribe(id), "ranking-link"));
                name.Add(Text($"{t.Members.Count} members  ·  with allies {world.BlocShare(t):P1}", "row-level"));
                row.Add(name);
                row.Add(Text($"{strength[t.Id].villages:N0}", "ranking-number"));
                row.Add(Text($"{strength[t.Id].points:N0}", "ranking-number"));
                list.Add(row);
            }
            SetText(summary, tribes.Count == 0 ? "No tribes have formed yet."
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

        /// <summary>Rebuilds on the next refresh, whatever changed.</summary>
        void Redraw()
        {
            signature = null;
            nextRefresh = 0;
        }

        public void Refresh(World world)
        {
            // Hundreds of lords' points change all the time: once a second is plenty.
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + 1f;
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

            // Rebuild only when something on this page changes.
            var now = new System.Text.StringBuilder().Append(pageStart).Append('|').Append(focusId).Append('|');
            for (int i = pageStart; i < end; i++)
                now.Append(rankings[i].Player.Id).Append(':').Append(rankings[i].Points).Append(':').Append(rankings[i].Villages).Append(',');
            string key = now.ToString();
            if (key == signature) return;
            signature = key;

            list.Clear();
            for (int i = pageStart; i < end; i++)
            {
                var r = rankings[i];
                var row = Element("ranking-row");
                row.EnableInClassList("ranking-row--you", r.Player.IsHuman);
                row.EnableInClassList("ranking-row--focus", r.Player.Id == focusId && !r.Player.IsHuman);

                row.Add(Text($"{i + 1:N0}", "ranking-rank"));
                var name = Element("ranking-name");
                var swatch = Element("legend-swatch");
                swatch.style.backgroundColor = r.Player.IsHuman
                    ? MapView.PlayerColor
                    : MapView.RivalColors[r.Player.ColorIndex % MapView.RivalColors.Length];
                name.Add(swatch);
                // The name opens the lord's profile.
                int id = r.Player.Id;
                name.Add(Link(r.Player.IsHuman ? $"{r.Player.Name} (you)" : r.Player.Name, () => links.OpenPlayer(id), "ranking-link"));
                var tribe = world.TribeOf(r.Player);
                if (tribe != null)
                {
                    int tid = tribe.Id;
                    name.Add(Link($"[{tribe.Tag}]", () => links.OpenTribe(tid)));
                }
                if (r.Villages == 0) name.Add(Text("(fallen)", "row-reason"));
                row.Add(name);
                row.Add(Text($"{r.Villages:N0}", "ranking-number"));
                row.Add(Text($"{r.Points:N0}", "ranking-number"));
                list.Add(row);
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
