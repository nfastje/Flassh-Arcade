using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Tribe targets: the village a tribe's leader names for its members to attack, and the human's calls for support.
    /// </summary>
    public partial class World
    {
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
    }
}
