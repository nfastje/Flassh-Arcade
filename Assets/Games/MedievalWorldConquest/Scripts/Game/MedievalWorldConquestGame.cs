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
    public class MedievalWorldConquestGame : MonoBehaviour
    {
        const float AutosaveInterval = 30f; // real seconds
        const float ViewSize = 5.5f; // village camera: 11 units tall, enough for the clearing and the four resource corners

        // World map camera: zoom is the camera's orthographic size (half the fields visible top to bottom).
        const float MinMapZoom = 4f, MaxMapZoom = 130f, DefaultMapZoom = 14f;
        const float DragThreshold = 6f; // pixels the mouse must move before a press becomes a drag instead of a click

        Camera cam;
        GameUI ui;
        VillageView village;
        MapView map;
        World world;
        float autosaveTimer;

        Vector2 mapFocus;
        float mapZoom = DefaultMapZoom;
        bool mapPressed, mapDragging;
        Vector2 dragStart, dragLast;

        public bool HasSave => SaveFiles.Exists;

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

            ui = GameUI.Create(this, cam);
            ShowStartScreen();
        }

        /// <summary>
        /// While the Village tab's building list covers the right of the screen, the camera shifts right so the
        /// village sits centred in the space that's left: half the list's width, converted from UI to world units.
        /// </summary>
        static float VillageCameraOffset => VillagePanel.ListWidth / 2f * (ViewSize * 2f) / 720f;

        void ShowStartScreen()
        {
            World saved = null;
            string problem = null;
            if (SaveFiles.Exists)
            {
                saved = SaveFiles.Load(out _, out string error);
                if (saved == null) problem = $"Your saved world couldn't be loaded. ({error})";
            }
            ui.ShowStart(saved, problem);
            ShowVillage(saved?.Settings.Seed ?? 0);
            if (saved?.PlayerVillage != null) village.ShowVillage(saved.PlayerVillage); // the saved village as a backdrop
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
            ui.Map.SelectedVillageId = null;
            mapZoom = DefaultMapZoom;
            CenterMapOnHome();
        }

        public void CenterMapOnHome()
        {
            var home = world?.PlayerVillage;
            if (home != null) mapFocus = MapView.FieldCentre(home.X, home.Y);
        }

        /// <summary>Moves the map view to a point, in map fields (from the minimap).</summary>
        public void CenterMapOn(Vector2 field) => mapFocus = MapView.Origin + field;

        // ---------------------------------------------------------------- called by the UI

        public void StartNewWorld(WorldSettings settings)
        {
            world = World.CreateNew(settings);
            SaveWorld();
            ShowVillage(settings.Seed);
            CreateMap();
            lastAnnouncedReport = 0;
            ui.ShowGame(world);
            ui.ShowToast($"Welcome to {world.PlayerVillage.Name}.", 4f);
        }

        public void ContinueWorld()
        {
            var loaded = SaveFiles.Load(out DateTime savedAt, out string error);
            if (loaded == null)
            {
                ui.ShowStart(null, $"Your saved world couldn't be loaded. ({error})");
                return;
            }

            world = loaded;
            double away = SaveGame.CatchUpRealSeconds(world, savedAt, DateTime.UtcNow);
            double before = world.Now;
            world.AdvanceByRealSeconds(away);

            ShowVillage(world.Settings.Seed);
            CreateMap();
            lastAnnouncedReport = world.NextReportId - 1; // reports from the catch-up wait in the Reports tab
            ui.ShowGame(world);
            if (world.Now - before >= 60)
                ui.ShowToast($"While you were away, {World.FormatDuration(world.Now - before)} passed in the realm.", 5f);
            else
                ui.ShowToast($"Welcome back to {world.PlayerVillage.Name}.", 3f);
        }

        public void QueueBuild(BuildingType type)
        {
            var v = world?.PlayerVillage;
            if (v == null) return;
            var check = world.QueueBuild(v, type);
            if (check.Status != BuildStatus.Ok)
                ui.ShowToast($"Can't upgrade {Buildings.Get(type).Name} right now.");
        }

        /// <summary>Queues a batch of troops. Returns whether it worked.</summary>
        public bool Recruit(UnitType unit, int count)
        {
            var v = world?.PlayerVillage;
            if (v == null) return false;
            var check = world.Recruit(v, unit, count);
            if (check.Status == RecruitStatus.Ok)
            {
                ui.ShowToast($"Training {count:N0} × {Units.Get(unit).Name}.");
                return true;
            }
            ui.ShowToast($"Can't train {Units.Get(unit).Name} right now.");
            return false;
        }

        public void CancelRecruit(int orderId)
        {
            var v = world?.PlayerVillage;
            if (v != null && world.CancelRecruit(v, orderId)) ui.ShowToast("Training cancelled. Untrained units refunded.");
        }

        /// <summary>Sends troops from the player's village to another. Returns whether they set off.</summary>
        public bool SendTroops(int targetVillageId, int[] troops, CommandKind kind, BuildingType catapultTarget)
        {
            var home = world?.PlayerVillage;
            var target = world?.FindVillage(targetVillageId);
            if (home == null || target == null) return false;
            var command = world.Send(home, target, troops, kind, catapultTarget);
            if (command == null)
            {
                ui.ShowToast("Those troops can't be sent.");
                return false;
            }
            string verb = kind == CommandKind.Attack ? "Attack" : "Support";
            ui.ShowToast($"{verb} sent to {target.Name}. Arrives in {Ui.Real(world, command.ArriveTime - world.Now)}.", 4f);
            return true;
        }

        public void Recall(int hostVillageId, int fromVillageId)
        {
            var host = world?.FindVillage(hostVillageId);
            if (host != null && world.Recall(host, fromVillageId) != null) ui.ShowToast("Support recalled. The troops are heading home.");
        }

        public void MarkReportRead(int id)
        {
            var report = world?.FindReport(id);
            if (report != null) report.Read = true;
        }

        public void MarkAllReportsRead() => world?.MarkAllReportsRead();

        public void DeleteReport(int id) => world?.DeleteReport(id);

        public void CancelLastBuild()
        {
            var v = world?.PlayerVillage;
            if (v != null && world.CancelLastBuild(v)) ui.ShowToast("Construction cancelled. Resources refunded.");
        }

        public void SaveWorld()
        {
            if (world == null) return;
            try
            {
                SaveFiles.Save(world);
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
                if (ui.DialogOpen) ui.CloseDialog();
                else ui.ToggleMenu();
            }

            // The world runs on regardless of menus, like the browser games it's based on.
            world.AdvanceByRealSeconds(dt);
            AnnounceNewReports();
            ui.Refresh(world);

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
            if (report != null) ui.ShowToast($"New report: {ReportsPanel.Title(report)}", 4f);
        }

        void UpdateVillage(float dt)
        {
            village.ShowVillage(world.PlayerVillage);
            village.Highlight(ui.VillageTabActive ? ui.SelectedBuilding : null);
            HandleVillageClicks();

            cam.orthographicSize = ViewSize;
            float targetX = ui.VillageTabActive ? VillageCameraOffset : 0f;
            var camPos = cam.transform.position;
            // Coming back from the map, jump straight to the village rather than sliding across the scene.
            camPos.x = Mathf.Abs(camPos.x - targetX) > 50f ? targetX : Mathf.Lerp(camPos.x, targetX, 1f - Mathf.Exp(-8f * dt));
            camPos.y = 0f;
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
                        var picked = MapView.VillageNear(world, ScreenToScene(screen), 0.8f);
                        ui.Map.SelectedVillageId = picked?.Id;
                        map.Select(picked);
                    }
                    mapPressed = mapDragging = false;
                }
            }

            // Stay over the map.
            mapFocus.x = Mathf.Clamp(mapFocus.x, MapView.Origin.x, MapView.Origin.x + World.MapSize);
            mapFocus.y = Mathf.Clamp(mapFocus.y, MapView.Origin.y, MapView.Origin.y + World.MapSize);
            cam.transform.position = new Vector3(mapFocus.x, mapFocus.y, -10f);

            var field = MapView.FieldAt(ScreenToScene(screen));
            bool onMapArea = !overUI && field.x >= 0 && field.y >= 0 && field.x < World.MapSize && field.y < World.MapSize;
            ui.Map.Hover = onMapArea ? field : (Vector2Int?)null;
            // The part of the map in view, in fields, for the minimap's frame.
            Vector2 low = ScreenToScene(Vector2.zero) - MapView.Origin, high = ScreenToScene(new Vector2(Screen.width, Screen.height)) - MapView.Origin;
            ui.Map.ViewInFields = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
            var hovered = onMapArea && !blocked && !mapDragging ? MapView.VillageNear(world, ScreenToScene(screen), 0.8f) : null;
            ui.Map.ShowTooltip(world, hovered, screen);

            map.Refresh(world, dt: dt);
        }

        /// <summary>Clicking a building in the illustrated village selects it in the building list.</summary>
        void HandleVillageClicks()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (!ui.VillageTabActive || ui.MenuOpen || ui.DialogOpen) return;

            Vector2 screen = mouse.position.ReadValue();
            if (ui.IsPointerOverUI(screen)) return;

            Vector2 worldPos = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            var hit = VillageView.BuildingAt(worldPos);
            if (hit.HasValue) ui.SelectBuilding(hit);
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
