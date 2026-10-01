using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The Loot Assistant, as Tribal Wars' Farm Assist: templates A and B to edit (C is worked out from the
    /// scouts), the raid cycle, and the villages around the current one to raid with a click, each with how the
    /// last raid went (a green, yellow or red dot, and whether it came back full), what's known of its wall and
    /// stores, and how long ago it was hit.
    /// </summary>
    public class LootPanel
    {
        static readonly string Letters = "ABC";

        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly UiLinks links;
        readonly IntegerField[,] templateFields = new IntegerField[2, Units.Count];
        readonly Label[] templateCarry = new Label[2];
        readonly Button[] cycleWith = new Button[3];
        readonly Label fromLabel, cycleHeading, listHeading, homeTitle, homeNone, raidsLeft;
        readonly Label[] homeCounts = new Label[Units.Count];
        readonly VisualElement[] homeCells = new VisualElement[Units.Count];
        readonly VisualElement cycleRows, farmRows;
        readonly List<FarmRow> farms = new List<FarmRow>();
        readonly List<CycleRow> cycles = new List<CycleRow>();
        string signature;
        float nextRefresh;
        int cycleTemplate;
        World lastWorld, templatesFor;

        class FarmRow
        {
            public int TargetId;
            public VisualElement Dot;
            public Label Full, Distance, LastRaid, Loot, Wall, Expected;
            public Button[] Send = new Button[3];
            public Button Cycle;
        }

        class CycleRow
        {
            public int TargetId;
            public VisualElement Dot;
            public Label Status;
        }

        public LootPanel(MedievalWorldConquestGame game, UiLinks links)
        {
            this.game = game;
            this.links = links;
            Root = Element("army", "overview");
            var layout = Element("overview-column", "loot-layout");
            Root.Add(layout);

            // Pinned above the list (it never scrolls away): the troops at home in the village raiding, and how
            // many more raids templates A and B can make with them.
            var homeBar = Element("loot-home-bar");
            homeTitle = Text("", "row-title", "loot-home-title");
            homeBar.Add(homeTitle);
            var counts = Element("loot-home-units");
            foreach (var type in Units.InDisplayOrder)
            {
                var cell = Element("loot-home-cell");
                cell.tooltip = Units.Get(type).Name;
                cell.Add(Icons.Element(Icons.Unit(type), 18, "cost-icon"));
                homeCounts[(int)type] = Text("", "loot-home-count");
                cell.Add(homeCounts[(int)type]);
                homeCells[(int)type] = cell;
                counts.Add(cell);
            }
            homeNone = Text("No troops at home.", "row-level");
            counts.Add(homeNone);
            homeBar.Add(counts);
            raidsLeft = Text("", "row-title", "loot-raids-left");
            raidsLeft.tooltip = "How many more times each template can be sent with the troops at home";
            homeBar.Add(raidsLeft);
            layout.Add(homeBar);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("loot-scroll");
            layout.Add(scroll);

            scroll.Add(Text("Loot Assistant", "heading"));
            scroll.Add(Text("Raid with a click using templates A and B, or C: just enough of the village's troops (fastest first) to carry off " +
                            "what your scouts last saw plus what the mines have made since, and a scout to check what's left (hover over C to see " +
                            "what it would send). A village's buttons gray out while a raid is on its way there or back. " +
                            "Put villages in the raid cycle and they're raided again each time " +
                            "the raiders get home, even while the game is closed. A cycle stops by itself if a raid is beaten or loses more than " +
                            "a tenth of its troops.", "row-info"));

            // Templates A and B.
            var templates = Element("pane-box", "manager-editor");
            templates.Add(Text("Templates", "pane-title"));
            for (int which = 0; which < 2; which++)
            {
                int w = which;
                var row = Element("loot-template");
                row.Add(Text(Letters[which].ToString(), "loot-letter"));
                foreach (var type in Units.InDisplayOrder)
                {
                    if (type == UnitType.Nobleman) continue;
                    var cell = Element("manager-troop-cell");
                    cell.tooltip = Units.Get(type).Name;
                    cell.Add(Icons.Element(Icons.Unit(type), 20, "cost-icon"));
                    var field = new IntegerField { value = 0 };
                    field.AddToClassList("amount-field");
                    field.AddToClassList("loot-amount");
                    templateFields[which, (int)type] = field;
                    cell.Add(field);
                    row.Add(cell);
                }
                templateCarry[which] = Text("", "row-level", "loot-carry");
                row.Add(templateCarry[which]);
                row.Add(ButtonWith("Save", () => SaveTemplate(w), "btn", "btn--small", "count-btn"));
                templates.Add(row);
            }
            scroll.Add(templates);

            // The raid cycle.
            cycleHeading = Text("Raid cycle", "heading");
            scroll.Add(cycleHeading);
            cycleRows = Element();
            scroll.Add(cycleRows);

            // The villages around the current one.
            listHeading = Text("Villages to raid", "heading");
            scroll.Add(listHeading);
            var from = Element("send-to-row", "loot-from");
            fromLabel = Text("", "row-info");
            from.Add(fromLabel);
            from.Add(Element("spacer"));
            from.Add(Text("Add to the cycle with", "row-level"));
            for (int i = 0; i < 3; i++)
            {
                int which = i;
                cycleWith[i] = ButtonWith(Letters[i].ToString(), () => ChooseCycleTemplate(which), "option", "loot-choice");
                from.Add(cycleWith[i]);
            }
            scroll.Add(from);
            ChooseCycleTemplate(0);

            var header = Element("overview-row", "ranking-header");
            header.Add(Text("", "overview-cell", "loot-dot-cell"));
            header.Add(Text("Village", "overview-cell", "overview-name"));
            header.Add(Text("Distance", "overview-cell", "loot-col"));
            header.Add(Text("Last raid", "overview-cell", "loot-col-wide"));
            header.Add(Text("Haul", "overview-cell", "loot-col"));
            header.Add(Text("Wall", "overview-cell", "loot-col-narrow"));
            header.Add(Text("Expected", "overview-cell", "loot-col"));
            scroll.Add(header);
            farmRows = Element();
            scroll.Add(farmRows);
        }

        void ChooseCycleTemplate(int which)
        {
            cycleTemplate = which;
            for (int i = 0; i < 3; i++) cycleWith[i].EnableInClassList("option--selected", i == which);
        }

        void SaveTemplate(int which)
        {
            var troops = new int[Units.Count];
            for (int i = 0; i < Units.Count; i++) troops[i] = Math.Max(0, templateFields[which, i]?.value ?? 0);
            game.SetLootTemplate(which, troops);
            templatesFor = null;
            nextRefresh = 0;
        }

        public void Refresh(World world)
        {
            lastWorld = world;
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + 0.5f;
            var home = world.PlayerVillage;
            if (home == null || !world.LootAssistantUnlocked) return;

            var list = world.LootList(home);
            var cycling = world.CycleTargets();
            string now = home.Id + "|" + string.Join(",", list.ConvertAll(v => v.Id.ToString())) + "|" +
                         string.Join(",", cycling.ConvertAll(t => t.VillageId + ":" + t.FromVillageId + ":" + t.Template));
            // The template boxes show what's saved: for a world just opened, and after a save (not while being typed in).
            if (templatesFor != world && !AnyFieldFocused())
            {
                templatesFor = world;
                FillTemplates(world);
            }
            if (now != signature)
            {
                signature = now;
                Rebuild(world, home, list, cycling);
            }

            SetText(fromLabel, $"Raiding from {home.Name} ({home.X}|{home.Y}). Nearest first.");
            RefreshHome(world, home);
            for (int i = 0; i < 2; i++)
                SetText(templateCarry[i], $"carries {Battle.CarryCapacity(world.LootTemplate(i)):N0}");
            foreach (var row in cycles) UpdateCycleRow(world, row);
            foreach (var row in farms) UpdateFarmRow(world, home, row);
        }

        /// <summary>The pinned bar: troops at home, and raids left with A and B.</summary>
        void RefreshHome(World world, Village home)
        {
            SetText(homeTitle, $"At home in {home.Name}:");
            bool any = false;
            foreach (var type in Units.InDisplayOrder)
            {
                int n = home.TroopCount(type);
                Show(homeCells[(int)type], n > 0);
                if (n > 0) SetText(homeCounts[(int)type], $"{n:N0}");
                any |= n > 0;
            }
            Show(homeNone, !any);
            SetText(raidsLeft, $"Raids left:  A × {RaidsLeft(home, world.LootTemplate(World.TemplateA))}   B × {RaidsLeft(home, world.LootTemplate(World.TemplateB))}");
        }

        /// <summary>How many times a template can be sent from a village with the troops at home ("–" if it's empty).</summary>
        static string RaidsLeft(Village home, int[] template)
        {
            int best = int.MaxValue;
            for (int i = 0; i < Units.Count && template != null && i < template.Length; i++)
                if (template[i] > 0) best = Math.Min(best, home.TroopCount((UnitType)i) / template[i]);
            return best == int.MaxValue ? "–" : best.ToString("N0");
        }

        bool AnyFieldFocused()
        {
            var focused = Root.panel?.focusController?.focusedElement as VisualElement;
            return focused is IntegerField || focused?.GetFirstAncestorOfType<IntegerField>() != null;
        }

        void FillTemplates(World world)
        {
            for (int which = 0; which < 2; which++)
            {
                var t = world.LootTemplate(which);
                for (int i = 0; i < Units.Count; i++) templateFields[which, i]?.SetValueWithoutNotify(t != null && i < t.Length ? t[i] : 0);
            }
        }

        void Rebuild(World world, Village home, List<Village> list, List<LootTarget> cycling)
        {
            cycleRows.Clear();
            cycles.Clear();
            SetText(cycleHeading, cycling.Count == 0 ? "Raid cycle" : $"Raid cycle ({cycling.Count})");
            if (cycling.Count == 0)
                cycleRows.Add(Text("No villages in the cycle yet. Choose a template above the list, then press Cycle on a village.", "row-level"));
            foreach (var t in cycling)
            {
                var target = world.FindVillage(t.VillageId);
                var from = world.FindVillage(t.FromVillageId);
                if (target == null) continue;
                int id = t.VillageId;
                var row = new CycleRow { TargetId = id };
                var line = Element("overview-row");
                row.Dot = Element("loot-dot");
                var dotCell = Element("loot-dot-cell");
                dotCell.Add(row.Dot);
                line.Add(dotCell);
                var name = Element("overview-name");
                name.Add(Link(target.Name, () => links.OpenVillage(id)));
                name.Add(Text($"({target.X}|{target.Y})", "row-level"));
                line.Add(name);
                line.Add(Text($"from {from?.Name ?? "?"}  ·  template {Letters[t.Template]}", "row-level", "loot-col-wide"));
                row.Status = Text("", "row-level", "manager-details");
                line.Add(row.Status);
                line.Add(ButtonWith("Stop", () => { game.StopCycle(id); signature = null; nextRefresh = 0; }, "btn", "btn--small", "count-btn"));
                cycleRows.Add(line);
                cycles.Add(row);
            }

            farmRows.Clear();
            farms.Clear();
            if (list.Count == 0) farmRows.Add(Text($"No barbarian villages within {World.LootRange:0} fields of {home.Name}.", "row-level"));
            foreach (var v in list)
            {
                int id = v.Id;
                var row = new FarmRow { TargetId = id };
                var line = Element("overview-row");
                row.Dot = Element("loot-dot");
                var dotCell = Element("loot-dot-cell");
                dotCell.Add(row.Dot);
                line.Add(dotCell);
                var name = Element("overview-name");
                name.Add(Link(v.Name, () => links.OpenVillage(id)));
                name.Add(Text($"({v.X}|{v.Y})", "row-level"));
                row.Full = Text("▲", "loot-full");
                row.Full.tooltip = "The last raid came back with all it could carry: there's likely more";
                name.Add(row.Full);
                line.Add(name);
                row.Distance = Text("", "overview-cell", "loot-col");
                line.Add(row.Distance);
                row.LastRaid = Text("", "overview-cell", "loot-col-wide");
                line.Add(row.LastRaid);
                row.Loot = Text("", "overview-cell", "loot-col");
                line.Add(row.Loot);
                row.Wall = Text("", "overview-cell", "loot-col-narrow");
                line.Add(row.Wall);
                row.Expected = Text("", "overview-cell", "loot-col");
                row.Expected.tooltip = "What should be there by now, from the last scouting";
                line.Add(row.Expected);
                for (int i = 0; i < 3; i++)
                {
                    int which = i;
                    row.Send[i] = ButtonWith(Letters[i].ToString(), () => { game.SendLoot(id, which); nextRefresh = 0; }, "btn", "btn--small", "loot-send");
                    line.Add(row.Send[i]);
                }
                row.Cycle = ButtonWith("Cycle", () =>
                {
                    var t = lastWorld?.LootTargetFor(id);
                    if (t != null && t.Cycling) game.StopCycle(id);
                    else game.StartCycle(id, cycleTemplate);
                    signature = null;
                    nextRefresh = 0;
                }, "btn", "btn--small", "count-btn");
                line.Add(row.Cycle);
                farmRows.Add(line);
                farms.Add(row);
            }
        }

        static void SetDot(VisualElement dot, RaidResult result)
        {
            dot.EnableInClassList("loot-dot--clean", result == RaidResult.Clean);
            dot.EnableInClassList("loot-dot--losses", result == RaidResult.Losses);
            dot.EnableInClassList("loot-dot--defeat", result == RaidResult.Defeat);
            dot.tooltip = result == RaidResult.Clean ? "Last raid: won, no losses" : result == RaidResult.Losses ? "Last raid: won, with losses"
                : result == RaidResult.Defeat ? "Last raid: beaten" : "Not raided yet";
        }

        void UpdateCycleRow(World world, CycleRow row)
        {
            var t = world.LootTargetFor(row.TargetId);
            if (t == null) return;
            SetDot(row.Dot, t.LastResult);
            SetText(row.Status, world.CycleStatus(t));
        }

        void UpdateFarmRow(World world, Village home, FarmRow row)
        {
            var v = world.FindVillage(row.TargetId);
            if (v == null) return;
            var t = world.LootTargetFor(row.TargetId);
            SetDot(row.Dot, t?.LastResult ?? RaidResult.None);
            Show(row.Full, t != null && t.FullHaul && t.LastResult != RaidResult.Defeat);
            SetText(row.Distance, $"{World.Distance(home, v):0.0}");
            SetText(row.LastRaid, t == null || t.LastRaidAt < 0 ? "—" : Real(world, world.Now - t.LastRaidAt) + " ago");
            SetText(row.Loot, t == null || t.LastRaidAt < 0 ? "—" : $"{t.LastLoot.Wood + t.LastLoot.Clay + t.LastLoot.Iron:N0}");
            int wall = world.ScoutedWall(v);
            SetText(row.Wall, wall >= 0 ? wall.ToString() : "?");
            SetText(row.Expected, wall >= 0 ? $"{world.ExpectedLoot(v):N0}" : "?");
            // Grayed while a raid from here is on its way there or back. (Buttons that can't send stay clickable,
            // only faded, so their tooltip still says why; clicking says so too.)
            bool sent = world.RaidUnderway(home.Id, v.Id);
            for (int i = 0; i < 3; i++)
            {
                var troops = world.LootTroops(i, home, v);
                bool can = troops != null && world.CheckSend(home, v, troops, CommandKind.Attack).Status == SendStatus.Ok;
                row.Send[i].EnableInClassList("loot-send--sent", sent);
                row.Send[i].EnableInClassList("loot-send--unable", !can && !sent);
                string what = troops == null ? (i == World.TemplateC ? world.WhyNoC(home, v) : "Template is empty.")
                    : $"Raid with template {Letters[i]}: {Describe(troops)}" + (can ? "" : " (not enough troops at home)");
                row.Send[i].tooltip = sent ? "A raid is on its way there or back.\n" + what : what;
                // Shown as unit icons with their counts (the text above is what marks it as having a tooltip).
                string note = troops == null ? what : !can ? "Not enough troops at home." : null;
                if (sent) note = note == null ? "A raid is on its way there or back." : "A raid is on its way there or back. " + note;
                row.Send[i].userData = new TroopTip
                {
                    Title = troops == null ? $"Template {Letters[i]}" : $"Raid with template {Letters[i]}",
                    Troops = troops,
                    Note = note,
                };
            }
            bool cycling = t != null && t.Cycling;
            SetText(row.Cycle, cycling ? "Stop" : "Cycle");
            row.Cycle.tooltip = cycling ? $"In the cycle ({world.CycleStatus(t).ToLowerInvariant()}): stop it"
                : !string.IsNullOrEmpty(t?.Stopped) ? $"The cycle stopped: {t.Stopped} Press to start it again."
                : $"Raid it again each time the raiders get home, with template {Letters[cycleTemplate]}";
            row.Cycle.EnableInClassList("loot-stopped", !cycling && !string.IsNullOrEmpty(t?.Stopped));
        }

        static string Describe(int[] troops)
        {
            var parts = new List<string>();
            foreach (var type in Units.InDisplayOrder)
                if ((int)type < troops.Length && troops[(int)type] > 0) parts.Add($"{troops[(int)type]:N0} {Units.Get(type).Name}");
            return string.Join(", ", parts);
        }
    }
}
