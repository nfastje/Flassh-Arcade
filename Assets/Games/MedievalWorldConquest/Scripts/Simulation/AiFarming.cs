using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Lords farming barbarians, noobs and inactive players for resources, judged from what they've seen.
    /// </summary>
    public partial class World
    {
        /// <summary>
        /// Game hours a sighting of a village's defenders stays good enough to raid on without a scout along to
        /// check again: a player's (who may have trained more since), and a barbarian village's (which can't).
        /// </summary>
        public const double AiFreshHoursPlayer = 12, AiFreshHoursBarbarian = 48;
        /// <summary>The least plunder a lord expects before a raid is worth sending.</summary>
        public const int AiMinHaul = 100;
        /// <summary>At most this many villages scouted per turn by a lord looking for new farms.</summary>
        public const int AiScoutRunsPerTurn = 2;

        /// <summary>
        /// Villages a lord farms rather than makes war on: barbarians, and players who are easy prey (noobs and
        /// inactive players), once they're out of beginner protection.
        /// </summary>
        bool IsFarm(Player lord, Village t)
        {
            if (t.IsBarbarian) return true;
            if (t.OwnerId == lord.Id || IsProtected(t.OwnerId) || AreFriendly(lord.Id, t.OwnerId)) return false;
            var owner = FindPlayer(t.OwnerId);
            return owner != null && (owner.Personality == AiPersonality.Noob || owner.Personality == AiPersonality.Inactive);
        }

        /// <summary>
        /// Farming, as a Tribal Wars player does it, by every personality but the noobs: raids on the nearest
        /// barbarian, noob and inactive villages, each sized to carry off what the lord expects to find there and
        /// to beat the defenders it expects. It only knows what its scouts and earlier raids have shown it: a
        /// village it knows nothing about is scouted first, and a scout rides along whenever what it knows is
        /// getting old. With no scouts (or before it has researched them) it farms barbarians blind, and learns
        /// the hard way which ones have troops.
        /// </summary>
        void AiRaid(Player lord, Village v, AiStyle style)
        {
            if (style.RaidWith.Length == 0) return;
            var available = new int[Units.Count];
            foreach (var type in style.RaidWith)
            {
                int home = v.TroopCount(type);
                bool guard = type == UnitType.Spearman || type == UnitType.Swordsman;
                // Of the home guard, only its share of all of them may be out at once, counting those already out.
                available[(int)type] = guard ? Math.Max(0, Math.Min(home, (int)((home + Away(v, type)) * style.HomeGuardOut) - Away(v, type))) : home;
            }
            int scouts = v.TroopCount(UnitType.Scout);
            if (Battle.CarryCapacity(available) < AiMinHaul && scouts == 0) return;

            var targets = Emptied(ref raidTargets);
            foreach (var t in VillagesNear(v.X, v.Y, AiRaidRange, Emptied(ref nearby)))
                if (IsFarm(lord, t)) targets.Add((t, Distance(v, t)));
            targets.Sort((a, b) => a.distance.CompareTo(b.distance));
            var underAttack = AttackTargets(lord);

            int raids = 0, scoutRuns = 0, looked = 0;
            foreach (var (target, _) in targets)
            {
                if (raids >= SkillMaxRaids) break;
                var note = NoteFor(lord, target.Id, false);
                if (note != null && (note.NextRaidAt > Now || note.AvoidUntil > Now)) continue;
                if (underAttack.Contains(target.Id)) continue;
                // The nearest dozen worth a look are plenty to choose from (and a crowded map has many more).
                if (looked++ >= AiRaidLooks) break;

                bool seen = note != null && note.SeenAt >= 0 && note.SeenTroops != null;
                double hoursOld = seen ? (Now - note.SeenAt) / 3600 : double.PositiveInfinity;
                bool fresh = hoursOld <= (target.IsBarbarian ? AiFreshHoursBarbarian : AiFreshHoursPlayer);

                // Never seen: scouts go first if there are any; players are never raided blind.
                if (!seen && scouts > 0)
                {
                    if (scoutRuns >= AiScoutRunsPerTurn) continue;
                    var look = new int[Units.Count];
                    look[(int)UnitType.Scout] = target.IsBarbarian ? 1 : 2;
                    if (look[(int)UnitType.Scout] > scouts) continue;
                    var run = Send(v, target, look, CommandKind.Attack);
                    if (run == null) continue;
                    scouts -= look[(int)UnitType.Scout];
                    scoutRuns++;
                    // Wait for the report before deciding.
                    NoteFor(lord, target.Id, true).NextRaidAt = Now + 2 * (run.ArriveTime - Now);
                    continue;
                }
                if (!seen && !target.IsBarbarian) continue;
                if (Battle.CarryCapacity(available) < AiMinHaul) continue;

                double haul = ExpectedHaul(note, target);
                if (haul < AiMinHaul) continue;
                var defenders = seen ? ExpectedDefenders(note, target) : NoTroops;
                int wall = seen ? note.SeenWall : GuessWall(target) / 2;
                var party = RaidParty(style, available, haul, defenders, wall);
                if (party == null) continue;
                // A scout rides along to bring back fresh news whenever what the lord knows is getting old.
                if (!fresh && scouts > 0)
                {
                    party[(int)UnitType.Scout] = 1;
                    scouts--;
                }
                var command = Send(v, target, party, CommandKind.Attack);
                if (command == null) continue;
                for (int i = 0; i < Units.Count; i++) if (i != (int)UnitType.Scout) available[i] -= party[i];
                NoteFor(lord, target.Id, true).NextRaidAt = Now + 2 * (command.ArriveTime - Now) + AiRaidRestHours * 3600;
                raids++;
            }
        }
    }
}
