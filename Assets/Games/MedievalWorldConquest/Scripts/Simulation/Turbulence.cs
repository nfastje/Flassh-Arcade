using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Social turbulence: the strains that build up in a tribe and, now and then, break it; and tribes merging,
    /// breaking up or being swallowed.
    /// </summary>
    public partial class World
    {
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
    }
}
