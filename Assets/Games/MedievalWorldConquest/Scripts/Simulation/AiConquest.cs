using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Lords winning villages over: gathering noblemen and sending them as noble trains.
    /// </summary>
    public partial class World
    {
        /// <summary>How many noblemen lords gather before sending them (how far they send them is <see cref="ConquestRangeOf"/>).</summary>
        public const int AiNoblesWanted = 4, AiNoblesWantedWithCoins = 2;

        /// <summary>
        /// How many noblemen a lord gathers before setting out: four at a flat price; on a gold-coin world, where
        /// every slot costs more, two (survivors come home, and it goes back until the village is won).
        /// </summary>
        int NoblesWanted => Settings.GoldCoins ? AiNoblesWantedWithCoins : AiNoblesWanted;
        /// <summary>The dearest resource in a nobleman's price: a warehouse must hold this much to save up for one.</summary>
        int NobleCostMax
        {
            get
            {
                var c = UnitCost(UnitType.Nobleman);
                return Math.Max(c.Wood, Math.Max(c.Clay, c.Iron));
            }
        }

        /// <summary>Whether a village has an academy and fewer noblemen (at home, training or marching) than a lord gathers.</summary>
        bool WantsNobles(Village v) =>
            v.Level(BuildingType.Academy) > 0 && ArmyOf(v)[(int)UnitType.Nobleman] < NoblesWanted
            && StyleOf(FindPlayer(v.OwnerId)?.Personality ?? AiPersonality.None).Expands;

        /// <summary>
        /// With a full set of noblemen at home, sends them as a noble train with the village's offensive troops to
        /// win over a village nearby: a barbarian one, or (for most personalities) a player's it knows or guesses it
        /// can beat. Sticks with its target until it's taken or an attack on it fails. A village a satellite of its
        /// faction has handed over comes first, then the tribe's target: for those, any noblemen at home go at once.
        /// </summary>
        void AiConquer(Player lord, Village v, AiStyle style)
        {
            int nobles = v.TroopCount(UnitType.Nobleman);
            if (!style.Expands || nobles == 0) return;
            var handed = HandedOverInReach(lord, v);
            var tribeTarget = handed ?? TribeTargetInReach(lord, v, style);
            if (nobles < NoblesWanted && tribeTarget == null) return;
            var underAttack = AttackTargets(lord);

            var target = tribeTarget ?? FindVillage(lord.ConquestTargetId);
            if (tribeTarget == null && (target == null || !ConquestTargetOk(lord, v, target, style)))
            {
                target = PickConquestTarget(lord, v, style, underAttack);
                lord.ConquestTargetId = target?.Id ?? -1;
            }
            if (target == null || underAttack.Contains(target.Id)) return;

            var army = new int[Units.Count];
            foreach (var type in OffensiveUnits) army[(int)type] = v.TroopCount(type);
            army[(int)UnitType.Nobleman] = nobles;
            // (What tribe mates have seen counts too: their attacks clear the way.)
            var note = Diplomacy ? SharedSighting(lord, target.Id).note : NoteFor(lord, target.Id, false);
            bool known = Known(note);
            if (target == handed)
            {
                // Its defenders stand aside: only the villagers and the wall to get past, so each nobleman takes just
                // the escort that gets him through, and the army stays home.
                var sizing = (int[])army.Clone();
                sizing[(int)UnitType.Nobleman] = Math.Max(2, nobles);
                var escort = SplitTrain(sizing, TrainEscort.Minimal, target.Level(BuildingType.Wall))[1];
                var party = new int[Units.Count];
                for (int i = 0; i < Units.Count; i++) party[i] = Math.Min(army[i], escort[i] * nobles);
                party[(int)UnitType.Nobleman] = nobles;
                SendTrain(v, target, party, TrainEscort.Minimal, BuildingType.Wall);
                return;
            }
            // The noblemen only go where the lord has seen lately (its farming scouts usually have); with no scouts
            // at all, it trusts that a barbarian village is empty and guesses at a player's.
            if (!known && v.TroopCount(UnitType.Scout) > 0 && !underAttack.Contains(target.Id))
            {
                var look = new int[Units.Count];
                look[(int)UnitType.Scout] = 2;
                Send(v, target, look, CommandKind.Attack);
                return;
            }
            var expected = known ? note.SeenTroops : target.IsBarbarian ? NoTroops : GuessDefenders(target);
            int wall = known ? note.SeenWall : GuessWall(target);
            // The noblemen have to live through it, so the lord wants an easy win (less so on the tribe's target).
            if (!Beatable(army, expected, known ? note.SeenAt : Now, wall, maxLoss: target == tribeTarget ? 0.5 : 0.35)) return;
            var train = SendTrain(v, target, army, TrainEscort.Minimal, BuildingType.Wall);
            // A noble train at a player is worth hiding among fakes.
            if (train != null && !target.IsBarbarian) MaybeFakes(lord, target);
        }

        /// <summary>A village handed over to this lord, near enough for this village's noblemen and not already under its attack.</summary>
        Village HandedOverInReach(Player lord, Village v)
        {
            if (!FactionsFormed) return null;
            foreach (var t in VillagesNear(v.X, v.Y, FeedRange))
                if (FedTo(t, lord.Id, Now) && t.OwnerId != lord.Id) return t;
            return null;
        }

        /// <summary>The tribe's target, if it's one this village's noblemen can go for.</summary>
        Village TribeTargetInReach(Player lord, Village v, AiStyle style)
        {
            var tribe = Diplomacy ? TribeOf(lord) : null;
            if (tribe == null || tribe.TargetUntil <= Now) return null;
            var t = FindVillage(tribe.TargetVillageId);
            return t != null && ConquestTargetOk(lord, v, t, style) ? t : null;
        }

        bool ConquestTargetOk(Player lord, Village from, Village t, AiStyle style)
        {
            if (t.OwnerId == lord.Id || Distance(from, t) > ConquestRangeOf(lord)) return false;
            // (Once the barbarian land is nearly gone, every lord who expands turns on other players, as does anyone
            // whose tribe has named the village its target.)
            if (!t.IsBarbarian && ((!style.ConquersPlayers && !LateGame && !IsTribeTarget(lord, t)) || IsProtected(t.OwnerId) || AreFriendly(lord.Id, t.OwnerId))) return false;
            var note = NoteFor(lord, t.Id, false);
            return note == null || note.AvoidUntil <= Now;
        }

        /// <summary>Whether a village is the target the lord's tribe has named (and still means).</summary>
        bool IsTribeTarget(Player lord, Village t)
        {
            var tribe = Diplomacy ? TribeOf(lord) : null;
            return tribe != null && tribe.TargetVillageId == t.Id && tribe.TargetUntil > Now;
        }

        /// <summary>The late game: barbarian villages are under a tenth of the world, so lords turn on each other.</summary>
        bool LateGame => VillagesOf(-1).Count < 0.1 * Villages.Count && WorldLocked;

        /// <summary>
        /// How far a lord reaches for villages to win over: a consolidator (spread 0) no further than 10 fields, a
        /// spreader (spread 1) up to 22.
        /// </summary>
        static double ConquestRangeOf(Player lord) => 10 + 12 * Math.Max(0, Math.Min(1, lord.Spread));

        /// <summary>
        /// The most tempting village to win over: well built and close (barbarians' first: they're easier). How much
        /// "close" matters is the lord's nature: a consolidator wants what's next door, a spreader weighs a rich
        /// village further off almost as highly.
        /// </summary>
        Village PickConquestTarget(Player lord, Village v, AiStyle style, HashSet<int> underAttack)
        {
            Village best = null;
            double bestScore = 0;
            double nearness = 0.6 + 1.2 * (1 - Math.Max(0, Math.Min(1, lord.Spread)));
            bool late = LateGame;
            int own = PointsOf(lord);
            foreach (var t in VillagesNear(v.X, v.Y, ConquestRangeOf(lord)))
            {
                if (underAttack.Contains(t.Id) || !ConquestTargetOk(lord, v, t, style)) continue;
                double score = (t.Points + 50) / Math.Pow(1 + Distance(v, t), nearness);
                if (!t.IsBarbarian)
                {
                    // Players' villages are harder, until there's little else left; the weak, and enemies, first.
                    score *= late ? 1.2 : 0.6;
                    int theirs = PointsOf(FindPlayer(t.OwnerId));
                    score *= 1 + 0.3 * Math.Min(3, own / (double)Math.Max(1, theirs));
                    if (AtWar(lord.Id, t.OwnerId)) score *= 1.5;
                    // The tribe's target: its members' attacks are wearing it down, and the noblemen follow.
                    if (IsTribeTarget(lord, t)) score *= 3;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>The villages a lord's attacks (or scouts) are already marching on.</summary>
        HashSet<int> AttackTargets(Player lord)
        {
            var targets = new HashSet<int>();
            foreach (var c in CommandsOf(lord.Id))
                if (c.Kind == CommandKind.Attack) targets.Add(c.ToVillageId);
            return targets;
        }

        /// <summary>Whether the lord's sighting of a village's defenders is recent enough to act on.</summary>
        bool Known(AiNote note) => note != null && note.SeenAt >= 0 && note.SeenTroops != null && Now - note.SeenAt < 2 * SecondsPerDay;

        /// <summary>
        /// Whether an attack beats the defenders seen at <paramref name="seenAt"/> (assumed to have grown a little since),
        /// by the skill's safety margin, at the worst luck, without losing most of the army.
        /// </summary>
        bool Beatable(int[] offense, int[] seenTroops, double seenAt, int wall, double maxLoss = 0.7)
        {
            double growth = (1 + 0.3 * Math.Max(0, Now - seenAt) / SecondsPerDay) * SkillAttackMargin;
            var expected = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < seenTroops.Length; i++) expected[i] = (int)Math.Ceiling(seenTroops[i] * growth);
            var result = Battle.Fight(offense, expected, wall, -Battle.MaxLuck);
            return result.AttackerWon && result.AttackerLossFraction < maxLoss;
        }

        /// <summary>A cautious guess at a village's defenders from its points, for lords with no scouts.</summary>
        static int[] GuessDefenders(Village v)
        {
            var guess = new int[Units.Count];
            guess[(int)UnitType.Spearman] = (int)(v.Points * 0.5);
            guess[(int)UnitType.Swordsman] = (int)(v.Points * 0.25);
            return guess;
        }

        /// <summary>
        /// After a battle: an attacking lord remembers what it saw of the defenders (if anyone lived to tell, or its
        /// scouts got through) and stays away for a while if it lost; a defending lord remembers who attacked it.
        /// </summary>
        void AiLearnFromBattle(Command command, Village target, BattleResult result, int[] defenders, int[] defenderLost,
            bool anySurvivors, Cost loot, int lootCapacity, BattleReport report)
        {
            bool fought = false;
            for (int i = 0; i < Units.Count && i < command.Troops.Length; i++)
                if ((UnitType)i != UnitType.Scout && command.Troops[i] > 0) fought = true;

            var attacker = FindPlayer(command.OwnerId);
            if (attacker != null && !attacker.IsHuman)
            {
                var note = NoteFor(attacker, target.Id, true);
                if (anySurvivors || result.Scouted)
                {
                    note.SeenAt = Now;
                    note.SeenTroops = new int[Units.Count];
                    for (int i = 0; i < Units.Count; i++) note.SeenTroops[i] = Math.Max(0, defenders[i] - defenderLost[i]);
                    note.SeenWall = target.Level(BuildingType.Wall);
                }
                if (fought && !result.AttackerWon) note.AvoidUntil = Now + (target.IsBarbarian ? 2 : 1) * SecondsPerDay;
                // Scouts sent alone who didn't get through: the village has scouts of its own. Leave it be a while.
                if (!fought && !result.Scouted) note.AvoidUntil = Now + SecondsPerDay;

                // What there is to plunder: counted by scouts that got through (with the buildings, so it can
                // work out the mines and hiding place), or learned from the haul: a raid that came back with room
                // to spare emptied the place; a full one means about as much again was left.
                int hidden = 0;
                if (result.Scouted && report.ScoutedLevels != null)
                {
                    note.SeenLevels = (int[])report.ScoutedLevels.Clone();
                    hidden = Buildings.HiddenCapacity(note.SeenLevels[(int)BuildingType.HidingPlace]);
                    note.LootSeenAt = Now;
                    note.LootWood = Math.Max(0, report.ScoutedResources.Wood - hidden);
                    note.LootClay = Math.Max(0, report.ScoutedResources.Clay - hidden);
                    note.LootIron = Math.Max(0, report.ScoutedResources.Iron - hidden);
                }
                else if (fought && result.AttackerWon && anySurvivors)
                {
                    bool full = loot.Wood + loot.Clay + loot.Iron >= lootCapacity;
                    note.LootSeenAt = Now;
                    note.LootWood = full ? loot.Wood : 0;
                    note.LootClay = full ? loot.Clay : 0;
                    note.LootIron = full ? loot.Iron : 0;
                }
                // A player's village that was hardly worth the trip is left alone for a day.
                int taken = loot.Wood + loot.Clay + loot.Iron;
                if (fought && result.AttackerWon && !target.IsBarbarian && taken < 0.3 * lootCapacity)
                    note.NextRaidAt = Math.Max(note.NextRaidAt, Now + (command.ArriveTime - command.DepartTime) + SecondsPerDay);
            }

            var defender = target.IsBarbarian ? null : FindPlayer(target.OwnerId);
            if (defender != null && !defender.IsHuman && fought && command.OwnerId != defender.Id)
            {
                defender.LastAttackedAt = Now;
                defender.LastAttackerId = command.OwnerId;
                // A noob beaten too often in too short a time gives up.
                if (defender.Personality == AiPersonality.Noob && result.AttackerWon)
                {
                    defender.HitTimes.Add(Now);
                    defender.HitTimes.RemoveAll(t => Now - t > NoobQuitWindowHours * 3600);
                    if (defender.HitTimes.Count >= NoobQuitHits) QuitLord(defender);
                }
            }
        }
    }
}
