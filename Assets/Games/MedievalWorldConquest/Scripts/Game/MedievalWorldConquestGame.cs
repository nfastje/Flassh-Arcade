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
            ui.ShowStart(summaries, errors, PlayerRecord.Lifetime(ProfileFile.Load(), summaries));
            var backdrop = latest >= 0 ? SaveFiles.Load(latest, out _, out _) : null;
            ShowVillage(backdrop?.Settings.Seed ?? 0);
            if (backdrop?.PlayerVillage != null) village.ShowVillage(backdrop.PlayerVillage);
        }

        /// <summary>
        /// Deletes the world in a slot for good, and shows the slots again. What the player did there is kept first,
        /// in their lifetime record.
        /// </summary>
        public void DeleteWorld(int saveSlot)
        {
            try
            {
                var summary = SaveFiles.Summary(saveSlot, out _);
                if (summary != null)
                {
                    var record = ProfileFile.Load();
                    record.Bank(summary);
                    ProfileFile.Save(record);
                }
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
            var own = world?.HumanVillagesByName();
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
            if (world == null || slot < 0 || catchingUp) return;
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
            if (catchingUp) return;

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
            if (kb != null && kb.pKey.wasPressedThisFrame && !ui.Typing) TogglePause();

            // The world runs on regardless of menus, like the browser games it's based on (unless the player has
            // paused a world that only runs while it's played).
            if (!Paused)
            {
                // Back from the background (the game doesn't run there): catch up behind the progress popup.
                if (dt > CatchUpGap)
                {
                    StartCoroutine(CatchUp(Math.Min(dt, SaveGame.MaxCatchUpSeconds), null));
                    return;
                }
                world.AdvanceByRealSeconds(dt);
                world.PlayedSeconds += dt;
            }
            AnnounceNewReports();
            AnnounceNewMessages();
            SoundTheHorn();
            world.UpdateQuests();
            CheckTips();
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
            // (A raid cycle's raids that go to plan come and go quietly.)
            if (report == null || report.Routine) return;
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

        /// <summary>Whether there's a music track yet (none until one is added), and switching it on and off.</summary>
        public bool HasMusic => audio != null && audio.HasMusic;
        public bool MusicOn => audio != null && !audio.MusicOff;

        public bool ToggleMusic()
        {
            audio.SetMusicOff(!audio.MusicOff);
            return !audio.MusicOff;
        }

        // ---------------------------------------------------------------- the Account Manager

        public void SetVillageTemplate(int villageId, string templateName) => world?.SetVillageTemplate(world.FindVillage(villageId), templateName);

        public void SetTroopTargets(int villageId, int[] targets, int troopShare)
        {
            if (world == null) return;
            world.SetTroopTargets(world.FindVillage(villageId), targets, troopShare);
            ui.ShowToast("Troop targets saved.", 2f);
        }

        /// <summary>A new build template of the player's; returns why not, or null.</summary>
        public string CreateTemplate(string name, string copyFrom) => world?.CreateTemplate(name, copyFrom);

        public void DeleteTemplate(string name) => world?.DeleteTemplate(name);

        /// <summary>A template was changed: the villages that follow it act on it now.</summary>
        public void TemplateEdited(string name)
        {
            if (world?.HumanPlayer == null) return;
            foreach (var v in new System.Collections.Generic.List<Village>(world.VillagesOf(world.HumanPlayer.Id)))
                if (world.ManagementOf(v.Id)?.Template == name) world.Manage(v);
        }

        // ---------------------------------------------------------------- pause

        bool paused;

        /// <summary>
        /// Whether the world is paused. Only worlds that stand still while the game is closed can be paused: a
        /// real-time world runs on, as the browser games did.
        /// </summary>
        public bool Paused => paused && CanPause;

        public bool CanPause => world != null && world.Settings.TimeMode == TimeMode.PausedWhenClosed;

        public void TogglePause()
        {
            if (!CanPause) return;
            paused = !paused;
            ui.ShowToast(paused ? "Paused. Nothing moves until you resume." : "Resumed.", 2f);
        }

        // ---------------------------------------------------------------- quests and tips

        const string TipsOffKey = "MedievalWorldConquest.TipsOff";
        float nextTipCheck;

        /// <summary>Whether tips are shown (the player's choice in the menu; quests are always on).</summary>
        public bool TipsOn => PlayerPrefs.GetInt(TipsOffKey, 0) == 0;

        public bool ToggleTips()
        {
            PlayerPrefs.SetInt(TipsOffKey, TipsOn ? 1 : 0);
            PlayerPrefs.Save();
            if (!TipsOn) ui.HideTip();
            return TipsOn;
        }

        /// <summary>Claims the current quest's reward.</summary>
        public void ClaimQuest()
        {
            var quest = world?.ClaimQuest();
            if (quest == null) return;
            var r = quest.Reward;
            ui.ShowToast(quest.Title == World.LootAssistantQuest
                ? $"Quest done: {quest.Title}. +{r.Wood:N0} of each resource, and the Loot Assistant is yours: see the Loot tab."
                : $"Quest done: {quest.Title}. +{r.Wood:N0} wood, +{r.Clay:N0} clay, +{r.Iron:N0} iron in {world.PlayerVillage.Name}.", 5f);
            audio.Play(GameAudio.Sound.Coins, 0.5f);
        }

        // ---------------------------------------------------------------- the Loot Assistant

        public void SetLootTemplate(int which, int[] troops)
        {
            if (world == null) return;
            world.SetLootTemplate(which, troops);
            ui.ShowToast($"Template {"ABC"[which]} saved.", 2f);
        }

        /// <summary>A one-click raid from the current village with template A, B or C.</summary>
        public void SendLoot(int targetId, int which)
        {
            if (world == null) return;
            string problem = world.SendLoot(world.PlayerVillage, world.FindVillage(targetId), which);
            if (problem != null) ui.ShowToast(problem, 3f);
        }

        /// <summary>Puts a village in the raid cycle, raided from the current village.</summary>
        public void StartCycle(int targetId, int which)
        {
            if (world == null) return;
            var target = world.FindVillage(targetId);
            string problem = world.StartCycle(world.PlayerVillage, target, which);
            ui.ShowToast(problem ?? $"{target.Name} is in the raid cycle: raided with template {"ABC"[which]} each time its raiders get home.", 3f);
        }

        public void StopCycle(int targetId) => world?.StopCycle(targetId);

        /// <summary>Once a second: the next tip whose moment has come, if tips are on and none is showing.</summary>
        void CheckTips()
        {
            if (Time.unscaledTime < nextTipCheck) return;
            nextTipCheck = Time.unscaledTime + 1f;
            if (!TipsOn || ui.TipShowing || ui.EndScreenOpen) return;
            var tip = world.NextTip();
            if (tip == null) return;
            world.MarkTipShown(tip);
            ui.ShowTip(tip.TextIn(world));
        }

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
