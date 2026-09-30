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
    /// What the UI asks of the game: starting and continuing worlds, building, training, research, coins, the
    /// market, troops, reports and buildings' screens.
    /// </summary>
    public partial class MedievalWorldConquestGame
    {
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
    }
}
