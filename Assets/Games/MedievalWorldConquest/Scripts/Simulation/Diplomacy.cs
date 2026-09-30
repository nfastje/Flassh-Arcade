using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// The life of tribes on a diplomacy world, worked out twice a game day (a <see cref="EventKind.TribeTick"/>):
    /// lords founding and joining tribes; invitations, answers and warnings to the human; tribes making pacts and
    /// war with each other; tribe leaders naming targets; and "social turbulence": the strains that build up in a
    /// tribe (members left alone under attack, a weak leader, an ambitious member who has outgrown it, members
    /// spread too far, a long dull peace, fear of a bloc about to win) and, now and then, break it: a member
    /// leaves, a group splinters off, the leadership changes hands, tribes merge or are absorbed, pacts sour. Calm
    /// tribes can last a whole world; strained ones rarely do. Also the tribe-and-allies ("bloc") victory.
    /// </summary>
    public partial class World
    {
        public const double TribeTickHours = 12;
        /// <summary>A bloc (a tribe and its allies) holding this share of the players' villages makes others uneasy.</summary>
        public const double BlocFearShare = 0.45;

        public int TribeTicks;
        /// <summary>An AI bloc reached the conquest goal (the player lost the world), which tribe led it, and whether the player has been told.</summary>
        public bool LostToBloc, LossShown;
        public int WinningTribeId = -1;

        /// <summary>Attacks between tribes since the last tick (not saved): grudges that sour relations.</summary>
        [NonSerialized] Dictionary<(int, int), int> incidents;

        void ScheduleTribeTick() => Schedule(TribeTickHours * 3600, EventKind.TribeTick);

        /// <summary>A repeatable random number for this tick, from the world seed and a salt.</summary>
        double TribeRandom(int a, int b = 0) => Terrain.Hash(Settings.Seed ^ 0x1656667B, a * 7919 + b, TribeTicks);

        static bool Warlike(Player p) => p.Personality == AiPersonality.Warlord || p.Personality == AiPersonality.Raider;

        static bool CanJoinTribes(Player p) =>
            p != null && !p.Quit && p.Personality != AiPersonality.Inactive;

        void TribeTick(ScheduledEvent e)
        {
            TribeTicks++;
            ScheduleTribeTick();
            if (!Diplomacy) return;
            double days = TribeTickHours / 24;

            ExpireHelpCalls();
            FormTribes();
            RecruitAndCull();
            BeatenLords(days);
            AnswerHuman();
            foreach (var t in ActiveTribes()) Turbulence(t, days);
            MergeAndAbsorb();
            Relate();
            FactionTick(days);
            NameTargets();
            HumanStanding();
            incidents?.Clear();
            var human = HumanPlayer;
            if (human != null && human.Reputation < 0) human.Reputation = Math.Min(0, human.Reputation + 1);
            CheckVictory();
        }

        // ---------------------------------------------------------------- forming and recruiting

        /// <summary>
        /// Lords without a tribe look for one: a tribe nearby with room takes them in; a strong lord with none near
        /// founds their own. Noobs join too, now and then; inactive players never.
        /// </summary>
        void FormTribes()
        {
            var tribes = ActiveTribes();
            foreach (var p in new List<Player>(Players))
            {
                if (p.IsHuman || p.TribeId >= 0 || !CanJoinTribes(p) || VillagesOf(p.Id).Count == 0) continue;
                double age = (Now - (p.ProtectedUntil - Settings.ProtectionDays * SecondsPerDay)) / SecondsPerDay;
                bool noob = p.Personality == AiPersonality.Noob;
                int points = PointsOf(p);
                if (age < 4 || points < (noob ? 150 : 250)) continue;

                // The most appealing tribe with room nearby that will have them: close, strong (the strong seek the
                // strong), and all the better if it's on the winning side.
                Tribe near = null;
                double nearest = double.MaxValue, bestAppeal = 0;
                foreach (var t in tribes)
                {
                    if (!HasRoom(t) || IsHumanLed(t)) continue;
                    double d = DistanceToTribe(p, t);
                    if (d > 20) continue;
                    double average = AveragePoints(t);
                    if (t.Members.Count >= 5 && points < 0.4 * average) continue; // too weak for them
                    double fit = Math.Sqrt(Math.Min(3, (average + 100) / (points + 100)));
                    double appeal = (1 + 4 * BlocShare(t)) * fit / (5 + d);
                    if (appeal > bestAppeal)
                    {
                        bestAppeal = appeal;
                        nearest = d;
                        near = t;
                    }
                }
                double roll = TribeRandom(p.Id, 1);
                if (near != null && nearest <= 20 && roll < (noob ? 0.1 : 0.25)) JoinTribe(p, near);
                else if (!noob && (near == null || nearest > 15) && roll < 0.08)
                {
                    var (name, tag) = NewTribeName(new Random(Settings.Seed * 31 + TribeTicks * 7 + p.Id));
                    tribes.Add(FoundTribe(p, name, tag));
                }
            }
        }

        public bool IsHumanLed(Tribe t) => t != null && FindPlayer(t.LeaderId)?.IsHuman == true;

        /// <summary>Days a lord stays put in a tribe before another may tempt them away.</summary>
        public const double PoachAfterDays = 20;

        /// <summary>How far (in fields) tribes look for recruits (half as far again once the world is locked), and how often (per tick) they try. (Tuning values.)</summary>
        public static double RecruitRange = 40, RecruitChance = 0.9;

        // The points a lord needs to be among the top tenth, worked out once a tick (not saved).
        [NonSerialized] int topLordPoints = int.MaxValue;
        [NonSerialized] double topLordPointsAt = -1;

        /// <summary>Whether a lord is among the strongest tenth of the lords still playing.</summary>
        bool IsTopLord(Player p)
        {
            if (topLordPointsAt != Now)
            {
                var all = new List<int>();
                foreach (var q in Players)
                    if (!q.IsHuman && !q.Quit && q.Personality != AiPersonality.Inactive && q.Personality != AiPersonality.Noob) all.Add(PointsOf(q));
                all.Sort();
                topLordPoints = all.Count == 0 ? int.MaxValue : all[(int)(all.Count * 0.9)];
                topLordPointsAt = Now;
            }
            return PointsOf(p) >= topLordPoints;
        }

        /// <summary>A tribe's members' average points.</summary>
        public double AveragePoints(Tribe t) => t.Members.Count == 0 ? 0 : TribeStrength(t).points / (double)t.Members.Count;

        /// <summary>
        /// Tribes that rise, as in Tribal Wars: a tribe with room keeps recruiting until it's full, taking the
        /// strongest lord nearby who's at least half as strong as its average member (the tribeless, and those in
        /// weaker tribes, who are tempted all the more if they're unhappy where they are, or if the recruiter is on
        /// the winning side), and a full tribe lets its much weakest members go to make room. (The human is never
        /// dropped for being weak.)
        /// </summary>
        void RecruitAndCull()
        {
            var tribes = ActiveTribes();
            var averages = new Dictionary<int, double>();
            foreach (var t in tribes) averages[t.Id] = AveragePoints(t);
            var winning = LeadingBloc();
            double range = WorldLocked ? 1.5 * RecruitRange : RecruitRange;
            var refused = new HashSet<int>();
            foreach (var t in tribes)
            {
                if (t.Disbanded || IsHumanLed(t)) continue;
                double average = averages[t.Id];

                // Room for someone better: the weakest goes when the tribe is full.
                if (t.Members.Count >= MaxTribeMembers - 1 && TribeRandom(t.Id, 20) < 0.4)
                {
                    Player weakest = null;
                    int weakestPoints = int.MaxValue;
                    foreach (int id in t.Members)
                    {
                        var m = FindPlayer(id);
                        if (m == null || m.IsHuman || id == t.LeaderId) continue;
                        int pts = PointsOf(m);
                        if (pts < weakestPoints)
                        {
                            weakestPoints = pts;
                            weakest = m;
                        }
                    }
                    if (weakest != null && weakestPoints < 0.25 * average)
                    {
                        LeaveTribe(weakest);
                        Log("dropped a weak member");
                    }
                }
                // A small tribe tries twice a tick, a bigger one once.
                int tries = t.Members.Count < MaxTribeMembers / 2 ? (winning.Contains(t.Id) ? 3 : 2) : 1;
                for (int attempt = 0; attempt < tries; attempt++)
                {
                    if (!HasRoom(t) || TribeRandom(t.Id, 21 + 10 * attempt) >= RecruitChance) break;
                    var (cx, cy) = TribeCenter(t);
                    Player best = null;
                    double bestPoints = 0.5 * average;
                    foreach (var p in Players)
                    {
                        if (p.IsHuman || p.Quit || p.TribeId == t.Id || p.Personality == AiPersonality.Inactive || refused.Contains(p.Id)) continue;
                        var own = VillagesOf(p.Id);
                        if (own.Count == 0) continue;
                        double dx = own[0].X - cx, dy = own[0].Y - cy;
                        if (dx * dx + dy * dy > range * range) continue;
                        var theirs = TribeOf(p);
                        // Poaching is for clear upgrades only: leaders don't leave their own tribes, lords who joined
                        // lately are settling in, allies' members and the winning side's aren't tempted away, and a
                        // lord only goes to a tribe at least twice as strong, member for member.
                        if (theirs != null && (theirs.LeaderId == p.Id || Now - p.JoinedTribeAt < PoachAfterDays * SecondsPerDay
                                               || Relation(t, theirs) == RelationKind.Ally || winning.Contains(theirs.Id) && !winning.Contains(t.Id)
                                               || averages.GetValueOrDefault(theirs.Id) > 0.5 * average)) continue;
                        int pts = PointsOf(p);
                        if (pts > bestPoints)
                        {
                            bestPoints = pts;
                            best = p;
                        }
                    }
                    if (best == null) break;
                    var current = TribeOf(best);
                    // The very best lords go where the power is; others need more persuading, less so by the winning side.
                    double chance = IsTopLord(best) ? 0.8 : current == null ? 0.6 : 0.3 + (best.Satisfaction < 50 ? 0.2 : 0);
                    if (winning.Contains(t.Id)) chance = Math.Min(0.95, chance + 0.15);
                    if (TribeRandom(best.Id, 22 + attempt) >= chance)
                    {
                        refused.Add(best.Id);
                        continue;
                    }
                    JoinTribe(best, t);
                    averages[t.Id] = average = AveragePoints(t);
                    Log(current == null ? "recruited" : "poached");
                }
            }
        }

        /// <summary>The tribes on the side holding the most of the world (none before anyone holds a tenth of it).</summary>
        HashSet<int> LeadingBloc()
        {
            Tribe leading = null;
            double leadingShare = 0.1;
            foreach (var t in ActiveTribes())
            {
                double share = BlocShare(t);
                if (share > leadingShare)
                {
                    leadingShare = share;
                    leading = t;
                }
            }
            return leading != null ? BlocOf(leading) : new HashSet<int>();
        }

        /// <summary>
        /// Once the world is locked, lords who are being beaten (down to 60% of the most villages they held or fewer,
        /// with one lost in the last 5 days) lose heart, as in Tribal Wars. The winning side takes in only those worth
        /// more than its weakest member (who makes way for them); the rest give up now and then and leave their
        /// villages to the barbarians. (Lords on the winning side don't.)
        /// </summary>
        void BeatenLords(double days)
        {
            var winning = WorldLocked ? LeadingBloc() : null;
            foreach (var p in new List<Player>(Players))
            {
                if (p.IsHuman || p.Quit || p.Personality == AiPersonality.Inactive || p.Personality == AiPersonality.Noob) continue;
                int held = VillagesOf(p.Id).Count;
                if (held > p.PeakVillages) p.PeakVillages = held;
                if (winning == null || held == 0 || winning.Contains(p.TribeId) || p.PeakVillages < 3 || held > 0.6 * p.PeakVillages
                    || p.LostVillageAt < 0 || Now - p.LostVillageAt > 5 * SecondsPerDay) continue;
                if (TribeRandom(p.Id, 30) >= 0.15 * days) continue;
                int points = PointsOf(p);
                Tribe haven = null;
                Player makesWay = null;
                double nearest = 30;
                foreach (int id in winning)
                {
                    var t = FindTribe(id);
                    if (t == null || t.Disbanded || IsHumanLed(t)) continue;
                    double d = DistanceToTribe(p, t);
                    var weakest = WeakestMember(t);
                    if (d > nearest || weakest == null || PointsOf(weakest) >= points) continue;
                    nearest = d;
                    haven = t;
                    makesWay = weakest;
                }
                if (haven != null && TribeRandom(p.Id, 31) < 0.5)
                {
                    if (!HasRoom(haven)) LeaveTribe(makesWay);
                    JoinTribe(p, haven);
                    Log("went over to the winners");
                }
                else if (TribeRandom(p.Id, 32) < 0.5)
                {
                    QuitLord(p);
                    Log("gave up");
                }
            }
        }

        /// <summary>A tribe's weakest member other than its leader and the human (null if there's none).</summary>
        Player WeakestMember(Tribe t)
        {
            Player weakest = null;
            int weakestPoints = int.MaxValue;
            foreach (int id in t.Members)
            {
                var m = FindPlayer(id);
                if (m == null || m.IsHuman || id == t.LeaderId) continue;
                int pts = PointsOf(m);
                if (pts < weakestPoints)
                {
                    weakestPoints = pts;
                    weakest = m;
                }
            }
            return weakest;
        }

        // ---------------------------------------------------------------- the human and the tribes

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

        // ---------------------------------------------------------------- calls for help

        /// <summary>
        /// When an attack is sent at a tribe member's village (anything but a small raid, unless it's the human's),
        /// the tribe is asked to help; the human, if a tribe mate, gets a message (not too many).
        /// </summary>
        void OnAttackSent(Command c, Village from, Village to)
        {
            if (!Diplomacy || to.IsBarbarian) return;
            var victim = FindPlayer(to.OwnerId);
            var tribe = TribeOf(victim);
            if (tribe != null && c.OwnerId != to.OwnerId)
            {
                var attackerTribe = TribeOf(c.OwnerId);
                if (attackerTribe != null) CountIncident(attackerTribe, tribe);
                int power = 0;
                for (int i = 0; i < Units.Count && i < c.Troops.Length; i++) power += c.Troops[i] * Units.Get((UnitType)i).Attack;
                if ((power >= 500 || victim.IsHuman) && tribe.HelpCalls.Count < 30)
                {
                    tribe.HelpCalls.Add(new HelpCall { VillageId = to.Id, OwnerId = victim.Id, AttackerId = c.OwnerId, ArriveTime = c.ArriveTime });
                    var human = HumanPlayer;
                    if (human != null && human.TribeId == tribe.Id && !victim.IsHuman && SupportRequestsToday() < 4
                        && !Messages.Exists(m => m.Kind == MessageKind.SupportRequest && m.A == to.Id && Now - m.Time < 12 * 3600))
                        Write(MessageKind.SupportRequest, victim, $"Attack on {to.Name} ({to.X}|{to.Y})",
                            $"{NameWithTag(FindPlayer(c.OwnerId))} is attacking my village. It lands {FormatClock(c.ArriveTime)}. Can you send support?", to.Id, tribeId: tribe.Id);
                }
            }
            // The human breaking faith: attacking a tribe mate or a friendly tribe.
            if (FindPlayer(c.OwnerId)?.IsHuman == true && victim != null && AreFriendly(c.OwnerId, victim.Id)) HumanBetrays(victim);
        }

        int SupportRequestsToday() => Messages.FindAll(m => m.Kind == MessageKind.SupportRequest && Now - m.Time < SecondsPerDay).Count;

        void CountIncident(Tribe a, Tribe b)
        {
            if (a == b) return;
            if (incidents == null) incidents = new Dictionary<(int, int), int>();
            var key = (Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id));
            incidents[key] = (incidents.TryGetValue(key, out int n) ? n : 0) + 1;
        }

        int Incidents(Tribe a, Tribe b) =>
            incidents != null && incidents.TryGetValue((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)), out int n) ? n : 0;

        /// <summary>The human attacked a friend: a tribe mate (expelled, or the victim leaves the human's tribe) or a friendly tribe (the pact is broken).</summary>
        void HumanBetrays(Player victim)
        {
            var human = HumanPlayer;
            var mine = TribeOf(human);
            var theirs = TribeOf(victim);
            if (mine == theirs)
            {
                human.Reputation -= 25;
                if (mine.LeaderId == human.Id)
                {
                    LeaveTribe(victim);
                    Write(MessageKind.Note, victim, "Traitor!", "You attack your own tribe mate? I'm leaving, and everyone will hear of it.", tribeId: mine.Id);
                }
                else
                {
                    LeaveTribe(human);
                    Write(MessageKind.Note, FindPlayer(mine.LeaderId), $"Expelled from {mine.Name}", "We don't attack our own. You're out.", tribeId: mine.Id);
                }
                return;
            }
            human.Reputation -= 15;
            SetRelation(mine, theirs, RelationKind.Enemy);
            Write(MessageKind.Note, FindPlayer(theirs.LeaderId), "Betrayal", $"You broke your word. {theirs.Name} is at war with {mine.Name}.", tribeId: theirs.Id);
        }

        /// <summary>
        /// The human sends support: if it goes to a tribe mate whose village is under attack, the tribe notices.
        /// </summary>
        void OnSupportSent(Command c, Village to)
        {
            if (!Diplomacy) return;
            var sender = FindPlayer(c.OwnerId);
            var tribe = TribeOf(sender);
            if (tribe == null || to.OwnerId == c.OwnerId) return;
            foreach (var call in tribe.HelpCalls)
                if (call.VillageId == to.Id && call.ArriveTime > Now)
                {
                    call.Supporters++;
                    if (sender.IsHuman) sender.Satisfaction = Math.Min(100, sender.Satisfaction + 8);
                    var helped = FindPlayer(call.OwnerId);
                    if (helped != null) helped.Satisfaction = Math.Min(100, helped.Satisfaction + 6);
                    tribe.Tension = Math.Max(0, tribe.Tension - 3);
                    return;
                }
        }

        /// <summary>
        /// Attacks that have landed: nobody went to help, and the tribe feels it (the victim most); the human, asked
        /// and absent, is noticed too.
        /// </summary>
        void ExpireHelpCalls()
        {
            var human = HumanPlayer;
            foreach (var tribe in ActiveTribes())
                for (int i = tribe.HelpCalls.Count - 1; i >= 0; i--)
                {
                    var call = tribe.HelpCalls[i];
                    if (call.ArriveTime > Now) continue;
                    tribe.HelpCalls.RemoveAt(i);
                    if (call.Supporters > 0) continue;
                    var victim = FindPlayer(call.OwnerId);
                    if (victim != null) victim.Satisfaction = Math.Max(0, victim.Satisfaction - 10);
                    tribe.AbandonedSinceTick++;
                    if (human != null && human.TribeId == tribe.Id && victim != null && !victim.IsHuman
                        && Messages.Exists(m => m.Kind == MessageKind.SupportRequest && m.A == call.VillageId && Now - m.Time < SecondsPerDay))
                        human.Satisfaction = Math.Max(0, human.Satisfaction - 8);
                }
        }

        // ---------------------------------------------------------------- turbulence

        /// <summary>Adds strain to a tribe, remembering what's straining it most.</summary>
        void AddTension(Tribe t, double amount, TensionCause cause)
        {
            t.Tension = Math.Max(0, Math.Min(100, t.Tension + amount));
            if (amount > 0 && amount >= 1) t.MainCause = cause;
        }

        /// <summary>
        /// A tribe's strains and comforts over the last half day, then (rarely, more often under strain) something
        /// gives. The chance of a break each game day is 0.4% at no tension, about 8% at 50, 30% at 100.
        /// </summary>
        void Turbulence(Tribe t, double days)
        {
            t.Members.RemoveAll(id => { var p = FindPlayer(id); return p == null || p.Quit || p.TribeId != t.Id; });
            if (t.Members.Count == 0)
            {
                Disband(t);
                return;
            }
            var leader = FindPlayer(t.LeaderId);
            if (leader == null || leader.Quit || !t.Members.Contains(leader.Id) || VillagesOf(leader.Id).Count == 0)
            {
                // A leaderless tribe: the strongest takes over (with some strain).
                leader = StrongestMember(t);
                if (leader == null)
                {
                    Disband(t);
                    return;
                }
                t.LeaderId = leader.Id;
                AddTension(t, 10, TensionCause.WeakLeader);
            }

            // Strains. (A tribe in a long war gets used to it: attacks left unanswered and villages lost strain it,
            // but only so much in any half day.)
            AddTension(t, Math.Min(6, 3 * t.AbandonedSinceTick), TensionCause.Abandoned);
            AddTension(t, Math.Min(8, 4 * t.LossesSinceTick), TensionCause.Losing);
            t.AbandonedSinceTick = 0;
            var strongest = StrongestMember(t);
            int leaderPoints = PointsOf(leader), strongestPoints = strongest != null ? PointsOf(strongest) : 0;
            if (strongest != null && strongest != leader)
            {
                if (leaderPoints < 0.5 * strongestPoints) AddTension(t, 2.5 * days, TensionCause.WeakLeader);
                if (Warlike(strongest) && !strongest.IsHuman && strongestPoints > 1.6 * leaderPoints) AddTension(t, 3 * days, TensionCause.Ambition);
            }
            if (t.Members.Count >= 3 && Spread(t) > 22) AddTension(t, 2 * days, TensionCause.Spread);
            // Big tribes are hard to hold together: cliques form.
            if (t.Members.Count > 10) AddTension(t, 0.4 * (t.Members.Count - 10) * days, TensionCause.Spread);
            bool atWar = Relations.Exists(r => r.Kind == RelationKind.Enemy && (r.A == t.Id || r.B == t.Id));
            int warlike = 0;
            foreach (int id in t.Members) if (Warlike(FindPlayer(id))) warlike++;
            if (!atWar && warlike >= 2 && Now - t.Founded > 15 * SecondsPerDay) AddTension(t, 1.2 * days, TensionCause.Restless);
            // Fear of being swallowed: only for a small partner in a bloc near the top, and only a little. Winning
            // should be hard, not impossible.
            // (Not once it's on a side holding the world's biggest share: by then its partners are in it together.)
            double blocShare = BlocShare(t);
            bool winningSide = blocShare >= WinningSideShare;
            var bloc = BiggestBlocAround(t);
            if (!winningSide && bloc != null && bloc != t && BlocShare(bloc) >= BlocFearShare && JuniorIn(t, bloc)) AddTension(t, 1.5 * days, TensionCause.Fear);

            // Being on the winning side: a bloc that's growing keeps its tribes content, and a big one all the more.
            // Sides that are winning rarely fall apart: success holds them together.
            if (blocShare > t.LastBlocShare + 0.002) t.Tension -= 2 * days;
            if (blocShare >= 0.2) t.Tension -= 1 * days;
            if (winningSide) t.Tension -= 3 * days;
            t.LastBlocShare = blocShare;

            // Comforts.
            t.Tension -= 3 * t.ConquestsSinceTick;
            if (atWar) t.Tension -= 2 * days;          // a common enemy pulls a tribe together
            if (strongest == leader) t.Tension -= 1 * days;
            if (Now - t.Founded > 20 * SecondsPerDay) t.Tension -= 0.5 * days; // old tribes have their habits
            t.Tension = Math.Max(0, Math.Min(100, t.Tension * Math.Pow(0.93, days)));
            t.LossesSinceTick = t.ConquestsSinceTick = 0;

            // Members' moods drift back towards content.
            foreach (int id in t.Members)
            {
                var m = FindPlayer(id);
                if (m != null && !m.IsHuman) m.Satisfaction += (60 - m.Satisfaction) * 0.05 * days;
            }

            double perDay = (0.004 + 0.3 * Math.Pow(t.Tension / 100, 2)) * (winningSide ? 0.3 : 1);
            if (TribeRandom(t.Id, 5) < 1 - Math.Pow(1 - perDay, days)) Break(t);
            else if (TribeRandom(t.Id, 6) < 0.0008 * days * warlike * (1 + t.Tension / 25)) Betrayal(t);
        }

        /// <summary>A side holding this share of the world or more is winning: its tribes hold together, and its leader wants strong partners.</summary>
        public const double WinningSideShare = 0.3;

        /// <summary>Whether a tribe holds only a small part (under a quarter) of its bloc's villages.</summary>
        bool JuniorIn(Tribe t, Tribe blocTribe)
        {
            CountTribeVillages();
            int own = tribeVillages.TryGetValue(t.Id, out int n) ? n : 0, all = 0;
            foreach (int id in BlocOf(blocTribe)) all += tribeVillages.TryGetValue(id, out int m) ? m : 0;
            return all > 0 && own < 0.25 * all;
        }

        /// <summary>How spread out a tribe is: its members' average distance (in fields) from its middle.</summary>
        double Spread(Tribe t)
        {
            var (cx, cy) = TribeCenter(t);
            double sum = 0;
            int n = 0;
            foreach (int id in t.Members)
            {
                var own = VillagesOf(id);
                if (own.Count == 0) continue;
                sum += Math.Sqrt((own[0].X - cx) * (own[0].X - cx) + (own[0].Y - cy) * (own[0].Y - cy));
                n++;
            }
            return n == 0 ? 0 : sum / n;
        }

        /// <summary>The event statistics, for tuning (not saved): how often each kind of break has happened.</summary>
        [NonSerialized] public Dictionary<string, int> TurbulenceLog = new Dictionary<string, int>();

        void Log(string what)
        {
            if (TurbulenceLog == null) TurbulenceLog = new Dictionary<string, int>();
            TurbulenceLog[what] = (TurbulenceLog.TryGetValue(what, out int n) ? n : 0) + 1;
        }

        /// <summary>Something gives, shaped by what's been straining the tribe most.</summary>
        void Break(Tribe t)
        {
            var leader = FindPlayer(t.LeaderId);
            bool humanLeads = leader?.IsHuman == true;
            var cause = t.MainCause;
            if (cause == TensionCause.None)
                cause = (TensionCause)(1 + (int)(TribeRandom(t.Id, 7) * 7)); // a surprise: anything can happen
            // A big tribe under real strain tends to split into factions, whatever started it.
            if (t.Members.Count >= 8 && t.Tension >= 35 && TribeRandom(t.Id, 14) < 0.4
                && Splinter(t, StrongestMember(t, t.LeaderId) is Player rival && Warlike(rival))) return;
            // A tribe that keeps losing blames its leader.
            if (cause == TensionCause.Losing && !humanLeads && TribeRandom(t.Id, 15) < 0.3) cause = TensionCause.WeakLeader;
            switch (cause)
            {
                case TensionCause.WeakLeader when !humanLeads:
                    var challenger = StrongestMember(t, t.LeaderId);
                    if (challenger != null && !challenger.IsHuman)
                    {
                        t.LeaderId = challenger.Id;
                        Log("new leader");
                        TellHuman(t, challenger, $"{challenger.Name} leads {t.Name} now", $"{leader?.Name ?? "Our old leader"} is out. {challenger.Name} leads us now.");
                        t.Tension -= 30;
                        return;
                    }
                    break;
                case TensionCause.Ambition:
                case TensionCause.Spread:
                    if (t.Members.Count >= 4 && Splinter(t, cause == TensionCause.Ambition)) return;
                    break;
                case TensionCause.Restless when !humanLeads:
                    if (DeclareWarOnNeighbor(t)) return;
                    break;
                case TensionCause.Fear:
                    if (LeaveBloc(t)) return;
                    break;
            }
            // Otherwise (and for anyone left to fend for themselves): the unhappiest member leaves.
            MemberLeaves(t);
        }

        void TellHuman(Tribe t, Player from, string subject, string body)
        {
            var human = HumanPlayer;
            if (human != null && human.TribeId == t.Id) Write(MessageKind.Note, from, subject, body, tribeId: t.Id);
        }

        /// <summary>
        /// The unhappiest member (not the leader, not the human) leaves, to go it alone or to a tribe nearby; under
        /// heavy strain, a few go together.
        /// </summary>
        void MemberLeaves(Tribe t)
        {
            int leaving = 1 + (int)Math.Max(0, (t.Tension - 40) / 20);
            for (int i = 0; i < leaving && t.Members.Count > 1; i++) OneLeaves(t);
        }

        void OneLeaves(Tribe t)
        {
            Player unhappiest = null;
            foreach (int id in t.Members)
            {
                var p = FindPlayer(id);
                if (p == null || p.IsHuman || p.Id == t.LeaderId) continue;
                if (unhappiest == null || p.Satisfaction < unhappiest.Satisfaction) unhappiest = p;
            }
            if (unhappiest == null) return;
            LeaveTribe(unhappiest);
            Log("member left");
            t.Tension -= 25;
            TellHuman(t, unhappiest, $"{unhappiest.Name} has left", Pick(9,
                "I'm leaving. This tribe does nothing for me.", "Goodbye. I'll find friends who show up when it matters.",
                "I've had enough of this tribe."));
            // Perhaps to another tribe nearby (a neutral one, not an enemy of the old).
            foreach (var other in ActiveTribes())
                if (other != t && HasRoom(other) && !IsHumanLed(other) && Relation(other, t) != RelationKind.Enemy && DistanceToTribe(unhappiest, other) <= 20)
                {
                    if (TribeRandom(unhappiest.Id, 8) < 0.5) JoinTribe(unhappiest, other);
                    break;
                }
        }

        /// <summary>
        /// A group breaks away: behind an ambitious member (and those living near them), or the members far from the
        /// tribe's middle. They found their own tribe. Returns whether it happened.
        /// </summary>
        bool Splinter(Tribe t, bool ambition)
        {
            Player head = null;
            if (ambition) head = StrongestMember(t, t.LeaderId);
            else
            {
                var (cx, cy) = TribeCenter(t);
                double far = -1;
                foreach (int id in t.Members)
                {
                    var own = VillagesOf(id);
                    if (id == t.LeaderId || own.Count == 0) continue;
                    double d = Math.Sqrt((own[0].X - cx) * (own[0].X - cx) + (own[0].Y - cy) * (own[0].Y - cy));
                    if (d > far)
                    {
                        far = d;
                        head = FindPlayer(id);
                    }
                }
            }
            if (head == null || head.IsHuman) return false;
            var home = VillagesOf(head.Id);
            if (home.Count == 0) return false;

            // Those near the new head, and not too happy where they are, go with them.
            var followers = new List<Player>();
            foreach (int id in t.Members)
            {
                var p = FindPlayer(id);
                if (p == null || p == head || p.IsHuman || p.Id == t.LeaderId) continue;
                var own = VillagesOf(id);
                if (own.Count == 0) continue;
                if (Distance(own[0], home[0]) <= 15 && p.Satisfaction < 65 && followers.Count < t.Members.Count / 2) followers.Add(p);
            }
            var (name, tag) = NewTribeName(new Random(Settings.Seed * 17 + TribeTicks * 13 + head.Id));
            var splinter = FoundTribe(head, name, tag);
            foreach (var p in followers) JoinTribe(p, splinter);
            splinter.Tension = 10;
            // Some splits are bitter.
            SetRelation(t, splinter, TribeRandom(t.Id, 9) < 0.4 ? RelationKind.Enemy : RelationKind.Neutral);
            Log(ambition ? "splinter (ambition)" : "splinter (spread)");
            t.Tension -= 40;
            TellHuman(t, head, $"{head.Name} has split from {t.Name}", ambition
                ? $"I've outgrown this tribe. I'm founding {splinter.Name} [{splinter.Tag}], and {followers.Count} ride with me."
                : $"We're too far from the rest of you to matter. We're going our own way as {splinter.Name} [{splinter.Tag}].");
            return true;
        }

        /// <summary>A restless tribe picks a fight with a weaker neighbor. Returns whether it did.</summary>
        bool DeclareWarOnNeighbor(Tribe t)
        {
            var (points, _) = TribeStrength(t);
            var (cx, cy) = TribeCenter(t);
            Tribe victim = null;
            double best = double.MaxValue;
            foreach (var other in ActiveTribes())
            {
                // A restless tribe will break a pact for a fight, not an alliance.
                var standing = Relation(t, other);
                if (other == t || (standing != RelationKind.Neutral && standing != RelationKind.NonAggression)) continue;
                var (ox, oy) = TribeCenter(other);
                double d = Math.Sqrt((ox - cx) * (ox - cx) + (oy - cy) * (oy - cy));
                if (d > 40 || TribeStrength(other).points > points * 0.9) continue;
                if (d < best)
                {
                    best = d;
                    victim = other;
                }
            }
            if (victim == null) return false;
            SetRelation(t, victim, RelationKind.Enemy);
            Log("war from restlessness");
            t.Tension -= 20;
            AnnounceRelation(t, victim, RelationKind.Enemy);
            return true;
        }

        /// <summary>A tribe that fears the bloc it belongs to (or its ally's bloc) walks out of the alliance. Returns whether it did.</summary>
        bool LeaveBloc(Tribe t)
        {
            foreach (var ally in AlliesOf(t))
                if (BlocShare(ally) >= BlocFearShare && TribeStrength(ally).points > TribeStrength(t).points)
                {
                    if (IsHumanLed(t)) return false;
                    SetRelation(t, ally, RelationKind.Neutral);
                    Log("left a bloc");
                    t.Tension -= 30;
                    AnnounceRelation(t, ally, RelationKind.Neutral);
                    return true;
                }
            return false;
        }

        /// <summary>A member turns on a tribe mate: expelled, with a grudge between them. Rare.</summary>
        void Betrayal(Tribe t)
        {
            foreach (int id in t.Members)
            {
                var traitor = FindPlayer(id);
                if (traitor == null || traitor.IsHuman || !Warlike(traitor) || id == t.LeaderId) continue;
                LeaveTribe(traitor);
                // Its victim-to-be remembers.
                var victim = FindPlayer(t.LeaderId);
                if (victim != null && !victim.IsHuman)
                {
                    victim.LastAttackerId = traitor.Id;
                    victim.LastAttackedAt = Now;
                }
                traitor.LastAttackerId = t.LeaderId;
                Log("betrayal");
                t.Tension += 15;
                TellHuman(t, FindPlayer(t.LeaderId), $"{traitor.Name} betrayed us", $"{traitor.Name} has turned on the tribe and is expelled. Watch your villages near theirs.");
                return;
            }
        }

        /// <summary>
        /// Allied tribes merge when there's room (a small one into the bigger); a small tribe in turmoil breaks up
        /// into its neighbors; and a small tribe next to a much stronger one that isn't its enemy is swallowed by it
        /// (sooner if it's strained, or the stronger one is on the winning side).
        /// </summary>
        void MergeAndAbsorb()
        {
            var tribes = ActiveTribes();
            var winning = LeadingBloc();
            foreach (var a in tribes)
            {
                if (a.Disbanded || IsHumanLed(a)) continue;
                foreach (var b in AlliesOf(a))
                {
                    if (b.Disbanded || IsHumanLed(b) || Math.Min(a.Members.Count, b.Members.Count) > 8 || a.Members.Count + b.Members.Count > MaxTribeMembers) continue;
                    if (TribeRandom(a.Id * 131 + b.Id, 10) >= 0.03 * TribeTickHours / 24) continue;
                    var (big, small) = TribeStrength(a).points >= TribeStrength(b).points ? (a, b) : (b, a);
                    foreach (int id in new List<int>(small.Members))
                    {
                        var p = FindPlayer(id);
                        if (p != null) JoinTribe(p, big);
                    }
                    if (!small.Disbanded) Disband(small);
                    Log("merger");
                    AnnounceMerger(big, small);
                    break;
                }
                if (!a.Disbanded && a.Members.Count <= 3 && a.Tension >= 50 && TribeRandom(a.Id, 11) < 0.05 * TribeTickHours / 24)
                {
                    foreach (int id in new List<int>(a.Members))
                    {
                        var p = FindPlayer(id);
                        if (p == null || p.IsHuman) continue;
                        foreach (var other in tribes)
                            if (other != a && !other.Disbanded && HasRoom(other) && !IsHumanLed(other) && DistanceToTribe(p, other) <= 20)
                            {
                                JoinTribe(p, other);
                                break;
                            }
                    }
                    if (!a.Disbanded && a.Members.TrueForAll(id => FindPlayer(id)?.IsHuman != true)) Disband(a);
                    Log("absorbed");
                }
                if (!a.Disbanded) Swallow(a, tribes, winning);
            }
        }

        /// <summary>A small tribe (without the human in it) joins a much stronger neighbor that will have all of it.</summary>
        void Swallow(Tribe small, List<Tribe> tribes, HashSet<int> winning)
        {
            if (small.Members.Count > 6 || small.Members.Exists(id => FindPlayer(id)?.IsHuman == true)) return;
            bool smallWinning = winning.Contains(small.Id);
            int smallPoints = TribeStrength(small).points;
            var (sx, sy) = TribeCenter(small);
            Tribe big = null;
            int bigPoints = 0;
            foreach (var t in tribes)
            {
                if (t == small || t.Disbanded || IsHumanLed(t) || t.Members.Count + small.Members.Count > MaxTribeMembers || Relation(t, small) == RelationKind.Enemy) continue;
                if (smallWinning && !winning.Contains(t.Id)) continue; // the winners' partners don't go over to outsiders
                int pts = TribeStrength(t).points;
                if (pts < 2 * smallPoints || pts <= bigPoints) continue;
                var (tx, ty) = TribeCenter(t);
                if ((tx - sx) * (tx - sx) + (ty - sy) * (ty - sy) > 30 * 30) continue;
                big = t;
                bigPoints = pts;
            }
            if (big == null) return;
            double perDay = 0.04 + (small.Tension >= 40 ? 0.06 : 0) + (winning.Contains(big.Id) ? 0.04 : 0);
            if (TribeRandom(small.Id * 131 + big.Id, 15) >= perDay * TribeTickHours / 24) return;
            foreach (int id in new List<int>(small.Members))
            {
                var p = FindPlayer(id);
                if (p != null) JoinTribe(p, big);
            }
            if (!small.Disbanded) Disband(small);
            Log("swallowed");
            AnnounceMerger(big, small);
        }

        void AnnounceMerger(Tribe big, Tribe small)
        {
            var human = HumanPlayer;
            if (human != null && human.TribeId == big.Id)
                Write(MessageKind.Note, FindPlayer(big.LeaderId), $"{small.Name} has joined us", $"The lords of {small.Name} [{small.Tag}] are part of {big.Name} now.", tribeId: big.Id);
        }

        // ---------------------------------------------------------------- relations between tribes

        /// <summary>
        /// Tribes near each other make and break pacts and war: grudges from attacks, weaker tribes against a strong
        /// neighbor, common enemies, fear of a bloc that's nearly won (which draws a coalition against it). With the
        /// human's tribe, they offer pacts by message instead of just making them.
        /// </summary>
        void Relate()
        {
            var tribes = ActiveTribes();
            var centers = new Dictionary<int, (double x, double y)>();
            var strength = new Dictionary<int, int>();
            foreach (var t in tribes)
            {
                centers[t.Id] = TribeCenter(t);
                strength[t.Id] = TribeStrength(t).points;
            }
            Tribe feared = null;
            foreach (var t in tribes)
                if (BlocShare(t) >= BlocFearShare && (feared == null || strength[t.Id] > strength[feared.Id])) feared = t;
            // The side that's winning draws in the small and uncommitted (bandwagoning), while bigger tribes outside
            // it look to each other (the coalition, above).
            Tribe leading = null;
            double leadingShare = 0.1;
            foreach (var t in tribes)
            {
                double share = BlocShare(t);
                if (share > leadingShare)
                {
                    leadingShare = share;
                    leading = t;
                }
            }
            var leadingBloc = leading != null ? BlocOf(leading) : new HashSet<int>();

            for (int i = 0; i < tribes.Count; i++)
                for (int j = i + 1; j < tribes.Count; j++)
                {
                    var a = tribes[i];
                    var b = tribes[j];
                    if (a.Disbanded || b.Disbanded) continue;
                    var (ax, ay) = centers[a.Id];
                    var (bx, by) = centers[b.Id];
                    double d = Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
                    if (d > 45) continue;
                    bool humanA = IsHumanLed(a), humanB = IsHumanLed(b);
                    var now = Relation(a, b);
                    int fights = Incidents(a, b);
                    double r = TribeRandom(a.Id * 977 + b.Id, 12), scale = TribeTickHours / 24;
                    var la = FindPlayer(a.LeaderId);
                    var lb = FindPlayer(b.LeaderId);
                    int sa = strength[a.Id], sb = strength[b.Id];
                    bool commonEnemy = false;
                    foreach (var c in tribes)
                        if (c != a && c != b && Relation(a, c) == RelationKind.Enemy && Relation(b, c) == RelationKind.Enemy) commonEnemy = true;
                    bool coalition = feared != null && feared != a && feared != b && !AlliesOf(feared).Contains(a) && !AlliesOf(feared).Contains(b);

                    // Pacts are the first thing to go when a tribe is under strain.
                    double strain = Math.Max(a.Tension, b.Tension) / 100;
                    RelationKind? next = null;
                    switch (now)
                    {
                        case RelationKind.Neutral:
                            if (fights >= 3 && r < 0.5) next = RelationKind.Enemy;
                            else if (la != null && lb != null && (Warlike(la) && sa > 1.5 * sb || Warlike(lb) && sb > 1.5 * sa) && r < 0.04 * scale * 2) next = RelationKind.Enemy;
                            else if ((commonEnemy || coalition) && r < 0.15) next = RelationKind.Ally;
                            else if (fights == 0 && !(la != null && Warlike(la)) && !(lb != null && Warlike(lb)) && r < 0.05) next = RelationKind.NonAggression;
                            break;
                        case RelationKind.NonAggression:
                            if (fights >= 2 && r < 0.5) next = RelationKind.Enemy;
                            else if ((commonEnemy || coalition) && r < 0.12) next = RelationKind.Ally;
                            else if (r < 0.005 + 0.12 * strain * strain) next = RelationKind.Neutral;
                            break;
                        case RelationKind.Ally:
                            // Old alliances are sturdy: the longer they've lasted, the less likely they end.
                            double age = (Now - (FindRelation(a.Id, b.Id)?.Since ?? Now)) / SecondsPerDay;
                            // (And a side that's winning doesn't let its alliances lapse.)
                            bool winners = leadingShare >= WinningSideShare && leadingBloc.Contains(a.Id) && leadingBloc.Contains(b.Id);
                            if (fights >= 2 && r < 0.5) next = RelationKind.Enemy;
                            else if (!winners && r < 0.004 * Math.Max(0.15, 1 - age / 60)) next = RelationKind.Neutral;
                            break;
                        case RelationKind.Enemy:
                            if ((Math.Min(sa, sb) < 0.5 * Math.Max(sa, sb) && r < 0.08) || (fights == 0 && r < 0.03)) next = RelationKind.Neutral;
                            break;
                    }
                    // Bandwagoning: a small tribe next to the leading bloc asks to join it.
                    if (next == null && leading != null && (now == RelationKind.Neutral || now == RelationKind.NonAggression)
                        && leadingBloc.Contains(a.Id) != leadingBloc.Contains(b.Id))
                    {
                        var outsider = leadingBloc.Contains(a.Id) ? b : a;
                        // (The side takes tribes that can grow with it, not the tiniest.)
                        if (BlocShare(outsider) < 0.5 * leadingShare && outsider.Members.Count >= 8 && TribeRandom(a.Id * 977 + b.Id, 14) < 0.1) next = RelationKind.Ally;
                    }
                    // A coalition against the one who's nearly won (slow to come together).
                    if (feared != null && (a == feared || b == feared) && now != RelationKind.Enemy && !AlliesOf(feared).Contains(a == feared ? b : a)
                        && TribeRandom(a.Id * 977 + b.Id, 13) < 0.03)
                        next = RelationKind.Enemy;
                    // Once the realm has split, no pacts across factions.
                    if (FactionsFormed && a.FactionId >= 0 && b.FactionId >= 0 && a.FactionId != b.FactionId
                        && (next == RelationKind.Ally || next == RelationKind.NonAggression)) next = null;
                    // Too many allies already (or too big a network together): a pact instead.
                    if (next == RelationKind.Ally && !CanAlly(a, b)) next = now == RelationKind.Neutral ? RelationKind.NonAggression : (RelationKind?)null;
                    if (next == null || next == now) continue;
                    if (now == RelationKind.Ally) Log(next == RelationKind.Enemy ? "alliance ended in war" : "alliance lapsed");

                    if (humanA || humanB)
                    {
                        // The human's tribe: offers come by message; war and endings are just announced.
                        var ai = humanA ? b : a;
                        var mine = humanA ? a : b;
                        if (next == RelationKind.Ally || next == RelationKind.NonAggression)
                        {
                            if (HumanPlayer.Reputation < -20 || Messages.Exists(m => m.Kind == MessageKind.PactOffer && m.A == ai.Id && !m.Answered && Now - m.Time < 3 * SecondsPerDay)) continue;
                            Write(MessageKind.PactOffer, FindPlayer(ai.LeaderId), $"{(next == RelationKind.Ally ? "An alliance" : "A non-aggression pact")} with {ai.Name}?",
                                next == RelationKind.Ally
                                    ? Pick(10, "We have enemies in common. Stand with us, and we'll stand with you.", "Together we'd be feared. Allies?")
                                    : Pick(11, "Let's leave each other in peace. Agreed?", "We've no quarrel with you. A pact?"),
                                ai.Id, (int)next.Value, ai.Id);
                            continue;
                        }
                        SetRelation(a, b, next.Value);
                        Write(MessageKind.Note, FindPlayer(ai.LeaderId), next == RelationKind.Enemy ? $"{ai.Name} declares war" : $"{ai.Name} ends the pact",
                            next == RelationKind.Enemy
                                ? Pick(12, "You've had this coming. From today we're at war.", "Your villages will burn. War.")
                                : "Our agreement is over. Don't take it personally.", tribeId: ai.Id);
                        continue;
                    }
                    SetRelation(a, b, next.Value);
                    AnnounceRelation(a, b, next.Value);
                }
            if (FactionsFormed)
                foreach (var core in Cores())
                    if (strength.ContainsKey(core.Id)) Realign(core, tribes, centers, strength);
            else if (leading != null) Realign(leading, tribes, centers, strength);
        }

        /// <summary>
        /// The leading tribe looks for strong partners: with an alliance to spare it offers one to the strongest
        /// neighbor that isn't on another side, and it trades a withering ally (a handful of members, or a fifth of
        /// its own strength) for such a neighbor. Winning takes partners who can carry their share.
        /// </summary>
        void Realign(Tribe leading, List<Tribe> tribes, Dictionary<int, (double x, double y)> centers, Dictionary<int, int> strength)
        {
            if (IsHumanLed(leading) || TribeRandom(leading.Id, 17) >= 0.3 * TribeTickHours / 24) return;
            int own = strength[leading.Id];
            Tribe weak = null;
            bool spare = AlliesOf(leading).Count < MaxAlliances;
            if (!spare)
                foreach (var a in AlliesOf(leading))
                    if (!IsHumanLed(a) && (a.Members.Count <= 6 || strength.GetValueOrDefault(a.Id) < 0.2 * own) && (weak == null || strength.GetValueOrDefault(a.Id) < strength.GetValueOrDefault(weak.Id)))
                        weak = a;
            if (!spare && weak == null) return;
            var (lx, ly) = centers[leading.Id];
            var bloc = BlocOf(leading);
            Tribe partner = null;
            foreach (var c in tribes)
            {
                if (c.Disbanded || bloc.Contains(c.Id) || IsHumanLed(c) || c.Members.Count < 10 || Relation(leading, c) == RelationKind.Enemy) continue;
                if (FactionsFormed && c.FactionId != leading.FactionId) continue; // partners come from its own faction
                if (AlliesOf(c).Count >= MaxAlliances || weak != null && strength[c.Id] <= strength.GetValueOrDefault(weak.Id)) continue;
                var (cx, cy) = centers[c.Id];
                if ((cx - lx) * (cx - lx) + (cy - ly) * (cy - ly) > 45 * 45) continue;
                if (partner == null || strength[c.Id] > strength[partner.Id]) partner = c;
            }
            if (partner == null) return;
            if (weak != null) SetRelation(leading, weak, RelationKind.Neutral);
            if (!CanAlly(leading, partner))
            {
                if (weak != null) SetRelation(leading, weak, RelationKind.Ally);
                return;
            }
            SetRelation(leading, partner, RelationKind.Ally);
            Log(weak != null ? "realigned" : "winners found a partner");
            if (weak != null) AnnounceRelation(leading, weak, RelationKind.Neutral);
            AnnounceRelation(leading, partner, RelationKind.Ally);
        }

        /// <summary>Tells the human (if in one of the tribes) about a change of relations made by an AI leader.</summary>
        void AnnounceRelation(Tribe a, Tribe b, RelationKind kind)
        {
            var human = HumanPlayer;
            if (human == null) return;
            var mine = human.TribeId == a.Id ? a : human.TribeId == b.Id ? b : null;
            if (mine == null) return;
            var other = mine == a ? b : a;
            string what = kind switch
            {
                RelationKind.Ally => $"We are allied with {other.Name} [{other.Tag}]. Don't touch their villages.",
                RelationKind.NonAggression => $"We have a non-aggression pact with {other.Name} [{other.Tag}]. Leave them be.",
                RelationKind.Enemy => $"We are at war with {other.Name} [{other.Tag}].",
                _ => $"We have no agreement with {other.Name} [{other.Tag}] any more.",
            };
            Write(MessageKind.Note, FindPlayer(mine.LeaderId), $"Relations with {other.Name}", what, tribeId: mine.Id);
        }

        // ---------------------------------------------------------------- tribe targets

        /// <summary>
        /// A leader at war names a target: the enemy village nearest the tribe's heart. The human, as a member, is
        /// told. In the late game a tribe on the winning side with no one to fight picks a fight: war on the weakest
        /// tribe nearby that isn't its ally.
        /// </summary>
        void NameTargets()
        {
            var human = HumanPlayer;
            var winning = LateGame ? LeadingBloc() : new HashSet<int>();
            foreach (var t in ActiveTribes())
            {
                if (IsHumanLed(t) || (t.TargetVillageId >= 0 && t.TargetUntil > Now && FindVillage(t.TargetVillageId) != null
                    && !AreFriendly(t.LeaderId, FindVillage(t.TargetVillageId).OwnerId) && TribeOf(FindVillage(t.TargetVillageId).OwnerId) != t)) continue;
                t.TargetVillageId = -1;
                var (cx, cy) = TribeCenter(t);
                Village best = null;
                double bestD = 25;
                foreach (var v in VillagesNear((int)cx, (int)cy, 25))
                {
                    if (v.IsBarbarian || !AtWar(t.LeaderId, v.OwnerId) || IsProtected(v.OwnerId)) continue;
                    double d = Math.Sqrt((v.X - cx) * (v.X - cx) + (v.Y - cy) * (v.Y - cy));
                    if (d < bestD)
                    {
                        bestD = d;
                        best = v;
                    }
                }
                if (best == null)
                {
                    if (winning.Contains(t.Id) && TribeRandom(t.Id, 16) < 0.2 * TribeTickHours / 24) PickAFight(t, cx, cy);
                    continue;
                }
                t.TargetVillageId = best.Id;
                t.TargetUntil = Now + 1.5 * SecondsPerDay;
                // (Renewing the same target doesn't repeat the order.)
                if (human != null && human.TribeId == t.Id && best.OwnerId != human.Id
                    && !Messages.Exists(m => m.Kind == MessageKind.Order && m.A == best.Id && Now - m.Time < 3 * SecondsPerDay))
                    Write(MessageKind.Order, FindPlayer(t.LeaderId), $"Target: {best.Name} ({best.X}|{best.Y})",
                        $"Everyone hit {best.Name} of {NameWithTag(FindPlayer(best.OwnerId))}. Send what you can.", best.Id, tribeId: t.Id);
            }
        }

        /// <summary>A tribe on the winning side declares war on the weakest tribe near it that isn't an ally.</summary>
        void PickAFight(Tribe t, double cx, double cy)
        {
            Tribe prey = null;
            int preyPoints = int.MaxValue;
            foreach (var o in ActiveTribes())
            {
                if (o == t || o.Disbanded || Relation(t, o) == RelationKind.Ally || Relation(t, o) == RelationKind.Enemy) continue;
                var (ox, oy) = TribeCenter(o);
                if ((ox - cx) * (ox - cx) + (oy - cy) * (oy - cy) > 35 * 35) continue;
                int pts = TribeStrength(o).points;
                if (pts < preyPoints)
                {
                    preyPoints = pts;
                    prey = o;
                }
            }
            if (prey == null) return;
            Log("the winners picked a fight");
            SetRelation(t, prey, RelationKind.Enemy);
            if (IsHumanLed(prey))
                Write(MessageKind.Note, FindPlayer(t.LeaderId), $"{t.Name} declares war", "There's no room left for both of us. From today we're at war.", tribeId: t.Id);
            else AnnounceRelation(t, prey, RelationKind.Enemy);
        }

        /// <summary>The human, leading a tribe, names a village as the tribe's target for a day and a half.</summary>
        public bool SetTribeTarget(Village v)
        {
            var human = HumanPlayer;
            var t = TribeOf(human);
            if (t == null || t.LeaderId != human.Id || v == null || AreFriendly(human.Id, v.OwnerId) || v.OwnerId == human.Id) return false;
            t.TargetVillageId = v.Id;
            t.TargetUntil = Now + 1.5 * SecondsPerDay;
            return true;
        }

        /// <summary>The human asks their tribe for support at one of their villages for the next day.</summary>
        public bool RequestSupport(Village v)
        {
            var human = HumanPlayer;
            var t = TribeOf(human);
            if (t == null || v == null || v.OwnerId != human.Id) return false;
            t.HelpCalls.Add(new HelpCall { VillageId = v.Id, OwnerId = human.Id, AttackerId = -1, ArriveTime = Now + SecondsPerDay });
            return true;
        }

        // ---------------------------------------------------------------- victory

        // Villages per tribe, and all the players' villages, counted once and reused until something changes (not saved).
        [NonSerialized] Dictionary<int, int> tribeVillages;
        [NonSerialized] int playerVillages;
        [NonSerialized] double tribeVillagesAt = -1;

        /// <summary>Forgets the village counts (a village changed hands, or someone joined or left a tribe).</summary>
        void RecountTribes() => tribeVillagesAt = -1;

        void CountTribeVillages()
        {
            if (tribeVillages != null && tribeVillagesAt == Now) return;
            tribeVillages = tribeVillages ?? new Dictionary<int, int>();
            tribeVillages.Clear();
            playerVillages = 0;
            foreach (var v in Villages)
            {
                if (v.IsBarbarian) continue;
                playerVillages++;
                var owner = FindPlayer(v.OwnerId);
                if (owner != null && owner.TribeId >= 0) tribeVillages[owner.TribeId] = (tribeVillages.TryGetValue(owner.TribeId, out int n) ? n : 0) + 1;
            }
            tribeVillagesAt = Now;
        }

        /// <summary>The share of the players' villages held by a tribe and its allies.</summary>
        public double BlocShare(Tribe t) => t == null ? 0 : ShareOf(BlocOf(t));

        /// <summary>
        /// The share of the world held by a set of tribes: of every village, barbarians' included, on a diplomacy
        /// world (so winning barbarian villages is progress, not dilution); of the players' villages otherwise.
        /// </summary>
        public double ShareOf(HashSet<int> tribeIds)
        {
            CountTribeVillages();
            int held = 0;
            foreach (int id in tribeIds) held += tribeVillages.TryGetValue(id, out int n) ? n : 0;
            int all = GoalVillageCount;
            return all == 0 ? 0 : held / (double)all;
        }

        /// <summary>
        /// On a diplomacy world the goal counts every village (a tuning switch while the endgame is balanced);
        /// otherwise only the players' villages count.
        /// </summary>
        public static bool GoalOverAllVillages = true;

        /// <summary>How many villages the conquest goal is a share of.</summary>
        public int GoalVillageCount => Diplomacy && GoalOverAllVillages ? Villages.Count : LordVillageCount;

        /// <summary>How the goal's villages are described ("villages", or "villages players rule").</summary>
        public string GoalVillagesLabel => Diplomacy && GoalOverAllVillages ? "villages in the realm" : "villages players rule";

        /// <summary>The biggest share any bloc holds.</summary>
        public double BiggestBlocShare()
        {
            double best = 0;
            foreach (var t in ActiveTribes()) best = Math.Max(best, BlocShare(t));
            return best;
        }

        /// <summary>The tribe whose bloc (with its allies) is biggest among those that include <paramref name="t"/> or its allies.</summary>
        Tribe BiggestBlocAround(Tribe t)
        {
            Tribe best = null;
            double bestShare = -1;
            foreach (var candidate in AlliesOf(t))
            {
                double share = BlocShare(candidate);
                if (share > bestShare)
                {
                    bestShare = share;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// The human's best standing towards the goal: alone, or (on a diplomacy world) with their tribe and its
        /// allies. They win on whichever comes first.
        /// </summary>
        public double HumanBestShare => Math.Max(HumanShare, Diplomacy ? BlocShare(TribeOf(HumanPlayer)) : 0);

        /// <summary>
        /// Game days a side must hold the conquest goal to win a diplomacy world, as in Tribal Wars' dominance
        /// endings: time for everyone else to break its grip. (A tuning value while the endgame is balanced.)
        /// </summary>
        public static double HoldDays = 10;

        /// <summary>
        /// Once a side is holding the goal, its grip only breaks if it falls more than this far below it (so a village
        /// or two changing hands doesn't reset the count). (Tuning value.)
        /// </summary>
        public static double HoldMargin = 0.03;

        /// <summary>
        /// And only if it stays that low for this many game days: a network that splits for a moment and mends
        /// keeps its clock. (Tuning value.)
        /// </summary>
        public static double HoldGraceDays = 1;

        /// <summary>When the side holding the goal fell clearly below it (-1: it hasn't).</summary>
        public double HoldBelowSince = -1;

        /// <summary>
        /// Who is holding the goal now: -1 nobody, -2 the human alone, otherwise a tribe (and its bloc); and since
        /// when.
        /// </summary>
        public int HoldTribeId = -1;
        public double HoldSince = -1;
        /// <summary>The tribes on the side holding the goal (its bloc as it stands), so the side keeps its clock if its network splits and the main part holds on.</summary>
        public List<int> HoldBloc = new List<int>();

        /// <summary>The day the side holding the goal wins, if it keeps it.</summary>
        public double HoldEnds => HoldSince + HoldDays * SecondsPerDay;

        /// <summary>Whether a holder (-2 or a tribe) is on the human's side: the human alone, or their tribe's bloc.</summary>
        public bool IsHumanSide(int holder)
        {
            if (holder == -2) return true;
            var mine = TribeOf(HumanPlayer);
            return mine != null && holder >= 0 && BlocOf(mine).Contains(holder);
        }

        /// <summary>
        /// On a diplomacy world, the dominance ending: whoever holds the goal (the human alone, their bloc, or any
        /// other bloc) for <see cref="HoldDays"/> wins the world. Everyone is told when a hold begins and when it's
        /// broken.
        /// </summary>
        void CheckDominance()
        {
            if (!Diplomacy || LostToBloc || Won) return;
            var human = HumanPlayer;
            var mine = TribeOf(human);
            double goal = Settings.ConquestGoal;
            // The side already holding keeps its grip unless it falls clearly below the goal. A side is its bloc as
            // it stood: if the network splits, the biggest part of it carries the clock on.
            if (HoldTribeId != -1)
            {
                double share = 0;
                if (HoldTribeId == -2) share = PlayerVillage != null ? HumanShare : 0;
                else
                    foreach (int id in HoldBloc ?? new List<int>())
                    {
                        var part = FindTribe(id);
                        if (part == null) continue;
                        double s = BlocShare(part);
                        if (s > share)
                        {
                            share = s;
                            HoldTribeId = part.Id;
                        }
                    }
                // Clearly below it, but only for a moment so far: the clock runs on.
                bool grace = share < goal - HoldMargin && (HoldBelowSince < 0 || Now - HoldBelowSince < HoldGraceDays * SecondsPerDay);
                if (share < goal - HoldMargin && HoldBelowSince < 0) HoldBelowSince = Now;
                if (share >= goal - HoldMargin) HoldBelowSince = -1;
                if (share >= goal - HoldMargin || grace)
                {
                    if (grace) return; // no win while it's below the goal
                    if (HoldTribeId >= 0) HoldBloc = new List<int>(BlocOf(FindTribe(HoldTribeId)));
                    if (Now < HoldEnds) return;
                    if (IsHumanSide(HoldTribeId)) Won = true;
                    else
                    {
                        LostToBloc = true;
                        WinningTribeId = HoldTribeId;
                    }
                    return;
                }
            }
            int holder = -1;
            if (human != null && PlayerVillage != null && HumanShare >= goal) holder = -2;
            else if (mine != null && PlayerVillage != null && BlocShare(mine) >= goal) holder = mine.Id;
            else
            {
                double best = goal;
                foreach (var t in ActiveTribes())
                {
                    double share = BlocShare(t);
                    if (share >= best)
                    {
                        best = share;
                        holder = t.Id;
                    }
                }
            }

            if (holder == -1)
            {
                if (HoldTribeId != -1) AnnounceHoldBroken();
                HoldTribeId = -1;
                HoldSince = -1;
                HoldBelowSince = -1;
                HoldBloc = new List<int>();
                return;
            }
            // The same side keeps its clock (the human alone and the human's bloc count as one side, and so does a bloc under another of its tribes).
            bool sameSide = HoldTribeId != -1 && (IsHumanSide(holder) && IsHumanSide(HoldTribeId)
                || HoldTribeId >= 0 && holder >= 0 && FindTribe(HoldTribeId) is Tribe held && BlocOf(held).Contains(holder));
            if (!sameSide)
            {
                if (HoldTribeId != -1) AnnounceHoldBroken();
                HoldTribeId = holder;
                HoldSince = Now;
                HoldBelowSince = -1;
                HoldBloc = holder >= 0 ? new List<int>(BlocOf(FindTribe(holder))) : new List<int>();
                AnnounceHold(holder);
                return;
            }
            HoldTribeId = holder;
            HoldBloc = holder >= 0 ? new List<int>(BlocOf(FindTribe(holder))) : new List<int>();
            if (Now < HoldEnds) return;
            if (IsHumanSide(holder)) Won = true;
            else
            {
                LostToBloc = true;
                WinningTribeId = holder;
            }
        }

        void AnnounceHold(int holder)
        {
            string until = FormatClock(HoldSince + HoldDays * SecondsPerDay);
            if (IsHumanSide(holder))
                Write(MessageKind.Note, null, "The realm is within your grasp",
                    $"Your side holds {Settings.ConquestGoal:P0} of the {GoalVillagesLabel}. Keep it until {until} and the world is yours.");
            else
            {
                var t = FindTribe(holder);
                Write(MessageKind.Note, null, $"{t?.Name} is taking the realm",
                    $"{t?.Name} [{t?.Tag}] and its allies hold {Settings.ConquestGoal:P0} of the {GoalVillagesLabel}. If they keep it until {until}, the world is theirs. Break their grip before then.");
            }
        }

        void AnnounceHoldBroken()
        {
            if (IsHumanSide(HoldTribeId))
                Write(MessageKind.Note, null, "Your hold is broken", $"Your side has fallen below {Settings.ConquestGoal:P0}. Win it back to start the count again.");
            else
                Write(MessageKind.Note, null, $"{FindTribe(HoldTribeId)?.Name ?? "The leading bloc"} has lost its grip",
                    $"They have fallen below {Settings.ConquestGoal:P0} of the {GoalVillagesLabel}. The realm is open again.");
        }
    }
}
