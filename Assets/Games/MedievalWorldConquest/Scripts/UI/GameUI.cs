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

        /// <summary>What fills the screen below the top bar.</summary>
        enum View { Village, Map, Reports, Ranking, Overview }

        /// <summary>Rival lord density choices: none, few, normal, many.</summary>
        static readonly float[] RivalDensities = { 0f, 0.5f, 1f, 2f };
        static readonly string[] RivalDensityNames = { "None", "Few", "Normal", "Many" };

        readonly MedievalWorldConquestGame game;
        readonly VisualElement root;
        readonly Camera cam;
        VillagePanel villagePanel;
        BuildingWindow buildingWindow;
        ReportsPanel reportsPanel;
        RankingPanel rankingPanel;
        OverviewPanel overviewPanel;
        SendDialog sendDialog;
        Label incomingWarning, unreadBadge;
        Button previousVillage, nextVillage;
        readonly Dictionary<View, Button> viewButtons = new Dictionary<View, Button>();
        public MapPanel Map { get; private set; }
        View currentView;
        World lastWorld;
        readonly Label[] resourceValues = new Label[3];
        readonly VisualElement[] resourceChips = new VisualElement[3];
        Label storageValue, populationValue;

        // Start screen
        VisualElement startScreen, continueSection, newGameSection;
        Label continueSummary, loadError;
        readonly List<Button> speedButtons = new List<Button>();
        readonly List<Button> modeButtons = new List<Button>();
        readonly List<Button> rivalButtons = new List<Button>();
        readonly List<Button> skillButtons = new List<Button>();
        readonly List<Button> nobleButtons = new List<Button>();
        bool chosenCoins;
        float chosenSpeed = 5f;
        TimeMode chosenMode = TimeMode.RealTime;
        float chosenDensity = 1f;
        TextField nameField;
        AiSkill chosenSkill = AiSkill.Normal;

        // In game
        VisualElement hud, menu, confirm;
        Button playerName, villageName;
        Label villageInfo, clock, speedTag, toastLabel, confirmText;
        VisualElement toast;
        Action confirmAction;
        float toastTime;

        public bool MenuOpen => menu.style.display == DisplayStyle.Flex;
        public bool DialogOpen => confirm.style.display == DisplayStyle.Flex || sendDialog.IsOpen || EndScreenOpen || buildingWindow.IsOpen;

        /// <summary>Whether the victory or defeat screen is showing.</summary>
        public bool EndScreenOpen => endScreen != null && endScreen.style.display == DisplayStyle.Flex;

        /// <summary>Whether the village view is showing (with its pane on the right).</summary>
        public bool VillageTabActive => hud.style.display == DisplayStyle.Flex && currentView == View.Village;

        /// <summary>Whether the map is showing (it replaces the village view).</summary>
        public bool MapTabActive => hud.style.display == DisplayStyle.Flex && currentView == View.Map;

        /// <summary>The building whose screen is open, to highlight it in the village.</summary>
        public BuildingType? SelectedBuilding => buildingWindow.Building;

        /// <summary>Closes any building screen (e.g. when switching villages).</summary>
        public void SelectBuilding(BuildingType? type)
        {
            if (type.HasValue) OpenBuilding(type.Value);
            else buildingWindow.Close();
        }

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
            BuildEndScreen();
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

            // How noblemen are paid for: a flat price, or Tribal Wars' gold coins (dearer with every conquest).
            newGameSection.Add(Text("Noblemen", "body-text"));
            var nobleRow = Element("option-row");
            nobleButtons.Add(ButtonWith("Flat price\nEvery nobleman costs the same", () => ChooseCoins(false), "option", "option-wide"));
            nobleButtons.Add(ButtonWith("Gold coins\nEach conquest makes the next dearer", () => ChooseCoins(true), "option", "option-wide"));
            nobleButtons[0].userData = false;
            nobleButtons[1].userData = true;
            foreach (var b in nobleButtons) nobleRow.Add(b);
            newGameSection.Add(nobleRow);

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
            ChooseCoins(chosenCoins);
        }

        void ChooseCoins(bool coins)
        {
            chosenCoins = coins;
            foreach (var b in nobleButtons) b.EnableInClassList("option--selected", (bool)b.userData == coins);
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
                GoldCoins = chosenCoins,
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
                                       (saved.Settings.RivalDensity <= 0 && lords == 0 ? "no rivals" : $"{lords} rival lords so far ({saved.Settings.RivalSkill})") +
                                       (saved.Settings.GoldCoins ? "  ·  gold coins" : "");
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

            // The top bar, as in Tribal Wars: the player and village names (the player's leads to their profile,
            // the village's back to the village), the map, the resources, then reports and ranking, the clock and
            // the menu. Everything but the names keeps its size; long names are cut short.
            var top = Element("top-bar");
            previousVillage = ButtonWith("<", () => game.CycleVillage(-1), "btn", "btn--small", "village-arrow");
            top.Add(previousVillage);
            var title = Element("top-title");
            playerName = ButtonWith("", () => { var human = lastWorld?.HumanPlayer; if (human != null) OpenPlayerInfo(human.Id); }, "top-link", "top-player");
            playerName.tooltip = "Your profile";
            var line = Element("top-village-line");
            villageName = ButtonWith("", () => ShowView(View.Village), "top-link", "village-name");
            villageName.tooltip = "Back to the village";
            villageInfo = Text("", "top-info");
            line.Add(villageName);
            line.Add(villageInfo);
            title.Add(playerName);
            title.Add(line);
            top.Add(title);
            nextVillage = ButtonWith(">", () => game.CycleVillage(1), "btn", "btn--small", "village-arrow");
            top.Add(nextVillage);
            viewButtons[View.Map] = IconButton(top, Icons.Map, "Map", () => ShowView(currentView == View.Map ? View.Village : View.Map));
            viewButtons[View.Overview] = IconButton(top, null, "Villages", () => ShowView(currentView == View.Overview ? View.Village : View.Overview));
            viewButtons[View.Overview].tooltip = "All your villages at a glance";

            top.Add(Element("spacer"));
            for (int i = 0; i < 3; i++)
            {
                // Production per hour is in the tooltip (and the village pane), as in Tribal Wars, to keep the bar short.
                var chip = Element("resource");
                chip.Add(Icons.Element(Icons.Resource((ResourceType)i), 20, "resource-icon"));
                resourceValues[i] = Text("", "resource-value");
                chip.Add(resourceValues[i]);
                resourceChips[i] = chip;
                top.Add(chip);
            }
            var storage = Element("resource");
            storage.tooltip = "Warehouse capacity";
            storage.Add(Icons.Element(Icons.Storage, 20, "resource-icon"));
            storageValue = Text("", "resource-value");
            storage.Add(storageValue);
            top.Add(storage);
            var population = Element("resource");
            population.tooltip = "Population (used / farm limit)";
            population.Add(Icons.Element(Icons.Population, 20, "resource-icon"));
            populationValue = Text("", "resource-value");
            population.Add(populationValue);
            top.Add(population);
            incomingWarning = Text("", "incoming-warning");
            top.Add(incomingWarning);
            top.Add(Element("spacer"));

            // Reports: a scroll with a little red count of the unread ones.
            viewButtons[View.Reports] = IconButton(top, Icons.Reports, "", () => ShowView(currentView == View.Reports ? View.Village : View.Reports));
            viewButtons[View.Reports].tooltip = "Reports";
            unreadBadge = Text("", "unread-badge");
            unreadBadge.pickingMode = PickingMode.Ignore;
            viewButtons[View.Reports].Add(unreadBadge);
            viewButtons[View.Ranking] = IconButton(top, Icons.Ranking, "", () => ShowView(currentView == View.Ranking ? View.Village : View.Ranking));
            viewButtons[View.Ranking].tooltip = "Ranking";

            clock = Text("", "clock");
            speedTag = Text("", "speed-tag");
            top.Add(clock);
            top.Add(speedTag);
            top.Add(ButtonWith("Menu", ToggleMenu, "btn", "btn--small"));
            hud.Add(top);

            // Where player and village names lead when clicked.
            links = new UiLinks
            {
                OpenVillage = OpenVillageInfo,
                OpenPlayer = OpenPlayerInfo,
                OpenReport = id =>
                {
                    ShowView(View.Reports);
                    reportsPanel.Open(id);
                },
                ShowOnMap = id =>
                {
                    ShowView(View.Map);
                    game.ShowOnMap(id);
                },
                SendTroops = id => sendDialog.Open(id),
                SwitchTo = id => game.SelectVillage(id),
                ShowInRanking = id =>
                {
                    CloseInfo();
                    ShowView(View.Ranking);
                    rankingPanel.ShowPlayer(id);
                },
                SendResources = id =>
                {
                    var target = lastWorld?.FindVillage(id);
                    if (target == null) return;
                    CloseInfo();
                    ShowView(View.Village);
                    buildingWindow.OpenMarketTo(target.X, target.Y);
                },
            };

            villagePanel = new VillagePanel(game, cam);
            hud.Add(villagePanel.Root);
            Map = new MapPanel(game);
            hud.Add(Map.Root);
            reportsPanel = new ReportsPanel(game, links);
            hud.Add(reportsPanel.Root);
            rankingPanel = new RankingPanel(links);
            hud.Add(rankingPanel.Root);
            overviewPanel = new OverviewPanel(id =>
            {
                game.SelectVillage(id);
                ShowView(View.Village);
            });
            hud.Add(overviewPanel.Root);

            // Troop movements (attacks coming in, and the player's own going out and coming home), along the bottom
            // of the village view.
            movementsPanel = Element("incoming-panel");
            movementsPanel.style.right = VillagePanel.PaneWidth + 12f;
            movementsHeading = Text("Troop movements", "row-title", "incoming-heading");
            movementsPanel.Add(movementsHeading);
            movementsList = new ScrollView(ScrollViewMode.Vertical);
            movementsList.AddToClassList("incoming-list");
            movementsPanel.Add(movementsList);
            hud.Add(movementsPanel);
            Show(movementsPanel, false);

            buildingWindow = new BuildingWindow(game, SendToField, links);
            hud.Add(buildingWindow.Root);
            villageWindow = new VillageWindow(links);
            hud.Add(villageWindow.Root);
            playerWindow = new PlayerWindow(links);
            hud.Add(playerWindow.Root);
            ShowView(View.Village);
        }

        UiLinks links;
        VillageWindow villageWindow;
        PlayerWindow playerWindow;

        /// <summary>Opens the window about a village (from the map, or a village's name anywhere).</summary>
        public void OpenVillageInfo(int villageId)
        {
            playerWindow.Close();
            villageWindow.Open(villageId);
        }

        /// <summary>Opens a player's profile (from their name anywhere).</summary>
        public void OpenPlayerInfo(int playerId)
        {
            villageWindow.Close();
            playerWindow.Open(playerId);
        }

        /// <summary>Whether the village or player window is showing.</summary>
        public bool InfoOpen => villageWindow.IsOpen || playerWindow.IsOpen;

        /// <summary>Closes the village and player windows.</summary>
        public void CloseInfo()
        {
            villageWindow.Close();
            playerWindow.Close();
        }

        /// <summary>A top-bar button with an icon (and optional label).</summary>
        static Button IconButton(VisualElement bar, UnityEngine.Texture2D icon, string label, Action onClick)
        {
            var b = new Button(onClick);
            b.AddToClassList("icon-button");
            if (icon != null) b.Add(Icons.Element(icon, 22));
            if (!string.IsNullOrEmpty(label)) b.Add(Text(label, "icon-button-label"));
            bar.Add(b);
            return b;
        }

        /// <summary>Opens the send-troops dialog for the village on a map field, from the rally point. Returns whether there is one.</summary>
        bool SendToField(int x, int y)
        {
            var target = lastWorld?.VillageAt(x, y);
            if (target == null) return false;
            sendDialog.Open(target.Id);
            return true;
        }

        void ShowView(View view)
        {
            currentView = view;
            foreach (var pair in viewButtons) pair.Value.EnableInClassList("icon-button--selected", pair.Key == view);
            Show(villagePanel.Root, view == View.Village);
            Show(Map.Root, view == View.Map);
            Show(reportsPanel.Root, view == View.Reports);
            Show(rankingPanel.Root, view == View.Ranking);
            Show(overviewPanel.Root, view == View.Overview);
            if (view != View.Village) buildingWindow.Close();
        }

        /// <summary>Opens a building's own screen in the village view.</summary>
        public void OpenBuilding(BuildingType type)
        {
            ShowView(View.Village);
            buildingWindow.Open(type);
        }

        public void ShowGame(World world)
        {
            Show(startScreen, false);
            Show(hud, true);
            Show(menu, false);
            ShowView(View.Village);
            Refresh(world);
        }

        /// <summary>Updates the HUD from the current world state. Cheap enough to call every frame.</summary>
        public void Refresh(World world)
        {
            lastWorld = world;
            var v = world.PlayerVillage;
            if (v == null) return;
            var own = world.HumanVillages();
            int index = own.IndexOf(v);
            SetText(playerName, world.HumanPlayer?.Name ?? "");
            SetText(villageName, v.Name);
            SetText(villageInfo, v.Loyalty < World.MaxLoyalty
                ? $"({v.X}|{v.Y}) {World.ContinentName(v.X, v.Y)} · loyalty {Math.Floor(v.Loyalty):0}"
                : $"({v.X}|{v.Y}) {World.ContinentName(v.X, v.Y)}");
            villageInfo.tooltip = own.Count > 1 ? $"{v.Points:N0} points · village {index + 1} of {own.Count}" : $"{v.Points:N0} points";
            Show(previousVillage, own.Count > 1);
            Show(nextVillage, own.Count > 1);
            SetText(clock, World.FormatClock(world.Now));
            SetText(speedTag, SpeedText(world.Settings.Speed));

            int capacity = v.StorageCapacity;
            for (int i = 0; i < resourceValues.Length; i++)
            {
                var r = (ResourceType)i;
                double stock = v.Stock(r);
                SetText(resourceValues[i], $"{Math.Floor(stock):N0}");
                // Rates are per real hour, like every duration in the UI.
                resourceChips[i].tooltip = $"{r}: +{v.ProductionPerHour(r) * world.Settings.Speed:N0} per hour";
                resourceValues[i].EnableInClassList("resource-value--full", stock >= capacity);
            }
            SetText(storageValue, $"{capacity:N0}");
            int used = v.PopulationUsed, cap = v.PopulationCapacity;
            SetText(populationValue, $"{used:N0}/{cap:N0}");
            populationValue.EnableInClassList("resource-value--full", used >= cap);

            int unread = world.UnreadReports;
            Show(unreadBadge, unread > 0);
            SetText(unreadBadge, unread > 99 ? "99+" : unread.ToString());

            // The banner up top counts the attacks heading for the player; the village view lists them, soonest first.
            var incoming = world.HumanPlayer != null ? world.IncomingAttacks(world.HumanPlayer.Id) : new List<Command>();
            Show(incomingWarning, incoming.Count > 0);
            if (incoming.Count > 0) SetText(incomingWarning, $"{incoming.Count} incoming");
            var moving = TrackedMovements(world);
            Show(movementsPanel, moving.Count > 0 && VillageTabActive && !buildingWindow.IsOpen);
            if (moving.Count > 0 && VillageTabActive) RefreshMovements(world, moving, incoming.Count);
            villageWindow.Refresh(world);
            playerWindow.Refresh(world);

            if (VillageTabActive)
            {
                villagePanel.Refresh(world, v);
                buildingWindow.Refresh(world, v);
            }
            else if (currentView == View.Map) Map.Refresh(world);
            else if (currentView == View.Reports) reportsPanel.Refresh(world);
            else if (currentView == View.Ranking) rankingPanel.Refresh(world);
            else if (currentView == View.Overview) overviewPanel.Refresh(world);
            sendDialog.Refresh(world);
        }

        VisualElement movementsPanel;
        Label movementsHeading;
        ScrollView movementsList;
        readonly List<MovementRow> movementRows = new List<MovementRow>();

        /// <summary>
        /// What the village view's movement list tracks, soonest first: attacks coming in, and the player's own
        /// attacks going out and troops coming home (support and the rest are at the rally point).
        /// </summary>
        static List<Command> TrackedMovements(World world)
        {
            var human = world.HumanPlayer;
            if (human == null) return new List<Command>();
            var list = world.IncomingAttacks(human.Id);
            foreach (var c in world.CommandsOf(human.Id))
                if (c.Kind == CommandKind.Attack || c.Kind == CommandKind.Return) list.Add(c);
            list.Sort((a, b) => a.ArriveTime.CompareTo(b.ArriveTime));
            return list;
        }

        /// <summary>One row per movement, with links to the villages and players involved; rows are reused.</summary>
        void RefreshMovements(World world, List<Command> moving, int incoming)
        {
            SetText(movementsHeading, incoming > 0 ? $"Troop movements  ·  {incoming} incoming" : "Troop movements");
            while (movementRows.Count < moving.Count)
            {
                var row = new MovementRow(links);
                movementRows.Add(row);
                movementsList.Add(row.Root);
            }
            for (int i = 0; i < movementRows.Count; i++)
            {
                Show(movementRows[i].Root, i < moving.Count);
                if (i < moving.Count) movementRows[i].Update(world, moving[i]);
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
            Button sound = null;
            sound = ButtonWith(game.SoundOn ? "Sound: On" : "Sound: Off", () => sound.text = game.ToggleSound() ? "Sound: On" : "Sound: Off", "btn");
            // (Only once there are sounds to switch: see GameAudio.)
            if (game.HasSounds) panel.Add(sound);
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
            // The top-most first: the send dialog can sit over a building's screen.
            if (confirm.style.display == DisplayStyle.Flex) Show(confirm, false);
            else if (sendDialog.IsOpen) sendDialog.Close();
            else if (buildingWindow.IsOpen) buildingWindow.Close();
            else CloseInfo();
        }

        // ---------------------------------------------------------------- victory and defeat

        VisualElement endScreen;
        Label endTitle, endText;
        Button endFirst, endSecond;
        Action endFirstAction, endSecondAction;

        void BuildEndScreen()
        {
            endScreen = Element("screen", "centered", "dim");
            var panel = Element("panel", "end-panel");
            endTitle = Text("", "title");
            endText = Text("", "body-text");
            panel.Add(endTitle);
            panel.Add(endText);
            var row = Element("option-row");
            row.style.marginTop = 14;
            endFirst = ButtonWith("", () => { Show(endScreen, false); endFirstAction?.Invoke(); }, "btn");
            endSecond = ButtonWith("", () => { Show(endScreen, false); endSecondAction?.Invoke(); }, "btn");
            row.Add(endFirst);
            row.Add(endSecond);
            panel.Add(row);
            endScreen.Add(panel);
            root.Add(endScreen);
            Show(endScreen, false);
        }

        void ShowEndScreen(string title, string text, string first, Action onFirst, string second, Action onSecond)
        {
            endTitle.text = title;
            endText.text = text;
            endFirst.text = first;
            endSecond.text = second;
            endFirstAction = onFirst;
            endSecondAction = onSecond;
            Show(endScreen, true);
        }

        /// <summary>The player reached the conquest goal.</summary>
        public void ShowVictory(World world) => ShowEndScreen("VICTORY",
            $"You rule {world.HumanVillages().Count:N0} of the {world.LordVillageCount:N0} villages held by lords ({world.HumanShare:P0}), " +
            $"past the {world.Settings.ConquestGoal:P0} you needed. The realm is yours!\nYou can keep playing this world as long as you like.",
            "Keep Playing", null, "Main Menu", () => game.LeaveToArcade());

        /// <summary>The player lost their last village.</summary>
        public void ShowDefeat(World world) => ShowEndScreen("DEFEATED",
            "Your last village has fallen. But a lord is more than their lands: start again with a new village on the " +
            "frontier of the realm, under fresh beginner protection.",
            "Rebuild on the Frontier", () => game.RespawnPlayer(), "Main Menu", () => game.LeaveToArcade());

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
