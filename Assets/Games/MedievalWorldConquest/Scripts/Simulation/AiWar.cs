using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Lords at war with other players: attacks on those who fight back, and the fakes that hide them.
    /// </summary>
    public partial class World
    {
        /// <summary>
        /// Now and then, looks for another player's village in reach that it can beat (as far as it knows), scouting
        /// it first if it can, then sends its whole offensive army, with catapults aimed at the wall or barracks.
        /// </summary>
        /// <summary>The fewest scouts a lord sends to look at a player's village before war.</summary>
        const int WarScouts = 3;

        void AiAttack(Player lord, Village v, AiStyle style)
        {
            // A tribe's named target draws its members in, whatever their mood; otherwise war is a matter of temperament.
            AiLoneFake(lord, v);
            var tribe = Diplomacy ? TribeOf(lord) : null;
            var target = tribe != null && tribe.TargetUntil > Now ? FindVillage(tribe.TargetVillageId) : null;
            bool tribeOp = target != null && Distance(v, target) <= AiAttackRange && AiRandom(lord, 13) < 0.5;
            if (!tribeOp && AiRandom(lord, 11) >= style.Aggression * SkillAggression) return;

            var offense = new int[Units.Count];
            int power = 0;
            foreach (var type in OffensiveUnits)
            {
                offense[(int)type] = v.TroopCount(type);
                power += offense[(int)type] * Units.Get(type).Attack;
            }
            // Too few at home to fight. (Only if the troops out raiding would make the difference is it worth
            // looking for a target to gather them for: see below.)
            bool canFight = power >= AiMinAttackPower;
            if (!canFight && power + OffenseAway(v) < AiMinAttackPower) return;

            // The most tempting target it can beat, as far as it knows: close, bigger (more to plunder), and all the
            // more if they attacked us. Villages it knows nothing about are scouted first when it has scouts;
            // otherwise it judges them by a cautious guess from their size.
            // Candidates are ranked by how tempting they are first, and only then checked (a battle worked out in
            // advance each) from the most tempting down, stopping at the first it can beat: the same choice as
            // checking them all, for a fraction of the work in a crowded neighborhood.
            var underAttack = AttackTargets(lord);
            var attackCandidates = Emptied(ref this.attackCandidates);
            foreach (var t in VillagesNear(v.X, v.Y, AiAttackRange, Emptied(ref nearby)))
            {
                // War is for players who fight back; barbarians, noobs and inactive players are farmed instead.
                if (t.IsBarbarian || t.OwnerId == lord.Id || IsProtected(t.OwnerId) || IsFarm(lord, t) || AreFriendly(lord.Id, t.OwnerId)) continue;
                var note = NoteFor(lord, t.Id, false);
                if (note != null && (note.AvoidUntil > Now || note.NextRaidAt > Now)) continue;
                if (underAttack.Contains(t.Id)) continue;
                bool scoutFirst = !Known(note, t) && ScoutRun(lord, v, t.Id, WarScouts, player: true) > 0;
                // Near and big enough to be worth it, but the biggest aren't singled out: size counts for less and less.
                double score = (20 * Math.Sqrt(t.Points) + 100) / (2 + Distance(v, t));
                if (scoutFirst) score *= 0.5; // a sure thing beats a maybe
                if (t.OwnerId == lord.LastAttackerId) score *= 2;
                if (AtWar(lord.Id, t.OwnerId)) score *= 2;     // the tribe's enemies first
                if (t == target) score *= 4;                   // and the tribe's target above all
                attackCandidates.Add((t, score));
            }
            attackCandidates.Sort((a, b) => b.score.CompareTo(a.score));

            Village best = null;
            bool bestNeedsScouting = false;
            foreach (var (t, _) in attackCandidates)
            {
                if (!canFight) break;
                // What the lord or its tribe mates have seen there.
                var note = Diplomacy ? SharedSighting(lord, t.Id).note : NoteFor(lord, t.Id, false);
                bool known = Known(note, t);
                if (known && !Beatable(offense, note.SeenTroops, note.SeenAt, note.SeenWall)) continue;
                // Scouts go first if enough of them can get through; otherwise it's a cautious guess (all the more
                // cautious where scouting has failed).
                bool canScout = !known && ScoutRun(lord, v, t.Id, WarScouts, player: true) > 0;
                if (!known && !canScout && !Beatable(offense, GuessDefenders(lord, t), Now, GuessWall(t))) continue;
                best = t;
                bestNeedsScouting = canScout;
                break;
            }
            // Nothing it can beat with what's home. If the most tempting target could be beaten with the troops out
            // raiding back as well, the village gathers them for it (a concrete operation, not a mere wish to fight).
            if (best == null)
            {
                if (attackCandidates.Count > 0 && OffenseAway(v) > 0)
                {
                    var t = attackCandidates[0].village;
                    var all = OffenseWithRaiders(v, offense);
                    var note = Diplomacy ? SharedSighting(lord, t.Id).note : NoteFor(lord, t.Id, false);
                    bool winnable = Known(note, t) ? Beatable(all, note.SeenTroops, note.SeenAt, note.SeenWall)
                                                   : Beatable(all, GuessDefenders(lord, t), Now, GuessWall(t));
                    if (winnable) Muster(v);
                }
                return;
            }

            if (bestNeedsScouting)
            {
                // Look before leaping: scouts now, the army on a later turn if the coast is clear.
                var scouts = new int[Units.Count];
                scouts[(int)UnitType.Scout] = ScoutRun(lord, v, best.Id, WarScouts, player: true);
                Send(v, best, scouts, CommandKind.Attack);
                return;
            }

            var seen = Diplomacy ? SharedSighting(lord, best.Id).note : NoteFor(lord, best.Id, false);
            int wall = Known(seen, best) ? seen.SeenWall : GuessWall(best);
            var aim = wall > 0 && offense[(int)UnitType.Ram] == 0 ? BuildingType.Wall : BuildingType.Barracks;
            var command = Send(v, best, offense, CommandKind.Attack, aim);
            if (command != null) Mustered(v);
            // Give the village time to recover before the next attack, so the lord doesn't hammer it non-stop.
            if (command != null) NoteFor(lord, best.Id, true).NextRaidAt = Now + 2 * (command.ArriveTime - Now) + 6 * 3600;
            // A ram attack is worth hiding among fakes.
            if (command != null && AttackSpeeds.IsDangerous(AttackSpeeds.Of(command.Troops))) MaybeFakes(lord, best);
        }

        /// <summary>
        /// Warlike lords hide a real ram or noble attack on a player among fakes, as Tribal Wars players did: 1 to 3
        /// single rams (or catapults), from any of their villages that has one, at other villages of the same player
        /// or their tribe nearby. Defenders see only the speed, so each looks as dangerous as the real one.
        /// </summary>
        void MaybeFakes(Player lord, Village real)
        {
            if (!Warlike(lord) || real.IsBarbarian || AiRandom(lord, 17) >= SkillFakeChance) return;
            int count = 1 + (int)(AiRandom(lord, 18) * 3);
            var victims = new HashSet<int> { real.OwnerId };
            var theirTribe = Diplomacy ? TribeOf(real.OwnerId) : null;
            if (theirTribe != null) victims.UnionWith(theirTribe.Members);
            foreach (var t in VillagesNear(real.X, real.Y, 15))
            {
                if (count == 0) break;
                if (t == real || t.IsBarbarian || !victims.Contains(t.OwnerId) || IsProtected(t.OwnerId) || AreFriendly(lord.Id, t.OwnerId)) continue;
                if (SendFake(lord, t)) count--;
            }
        }

        /// <summary>
        /// Now and then, a warlike lord sends a fake on its own at an enemy (on a world with tribes: one its tribe is
        /// at war with, or the tribe's target; without tribes: a player it's already attacking).
        /// </summary>
        void AiLoneFake(Player lord, Village v)
        {
            if (!Warlike(lord) || SkillFakeChance <= 0 || (lord.LastFakeAt >= 0 && Now - lord.LastFakeAt < SkillFakeGapDays * SecondsPerDay)) return;
            if (AiRandom(lord, 19) >= 0.1 * FakeRate) return;
            var attacking = new HashSet<int>();
            if (!Diplomacy)
                foreach (var c in CommandsOf(lord.Id))
                    if (c.Kind == CommandKind.Attack && FindVillage(c.ToVillageId) is Village to && !to.IsBarbarian) attacking.Add(to.OwnerId);
            foreach (var t in VillagesNear(v.X, v.Y, AiAttackRange))
            {
                if (t.IsBarbarian || t.OwnerId == lord.Id || IsProtected(t.OwnerId) || AreFriendly(lord.Id, t.OwnerId)) continue;
                bool enemy = Diplomacy ? AtWar(lord.Id, t.OwnerId) || IsTribeTarget(lord, t) : attacking.Contains(t.OwnerId);
                if (!enemy || !SendFake(lord, t)) continue;
                lord.LastFakeAt = Now;
                return;
            }
        }

        /// <summary>Sends one ram (or catapult) at a village from the nearest of the lord's villages that has one in reach.</summary>
        bool SendFake(Player lord, Village target)
        {
            Village from = null;
            double nearest = AiAttackRange;
            foreach (var u in VillagesOf(lord.Id))
            {
                if (u.TroopCount(UnitType.Ram) + u.TroopCount(UnitType.Catapult) == 0) continue;
                double d = Distance(u, target);
                if (d <= nearest)
                {
                    nearest = d;
                    from = u;
                }
            }
            if (from == null) return false;
            var fake = new int[Units.Count];
            fake[from.TroopCount(UnitType.Ram) > 0 ? (int)UnitType.Ram : (int)UnitType.Catapult] = 1;
            return Send(from, target, fake, CommandKind.Attack) != null;
        }

        static int GuessWall(Village v) => Math.Min(20, v.Points / 60);

        // Scratch lists reused from turn to turn (not saved; made when first needed), so busy worlds don't churn memory.
        [NonSerialized] List<(Village village, double score)> attackCandidates;
        [NonSerialized] List<(Village village, double distance)> raidTargets;
        [NonSerialized] List<Village> nearby;

        static List<T> Emptied<T>(ref List<T> list)
        {
            if (list == null) list = new List<T>();
            list.Clear();
            return list;
        }
    }
}
