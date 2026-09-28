using System;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>The Reports tab: the player's battle reports on the left, the chosen one in full on the right.</summary>
    public class ReportsPanel
    {
        static readonly string[] ShortNames = { "Spear", "Sword", "Axe", "Archer", "Scout", "L. Cav", "H. Cav", "Ram", "Catap." };

        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly ScrollView list;
        readonly ScrollView detail;
        readonly Label empty;
        int? selectedId;
        string listSignature;

        public ReportsPanel(MedievalWorldConquestGame game)
        {
            this.game = game;
            Root = Element("army", "reports");

            var left = Element("reports-list-column");
            var actions = Element("option-row", "reports-actions");
            actions.Add(ButtonWith("Mark all read", () => game.MarkAllReportsRead(), "btn", "btn--small"));
            actions.Add(ButtonWith("Delete", () =>
            {
                if (selectedId.HasValue) game.DeleteReport(selectedId.Value);
                selectedId = null;
            }, "btn", "btn--small"));
            left.Add(Text("Reports", "heading"));
            left.Add(actions);
            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("reports-list");
            left.Add(list);
            Root.Add(left);

            detail = new ScrollView(ScrollViewMode.Vertical);
            detail.AddToClassList("reports-detail");
            empty = Text("No reports yet. Attack a village from the Map tab and the battle report will appear here.", "row-info");
            Root.Add(detail);
        }

        public static string Title(BattleReport r) => r.Kind switch
        {
            ReportKind.Attack => $"{(r.AttackerWon ? "Victory" : "Defeat")}: attack on {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})",
            ReportKind.Defense => $"{(r.AttackerWon ? "Village lost the fight" : "Defended")}: {(string.IsNullOrEmpty(r.AttackerPlayer) ? "attack" : r.AttackerPlayer)} from {r.AttackerVillage} ({r.AttackerX}|{r.AttackerY})",
            _ => $"Support arrived at {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})",
        };

        /// <summary>"Aldric the Bold, " before a village name (nothing for older reports that didn't record it).</summary>
        static string Lord(string player) => string.IsNullOrEmpty(player) ? "" : $"{player}, ";

        public void Refresh(World world)
        {
            // Rebuild the list when reports arrive, are deleted or read.
            string signature = selectedId + ":";
            foreach (var r in world.Reports) signature += r.Id + (r.Read ? "r," : "u,");
            if (signature == listSignature) return;
            listSignature = signature;

            list.Clear();
            for (int i = world.Reports.Count - 1; i >= 0; i--)
            {
                var r = world.Reports[i];
                int id = r.Id;
                var item = ButtonWith($"{Title(r)}\n{World.FormatClock(r.Time)}", () => Open(id), "report-item");
                item.EnableInClassList("report-item--unread", !r.Read);
                item.EnableInClassList("report-item--selected", selectedId == id);
                item.EnableInClassList("report-item--lost", !r.PlayerWon);
                list.Add(item);
            }

            detail.Clear();
            var selected = selectedId.HasValue ? world.FindReport(selectedId.Value) : null;
            if (selected == null) detail.Add(world.Reports.Count == 0 ? empty : Text("Choose a report on the left.", "row-info"));
            else ShowReport(world, selected);
        }

        void Open(int id)
        {
            selectedId = id;
            game.MarkReportRead(id);
            listSignature = null; // redraw
        }

        void ShowReport(World world, BattleReport r)
        {
            detail.Add(Text(Title(r), "heading"));
            detail.Add(Text($"{World.FormatClock(r.Time)}", "row-level"));

            if (r.Kind == ReportKind.SupportArrived)
            {
                detail.Add(Text($"Your troops from {r.AttackerVillage} are now helping to defend {r.DefenderVillage}.", "row-info"));
                detail.Add(TroopTable(("Troops", r.AttackerSent)));
                return;
            }

            detail.Add(Text($"Luck: {(r.Luck >= 0 ? "+" : "")}{r.Luck * 100:0}%", "row-info"));

            detail.Add(Text($"Attacker: {Lord(r.AttackerPlayer)}{r.AttackerVillage} ({r.AttackerX}|{r.AttackerY})", "row-title", "report-side"));
            detail.Add(TroopTable(("Sent", r.AttackerSent), ("Lost", r.AttackerLost)));

            detail.Add(Text($"Defender: {Lord(r.DefenderPlayer)}{r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})", "row-title", "report-side"));
            if (r.DefenderVisible) detail.Add(TroopTable(("Troops", r.DefenderTroops), ("Lost", r.DefenderLost)));
            else detail.Add(Text("None of your troops survived to see the defenders.", "row-reason"));

            if (r.WallAfter != r.WallBefore)
                detail.Add(Text($"Rams damaged the wall: level {r.WallBefore} → {r.WallAfter}.", "row-info"));
            if (r.CatapultBuilding >= 0)
            {
                string building = Buildings.Get((BuildingType)r.CatapultBuilding).Name;
                detail.Add(Text(r.CatapultBefore == 0 ? $"The catapults found no {building} to hit."
                    : r.CatapultAfter == r.CatapultBefore ? $"The catapults didn't damage the {building} (level {r.CatapultBefore})."
                    : $"Catapults hit the {building}: level {r.CatapultBefore} → {r.CatapultAfter}.", "row-info"));
            }
            if (r.AttackerWon && r.Kind == ReportKind.Attack)
            {
                int total = r.Loot.Wood + r.Loot.Clay + r.Loot.Iron;
                detail.Add(Text($"Loot: wood {r.Loot.Wood:N0} · clay {r.Loot.Clay:N0} · iron {r.Loot.Iron:N0}  ({total:N0} of {r.LootCapacity:N0} carried)", "detail-effect"));
            }
            if (r.AttackerWon && r.Kind == ReportKind.Defense)
            {
                int total = r.Loot.Wood + r.Loot.Clay + r.Loot.Iron;
                detail.Add(Text($"The attackers carried off {total:N0} resources.", "row-reason"));
            }

            if (r.Scouted && r.ScoutedLevels != null)
            {
                detail.Add(Text("Your scouts saw", "row-title", "report-side"));
                detail.Add(Text($"Resources: wood {r.ScoutedResources.Wood:N0} · clay {r.ScoutedResources.Clay:N0} · iron {r.ScoutedResources.Iron:N0}", "row-info"));
                var buildings = "";
                foreach (var d in Buildings.Definitions)
                    if ((int)d.Type < r.ScoutedLevels.Length && r.ScoutedLevels[(int)d.Type] > 0)
                        buildings += $"{d.Name} {r.ScoutedLevels[(int)d.Type]} · ";
                detail.Add(Text(buildings.TrimEnd(' ', '·'), "row-info"));
            }
        }

        /// <summary>A small table: one column per unit type, one row per set of numbers.</summary>
        static VisualElement TroopTable(params (string label, int[] values)[] rows)
        {
            var table = Element("troop-table");
            var header = Element("troop-row");
            header.Add(Text("", "troop-cell", "troop-label"));
            foreach (var name in ShortNames) header.Add(Text(name, "troop-cell", "troop-head"));
            table.Add(header);
            foreach (var (label, values) in rows)
            {
                var row = Element("troop-row");
                row.Add(Text(label, "troop-cell", "troop-label"));
                for (int i = 0; i < ShortNames.Length; i++)
                {
                    int v = values != null && i < values.Length ? values[i] : 0;
                    var cell = Text(v == 0 ? "–" : v.ToString("N0"), "troop-cell");
                    if (v == 0) cell.AddToClassList("troop-cell--zero");
                    row.Add(cell);
                }
                table.Add(row);
            }
            return table;
        }
    }
}
