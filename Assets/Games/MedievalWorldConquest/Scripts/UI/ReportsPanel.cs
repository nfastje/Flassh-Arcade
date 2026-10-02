using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The Reports tab: the player's battle reports on the left (the list, or the archive), the chosen one in full
    /// on the right. Reports can be ticked to archive or delete several at once.
    /// </summary>
    public class ReportsPanel
    {
        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly UiLinks links;
        readonly ScrollView list;
        readonly ScrollView detail;
        readonly Label empty, emptyArchive;
        readonly Button reportsTab, archiveTab, archiveButton;
        readonly CheckBox selectAll;
        /// <summary>The ticked reports, and the boxes showing them (rebuilt with the list).</summary>
        readonly HashSet<int> ticked = new HashSet<int>();
        readonly List<(int id, CheckBox box)> boxes = new List<(int, CheckBox)>();
        int? selectedId;
        /// <summary>Whether the archive is showing instead of the list.</summary>
        bool showArchive;
        /// <summary>Set when a report is opened from elsewhere: show whichever of the list or archive holds it.</summary>
        bool reveal;
        string listSignature;

        public ReportsPanel(MedievalWorldConquestGame game, UiLinks links)
        {
            this.game = game;
            this.links = links;
            Root = Element("army", "reports");

            var left = Element("reports-list-column");
            var tabs = Element("option-row", "reports-tabs");
            reportsTab = ButtonWith("Reports", () => ShowArchive(false), "option");
            archiveTab = ButtonWith("Archive", () => ShowArchive(true), "option");
            tabs.Add(reportsTab);
            tabs.Add(archiveTab);
            left.Add(tabs);

            // The buttons act on the ticked reports, or if none are ticked, on the one that's open.
            var actions = Element("option-row", "reports-actions");
            actions.Add(ButtonWith("Mark all read", () => game.MarkAllReportsRead(showArchive), "btn", "btn--small"));
            archiveButton = ButtonWith("Archive", () => ActOnChosen(game.ArchiveReports), "btn", "btn--small");
            actions.Add(archiveButton);
            actions.Add(ButtonWith("Delete", () => ActOnChosen(game.DeleteReports), "btn", "btn--small"));
            left.Add(actions);

            // A box to tick (or untick) every report, at the top of the column of boxes.
            var header = Element("report-row", "reports-select-all");
            selectAll = new CheckBox();
            selectAll.AddToClassList("report-check");
            selectAll.Changed += TickAll;
            header.Add(selectAll);
            left.Add(header);

            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("reports-list");
            left.Add(list);
            Root.Add(left);

            detail = new ScrollView(ScrollViewMode.Vertical);
            detail.AddToClassList("reports-detail");
            empty = Text("No reports yet. Attack a village from the Map tab and the battle report will appear here.", "row-info");
            emptyArchive = Text($"Nothing archived. Reports you archive, and the oldest once the list passes {World.MaxReports}, " +
                                $"are kept here (the newest {World.MaxArchivedReports:N0}).", "row-info");
            Root.Add(detail);
            ShowArchive(false);
        }

        void ShowArchive(bool archive)
        {
            if (archive != showArchive) ticked.Clear();
            showArchive = archive;
            reportsTab.EnableInClassList("option--selected", !archive);
            archiveTab.EnableInClassList("option--selected", archive);
            Show(archiveButton, !archive);
            list.scrollOffset = UnityEngine.Vector2.zero;
            listSignature = null;
        }

        /// <summary>Archives or deletes the ticked reports, or the open one if none are ticked.</summary>
        void ActOnChosen(Action<ICollection<int>> act)
        {
            var ids = new List<int>(ticked);
            if (ids.Count == 0 && selectedId.HasValue) ids.Add(selectedId.Value);
            if (ids.Count == 0) return;
            act(ids);
            if (selectedId.HasValue && ids.Contains(selectedId.Value)) selectedId = null;
            ticked.Clear();
            listSignature = null;
        }

        void TickAll(bool on)
        {
            foreach (var (id, box) in boxes)
            {
                box.SetValueWithoutNotify(on);
                if (on) ticked.Add(id);
                else ticked.Remove(id);
            }
        }

        void Tick(int id, bool on)
        {
            if (on) ticked.Add(id);
            else ticked.Remove(id);
            selectAll.SetValueWithoutNotify(boxes.Count > 0 && ticked.Count == boxes.Count);
        }

        public static string Title(BattleReport r) => r.Kind switch
        {
            ReportKind.Attack when r.Conquered => $"Conquered: {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY}) is yours!",
            ReportKind.Defense when r.Conquered => $"Village lost: {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY}) fell to {r.AttackerPlayer}",
            ReportKind.Attack => $"{(r.AttackerWon ? "Victory" : "Defeat")}: attack on {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})",
            ReportKind.Defense => $"{r.DefenderVillage} {(r.AttackerWon ? "lost the fight" : "held")}: {(string.IsNullOrEmpty(r.AttackerPlayer) ? "attack" : r.AttackerPlayer)} from {r.AttackerVillage} ({r.AttackerX}|{r.AttackerY})",
            ReportKind.ResourcesArrived => $"Resources delivered to {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})",
            _ => $"Support{(string.IsNullOrEmpty(r.AttackerPlayer) ? "" : $" from {r.AttackerPlayer}")} arrived at {r.DefenderVillage} ({r.DefenderX}|{r.DefenderY})",
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
            if (reveal && selectedId.HasValue)
            {
                reveal = false;
                int id = selectedId.Value;
                bool archived = world.IsArchived(id);
                if (archived != showArchive && (archived || world.Reports.Exists(r => r.Id == id))) ShowArchive(archived);
            }

            // Rebuild the list when reports arrive, move, are deleted or read: its size, ends and unread count tell.
            var reports = showArchive ? world.ArchivedReports : world.Reports;
            int unread = 0;
            foreach (var r in reports)
                if (!r.Read) unread++;
            string signature = $"{showArchive}:{selectedId}:{reports.Count}:{unread}:{world.NextReportId}:" +
                               (reports.Count > 0 ? $"{reports[0].Id}:{reports[reports.Count - 1].Id}" : "");
            SetText(archiveTab, world.ArchivedReports.Count == 0 ? "Archive" : $"Archive ({world.ArchivedReports.Count:N0})");
            if (signature == listSignature) return;
            listSignature = signature;

            list.Clear();
            boxes.Clear();
            var present = new HashSet<int>();
            for (int i = reports.Count - 1; i >= 0; i--)
            {
                var r = reports[i];
                int id = r.Id;
                present.Add(id);
                var row = Element("report-row");
                var box = new CheckBox();
                box.SetValueWithoutNotify(ticked.Contains(id));
                box.AddToClassList("report-check");
                box.Changed += on => Tick(id, on);
                row.Add(box);
                boxes.Add((id, box));
                var item = ButtonWith($"{Title(r)}\n{World.FormatClock(r.Time)}", () => Open(id), "report-item");
                item.EnableInClassList("report-item--unread", !r.Read);
                item.EnableInClassList("report-item--selected", selectedId == id);
                item.EnableInClassList("report-item--lost", !r.PlayerWon);
                row.Add(item);
                list.Add(row);
            }
            ticked.IntersectWith(present); // (ticks on reports that have gone)
            selectAll.SetValueWithoutNotify(boxes.Count > 0 && ticked.Count == boxes.Count);
            selectAll.SetEnabled(boxes.Count > 0);

            detail.Clear();
            var selected = selectedId.HasValue ? world.FindReport(selectedId.Value) : null;
            if (selected != null) ShowReport(world, selected);
            else if (reports.Count == 0) detail.Add(showArchive ? emptyArchive : empty);
            else detail.Add(Text("Choose a report on the left.", "row-info"));
        }

        /// <summary>Shows one report (e.g. from a village's window, or a raided village's latest in the Loot Assistant).</summary>
        public void Open(int id)
        {
            if (selectedId != id) reveal = true;
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
                // Who sent them and where they are, as links (as in an attack's report).
                detail.Add(Side("From", r.AttackerPlayer, r.AttackerPlayerId, r.AttackerVillage, r.AttackerVillageId, r.AttackerX, r.AttackerY));
                detail.Add(Side("To", r.DefenderPlayer, r.DefenderPlayerId, r.DefenderVillage, r.DefenderVillageId, r.DefenderX, r.DefenderY));
                bool yours = r.AttackerPlayerId < 0 || r.AttackerPlayerId == world.HumanPlayer?.Id;
                detail.Add(Text(yours ? "Your troops are now helping to defend the village. Send them home from either village's rally point."
                                      : $"{r.AttackerPlayer}'s troops are now helping to defend your village. You can send them home from its rally point.", "row-info"));
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

            if (r.Scouted)
            {
                // As much as enough scouts came back to see (any: the troops, above; half: the resources; seven in ten: the buildings).
                bool ours = r.Kind == ReportKind.Attack;
                detail.Add(Text(ours ? "Your scouts saw" : "Their scouts saw", "row-title", "report-side"));
                if (r.ScoutSurvival >= 0) detail.Add(Text($"{r.ScoutSurvival}% of the scouts made it back.", "row-level"));
                if (r.SawResources)
                {
                    var stores = LootLine("Resources:", r.ScoutedResources, 0, false);
                    stores.Q<Label>(className: "loot-total")?.RemoveFromHierarchy();
                    detail.Add(stores);
                }
                else detail.Add(Text($"Too few came back to count the resources (it takes {BattleReport.ResourcesSurvival}% of them).", "row-reason"));
                if (r.SawBuildings && r.ScoutedLevels != null && r.ScoutedLevels.Length > 0)
                {
                    var buildings = "";
                    foreach (var d in Buildings.Definitions)
                        if ((int)d.Type < r.ScoutedLevels.Length && r.ScoutedLevels[(int)d.Type] > 0)
                            buildings += $"{d.Name} {r.ScoutedLevels[(int)d.Type]} · ";
                    detail.Add(Text(buildings.TrimEnd(' ', '·'), "row-info"));
                }
                else detail.Add(Text($"Too few came back to see the buildings (it takes {BattleReport.BuildingsSurvival}% of them).", "row-reason"));
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
