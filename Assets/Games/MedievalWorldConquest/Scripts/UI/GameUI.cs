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

        /// <summary>The top bar's height in UI units (as in the style sheet's .top-bar).</summary>
        public const float TopBarHeight = 84f;

        static readonly float[] Speeds = { 1f, 5f, 20f, 100f };

        /// <summary>What fills the screen below the top bar.</summary>
        enum View { Village, Map, Reports, Ranking, Overview, Tribe, Messages }


        readonly MedievalWorldConquestGame game;
        readonly VisualElement root;
        readonly Camera cam;
        VillagePanel villagePanel;
        BuildingWindow buildingWindow;
        ReportsPanel reportsPanel;
        RankingPanel rankingPanel;
        OverviewPanel overviewPanel;
        TribePanel tribePanel;
        MessagesPanel messagesPanel;
        TribeWindow tribeWindow;
        Label unreadMessages;
        readonly List<Button> diplomacyButtons = new List<Button>();
        bool chosenDiplomacy;
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

        // Start screen (the save slots) and the new-world screen
        VisualElement startScreen, slotRow, newWorldScreen;
        Label newWorldTitle;
        int newWorldSlot;
        readonly List<Button> speedButtons = new List<Button>();
        readonly List<Button> modeButtons = new List<Button>();
        readonly List<Button> skillButtons = new List<Button>();
        readonly List<Button> nobleButtons = new List<Button>();
        bool chosenCoins;
        float chosenSpeed = 5f;
        TimeMode chosenMode = TimeMode.RealTime;
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
        public BuildingType? SelectedBuilding => buildingWindow.Highlight;

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

        /// <summary>
        /// The start screen lists the save slots side by side: each world's summary with Continue and Delete, or
        /// New World for an empty slot. A new world's settings are on a screen of their own.
        /// </summary>
        void BuildStartScreen()
        {
            startScreen = Element("screen", "centered", "dim");
            root.Add(startScreen);
            var panel = Element("panel");
            startScreen.Add(panel);
            panel.Add(Text("MEDIEVAL WORLD CONQUEST", "title"));
            panel.Add(Text("Build a village, raise an army and conquer the realm.", "subtitle"));
            panel.Add(Text("Your worlds", "heading"));
            slotRow = Element("slot-row");
            panel.Add(slotRow);
            var buttons = Element("option-row");
            buttons.style.marginTop = 12;
            buttons.Add(ButtonWith("Main Menu", () => game.LeaveToArcade(), "btn"));
            panel.Add(buttons);

            BuildNewWorldScreen();
        }

        void BuildNewWorldScreen()
        {
            newWorldScreen = Element("screen", "centered", "dim");
            root.Add(newWorldScreen);
            var panel = Element("panel", "new-world-panel");
            newWorldScreen.Add(panel);
            newWorldTitle = Text("New world", "title");
            panel.Add(newWorldTitle);

            // One row per setting: its name on the left, the choices on the right.
            // What the player is called: shown on their villages and in the rankings.
            nameField = new TextField { maxLength = 24, value = World.DefaultPlayerName };
            nameField.AddToClassList("name-field");
            panel.Add(SettingRow("Your name", nameField));

            var speedRow = Element("setting-options");
            foreach (float speed in Speeds)
            {
                var b = ButtonWith(SpeedText(speed), () => ChooseSpeed(speed), "option");
                b.userData = speed;
                speedButtons.Add(b);
                speedRow.Add(b);
            }
            panel.Add(SettingRow("World speed", speedRow));

            var modeRow = Element("setting-options");
            modeButtons.Add(ButtonWith("Real time\nThe world keeps going", () => ChooseMode(TimeMode.RealTime), "option", "option-wide"));
            modeButtons.Add(ButtonWith("Paused\nTime only passes while playing", () => ChooseMode(TimeMode.PausedWhenClosed), "option", "option-wide"));
            modeButtons[0].userData = TimeMode.RealTime;
            modeButtons[1].userData = TimeMode.PausedWhenClosed;
            foreach (var b in modeButtons) modeRow.Add(b);
            panel.Add(SettingRow("When closed", modeRow));

            // How cleverly the rival lords play (how many there are is the world's own).
            var skillRow = Element("setting-options");
            foreach (AiSkill skill in Enum.GetValues(typeof(AiSkill)))
            {
                var b = ButtonWith(skill.ToString(), () => ChooseSkill(skill), "option");
                b.userData = skill;
                skillButtons.Add(b);
                skillRow.Add(b);
            }
            panel.Add(SettingRow("Rival skill", skillRow));

            // How noblemen are paid for: a flat price, or Tribal Wars' gold coins (dearer with every conquest).
            var nobleRow = Element("setting-options");
            nobleButtons.Add(ButtonWith("Flat price\nEvery nobleman costs the same", () => ChooseCoins(false), "option", "option-wide"));
            nobleButtons.Add(ButtonWith("Gold coins\nEach conquest makes the next dearer", () => ChooseCoins(true), "option", "option-wide"));
            nobleButtons[0].userData = false;
            nobleButtons[1].userData = true;
            foreach (var b in nobleButtons) nobleRow.Add(b);
            panel.Add(SettingRow("Noblemen", nobleRow));

            // Every lord for themselves, or a simulated MMO with tribes, pacts and wars.
            var diplomacyRow = Element("setting-options");
            diplomacyButtons.Add(ButtonWith("Free-for-all\nEvery lord for themselves", () => ChooseDiplomacy(false), "option", "option-wide"));
            diplomacyButtons.Add(ButtonWith("Tribes\nPacts, wars, and blocs that can win", () => ChooseDiplomacy(true), "option", "option-wide"));
            diplomacyButtons[0].userData = false;
            diplomacyButtons[1].userData = true;
            foreach (var b in diplomacyButtons) diplomacyRow.Add(b);
            panel.Add(SettingRow("Diplomacy", diplomacyRow));

            var buttons = Element("option-row");
            buttons.style.marginTop = 14;
            buttons.Add(ButtonWith("Start World", OnStartNewWorld, "btn"));
            buttons.Add(ButtonWith("Back", () => Show(newWorldScreen, false), "btn"));
            panel.Add(buttons);
            Show(newWorldScreen, false);

            ChooseSpeed(chosenSpeed);
            ChooseMode(chosenMode);
            ChooseSkill(chosenSkill);
            ChooseCoins(chosenCoins);
            ChooseDiplomacy(chosenDiplomacy);
        }

        /// <summary>A setting on the new-world screen: its name on the left, its choices on the right.</summary>
        static VisualElement SettingRow(string label, VisualElement choices)
        {
            var row = Element("setting-row");
            row.Add(Text(label, "body-text", "setting-label"));
            row.Add(choices);
            return row;
        }

        void ChooseDiplomacy(bool on)
        {
            chosenDiplomacy = on;
            foreach (var b in diplomacyButtons) b.EnableInClassList("option--selected", (bool)b.userData == on);
        }

        void ChooseCoins(bool coins)
        {
            chosenCoins = coins;
            foreach (var b in nobleButtons) b.EnableInClassList("option--selected", (bool)b.userData == coins);
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

        /// <summary>Opens the new-world screen for an empty slot.</summary>
        void OpenNewWorld(int saveSlot)
        {
            newWorldSlot = saveSlot;
            SetText(newWorldTitle, $"New world  ·  slot {saveSlot + 1}");
            Show(newWorldScreen, true);
        }

        void OnStartNewWorld()
        {
            var settings = new WorldSettings
            {
                Speed = chosenSpeed,
                TimeMode = chosenMode,
                Seed = Environment.TickCount,
                RivalSkill = chosenSkill,
                PlayerName = nameField.value,
                GoldCoins = chosenCoins,
                Diplomacy = chosenDiplomacy,
                ConquestGoal = WorldSettings.StandardGoal,
            };
            Show(newWorldScreen, false);
            game.StartNewWorld(settings, newWorldSlot);
        }

        /// <param name="summaries">Each save slot's world (null: empty).</param>
        /// <param name="errors">For each slot: why its save couldn't be read, if it couldn't.</param>
        public void ShowStart(SaveSummary[] summaries, string[] errors)
        {
            Show(startScreen, true);
            Show(newWorldScreen, false);
            Show(hud, false);
            Show(menu, false);

            slotRow.Clear();
            for (int i = 0; i < summaries.Length; i++)
            {
                int index = i;
                var s = summaries[i];
                var card = Element("slot-card");
                card.Add(Text($"World {i + 1}", "row-title", "slot-title"));
                var body = Element("slot-body");
                if (s != null)
                {
                    string state = s.Won ? "  ·  won" : s.Lost ? "  ·  the world has ended" : "";
                    body.Add(Text($"{s.PlayerName}  ·  {s.VillageName}", "slot-line", "slot-line--strong"));
                    body.Add(Text($"Day {s.Day}{state}", "slot-line"));
                    body.Add(Text($"{s.Villages:N0} {(s.Villages == 1 ? "village" : "villages")}  ·  {s.Points:N0} points" +
                                  (s.Rank > 0 ? $"  ·  rank {s.Rank:N0} of {s.Lords:N0}" : ""), "slot-line"));
                    body.Add(Text($"{SpeedText(s.Speed)}  ·  {ModeText(s.TimeMode)}  ·  {s.Skill} rivals", "slot-line"));
                    body.Add(Text($"{(s.GoldCoins ? "Gold coins" : "Flat-price noblemen")}  ·  {(s.Diplomacy ? "Tribes" : "Free-for-all")}", "slot-line"));
                    body.Add(Text($"Saved {new DateTime(s.SavedAtUtcTicks, DateTimeKind.Utc).ToLocalTime():g}", "slot-line", "slot-line--faint"));
                }
                else if (!string.IsNullOrEmpty(errors[i]))
                    body.Add(Text($"This world couldn't be read. ({errors[i]})", "slot-line", "error-text"));
                else
                    body.Add(Text("Empty", "slot-line", "slot-line--faint"));
                card.Add(body);

                var actions = Element("slot-actions");
                if (s != null)
                {
                    actions.Add(ButtonWith("Continue", () => game.ContinueWorld(index), "btn", "btn--small"));
                    actions.Add(ButtonWith("Delete", () => AskToConfirm($"Delete world {index + 1} for good? This can't be undone.", () => game.DeleteWorld(index)), "btn", "btn--small", "slot-delete"));
                }
                else if (!string.IsNullOrEmpty(errors[i]))
                    actions.Add(ButtonWith("Delete", () => AskToConfirm($"Delete the unreadable world {index + 1}?", () => game.DeleteWorld(index)), "btn", "btn--small", "slot-delete"));
                else
                    actions.Add(ButtonWith("New World", () => OpenNewWorld(index), "btn", "btn--small"));
                card.Add(actions);
                slotRow.Add(card);
            }
        }
        // ---------------------------------------------------------------- HUD

        void BuildHud()
        {
            hud = Element("screen");
            hud.pickingMode = PickingMode.Ignore;
            root.Add(hud);

            // The top bar, in two rows as in Tribal Wars. Above, the menu: the tabs (each with its name), then the
            // player's name (to their profile), the clock (the world's speed in its tooltip) and the menu button.
            // Below, the village: its name (back to the village) between the arrows to the player's other villages,
            // where it is and what it's worth, then its resources, storage and population.
            var top = Element("top-bar");
            var nav = Element("top-row", "top-row--nav");
            viewButtons[View.Village] = IconButton(nav, Icons.Village, "Village", () => ShowView(View.Village));
            viewButtons[View.Map] = IconButton(nav, Icons.Map, "Map", () => ShowView(currentView == View.Map ? View.Village : View.Map));
            viewButtons[View.Overview] = IconButton(nav, Icons.Villages, "Villages", () => ShowView(currentView == View.Overview ? View.Village : View.Overview));
            viewButtons[View.Overview].tooltip = "All your villages at a glance";
            // Reports: a scroll with a little red count of the unread ones.
            viewButtons[View.Reports] = IconButton(nav, Icons.Reports, "Reports", () => ShowView(currentView == View.Reports ? View.Village : View.Reports));
            unreadBadge = Text("", "unread-badge");
            unreadBadge.pickingMode = PickingMode.Ignore;
            viewButtons[View.Reports].Add(unreadBadge);
            viewButtons[View.Ranking] = IconButton(nav, Icons.Ranking, "Ranking", () => ShowView(currentView == View.Ranking ? View.Village : View.Ranking));
            // Tribes and messages, on diplomacy worlds only.
            viewButtons[View.Tribe] = IconButton(nav, Icons.Tribe, "Tribe", () => ShowView(currentView == View.Tribe ? View.Village : View.Tribe));
            viewButtons[View.Messages] = IconButton(nav, Icons.Messages, "Messages", () => ShowView(currentView == View.Messages ? View.Village : View.Messages));
            unreadMessages = Text("", "unread-badge");
            unreadMessages.pickingMode = PickingMode.Ignore;
            viewButtons[View.Messages].Add(unreadMessages);
            nav.Add(Element("spacer"));
            playerName = ButtonWith("", () => { var human = lastWorld?.HumanPlayer; if (human != null) OpenPlayerInfo(human.Id); }, "top-link", "top-player");
            playerName.tooltip = "Your profile";
            nav.Add(playerName);
            clock = Text("", "clock");
            speedTag = Text("", "speed-tag");
            nav.Add(clock);
            nav.Add(ButtonWith("Menu", ToggleMenu, "btn", "btn--small"));
            top.Add(nav);

            var here = Element("top-row", "top-row--village");
            previousVillage = ButtonWith("<", () => game.CycleVillage(-1), "btn", "btn--small", "village-arrow");
            here.Add(previousVillage);
            villageName = ButtonWith("", () => ShowView(View.Village), "top-link", "village-name");
            villageName.tooltip = "Back to the village";
            here.Add(villageName);
            nextVillage = ButtonWith(">", () => game.CycleVillage(1), "btn", "btn--small", "village-arrow");
            here.Add(nextVillage);
            villageInfo = Text("", "top-info");
            here.Add(villageInfo);
            here.Add(Element("spacer"));
            incomingWarning = Text("", "incoming-warning");
            here.Add(incomingWarning);
            for (int i = 0; i < 3; i++)
            {
                // Production per hour is in the tooltip (and the village pane), as in Tribal Wars, to keep the bar short.
                var chip = Element("resource");
                chip.Add(Icons.Element(Icons.Resource((ResourceType)i), 20, "resource-icon"));
                resourceValues[i] = Text("", "resource-value");
                chip.Add(resourceValues[i]);
                resourceChips[i] = chip;
                here.Add(chip);
            }
            var storage = Element("resource");
            storage.tooltip = "Warehouse capacity";
            storage.Add(Icons.Element(Icons.Storage, 20, "resource-icon"));
            storageValue = Text("", "resource-value");
            storage.Add(storageValue);
            here.Add(storage);
            var population = Element("resource");
            population.tooltip = "Population (used / farm limit)";
            population.Add(Icons.Element(Icons.Population, 20, "resource-icon"));
            populationValue = Text("", "resource-value");
            population.Add(populationValue);
            here.Add(population);
            top.Add(here);
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
                OpenTribe = OpenTribeInfo,
                InviteToTribe = id => game.InviteToTribe(id),
                ExpelFromTribe = id => AskToConfirm("Expel this lord from your tribe?", () => game.ExpelFromTribe(id)),
                SetTribeTarget = id => game.SetTribeTarget(id),
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
            tribePanel = new TribePanel(game, links, AskToConfirm);
            hud.Add(tribePanel.Root);
            messagesPanel = new MessagesPanel(game, links);
            hud.Add(messagesPanel.Root);

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
            tribeWindow = new TribeWindow(game, links);
            hud.Add(tribeWindow.Root);
            ShowView(View.Village);
        }

        UiLinks links;
        VillageWindow villageWindow;
        PlayerWindow playerWindow;

        /// <summary>Opens the window about a village (from the map, or a village's name anywhere).</summary>
        public void OpenVillageInfo(int villageId)
        {
            playerWindow.Close();
            tribeWindow.Close();
            villageWindow.Open(villageId);
        }

        /// <summary>Opens a player's profile (from their name anywhere).</summary>
        public void OpenPlayerInfo(int playerId)
        {
            villageWindow.Close();
            tribeWindow.Close();
            playerWindow.Open(playerId);
        }

        /// <summary>Opens a tribe's window (from its tag anywhere).</summary>
        public void OpenTribeInfo(int tribeId)
        {
            villageWindow.Close();
            playerWindow.Close();
            tribeWindow.Open(tribeId);
        }

        /// <summary>Whether the village, player or tribe window is showing.</summary>
        public bool InfoOpen => villageWindow.IsOpen || playerWindow.IsOpen || tribeWindow.IsOpen;

        /// <summary>Closes the village, player and tribe windows.</summary>
        public void CloseInfo()
        {
            villageWindow.Close();
            playerWindow.Close();
            tribeWindow.Close();
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
            Show(tribePanel.Root, view == View.Tribe);
            Show(messagesPanel.Root, view == View.Messages);
            if (view != View.Village) buildingWindow.Close();
        }

        /// <summary>Opens a building's own screen in the village view.</summary>
        public void OpenBuilding(BuildingType type)
        {
            ShowView(View.Village);
            buildingWindow.Open(type);
        }

        /// <summary>Opens the Recruit screen (the barracks, stable and workshop together) in the village view.</summary>
        public void OpenRecruit()
        {
            ShowView(View.Village);
            buildingWindow.OpenRecruitAll();
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
                ? $"({v.X}|{v.Y}) {World.ContinentName(v.X, v.Y)} · {v.Points:N0} points · loyalty {Math.Floor(v.Loyalty):0}"
                : $"({v.X}|{v.Y}) {World.ContinentName(v.X, v.Y)} · {v.Points:N0} points");
            villageInfo.tooltip = own.Count > 1 ? $"Village {index + 1} of {own.Count}" : "Your village";
            Show(previousVillage, own.Count > 1);
            Show(nextVillage, own.Count > 1);
            // The world's speed is in the clock's tooltip, to leave the bar's room for the names.
            SetText(clock, World.FormatClock(world.Now));
            clock.tooltip = $"World speed {SpeedText(world.Settings.Speed)}";
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
            tribeWindow.Refresh(world);
            Show(viewButtons[View.Tribe], world.Diplomacy);
            Show(viewButtons[View.Messages], world.Diplomacy);
            int unreadMail = world.UnreadMessages;
            Show(unreadMessages, unreadMail > 0);
            SetText(unreadMessages, unreadMail > 99 ? "99+" : unreadMail.ToString());

            if (VillageTabActive)
            {
                villagePanel.Refresh(world, v);
                buildingWindow.Refresh(world, v);
            }
            else if (currentView == View.Map) Map.Refresh(world);
            else if (currentView == View.Reports) reportsPanel.Refresh(world);
            else if (currentView == View.Ranking) rankingPanel.Refresh(world);
            else if (currentView == View.Overview) overviewPanel.Refresh(world);
            else if (currentView == View.Tribe) tribePanel.Refresh(world);
            else if (currentView == View.Messages) messagesPanel.Refresh(world);
            sendDialog.Refresh(world);
        }

        VisualElement movementsPanel;
        Label movementsHeading;
        ScrollView movementsList;
        readonly List<MovementRow> movementRows = new List<MovementRow>();

        /// <summary>
        /// What the village view's movement list tracks, soonest first: every troop movement to or from the
        /// player's villages (attacks and support coming in, the player's own attacks and support going out, troops
        /// coming home). Merchants are at the market and the rally point.
        /// </summary>
        static List<Command> TrackedMovements(World world)
        {
            var human = world.HumanPlayer;
            if (human == null) return new List<Command>();
            return world.MovementsFor(human.Id).FindAll(c => !c.IsTrade);
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
        /// <summary>The standings shown when a world ends: the winning tribes and the top ten lords.</summary>
        ScrollView endStandings;
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
            endStandings = new ScrollView(ScrollViewMode.Vertical);
            endStandings.AddToClassList("end-standings");
            panel.Add(endStandings);
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
            endStandings.Clear();
            Show(endStandings, false);
            endFirst.text = first;
            endSecond.text = second;
            endFirstAction = onFirst;
            endSecondAction = onSecond;
            Show(endScreen, true);
        }

        /// <summary>The player reached the conquest goal: alone, or with their tribe and its allies.</summary>
        public void ShowVictory(World world)
        {
            var tribe = world.TribeOf(world.HumanPlayer);
            bool alone = world.HumanShare >= world.Settings.ConquestGoal || tribe == null;
            ShowEndScreen("VICTORY", alone
                ? $"You rule {world.HumanVillages().Count:N0} of the {world.GoalVillageCount:N0} {world.GoalVillagesLabel} ({world.HumanShare:P0}), " +
                  $"past the {world.Settings.ConquestGoal:P0} you needed. The realm is yours!\nYou can keep playing this world as long as you like."
                : $"{tribe.Name} [{tribe.Tag}] and its allies hold {world.BlocShare(tribe):P0} of the {world.GoalVillagesLabel}, past the {world.Settings.ConquestGoal:P0} " +
                  "needed. The realm is yours, and your allies', together!\nYou can keep playing this world as long as you like.",
                "Keep Playing", null, "Main Menu", () => game.LeaveToArcade());
            ShowStandings(world, alone ? null : tribe);
        }

        /// <summary>Under the end screen's words: the winning tribes (if a tribe won) and the top ten lords of the ranking.</summary>
        void ShowStandings(World world, Tribe winner)
        {
            endStandings.Clear();
            if (winner != null)
            {
                endStandings.Add(Text("The winning tribes", "row-title", "stats-heading"));
                var header = Element("ranking-row", "ranking-header");
                header.Add(Text("", "ranking-rank"));
                header.Add(Text("Tribe", "ranking-name"));
                header.Add(Text("Villages", "ranking-number"));
                header.Add(Text("Points", "ranking-number"));
                endStandings.Add(header);
                var bloc = new List<Tribe>();
                foreach (int id in world.BlocOf(winner))
                    if (world.FindTribe(id) is Tribe t) bloc.Add(t);
                bloc.Sort((a, b) => world.TribeStrength(b).villages.CompareTo(world.TribeStrength(a).villages));
                foreach (var t in bloc)
                {
                    var (points, villages) = world.TribeStrength(t);
                    var row = Element("ranking-row");
                    row.EnableInClassList("ranking-row--you", world.HumanPlayer?.TribeId == t.Id);
                    row.Add(Text("", "ranking-rank"));
                    row.Add(Text($"[{t.Tag}] {t.Name}  ·  {t.Members.Count} members", "ranking-name"));
                    row.Add(Text($"{villages:N0}", "ranking-number"));
                    row.Add(Text($"{points:N0}", "ranking-number"));
                    endStandings.Add(row);
                }
            }
            endStandings.Add(Text("The top ten lords", "row-title", "stats-heading"));
            var top = Element("ranking-row", "ranking-header");
            top.Add(Text("#", "ranking-rank"));
            top.Add(Text("Lord", "ranking-name"));
            top.Add(Text("Villages", "ranking-number"));
            top.Add(Text("Points", "ranking-number"));
            endStandings.Add(top);
            var rankings = world.Rankings();
            for (int i = 0; i < rankings.Count && i < 10; i++)
            {
                var r = rankings[i];
                var row = Element("ranking-row");
                row.EnableInClassList("ranking-row--you", r.Player.IsHuman);
                row.Add(Text($"{i + 1}", "ranking-rank"));
                row.Add(Text(world.NameWithTag(r.Player) + (r.Player.IsHuman ? " (you)" : ""), "ranking-name"));
                row.Add(Text($"{r.Villages:N0}", "ranking-number"));
                row.Add(Text($"{r.Points:N0}", "ranking-number"));
                endStandings.Add(row);
            }
            Show(endStandings, true);
        }

        /// <summary>
        /// On a diplomacy world, a side without the player took the world first (a satellite of the winning faction
        /// included): the world has ended, and the end screen shows the winning tribes and the top ten lords.
        /// </summary>
        public void ShowBlocVictory(World world)
        {
            var winner = world.FindTribe(world.WinningTribeId);
            ShowEndScreen("THE WORLD HAS ENDED", $"A side held {world.Settings.ConquestGoal:P0} of the {world.GoalVillagesLabel} for {World.HoldDays:0} days. Here are the winning tribes.\n" +
                "You can keep playing this world as long as you like.", "Keep Playing", null, "Main Menu", () => game.LeaveToArcade());
            ShowStandings(world, winner);
        }

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
