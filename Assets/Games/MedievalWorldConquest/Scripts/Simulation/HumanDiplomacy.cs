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
                    // (A peace offer only means something while the war is still on.)
                    if ((RelationKind)m.B == RelationKind.Neutral && Relation(mine, theirs) != RelationKind.Enemy) return false;
                    SetRelation(mine, theirs, (RelationKind)m.B);
                    CheckVictory();
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The human, leading a tribe, changes how it stands with another: war is declared at once, and pacts ended
        /// at once; peace, a pact or an alliance is offered, and the other tribe's leader answers by message.
        /// Returns whether the change was made.
        /// </summary>
        public bool ProposeRelation(Tribe other, RelationKind kind)
        {
            var human = HumanPlayer;
            var mine = TribeOf(human);
            if (mine == null || other == null || other == mine || mine.LeaderId != human.Id) return false;
            var now = Relation(mine, other);
            if (kind == RelationKind.Neutral && now == RelationKind.Enemy) return OfferPeace(mine, other);
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

        /// <summary>How long a tribe that turned down the human's peace offer won't hear another (game seconds).</summary>
        public const double PeaceOfferCooldown = SecondsPerDay;

        /// <summary>When the human's tribe may next offer peace to a tribe it's at war with (now or earlier: it may).</summary>
        public double NextPeaceOffer(Tribe mine, Tribe other)
        {
            var r = mine != null && other != null ? FindRelation(mine.Id, other.Id) : null;
            return r == null || r.PeaceOfferedAt <= 0 ? Now : r.PeaceOfferedAt + PeaceOfferCooldown;
        }

        /// <summary>The human offers peace to a tribe at war with theirs: its leader weighs it, and says why.</summary>
        bool OfferPeace(Tribe mine, Tribe other)
        {
            var r = FindRelation(mine.Id, other.Id);
            if (r == null || NextPeaceOffer(mine, other) > Now) return false;
            r.PeaceOfferedAt = Now;
            var (yes, why) = WeighPeace(other, mine);
            if (yes) SetRelation(mine, other, RelationKind.Neutral);
            Write(MessageKind.Note, FindPlayer(other.LeaderId), yes ? $"Peace with {other.Name}" : $"Your peace offer to {other.Name}", why, tribeId: other.Id);
            return yes;
        }

        /// <summary>
        /// Whether a tribe would rather end its war with another, from its own point of view, and the main reason
        /// why (in its leader's words). For peace: losing the war (villages taken from it, more troops lost),
        /// facing a stronger tribe, a long war, other wars to fight (above all against a common enemy), living far
        /// apart, a peaceable leader. Against: winning it, competing for the same land, a war just begun, a warlike
        /// leader, an enemy who breaks its word, or one whose side is close to winning the world. Across the factions
        /// of a split realm, there's no peace.
        /// </summary>
        public (bool yes, string why) WeighPeace(Tribe self, Tribe other)
        {
            var r = FindRelation(self.Id, other.Id);
            if (r == null || r.Kind != RelationKind.Enemy) return (true, "We aren't at war.");
            var leader = FindPlayer(self.LeaderId);
            var otherLeader = FindPlayer(other.LeaderId);
            if (FactionsFormed && self.FactionId >= 0 && other.FactionId >= 0 && self.FactionId != other.FactionId)
                return (false, "The realm is split, and you stand on the other side. There will be no peace until it's decided.");
            if (otherLeader != null && otherLeader.IsHuman && otherLeader.Reputation < -20)
                return (false, "Your word is worth nothing to us. We'll keep fighting.");

            bool selfIsA = self.Id == r.A;
            int taken = selfIsA ? r.TakenByA : r.TakenByB, lost = selfIsA ? r.TakenByB : r.TakenByA;
            long troopsLost = selfIsA ? r.LostByA : r.LostByB, troopsKilled = selfIsA ? r.LostByB : r.LostByA;
            int sSelf = Math.Max(1, TribeStrength(self).points), sOther = Math.Max(1, TribeStrength(other).points);
            var (sx, sy) = TribeCenter(self);
            var (ox, oy) = TribeCenter(other);
            double distance = Math.Sqrt((sx - ox) * (sx - ox) + (sy - oy) * (sy - oy));
            double days = (Now - r.Since) / SecondsPerDay;

            // Each reason pulls for peace (+) or against it (-); the strongest one is what the leader says.
            var reasons = new List<(double weight, string words)>();
            int villages = lost - taken;
            if (villages != 0)
                reasons.Add((Math.Max(-0.6, Math.Min(0.6, 0.15 * villages)), villages > 0
                    ? $"You've taken {lost} of our villages. Enough blood has been spilled."
                    : $"We've taken {taken} of your villages and we're not finished. Why would we stop now?"));
            if (troopsLost + troopsKilled > 500)
            {
                double ratio = Math.Log((troopsLost + 1000.0) / (troopsKilled + 1000.0));
                reasons.Add((Math.Max(-0.4, Math.Min(0.4, 0.4 * ratio)), ratio > 0
                    ? "Our armies have bled more than yours. We'll take peace."
                    : "Your armies break on ours. Peace would only save you."));
            }
            double strength = Math.Log((double)sOther / sSelf, 2);
            if (Math.Abs(strength) > 0.3)
                reasons.Add((Math.Max(-0.3, Math.Min(0.3, 0.3 * strength)), strength > 0
                    ? "You're stronger than we'd like. Peace suits us."
                    : "We're stronger than you, and we both know it."));
            if (distance < 15) reasons.Add((-0.25, "You sit on land we mean to have."));
            else if (distance > 35) reasons.Add((0.15, "Your lands are far from ours. There's little to fight over."));
            Tribe busy = null, common = null;
            foreach (var t in ActiveTribes())
            {
                if (t == self || t == other || Relation(self, t) != RelationKind.Enemy) continue;
                if (Relation(other, t) == RelationKind.Enemy) common = t;
                else if (busy == null || TribeStrength(t).points > TribeStrength(busy).points) busy = t;
            }
            if (common != null) reasons.Add((0.35, $"We have a common enemy in {common.Name}. Let's turn on them instead."));
            if (busy != null) reasons.Add((0.25, $"Our real quarrel is with {busy.Name}. Peace with you suits us."));
            if (days < 1) reasons.Add((-0.3, "This war has barely begun."));
            else if (days > 5) reasons.Add((Math.Min(0.3, 0.3 * (days - 5) / 25), "This war has gone on long enough."));
            if (leader != null && Warlike(leader)) reasons.Add((-0.15, "Our warriors are still thirsty."));
            else if (leader != null && leader.Personality == AiPersonality.Defender) reasons.Add((0.15, "We never wanted this war."));
            if (BlocShare(other) >= BlocFearShare) reasons.Add((-0.4, "Your side is close to ruling the realm. We won't help you get there."));

            double score = (TribeRandom(self.Id * 31 + other.Id, (int)(Now / SecondsPerDay)) - 0.5) * 0.3;
            foreach (var (weight, _) in reasons) score += weight;
            bool yes = score > PeaceThreshold;
            string why = null;
            double best = 0;
            foreach (var (weight, words) in reasons)
                if ((yes ? weight : -weight) > best)
                {
                    best = yes ? weight : -weight;
                    why = words;
                }
            why ??= yes ? "Very well. Peace." : "We're not ready for peace.";
            return (yes, (yes ? "We accept. " : "We refuse. ") + why);
        }

        /// <summary>How much a tribe's reasons must favor peace for it to make peace.</summary>
        const double PeaceThreshold = 0.2;

        /// <summary>Records troops lost in a battle between tribes at war, in their war's ledger.</summary>
        void NoteWarLosses(int attacker, int attackerLost, int defender, int defenderLost)
        {
            var r = WarBetween(attacker, defender);
            if (r == null) return;
            bool attackerIsA = TribeOf(attacker).Id == r.A;
            if (attackerIsA) { r.LostByA += attackerLost; r.LostByB += defenderLost; }
            else { r.LostByB += attackerLost; r.LostByA += defenderLost; }
        }

        /// <summary>Records a village taken by one tribe from another it's at war with.</summary>
        void NoteWarConquest(int winner, int loser)
        {
            var r = WarBetween(winner, loser);
            if (r == null) return;
            if (TribeOf(winner).Id == r.A) r.TakenByA++;
            else r.TakenByB++;
        }

        /// <summary>The war between two players' tribes, if they're at war (on a diplomacy world).</summary>
        TribeRelation WarBetween(int playerA, int playerB)
        {
            if (!Diplomacy || playerA < 0 || playerB < 0) return null;
            var a = TribeOf(playerA);
            var b = TribeOf(playerB);
            if (a == null || b == null || a == b) return null;
            var r = FindRelation(a.Id, b.Id);
            return r != null && r.Kind == RelationKind.Enemy ? r : null;
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
