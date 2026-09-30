using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
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
