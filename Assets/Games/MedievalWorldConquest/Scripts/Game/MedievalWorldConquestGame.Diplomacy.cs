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
    /// What the UI asks of the game on a diplomacy world: tribes, relations, targets and messages.
    /// </summary>
    public partial class MedievalWorldConquestGame
    {
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

        /// <summary>Brings a lord of the faction's other tribes into the player's tribe (their weakest member swapping out if it's full).</summary>
        public void BringIntoTribe(int playerId)
        {
            if (world == null) return;
            ui.ShowToast(world.BringIntoTribe(world.FindPlayer(playerId)), 4f);
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
    }
}
