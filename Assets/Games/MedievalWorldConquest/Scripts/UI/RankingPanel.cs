using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>The Ranking tab: every lord in the world by points, the player's row highlighted.</summary>
    public class RankingPanel
    {
        public VisualElement Root { get; }

        readonly ScrollView list;
        readonly Label summary;
        string signature;
        float nextRefresh;

        public RankingPanel()
        {
            Root = Element("army", "ranking");
            var column = Element("ranking-column");
            column.Add(Text("Ranking", "heading"));
            summary = Text("", "row-info");
            column.Add(summary);

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
        }

        public void Refresh(World world)
        {
            // Hundreds of lords' points change all the time: once a second is plenty.
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + 1f;
            var rankings = world.Rankings();
            // Rebuild only when a total changes.
            string now = "";
            foreach (var r in rankings) now += r.Player.Id + ":" + r.Points + ":" + r.Villages + ",";
            if (now == signature) return;
            signature = now;

            int lords = rankings.Count - 1, you = 0;
            list.Clear();
            for (int i = 0; i < rankings.Count; i++)
            {
                var r = rankings[i];
                var row = Element("ranking-row");
                row.EnableInClassList("ranking-row--you", r.Player.IsHuman);
                if (r.Player.IsHuman) you = i + 1;

                row.Add(Text($"{i + 1}", "ranking-rank"));
                var name = Element("ranking-name");
                var swatch = Element("legend-swatch");
                swatch.style.backgroundColor = r.Player.IsHuman
                    ? MapView.PlayerColor
                    : MapView.RivalColors[r.Player.ColorIndex % MapView.RivalColors.Length];
                name.Add(swatch);
                name.Add(Text(r.Player.IsHuman ? $"{r.Player.Name} (you)" : r.Player.Name, "row-title"));
                if (r.Villages == 0) name.Add(Text("(fallen)", "row-reason"));
                row.Add(name);
                row.Add(Text($"{r.Villages:N0}", "ranking-number"));
                row.Add(Text($"{r.Points:N0}", "ranking-number"));
                list.Add(row);
            }
            SetText(summary, lords == 0
                ? "There are no rival lords in this world yet."
                : $"You are ranked {you} of {rankings.Count}. More lords settle on the frontier as the world grows.");
        }
    }
}
