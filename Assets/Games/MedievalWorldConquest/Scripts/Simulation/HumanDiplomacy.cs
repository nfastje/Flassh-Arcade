using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// The human among the tribes: asking to join and being asked, inviting and expelling, leaving, answering
    /// messages, proposing relations, and how the human's tribe mates feel about them.
    /// </summary>
    public partial class World
    {
        /// <summary>The human asks to join a tribe; its leader answers at the next tick.</summary>
        public bool AskToJoin(Tribe t)
        {
            var human = HumanPlayer;
            if (human == null || t == null || !Diplomacy || human.TribeId == t.Id) return false;
            human.AskedToJoinTribe = t.Id;
            return true;
        }

        /// <summary>The human (leading a tribe) invites a lord; the lord answers at the next tick.</summary>
        public bool InviteToTribe(Player lord)
        {
            var human = HumanPlayer;
            var tribe = TribeOf(human);
            if (tribe == null || tribe.LeaderId != human.Id || lord == null || lord.IsHuman || !CanJoinTribes(lord) || lord.TribeId == tribe.Id) return false;
            if (!human.Invited.Contains(lord.Id)) human.Invited.Add(lord.Id);
            return true;
        }

        /// <summary>The human, leading a tribe, sends a member away.</summary>
        public bool Expel(Player member)
        {
            var human = HumanPlayer;
            var tribe = TribeOf(human);
            if (tribe == null || tribe.LeaderId != human.Id || member == null || member == human || member.TribeId != tribe.Id) return false;
            LeaveTribe(member);
            return true;
        }

        /// <summary>The human leaves their tribe (leaving one they only just joined looks fickle).</summary>
        public void HumanLeavesTribe()
        {
            var human = HumanPlayer;
            if (human == null || human.TribeId < 0) return;
            if (Now - human.JoinedTribeAt < 3 * SecondsPerDay) human.Reputation -= 5;
            LeaveTribe(human);
        }

        /// <summary>Lords' and leaders' answers to what the human asked since the last tick, and invitations for the human.</summary>
        void AnswerHuman()
        {
            var human = HumanPlayer;
            if (human == null || VillagesOf(human.Id).Count == 0) return;
            int humanPoints = PointsOf(human);

            // Asked to join a tribe.
            var asked = FindTribe(human.AskedToJoinTribe);
            human.AskedToJoinTribe = -1;
            if (asked != null && human.TribeId != asked.Id)
            {
                var leader = FindPlayer(asked.LeaderId);
                var (points, _) = TribeStrength(asked);
                bool yes = HasRoom(asked) && DistanceToTribe(human, asked) <= 30 && human.Reputation >= -25
                           && humanPoints >= 0.25 * points / Math.Max(1, asked.Members.Count);
                if (yes)
                {
                    JoinTribe(human, asked);
                    Write(MessageKind.Note, leader, $"Welcome to {asked.Name}", Pick(1,
                        "You're one of us now. Watch your tribe mates' backs and they'll watch yours.",
                        "Welcome aboard. Send support when we call, and don't touch our allies.",
                        "Glad to have you. We stand together, or we fall one by one."), tribeId: asked.Id);
                }
                else
                    Write(MessageKind.Note, leader, $"About joining {asked.Name}", !HasRoom(asked) ? "We're full, I'm afraid."
                        : human.Reputation < -25 ? "Word of your broken promises travels fast. No."
                        : DistanceToTribe(human, asked) > 30 ? "You're too far from us to be any help, or to be helped."
                        : "Come back when you've grown a little.", tribeId: asked.Id);
            }

            // Lords the human invited.
            var tribe = TribeOf(human);
            foreach (int id in human.Invited)
            {
                var lord = FindPlayer(id);
                if (lord == null || tribe == null || !CanJoinTribes(lord) || lord.TribeId == tribe.Id) continue;
                var theirs = TribeOf(lord);
                double chance = theirs == null ? 0.6 : lord.Satisfaction < 40 ? 0.4 : 0.05;
                bool yes = HasRoom(tribe) && DistanceToTribe(lord, tribe) <= 30 && human.Reputation >= -25 && TribeRandom(id, 2) < chance;
                if (yes)
                {
                    JoinTribe(lord, tribe);
                    Write(MessageKind.Note, lord, $"Joining {tribe.Name}", theirs == null
                        ? Pick(2, "I accept. Let's make them fear us.", "Count me in.", "An honor. My spears are yours.")
                        : $"I'm done with {theirs.Name}. I'll ride with you.", tribeId: tribe.Id);
                }
                else
                    Write(MessageKind.Note, lord, "Your invitation", theirs != null && lord.Satisfaction >= 40
                        ? $"Thank you, but I'm staying with {theirs.Name}."
                        : Pick(3, "Not now.", "I'll think about it. (No.)", "You'll have to prove yourself first."));
            }
            human.Invited.Clear();

            // Tribes nearby invite a promising lord without a tribe (at most one invitation a tick, one per tribe at a time).
            if (human.TribeId >= 0 || human.Reputation < -30 || humanPoints < 150) return;
            foreach (var t in ActiveTribes())
            {
                if (!HasRoom(t) || IsHumanLed(t) || DistanceToTribe(human, t) > 20) continue;
                if (Messages.Exists(m => m.Kind == MessageKind.Invitation && m.A == t.Id && !m.Answered && Now - m.Time < 3 * SecondsPerDay)) continue;
                if (TribeRandom(t.Id, 3) >= 0.15) continue;
                var leader = FindPlayer(t.LeaderId);
                Write(MessageKind.Invitation, leader, $"An invitation from {t.Name} [{t.Tag}]", Pick(4,
                    $"We've watched you grow. Join {t.Name}: we defend our own, and we don't forget our friends.",
                    $"The realm is no place to stand alone. {t.Name} has room for you.",
                    $"You're a neighbor worth having on our side. Will you join {t.Name}?"), t.Id, tribeId: t.Id);
                return;
            }
        }

        /// <summary>The human answers a message that asks something. Returns whether the answer did anything.</summary>
        public bool AnswerMessage(int messageId, bool yes)
        {
            var m = FindMessage(messageId);
            var human = HumanPlayer;
            if (m == null || m.Answered || human == null) return false;
            m.Answered = true;
            m.Read = true;
            if (!yes)
            {
                if (m.Kind == MessageKind.FactionInvite) AnswerFactionInvite(m, false);
                if (m.Kind == MessageKind.FeedRequest) AnswerFeedRequest(m, false);
                return true;
            }
            switch (m.Kind)
            {
                case MessageKind.Invitation:
                    var inviting = FindTribe(m.A);
                    // A faction's winning side makes room for the human (its weakest goes).
                    if (inviting != null && !HasRoom(inviting) && OnCoreSide(inviting) && WeakestMember(inviting) is Player makeWay
                        && PointsOf(makeWay) < PointsOf(human)) LeaveTribe(makeWay);
                    return JoinTribe(human, inviting);
                case MessageKind.FactionInvite:
                    return AnswerFactionInvite(m, true);
                case MessageKind.FeedRequest:
                    return AnswerFeedRequest(m, true);
                case MessageKind.PactOffer:
                    var mine = TribeOf(human);
                    var theirs = FindTribe(m.A);
                    if (mine == null || theirs == null || mine.LeaderId != human.Id) return false;
                    if ((RelationKind)m.B == RelationKind.Ally && !CanAlly(mine, theirs)) return false;
                    SetRelation(mine, theirs, (RelationKind)m.B);
                    CheckVictory();
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The human, leading a tribe, changes how it stands with another: war is declared at once, and pacts ended
        /// at once; a pact offered is answered by the other tribe's leader at the next tick.
        /// </summary>
        public bool ProposeRelation(Tribe other, RelationKind kind)
        {
            var human = HumanPlayer;
            var mine = TribeOf(human);
            if (mine == null || other == null || other == mine || mine.LeaderId != human.Id) return false;
            var now = Relation(mine, other);
            if (kind == RelationKind.Enemy || kind == RelationKind.Neutral)
            {
                SetRelation(mine, other, kind);
                if (kind == RelationKind.Enemy && (now == RelationKind.Ally || now == RelationKind.NonAggression)) human.Reputation -= 5;
                return true;
            }
            // Offers are weighed like any tribe's: they like allies against a common enemy, and dislike liars.
            bool yes = human.Reputation >= -20 && (kind != RelationKind.Ally || CanAlly(mine, other)) && TribeRandom(other.Id, 4) < AcceptChance(other, mine, kind);
            var leader = FindPlayer(other.LeaderId);
            if (yes) SetRelation(mine, other, kind);
            Write(MessageKind.Note, leader, yes ? $"{(kind == RelationKind.Ally ? "Alliance" : "Pact")} with {other.Name}" : $"Your offer to {other.Name}",
                yes ? Pick(5, "Agreed. Don't make us regret it.", "We accept. Our enemies are yours.", "So be it.")
                    : human.Reputation < -20 ? "Your word is worth nothing to us." : Pick(6, "We decline.", "Not yet.", "We don't need you."), tribeId: other.Id);
            CheckVictory();
            return yes;
        }

        /// <summary>How likely a tribe is to accept a pact with another.</summary>
        double AcceptChance(Tribe self, Tribe other, RelationKind kind)
        {
            if (Relation(self, other) == RelationKind.Enemy) return 0.15;
            bool commonEnemy = false;
            foreach (var t in ActiveTribes())
                if (Relation(self, t) == RelationKind.Enemy && Relation(other, t) == RelationKind.Enemy) commonEnemy = true;
            double chance = kind == RelationKind.NonAggression ? 0.6 : 0.3;
            if (commonEnemy) chance += 0.4;
            if (BlocShare(other) >= BlocFearShare) chance -= 0.3; // wary of the one who's winning
            return Math.Max(0.02, Math.Min(0.95, chance));
        }

        /// <summary>
        /// How the human's tribe sees them, and how the human's AI members feel: warnings first, then someone goes.
        /// </summary>
        void HumanStanding()
        {
            var human = HumanPlayer;
            var tribe = TribeOf(human);
            if (tribe == null) return;
            var leader = FindPlayer(tribe.LeaderId);
            if (tribe.LeaderId != human.Id)
            {
                // A member who never helps: warned, then shown the door.
                if (human.Satisfaction < 15)
                {
                    LeaveTribe(human);
                    human.Reputation -= 5;
                    Write(MessageKind.Note, leader, $"Expelled from {tribe.Name}", "We called for help and you never came. You're on your own now.", tribeId: tribe.Id);
                }
                else if (human.Satisfaction < 30 && !RecentlyWarned(leader?.Id ?? -1))
                    Write(MessageKind.Note, leader, "A word of warning", "Your tribe mates have asked for help and got none from you. Do better, or find another tribe.", tribeId: tribe.Id);
                return;
            }
            // The human leads: unhappy members say so, then leave.
            foreach (int id in new List<int>(tribe.Members))
            {
                var member = FindPlayer(id);
                if (member == null || member.IsHuman) continue;
                if (member.Satisfaction < 12)
                {
                    LeaveTribe(member);
                    Write(MessageKind.Note, member, $"Leaving {tribe.Name}", Pick(7,
                        "I was attacked and no one came. I'll look after myself from now on.",
                        "This tribe does nothing for me. Goodbye.",
                        "I've had enough. Don't expect my help either."), tribeId: tribe.Id);
                }
                else if (member.Satisfaction < 25 && !RecentlyWarned(member.Id))
                    Write(MessageKind.Note, member, "I'm not happy", Pick(8,
                        "I was left to fend for myself. If it happens again, I'm gone.",
                        "What is this tribe for, if nobody helps anybody?",
                        "Some of us are thinking of leaving. Show us you care."), tribeId: tribe.Id);
            }
        }

        bool RecentlyWarned(int fromId) =>
            Messages.Exists(m => m.FromPlayerId == fromId && m.Kind == MessageKind.Note && Now - m.Time < 3 * SecondsPerDay);
    }
}
