using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The start screen (the save slots) and the new-world screen.
    /// </summary>
    public partial class GameUI
    {
        /// <summary>
        /// The start screen lists the save slots side by side: each world's summary with Continue and Delete, or
        /// New World for an empty slot. A new world's settings are on a screen of their own.
        /// </summary>
        void BuildStartScreen()
        {
            startScreen = Element("screen", "centered", "dim");
            root.Add(startScreen);
            var panel = Element("panel");
            startScreen.Add(panel);
            panel.Add(Text("MEDIEVAL WORLD CONQUEST", "title"));
            panel.Add(Text("Build a village, raise an army and conquer the realm.", "subtitle"));
            panel.Add(Text("Your worlds", "heading"));
            slotRow = Element("slot-row");
            panel.Add(slotRow);
            var buttons = Element("option-row");
            buttons.style.marginTop = 12;
            buttons.Add(ButtonWith("Main Menu", () => game.LeaveToArcade(), "btn"));
            panel.Add(buttons);

            BuildNewWorldScreen();
        }

        void BuildNewWorldScreen()
        {
            newWorldScreen = Element("screen", "centered", "dim");
            root.Add(newWorldScreen);
            var panel = Element("panel", "new-world-panel");
            newWorldScreen.Add(panel);
            newWorldTitle = Text("New world", "title");
            panel.Add(newWorldTitle);

            // One row per setting: its name on the left, the choices on the right.
            // What the player is called: shown on their villages and in the rankings.
            nameField = new TextField { maxLength = 24, value = World.DefaultPlayerName };
            nameField.AddToClassList("name-field");
            panel.Add(SettingRow("Your name", nameField));

            var speedRow = Element("setting-options");
            foreach (float speed in Speeds)
            {
                var b = ButtonWith(SpeedText(speed), () => ChooseSpeed(speed), "option");
                b.userData = speed;
                speedButtons.Add(b);
                speedRow.Add(b);
            }
            panel.Add(SettingRow("World speed", speedRow));

            var modeRow = Element("setting-options");
            modeButtons.Add(ButtonWith("Real time\nThe world keeps going", () => ChooseMode(TimeMode.RealTime), "option", "option-wide"));
            modeButtons.Add(ButtonWith("Paused\nTime only passes while playing", () => ChooseMode(TimeMode.PausedWhenClosed), "option", "option-wide"));
            modeButtons[0].userData = TimeMode.RealTime;
            modeButtons[1].userData = TimeMode.PausedWhenClosed;
            foreach (var b in modeButtons) modeRow.Add(b);
            panel.Add(SettingRow("When closed", modeRow));

            // How cleverly the rival lords play (how many there are is the world's own).
            var skillRow = Element("setting-options");
            foreach (AiSkill skill in Enum.GetValues(typeof(AiSkill)))
            {
                var b = ButtonWith(skill.ToString(), () => ChooseSkill(skill), "option");
                b.userData = skill;
                skillButtons.Add(b);
                skillRow.Add(b);
            }
            panel.Add(SettingRow("Rival skill", skillRow));

            // How noblemen are paid for: a flat price, or Tribal Wars' gold coins (dearer with every conquest).
            var nobleRow = Element("setting-options");
            nobleButtons.Add(ButtonWith("Flat price\nEvery nobleman costs the same", () => ChooseCoins(false), "option", "option-wide"));
            nobleButtons.Add(ButtonWith("Gold coins\nEach conquest makes the next dearer", () => ChooseCoins(true), "option", "option-wide"));
            nobleButtons[0].userData = false;
            nobleButtons[1].userData = true;
            foreach (var b in nobleButtons) nobleRow.Add(b);
            panel.Add(SettingRow("Noblemen", nobleRow));

            // Every lord for themselves, or a simulated MMO with tribes, pacts and wars.
            var diplomacyRow = Element("setting-options");
            diplomacyButtons.Add(ButtonWith("Free-for-all\nEvery lord for themselves", () => ChooseDiplomacy(false), "option", "option-wide"));
            diplomacyButtons.Add(ButtonWith("Tribes\nPacts, wars, and blocs that can win", () => ChooseDiplomacy(true), "option", "option-wide"));
            diplomacyButtons[0].userData = false;
            diplomacyButtons[1].userData = true;
            foreach (var b in diplomacyButtons) diplomacyRow.Add(b);
            panel.Add(SettingRow("Diplomacy", diplomacyRow));

            var buttons = Element("option-row");
            buttons.style.marginTop = 14;
            buttons.Add(ButtonWith("Start World", OnStartNewWorld, "btn"));
            buttons.Add(ButtonWith("Back", () => Show(newWorldScreen, false), "btn"));
            panel.Add(buttons);
            Show(newWorldScreen, false);

            ChooseSpeed(chosenSpeed);
            ChooseMode(chosenMode);
            ChooseSkill(chosenSkill);
            ChooseCoins(chosenCoins);
            ChooseDiplomacy(chosenDiplomacy);
        }

        /// <summary>A setting on the new-world screen: its name on the left, its choices on the right.</summary>
        static VisualElement SettingRow(string label, VisualElement choices)
        {
            var row = Element("setting-row");
            row.Add(Text(label, "body-text", "setting-label"));
            row.Add(choices);
            return row;
        }

        void ChooseDiplomacy(bool on)
        {
            chosenDiplomacy = on;
            foreach (var b in diplomacyButtons) b.EnableInClassList("option--selected", (bool)b.userData == on);
        }

        void ChooseCoins(bool coins)
        {
            chosenCoins = coins;
            foreach (var b in nobleButtons) b.EnableInClassList("option--selected", (bool)b.userData == coins);
        }

        void ChooseSkill(AiSkill skill)
        {
            chosenSkill = skill;
            foreach (var b in skillButtons) b.EnableInClassList("option--selected", (AiSkill)b.userData == skill);
        }

        void ChooseSpeed(float speed)
        {
            chosenSpeed = speed;
            foreach (var b in speedButtons) b.EnableInClassList("option--selected", (float)b.userData == speed);
        }

        void ChooseMode(TimeMode mode)
        {
            chosenMode = mode;
            foreach (var b in modeButtons) b.EnableInClassList("option--selected", (TimeMode)b.userData == mode);
        }

        /// <summary>Opens the new-world screen for an empty slot.</summary>
        void OpenNewWorld(int saveSlot)
        {
            newWorldSlot = saveSlot;
            SetText(newWorldTitle, $"New world  ·  slot {saveSlot + 1}");
            Show(newWorldScreen, true);
        }

        void OnStartNewWorld()
        {
            var settings = new WorldSettings
            {
                Speed = chosenSpeed,
                TimeMode = chosenMode,
                Seed = Environment.TickCount,
                RivalSkill = chosenSkill,
                PlayerName = nameField.value,
                GoldCoins = chosenCoins,
                Diplomacy = chosenDiplomacy,
                ConquestGoal = WorldSettings.StandardGoal,
            };
            Show(newWorldScreen, false);
            game.StartNewWorld(settings, newWorldSlot);
        }

        /// <param name="summaries">Each save slot's world (null: empty).</param>
        /// <param name="errors">For each slot: why its save couldn't be read, if it couldn't.</param>
        public void ShowStart(SaveSummary[] summaries, string[] errors)
        {
            Show(startScreen, true);
            Show(newWorldScreen, false);
            Show(hud, false);
            Show(menu, false);

            slotRow.Clear();
            for (int i = 0; i < summaries.Length; i++)
            {
                int index = i;
                var s = summaries[i];
                var card = Element("slot-card");
                card.Add(Text($"World {i + 1}", "row-title", "slot-title"));
                var body = Element("slot-body");
                if (s != null)
                {
                    string state = s.Won ? "  ·  won" : s.Lost ? "  ·  the world has ended" : "";
                    body.Add(Text($"{s.PlayerName}  ·  {s.VillageName}", "slot-line", "slot-line--strong"));
                    body.Add(Text($"Day {s.Day}{state}", "slot-line"));
                    body.Add(Text($"{s.Villages:N0} {(s.Villages == 1 ? "village" : "villages")}  ·  {s.Points:N0} points" +
                                  (s.Rank > 0 ? $"  ·  rank {s.Rank:N0} of {s.Lords:N0}" : ""), "slot-line"));
                    body.Add(Text($"{SpeedText(s.Speed)}  ·  {ModeText(s.TimeMode)}  ·  {s.Skill} rivals", "slot-line"));
                    body.Add(Text($"{(s.GoldCoins ? "Gold coins" : "Flat-price noblemen")}  ·  {(s.Diplomacy ? "Tribes" : "Free-for-all")}", "slot-line"));
                    body.Add(Text($"Saved {new DateTime(s.SavedAtUtcTicks, DateTimeKind.Utc).ToLocalTime():g}", "slot-line", "slot-line--faint"));
                }
                else if (!string.IsNullOrEmpty(errors[i]))
                    body.Add(Text($"This world couldn't be read. ({errors[i]})", "slot-line", "error-text"));
                else
                    body.Add(Text("Empty", "slot-line", "slot-line--faint"));
                card.Add(body);

                var actions = Element("slot-actions");
                if (s != null)
                {
                    actions.Add(ButtonWith("Continue", () => game.ContinueWorld(index), "btn", "btn--small"));
                    actions.Add(ButtonWith("Delete", () => AskToConfirm($"Delete world {index + 1} for good? This can't be undone.", () => game.DeleteWorld(index)), "btn", "btn--small", "slot-delete"));
                }
                else if (!string.IsNullOrEmpty(errors[i]))
                    actions.Add(ButtonWith("Delete", () => AskToConfirm($"Delete the unreadable world {index + 1}?", () => game.DeleteWorld(index)), "btn", "btn--small", "slot-delete"));
                else
                    actions.Add(ButtonWith("New World", () => OpenNewWorld(index), "btn", "btn--small"));
                card.Add(actions);
                slotRow.Add(card);
            }
        }
    }
}
