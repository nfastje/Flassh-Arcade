using System;
using System.Collections;
using System.Collections.Generic;
using FlasshArcade;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace MedievalWorldConquest
{
    /// <summary>
    /// What the UI asks of the game: starting and continuing worlds, building, training, research, coins, the
    /// market, troops, reports and buildings' screens.
    /// </summary>
    public partial class MedievalWorldConquestGame
    {
        public void StartNewWorld(WorldSettings settings, int saveSlot)
        {
            slot = saveSlot;
            paused = false;
            world = World.CreateNew(settings);
            LoadTroopTemplates();
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
            paused = false;
            world = loaded;
            LoadTroopTemplates(); // (before catching up: the Account Manager works through the time away too)
            double away = SaveGame.CatchUpRealSeconds(world, savedAt, DateTime.UtcNow);
            double before = world.Now;
            StartCoroutine(CatchUp(away, () =>
            {
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
            }));
        }

        /// <summary>Game seconds simulated between looks at the clock while catching up.</summary>
        const double CatchUpStep = 3600;

        /// <summary>Real seconds spent simulating per frame while catching up; the rest of the frame draws the progress.</summary>
        const double CatchUpFrameBudget = 0.1;

        /// <summary>A frame longer than this (real seconds: the game was in the background) catches up like a load does.</summary>
        const float CatchUpGap = 2f;

        /// <summary>Whether the world is catching up on time spent away (the game waits; nothing is saved meanwhile).</summary>
        bool catchingUp;

        /// <summary>
        /// Runs the world forward by <paramref name="awayRealSeconds"/> a slice per frame, so the screen stays alive.
        /// If it takes more than one slice, a popup shows how far it has got. Then <paramref name="done"/> runs. Saving
        /// waits until it's over: the save on disk is from before, so closing the game meanwhile loses nothing.
        /// </summary>
        IEnumerator CatchUp(double awayRealSeconds, Action done)
        {
            catchingUp = true;
            // (While the player was away, nobody expects them to answer calls for help.)
            world.PlayerAway = true;
            double start = world.Now, target = start + Math.Max(0, awayRealSeconds) * world.Settings.Speed;
            int days = Math.Max(1, (int)Math.Ceiling((target - start) / World.SecondsPerDay));
            var clock = new System.Diagnostics.Stopwatch();
            bool showing = false;
            while (true)
            {
                clock.Restart();
                while (world.Now < target && clock.Elapsed.TotalSeconds < CatchUpFrameBudget)
                    world.AdvanceTo(Math.Min(target, world.Now + CatchUpStep));
                if (world.Now >= target) break;

                if (!showing)
                {
                    showing = true;
                    ui.ShowCatchUp(world.HumanPlayer?.Name ?? "You", awayRealSeconds);
                }
                int day = Math.Min(days, (int)((world.Now - start) / World.SecondsPerDay) + 1);
                ui.ShowCatchUpProgress(day, days, (world.Now - start) / (target - start));
                yield return null;
            }
            world.PlayerAway = false;
            catchingUp = false;
            ui.HideCatchUp();
            done?.Invoke();
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

        /// <summary>Opens the Recruit screen: every unit the village's training buildings train.</summary>
        public void OpenRecruit() => ui.OpenRecruit();

        /// <summary>Renames the current village (from the Headquarters).</summary>
        public void RenameVillage(string name)
        {
            var v = world?.PlayerVillage;
            if (v != null) RenameVillage(v.Id, name);
        }

        /// <summary>Renames one of the player's villages (from the Account Manager).</summary>
        public void RenameVillage(int villageId, string name)
        {
            var v = world?.FindVillage(villageId);
            if (v == null) return;
            if (world.RenameVillage(v, name)) ui.ShowToast($"Your village is now called {v.Name}.");
            else if (string.IsNullOrWhiteSpace(name)) ui.ShowToast("A village needs a name.");
        }

        /// <summary>Calls the player's support troops stationed in another village back home (from the rally point they came from).</summary>
        public void Recall(int hostVillageId, int fromVillageId)
        {
            var host = world?.FindVillage(hostVillageId);
            var group = host?.Supports.Find(g => g.FromVillageId == fromVillageId);
            if (group == null || group.OwnerId != world.HumanPlayer?.Id) return;
            if (world.Recall(host, fromVillageId) != null) ui.ShowToast("Support recalled. The troops are heading home.");
        }

        /// <summary>Sends support stationed in one of the player's villages (theirs or anyone's) back where it came from.</summary>
        public void SendSupportHome(int hostVillageId, int fromVillageId)
        {
            var host = world?.FindVillage(hostVillageId);
            if (host == null || host.OwnerId != world.HumanPlayer?.Id) return;
            var from = world.FindVillage(fromVillageId);
            if (world.Recall(host, fromVillageId) != null) ui.ShowToast($"The support from {from?.Name ?? "elsewhere"} is heading home.");
        }

        public void MarkReportRead(int id)
        {
            var report = world?.FindReport(id);
            if (report != null) report.Read = true;
        }

        public void MarkAllReportsRead(bool archive) => world?.MarkAllReportsRead(archive);

        public void DeleteReports(ICollection<int> ids) => world?.DeleteReports(ids);

        public void ArchiveReports(ICollection<int> ids) => world?.ArchiveReports(ids);

        public void CancelLastBuild()
        {
            var v = world?.PlayerVillage;
            if (v != null && world.CancelLastBuild(v)) ui.ShowToast("Construction canceled. Resources refunded.");
        }
    }
}
