using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>A tribe's profile: its leader and members, strength, how it stands with the player's tribe, and what the player can do about it.</summary>
    public class TribeWindow : InfoWindow
    {
        readonly MedievalWorldConquestGame game;
        public int? TribeId { get; private set; }
        string signature;

        public TribeWindow(MedievalWorldConquestGame game, UiLinks links) : base(links) => this.game = game;

        public void Open(int tribeId)
        {
            TribeId = tribeId;
            signature = null;
            Show(Root, true);
        }

        public override void Close()
        {
            TribeId = null;
            base.Close();
        }

        public override void Refresh(World world)
        {
            if (!IsOpen || !TribeId.HasValue) return;
            var t = world.FindTribe(TribeId.Value);
            if (t == null)
            {
                Close();
                return;
            }
            var human = world.HumanPlayer;
            var mine = world.TribeOf(human);
            var (points, villages) = world.TribeStrength(t);
            string now = $"{t.Members.Count}|{t.LeaderId}|{points / 100}|{villages}|{(int)world.Relation(mine, t)}|{mine?.Id}|{human?.AskedToJoinTribe}|{(mine != null ? TribePanel.PeaceWaitMinutes(world, mine, t) : 0)}";
            if (now == signature) return;
            signature = now;
            SetText(title, $"{t.Name} [{t.Tag}]");
            body.Clear();

            var leader = world.FindPlayer(t.LeaderId);
            body.Add(Line("Leader: ", (leader?.Name ?? "?", (Action)(() => links.OpenPlayer(t.LeaderId)))));
            body.Add(Line($"{t.Members.Count} members  ·  {points:N0} points  ·  {villages:N0} villages  ·  with allies {world.BlocShare(t):P1} of the world"));
            if (mine != null && mine != t) body.Add(Line($"Your tribe: {TribeText.Relation(world.Relation(mine, t))}"));
            if (mine == t) body.Add(Text("This is your tribe.", "row-info", "protection-note"));

            var actions = Element("option-row", "info-actions");
            int id = t.Id;
            if (mine == null && World.HasRoom(t) && !world.IsHumanLed(t))
                actions.Add(ButtonWith(human.AskedToJoinTribe == t.Id ? "Asked to join" : "Ask to join", () => game.AskToJoin(id), "btn", "btn--small"));
            if (mine != null && mine != t && mine.LeaderId == human.Id)
            {
                var kind = world.Relation(mine, t);
                if (kind == RelationKind.Neutral) actions.Add(ButtonWith("Offer pact", () => game.ProposeRelation(id, RelationKind.NonAggression), "btn", "btn--small"));
                if (kind != RelationKind.Ally && kind != RelationKind.Enemy) actions.Add(ButtonWith("Offer alliance", () => game.ProposeRelation(id, RelationKind.Ally), "btn", "btn--small"));
                if (kind != RelationKind.Enemy) actions.Add(ButtonWith("Declare war", () => game.ProposeRelation(id, RelationKind.Enemy), "btn", "btn--small"));
                if (kind == RelationKind.Enemy) actions.Add(TribePanel.PeaceButton(world, mine, t, () => game.ProposeRelation(id, RelationKind.Neutral)));
                else if (kind != RelationKind.Neutral) actions.Add(ButtonWith("End agreement", () => game.ProposeRelation(id, RelationKind.Neutral), "btn", "btn--small"));
            }
            body.Add(actions);

            body.Add(Text("Members", "heading"));
            var members = new List<Player>();
            foreach (int m in t.Members)
            {
                var p = world.FindPlayer(m);
                if (p != null) members.Add(p);
            }
            members.Sort((a, b) => world.PointsOf(b).CompareTo(world.PointsOf(a)));
            foreach (var p in members)
            {
                var row = Element("info-village-row");
                int pid = p.Id;
                row.Add(Link(p.IsHuman ? $"{p.Name} (you)" : p.Name, () => links.OpenPlayer(pid), "link--owner"));
                row.Add(Element("spacer"));
                row.Add(Text($"{world.PointsOf(p):N0} pts  ·  {world.VillagesOf(p.Id).Count} villages", "row-level"));
                body.Add(row);
            }

            var allies = world.AlliesOf(t);
            if (allies.Count > 0)
            {
                body.Add(Text("Allies", "heading"));
                foreach (var a in allies)
                {
                    int aid = a.Id;
                    body.Add(Line(($"[{a.Tag}] {a.Name}", (Action)(() => links.OpenTribe(aid)))));
                }
            }
        }
    }
}
