using System;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>The Reports tab: the player's battle reports on the left, the chosen one in full on the right.</summary>
    public class ReportsPanel
    {
        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly UiLinks links;
        readonly ScrollView list;
        readonly ScrollView detail;
        readonly Label empty;
        int? selectedId;
        string listSignature;

        public ReportsPanel(MedievalWorldConquestGame game, UiLinks links)
        {
            this.game = game;
            this.links = links;
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
            ReportKind.Attack when r.Conquered => $"Conquered: {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY}) is yours!",
            ReportKind.Defense when r.Conquered => $"Village lost: {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY}) fell to {r.AttackerPlayer}",
            ReportKind.Attack => $"{(r.AttackerWon ? "Victory" : "Defeat")}: attack on {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})",
            ReportKind.Defense => $"{r.DefenderVillage} {(r.AttackerWon ? "lost the fight" : "held")}: {(string.IsNullOrEmpty(r.AttackerPlayer) ? "attack" : r.AttackerPlayer)} from {r.AttackerVillage} ({r.AttackerX}|{r.AttackerY})",
            ReportKind.ResourcesArrived => $"Resources delivered to {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})",
            _ => $"Support arrived at {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})",
        };

        /// <summary>"Attacker: [player] [village (x|y)]", the names as links to their windows.</summary>
        VisualElement Side(string role, string player, int playerId, string village, int villageId, int x, int y)
        {
            var line = Element("link-line", "report-side");
            line.Add(Text($"{role}:", "row-title", "report-role"));
            if (playerId >= 0 && !string.IsNullOrEmpty(player)) line.Add(Link(player, () => links.OpenPlayer(playerId), "link--owner"));
            else if (!string.IsNullOrEmpty(player)) line.Add(Text(player, "row-info", "link-text"));
            line.Add(Link($"{village} ({x}|{y})", () => links.OpenVillage(villageId)));
            return line;
        }

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

        /// <summary>Shows one report (e.g. from a village's window).</summary>
        public void Open(int id)
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

            if (r.Kind == ReportKind.ResourcesArrived)
            {
                detail.Add(Side("From", r.AttackerPlayer, r.AttackerPlayerId, r.AttackerVillage, r.AttackerVillageId, r.AttackerX, r.AttackerY));
                detail.Add(Side("To", r.DefenderPlayer, r.DefenderPlayerId, r.DefenderVillage, r.DefenderVillageId, r.DefenderX, r.DefenderY));
                var goods = LootLine("Delivered:", r.Loot, 0, false);
                goods.Q<Label>(className: "loot-total")?.RemoveFromHierarchy();
                detail.Add(goods);
                detail.Add(Text("Whatever the warehouse couldn't hold was lost.", "row-info"));
                return;
            }

            detail.Add(Text($"Luck: {(r.Luck >= 0 ? "+" : "")}{r.Luck * 100:0}%", "row-info"));

            detail.Add(Side("Attacker", r.AttackerPlayer, r.AttackerPlayerId, r.AttackerVillage, r.AttackerVillageId, r.AttackerX, r.AttackerY));
            detail.Add(TroopTable(("Sent", r.AttackerSent), ("Lost", r.AttackerLost)));

            detail.Add(Side("Defender", r.DefenderPlayer, r.DefenderPlayerId, r.DefenderVillage, r.DefenderVillageId, r.DefenderX, r.DefenderY));
            if (r.DefenderVisible) detail.Add(TroopTable(("Troops", r.DefenderTroops), ("Lost", r.DefenderLost)));
            else detail.Add(Text("None of your troops survived to see the defenders.", "row-reason"));

            if (r.WallAfter != r.WallBefore)
                detail.Add(Text($"Rams damaged the wall: level {r.WallBefore} → {r.WallAfter}.", "row-info"));
            if (r.Conquered)
                detail.Add(Text(r.Kind == ReportKind.Attack
                    ? $"The noblemen won {r.DefenderVillage} over: loyalty {r.LoyaltyBefore} → 0. The surviving troops have moved in, and its loyalty to you starts at {r.LoyaltyAfter}."
                    : $"{r.DefenderVillage}'s loyalty fell from {r.LoyaltyBefore} to 0 and it now belongs to {r.AttackerPlayer}.", "detail-effect"));
            else if (r.LoyaltyBefore > r.LoyaltyAfter && r.LoyaltyAfter >= 0)
                detail.Add(Text($"Noblemen swayed the village: loyalty {r.LoyaltyBefore} → {r.LoyaltyAfter}.", r.Kind == ReportKind.Attack ? "row-info" : "row-reason"));
            if (r.CatapultBuilding >= 0)
            {
                string building = Buildings.Get((BuildingType)r.CatapultBuilding).Name;
                detail.Add(Text(r.CatapultBefore == 0 ? $"The catapults found no {building} to hit."
                    : r.CatapultAfter == r.CatapultBefore ? $"The catapults didn't damage the {building} (level {r.CatapultBefore})."
                    : $"Catapults hit the {building}: level {r.CatapultBefore} → {r.CatapultAfter}.", "row-info"));
            }
            if (r.AttackerWon && !r.Conquered)
                detail.Add(LootLine(r.Kind == ReportKind.Attack ? "Loot:" : "Carried off:", r.Loot, r.LootCapacity, r.Kind == ReportKind.Defense));

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

        /// <summary>
        /// What was taken, as in Tribal Wars: each resource's icon and amount, then how much was carried out of what
        /// the surviving troops could carry.
        /// </summary>
        static VisualElement LootLine(string label, Cost loot, int capacity, bool lost)
        {
            var line = Element("loot-line");
            line.Add(Text(label, "row-title", "loot-label"));
            foreach (var (icon, amount) in new[] { (Icons.Wood, loot.Wood), (Icons.Clay, loot.Clay), (Icons.Iron, loot.Iron) })
            {
                line.Add(Icons.Element(icon, 18, "loot-icon"));
                line.Add(Text($"{amount:N0}", "loot-value"));
            }
            int total = loot.Wood + loot.Clay + loot.Iron;
            line.Add(Text($"{total:N0} / {capacity:N0}", "loot-total"));
            line.EnableInClassList("loot-line--lost", lost);
            return line;
        }

        /// <summary>A small table: one column per unit type, one row per set of numbers.</summary>
        static VisualElement TroopTable(params (string label, int[] values)[] rows)
        {
            var table = Element("troop-table");
            var header = Element("troop-row");
            header.Add(Text("", "troop-cell", "troop-label"));
            // Unit icons across the top, as in Tribal Wars' reports (the name shows on hover).
            foreach (var type in Units.InDisplayOrder)
            {
                var cell = Element("troop-cell", "troop-head");
                cell.Add(Icons.Element(Icons.Unit(type), 20));
                cell.tooltip = Units.Get(type).Name;
                header.Add(cell);
            }
            table.Add(header);
            foreach (var (label, values) in rows)
            {
                var row = Element("troop-row");
                row.Add(Text(label, "troop-cell", "troop-label"));
                foreach (var type in Units.InDisplayOrder)
                {
                    int i = (int)type;
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
