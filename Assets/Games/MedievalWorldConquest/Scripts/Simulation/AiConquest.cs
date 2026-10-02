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
            bool known = Known(note, target);
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
            // The noblemen only go where the lord has seen lately (its farming scouts usually have): at a player, in
            // the last few hours, as a player may have brought in support since. With no scouts at all, it trusts
            // that a barbarian village is empty and guesses at a player's.
            bool fresh = known && (target.IsBarbarian || Now - note.SeenAt < NobleSightingHours * 3600);
            int scouts = fresh || underAttack.Contains(target.Id) ? 0 : ScoutRun(lord, v, target.Id, 2, player: !target.IsBarbarian);
            if (scouts > 0)
            {
                var look = new int[Units.Count];
                look[(int)UnitType.Scout] = scouts;
                Send(v, target, look, CommandKind.Attack);
                return;
            }
            // (A village it can't see into is judged by a cautious guess; one seen a while ago, by half as much again.)
            var expected = known ? note.SeenTroops : target.IsBarbarian ? NoTroops : GuessDefenders(lord, target);
            if (known && !fresh)
            {
                expected = (int[])expected.Clone();
                for (int i = 0; i < expected.Length; i++) expected[i] = (int)Math.Ceiling(expected[i] * 1.5);
            }
            int wall = known ? note.SeenWall : GuessWall(target);
            // The noblemen have to live through it, so the lord wants an easy win (less so on the tribe's target).
            if (!Beatable(army, expected, known ? note.SeenAt : Now, wall, maxLoss: target == tribeTarget ? 0.5 : 0.35))
            {
                // Not enough here: if the troops out raiding would make the difference, the village gathers them for it.
                if (OffenseAway(v) > 0 && Beatable(OffenseWithRaiders(v, army), expected, known ? note.SeenAt : Now, wall, maxLoss: target == tribeTarget ? 0.5 : 0.35))
                    Muster(v);
                return;
            }
            var train = SendTrain(v, target, army, TrainEscort.Minimal, BuildingType.Wall);
            Mustered(v);
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
        /// <summary>
        /// Game hours a sighting of a village's defenders stays good enough to judge an attack by: a player's only
        /// half a day (players reinforce their villages), a barbarian village's two days.
        /// </summary>
        public const double PlayerSightingHours = 12, BarbarianSightingHours = 48;

        /// <summary>Game hours a sighting stays good enough to send noblemen at a player on (older, and scouts look again first).</summary>
        public const double NobleSightingHours = 6;

        bool Known(AiNote note, Village target) =>
            note != null && note.SeenAt >= 0 && note.SeenTroops != null
            && Now - note.SeenAt < (target.IsBarbarian ? BarbarianSightingHours : PlayerSightingHours) * 3600;

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

        /// <summary>A week after the last wait ends, a failed scouting is forgotten (the village may have changed).</summary>
        const double ScoutMemorySeconds = 7 * SecondsPerDay;

        /// <summary>Game hours a lord waits after its first failed scouting run before trying again (doubling each time, up to the most).</summary>
        public const double ScoutWaitHours = 6, ScoutWaitMaxHours = 48;

        /// <summary>
        /// How many scouts a lord sends to look at a player's village, by the size of the village they go from (as
        /// players do as a world goes on): one per 40 points, so about 20-25 early, 100 by mid-game and 200 or more
        /// late (between <see cref="MinStageScouts"/> and <see cref="MaxStageScouts"/>).
        /// </summary>
        public static int StageScouts(Village from) => StageScouts(from.Points);

        public static int StageScouts(int points) => Math.Max(MinStageScouts, Math.Min(MaxStageScouts, (int)Math.Round(points / 40.0)));

        public const int MinStageScouts = 20, MaxStageScouts = 300;

        /// <summary>The share of its scouts a village sends out on one look (the rest stay home against enemy scouts).</summary>
        const double ScoutsOut = 0.8;

        /// <summary>
        /// What a lord and its tribe mates (who share their reports) have learned about scouting a village: the
        /// scouts it takes to get through (one more than any seen there, a quarter more for growth; or what failed
        /// runs have taught), whether a run has failed lately, and when they may try again.
        /// </summary>
        (int needed, bool failed, double againAt) ScoutingKnowledge(Player lord, int villageId)
        {
            int needed = 0;
            bool failed = false;
            double again = 0;
            void Consider(Player q)
            {
                var n = q == null || q.IsHuman ? null : NoteFor(q, villageId, false);
                if (n == null) return;
                if (n.SeenAt >= 0 && n.SeenTroops != null && (int)UnitType.Scout < n.SeenTroops.Length)
                    needed = Math.Max(needed, (int)Math.Ceiling(n.SeenTroops[(int)UnitType.Scout] * 1.25) + 1);
                if (n.ScoutFails > 0 && Now < n.ScoutAgainAt + ScoutMemorySeconds)
                {
                    needed = Math.Max(needed, n.ScoutsNeeded);
                    failed = true;
                    again = Math.Max(again, n.ScoutAgainAt);
                }
            }
            Consider(lord);
            var tribe = Diplomacy ? TribeOf(lord) : null;
            if (tribe != null)
                foreach (int id in tribe.Members)
                    if (id != lord.Id) Consider(FindPlayer(id));
            return (needed, failed, again);
        }

        /// <summary>
        /// How many scouts a lord sends from a village to look at another, or 0 if it shouldn't try: it (or a tribe
        /// mate) is waiting after a failed run, or it can't send enough to get through. A player's village gets a
        /// look the size of the stage of the game (<see cref="StageScouts"/>, or what the village can spare); a farm
        /// just <paramref name="basic"/> (they seldom keep scouts). Either way, at least what's been learned it takes.
        /// </summary>
        public int ScoutRun(Player lord, Village from, int villageId, int basic, bool player = false)
        {
            var (needed, _, again) = ScoutingKnowledge(lord, villageId);
            if (again > Now) return 0;
            int spare = (int)(from.TroopCount(UnitType.Scout) * ScoutsOut);
            int least = Math.Max(basic, needed);
            int send = player ? Math.Max(least, Math.Min(StageScouts(from), spare)) : least;
            return spare >= send ? send : 0;
        }

        /// <summary>
        /// A cautious guess at the defenders of a village not seen lately: from its size, and twice that if scouting
        /// it has failed (a village guarded by that many scouts is likely guarded by more than scouts).
        /// </summary>
        int[] GuessDefenders(Player lord, Village v)
        {
            var guess = GuessDefenders(v);
            if (ScoutingKnowledge(lord, v.Id).failed)
                for (int i = 0; i < guess.Length; i++) guess[i] *= 2;
            return guess;
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
                // Scouts sent alone who didn't get through: the village has more scouts than were sent. Next time
                // it takes twice as many (and one more), after a wait that grows with every failure: 6 and 12
                // hours, a day, then two days at most. Scouts that get through wipe the slate.
                if (!fought && !result.Scouted)
                {
                    int sent = command.Troops[(int)UnitType.Scout];
                    note.ScoutsNeeded = Math.Max(note.ScoutsNeeded, 2 * sent + 1);
                    note.ScoutFails++;
                    note.ScoutAgainAt = Now + Math.Min(ScoutWaitMaxHours, ScoutWaitHours * Math.Pow(2, note.ScoutFails - 1)) * 3600;
                }
                if (result.Scouted)
                {
                    note.ScoutsNeeded = note.ScoutFails = 0;
                    note.ScoutAgainAt = 0;
                }

                // What there is to plunder: counted by scouts that got through (with the buildings, so it can
                // work out the mines and hiding place), or learned from the haul: a raid that came back with room
                // to spare emptied the place; a full one means about as much again was left.
                // (Only what enough scouts came back to see: the buildings, the stores, or neither.)
                // (A list that was never filled comes back from a save empty rather than missing: check the length.)
                int hidden = 0;
                if (report.SawBuildings && report.ScoutedLevels != null && report.ScoutedLevels.Length > 0) note.SeenLevels = (int[])report.ScoutedLevels.Clone();
                if (report.SawResources)
                {
                    if (note.SeenLevels != null && (int)BuildingType.HidingPlace < note.SeenLevels.Length)
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
