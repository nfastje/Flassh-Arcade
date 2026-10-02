using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The rally point: the troops at home and their strength, sending troops to any map field, the support
    /// stationed in this village (which can be sent home), every troop movement (attacks, support, returns, incoming
    /// attacks), and this village's troops supporting other villages (which can be recalled).
    /// </summary>
    class RallyPointView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly Func<int, int, bool> sendTo;
        readonly Label[] homeCounts = new Label[Units.Count];
        readonly Label strength, sendMessage;
        readonly IntegerField targetX, targetY;
        readonly ScrollView movements;
        readonly VisualElement picker, pickerList;
        World lastWorld;
        readonly VisualElement hosted;
        readonly Label hostedHeading;
        readonly UiLinks links;
        readonly List<MovementRow> rows = new List<MovementRow>();
        string movementsSignature;

        public RallyPointView(MedievalWorldConquestGame game, Func<int, int, bool> sendTo, UiLinks links)
        {
            this.game = game;
            this.sendTo = sendTo;
            this.links = links;
            Root = Element("window-section");

            Root.Add(Text("Troops at home", "heading"));
            var grid = Element("troop-grid");
            foreach (var type in Units.InDisplayOrder)
            {
                int i = (int)type;
                var cell = Element("troop-grid-cell");
                cell.Add(Icons.Element(Icons.Unit(type), 22));
                homeCounts[i] = Text("", "garrison-count");
                cell.Add(homeCounts[i]);
                cell.tooltip = Units.Get(type).Name;
                grid.Add(cell);
            }
            Root.Add(grid);
            strength = Text("", "row-info");
            Root.Add(strength);

            Root.Add(Text("Send troops", "heading"));
            var send = Element("send-to-row");
            send.Add(Text("Target field", "row-title"));
            targetX = new IntegerField { value = World.MapSize / 2 };
            targetY = new IntegerField { value = World.MapSize / 2 };
            targetX.AddToClassList("coord-field");
            targetY.AddToClassList("coord-field");
            send.Add(targetX);
            send.Add(Text("|", "row-title"));
            send.Add(targetY);
            send.Add(ButtonWith("Choose troops", () =>
            {
                if (!sendTo(targetX.value, targetY.value)) SetText(sendMessage, $"There's no village at ({targetX.value}|{targetY.value}).");
                else SetText(sendMessage, "");
            }, "btn", "btn--small"));
            Button choose = null;
            choose = ButtonWith("Choose village", () => TogglePicker(choose), "btn", "btn--small", "rally-choose-village");
            send.Add(choose);
            Root.Add(send);

            // A small window over the screen listing the player's villages: picking one fills in the target field.
            picker = Element("village-picker");
            var header = Element("manager-popup-header");
            header.Add(Text("Your villages", "manager-popup-title"));
            header.Add(ButtonWith("✕", () => Show(picker, false), "btn", "btn--small", "manager-popup-close"));
            picker.Add(header);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("village-picker-scroll");
            pickerList = Element();
            scroll.Add(pickerList);
            picker.Add(scroll);
            Show(picker, false);
            sendMessage = Text("You can also pick a village on the Map and choose Attack or Support.", "row-info");
            Root.Add(sendMessage);

            // Support from elsewhere stationed here: who sent it, and a button to send it home.
            hostedHeading = Text("Support in this village", "heading");
            Root.Add(hostedHeading);
            hosted = Element();
            Root.Add(hosted);

            Root.Add(Text("Troop movements", "heading"));
            movements = new ScrollView(ScrollViewMode.Vertical);
            movements.AddToClassList("movements");
            Root.Add(movements);
            Root.Add(picker); // last, so it's drawn over the rest
        }

        /// <summary>Opens (just under its button) or closes the list of the player's villages, by name.</summary>
        void TogglePicker(VisualElement by)
        {
            if (picker.style.display == DisplayStyle.Flex || lastWorld == null)
            {
                Show(picker, false);
                return;
            }
            pickerList.Clear();
            var here = lastWorld.PlayerVillage;
            foreach (var v in lastWorld.HumanVillagesByName())
            {
                int x = v.X, y = v.Y;
                var row = Link($"{v.Name} ({v.X}|{v.Y})", () =>
                {
                    targetX.value = x;
                    targetY.value = y;
                    SetText(sendMessage, "");
                    Show(picker, false);
                }, "village-picker-item");
                if (v == here)
                {
                    row.text += "  (this village)";
                    row.SetEnabled(false);
                }
                pickerList.Add(row);
            }
            var at = Root.WorldToLocal(by.worldBound);
            picker.style.left = Math.Max(0, at.xMin);
            picker.style.top = at.yMax + 4;
            Show(picker, true);
            picker.BringToFront();
        }

        public void Refresh(World world, Village v)
        {
            lastWorld = world;
            long attack = 0, defInf = 0, defCav = 0, carry = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units.Get((UnitType)i);
                int n = v.TroopCount(u.Type);
                SetText(homeCounts[i], n.ToString("N0"));
                homeCounts[i].EnableInClassList("garrison-count--none", n == 0);
                attack += (long)n * u.Attack;
                defInf += (long)n * u.DefenseInfantry;
                defCav += (long)n * u.DefenseCavalry;
                carry += (long)n * u.Carry;
            }
            SetText(strength, $"Attack {attack:N0} · defense {defInf:N0} vs infantry, {defCav:N0} vs cavalry · carries {carry:N0} · troop population {v.TroopPopulation:N0} (free {v.FreePopulation:N0})");
            RefreshMovements(world, v);
        }

        void RefreshMovements(World world, Village v)
        {
            var human = world.HumanPlayer;
            if (human == null) return;
            var commands = world.MovementsFor(human.Id);
            // This village's troops stationed elsewhere (recalled from here; other villages' from their own rally points).
            var stationed = world.Villages.FindAll(x => x.Supports.Exists(g => g.OwnerId == human.Id && g.FromVillageId == v.Id));

            string signature = v.Id + "|" + string.Join(",", commands.ConvertAll(c => c.Id.ToString())) + "|" + string.Join(",", stationed.ConvertAll(x => x.Id.ToString()))
                               + "|" + string.Join(",", v.Supports.ConvertAll(g => g.FromVillageId + ":" + string.Join(".", g.Troops)));
            if (signature != movementsSignature)
            {
                movementsSignature = signature;
                movements.Clear();
                rows.Clear();
                ShowHosted(world, v, human);
                if (commands.Count == 0 && stationed.Count == 0) movements.Add(Text("No troops on the move.", "row-info"));
                foreach (var c in commands)
                {
                    // Each movement names its villages and players as links.
                    var row = new MovementRow(links);
                    rows.Add(row);
                    movements.Add(row.Root);
                }
                foreach (var host in stationed)
                    foreach (var g in host.Supports)
                    {
                        if (g.OwnerId != human.Id || g.FromVillageId != v.Id) continue;
                        int hostId = host.Id, fromId = g.FromVillageId;
                        var item = Element("queue-item", "recruit-item");
                        item.Add(Text($"Supporting {host.Name} ({host.X}|{host.Y}): {TroopSummary(g.Troops)}", "row-info"));
                        item.Add(ButtonWith("Recall troops", () => game.Recall(hostId, fromId), "btn", "btn--small", "cancel-btn"));
                        movements.Add(item);
                    }
            }

            for (int i = 0; i < rows.Count && i < commands.Count; i++) rows[i].Update(world, commands[i]);
        }

        /// <summary>The support stationed in this village, a row for each village it came from, each with Send home.</summary>
        void ShowHosted(World world, Village v, Player human)
        {
            hosted.Clear();
            Show(hostedHeading, v.Supports.Count > 0);
            foreach (var g in v.Supports)
            {
                int hostId = v.Id, fromId = g.FromVillageId;
                var from = world.FindVillage(fromId);
                var owner = world.FindPlayer(g.OwnerId);
                string who = owner == null || owner == human ? "" : $"{owner.Name}, ";
                var item = Element("queue-item", "recruit-item");
                item.Add(Text($"From {who}{from?.Name ?? "?"}{(from != null ? $" ({from.X}|{from.Y})" : "")}: {TroopSummary(g.Troops)}", "row-info"));
                item.Add(ButtonWith("Send home", () => game.SendSupportHome(hostId, fromId), "btn", "btn--small", "cancel-btn"));
                hosted.Add(item);
            }
        }

        static string TroopSummary(int[] troops)
        {
            var parts = new List<string>();
            for (int i = 0; i < troops.Length && i < Units.Count; i++)
                if (troops[i] > 0) parts.Add($"{troops[i]:N0} {Units.Get((UnitType)i).Name}");
            return parts.Count > 0 ? string.Join(", ", parts) : "no troops";
        }
    }
}
