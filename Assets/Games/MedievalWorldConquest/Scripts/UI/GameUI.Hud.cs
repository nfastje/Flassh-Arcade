using System;
using System.Collections.Generic;
using System.Linq;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The in-game HUD: the two-row top bar, the pages below it, and the village's troop movements.
    /// </summary>
    public partial class GameUI
    {
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
            viewButtons[View.Manager] = IconButton(nav, Icons.Ledger, "Manager", () => ShowView(currentView == View.Manager ? View.Village : View.Manager));
            viewButtons[View.Manager].tooltip = "The Account Manager: build templates and troop targets for your villages";
            // The Loot Assistant, once the first raid quest has unlocked it.
            viewButtons[View.Loot] = IconButton(nav, Icons.Loot, "Loot", () => ShowView(currentView == View.Loot ? View.Village : View.Loot));
            viewButtons[View.Loot].tooltip = "The Loot Assistant: one-click raids, and a raid cycle that keeps going while you're away";
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
            nav.Add(clock);
            // (Only in worlds that stand still while the game is closed; P does the same.)
            pauseButton = ButtonWith("Pause", () => game.TogglePause(), "btn", "btn--small", "pause-btn");
            pauseButton.tooltip = "Pause the world (P)";
            nav.Add(pauseButton);
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
                resourceValues[i] = new SteadyNumber("resource-value");
                chip.Add(resourceValues[i]);
                resourceChips[i] = chip;
                here.Add(chip);
            }
            var storage = Element("resource");
            storage.tooltip = "Warehouse capacity";
            storage.Add(Icons.Element(Icons.Storage, 20, "resource-icon"));
            storageValue = new SteadyNumber("resource-value");
            storage.Add(storageValue);
            here.Add(storage);
            var population = Element("resource");
            population.tooltip = "Population (used / farm limit)";
            population.Add(Icons.Element(Icons.Population, 20, "resource-icon"));
            populationValue = new SteadyNumber("resource-value", "resource-value--wide");
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
            }, game.RenameVillage);
            hud.Add(overviewPanel.Root);
            managerPanel = new ManagerPanel(game, links, AskToConfirm);
            hud.Add(managerPanel.Root);
            lootPanel = new LootPanel(game, links);
            hud.Add(lootPanel.Root);
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

        Button pauseButton;

        /// <summary>Whether the player is typing in a text box (so keys like P are letters, not shortcuts).</summary>
        public bool Typing => root.panel?.focusController?.focusedElement is VisualElement focused
                              && (focused is TextField || focused is IntegerField || focused.GetFirstAncestorOfType<TextField>() != null
                                  || focused.GetFirstAncestorOfType<IntegerField>() != null);

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
            Show(managerPanel.Root, view == View.Manager);
            Show(lootPanel.Root, view == View.Loot);
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
            RefreshTooltip();
            var v = world.PlayerVillage;
            if (v == null) return;
            var own = world.HumanVillagesByName();
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
            SetText(clock, game.Paused ? $"{World.FormatClock(world.Now)}  ·  paused" : World.FormatClock(world.Now));
            clock.tooltip = $"World speed {SpeedText(world.Settings.Speed)}";
            clock.EnableInClassList("clock--paused", game.Paused);
            Show(pauseButton, game.CanPause);
            SetText(pauseButton, game.Paused ? "Resume" : "Pause");

            int capacity = v.StorageCapacity;
            for (int i = 0; i < resourceValues.Length; i++)
            {
                var r = (ResourceType)i;
                double stock = v.Stock(r);
                resourceValues[i].SetText($"{Math.Floor(stock):N0}");
                // Rates are per real hour, like every duration in the UI.
                resourceChips[i].tooltip = $"{r}: +{v.ProductionPerHour(r) * world.Settings.Speed:N0} per hour";
                resourceValues[i].EnableInClassList("resource-value--full", stock >= capacity);
            }
            storageValue.SetText($"{capacity:N0}");
            int used = v.PopulationUsed, cap = v.PopulationCapacity;
            populationValue.SetText($"{used:N0}/{cap:N0}");
            populationValue.EnableInClassList("resource-value--full", used >= cap);

            int unread = world.UnreadReports;
            Show(unreadBadge, unread > 0);
            SetText(unreadBadge, unread > 99 ? "99+" : unread.ToString());

            // The banner up top counts the attacks heading for the player; the village view lists them, soonest first.
            var incoming = world.HumanPlayer != null ? world.IncomingAttacks(world.HumanPlayer.Id) : new List<Command>();
            Show(incomingWarning, incoming.Count > 0);
            if (incoming.Count > 0) SetText(incomingWarning, $"{incoming.Count} incoming");
            // (The list under the village is the village's own: switch villages, from the banner's count or the
            // villages overview, to see another's.)
            var here = world.PlayerVillage;
            var moving = TrackedMovements(world, here);
            Show(movementsPanel, moving.Count > 0 && VillageTabActive && !buildingWindow.IsOpen);
            if (moving.Count > 0 && VillageTabActive) RefreshMovements(world, moving, incoming.Count(c => c.ToVillageId == here?.Id));
            villageWindow.Refresh(world);
            playerWindow.Refresh(world);
            tribeWindow.Refresh(world);
            Show(viewButtons[View.Tribe], world.Diplomacy);
            Show(viewButtons[View.Messages], world.Diplomacy);
            Show(viewButtons[View.Loot], world.LootAssistantUnlocked);
            if (currentView == View.Loot && !world.LootAssistantUnlocked) ShowView(View.Village);
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
            else if (currentView == View.Manager) managerPanel.Refresh(world);
            else if (currentView == View.Loot) lootPanel.Refresh(world);
            else if (currentView == View.Tribe) tribePanel.Refresh(world);
            else if (currentView == View.Messages) messagesPanel.Refresh(world);
            sendDialog.Refresh(world);
        }

        VisualElement movementsPanel;
        Label movementsHeading;
        ScrollView movementsList;
        readonly List<MovementRow> movementRows = new List<MovementRow>();

        /// <summary>
        /// What the village view's movement list tracks, soonest first: the troop movements to or from the village
        /// being viewed (attacks and support coming in, its own attacks and support going out, its troops coming
        /// home). Merchants are at the market and the rally point.
        /// </summary>
        static List<Command> TrackedMovements(World world, Village here)
        {
            var human = world.HumanPlayer;
            if (human == null || here == null) return new List<Command>();
            return world.MovementsFor(human.Id).FindAll(c => !c.IsTrade && (c.ToVillageId == here.Id || c.FromVillageId == here.Id));
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
    }
}
