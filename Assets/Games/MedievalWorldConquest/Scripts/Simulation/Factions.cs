using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// The endgame on a diplomacy world, as Tribal Wars worlds ended: once the strongest players between them could
    /// take the world, the realm splits into two to four factions, each around a leading tribe (its "core") and that
    /// tribe's allies, the side that can win. The other tribes of a faction ("satellites") feed it: their best lords
    /// move up into the core side, their villages are handed over to core lords' noblemen, and their surplus goes by
    /// merchant to core lords' academies. There's no announcement: when it happens is worked out from a few hidden
    /// checks with thresholds of the world's own, and tribes pick their faction one by one over the following weeks.
    /// </summary>
    public partial class World
    {
        /// <summary>Whether the realm has split into factions, and when it will (-1: not yet due).</summary>
        public bool FactionsFormed;
        public double FactionsDueAt = -1;

        /// <summary>The most factions the realm splits into (the fewest is two).</summary>
        public const int MaxFactions = 4;
        /// <summary>Game days a satellite lord's offer of a village stands.</summary>
        public const double FeedDays = 7;
        /// <summary>How far (in fields) a satellite village may be from the core lord it's handed to.</summary>
        public const double FeedRange = 20;

        /// <summary>A repeatable random number for this world (not the tick), from its seed and a salt.</summary>
        double WorldRandom(int salt) => Terrain.Hash(Settings.Seed ^ 0x3C6EF372, salt, 0);

        /// <summary>
        /// The hidden trigger. With the world locked, it counts how many of these hold, each against a threshold of
        /// this world's own: the 60 players with most villages hold 38–48% of the map; the top 20 average 30–45
        /// villages; barbarians are down to 3–7% of it; the leading side holds 22–32%. Two will do.
        /// </summary>
        public bool FactionTriggerMet()
        {
            if (!Diplomacy || !WorldLocked) return false;
            var held = new List<int>();
            foreach (var p in Players)
            {
                if (p.Quit || p.Personality == AiPersonality.Inactive || p.Personality == AiPersonality.Noob) continue;
                int n = VillagesOf(p.Id).Count;
                if (n > 0) held.Add(n);
            }
            held.Sort((a, b) => b.CompareTo(a));
            int top60 = 0, top20 = 0;
            for (int i = 0; i < held.Count && i < 60; i++)
            {
                top60 += held[i];
                if (i < 20) top20 += held[i];
            }
            int checks = 0;
            if (top60 >= (0.38 + 0.1 * WorldRandom(1)) * Villages.Count) checks++;
            if (top20 / 20.0 >= 30 + 15 * WorldRandom(2)) checks++;
            if (VillagesOf(-1).Count < (0.03 + 0.04 * WorldRandom(3)) * Villages.Count) checks++;
            if (ShareOf(LeadingBloc()) >= 0.22 + 0.1 * WorldRandom(4)) checks++;
            return checks >= 2;
        }

        /// <summary>The factions' leading tribes.</summary>
        public List<Tribe> Cores()
        {
            var cores = new List<Tribe>();
            foreach (var t in Tribes)
                if (!t.Disbanded && t.FactionId == t.Id) cores.Add(t);
            return cores;
        }

        /// <summary>The leading tribe of a tribe's faction (null if it isn't in one).</summary>
        public Tribe CoreOf(Tribe t) => t == null || t.FactionId < 0 ? null : FindTribe(t.FactionId);

        /// <summary>Whether a tribe is on its faction's winning side: the core tribe or one of its allies.</summary>
        public bool OnCoreSide(Tribe t)
        {
            var core = CoreOf(t);
            return core != null && (core == t || Relation(core, t) == RelationKind.Ally);
        }

        /// <summary>Whether a tribe is a satellite: in a faction, but not on its winning side.</summary>
        public bool IsSatellite(Tribe t) => FactionsFormed && CoreOf(t) != null && !OnCoreSide(t);

        void FactionTick(double days)
        {
            if (!FactionsFormed)
            {
                // Once the checks hold, the split comes a day to six later.
                if (FactionsDueAt < 0)
                {
                    if (FactionTriggerMet()) FactionsDueAt = Now + (1 + 5 * WorldRandom(5)) * SecondsPerDay;
                    return;
                }
                if (Now < FactionsDueAt) return;
                FormFactions();
                if (!FactionsFormed) return;
            }
            KeepCores();
            ChooseSides();
            Defect(days);
            FactionRelations();
            FeedPlayers();
            FeedVillages(days);
            ExpireFeeds();
        }

        /// <summary>
        /// Names the factions' cores, strongest tribe first. Each tribe after it (of the eight strongest) that is
        /// allied with or has a pact with one already named follows it; one that is strong enough (two fifths of the
        /// first's strength) and either at war with one of them or far from all of them leads a faction of its own.
        /// So how many factions there are comes from how the top tribes stand with each other, up to this world's
        /// own limit (two to four).
        /// </summary>
        public void FormFactions()
        {
            var tribes = ActiveTribes();
            var strength = new Dictionary<int, int>();
            foreach (var t in tribes) strength[t.Id] = TribeStrength(t).points;
            var candidates = tribes.FindAll(t => t.Members.Count >= 5);
            candidates.Sort((a, b) => strength[b.Id].CompareTo(strength[a.Id]));
            if (candidates.Count > 8) candidates.RemoveRange(8, candidates.Count - 8);
            var cores = new List<Tribe>();
            int most = 2 + (int)(3 * WorldRandom(6));
            foreach (var c in candidates)
            {
                if (cores.Count >= Math.Min(most, MaxFactions)) break;
                if (cores.Count == 0)
                {
                    cores.Add(c);
                    continue;
                }
                bool bound = false;
                int grudges = 0;
                double nearest = double.MaxValue;
                var (cx, cy) = TribeCenter(c);
                foreach (var k in cores)
                {
                    var rel = Relation(k, c);
                    if (rel == RelationKind.Ally || rel == RelationKind.NonAggression) bound = true;
                    if (rel == RelationKind.Enemy) grudges++;
                    var (kx, ky) = TribeCenter(k);
                    nearest = Math.Min(nearest, Math.Sqrt((kx - cx) * (kx - cx) + (ky - cy) * (ky - cy)));
                }
                if (bound) continue;
                if (strength[c.Id] >= 0.4 * strength[cores[0].Id] && (grudges > 0 || nearest > 60)) cores.Add(c);
            }
            if (cores.Count < 2)
            {
                // Somebody always stands against the strongest: the strongest tribe that isn't its ally.
                var rival = candidates.Find(c => c != cores[0] && Relation(cores[0], c) != RelationKind.Ally);
                if (rival == null) return;
                cores.Add(rival);
            }
            FactionsFormed = true;
            Log($"{cores.Count} factions");
            foreach (var k in cores) k.FactionId = k.Id;
            foreach (var k in cores)
                foreach (var a in AlliesOf(k))
                    if (a.FactionId < 0) a.FactionId = k.Id;
            // The rest choose over the next fortnight, one by one.
            foreach (var t in tribes)
                if (t.FactionId < 0) t.FactionJoinAt = Now + 15 * TribeRandom(t.Id, 40) * SecondsPerDay;
        }

        /// <summary>A faction whose leading tribe has gone is led by its strongest tribe left.</summary>
        void KeepCores()
        {
            var lost = new HashSet<int>();
            foreach (var t in Tribes)
                if (!t.Disbanded && t.FactionId >= 0 && FindTribe(t.FactionId) == null) lost.Add(t.FactionId);
            foreach (int old in lost)
            {
                Tribe successor = null;
                int best = -1;
                foreach (var t in ActiveTribes())
                {
                    if (t.FactionId != old) continue;
                    int pts = TribeStrength(t).points;
                    if (pts > best)
                    {
                        best = pts;
                        successor = t;
                    }
                }
                foreach (var t in Tribes)
                    if (t.FactionId == old) t.FactionId = successor?.Id ?? -1;
                Log("new faction leader");
            }
        }

        /// <summary>
        /// Tribes not yet in a faction (and new ones) pick one when their time comes: the one they stand best with,
        /// strong and near. It all happens behind the scenes, the human's tribe included: the player only sees what
        /// follows (wars, and villages and lords changing hands).
        /// </summary>
        void ChooseSides()
        {
            var cores = Cores();
            if (cores.Count == 0) return;
            foreach (var t in ActiveTribes())
            {
                if (t.FactionId != -1) continue;
                if (t.FactionJoinAt <= 0)
                {
                    t.FactionJoinAt = Now + 15 * TribeRandom(t.Id, 40) * SecondsPerDay;
                    continue;
                }
                if (Now < t.FactionJoinAt) continue;
                Tribe best = null;
                double bestAppeal = 0;
                var (tx, ty) = TribeCenter(t);
                foreach (var k in cores)
                {
                    var rel = Relation(k, t);
                    double warmth = rel == RelationKind.Ally ? 4 : rel == RelationKind.NonAggression ? 2 : rel == RelationKind.Enemy ? 0.15 : 1;
                    var (kx, ky) = TribeCenter(k);
                    // (The strong draw the undecided: strength counts in full.)
                    double appeal = warmth * (TribeStrength(k).points + 1) / (10 + Math.Sqrt((kx - tx) * (kx - tx) + (ky - ty) * (ky - ty)));
                    if (appeal > bestAppeal)
                    {
                        bestAppeal = appeal;
                        best = k;
                    }
                }
                if (best == null) continue;
                t.FactionId = best.Id;
                Log("chose a faction");
            }
        }

        /// <summary>
        /// A faction falling behind bleeds: while it holds under three fifths of what the leading faction does, its
        /// satellites (4% a day) and its leader's allies (1.5% a day) go over to the leading faction; and a faction
        /// down to under 8% of the world a month after the split breaks up, its tribes choosing again.
        /// </summary>
        void Defect(double days)
        {
            var cores = Cores();
            if (cores.Count < 2) return;
            var tribes = ActiveTribes();
            var share = new Dictionary<int, double>();
            foreach (var k in cores)
            {
                var ids = new HashSet<int>();
                foreach (var t in tribes)
                    if (t.FactionId == k.Id) ids.Add(t.Id);
                share[k.Id] = ShareOf(ids);
            }
            Tribe leader = null;
            foreach (var k in cores)
                if (leader == null || share[k.Id] > share[leader.Id]) leader = k;
            foreach (var t in tribes)
            {
                // (A tribe the human leads keeps the side it's on: that's for the player to change.)
                if (t.FactionId < 0 || t.FactionId == leader.Id || t.FactionId == t.Id || IsHumanLed(t) || !share.TryGetValue(t.FactionId, out double own)) continue;
                if (own >= 0.6 * share[leader.Id]) continue;
                double perDay = OnCoreSide(t) ? 0.015 : 0.04;
                if (TribeRandom(t.Id, 45) >= perDay * days) continue;
                var old = CoreOf(t);
                if (Relation(t, old) == RelationKind.Ally) SetRelation(t, old, RelationKind.Neutral);
                t.FactionId = leader.Id;
                Log("switched faction");
            }
            foreach (var k in cores)
            {
                if (k == leader || share[k.Id] >= 0.08 || Now - FactionsDueAt < 30 * SecondsPerDay || IsHumanLed(k)) continue;
                foreach (var t in tribes)
                    if (t.FactionId == k.Id && !IsHumanLed(t))
                    {
                        t.FactionId = -1;
                        t.FactionJoinAt = Now + 5 * TribeRandom(t.Id, 46) * SecondsPerDay;
                    }
                Log("faction broke up");
            }
        }

        /// <summary>
        /// Within a faction, tribes keep the peace (a pact at least); between factions it comes to war, the leading
        /// tribes at once, the others one by one.
        /// </summary>
        void FactionRelations()
        {
            var tribes = ActiveTribes().FindAll(t => t.FactionId >= 0);
            for (int i = 0; i < tribes.Count; i++)
                for (int j = i + 1; j < tribes.Count; j++)
                {
                    var a = tribes[i];
                    var b = tribes[j];
                    var now = Relation(a, b);
                    RelationKind? next = null;
                    if (a.FactionId == b.FactionId)
                    {
                        if (now == RelationKind.Neutral || now == RelationKind.Enemy) next = RelationKind.NonAggression;
                    }
                    else if (now != RelationKind.Enemy)
                    {
                        bool leaders = a.FactionId == a.Id && b.FactionId == b.Id;
                        if (leaders || TribeRandom(a.Id * 977 + b.Id, 41) < 0.1) next = RelationKind.Enemy;
                    }
                    if (next == null) continue;
                    SetRelation(a, b, next.Value);
                    AnnounceRelation(a, b, next.Value);
                }
        }

        /// <summary>
        /// Feeding lords: each tick, now and then, a tribe on a faction's winning side takes the strongest lord of
        /// its satellites who is stronger than its own weakest member; if it's full, that member goes to the
        /// satellite in their place. A human worth it is invited instead.
        /// </summary>
        void FeedPlayers()
        {
            var human = HumanPlayer;
            var humanTribe = TribeOf(human);
            foreach (var t in ActiveTribes())
            {
                if (!OnCoreSide(t) || IsHumanLed(t) || TribeRandom(t.Id, 42) >= 0.5) continue;
                var weakest = WeakestMember(t);
                int floor = HasRoom(t) ? 0 : weakest != null ? PointsOf(weakest) : int.MaxValue;
                var (cx, cy) = TribeCenter(t);
                Player best = null;
                int bestPoints = floor;
                foreach (var s in ActiveTribes())
                {
                    if (s.FactionId != t.FactionId || !IsSatellite(s)) continue;
                    foreach (int id in s.Members)
                    {
                        var m = FindPlayer(id);
                        if (m == null || m.Quit || id == s.LeaderId || m.IsHuman) continue;
                        var own = VillagesOf(id);
                        if (own.Count == 0 || (own[0].X - cx) * (own[0].X - cx) + (own[0].Y - cy) * (own[0].Y - cy) > 60 * 60) continue;
                        int pts = PointsOf(m);
                        if (pts > bestPoints)
                        {
                            bestPoints = pts;
                            best = m;
                        }
                    }
                }
                // The human in a satellite, if they're the best of them: an invitation to move up.
                if (human != null && humanTribe != null && humanTribe.FactionId == t.FactionId && IsSatellite(humanTribe)
                    && PointsOf(human) > bestPoints && humanTribe.LeaderId != human.Id
                    && !Messages.Exists(m => m.Kind == MessageKind.Invitation && m.A == t.Id && Now - m.Time < 5 * SecondsPerDay))
                {
                    Write(MessageKind.Invitation, FindPlayer(t.LeaderId), $"A place in {t.Name} [{t.Tag}]",
                        $"You've outgrown {humanTribe.Name}. The faction needs its best in {t.Name}, where the crown will be won. Join us.", t.Id, tribeId: t.Id);
                    continue;
                }
                if (best == null) continue;
                var from = TribeOf(best);
                LeaveTribe(best);
                if (!HasRoom(t) && weakest != null)
                {
                    if (from != null && !from.Disbanded) JoinTribe(weakest, from);
                    else LeaveTribe(weakest);
                }
                JoinTribe(best, t);
                Log("fed a lord");
            }
        }

        /// <summary>
        /// For a human leading a tribe on their faction's winning side (AI satellites don't move lords into a tribe
        /// the player leads: that's the player's call): the faction's other lords who would make it stronger (any,
        /// while it has room; otherwise those stronger than its weakest member), strongest first.
        /// </summary>
        public List<Player> SwapCandidates(int most = 10)
        {
            var found = new List<Player>();
            var human = HumanPlayer;
            var tribe = TribeOf(human);
            if (!FactionsFormed || tribe == null || tribe.LeaderId != human.Id || !OnCoreSide(tribe)) return found;
            var weakest = WeakestMember(tribe);
            int floor = HasRoom(tribe) ? 0 : weakest != null ? PointsOf(weakest) : int.MaxValue;
            foreach (var s in ActiveTribes())
            {
                if (s.FactionId != tribe.FactionId || !IsSatellite(s)) continue;
                foreach (int id in s.Members)
                {
                    var m = FindPlayer(id);
                    if (m == null || m.Quit || m.IsHuman || id == s.LeaderId || VillagesOf(id).Count == 0 || PointsOf(m) <= floor) continue;
                    found.Add(m);
                }
            }
            found.Sort((a, b) => PointsOf(b).CompareTo(PointsOf(a)));
            if (found.Count > most) found.RemoveRange(most, found.Count - most);
            return found;
        }

        /// <summary>
        /// The human brings a faction lord into their tribe (one of <see cref="SwapCandidates"/>); if it's full, its
        /// weakest member takes that lord's place in their tribe. Returns what happened, or why it didn't.
        /// </summary>
        public string BringIntoTribe(Player lord)
        {
            var tribe = TribeOf(HumanPlayer);
            if (lord == null || tribe == null || !SwapCandidates(int.MaxValue).Contains(lord)) return "They can't join your tribe right now.";
            var from = TribeOf(lord);
            var weakest = HasRoom(tribe) ? null : WeakestMember(tribe);
            LeaveTribe(lord);
            if (weakest != null)
            {
                if (from != null && !from.Disbanded) JoinTribe(weakest, from);
                else LeaveTribe(weakest);
            }
            JoinTribe(lord, tribe);
            Log("fed a lord");
            return weakest != null && from != null
                ? $"{lord.Name} has joined {tribe.Name}; {weakest.Name} has gone to {from.Name} in their place."
                : $"{lord.Name} has joined {tribe.Name}.";
        }

        /// <summary>A village's offer (to the lord it's offered to) is still standing.</summary>
        public static bool FedTo(Village v, int playerId, double now) => v.FedTo >= 0 && v.FedTo == playerId && v.FedUntil > now;

        /// <summary>
        /// Feeding villages: a satellite lord with villages to spare offers one (at a time) to the nearest core lord
        /// with an academy: its defenders stand down for that lord's noblemen for <see cref="FeedDays"/> days. The
        /// human, in a satellite, is asked; on the winning side, is offered villages.
        /// </summary>
        void FeedVillages(double days)
        {
            var human = HumanPlayer;
            var humanTribe = TribeOf(human);
            foreach (var p in Players)
            {
                if (p.Quit || p.IsHuman) continue;
                var tribe = TribeOf(p);
                if (!IsSatellite(tribe) || TribeRandom(p.Id, 43) >= 0.3 * days) continue;
                var own = VillagesOf(p.Id);
                if (own.Count < 2 || own.Exists(v => v.FedTo >= 0 && v.FedUntil > Now)) continue;
                var (v, taker) = FeedCandidate(own, tribe.FactionId, p.Id);
                if (v == null) continue;
                v.FedTo = taker.Id;
                v.FedUntil = Now + FeedDays * SecondsPerDay;
                Log("fed a village");
                if (taker.IsHuman)
                    Write(MessageKind.FeedOffer, p, $"{v.Name} ({v.X}|{v.Y}) is yours to take",
                        $"Send your noblemen to {v.Name}: my troops there will stand aside until {FormatClock(v.FedUntil)}. The faction needs it in your hands, not mine.", v.Id);
            }

            // The human in a satellite: now and then a core lord asks for one of their villages.
            if (human == null || !IsSatellite(humanTribe) || TribeRandom(human.Id, 44) >= 0.3 * days) return;
            if (Messages.Exists(m => m.Kind == MessageKind.FeedRequest && !m.Answered && Now - m.Time < 3 * SecondsPerDay)) return;
            var mine = VillagesOf(human.Id);
            if (mine.Count < 2 || mine.Exists(x => x.FedTo >= 0 && x.FedUntil > Now)) return;
            var (asked, asker) = FeedCandidate(mine, humanTribe.FactionId, human.Id);
            if (asked == null) return;
            Write(MessageKind.FeedRequest, asker, $"May I take {asked.Name}?",
                $"{asked.Name} ({asked.X}|{asked.Y}) sits on my border. Let my noblemen have it, and the faction is that much nearer the crown. " +
                "Say yes and your troops there stand aside for a week.", asked.Id, asker.Id);
        }

        /// <summary>
        /// Which of a lord's villages to hand over, and to whom: the pair closest together of one of its villages
        /// (never its biggest) and a village of a core lord of its faction with an academy (the human needs none).
        /// </summary>
        (Village village, Player taker) FeedCandidate(List<Village> own, int factionId, int ownerId)
        {
            Village keep = null;
            foreach (var v in own)
                if (keep == null || v.Points > keep.Points) keep = v;
            Village best = null;
            Player taker = null;
            double bestD = double.MaxValue;
            foreach (var v in own)
            {
                if (v == keep) continue;
                foreach (var u in VillagesNear(v.X, v.Y, FeedRange))
                {
                    if (u.IsBarbarian || u.OwnerId == ownerId) continue;
                    var t = TribeOf(u.OwnerId);
                    if (t == null || t.FactionId != factionId || !OnCoreSide(t)) continue;
                    var q = FindPlayer(u.OwnerId);
                    if (q == null || q.Quit || (!q.IsHuman && !VillagesOf(q.Id).Exists(x => x.Level(BuildingType.Academy) > 0))) continue;
                    double d = Distance(v, u);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = v;
                        taker = q;
                    }
                }
            }
            return (best, taker);
        }

        /// <summary>Offers of villages no one came for lapse.</summary>
        void ExpireFeeds()
        {
            foreach (var v in Villages)
                if (v.FedTo >= 0 && v.FedUntil <= Now) v.FedTo = -1;
        }

        /// <summary>The human, leading a tribe, answers a faction's invitation (yes: the tribe joins it; no: it stays out).</summary>
        bool AnswerFactionInvite(Message m, bool yes)
        {
            var tribe = TribeOf(HumanPlayer);
            var core = FindTribe(m.A);
            if (tribe == null || !IsHumanLed(tribe) || tribe.FactionId >= 0) return false;
            if (!yes || core == null || core.FactionId != core.Id)
            {
                tribe.FactionId = -2;
                return !yes;
            }
            tribe.FactionId = core.Id;
            FactionRelations();
            return true;
        }

        /// <summary>The human answers a core lord who asked for one of their villages.</summary>
        bool AnswerFeedRequest(Message m, bool yes)
        {
            var human = HumanPlayer;
            var v = FindVillage(m.A);
            var asker = FindPlayer(m.B);
            if (!yes)
            {
                human.Reputation -= 3;
                return true;
            }
            if (v == null || v.OwnerId != human.Id || asker == null || asker.Quit) return false;
            v.FedTo = asker.Id;
            v.FedUntil = Now + FeedDays * SecondsPerDay;
            return true;
        }
    }
}
