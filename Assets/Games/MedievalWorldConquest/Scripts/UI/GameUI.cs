using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// All of Medieval World Conquest's screens, built with UI Toolkit in code: the start screen (continue / new
    /// world), the in-game HUD with its tabs, the in-game menu, confirmations and toasts. It only displays state
    /// and forwards clicks to <see cref="MedievalWorldConquestGame"/>; game rules live in the simulation.
    /// </summary>
    public class GameUI
    {
        const string ResourcePath = "MedievalWorldConquest/";

        static readonly float[] Speeds = { 1f, 5f, 20f, 100f };

        enum Tab { Village, Map, Army, Reports, Ranking }

        /// <summary>Rival lord density choices: none, few, normal, many.</summary>
        static readonly float[] RivalDensities = { 0f, 0.5f, 1f, 2f };
        static readonly string[] RivalDensityNames = { "None", "Few", "Normal", "Many" };

        readonly MedievalWorldConquestGame game;
        readonly VisualElement root;
        readonly Camera cam;
        VillagePanel villagePanel;
        ArmyPanel armyPanel;
        ReportsPanel reportsPanel;
        RankingPanel rankingPanel;
        SendDialog sendDialog;
        Label incomingWarning, protectionTag;
        public MapPanel Map { get; private set; }
        Tab currentTab;
        readonly Label[] resourceValues = new Label[3], resourceRates = new Label[3];
        Label storageValue, populationValue;

        // Start screen
        VisualElement startScreen, continueSection, newGameSection;
        Label continueSummary, loadError;
        readonly List<Button> speedButtons = new List<Button>();
        readonly List<Button> modeButtons = new List<Button>();
        readonly List<Button> rivalButtons = new List<Button>();
        readonly List<Button> skillButtons = new List<Button>();
        float chosenSpeed = 5f;
        TimeMode chosenMode = TimeMode.RealTime;
        float chosenDensity = 1f;
        TextField nameField;
        AiSkill chosenSkill = AiSkill.Normal;

        // In game
        VisualElement hud, tabContent, menu, confirm;
        Label playerName, villageName, villageInfo, clock, speedTag, toastLabel, confirmText;
        VisualElement toast;
        Action confirmAction;
        readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
        float toastTime;

        public bool MenuOpen => menu.style.display == DisplayStyle.Flex;
        public bool DialogOpen => confirm.style.display == DisplayStyle.Flex || sendDialog.IsOpen;

        /// <summary>Whether the Village tab is showing (the building list covers the right of the screen).</summary>
        public bool VillageTabActive => hud.style.display == DisplayStyle.Flex && currentTab == Tab.Village;

        /// <summary>Whether the Map tab is showing (the world map replaces the village view).</summary>
        public bool MapTabActive => hud.style.display == DisplayStyle.Flex && currentTab == Tab.Map;

        public BuildingType? SelectedBuilding => villagePanel.Selected;

        public void SelectBuilding(BuildingType? type) => villagePanel.Select(type);

        /// <summary>Whether a screen position (as from the Input System, origin bottom-left) is over clickable UI.</summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            var panel = root.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return panel.Pick(p) != null; // non-clickable containers are set to ignore picking
        }

        public static GameUI Create(MedievalWorldConquestGame game, Camera cam)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>(ResourcePath + "DefaultTheme");
            if (settings.themeStyleSheet == null) Debug.LogError("Medieval World Conquest: UI theme not found; text may not render.");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1280, 720);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f; // scale with the screen height

            // The document builds its visual tree when enabled, so give it its settings while inactive.
            var go = new GameObject("UI");
            go.SetActive(false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            go.SetActive(true);

            return new GameUI(game, document.rootVisualElement, cam);
        }

        GameUI(MedievalWorldConquestGame game, VisualElement root, Camera cam)
        {
            this.game = game;
            this.root = root;
            this.cam = cam;
            var styles = Resources.Load<StyleSheet>(ResourcePath + "Styles");
            if (styles != null) root.styleSheets.Add(styles);
            root.pickingMode = PickingMode.Ignore;

            BuildStartScreen();
            BuildHud();
            sendDialog = new SendDialog(game);
            root.Add(sendDialog.Root);
            BuildMenu();
            BuildConfirm();
            BuildToast();
        }

        // ---------------------------------------------------------------- helpers

        static string SpeedText(float speed) => $"{speed:0.#}×";

        static string ModeText(TimeMode mode) => mode == TimeMode.RealTime ? "Real time" : "Paused while closed";

        // ---------------------------------------------------------------- start screen

        void BuildStartScreen()
        {
            startScreen = Element("screen", "centered", "dim");
            root.Add(startScreen);

            var panel = Element("panel");
            startScreen.Add(panel);
            panel.Add(Text("MEDIEVAL WORLD CONQUEST", "title"));
            panel.Add(Text("Build a village, raise an army and conquer the realm.", "subtitle"));

            loadError = Text("", "error-text");
            panel.Add(loadError);

            continueSection = Element();
            continueSection.style.alignItems = Align.Center;
            continueSummary = Text("", "body-text");
            continueSection.Add(continueSummary);
            continueSection.Add(ButtonWith("Continue", () => game.ContinueWorld(), "btn"));
            continueSection.Add(Element("divider"));
            panel.Add(continueSection);

            newGameSection = Element();
            newGameSection.style.alignItems = Align.Center;
            newGameSection.Add(Text("New world", "heading"));

            // What the player is called: shown on their villages and in the rankings.
            var nameRow = Element("option-row", "name-row");
            nameRow.Add(Text("Your name", "body-text", "option-label"));
            nameField = new TextField { maxLength = 24, value = World.DefaultPlayerName };
            nameField.AddToClassList("name-field");
            nameRow.Add(nameField);
            newGameSection.Add(nameRow);

            newGameSection.Add(Text("World speed", "body-text"));
            var speedRow = Element("option-row");
            foreach (float speed in Speeds)
            {
                var b = ButtonWith(SpeedText(speed), () => ChooseSpeed(speed), "option");
                b.userData = speed;
                speedButtons.Add(b);
                speedRow.Add(b);
            }
            newGameSection.Add(speedRow);

            newGameSection.Add(Text("When the game is closed", "body-text"));
            var modeRow = Element("option-row");
            modeButtons.Add(ButtonWith("Real time\nThe world keeps going", () => ChooseMode(TimeMode.RealTime), "option", "option-wide"));
            modeButtons.Add(ButtonWith("Paused\nTime only passes while playing", () => ChooseMode(TimeMode.PausedWhenClosed), "option", "option-wide"));
            modeButtons[0].userData = TimeMode.RealTime;
            modeButtons[1].userData = TimeMode.PausedWhenClosed;
            foreach (var b in modeButtons) modeRow.Add(b);
            newGameSection.Add(modeRow);

            newGameSection.Add(Text("Rival lords: they keep arriving as the world grows", "body-text"));
            var rivalRow = Element("option-row");
            for (int i = 0; i < RivalDensities.Length; i++)
            {
                float density = RivalDensities[i];
                var b = ButtonWith(RivalDensityNames[i], () => ChooseRivals(density), "option");
                b.userData = density;
                rivalButtons.Add(b);
                rivalRow.Add(b);
            }
            // Their skill, in the same row to keep the panel short.
            rivalRow.Add(Text("Skill", "body-text", "option-label"));
            foreach (AiSkill skill in Enum.GetValues(typeof(AiSkill)))
            {
                var b = ButtonWith(skill.ToString(), () => ChooseSkill(skill), "option");
                b.userData = skill;
                skillButtons.Add(b);
                rivalRow.Add(b);
            }
            newGameSection.Add(rivalRow);

            var buttons = Element("option-row");
            buttons.style.marginTop = 12;
            buttons.Add(ButtonWith("Start New World", OnStartNewWorld, "btn"));
            buttons.Add(ButtonWith("Main Menu", () => game.LeaveToArcade(), "btn"));
            newGameSection.Add(buttons);
            panel.Add(newGameSection);

            ChooseSpeed(chosenSpeed);
            ChooseMode(chosenMode);
            ChooseRivals(chosenDensity);
            ChooseSkill(chosenSkill);
        }

        void ChooseRivals(float density)
        {
            chosenDensity = density;
            foreach (var b in rivalButtons) b.EnableInClassList("option--selected", (float)b.userData == density);
            foreach (var b in skillButtons) b.SetEnabled(density > 0);
        }

        void ChooseSkill(AiSkill skill)
        {
            chosenSkill = skill;
            foreach (var b in skillButtons) b.EnableInClassList("option--selected", (AiSkill)b.userData == skill);
        }

        void ChooseSpeed(float speed)
        {
            chosenSpeed = speed;
            foreach (var b in speedButtons) b.EnableInClassList("option--selected", (float)b.userData == speed);
        }

        void ChooseMode(TimeMode mode)
        {
            chosenMode = mode;
            foreach (var b in modeButtons) b.EnableInClassList("option--selected", (TimeMode)b.userData == mode);
        }

        void OnStartNewWorld()
        {
            var settings = new WorldSettings
            {
                Speed = chosenSpeed,
                TimeMode = chosenMode,
                Seed = Environment.TickCount,
                RivalDensity = chosenDensity,
                RivalSkill = chosenSkill,
                PlayerName = nameField.value,
            };
            if (game.HasSave)
                AskToConfirm("Starting a new world will replace your saved one. Continue?", () => game.StartNewWorld(settings));
            else
                game.StartNewWorld(settings);
        }

        /// <param name="saved">The saved world to offer to continue, or null if there isn't one.</param>
        /// <param name="error">Why a save couldn't be loaded, if one exists but is unreadable.</param>
        public void ShowStart(World saved, string error)
        {
            Show(startScreen, true);
            Show(hud, false);
            Show(menu, false);

            Show(continueSection, saved != null);
            if (saved != null)
            {
                var v = saved.PlayerVillage;
                int lords = saved.Players.FindAll(p => !p.IsHuman).Count;
                continueSummary.text = $"{v?.Name ?? "Your village"}  ·  {World.FormatClock(saved.Now)}\n" +
                                       $"{SpeedText(saved.Settings.Speed)} speed  ·  {ModeText(saved.Settings.TimeMode)}  ·  " +
                                       (saved.Settings.RivalDensity <= 0 && lords == 0 ? "no rivals" : $"{lords} rival lords so far ({saved.Settings.RivalSkill})");
            }
            loadError.text = error ?? "";
            Show(loadError, !string.IsNullOrEmpty(error));
        }

        // ---------------------------------------------------------------- HUD

        void BuildHud()
        {
            hud = Element("screen");
            hud.pickingMode = PickingMode.Ignore;
            root.Add(hud);

            var top = Element("top-bar");
            // Who, which village, then where and how big.
            // Stacked to save room in the bar: the player's name small above the village's line.
            var title = Element("top-title");
            playerName = Text("", "top-player");
            var line = Element("top-village-line");
            villageName = Text("", "village-name");
            villageInfo = Text("", "top-info");
            line.Add(villageName);
            line.Add(villageInfo);
            title.Add(playerName);
            title.Add(line);
            top.Add(title);

            string[] names = { "Wood", "Clay", "Iron" };
            for (int i = 0; i < names.Length; i++)
            {
                var chip = Element("resource");
                chip.Add(Text(names[i], "resource-name"));
                resourceValues[i] = Text("", "resource-value");
                chip.Add(resourceValues[i]);
                resourceRates[i] = Text("", "resource-rate");
                chip.Add(resourceRates[i]);
                top.Add(chip);
            }
            var storage = Element("resource");
            storage.Add(Text("Storage", "resource-name"));
            storageValue = Text("", "resource-value");
            storage.Add(storageValue);
            top.Add(storage);
            var population = Element("resource");
            population.Add(Text("Population", "resource-name"));
            populationValue = Text("", "resource-value");
            population.Add(populationValue);
            top.Add(population);
            incomingWarning = Text("", "incoming-warning");
            top.Add(incomingWarning);
            top.Add(Element("spacer"));
            clock = Text("", "clock");
            speedTag = Text("", "speed-tag");
            top.Add(clock);
            top.Add(speedTag);
            top.Add(ButtonWith("Menu", ToggleMenu, "btn", "btn--small"));
            hud.Add(top);

            tabContent = Element("tab-content");
            tabContent.pickingMode = PickingMode.Ignore;
            hud.Add(tabContent);

            villagePanel = new VillagePanel(game, cam);
            hud.Add(villagePanel.Root);
            armyPanel = new ArmyPanel(game);
            hud.Add(armyPanel.Root);
            Map = new MapPanel(game, targetId => sendDialog.Open(targetId));
            hud.Add(Map.Root);
            reportsPanel = new ReportsPanel(game);
            hud.Add(reportsPanel.Root);
            rankingPanel = new RankingPanel();
            hud.Add(rankingPanel.Root);

            // Attacks on their way, along the bottom of the village view.
            incomingPanel = Element("incoming-panel");
            incomingPanel.style.right = VillagePanel.ListWidth + 12f;
            incomingPanel.Add(Text("Incoming attacks", "row-title", "incoming-heading"));
            incomingList = new ScrollView(ScrollViewMode.Vertical);
            incomingList.AddToClassList("incoming-list");
            incomingPanel.Add(incomingList);
            hud.Add(incomingPanel);
            Show(incomingPanel, false);

            var tabs = Element("tab-bar");
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                var b = ButtonWith(tab.ToString(), () => SelectTab(tab), "tab");
                tabButtons[tab] = b;
                tabs.Add(b);
            }
            // Beginner protection's countdown sits in the tab bar's corner (the top bar is full).
            protectionTag = Text("", "protection-tag");
            tabs.Add(protectionTag);
            hud.Add(tabs);
            SelectTab(Tab.Village);
        }

        void SelectTab(Tab tab)
        {
            currentTab = tab;
            foreach (var pair in tabButtons) pair.Value.EnableInClassList("tab--selected", pair.Key == tab);
            tabContent.Clear();
            Show(villagePanel.Root, tab == Tab.Village);
            Show(armyPanel.Root, tab == Tab.Army);
            Show(Map.Root, tab == Tab.Map);
            Show(reportsPanel.Root, tab == Tab.Reports);
            Show(rankingPanel.Root, tab == Tab.Ranking);
        }

        public void ShowGame(World world)
        {
            Show(startScreen, false);
            Show(hud, true);
            Show(menu, false);
            SelectTab(Tab.Village);
            Refresh(world);
        }

        /// <summary>Updates the HUD from the current world state. Cheap enough to call every frame.</summary>
        public void Refresh(World world)
        {
            var v = world.PlayerVillage;
            if (v == null) return;
            SetText(playerName, world.HumanPlayer?.Name ?? "");
            SetText(villageName, v.Name);
            SetText(villageInfo, $"({v.X}|{v.Y}) · {v.Points:N0} pts");
            SetText(clock, World.FormatClock(world.Now));
            SetText(speedTag, $"{SpeedText(world.Settings.Speed)} speed");

            int capacity = v.StorageCapacity;
            for (int i = 0; i < resourceValues.Length; i++)
            {
                var r = (ResourceType)i;
                double stock = v.Stock(r);
                SetText(resourceValues[i], $"{Math.Floor(stock):N0}");
                // Rates are per real hour, like every duration in the UI.
                SetText(resourceRates[i], $"+{v.ProductionPerHour(r) * world.Settings.Speed:N0}/h");
                resourceValues[i].EnableInClassList("resource-value--full", stock >= capacity);
            }
            SetText(storageValue, $"{capacity:N0}");
            int used = v.PopulationUsed, cap = v.PopulationCapacity;
            SetText(populationValue, $"{used:N0}/{cap:N0}");
            populationValue.EnableInClassList("resource-value--full", used >= cap);

            int unread = world.UnreadReports;
            SetText(tabButtons[Tab.Reports], unread > 0 ? $"Reports ({unread})" : "Reports");

            // The banner up top counts the attacks heading for the player; the village tab lists them, soonest first.
            var incoming = world.HumanPlayer != null ? world.IncomingAttacks(world.HumanPlayer.Id) : new List<Command>();
            Show(incomingWarning, incoming.Count > 0);
            if (incoming.Count > 0) SetText(incomingWarning, $"Incoming attacks: {incoming.Count}");
            Show(incomingPanel, incoming.Count > 0 && VillageTabActive);

            // While the player is still under beginner protection, say for how long.
            var human = world.HumanPlayer;
            bool protectedNow = human != null && world.IsProtected(human.Id);
            Show(protectionTag, protectedNow && world.Players.Count > 1);
            if (protectedNow) SetText(protectionTag, $"Protected: {Real(world, human.ProtectedUntil - world.Now)}");
            if (incoming.Count > 0 && VillageTabActive) RefreshIncoming(world, incoming);

            if (VillageTabActive) villagePanel.Refresh(world, v);
            else if (currentTab == Tab.Army) armyPanel.Refresh(world, v);
            else if (currentTab == Tab.Map) Map.Refresh(world);
            else if (currentTab == Tab.Reports) reportsPanel.Refresh(world);
            else if (currentTab == Tab.Ranking) rankingPanel.Refresh(world);
            sendDialog.Refresh(world);
        }

        VisualElement incomingPanel;
        ScrollView incomingList;
        string incomingSignature;

        /// <summary>One line per incoming attack, soonest first; rebuilt when attacks come or go, countdowns updated in place.</summary>
        void RefreshIncoming(World world, List<Command> incoming)
        {
            string signature = string.Join(",", incoming.ConvertAll(c => c.Id.ToString()));
            if (signature != incomingSignature)
            {
                incomingSignature = signature;
                incomingList.Clear();
                foreach (var c in incoming)
                {
                    var line = Element("row-header", "incoming-line");
                    line.Add(Text("", "row-info", "incoming-from"));
                    line.Add(Text("", "row-title", "incoming-time"));
                    incomingList.Add(line);
                }
            }

            for (int i = 0; i < incoming.Count && i < incomingList.childCount; i++)
            {
                var c = incoming[i];
                var line = incomingList[i];
                var origin = world.FindVillage(c.FromVillageId);
                var target = world.FindVillage(c.ToVillageId);
                string lord = origin != null ? world.OwnerName(origin) : "?";
                SetText(line.Q<Label>(className: "incoming-from"),
                    $"{lord}, from {origin?.Name} ({origin?.X}|{origin?.Y}) to {target?.Name}  ·  arrives {World.FormatClock(c.ArriveTime)}");
                SetText(line.Q<Label>(className: "incoming-time"), Real(world, Math.Max(0, c.ArriveTime - world.Now)));
            }
        }

        // ---------------------------------------------------------------- menu, confirm, toast

        void BuildMenu()
        {
            menu = Element("screen", "centered", "dim");
            var panel = Element("panel");
            panel.Add(Text("Menu", "title"));
            panel.Add(ButtonWith("Resume", ToggleMenu, "btn"));
            panel.Add(ButtonWith("Save Game", () =>
            {
                game.SaveWorld();
                ShowToast("Game saved.");
            }, "btn"));
            panel.Add(ButtonWith("Main Menu", () => game.LeaveToArcade(), "btn"));
            menu.Add(panel);
            root.Add(menu);
            Show(menu, false);
        }

        public void ToggleMenu() => Show(menu, !MenuOpen);

        void BuildConfirm()
        {
            confirm = Element("screen", "centered", "dim");
            var panel = Element("panel");
            confirmText = Text("", "body-text");
            panel.Add(confirmText);
            var row = Element("option-row");
            row.style.marginTop = 10;
            row.Add(ButtonWith("Yes", () =>
            {
                Show(confirm, false);
                confirmAction?.Invoke();
            }, "btn"));
            row.Add(ButtonWith("No", CloseDialog, "btn"));
            panel.Add(row);
            confirm.Add(panel);
            root.Add(confirm);
            Show(confirm, false);
        }

        void AskToConfirm(string question, Action onYes)
        {
            confirmText.text = question;
            confirmAction = onYes;
            Show(confirm, true);
        }

        public void CloseDialog()
        {
            Show(confirm, false);
            sendDialog.Close();
        }

        void BuildToast()
        {
            toast = Element("toast");
            toast.pickingMode = PickingMode.Ignore;
            toastLabel = Text("", "toast-label");
            toast.Add(toastLabel);
            root.Add(toast);
            Show(toast, false);
        }

        public void ShowToast(string text, float seconds = 3f)
        {
            toastLabel.text = text;
            toastTime = seconds;
            toast.style.opacity = 1f;
            Show(toast, true);
        }

        /// <summary>Per-frame UI animation (fading the toast).</summary>
        public void Tick(float dt)
        {
            if (toastTime <= 0f) return;
            toastTime -= dt;
            toast.style.opacity = Mathf.Clamp01(toastTime / 0.5f);
            if (toastTime <= 0f) Show(toast, false);
        }
    }
}
