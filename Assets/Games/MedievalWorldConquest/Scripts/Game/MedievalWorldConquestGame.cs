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

        // ---------------------------------------------------------------- called by the UI

        public void StartNewWorld(WorldSettings settings, int saveSlot)
        {
            slot = saveSlot;
            world = World.CreateNew(settings);
            ListenForSounds();
            SaveWorld();
            ShowVillage(settings.Seed);
            CreateMap();
            lastAnnouncedReport = 0;
            lastAnnouncedMessage = 0;
            ui.ShowGame(world);
            ui.ShowToast($"Welcome to {world.PlayerVillage.Name}.", 4f);
        }

        public void ContinueWorld(int saveSlot)
        {
            var loaded = SaveFiles.Load(saveSlot, out DateTime savedAt, out string error);
            if (loaded == null)
            {
                ShowStartScreen();
                ui.ShowToast($"That world couldn't be loaded. ({error})", 5f);
                return;
            }

            slot = saveSlot;
            world = loaded;
            double away = SaveGame.CatchUpRealSeconds(world, savedAt, DateTime.UtcNow);
            double before = world.Now;
            world.AdvanceByRealSeconds(away);

            ListenForSounds(); // after catching up: what happened while away doesn't all play at once
            ShowVillage(world.Settings.Seed);
            CreateMap();
            lastAnnouncedReport = world.NextReportId - 1; // reports from the catch-up wait in the Reports tab
            lastAnnouncedMessage = world.NextMessageId - 1;
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

        public void StartResearch(UnitType unit)
        {
            var v = world?.PlayerVillage;
            if (v == null) return;
            if (world.StartResearch(v, unit).Status == ResearchStatus.Ok) ui.ShowToast($"Researching {Units.Get(unit).Name}.");
            else ui.ShowToast($"Can't research {Units.Get(unit).Name} right now.");
        }

        /// <summary>Mints gold coins at the current village's academy (int.MaxValue: as many as it can afford).</summary>
        public void MintCoins(int count)
        {
            var v = world?.PlayerVillage;
            if (v == null) return;
            if (count == int.MaxValue) count = world.CheckMint(v, 1).MaxAffordable;
            var check = world.MintCoins(v, count);
            if (check.Status == MintStatus.Ok)
            {
                audio.Play(GameAudio.Sound.Coins);
                ui.ShowToast($"Minted {count:N0} gold coin{(count == 1 ? "" : "s")}. You have {world.HumanPlayer.Coins:N0}.");
            }
            else ui.ShowToast("Can't mint coins right now.");
        }

        // ---------------------------------------------------------------- tribes and messages (diplomacy worlds)

        /// <summary>Founds a tribe led by the player. Returns what's wrong, or null if it's done.</summary>
        public string FoundTribe(string name, string tag)
        {
            if (world == null || !world.Diplomacy) return "This world has no tribes.";
            if (string.IsNullOrWhiteSpace(name)) return "Give your tribe a name.";
            if (string.IsNullOrWhiteSpace(tag)) return "Give your tribe a short tag, like WOLF.";
            var tribe = world.FoundTribe(world.HumanPlayer, name, tag);
            if (tribe == null) return "The tribe couldn't be founded.";
            ui.ShowToast($"You lead {tribe.Name} [{tribe.Tag}]. Invite lords from their profiles.", 4f);
            return null;
        }

        public void AskToJoin(int tribeId)
        {
            var t = world?.FindTribe(tribeId);
            if (t != null && world.AskToJoin(t)) ui.ShowToast($"You've asked to join {t.Name}. Their leader will answer soon.");
        }

        public void InviteToTribe(int playerId)
        {
            var p = world?.FindPlayer(playerId);
            if (p != null && world.InviteToTribe(p)) ui.ShowToast($"Invitation sent to {p.Name}.");
        }

        public void ExpelFromTribe(int playerId)
        {
            var p = world?.FindPlayer(playerId);
            if (p != null && world.Expel(p)) ui.ShowToast($"{p.Name} is out of the tribe.");
        }

        public void LeaveTribe()
        {
            if (world == null) return;
            world.HumanLeavesTribe();
            ui.ShowToast("You have left your tribe.");
        }

        public void ProposeRelation(int tribeId, RelationKind kind)
        {
            var t = world?.FindTribe(tribeId);
            if (t == null || !world.ProposeRelation(t, kind)) return;
            if (kind == RelationKind.Enemy) ui.ShowToast($"You are at war with {t.Name}.");
            else if (kind == RelationKind.Neutral) ui.ShowToast($"No more agreement with {t.Name}.");
            else ui.ShowToast($"{t.Name} accepted.");
        }

        public void SetTribeTarget(int villageId)
        {
            var v = world?.FindVillage(villageId);
            if (v != null && world.SetTribeTarget(v)) ui.ShowToast($"{v.Name} is your tribe's target.");
        }

        public void ClearTribeTarget()
        {
            var t = world?.TribeOf(world.HumanPlayer);
            if (t != null) t.TargetVillageId = -1;
        }

        public void RequestSupport()
        {
            if (world != null && world.RequestSupport(world.PlayerVillage)) ui.ShowToast($"Your tribe mates have been asked to support {world.PlayerVillage.Name}.");
        }

        public void AnswerMessage(int id, bool yes)
        {
            if (world == null) return;
            var m = world.FindMessage(id);
            bool done = world.AnswerMessage(id, yes);
            if (m != null && yes && m.Kind == MessageKind.Invitation) ui.ShowToast(done ? "You have joined the tribe." : "That invitation no longer stands.");
        }

        public void MarkMessageRead(int id)
        {
            var m = world?.FindMessage(id);
            if (m != null) m.Read = true;
        }

        public void MarkAllMessagesRead() => world?.MarkAllMessagesRead();

        public void DeleteMessage(int id) => world?.DeleteMessage(id);

        int lastAnnouncedMessage;

        /// <summary>Pops up the subject of any message that arrived since last frame.</summary>
        void AnnounceNewMessages()
        {
            int newest = world.NextMessageId - 1;
            if (newest <= lastAnnouncedMessage) return;
            lastAnnouncedMessage = newest;
            var m = world.FindMessage(newest);
            if (m != null) ui.ShowToast($"New message: {m.Subject}", 4f);
        }

        public void CancelResearch(int orderId)
        {
            var v = world?.PlayerVillage;
            if (v != null && world.CancelResearch(v, orderId)) ui.ShowToast("Research canceled. Resources refunded.");
        }

        /// <summary>Sends merchants with resources to the village at a map field. Returns what's wrong, or null if they set off.</summary>
        public string SendResources(int x, int y, Cost goods)
        {
            var v = world?.PlayerVillage;
            if (v == null) return "No village.";
            var status = world.SendResources(v, world.VillageAt(x, y), goods);
            if (status != TradeStatus.Ok) return BuildingText.Trade(status);
            ui.ShowToast($"Merchants sent to ({x}|{y}).");
            return null;
        }

        /// <summary>Puts up a market offer from the current village. Returns what's wrong, or null if it's up.</summary>
        public string PostOffer(ResourceType sell, int sellAmount, ResourceType buy, int buyAmount, int lots)
        {
            var v = world?.PlayerVillage;
            if (v == null) return "No village.";
            var status = world.CheckOffer(v, sell, sellAmount, buy, buyAmount, lots);
            if (status != TradeStatus.Ok) return BuildingText.Trade(status);
            world.PostOffer(v, sell, sellAmount, buy, buyAmount, lots);
            return null;
        }

        public void WithdrawOffer(int offerId)
        {
            if (world != null && world.WithdrawOffer(offerId)) ui.ShowToast("Offer withdrawn. The goods are back in the warehouse.");
        }

        public void AcceptOffer(int offerId, int lots)
        {
            var v = world?.PlayerVillage;
            if (v == null) return;
            var status = world.AcceptOffer(v, offerId, lots);
            ui.ShowToast(status == TradeStatus.Ok ? "Deal! Merchants from both sides are on their way." : BuildingText.Trade(status));
        }

        public void CancelRecruit(int orderId)
        {
            var v = world?.PlayerVillage;
            if (v != null && world.CancelRecruit(v, orderId)) ui.ShowToast("Training canceled. Untrained units refunded.");
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

        /// <summary>Sends a noble train from the current village: one attack per nobleman, landing one after another.</summary>
        public bool SendNobleTrain(int targetVillageId, int[] troops, TrainEscort escort, BuildingType catapultTarget)
        {
            var home = world?.PlayerVillage;
            var target = world?.FindVillage(targetVillageId);
            if (home == null || target == null) return false;
            var train = world.SendTrain(home, target, troops, escort, catapultTarget);
            if (train == null || train.Count == 0)
            {
                ui.ShowToast("Those troops can't be sent.");
                return false;
            }
            ui.ShowToast($"Noble train of {train.Count} attacks sent to {target.Name}. Arrives in {Ui.Real(world, train[0].ArriveTime - world.Now)}.", 4f);
            return true;
        }

        /// <summary>Opens a building's own screen in the village view.</summary>
        public void OpenBuilding(BuildingType type) => ui.OpenBuilding(type);

        /// <summary>Renames the current village (from the Headquarters).</summary>
        public void RenameVillage(string name)
        {
            var v = world?.PlayerVillage;
            if (v == null) return;
            if (world.RenameVillage(v, name)) ui.ShowToast($"Your village is now called {v.Name}.");
            else if (string.IsNullOrWhiteSpace(name)) ui.ShowToast("A village needs a name.");
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
            if (v != null && world.CancelLastBuild(v)) ui.ShowToast("Construction canceled. Resources refunded.");
        }

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
