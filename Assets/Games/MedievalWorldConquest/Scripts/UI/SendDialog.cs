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
    /// sways the village), landing one straight after another. Everything is always there (units that aren't home,
    /// and the catapult and noble train options when they don't apply, grayed out), so nothing moves as troops are chosen.
    /// </summary>
    public class SendDialog
    {
        public VisualElement Root { get; }
        public bool IsOpen => Root.style.display == DisplayStyle.Flex;

        readonly MedievalWorldConquestGame game;
        readonly int[] selected = new int[Units.Count];
        readonly VisualElement[] rows = new VisualElement[Units.Count];
        readonly IntegerField[] amounts = new IntegerField[Units.Count];
        readonly Button[] allLinks = new Button[Units.Count];
        readonly Label title, summary, reason;
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
            quick.Add(ButtonWith("None", () => Array.Clear(selected, 0, selected.Length), "btn", "btn--small", "count-btn"));
            panel.Add(quick);


            // As in Tribal Wars: for each unit, a box to type how many, and links for all of them or none; in two
            // columns, foot and siege on the left, horse and noblemen on the right.
            var grid = Element("send-grid");
            var columns = new[] { Element("send-column"), Element("send-column") };
            grid.Add(columns[0]);
            grid.Add(columns[1]);
            panel.Add(grid);
            foreach (var type in Units.InDisplayOrder)
            {
                var u = Units.Get(type);
                int index = (int)type;
                var row = Element("send-row");
                row.Add(Icons.Element(Icons.Unit(type), 20, "send-icon"));
                row.Add(Text(u.Name, "row-title", "send-name"));
                var field = new IntegerField { value = 0 };
                field.AddToClassList("amount-field");
                field.AddToClassList("send-amount");
                field.RegisterValueChangedCallback(e => selected[index] = Math.Max(0, e.newValue));
                amounts[index] = field;
                row.Add(field);
                allLinks[index] = Link("", () => selected[index] = int.MaxValue, "send-all");
                allLinks[index].tooltip = "Send them all";
                row.Add(allLinks[index]);
                row.Add(Link("none", () => selected[index] = 0, "send-none"));
                // One more (a lone scout for a raid, say) without typing; never more than are home.
                row.Add(Link("+1", () => selected[index] = selected[index] == int.MaxValue ? int.MaxValue : selected[index] + 1, "send-plus"));
                rows[index] = row;
                bool horse = type == UnitType.Scout || type == UnitType.LightCavalry || type == UnitType.MountedArcher
                             || type == UnitType.HeavyCavalry || type == UnitType.Nobleman;
                columns[horse ? 1 : 0].Add(row);
            }

            // Which building the catapults aim at (grayed out unless catapults are going).
            catapultRow = Element("send-row", "catapult-row");
            catapultRow.Add(Text("Catapults aim at", "row-title", "send-name"));
            var names = new List<string>();
            foreach (var d in Buildings.Definitions) names.Add(d.Name);
            catapultTarget = new DropdownField(names, (int)BuildingType.Headquarters);
            catapultTarget.AddToClassList("catapult-target");
            catapultRow.Add(catapultTarget);
            panel.Add(catapultRow);

            // How a noble train shares out the troops (grayed out unless two or more noblemen are going).
            trainRow = Element("send-row", "catapult-row");
            trainRow.Add(Text("Noble train", "row-title", "send-name"));
            trainEscort = new DropdownField(new List<string> { "Minimal escort", "Troops split evenly" }, 0);
            trainEscort.AddToClassList("catapult-target");
            trainEscort.tooltip = "Only one nobleman in an attack sways a village, so noblemen go as a train: one attack each, landing a split second apart. The first, with most of the army, clears the village; each nobleman after it takes just enough troops to survive the emptied village (or, if you prefer, the troops are split evenly).";
            trainRow.Add(trainEscort);
            panel.Add(trainRow);

            summary = Text("", "row-info", "send-summary");
            panel.Add(summary);
            reason = Text("", "row-reason", "send-reason");
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

        void SelectAll()
        {
            for (int i = 0; i < selected.Length; i++) selected[i] = int.MaxValue; // clamped to what's home on refresh
        }

        /// <summary>
        /// When the next attack the player knows of lands on a village: on their own, the attacks they can see
        /// coming; on a tribe mate's, the ones the tribe has been asked for help against. Null if none.
        /// </summary>
        static double? NextAttackOn(World world, Village target)
        {
            var human = world.HumanPlayer;
            if (human == null) return null;
            double? first = null;
            if (target.OwnerId == human.Id)
            {
                foreach (var c in world.IncomingAttacks(human.Id))
                    if (c.ToVillageId == target.Id && (first == null || c.ArriveTime < first)) first = c.ArriveTime;
                return first;
            }
            var tribe = world.TribeOf(human);
            if (tribe == null) return null;
            foreach (var call in tribe.HelpCalls)
                if (call.VillageId == target.Id && call.ArriveTime > world.Now && (first == null || call.ArriveTime < first)) first = call.ArriveTime;
            return first;
        }

        void Send(CommandKind kind)
        {
            var aim = (BuildingType)Math.Max(0, catapultTarget.index);
            bool train = kind == CommandKind.Attack && IsTrain;
            if (train ? game.SendNobleTrain(targetId, (int[])selected.Clone(), (TrainEscort)Math.Max(0, trainEscort.index), aim)
                      : game.SendTroops(targetId, (int[])selected.Clone(), kind, aim)) Close();
        }

        bool IsTrain => selected[(int)UnitType.Nobleman] >= 2;

        /// <summary>For support to a village under attack: whether it gets there before the next attack does.</summary>
        static string InTimeLine(World world, Village target, SendCheck support)
        {
            var lands = NextAttackOn(world, target);
            if (lands == null || support.Status != SendStatus.Ok) return "";
            double left = lands.Value - world.Now, margin = left - support.Seconds;
            return $"\nAn attack lands there in {Real(world, left)}: support sent now " +
                   (margin >= 0 ? $"gets there {Real(world, margin)} before it." : $"would be {Real(world, -margin)} too late. Send faster units, or from nearer.");
        }

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
                selected[i] = Math.Max(0, Math.Min(selected[i], atHome)); // never more than are home
                rows[i].SetEnabled(atHome > 0);
                any |= atHome > 0;
                SetText(allLinks[i], $"({atHome:N0})");
                // What's typed stays as typed (unless it's more than there are); the links and buttons show here.
                if (amounts[i].value != selected[i]) amounts[i].SetValueWithoutNotify(selected[i]);
            }

            var attackCheck = world.CheckSend(home, target, selected, CommandKind.Attack);
            var supportCheck = world.CheckSend(home, target, selected, CommandKind.Support);
            if (attackCheck.Status == SendStatus.NoTroops)
                SetText(summary, $"Distance: {World.Distance(home, target):0.0} fields. Choose some troops.");
            else
                SetText(summary,
                    $"Distance {World.Distance(home, target):0.0} fields  ·  pace of the slowest: {Units.Get(attackCheck.Slowest).Name}\n" +
                    $"Travel time {Real(world, attackCheck.Seconds)}  ·  arrives {World.FormatClock(world.Now + attackCheck.Seconds)}\n" +
                    $"Attack strength {attackCheck.Attack:N0}  ·  can carry {attackCheck.Carry:N0} loot" +
                    (target.OwnerId != home.OwnerId && IsTrain ? TrainLine(world, home, target) : "") +
                    InTimeLine(world, target, supportCheck));

            bool own = target.OwnerId == home.OwnerId;
            trainRow.SetEnabled(!own && selected[(int)UnitType.Nobleman] >= 2);
            Show(attack, !own);
            attack.SetEnabled(attackCheck.Status == SendStatus.Ok);
            support.SetEnabled(supportCheck.Status == SendStatus.Ok);
            catapultRow.SetEnabled(!own && selected[(int)UnitType.Catapult] > 0);
            var lord = world.FindPlayer(target.OwnerId);
            bool friendly = !own && world.AreFriendly(home.OwnerId, target.OwnerId);
            bool mate = friendly && world.TribeOf(home.OwnerId) == world.TribeOf(target.OwnerId);
            SetText(reason,
                !any ? "You have no troops at home to send."
                : attackCheck.Status == SendStatus.SameVillage ? "That's the village they're in."
                : friendly && attackCheck.Status == SendStatus.Ok
                    ? mate ? "Careful: this is a tribe mate. Attacking gets you thrown out of the tribe."
                           : "Careful: your tribes have a pact. Attacking breaks it and means war."
                : !own && attackCheck.Status == SendStatus.TargetProtected && lord != null
                    ? $"{lord.Name} is under beginner protection for another {Real(world, lord.ProtectedUntil - world.Now)}. You can still send support."
                : "");
        }
    }
}
