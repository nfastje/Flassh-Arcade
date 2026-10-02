using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Relations between tribes: pacts, alliances and war, and the leading tribe looking for strong partners.
    /// </summary>
    public partial class World
    {
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
                            // With the human's tribe, peace is weighed as when the human offers it.
                            if (humanA || humanB) { if (WeighPeace(humanA ? b : a, humanA ? a : b).yes && r < 0.1) next = RelationKind.Neutral; }
                            else if ((Math.Min(sa, sb) < 0.5 * Math.Max(sa, sb) && r < 0.08) || (fights == 0 && r < 0.03)) next = RelationKind.Neutral;
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
                        if (now == RelationKind.Enemy && next == RelationKind.Neutral)
                        {
                            // Peace is offered, not imposed: the human may fight on.
                            if (Messages.Exists(m => m.Kind == MessageKind.PactOffer && m.A == ai.Id && !m.Answered && Now - m.Time < 3 * SecondsPerDay)) continue;
                            Write(MessageKind.PactOffer, FindPlayer(ai.LeaderId), $"Peace with {ai.Name}?", WeighPeace(ai, mine).why.Replace("We accept. ", ""),
                                ai.Id, (int)RelationKind.Neutral, ai.Id);
                            continue;
                        }
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
    }
}
