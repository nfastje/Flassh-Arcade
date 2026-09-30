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
    public partial class GameUI
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
        Label villageInfo, clock, toastLabel, confirmText;
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
    }
}
