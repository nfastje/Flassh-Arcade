using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The menu, confirmations, toasts, and the victory and defeat screens.
    /// </summary>
    public partial class GameUI
    {
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
            // (Only once there are sounds, or music, to switch: see GameAudio.)
            if (game.HasSounds) panel.Add(sound);
            Button music = null;
            music = ButtonWith(game.MusicOn ? "Music: On" : "Music: Off", () => music.text = game.ToggleMusic() ? "Music: On" : "Music: Off", "btn");
            if (game.HasMusic) panel.Add(music);
            // Tips can be switched off; the quests are always there.
            tipsButton = ButtonWith("", () => tipsButton.text = game.ToggleTips() ? "Tips: On" : "Tips: Off", "btn");
            panel.Add(tipsButton);
            panel.Add(ButtonWith("Main Menu", () => game.LeaveToArcade(), "btn"));
            menu.Add(panel);
            root.Add(menu);
            Show(menu, false);
        }

        Button tipsButton;

        public void ToggleMenu()
        {
            // (Tips can also be switched off from a tip itself.)
            tipsButton.text = game.TipsOn ? "Tips: On" : "Tips: Off";
            Show(menu, !MenuOpen);
        }

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
                  $"past the {world.Settings.ConquestGoal:P0} you needed. The realm is yours!\nResume to carry on: the world goes on as before."
                : $"{tribe.Name} [{tribe.Tag}] and its allies hold {world.BlocShare(tribe):P0} of the {world.GoalVillagesLabel}, past the {world.Settings.ConquestGoal:P0} " +
                  "needed. The realm is yours, and your allies', together!\nResume to carry on: the world goes on as before.",
                "Resume", null, "Main Menu", () => game.LeaveToArcade());
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
                "Resume to carry on: the world goes on as before.", "Resume", null, "Main Menu", () => game.LeaveToArcade());
            ShowStandings(world, winner);
        }

        /// <summary>The player lost their last village.</summary>
        public void ShowDefeat(World world) => ShowEndScreen("DEFEATED",
            "Your last village has fallen. But a lord is more than their lands: start again with a new village on the " +
            "frontier of the realm, under fresh beginner protection.",
            "Rebuild on the Frontier", () => game.RespawnPlayer(), "Main Menu", () => game.LeaveToArcade());

        // ---------------------------------------------------------------- tips

        VisualElement tipBanner;
        Label tipText;

        /// <summary>A tip: a banner under the top bar until the player has read it.</summary>
        void BuildTip()
        {
            var layer = Element("tip-layer");
            layer.pickingMode = PickingMode.Ignore;
            tipBanner = Element("tip-banner");
            tipBanner.Add(Text("Tip", "tip-title"));
            tipText = Text("", "tip-text");
            tipBanner.Add(tipText);
            var row = Element("option-row", "tip-actions");
            row.Add(ButtonWith("Got it", HideTip, "btn", "btn--small"));
            row.Add(Link("Turn tips off", () => game.ToggleTips(), "tip-off"));
            tipBanner.Add(row);
            layer.Add(tipBanner);
            hud.Add(layer);
            Show(tipBanner, false);
        }

        public bool TipShowing => tipBanner.style.display == DisplayStyle.Flex;

        public void ShowTip(string text)
        {
            tipText.text = text;
            Show(tipBanner, true);
        }

        public void HideTip() => Show(tipBanner, false);

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
