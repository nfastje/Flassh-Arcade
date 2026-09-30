using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Tribes forming, recruiting strong lords and letting their weakest go; and beaten lords giving up, or going
    /// over to the winning side.
    /// </summary>
    public partial class World
    {
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
    }
}
