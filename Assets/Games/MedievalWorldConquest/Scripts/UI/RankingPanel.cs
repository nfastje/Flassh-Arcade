using System;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The Ranking tab: every player by points, a page of <see cref="PageSize"/> at a time, as in Tribal Wars.
    /// Step through the pages, jump to the top or to your own rank, or come here from a player's profile to see
    /// them in the list (their row is marked, like yours).
    /// </summary>
    public class RankingPanel
    {
        const int PageSize = 50;

        public VisualElement Root { get; }

        readonly UiLinks links;
        readonly ScrollView list;
        readonly Label summary, pageLabel;
        readonly Button first, previous, next;
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
            summary = Text("", "row-info");
            column.Add(summary);

            var pager = Element("option-row", "ranking-pager");
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

            var header = Element("ranking-row", "ranking-header");
            header.Add(Text("#", "ranking-rank"));
            header.Add(Text("Lord", "ranking-name"));
            header.Add(Text("Villages", "ranking-number"));
            header.Add(Text("Points", "ranking-number"));
            column.Add(header);

            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ranking-list");
            column.Add(list);
            Root.Add(column);

            // Opens on the player's own page.
            focusPending = true;
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
                if (r.Villages == 0) name.Add(Text("(fallen)", "row-reason"));
                row.Add(name);
                row.Add(Text($"{r.Villages:N0}", "ranking-number"));
                row.Add(Text($"{r.Points:N0}", "ranking-number"));
                list.Add(row);
            }

            int own = world.HumanVillages().Count;
            string goal = $"You hold {own:N0} of the {world.LordVillageCount:N0} villages players rule ({world.HumanShare:P1}); " +
                          $"win by holding {world.Settings.ConquestGoal:P0} of them (barbarian villages don't count).";
            SetText(summary, rankings.Count <= 1
                ? $"There are no rival lords in this world yet. {goal}"
                : $"You are ranked {you + 1:N0} of {rankings.Count:N0}. {goal}");
        }
    }
}
