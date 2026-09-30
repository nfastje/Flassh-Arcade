using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The dialog for sending troops from the player's village to another: pick how many of each unit, see how long
    /// the march takes and how strong the attack is, then attack (or support one of the player's own villages).
    /// With two noblemen or more, an attack goes as a noble train: one attack per nobleman (only one in an attack
    /// sways the village), landing one straight after another.
    /// </summary>
    public class SendDialog
    {
        public VisualElement Root { get; }
        public bool IsOpen => Root.style.display == DisplayStyle.Flex;

        readonly MedievalWorldConquestGame game;
        readonly int[] selected = new int[Units.Count];
        readonly VisualElement[] rows = new VisualElement[Units.Count];
        readonly Label[] counts = new Label[Units.Count];
        readonly Label[] available = new Label[Units.Count];
        readonly Label title, summary, reason, nothingHome;
        readonly Button attack, support;
        readonly VisualElement catapultRow, trainRow;
        readonly DropdownField catapultTarget, trainEscort;
        int targetId;

        public SendDialog(MedievalWorldConquestGame game)
        {
            this.game = game;
            Root = Element("screen", "centered", "dim");
            var panel = Element("panel", "send-panel");
            Root.Add(panel);

            title = Text("", "heading");
            panel.Add(title);

            var quick = Element("option-row");
            quick.Add(ButtonWith("All troops", () => SelectAll(), "btn", "btn--small", "count-btn"));
            quick.Add(ButtonWith("Clear", () => Array.Clear(selected, 0, selected.Length), "btn", "btn--small", "count-btn"));
            panel.Add(quick);

            nothingHome = Text("You have no troops at home to send.", "row-reason");
            panel.Add(nothingHome);

            foreach (var type in Units.InDisplayOrder)
            {
                var u = Units.Get(type);
                var row = Element("send-row");
                row.Add(Icons.Element(Icons.Unit(type), 20, "send-icon"));
                row.Add(Text(u.Name, "row-title", "send-name"));
                available[(int)type] = Text("", "row-level", "send-available");
                row.Add(available[(int)type]);
                row.Add(ButtonWith("-10", () => Adjust(type, -10), "btn", "btn--small", "count-btn"));
                row.Add(ButtonWith("-1", () => Adjust(type, -1), "btn", "btn--small", "count-btn"));
                counts[(int)type] = Text("0", "unit-count");
                row.Add(counts[(int)type]);
                row.Add(ButtonWith("+1", () => Adjust(type, 1), "btn", "btn--small", "count-btn"));
                row.Add(ButtonWith("+10", () => Adjust(type, 10), "btn", "btn--small", "count-btn"));
                row.Add(ButtonWith("All", () => selected[(int)type] = int.MaxValue, "btn", "btn--small", "count-btn"));
                rows[(int)type] = row;
                panel.Add(row);
            }

            // Which building the catapults aim at (only asked when catapults are going).
            catapultRow = Element("send-row", "catapult-row");
            catapultRow.Add(Text("Catapults aim at", "row-title", "send-name"));
            var names = new List<string>();
            foreach (var d in Buildings.Definitions) names.Add(d.Name);
            catapultTarget = new DropdownField(names, (int)BuildingType.Headquarters);
            catapultTarget.AddToClassList("catapult-target");
            catapultRow.Add(catapultTarget);
            panel.Add(catapultRow);

            // How a noble train shares out the troops (only asked when two or more noblemen are going).
            trainRow = Element("send-row", "catapult-row");
            trainRow.Add(Text("Noble train", "row-title", "send-name"));
            trainEscort = new DropdownField(new List<string> { "Minimal escort", "Troops split evenly" }, 0);
            trainEscort.AddToClassList("catapult-target");
            trainEscort.tooltip = "Only one nobleman in an attack sways a village, so noblemen go as a train: one attack each, landing a split second apart. The first, with most of the army, clears the village; each nobleman after it takes just enough troops to survive the emptied village (or, if you prefer, the troops are split evenly).";
            trainRow.Add(trainEscort);
            panel.Add(trainRow);

            summary = Text("", "row-info", "send-summary");
            panel.Add(summary);
            reason = Text("", "row-reason");
            panel.Add(reason);

            var actions = Element("option-row");
            attack = ButtonWith("Attack", () => Send(CommandKind.Attack), "btn");
            support = ButtonWith("Support", () => Send(CommandKind.Support), "btn");
            actions.Add(attack);
            actions.Add(support);
            actions.Add(ButtonWith("Cancel", Close, "btn"));
            panel.Add(actions);

            Show(Root, false);
        }

        public void Open(int targetVillageId)
        {
            targetId = targetVillageId;
            Array.Clear(selected, 0, selected.Length);
            Show(Root, true);
        }

        public void Close() => Show(Root, false);

        void Adjust(UnitType type, int delta)
        {
            long value = (long)selected[(int)type] + delta;
            selected[(int)type] = (int)Math.Max(0, Math.Min(int.MaxValue, value));
        }

        void SelectAll()
        {
            for (int i = 0; i < selected.Length; i++) selected[i] = int.MaxValue; // clamped to what's home on refresh
        }

        void Send(CommandKind kind)
        {
            var aim = (BuildingType)Math.Max(0, catapultTarget.index);
            bool train = kind == CommandKind.Attack && IsTrain;
            if (train ? game.SendNobleTrain(targetId, (int[])selected.Clone(), (TrainEscort)Math.Max(0, trainEscort.index), aim)
                      : game.SendTroops(targetId, (int[])selected.Clone(), kind, aim)) Close();
        }

        bool IsTrain => selected[(int)UnitType.Nobleman] >= 2;

        /// <summary>What the train looks like: how many attacks, and how strong the first one is.</summary>
        string TrainLine(World world, Village home, Village target)
        {
            var waves = World.SplitTrain(selected, (TrainEscort)Math.Max(0, trainEscort.index), target.Level(BuildingType.Wall));
            var first = world.CheckSend(home, target, waves[0], CommandKind.Attack);
            var escort = new List<string>();
            for (int i = 0; i < Units.Count; i++)
                if (i != (int)UnitType.Nobleman && waves[1][i] > 0) escort.Add($"{waves[1][i]:N0} {Units.Get((UnitType)i).Name}");
            return $"\nNoble train: {waves.Count} attacks landing a split second apart  ·  the first attacks with {first.Attack:N0}" +
                   $"\nEach nobleman after it takes {(escort.Count == 0 ? "no escort" : string.Join(", ", escort))}";
        }

        public void Refresh(World world)
        {
            if (!IsOpen) return;
            var home = world.PlayerVillage;
            var target = world.FindVillage(targetId);
            if (home == null || target == null)
            {
                Close();
                return;
            }

            SetText(title, $"Send troops to {target.Name} ({target.X}|{target.Y})");
            bool any = false;
            for (int i = 0; i < Units.Count; i++)
            {
                int atHome = home.TroopCount((UnitType)i);
                selected[i] = Math.Min(selected[i], atHome); // never more than are home
                Show(rows[i], atHome > 0);
                any |= atHome > 0;
                SetText(available[i], $"{atHome:N0} home");
                SetText(counts[i], selected[i].ToString("N0"));
            }
            Show(nothingHome, !any);

            var attackCheck = world.CheckSend(home, target, selected, CommandKind.Attack);
            var supportCheck = world.CheckSend(home, target, selected, CommandKind.Support);
            if (attackCheck.Status == SendStatus.NoTroops)
                SetText(summary, $"Distance: {World.Distance(home, target):0.0} fields. Choose some troops.");
            else
                SetText(summary,
                    $"Distance {World.Distance(home, target):0.0} fields  ·  pace of the slowest: {Units.Get(attackCheck.Slowest).Name}\n" +
                    $"Travel time {Real(world, attackCheck.Seconds)}  ·  arrives {World.FormatClock(world.Now + attackCheck.Seconds)}\n" +
                    $"Attack strength {attackCheck.Attack:N0}  ·  can carry {attackCheck.Carry:N0} loot" +
                    (target.OwnerId != home.OwnerId && IsTrain ? TrainLine(world, home, target) : ""));

            bool own = target.OwnerId == home.OwnerId;
            Show(trainRow, !own && selected[(int)UnitType.Nobleman] >= 2);
            Show(attack, !own);
            attack.SetEnabled(attackCheck.Status == SendStatus.Ok);
            support.SetEnabled(supportCheck.Status == SendStatus.Ok);
            Show(catapultRow, !own && selected[(int)UnitType.Catapult] > 0);
            var lord = world.FindPlayer(target.OwnerId);
            bool friendly = !own && world.AreFriendly(home.OwnerId, target.OwnerId);
            bool mate = friendly && world.TribeOf(home.OwnerId) == world.TribeOf(target.OwnerId);
            SetText(reason,
                attackCheck.Status == SendStatus.SameVillage ? "That's the village they're in."
                : friendly && attackCheck.Status == SendStatus.Ok
                    ? mate ? "Careful: this is a tribe mate. Attacking gets you thrown out of the tribe."
                           : "Careful: your tribes have a pact. Attacking breaks it and means war."
                : !own && attackCheck.Status == SendStatus.TargetProtected && lord != null
                    ? $"{lord.Name} is under beginner protection for another {Real(world, lord.ProtectedUntil - world.Now)}. You can still send support."
                : "");
        }
    }
}
