using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>Words for how tribes stand, shared by the tribe screens.</summary>
    static class TribeText
    {
        public static string Relation(RelationKind kind) => kind switch
        {
            RelationKind.Ally => "Allies",
            RelationKind.NonAggression => "Non-aggression pact",
            RelationKind.Enemy => "At war",
            _ => "No agreement",
        };

        /// <summary>How a tribe feels, from its tension: a hint, never a number.</summary>
        public static string Mood(Tribe t) =>
            t.Tension < 20 ? "Calm" : t.Tension < 40 ? "Uneasy" : t.Tension < 65 ? "Restless" : "In turmoil";

        public static string RelationClass(RelationKind kind) => kind switch
        {
            RelationKind.Ally => "relation-ally",
            RelationKind.NonAggression => "relation-pact",
            RelationKind.Enemy => "relation-enemy",
            _ => "relation-none",
        };
    }

    // -------------------------------------------------------------------- the player's tribe

    /// <summary>
    /// The Tribe screen (diplomacy worlds): without a tribe, found one or ask to join one nearby (invitations come
    /// as messages); in a tribe, its members, relations with other tribes, its target and the player's options:
    /// asking for support, leaving, and for a leader, expelling members, making pacts and war.
    /// </summary>
    public class TribePanel
    {
        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly UiLinks links;
        readonly Action<string, Action> confirm;
        readonly ScrollView body;
        readonly UnderAttackList underAttack;
        readonly TextField nameField, tagField;
        readonly VisualElement foundBox;
        readonly Label foundMessage;
        string signature;
        float nextRefresh;

        /// <param name="confirm">Asks the player to confirm something before it's done.</param>
        public TribePanel(MedievalWorldConquestGame game, UiLinks links, Action<string, Action> confirm)
        {
            this.game = game;
            this.links = links;
            this.confirm = confirm;
            Root = Element("army", "overview");
            var column = Element("overview-column");
            body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("ranking-list");
            column.Add(body);
            Root.Add(column);

            // The founding form is kept (so what's typed survives refreshes) and shown when there's no tribe.
            foundBox = Element("pane-box", "tribe-found");
            foundBox.Add(Text("Found a tribe", "pane-title"));
            var row = Element("send-to-row", "tribe-found-row");
            row.Add(Text("Name", "row-title"));
            nameField = new TextField { maxLength = 32 };
            nameField.AddToClassList("rename-field");
            row.Add(nameField);
            row.Add(Text("Tag", "row-title"));
            tagField = new TextField { maxLength = 6 };
            tagField.AddToClassList("coord-field");
            row.Add(tagField);
            row.Add(ButtonWith("Found", () => SetText(foundMessage, game.FoundTribe(nameField.value, tagField.value) ?? ""), "btn", "btn--small"));
            foundBox.Add(row);
            foundMessage = Text("", "row-reason");
            foundBox.Add(foundMessage);
            underAttack = new UnderAttackList(links);
        }

        public void Refresh(World world)
        {
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + 1f;
            var human = world.HumanPlayer;
            if (human == null) return;
            var tribe = world.TribeOf(human);

            string now = TribeSignature(world, tribe) + "|" + human.AskedToJoinTribe + "|" + world.HoldTribeId + ":" + world.HoldSince
                         + "|" + string.Join(",", world.SwapCandidates().ConvertAll(p => p.Id.ToString())) + "|" + PeaceSignature(world, tribe);
            if (now != signature)
            {
                signature = now;
                body.Clear();
                if (tribe == null) ShowNoTribe(world, human);
                else ShowTribe(world, human, tribe);
            }
            // (Kept between rebuilds, and brought up to date every second.)
            if (tribe != null) underAttack.Refresh(world, tribe);
        }

        /// <summary>For each tribe at war with the player's: the real minutes until peace can be offered again (so the buttons keep up).</summary>
        static string PeaceSignature(World world, Tribe mine)
        {
            if (mine == null) return "";
            var parts = new List<string>();
            foreach (var t in world.ActiveTribes())
                if (t != mine && world.Relation(mine, t) == RelationKind.Enemy) parts.Add(t.Id + ":" + PeaceWaitMinutes(world, mine, t));
            return string.Join(",", parts);
        }

        public static int PeaceWaitMinutes(World world, Tribe mine, Tribe other) =>
            (int)Math.Ceiling(Math.Max(0, world.NextPeaceOffer(mine, other) - world.Now) / (60 * world.Settings.Speed));

        static string TribeSignature(World world, Tribe tribe)
        {
            var parts = new List<string>();
            foreach (var t in world.ActiveTribes()) parts.Add($"{t.Id}:{t.Members.Count}:{t.LeaderId}");
            foreach (var r in world.Relations) parts.Add($"{r.A}-{r.B}:{(int)r.Kind}");
            if (tribe != null)
            {
                var (points, villages) = world.TribeStrength(tribe);
                parts.Add($"me:{tribe.Id}:{string.Join(",", tribe.Members)}:{points / 100}:{villages}:{tribe.TargetVillageId}:{TribeText.Mood(tribe)}");
            }
            return string.Join(";", parts);
        }

        void ShowNoTribe(World world, Player human)
        {
            body.Add(Text("Tribe", "heading"));
            body.Add(Text("You're not in a tribe. Tribes defend their members, share what they see, pick targets together, and win together with their allies. " +
                          "Found your own, or ask a tribe nearby to take you in. Tribes also send invitations to promising lords (see your messages).", "row-info"));
            body.Add(foundBox);
            var asked = world.FindTribe(human.AskedToJoinTribe);
            if (asked != null) body.Add(Text($"You've asked to join {asked.Name} [{asked.Tag}]. Their leader will answer soon.", "row-info", "protection-note"));

            body.Add(Text("Tribes nearby", "heading"));
            var near = NearbyTribes(world, human, 15);
            if (near.Count == 0) body.Add(Text("No tribes near you yet.", "row-info"));
            foreach (var (t, distance) in near)
            {
                var row = TribeRow(world, t, distance);
                int id = t.Id;
                if (World.HasRoom(t) && !world.IsHumanLed(t))
                    row.Add(ButtonWith("Ask to join", () => game.AskToJoin(id), "btn", "btn--small", "count-btn"));
                else row.Add(Text(World.HasRoom(t) ? "" : "full", "row-level"));
                body.Add(row);
            }
        }

        void ShowTribe(World world, Player human, Tribe tribe)
        {
            bool leader = tribe.LeaderId == human.Id;
            var (points, villages) = world.TribeStrength(tribe);
            body.Add(Text($"{tribe.Name} [{tribe.Tag}]", "heading"));
            var head = world.FindPlayer(tribe.LeaderId);
            var line = Element("link-line");
            line.Add(Text("Leader:", "row-info", "link-text"));
            line.Add(Link(head == human ? $"{head.Name} (you)" : head?.Name ?? "?", () => links.OpenPlayer(tribe.LeaderId), "link--owner"));
            line.Add(Text($"·  {tribe.Members.Count} of {World.MaxTribeMembers} members  ·  {points:N0} points  ·  {villages:N0} villages  ·  mood: {TribeText.Mood(tribe)}", "row-info", "link-text"));
            body.Add(line);
            double bloc = world.BlocShare(tribe);
            body.Add(Text($"With its allies your tribe holds {bloc:P1} of the {world.GoalVillagesLabel}. Hold {world.Settings.ConquestGoal:P0} together, or on your own, for {World.HoldDays:0} days to win the world.", "row-info"));
            string hold = RankingPanel.HoldStatus(world);
            if (hold.Length > 0) body.Add(Text(hold, "row-info", "protection-note"));

            // The target.
            var target = tribe.TargetUntil > world.Now ? world.FindVillage(tribe.TargetVillageId) : null;
            var targetLine = Element("link-line");
            targetLine.Add(Text("Tribe target:", "row-title", "report-role"));
            if (target != null)
            {
                int tid = target.Id;
                targetLine.Add(Link($"{target.Name} ({target.X}|{target.Y})", () => links.OpenVillage(tid)));
                targetLine.Add(Text($"until {World.FormatClock(tribe.TargetUntil)}", "row-level"));
                targetLine.Add(ButtonWith("Attack it", () => links.SendTroops(tid), "btn", "btn--small", "count-btn"));
                if (leader) targetLine.Add(ButtonWith("Clear", () => game.ClearTribeTarget(), "btn", "btn--small", "count-btn"));
            }
            else targetLine.Add(Text(leader ? "none. Name one from any village's window." : "none right now.", "row-info", "link-text"));
            body.Add(targetLine);

            var actions = Element("option-row", "info-actions");
            actions.Add(ButtonWith("Ask for support here", () => game.RequestSupport(), "btn", "btn--small"));
            actions.Add(ButtonWith("Leave tribe", () => confirm(leader && tribe.Members.Count > 1
                ? "Leave your tribe? The strongest member will lead it."
                : "Leave your tribe?", () => game.LeaveTribe()), "btn", "btn--small"));
            body.Add(actions);

            // Tribe mates' villages under attack (the list keeps itself up to date).
            body.Add(underAttack.Root);

            // Leading a tribe on the winning side of the endgame: the faction's lords who would strengthen it.
            var candidates = world.SwapCandidates();
            if (candidates.Count > 0)
            {
                body.Add(Text("Strengthen your tribe", "heading"));
                bool room = World.HasRoom(tribe);
                body.Add(Text(room
                    ? "Your tribe stands with the side that can win the world. These lords of the tribes on your side would join you: bring them in while you have room."
                    : "Your tribe stands with the side that can win the world. These lords of the tribes on your side are stronger than your weakest member: bring one in and your weakest takes their place in their tribe.", "row-info"));
                foreach (var c in candidates)
                {
                    var row = Element("info-village-row");
                    int id = c.Id;
                    row.Add(Link(c.Name, () => links.OpenPlayer(id), "link--owner"));
                    var theirs = world.TribeOf(c);
                    if (theirs != null)
                    {
                        int tid = theirs.Id;
                        row.Add(Link($"[{theirs.Tag}]", () => links.OpenTribe(tid)));
                    }
                    row.Add(Element("spacer"));
                    row.Add(Text($"{world.PointsOf(c):N0} pts  ·  {world.VillagesOf(c.Id).Count} villages", "row-level"));
                    row.Add(ButtonWith("Bring in", () => game.BringIntoTribe(id), "btn", "btn--small", "count-btn"));
                    body.Add(row);
                }
            }

            // Members.
            body.Add(Text("Members", "heading"));
            var members = new List<Player>();
            foreach (int id in tribe.Members)
            {
                var p = world.FindPlayer(id);
                if (p != null) members.Add(p);
            }
            members.Sort((a, b) => world.PointsOf(b).CompareTo(world.PointsOf(a)));
            foreach (var m in members)
            {
                var row = Element("info-village-row");
                int id = m.Id;
                row.Add(Link(m.IsHuman ? $"{m.Name} (you)" : m.Name, () => links.OpenPlayer(id), "link--owner"));
                if (m.Id == tribe.LeaderId) row.Add(Text("leader", "row-level"));
                row.Add(Element("spacer"));
                row.Add(Text($"{world.PointsOf(m):N0} pts  ·  {world.VillagesOf(m.Id).Count} villages", "row-level"));
                if (leader && !m.IsHuman)
                {
                    string name = m.Name;
                    row.Add(ButtonWith("Expel", () => confirm($"Expel {name} from the tribe?", () => game.ExpelFromTribe(id)), "btn", "btn--small", "count-btn"));
                }
                body.Add(row);
            }

            // Relations: those the tribe has, then others nearby.
            body.Add(Text("Other tribes", "heading"));
            if (!leader) body.Add(Text("Your leader decides on pacts and war.", "row-info"));
            var shown = new HashSet<int>();
            foreach (var r in world.Relations)
            {
                if (r.A != tribe.Id && r.B != tribe.Id) continue;
                var other = world.FindTribe(r.A == tribe.Id ? r.B : r.A);
                if (other == null) continue;
                shown.Add(other.Id);
                body.Add(RelationRow(world, tribe, other, leader, world.DistanceToTribe(human, other)));
            }
            foreach (var (t, distance) in NearbyTribes(world, human, 12))
                if (t != tribe && shown.Add(t.Id)) body.Add(RelationRow(world, tribe, t, leader, distance));
        }

        VisualElement RelationRow(World world, Tribe mine, Tribe other, bool leader, double distance)
        {
            var kind = world.Relation(mine, other);
            var row = TribeRow(world, other, distance);
            row.Add(Text(TribeText.Relation(kind), "row-level", "relation-label", TribeText.RelationClass(kind)));
            if (!leader) return row;
            int id = other.Id;
            string name = other.Name;
            if (kind == RelationKind.Neutral)
            {
                row.Add(ButtonWith("Offer pact", () => game.ProposeRelation(id, RelationKind.NonAggression), "btn", "btn--small", "count-btn"));
                row.Add(ButtonWith("Offer alliance", () => game.ProposeRelation(id, RelationKind.Ally), "btn", "btn--small", "count-btn"));
            }
            else if (kind == RelationKind.NonAggression)
                row.Add(ButtonWith("Offer alliance", () => game.ProposeRelation(id, RelationKind.Ally), "btn", "btn--small", "count-btn"));
            if (kind != RelationKind.Enemy)
                row.Add(ButtonWith("Declare war", () => confirm(kind == RelationKind.Neutral ? $"Declare war on {name}?" : $"Break your agreement with {name} and declare war? Others will remember it.",
                    () => game.ProposeRelation(id, RelationKind.Enemy)), "btn", "btn--small", "count-btn"));
            if (kind == RelationKind.Enemy) row.Add(PeaceButton(world, mine, other, () => game.ProposeRelation(id, RelationKind.Neutral), "count-btn"));
            else if (kind != RelationKind.Neutral)
                row.Add(ButtonWith("End it", () => game.ProposeRelation(id, RelationKind.Neutral), "btn", "btn--small", "count-btn"));
            return row;
        }

        /// <summary>Offers peace to a tribe at war with the player's (grayed out for a day after it's turned down).</summary>
        public static Button PeaceButton(World world, Tribe mine, Tribe other, Action offer, string extraClass = null)
        {
            double wait = world.NextPeaceOffer(mine, other) - world.Now;
            var button = ButtonWith(wait > 0 ? $"Peace refused ({Real(world, wait)})" : "Offer peace", offer, "btn", "btn--small");
            if (extraClass != null) button.AddToClassList(extraClass);
            button.SetEnabled(wait <= 0);
            button.tooltip = wait > 0 ? "They turned down your last offer. You can offer again once the wait is over."
                : "Their leader weighs it: how the war is going, whether you compete for the same land, other enemies, and your word.";
            return button;
        }

        /// <summary>"[IW] The Iron Wolves  ·  12 members · 34,000 pts · 18 fields", the tag a link to the tribe.</summary>
        VisualElement TribeRow(World world, Tribe t, double distance)
        {
            var row = Element("info-village-row");
            int id = t.Id;
            row.Add(Link($"[{t.Tag}] {t.Name}", () => links.OpenTribe(id)));
            row.Add(Element("spacer"));
            var (points, _) = world.TribeStrength(t);
            row.Add(Text($"{t.Members.Count} members  ·  {points:N0} pts  ·  {distance:0} fields", "row-level"));
            return row;
        }

        static List<(Tribe tribe, double distance)> NearbyTribes(World world, Player human, int count)
        {
            var list = new List<(Tribe, double)>();
            foreach (var t in world.ActiveTribes()) list.Add((t, world.DistanceToTribe(human, t)));
            list.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            if (list.Count > count) list.RemoveRange(count, list.Count - count);
            return list;
        }
    }
}
