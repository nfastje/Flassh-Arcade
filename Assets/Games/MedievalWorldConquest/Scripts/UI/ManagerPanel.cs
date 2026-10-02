using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The Account Manager, as in Tribal Wars: every village with its build template (what's next, or why it's
    /// waiting) and its troop targets; and the build templates themselves, the game's own (to follow or copy) and the
    /// player's (to edit step by step). The world carries it all out, even while the game is closed.
    /// </summary>
    public class ManagerPanel
    {
        const string NoTemplate = "None";

        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly UiLinks links;
        readonly Action<string, Action> confirm;
        readonly VisualElement villageRows, templateRows, troopEditor, templateEditor, villagesSection, templatesSection;
        readonly Button villagesTab, templatesTab;
        readonly ScrollView scroll;
        readonly Label troopTitle, templateTitle, createMessage, troopPopulation;
        readonly IntegerField[] troopFields = new IntegerField[Units.Count];
        readonly List<(int share, Button button)> shareButtons = new List<(int, Button)>();
        int troopShare = World.DefaultTroopShare;
        readonly TextField newName;
        readonly DropdownField copyFrom;
        readonly VisualElement stepList;
        readonly List<(Village village, Label status, VisualElement troops)> shown = new List<(Village, Label, VisualElement)>();
        string villagesSignature, templatesSignature;
        float nextRefresh;
        int troopVillageId = -1;
        BuildTemplate editing;
        World lastWorld;

        public ManagerPanel(MedievalWorldConquestGame game, UiLinks links, Action<string, Action> confirm)
        {
            this.game = game;
            this.links = links;
            this.confirm = confirm;
            Root = Element("army", "overview");
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("overview-column");
            Root.Add(scroll);

            scroll.Add(Text("Account Manager", "heading"));
            scroll.Add(Text("Give each village a build template and troop targets, and they're carried out for you: buildings in the template's order " +
                            "as resources allow (a farm or warehouse that's too small comes first), then troops with what's left, researching them at the smithy " +
                            "when needed. It keeps working while the game is closed.", "row-info"));

            // Two tabs, so the templates don't get lost below a long list of villages.
            var tabs = Element("option-row", "manager-tabs");
            villagesTab = ButtonWith("Villages", () => ShowTab(false), "option");
            templatesTab = ButtonWith("Build templates", () => ShowTab(true), "option");
            tabs.Add(villagesTab);
            tabs.Add(templatesTab);
            scroll.Add(tabs);
            villagesSection = Element();
            templatesSection = Element();
            scroll.Add(villagesSection);
            scroll.Add(templatesSection);

            // The troop targets of one village: a little window over the list, by the village's Troops button (so
            // nothing below moves), closed with Save, Cancel or its ✕.
            troopEditor = Element("manager-popup");
            var popupHeader = Element("manager-popup-header");
            troopTitle = Text("", "manager-popup-title");
            popupHeader.Add(troopTitle);
            popupHeader.Add(ButtonWith("✕", () => Show(troopEditor, false), "btn", "btn--small", "manager-popup-close"));
            troopEditor.Add(popupHeader);
            troopEditor.Add(Text("How many of each to have, counting those at home, in training, out on the march, and supporting other villages.", "row-level", "manager-popup-hint"));
            var grid = Element("manager-troop-grid");
            foreach (var type in Units.InDisplayOrder)
            {
                var cell = Element("manager-troop-cell");
                cell.tooltip = Units.Get(type).Name;
                cell.Add(Icons.Element(Icons.Unit(type), 20, "cost-icon"));
                var field = new IntegerField { value = 0 };
                field.AddToClassList("amount-field");
                field.RegisterValueChangedCallback(_ => ShowTroopPopulation());
                troopFields[(int)type] = field;
                cell.Add(field);
                grid.Add(cell);
            }
            troopEditor.Add(grid);
            // What the targets come to in population, against the room the farm has (and could have).
            troopPopulation = Text("", "row-level", "manager-popup-hint", "manager-troop-population");
            troopEditor.Add(troopPopulation);
            // How the village's resources are split while it has both buildings and troops to get.
            var shareRow = Element("option-row", "manager-share-row");
            shareRow.Add(Text("Spend on troops", "row-title", "manager-share-label"));
            foreach (int share in World.TroopShares)
            {
                int s = share;
                var b = ButtonWith($"{s}%", () => ChooseShare(s), "option", "manager-share");
                shareButtons.Add((s, b));
                shareRow.Add(b);
            }
            troopEditor.Add(shareRow);
            troopEditor.Add(Text("The rest goes on the build template. With the template finished, troops get everything.", "row-level", "manager-popup-hint"));
            var troopActions = Element("option-row", "manager-actions");
            foreach (var (name, troops) in World.TroopPresets)
            {
                var preset = troops;
                troopActions.Add(ButtonWith(name, () => FillTroops(preset), "btn", "btn--small"));
            }
            troopActions.Add(ButtonWith("Clear", () => FillTroops(new int[Units.Count]), "btn", "btn--small"));
            troopActions.Add(ButtonWith("Save", SaveTroops, "btn", "btn--small"));
            troopActions.Add(ButtonWith("Cancel", () => Show(troopEditor, false), "btn", "btn--small"));
            troopEditor.Add(troopActions);
            Root.Add(troopEditor);
            Show(troopEditor, false);
            // Kept inside the panel once its size is known (near the bottom, it opens upwards).
            troopEditor.RegisterCallback<GeometryChangedEvent>(_ => KeepInside(troopEditor));
            villageRows = Element();
            villagesSection.Add(villageRows);

            templateRows = Element();
            templatesSection.Add(templateRows);
            var create = Element("send-to-row", "manager-create");
            create.Add(Text("New template", "row-title"));
            newName = new TextField { maxLength = 24 };
            newName.AddToClassList("rename-field");
            create.Add(newName);
            create.Add(Text("copied from", "row-level"));
            copyFrom = new DropdownField(new List<string> { "(empty)" }, 0);
            copyFrom.AddToClassList("manager-dropdown");
            create.Add(copyFrom);
            create.Add(ButtonWith("Create", CreateTemplate, "btn", "btn--small"));
            templatesSection.Add(create);
            createMessage = Text("", "row-reason");
            templatesSection.Add(createMessage);

            // One template's steps: read-only for the game's own, editable for the player's.
            templateEditor = Element("pane-box", "manager-editor");
            templateTitle = Text("", "pane-title");
            templateEditor.Add(templateTitle);
            stepList = Element();
            templateEditor.Add(stepList);
            templatesSection.Add(templateEditor);
            ShowTab(false);
            Show(templateEditor, false);
        }

        void ShowTab(bool templates)
        {
            Show(troopEditor, false);
            Show(villagesSection, !templates);
            Show(templatesSection, templates);
            villagesTab.EnableInClassList("option--selected", !templates);
            templatesTab.EnableInClassList("option--selected", templates);
            scroll.scrollOffset = UnityEngine.Vector2.zero;
        }

        public void Refresh(World world)
        {
            lastWorld = world;
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + 0.5f;
            var human = world.HumanPlayer;
            if (human == null) return;
            var villages = world.HumanVillagesByName();
            var templates = world.AllTemplates();

            string templatesNow = string.Join("|", templates.ConvertAll(t => t.Name + ":" + t.Steps.Count));
            string villagesNow = string.Join(",", villages.ConvertAll(v => v.Id + ":" + v.Name)) + "#" + templatesNow;
            if (villagesNow != villagesSignature)
            {
                villagesSignature = villagesNow;
                BuildVillageRows(world, villages, templates);
            }
            if (templatesNow != templatesSignature)
            {
                templatesSignature = templatesNow;
                BuildTemplateRows(templates);
            }
            foreach (var (v, status, troops) in shown)
            {
                SetText(status, world.ManagerStatus(v));
                ShowTroopTargets(world, v, troops);
            }
        }

        void BuildVillageRows(World world, List<Village> villages, List<BuildTemplate> templates)
        {
            villageRows.Clear();
            shown.Clear();
            var choices = new List<string> { NoTemplate };
            choices.AddRange(templates.ConvertAll(t => t.Name));
            foreach (var v in villages)
            {
                int id = v.Id;
                var row = Element("manager-row");
                var name = Element("manager-name");
                ShowName(name, v);
                row.Add(name);
                var m = world.ManagementOf(id);
                var pick = new DropdownField(choices, Math.Max(0, choices.IndexOf(string.IsNullOrEmpty(m?.Template) ? NoTemplate : m.Template)));
                pick.AddToClassList("manager-dropdown");
                pick.RegisterValueChangedCallback(e => game.SetVillageTemplate(id, e.newValue == NoTemplate ? "" : e.newValue));
                row.Add(pick);
                var details = Element("manager-details");
                var status = Text("", "row-level");
                details.Add(status);
                var troops = Element("manager-troops");
                details.Add(troops);
                row.Add(details);
                Button troopsButton = null;
                troopsButton = ButtonWith("Troops", () => OpenTroops(world, id, troopsButton), "btn", "btn--small", "count-btn");
                row.Add(troopsButton);
                villageRows.Add(row);
                shown.Add((v, status, troops));
            }
        }

        /// <summary>A village's name, as a link to switch to it, with a pencil to rename it, then its coordinates.</summary>
        void ShowName(VisualElement cell, Village v)
        {
            cell.Clear();
            int id = v.Id;
            var link = Link(v.Name, () => links.SwitchTo(id), "manager-village-link");
            link.tooltip = v.Name;
            cell.Add(link);
            var pencil = new Button(() => StartRename(cell, v)) { tooltip = "Rename" };
            pencil.AddToClassList("pencil-btn");
            pencil.Add(Icons.Element(Icons.Pencil, 16));
            cell.Add(pencil);
            cell.Add(Text($"({v.X}|{v.Y})", "row-level", "manager-coords"));
        }

        /// <summary>Swaps the name for a box to type the new one in: Enter or OK renames, ✕ leaves it be.</summary>
        void StartRename(VisualElement cell, Village v)
        {
            cell.Clear();
            var field = new TextField { maxLength = World.MaxVillageNameLength, value = v.Name };
            field.AddToClassList("rename-field");
            field.AddToClassList("manager-rename-field");
            void Done(bool rename)
            {
                if (rename) game.RenameVillage(v.Id, field.value);
                ShowName(cell, v);
            }
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == UnityEngine.KeyCode.Return || e.keyCode == UnityEngine.KeyCode.KeypadEnter) Done(true);
            }, TrickleDown.TrickleDown);
            cell.Add(field);
            cell.Add(ButtonWith("OK", () => Done(true), "btn", "btn--small", "manager-rename-btn"));
            cell.Add(ButtonWith("✕", () => Done(false), "btn", "btn--small", "manager-rename-btn"));
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectAll();
            });
        }

        /// <summary>
        /// A village's troop targets as unit icons with their counts (after the share of spending that goes on
        /// troops), compact enough to fit the row. Rebuilt only when the targets change.
        /// </summary>
        static void ShowTroopTargets(World world, Village v, VisualElement into)
        {
            var m = world.ManagementOf(v.Id);
            string key = m?.TroopTargets == null ? "" : m.TroopShare + ":" + string.Join(",", m.TroopTargets);
            if (into.userData as string == key) return;
            into.userData = key;
            into.Clear();
            bool any = false;
            if (m?.TroopTargets != null)
                foreach (var type in Units.InDisplayOrder)
                {
                    int n = (int)type < m.TroopTargets.Length ? m.TroopTargets[(int)type] : 0;
                    if (n <= 0) continue;
                    if (!any)
                    {
                        var share = Text($"{m.TroopShare}%", "row-level", "manager-troops-share");
                        share.tooltip = $"{m.TroopShare}% of the village's spending goes on troops";
                        into.Add(share);
                        any = true;
                    }
                    var cell = Element("manager-troop-target");
                    cell.tooltip = $"{Units.Get(type).Name}: {n:N0} wanted";
                    cell.Add(Icons.Element(Icons.Unit(type), 16, "manager-troop-icon"));
                    cell.Add(Text($"{n:N0}", "row-level", "manager-troop-count"));
                    into.Add(cell);
                }
            if (!any) into.Add(Text("No troop targets.", "row-level"));
        }

        void OpenTroops(World world, int villageId, VisualElement by)
        {
            var v = world.FindVillage(villageId);
            if (v == null) return;
            troopVillageId = villageId;
            SetText(troopTitle, $"Troop targets: {v.Name}");
            var m = world.ManagementOf(villageId);
            FillTroops(m?.TroopTargets ?? new int[Units.Count]);
            ChooseShare(m?.TroopShare ?? World.DefaultTroopShare);
            // Just below the button, its right edge lined up with the button's.
            var at = Root.WorldToLocal(by.worldBound);
            troopEditor.style.left = at.xMax - PopupWidth;
            troopEditor.style.top = at.yMax + 4;
            popupAnchorTop = at.yMin;
            Show(troopEditor, true);
            troopEditor.BringToFront();
            KeepInside(troopEditor);
        }

        const float PopupWidth = 470f;
        float popupAnchorTop;

        /// <summary>Moves the popup back inside the panel: up above its button if it would run off the bottom.</summary>
        void KeepInside(VisualElement popup)
        {
            float width = Root.layout.width, height = Root.layout.height, h = popup.layout.height;
            if (float.IsNaN(width) || float.IsNaN(h) || h <= 0) return;
            float left = Math.Max(8, Math.Min(popup.style.left.value.value, width - PopupWidth - 8));
            float top = popup.style.top.value.value;
            if (top + h > height - 8) top = Math.Max(8, popupAnchorTop - h - 4);
            if (Math.Abs(left - popup.style.left.value.value) > 0.5f) popup.style.left = left;
            if (Math.Abs(top - popup.style.top.value.value) > 0.5f) popup.style.top = top;
        }

        void FillTroops(int[] troops)
        {
            for (int i = 0; i < Units.Count; i++) troopFields[i]?.SetValueWithoutNotify(i < troops.Length ? troops[i] : 0);
            ShowTroopPopulation();
        }

        /// <summary>
        /// The population the targets need, the room for troops now (what the farm holds less what the buildings use),
        /// and the most there could be: a level 30 farm, with the village's build template finished.
        /// </summary>
        void ShowTroopPopulation()
        {
            var world = lastWorld;
            var v = world?.FindVillage(troopVillageId);
            if (v == null || troopPopulation == null) return;
            long need = 0;
            for (int i = 0; i < Units.Count; i++) need += (long)Math.Max(0, troopFields[i]?.value ?? 0) * Units.Get((UnitType)i).Cost.Population;
            int roomNow = v.PopulationCapacity - v.BuildingPopulation;
            // The buildings once the template is done: each at the higher of its level now (with what's queued) and the template's.
            var levels = new int[Buildings.Count];
            for (int b = 0; b < Buildings.Count; b++) levels[b] = v.Level((BuildingType)b) + v.QueuedCount((BuildingType)b);
            var template = world.FindTemplate(world.ManagementOf(v.Id)?.Template);
            if (template != null)
                foreach (var step in template.Steps)
                    levels[(int)step.Building] = Math.Max(levels[(int)step.Building], Math.Min(step.Level, Buildings.Get(step.Building).MaxLevel));
            int finishedBuildings = 0;
            for (int b = 0; b < Buildings.Count; b++) finishedBuildings += Buildings.PopulationAtLevel((BuildingType)b, levels[b]);
            int maxFarm = Buildings.Get(BuildingType.Farm).MaxLevel;
            int roomMost = Buildings.FarmCapacity(maxFarm) - finishedBuildings;
            SetText(troopPopulation,
                $"These troops need {need:N0} population. Room for troops now: {Math.Max(0, roomNow):N0} (farm level {v.Level(BuildingType.Farm)}). " +
                $"At most: {Math.Max(0, roomMost):N0} (farm level {maxFarm}{(template != null ? $", {template.Name} finished" : "")}).");
            troopPopulation.EnableInClassList("manager-troop-population--over", need > roomMost);
        }

        void ChooseShare(int share)
        {
            troopShare = share;
            foreach (var (s, b) in shareButtons) b.EnableInClassList("option--selected", s == share);
        }

        void SaveTroops()
        {
            var targets = new int[Units.Count];
            for (int i = 0; i < Units.Count; i++) targets[i] = Math.Max(0, troopFields[i]?.value ?? 0);
            game.SetTroopTargets(troopVillageId, targets, troopShare);
            Show(troopEditor, false);
        }

        void BuildTemplateRows(List<BuildTemplate> templates)
        {
            templateRows.Clear();
            var names = new List<string> { "(empty)" };
            foreach (var t in templates)
            {
                names.Add(t.Name);
                var row = Element("manager-row");
                row.Add(Text(t.Name, "row-title", "manager-name"));
                row.Add(Text($"{t.Steps.Count} steps{(t.BuiltIn ? "  ·  the game's own" : "")}", "row-level", "manager-details"));
                var template = t;
                row.Add(ButtonWith(t.BuiltIn ? "View" : "Edit", () => OpenTemplate(template), "btn", "btn--small", "count-btn"));
                if (!t.BuiltIn)
                    row.Add(ButtonWith("Delete", () => confirm($"Delete the template \"{template.Name}\"? Villages that follow it will follow none.", () =>
                    {
                        game.DeleteTemplate(template.Name);
                        if (editing == template) Show(templateEditor, false);
                    }), "btn", "btn--small", "count-btn"));
                templateRows.Add(row);
            }
            copyFrom.choices = names;
            if (copyFrom.index < 0 || copyFrom.index >= names.Count) copyFrom.index = 0;
        }

        void CreateTemplate()
        {
            string from = copyFrom.index > 0 ? copyFrom.value : null;
            string problem = game.CreateTemplate(newName.value, from);
            SetText(createMessage, problem ?? $"Template \"{newName.value.Trim()}\" made: edit its steps below, then choose it for a village.");
            if (problem != null) return;
            var made = lastWorld?.FindTemplate(newName.value.Trim());
            newName.SetValueWithoutNotify("");
            if (made != null) OpenTemplate(made);
        }

        void OpenTemplate(BuildTemplate t)
        {
            editing = t;
            SetText(templateTitle, t.BuiltIn ? $"{t.Name} (the game's own: copy it to change it)" : $"Editing {t.Name}");
            ShowSteps();
            Show(templateEditor, true);
        }

        /// <summary>The steps, as a list: for the player's own templates, each with its building, level, and controls.</summary>
        void ShowSteps()
        {
            stepList.Clear();
            var t = editing;
            if (t == null) return;
            var buildingNames = new List<string>();
            foreach (var d in Buildings.Definitions) buildingNames.Add(d.Name);
            for (int i = 0; i < t.Steps.Count; i++)
            {
                int index = i;
                var step = t.Steps[i];
                var row = Element("manager-step");
                row.Add(Text($"{i + 1}.", "row-level", "manager-step-number"));
                if (t.BuiltIn)
                {
                    row.Add(Text($"{Buildings.Get(step.Building).Name} {step.Level}", "row-info"));
                    stepList.Add(row);
                    continue;
                }
                var building = new DropdownField(buildingNames, (int)step.Building);
                building.AddToClassList("manager-dropdown");
                building.RegisterValueChangedCallback(e => { step.Building = (BuildingType)Math.Max(0, buildingNames.IndexOf(e.newValue)); Edited(); });
                row.Add(building);
                var level = new IntegerField { value = step.Level };
                level.AddToClassList("amount-field");
                level.RegisterValueChangedCallback(e => { step.Level = Math.Max(1, Math.Min(Buildings.Get(step.Building).MaxLevel, e.newValue)); Edited(); });
                row.Add(level);
                row.Add(ButtonWith("↑", () => Move(index, -1), "btn", "btn--small", "count-btn"));
                row.Add(ButtonWith("↓", () => Move(index, 1), "btn", "btn--small", "count-btn"));
                row.Add(ButtonWith("✕", () => { t.Steps.RemoveAt(index); Edited(); ShowSteps(); }, "btn", "btn--small", "count-btn"));
                stepList.Add(row);
            }
            var actions = Element("option-row", "manager-actions");
            if (!t.BuiltIn)
                actions.Add(ButtonWith("Add a step", () =>
                {
                    var last = t.Steps.Count > 0 ? t.Steps[t.Steps.Count - 1] : new BuildStep(BuildingType.Headquarters, 0);
                    t.Steps.Add(new BuildStep(last.Building, Math.Min(Buildings.Get(last.Building).MaxLevel, last.Level + 1)));
                    Edited();
                    ShowSteps();
                }, "btn", "btn--small"));
            actions.Add(ButtonWith("Close", () => Show(templateEditor, false), "btn", "btn--small"));
            stepList.Add(actions);
        }

        void Move(int index, int by)
        {
            var steps = editing.Steps;
            int to = index + by;
            if (to < 0 || to >= steps.Count) return;
            (steps[index], steps[to]) = (steps[to], steps[index]);
            Edited();
            ShowSteps();
        }

        void Edited()
        {
            if (editing != null) game.TemplateEdited(editing.Name);
            templatesSignature = null;
        }
    }
}
