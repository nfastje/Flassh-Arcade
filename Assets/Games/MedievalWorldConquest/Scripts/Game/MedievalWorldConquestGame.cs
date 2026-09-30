using System;
using FlasshArcade;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace MedievalWorldConquest
{
    /// <summary>
    /// Runs Medieval World Conquest: owns the <see cref="World"/> simulation, advances it in real time, saves and
    /// loads it (catching up on time spent away), and connects it to the illustrated village and the UI.
    /// </summary>
    public partial class MedievalWorldConquestGame : MonoBehaviour
    {
        const float AutosaveInterval = 30f; // real seconds
        const float ViewSize = 5.5f; // village camera: 11 units tall, enough for the clearing and the four resource corners

        // World map camera: zoom is the camera's orthographic size (half the fields visible top to bottom).
        const float MinMapZoom = 4f, MaxMapZoom = 130f, DefaultMapZoom = 14f;
        const float DragThreshold = 6f; // pixels the mouse must move before a press becomes a drag instead of a click

        Camera cam;
        GameUI ui;
        GameAudio audio;
        int lastIncoming;
        VillageView village;
        MapView map;
        World world;
        float autosaveTimer;

        Vector2 mapFocus;
        float mapZoom = DefaultMapZoom;
        bool mapPressed, mapDragging;
        Vector2 dragStart, dragLast;

        /// <summary>The save slot of the world being played (-1: none).</summary>
        int slot = -1;

        void Awake()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.orthographic = true;
            cam.orthographicSize = ViewSize;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            ArcadeGui.SetBackground(new Color(0.2f, 0.3f, 0.15f));

            // UI Toolkit reads clicks through an EventSystem wired to the Input System (the project's only input backend).
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Event System");
                events.AddComponent<EventSystem>();
                events.AddComponent<InputSystemUIInputModule>();
            }

            audio = gameObject.AddComponent<GameAudio>();
            ui = GameUI.Create(this, cam);
            ShowStartScreen();
        }

        /// <summary>
        /// While the village view's pane covers the right of the screen, the camera shifts right so the village sits
        /// centered in the space that's left: half the pane's width, converted from UI to world units.
        /// </summary>
        static float VillageCameraOffset => VillagePanel.PaneWidth / 2f * (ViewSize * 2f) / 720f;

        /// <summary>
        /// The start screen: every save slot with a summary of its world (continue, delete, or start a new one in an
        /// empty slot), over the most recently played world's village.
        /// </summary>
        void ShowStartScreen()
        {
            var summaries = new SaveSummary[SaveFiles.Slots];
            var errors = new string[SaveFiles.Slots];
            int latest = -1;
            for (int i = 0; i < SaveFiles.Slots; i++)
            {
                summaries[i] = SaveFiles.Summary(i, out errors[i]);
                if (summaries[i] != null && (latest < 0 || summaries[i].SavedAtUtcTicks > summaries[latest].SavedAtUtcTicks)) latest = i;
            }
            ui.ShowStart(summaries, errors);
            var backdrop = latest >= 0 ? SaveFiles.Load(latest, out _, out _) : null;
            ShowVillage(backdrop?.Settings.Seed ?? 0);
            if (backdrop?.PlayerVillage != null) village.ShowVillage(backdrop.PlayerVillage);
        }

        /// <summary>Deletes the world in a slot for good, and shows the slots again.</summary>
        public void DeleteWorld(int saveSlot)
        {
            try
            {
                SaveFiles.Delete(saveSlot);
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                ui.ShowToast("Couldn't delete that world.", 4f);
            }
            ShowStartScreen();
        }

        void ShowVillage(int seed)
        {
            if (village != null) Destroy(village.gameObject);
            village = VillageView.Create(cam, seed);
        }

        void CreateMap()
        {
            if (map != null) Destroy(map.gameObject);
            map = MapView.Create(world);
            map.gameObject.SetActive(false);
            ui.CloseInfo();
            mapZoom = DefaultMapZoom;
            CenterMapOnHome();
        }

        public void CenterMapOnHome()
        {
            var home = world?.PlayerVillage;
            if (home != null) mapFocus = MapView.FieldCenter(home.X, home.Y);
        }

        /// <summary>Steps to the player's next (or previous) village.</summary>
        public void CycleVillage(int step)
        {
            var own = world?.HumanVillages();
            if (own == null || own.Count < 2) return;
            int index = own.IndexOf(world.PlayerVillage);
            SelectVillage(own[((index + step) % own.Count + own.Count) % own.Count].Id);
        }

        /// <summary>Makes one of the player's villages the current one: the village view, army and orders follow it.</summary>
        public void SelectVillage(int villageId)
        {
            // (An open building screen stays open, now showing the same building in the other village.)
            if (world == null || !world.SelectVillage(villageId)) return;
            ui.ShowToast($"Now in {world.PlayerVillage.Name}.", 2f);
        }

        /// <summary>After losing everything: a new village on the frontier.</summary>
        public void RespawnPlayer()
        {
            var fresh = world?.RespawnHuman();
            if (fresh == null) return;
            CenterMapOnHome();
            ui.ShowToast($"A new beginning at {fresh.Name} ({fresh.X}|{fresh.Y}).", 4f);
            SaveWorld();
        }

        /// <summary>Centers the map on a village and marks it (from its window's "Show on map").</summary>
        public void ShowOnMap(int villageId)
        {
            var v = world?.FindVillage(villageId);
            if (v == null) return;
            mapFocus = MapView.FieldCenter(v.X, v.Y);
            if (map != null) map.Select(v);
        }

        /// <summary>Moves the map view to a point, in map fields (from the minimap).</summary>
        public void CenterMapOn(Vector2 field) => mapFocus = MapView.FromFields(field);

        public void SaveWorld()
        {
            if (world == null || slot < 0) return;
            try
            {
                SaveFiles.Save(world, slot);
                autosaveTimer = 0f;
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"Medieval World Conquest: couldn't save the world: {e.Message}");
                ui.ShowToast("Couldn't save the game!", 4f);
            }
        }

        public void LeaveToArcade()
        {
            SaveWorld();
            world = null; // saved; don't save again as the scene unloads
            Arcade.LoadHome();
        }

        // ---------------------------------------------------------------- loop

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            ui.Tick(dt);

            var kb = Keyboard.current;
            bool escape = kb != null && kb.escapeKey.wasPressedThisFrame;

            if (world == null)
            {
                if (escape)
                {
                    if (ui.DialogOpen) ui.CloseDialog();
                    else LeaveToArcade();
                }
                return;
            }

            if (escape)
            {
                if (ui.DialogOpen || ui.InfoOpen) ui.CloseDialog();
                else ui.ToggleMenu();
            }

            // The world runs on regardless of menus, like the browser games it's based on.
            world.AdvanceByRealSeconds(dt);
            AnnounceNewReports();
            AnnounceNewMessages();
            SoundTheHorn();
            ui.Refresh(world);

            // Winning (once) and losing everything each get their own screen.
            if (world.Won && !world.VictoryShown && !ui.EndScreenOpen)
            {
                world.VictoryShown = true;
                ui.ShowVictory(world);
            }
            if (world.HumanDefeated && !ui.EndScreenOpen) ui.ShowDefeat(world);
            // On a diplomacy world, another bloc can take the world first.
            if (world.LostToBloc && !world.LossShown && !ui.EndScreenOpen)
            {
                world.LossShown = true;
                ui.ShowBlocVictory(world);
            }

            // The map tab swaps the illustrated village for the world map.
            bool onMap = ui.MapTabActive;
            village.gameObject.SetActive(!onMap);
            map.gameObject.SetActive(onMap);
            if (onMap) UpdateMap(dt);
            else UpdateVillage(dt);

            autosaveTimer += dt;
            if (autosaveTimer >= AutosaveInterval) SaveWorld();
        }

        int lastAnnouncedReport;

        /// <summary>Pops up the headline of any report that arrived since last frame.</summary>
        void AnnounceNewReports()
        {
            int newest = world.NextReportId - 1;
            if (newest <= lastAnnouncedReport) return;
            lastAnnouncedReport = newest;
            var report = world.FindReport(newest);
            if (report == null) return;
            ui.ShowToast($"New report: {ReportsPanel.Title(report)}", 4f);
            if (report.Kind == ReportKind.ResourcesArrived) audio.Play(GameAudio.Sound.Coins, 0.4f);
            else if (report.Kind != ReportKind.SupportArrived) audio.Play(report.PlayerWon ? GameAudio.Sound.Victory : GameAudio.Sound.Defeat);
        }

        /// <summary>Plays a sound when one of the player's buildings or researches finishes.</summary>
        void ListenForSounds()
        {
            lastIncoming = world.HumanPlayer != null ? world.IncomingAttacks(world.HumanPlayer.Id).Count : 0;
            world.EventApplied += e =>
            {
                if (e.Kind != EventKind.BuildingComplete && e.Kind != EventKind.ResearchComplete) return;
                var v = world.FindVillage(e.VillageId);
                if (v == null || v.OwnerId != world.HumanPlayer?.Id) return;
                audio.Play(e.Kind == EventKind.BuildingComplete ? GameAudio.Sound.BuildDone : GameAudio.Sound.ResearchDone, 0.5f);
            };
        }

        /// <summary>A horn when a new attack on the player is seen coming.</summary>
        void SoundTheHorn()
        {
            int incoming = world.HumanPlayer != null ? world.IncomingAttacks(world.HumanPlayer.Id).Count : 0;
            if (incoming > lastIncoming) audio.Play(GameAudio.Sound.Incoming, 0.7f);
            lastIncoming = incoming;
        }

        /// <summary>Switches sound on or off (remembered). Returns whether it's now on.</summary>
        public bool ToggleSound()
        {
            audio.SetMuted(!audio.Muted);
            return !audio.Muted;
        }

        public bool SoundOn => audio != null && !audio.Muted;

        /// <summary>Whether the game has any sounds yet (none until custom clips are added).</summary>
        public bool HasSounds => audio != null && audio.HasSounds;

        void UpdateVillage(float dt)
        {
            if (world.PlayerVillage == null) return; // defeated: nothing to show until they start again
            village.ShowVillage(world.PlayerVillage);
            village.Highlight(ui.VillageTabActive ? ui.SelectedBuilding : null);
            HandleVillageClicks();

            cam.orthographicSize = ViewSize;
            float targetX = ui.VillageTabActive ? VillageCameraOffset : 0f;
            var camPos = cam.transform.position;
            // Straight to its place, whichever tab the player comes back from (no sliding across the scene).
            camPos.x = targetX;
            // Raised by half the top bar, so the village sits in the middle of the space below it.
            camPos.y = GameUI.TopBarHeight / 2f * (ViewSize * 2f) / 720f;
            cam.transform.position = camPos;
        }

        Vector2 ScreenToScene(Vector2 screen) => cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));

        /// <summary>World map controls: drag or arrow keys to pan, scroll to zoom towards the cursor, click to select a village.</summary>
        void UpdateMap(float dt)
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool blocked = ui.MenuOpen || ui.DialogOpen;
            Vector2 screen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            bool overUI = mouse == null || ui.IsPointerOverUI(screen);

            // Keep the camera on the map before converting mouse positions.
            cam.orthographicSize = mapZoom;
            cam.transform.position = new Vector3(mapFocus.x, mapFocus.y, -10f);

            if (!blocked && kb != null)
            {
                var move = Vector2.zero;
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) move.x -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) move.x += 1f;
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) move.y -= 1f;
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) move.y += 1f;
                mapFocus += move * (mapZoom * 1.2f * dt);
            }

            if (mouse != null && !blocked)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !overUI)
                {
                    // Zoom towards the cursor: the point under it stays put.
                    Vector2 before = ScreenToScene(screen);
                    mapZoom = Mathf.Clamp(mapZoom * (scroll > 0f ? 0.87f : 1f / 0.87f), MinMapZoom, MaxMapZoom);
                    cam.orthographicSize = mapZoom;
                    mapFocus += before - ScreenToScene(screen);
                    cam.transform.position = new Vector3(mapFocus.x, mapFocus.y, -10f);
                }

                if (mouse.leftButton.wasPressedThisFrame && !overUI)
                {
                    mapPressed = true;
                    mapDragging = false;
                    dragStart = dragLast = screen;
                }
                if (mapPressed && mouse.leftButton.isPressed)
                {
                    if (!mapDragging && (screen - dragStart).magnitude > DragThreshold) mapDragging = true;
                    if (mapDragging) mapFocus += ScreenToScene(dragLast) - ScreenToScene(screen);
                    dragLast = screen;
                }
                if (mapPressed && mouse.leftButton.wasReleasedThisFrame)
                {
                    if (!mapDragging)
                    {
                        // A village opens its window; empty map closes it.
                        var picked = MapView.VillageNear(world, ScreenToScene(screen), 0.8f);
                        map.Select(picked);
                        if (picked != null) ui.OpenVillageInfo(picked.Id);
                        else ui.CloseInfo();
                    }
                    mapPressed = mapDragging = false;
                }
            }

            // Stay over the map.
            mapFocus.x = Mathf.Clamp(mapFocus.x, MapView.Origin.x, MapView.Origin.x + World.MapSize * MapView.FieldWidth);
            mapFocus.y = Mathf.Clamp(mapFocus.y, MapView.Origin.y, MapView.Origin.y + World.MapSize);
            cam.transform.position = new Vector3(mapFocus.x, mapFocus.y, -10f);

            var field = MapView.FieldAt(ScreenToScene(screen));
            bool onMapArea = !overUI && field.x >= 0 && field.y >= 0 && field.x < World.MapSize && field.y < World.MapSize;
            // The part of the map in view, in fields, for the minimap's frame.
            Vector2 low = MapView.ToFields(ScreenToScene(Vector2.zero)), high = MapView.ToFields(ScreenToScene(new Vector2(Screen.width, Screen.height)));
            ui.Map.ViewInFields = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
            var hovered = onMapArea && !blocked && !mapDragging ? MapView.VillageNear(world, ScreenToScene(screen), 0.8f) : null;
            ui.Map.ShowTooltip(world, hovered, screen);

            map.ShowGrid(cam.orthographicSize, Screen.height); // finer lines fade in as the view zooms in
            ui.Map.ShowContinents(cam, cam.orthographicSize);
            map.Refresh(world, dt: dt);
        }

        /// <summary>Clicking a building in the illustrated village opens its screen.</summary>
        void HandleVillageClicks()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (!ui.VillageTabActive || ui.MenuOpen || ui.DialogOpen) return;

            Vector2 screen = mouse.position.ReadValue();
            if (ui.IsPointerOverUI(screen)) return;

            Vector2 worldPos = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            var hit = VillageView.BuildingAt(worldPos, world.PlayerVillage);
            if (hit.HasValue) ui.OpenBuilding(hit.Value);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveWorld();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) SaveWorld();
        }

        void OnApplicationQuit() => SaveWorld();

        void OnDestroy() => SaveWorld();
    }
}
