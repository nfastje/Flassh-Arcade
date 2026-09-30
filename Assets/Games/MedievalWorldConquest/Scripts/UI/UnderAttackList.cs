using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The tribe's villages under attack (tribe mates', not the player's own, which are in the village view): each
    /// attack's speed as a unit icon (all a defender can tell), where and whose, who from, when it lands, whether
    /// the player's troops could get there first, and a link to send support. Countdowns update in place; rows are
    /// rebuilt only when attacks come and go.
    /// </summary>
    class UnderAttackList
    {
        public VisualElement Root { get; }
        readonly UiLinks links;
        readonly Label heading;
        readonly VisualElement rows;
        readonly List<(Command attack, Village target, Label lands, Label reach)> shown = new List<(Command, Village, Label, Label)>();
        string signature;

        const int MaxShown = 25;

        public UnderAttackList(UiLinks links)
        {
            this.links = links;
            Root = Element("under-attack");
            heading = Text("Under attack", "heading");
            Root.Add(heading);
            rows = Element();
            Root.Add(rows);
        }

        public void Refresh(World world, Tribe tribe)
        {
            var human = world.HumanPlayer;
            var attacks = new List<(Command c, Village to)>();
            foreach (var c in world.Commands)
            {
                if (c.Kind != CommandKind.Attack) continue;
                var to = world.FindVillage(c.ToVillageId);
                if (to == null || to.IsBarbarian || to.OwnerId == human.Id || c.OwnerId == to.OwnerId) continue;
                if (world.FindPlayer(to.OwnerId)?.TribeId != tribe.Id) continue;
                attacks.Add((c, to));
            }
            attacks.Sort((a, b) => a.c.ArriveTime.CompareTo(b.c.ArriveTime));
            if (attacks.Count > MaxShown) attacks.RemoveRange(MaxShown, attacks.Count - MaxShown);

            string now = string.Join(",", attacks.ConvertAll(a => a.c.Id.ToString()));
            if (now != signature)
            {
                signature = now;
                rows.Clear();
                shown.Clear();
                if (attacks.Count == 0) rows.Add(Text("No tribe mate's village is under attack.", "row-info"));
                foreach (var (c, to) in attacks)
                {
                    var speed = AttackSpeeds.Of(c.Troops);
                    var row = Element("under-attack-row");
                    row.EnableInClassList("under-attack-row--danger", AttackSpeeds.IsDangerous(speed));
                    var icon = Icons.Element(Icons.Unit(AttackSpeeds.Icon(speed)), 22, "attack-speed-icon");
                    icon.pickingMode = PickingMode.Position;
                    icon.tooltip = AttackSpeeds.Describe(speed);
                    row.Add(icon);
                    int villageId = to.Id, ownerId = to.OwnerId, attackerId = c.OwnerId;
                    var owner = world.FindPlayer(ownerId);
                    var attacker = world.FindPlayer(attackerId);
                    row.Add(Link($"{to.Name} ({to.X}|{to.Y})", () => links.OpenVillage(villageId)));
                    if (owner != null) row.Add(Link(owner.Name, () => links.OpenPlayer(ownerId), "link--owner"));
                    row.Add(Text("from", "row-level"));
                    if (attacker != null) row.Add(Link(world.NameWithTag(attacker), () => links.OpenPlayer(attackerId), "link--owner"));
                    row.Add(Element("spacer"));
                    var lands = Text("", "row-level", "under-attack-when");
                    row.Add(lands);
                    var reach = Text("", "row-level", "under-attack-reach");
                    row.Add(reach);
                    row.Add(ButtonWith("Send support", () => links.SendTroops(villageId), "btn", "btn--small", "count-btn"));
                    rows.Add(row);
                    shown.Add((c, to, lands, reach));
                }
            }

            SetText(heading, attacks.Count > 0 ? $"Under attack  ·  {attacks.Count}{(attacks.Count == MaxShown ? "+" : "")}" : "Under attack");
            foreach (var (c, to, lands, reach) in shown)
            {
                SetText(lands, $"lands {World.FormatClock(c.ArriveTime)}  ·  {Real(world, Math.Max(0, c.ArriveTime - world.Now))}");
                bool canReach = world.HumanCanReach(to, c.ArriveTime);
                SetText(reach, canReach ? "you can make it" : "too far for you");
                reach.EnableInClassList("under-attack-reach--no", !canReach);
            }
        }
    }
}
