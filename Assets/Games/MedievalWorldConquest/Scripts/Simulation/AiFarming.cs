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
        /// Whether a lord's raiders go straight back out when they get home from a farm, as the Loot Assistant's
        /// cycle does for the player, rather than waiting for the lord's next turn (which late in a world can be
        /// hours away). (Tuning switch.)
        /// </summary>
        public static bool RaidOnReturn = true;

        /// <summary>
        /// Whether, until a village has light cavalry, its lord also raids with spearmen (keeping at least half of
        /// them home), as Tribal Wars players farm from their first days, whatever they mean to become. (Tuning switch.)
        /// </summary>
        public static bool EarlySpearRaids = true;

        /// <summary>
        /// The units a village raids with, and the share of its home guard (spearmen and swordsmen) that may be out
        /// at once: its lord's style, and spearmen too before it has light cavalry (with <see cref="EarlySpearRaids"/>).
        /// </summary>
        static (UnitType[] units, double guardOut) RaidersOf(Village v, AiStyle style)
        {
            if (!EarlySpearRaids || style.RaidWith.Length == 0 || v.TroopCount(UnitType.LightCavalry) > 0) return (style.RaidWith, style.HomeGuardOut);
            var units = Array.IndexOf(style.RaidWith, UnitType.Spearman) >= 0 ? style.RaidWith : AppendSpearmen(style.RaidWith);
            return (units, Math.Max(style.HomeGuardOut, 0.5));
        }

        static UnitType[] AppendSpearmen(UnitType[] units)
        {
            var with = new UnitType[units.Length + 1];
            units.CopyTo(with, 0);
            with[units.Length] = UnitType.Spearman;
            return with;
        }

        // ---------------------------------------------------------------- raiders kept for what matters more

        /// <summary>Game hours a village gathers its army for an operation (its raiders staying home) before giving up on it.</summary>
        public const double MusterHours = 12;

        /// <summary>Game hours after being attacked that a lord's raiders stay home when they get back, rather than going straight out again.</summary>
        public const double ThreatHours = 6;

        /// <summary>
        /// Villages gathering their army for an operation (a nobleman train or an attack the lord couldn't send for
        /// want of the troops out raiding): until it goes, or <see cref="MusterHours"/> pass, raiders home stay home.
        /// (Not saved: after a load, the next turn decides again.)
        /// </summary>
        [NonSerialized] Dictionary<int, double> musterUntil;

        bool Mustering(Village v) => musterUntil != null && musterUntil.TryGetValue(v.Id, out double until) && until > Now;

        void Muster(Village v) => (musterUntil ??= new Dictionary<int, double>())[v.Id] = Now + MusterHours * 3600;

        void Mustered(Village v) => musterUntil?.Remove(v.Id);

        /// <summary>Whether a lord was attacked (in earnest, or by a fake) lately.</summary>
        bool UnderThreat(Player lord) => lord.LastAttackedAt >= 0 && Now - lord.LastAttackedAt < ThreatHours * 3600;

        /// <summary>The attack strength of a village's offensive troops out on the march (raiding, mostly).</summary>
        int OffenseAway(Village v)
        {
            int power = 0;
            foreach (var type in OffensiveUnits) power += Away(v, type) * Units.Get(type).Attack;
            return power;
        }

        /// <summary>
        /// A village's offensive troops at home together with those out on the march (raiding, mostly): the army it
        /// would have if they were gathered.
        /// </summary>
        int[] OffenseWithRaiders(Village v, int[] atHome)
        {
            var all = (int[])atHome.Clone();
            foreach (var type in OffensiveUnits) all[(int)type] += Away(v, type);
            return all;
        }

        // ---------------------------------------------------------------- raiding

        /// <summary>The farms near each lord's village, nearest first, as its last turn found them (not saved).</summary>
        [NonSerialized] Dictionary<int, List<int>> raidTargetCache;

        /// <summary>
        /// A lord's raiding party is home from a farm. Unless the village is gathering for an operation or its lord
        /// was attacked lately, the raiders go straight out again, as the Loot Assistant's cycle sends the
        /// player's: one raid on the nearest farm ready for one, from the list the lord's last turn made (no new
        /// search, no scouting: those wait for its turn).
        /// </summary>
        void AiRaidOnReturn(Command command, Village home)
        {
            var lord = FindPlayer(command.OwnerId);
            if (lord == null || lord.IsHuman || lord.Quit) return;
            var from = FindVillage(command.FromVillageId);
            if (from == null || !IsFarm(lord, from)) return;
            if (Mustering(home) || UnderThreat(lord)) return;
            if (raidTargetCache == null || !raidTargetCache.TryGetValue(home.Id, out var ids)) return;
            var style = StyleOf(lord.Personality);
            if (style.RaidWith.Length == 0) return;
            var (raiders, guardOut) = RaidersOf(home, style);
            var available = AvailableRaiders(home, raiders, guardOut);
            if (Battle.CarryCapacity(available) < AiMinHaul) return;
            var underAttack = AttackTargets(lord);
            int scouts = 0; // (no scout rides along on these)
            foreach (int id in ids)
            {
                var target = FindVillage(id);
                if (target == null || underAttack.Contains(id) || !IsFarm(lord, target)) continue;
                var note = NoteFor(lord, id, false);
                if (note != null && (note.NextRaidAt > Now || note.AvoidUntil > Now)) continue;
                bool seen = note != null && note.SeenAt >= 0 && note.SeenTroops != null;
                if (!seen && !target.IsBarbarian) continue;
                if (RaidOnce(lord, home, target, note, seen, false, raiders, available, ref scouts)) return;
            }
        }

        /// <summary>
        /// What of each raiding unit a village can send now: all of them at home, but of the home guard (spearmen
        /// and swordsmen) only its share of all of them out at once, counting those already out.
        /// </summary>
        int[] AvailableRaiders(Village v, UnitType[] raiders, double guardOut)
        {
            var available = new int[Units.Count];
            foreach (var type in raiders)
            {
                int home = v.TroopCount(type);
                bool guard = type == UnitType.Spearman || type == UnitType.Swordsman;
                available[(int)type] = guard ? Math.Max(0, Math.Min(home, (int)((home + Away(v, type)) * guardOut) - Away(v, type))) : home;
            }
            return available;
        }

        /// <summary>
        /// One raid on a farm, sized to carry what the lord expects there and to beat the defenders it expects (a
        /// scout along if what it knows is getting old and one is to hand). Returns whether it went.
        /// </summary>
        bool RaidOnce(Player lord, Village v, Village target, AiNote note, bool seen, bool fresh, UnitType[] raiders, int[] available, ref int scouts)
        {
            if (Battle.CarryCapacity(available) < AiMinHaul) return false;
            double haul = ExpectedHaul(note, target);
            if (haul < AiMinHaul) return false;
            var defenders = seen ? ExpectedDefenders(note, target) : NoTroops;
            int wall = seen ? note.SeenWall : GuessWall(target) / 2;
            var party = RaidParty(raiders, available, haul, defenders, wall);
            if (party == null) return false;
            // A scout rides along to bring back fresh news whenever what the lord knows is getting old.
            if (!fresh && scouts > 0)
            {
                party[(int)UnitType.Scout] = 1;
                scouts--;
            }
            var command = Send(v, target, party, CommandKind.Attack);
            if (command == null) return false;
            for (int i = 0; i < Units.Count; i++) if (i != (int)UnitType.Scout) available[i] -= party[i];
            NoteFor(lord, target.Id, true).NextRaidAt = Now + 2 * (command.ArriveTime - Now) + AiRaidRestHours * 3600;
            return true;
        }

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
            // A village gathering its army for an operation keeps its raiders home.
            if (Mustering(v)) return;
            var (raiders, guardOut) = RaidersOf(v, style);
            var available = AvailableRaiders(v, raiders, guardOut);
            int scouts = v.TroopCount(UnitType.Scout);
            if (Battle.CarryCapacity(available) < AiMinHaul && scouts == 0) return;

            var targets = Emptied(ref raidTargets);
            foreach (var t in VillagesNear(v.X, v.Y, AiRaidRange, Emptied(ref nearby)))
                if (IsFarm(lord, t)) targets.Add((t, Distance(v, t)));
            targets.Sort((a, b) => a.distance.CompareTo(b.distance));
            // Remembered for the raiders' return (see AiRaidOnReturn): the nearest few dozen.
            if (RaidOnReturn)
            {
                raidTargetCache ??= new Dictionary<int, List<int>>();
                if (!raidTargetCache.TryGetValue(v.Id, out var cached)) raidTargetCache[v.Id] = cached = new List<int>();
                cached.Clear();
                for (int i = 0; i < targets.Count && i < AiRaidLooks * 3; i++) cached.Add(targets[i].village.Id);
            }
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

                // Never seen: scouts go first if enough can get through; players are never raided blind (barbarians,
                // whose scouts keep ours out, are).
                int lookWith = !seen && scouts > 0 ? ScoutRun(lord, v, target.Id, target.IsBarbarian ? 1 : 2) : 0;
                if (lookWith > 0 && lookWith <= scouts)
                {
                    if (scoutRuns >= AiScoutRunsPerTurn) continue;
                    var look = new int[Units.Count];
                    look[(int)UnitType.Scout] = lookWith;
                    var run = Send(v, target, look, CommandKind.Attack);
                    if (run == null) continue;
                    scouts -= lookWith;
                    scoutRuns++;
                    // Wait for the report before deciding.
                    NoteFor(lord, target.Id, true).NextRaidAt = Now + 2 * (run.ArriveTime - Now);
                    continue;
                }
                if (!seen && !target.IsBarbarian) continue;
                if (RaidOnce(lord, v, target, note, seen, fresh, raiders, available, ref scouts)) raids++;
            }
        }
    }
}
