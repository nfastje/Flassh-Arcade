using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// One troop movement as a line: what it is, the village it's from or bound for (a link), its owner (a link),
    /// and when it arrives. Click the line to see what's in it (the player's own troops, or what their merchants
    /// carry; what's coming at the player stays unknown until it lands). Updated in place every frame.
    /// </summary>
    public class MovementRow
    {
        /// <summary>The movements whose contents are showing, by command id (not saved).</summary>
        static readonly HashSet<int> Expanded = new HashSet<int>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Expanded.Clear();

        public VisualElement Root { get; }
        public int CommandId { get; private set; }
        readonly VisualElement line, details, speedIcon;
        AttackSpeed? speedShown;
        readonly Label caret, kind, time, when;
        readonly Button village, player;
        readonly Label target;
        int villageId = -1, playerId = -1, targetId = -1;
        /// <summary>What the details show (the command and whether they're open), to rebuild them only when it changes.</summary>
        string detailsShown;

        public MovementRow(UiLinks links)
        {
            Root = Element("movement-item");
            line = Element("movement-row");
            line.tooltip = "Click to see what's in it";
            caret = Text("▸", "movement-caret");
            line.Add(caret);
            // An incoming attack's speed as a unit icon: all the player can tell about it before it lands.
            speedIcon = Icons.Element(Icons.Unit(UnitType.Axeman), 18, "attack-speed-icon");
            speedIcon.pickingMode = PickingMode.Position;
            line.Add(speedIcon);
            kind = Text("", "movement-kind");
            line.Add(kind);
            village = Link("", () => { if (villageId >= 0) links.OpenVillage(villageId); });
            line.Add(village);
            player = Link("", () => { if (playerId >= 0) links.OpenPlayer(playerId); }, "link--owner");
            line.Add(player);
            target = Text("", "row-info", "movement-target");
            target.RegisterCallback<ClickEvent>(_ => { if (targetId >= 0) links.OpenVillage(targetId); });
            line.Add(target);
            line.Add(Element("spacer"));
            when = Text("", "row-level", "movement-when");
            line.Add(when);
            time = Text("", "row-title", "movement-countdown");
            line.Add(time);
            // A click anywhere on the line but its links opens or closes the details.
            line.RegisterCallback<ClickEvent>(e =>
            {
                var hit = e.target as VisualElement;
                if (hit == target || hit is Button || hit?.parent is Button) return;
                if (!Expanded.Remove(CommandId)) Expanded.Add(CommandId);
            });
            Root.Add(line);
            details = Element("movement-details");
            Root.Add(details);
        }

        /// <summary>The troops (or goods) in a movement, as icons and counts.</summary>
        void ShowDetails(World world, Command c, bool incoming)
        {
            details.Clear();
            if (incoming)
            {
                details.Add(Text("You can't tell what's in it until it arrives. Scouts only see what's at home.", "row-info"));
                return;
            }
            if (c.IsTrade)
            {
                if (c.Kind == CommandKind.TransportReturn)
                {
                    details.Add(Text($"{c.Merchants:N0} merchants on their way home, empty.", "row-info"));
                    return;
                }
                details.Add(Text($"{c.Merchants:N0} merchants carrying", "row-info"));
                AddGoods(c.Loot);
                return;
            }
            bool any = false;
            foreach (var type in Units.InDisplayOrder)
            {
                int n = c.Troops != null && (int)type < c.Troops.Length ? c.Troops[(int)type] : 0;
                if (n <= 0) continue;
                any = true;
                var chip = Element("movement-unit");
                chip.tooltip = Units.Get(type).Name;
                chip.Add(Icons.Element(Icons.Unit(type), 18, "cost-icon"));
                chip.Add(Text($"{n:N0}", "cost-value"));
                details.Add(chip);
            }
            if (!any) details.Add(Text("No troops.", "row-info"));
            if (c.Kind == CommandKind.Return && c.Loot.Wood + c.Loot.Clay + c.Loot.Iron > 0)
            {
                details.Add(Text("  carrying", "row-info"));
                AddGoods(c.Loot);
            }
        }

        void AddGoods(Cost goods)
        {
            for (int i = 0; i < 3; i++)
            {
                var r = (ResourceType)i;
                int amount = goods.Get(r);
                if (amount <= 0) continue;
                var chip = Element("movement-unit");
                chip.Add(Icons.Element(Icons.Resource(r), 18, "cost-icon"));
                chip.Add(Text($"{amount:N0}", "cost-value"));
                details.Add(chip);
            }
        }

        public void Update(World world, Command c)
        {
            CommandId = c.Id;
            var human = world.HumanPlayer;
            bool incoming = human != null && c.OwnerId != human.Id;
            var from = world.FindVillage(c.FromVillageId);
            var to = world.FindVillage(c.ToVillageId);

            // Incoming: where it's from and whose it is. Outgoing: the village it's bound for and its owner. Coming
            // home: the village it's coming back from.
            bool back = c.Kind == CommandKind.Return || c.Kind == CommandKind.TransportReturn;
            var shown = incoming || back ? from : to;
            SetText(kind, incoming
                ? (c.Kind == CommandKind.Attack ? "Incoming attack from" : c.Kind == CommandKind.Transport ? "Merchants coming from"
                    : c.Kind == CommandKind.Support ? "Support coming from" : "Troops coming from")
                : c.Kind switch
                {
                    CommandKind.Attack => "Attack on",
                    CommandKind.Support => "Support to",
                    CommandKind.Transport => "Merchants to",
                    CommandKind.TransportReturn => "Merchants back from",
                    _ => "Returning from",
                });
            villageId = shown?.Id ?? -1;
            SetText(village, shown != null ? $"{shown.Name} ({shown.X}|{shown.Y})" : "?");
            var owner = shown != null ? world.FindPlayer(shown.OwnerId) : null;
            playerId = owner?.Id ?? -1;
            Show(player, shown != null && !(owner?.IsHuman ?? false));
            SetText(player, owner != null ? owner.Name : "barbarians");
            player.SetEnabled(owner != null);
            // With several villages, which of the player's it concerns.
            var own = incoming || back ? to : from;
            targetId = own?.Id ?? -1;
            bool several = human != null && world.VillagesOf(human.Id).Count > 1;
            Show(target, several && own != null);
            if (several && own != null) SetText(target, incoming || back ? $"→ {own.Name}" : $"from {own.Name}");
            // When it gets there (for troops coming home: when they're back), as a countdown and on the clock; the
            // player's own attacks also say roughly when the survivors will be home again.
            SetText(time, Real(world, Math.Max(0, c.ArriveTime - world.Now)));
            SetText(when, World.FormatClock(c.ArriveTime) +
                          (!incoming && c.Kind == CommandKind.Attack ? $"  ·  back about {World.FormatClock(c.ArriveTime + (c.ArriveTime - c.DepartTime))}" : ""));
            line.EnableInClassList("movement-row--incoming", incoming && c.Kind == CommandKind.Attack);
            line.EnableInClassList("movement-row--return", back || c.IsTrade);
            bool attackOnUs = incoming && c.Kind == CommandKind.Attack;
            Show(speedIcon, attackOnUs);
            AttackSpeed? speed = attackOnUs ? AttackSpeeds.Of(c.Troops) : (AttackSpeed?)null;
            if (speed != speedShown)
            {
                speedShown = speed;
                if (speed.HasValue)
                {
                    speedIcon.style.backgroundImage = new StyleBackground(Icons.Unit(AttackSpeeds.Icon(speed.Value)));
                    speedIcon.tooltip = AttackSpeeds.Describe(speed.Value);
                }
            }
            line.EnableInClassList("movement-row--danger", speed.HasValue && AttackSpeeds.IsDangerous(speed.Value));

            bool open = Expanded.Contains(c.Id);
            SetText(caret, open ? "▾" : "▸");
            Show(details, open);
            string shownNow = open ? $"{c.Id}" : "";
            if (shownNow != detailsShown)
            {
                detailsShown = shownNow;
                if (open) ShowDetails(world, c, incoming);
            }
        }
    }
}
