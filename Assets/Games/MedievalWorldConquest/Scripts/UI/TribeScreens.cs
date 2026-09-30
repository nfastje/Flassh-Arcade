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
        }

        public void Refresh(World world)
        {
            if (UnityEngine.Time.unscaledTime < nextRefresh) return;
            nextRefresh = UnityEngine.Time.unscaledTime + 1f;
            var human = world.HumanPlayer;
            if (human == null) return;
            var tribe = world.TribeOf(human);

            string now = TribeSignature(world, tribe) + "|" + human.AskedToJoinTribe + "|" + world.HoldTribeId + ":" + world.HoldSince;
            if (now == signature) return;
            signature = now;
            body.Clear();
            if (tribe == null) ShowNoTribe(world, human);
            else ShowTribe(world, human, tribe);
        }

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
            body.Add(Text($"With its allies (and theirs) your tribe holds {bloc:P1} of the {world.GoalVillagesLabel}. Hold {world.Settings.ConquestGoal:P0} together, or on your own, for {World.HoldDays:0} days to win the world.", "row-info"));
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
            if (kind != RelationKind.Neutral)
                row.Add(ButtonWith(kind == RelationKind.Enemy ? "Make peace" : "End it", () => game.ProposeRelation(id, RelationKind.Neutral), "btn", "btn--small", "count-btn"));
            return row;
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

    // -------------------------------------------------------------------- any tribe

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
            string now = $"{t.Members.Count}|{t.LeaderId}|{points / 100}|{villages}|{(int)world.Relation(mine, t)}|{mine?.Id}|{human?.AskedToJoinTribe}";
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
                if (kind != RelationKind.Neutral) actions.Add(ButtonWith(kind == RelationKind.Enemy ? "Make peace" : "End agreement", () => game.ProposeRelation(id, RelationKind.Neutral), "btn", "btn--small"));
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

    // -------------------------------------------------------------------- messages

    /// <summary>
    /// The player's messages from lords and tribes (diplomacy worlds): invitations, pact offers, calls for support,
    /// orders, warnings. Those that ask something have their answers as buttons.
    /// </summary>
    public class MessagesPanel
    {
        public VisualElement Root { get; }

        readonly MedievalWorldConquestGame game;
        readonly UiLinks links;
        readonly ScrollView list, detail;
        int? selectedId;
        string listSignature;

        public MessagesPanel(MedievalWorldConquestGame game, UiLinks links)
        {
            this.game = game;
            this.links = links;
            Root = Element("army", "reports");
            var left = Element("reports-list-column");
            left.Add(Text("Messages", "heading"));
            var actions = Element("option-row", "reports-actions");
            actions.Add(ButtonWith("Mark all read", () => game.MarkAllMessagesRead(), "btn", "btn--small"));
            actions.Add(ButtonWith("Delete", () =>
            {
                if (selectedId.HasValue) game.DeleteMessage(selectedId.Value);
                selectedId = null;
            }, "btn", "btn--small"));
            left.Add(actions);
            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("reports-list");
            left.Add(list);
            Root.Add(left);
            detail = new ScrollView(ScrollViewMode.Vertical);
            detail.AddToClassList("reports-detail");
            Root.Add(detail);
        }

        /// <summary>Shows one message.</summary>
        public void Open(int id)
        {
            selectedId = id;
            game.MarkMessageRead(id);
            listSignature = null;
        }

        public void Refresh(World world)
        {
            string signature = selectedId + ":";
            foreach (var m in world.Messages) signature += m.Id + (m.Read ? "r" : "u") + (m.Answered ? "a," : ",");
            if (signature == listSignature) return;
            listSignature = signature;

            list.Clear();
            for (int i = world.Messages.Count - 1; i >= 0; i--)
            {
                var m = world.Messages[i];
                int id = m.Id;
                var item = ButtonWith($"{m.Subject}\n{(string.IsNullOrEmpty(m.From) ? "" : m.From + "  ·  ")}{World.FormatClock(m.Time)}", () => Open(id), "report-item");
                item.EnableInClassList("report-item--unread", !m.Read);
                item.EnableInClassList("report-item--selected", selectedId == id);
                item.EnableInClassList("report-item--lost", m.Kind == MessageKind.SupportRequest || m.Kind == MessageKind.Order || m.Kind == MessageKind.FeedRequest);
                list.Add(item);
            }

            detail.Clear();
            var selected = selectedId.HasValue ? world.FindMessage(selectedId.Value) : null;
            if (selected == null)
            {
                detail.Add(Text(world.Messages.Count == 0 ? "No messages yet. Lords and tribes will write to you here." : "Choose a message on the left.", "row-info"));
                return;
            }
            detail.Add(Text(selected.Subject, "heading"));
            var from = Element("link-line");
            from.Add(Text($"{World.FormatClock(selected.Time)}  ·  from", "row-level"));
            var sender = world.FindPlayer(selected.FromPlayerId);
            if (sender != null) from.Add(Link(selected.From, () => links.OpenPlayer(sender.Id), "link--owner"));
            else from.Add(Text(string.IsNullOrEmpty(selected.From) ? "the realm" : selected.From, "row-level"));
            var tribe = world.FindTribe(selected.TribeId);
            if (tribe != null) from.Add(Link($"[{tribe.Tag}]", () => links.OpenTribe(tribe.Id)));
            detail.Add(from);
            detail.Add(Text(selected.Body, "body-text", "message-body"));

            var actions = Element("option-row", "info-actions");
            int mid = selected.Id, a = selected.A;
            switch (selected.Kind)
            {
                case MessageKind.FeedRequest:
                    if (selected.Answered) actions.Add(Text("Answered.", "row-level"));
                    else
                    {
                        actions.Add(ButtonWith("Hand it over", () => game.AnswerMessage(mid, true), "btn", "btn--small"));
                        actions.Add(ButtonWith("Refuse", () => game.AnswerMessage(mid, false), "btn", "btn--small"));
                    }
                    actions.Add(ButtonWith("The village", () => links.OpenVillage(a), "btn", "btn--small"));
                    break;
                case MessageKind.FeedOffer:
                    actions.Add(ButtonWith("Send noblemen", () => links.SendTroops(a), "btn", "btn--small"));
                    actions.Add(ButtonWith("Show on map", () => links.ShowOnMap(a), "btn", "btn--small"));
                    break;
                case MessageKind.Invitation:
                case MessageKind.PactOffer:
                case MessageKind.FactionInvite:
                    if (selected.Answered) actions.Add(Text("Answered.", "row-level"));
                    else
                    {
                        actions.Add(ButtonWith("Accept", () => game.AnswerMessage(mid, true), "btn", "btn--small"));
                        actions.Add(ButtonWith("Decline", () => game.AnswerMessage(mid, false), "btn", "btn--small"));
                    }
                    if (tribe != null) actions.Add(ButtonWith("About the tribe", () => links.OpenTribe(tribe.Id), "btn", "btn--small"));
                    break;
                case MessageKind.SupportRequest:
                    actions.Add(ButtonWith("Send support", () => links.SendTroops(a), "btn", "btn--small"));
                    actions.Add(ButtonWith("The village", () => links.OpenVillage(a), "btn", "btn--small"));
                    break;
                case MessageKind.Order:
                    actions.Add(ButtonWith("Attack", () => links.SendTroops(a), "btn", "btn--small"));
                    actions.Add(ButtonWith("Show on map", () => links.ShowOnMap(a), "btn", "btn--small"));
                    break;
            }
            detail.Add(actions);
        }
    }
}
