using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Winning villages over, as in Tribal Wars: every nobleman who survives a victorious attack lowers the
    /// village's loyalty by 20 to 35 points; loyalty creeps back up by one point an hour; and when it reaches zero
    /// the village changes hands, its attackers moving in. Also: winning the world, and starting again after
    /// losing everything.
    /// </summary>
    public partial class World
    {
        public const double MaxLoyalty = 100;
        /// <summary>Loyalty a village regains each game hour by itself.</summary>
        public const double LoyaltyPerHour = 1;
        /// <summary>The loyalty a surviving nobleman takes away: somewhere in this range, at random.</summary>
        public const int NobleLoyaltyMin = 20, NobleLoyaltyMax = 35;
        /// <summary>A freshly conquered village's loyalty to its new owner.</summary>
        public const double LoyaltyAfterConquest = 25;

        /// <summary>Whether the player has reached the conquest goal, and whether they've been told.</summary>
        public bool Won, VictoryShown;

        /// <summary>
        /// The share of the lords' villages (everyone's but the barbarians', the player's included) that the human
        /// player owns, 0 to 1. Reaching <see cref="WorldSettings.ConquestGoal"/> wins.
        /// </summary>
        public double HumanShare
        {
            get
            {
                var human = HumanPlayer;
                if (human == null) return 0;
                int own = VillagesOf(human.Id).Count, all = GoalVillageCount;
                return all == 0 ? 0 : own / (double)all;
            }
        }

        /// <summary>How many villages belong to players (anyone but the barbarians).</summary>
        public int LordVillageCount
        {
            get
            {
                int n = 0;
                foreach (var v in Villages)
                    if (!v.IsBarbarian) n++;
                return n;
            }
        }

        /// <summary>
        /// A lord gives up: it stops playing, and its villages go barbarian, keeping their buildings and whatever
        /// troops were at home (in Tribal Wars, the only barbarians with troops are villages their players left), and
        /// their names.
        /// </summary>
        public void QuitLord(Player lord)
        {
            if (lord == null || lord.IsHuman || lord.Quit) return;
            LeaveTribe(lord);
            lord.Quit = true;
            foreach (var v in new List<Village>(VillagesOf(lord.Id)))
            {
                Touch(v);
                SetOwner(v, -1);
                v.Queue.Clear();
                v.Recruitment.Clear();
                v.AwayPopulation = 0;
                v.GrowthSteps = 0;
                ScheduleBarbarianGrowth(v);
            }
            foreach (var p in Players)
                if (p.ConquestTargetId >= 0 && FindVillage(p.ConquestTargetId)?.IsBarbarian == true) p.ConquestTargetId = -1;
            CheckVictory(); // fewer lords' villages: the player's share may now be enough
        }

        /// <summary>Whether the human player has lost every village they had.</summary>
        public bool HumanDefeated => HumanPlayer != null && PlayerVillage == null;

        /// <summary>
        /// The loyalty an attack's surviving nobleman takes away from its target: 20 to 35 points, repeatably random
        /// from the world seed and the attack. Only one counts, however many came (as in Tribal Wars: it takes a
        /// noble train, one attack per nobleman).
        /// </summary>
        double LoyaltyLoss(Command command) =>
            NobleLoyaltyMin + Math.Floor(Terrain.Hash(Settings.Seed ^ 0x1B873593, command.Id, 0) * (NobleLoyaltyMax - NobleLoyaltyMin + 1));

        /// <summary>
        /// The noblemen who survived a victorious attack sway the village; if its loyalty reaches zero it's
        /// conquered. Returns whether it was. The report records the loyalty before and after.
        /// </summary>
        bool Sway(Command command, Village attacker, Village target, int[] survivors, BattleReport report)
        {
            int nobles = survivors[(int)UnitType.Nobleman];
            if (nobles <= 0 || target.OwnerId == command.OwnerId) return false;

            report.LoyaltyBefore = (int)Math.Floor(target.Loyalty);
            target.Loyalty -= LoyaltyLoss(command);
            // Loyalty grows back a little at a time, so it's seldom a whole number: the village falls once what's
            // shown (the whole number) reaches 0, not only at exactly 0, so a report never says "0" of a village that held.
            if (target.Loyalty >= 1)
            {
                report.LoyaltyAfter = (int)Math.Floor(target.Loyalty);
                return false;
            }
            Conquer(command, attacker, target, survivors);
            report.LoyaltyAfter = (int)LoyaltyAfterConquest;
            report.Conquered = true;
            return true;
        }

        /// <summary>
        /// A village changes hands. Its new owner's surviving attackers stay to guard it, less the nobleman who now
        /// rules it; whatever was being built or trained there is lost; a barbarian village gets a proper name.
        /// </summary>
        void Conquer(Command command, Village attacker, Village target, int[] survivors)
        {
            int oldOwner = target.OwnerId;
            bool wasBarbarian = target.IsBarbarian;
            bool handedOver = FedTo(target, command.OwnerId, Now);
            // Tribes feel it: a loss for one, a win for the other.
            var loserTribe = TribeOf(oldOwner);
            var winnerTribe = TribeOf(command.OwnerId);
            if (loserTribe != null && loserTribe != winnerTribe) loserTribe.LossesSinceTick++;
            if (winnerTribe != null) winnerTribe.ConquestsSinceTick++;
            if (!wasBarbarian && FindPlayer(oldOwner) is Player beaten) beaten.LostVillageAt = Now;
            AddStat(command.OwnerId, StatKind.VillagesConquered, 1);
            if (!wasBarbarian) NoteWarConquest(command.OwnerId, oldOwner);
            if (!wasBarbarian) AddStat(oldOwner, StatKind.VillagesLost, 1);
            SetOwner(target, command.OwnerId);
            target.Loyalty = LoyaltyAfterConquest;
            // A plain barbarian village gets a proper name; one a player left keeps the name it had.
            if (wasBarbarian && target.Name == BarbarianName) target.Name = NewPlaceName(new Random(Settings.Seed ^ command.Id));
            target.Queue.Clear();
            target.Recruitment.Clear();
            // A village its old owner handed over: their troops had already left for their nearest other village.
            if (handedOver)
            {
                Village refuge = null;
                foreach (var v in VillagesOf(oldOwner))
                    if (refuge == null || Distance(target, v) < Distance(target, refuge)) refuge = v;
                if (refuge != null)
                    for (int i = 0; i < Units.Count; i++) refuge.Troops[i] += target.Troops[i];
                Array.Clear(target.Troops, 0, target.Troops.Length);
            }
            target.FedTo = -1;
            // The old owner's troops out on the march are no longer this village's concern, and those it had
            // stationed in other villages, supporting them, are gone with it (as are any still on their way there:
            // see Station). One look through the villages' support, once per conquest.
            target.AwayPopulation = 0;
            if (!wasBarbarian)
                foreach (var v in Villages)
                    if (v.Supports.Count > 0) v.Supports.RemoveAll(g => g.FromVillageId == target.Id && g.OwnerId == oldOwner);

            // The nobleman who rules it now is spent. The other survivors stay to guard it, as in Tribal Wars, as
            // support from the village they came from (still counted at that village's farm, not the new one's)
            // until they're sent home.
            survivors[(int)UnitType.Nobleman]--;
            attacker.AwayPopulation = Math.Max(0, attacker.AwayPopulation - Units.Get(UnitType.Nobleman).Cost.Population);
            Guard(command.OwnerId, attacker, target, survivors);

            // Lords who were after this village find another; the loser remembers who took it.
            foreach (var p in Players)
                if (p.ConquestTargetId == target.Id) p.ConquestTargetId = -1;
            var loser = FindPlayer(oldOwner);
            if (loser != null && !loser.IsHuman)
            {
                loser.LastAttackedAt = Now;
                loser.LastAttackerId = command.OwnerId;
                // A lord (or inactive player) who loses their last village is finished: out of their tribe and the rankings.
                if (VillagesOf(loser.Id).Count == 0)
                {
                    LeaveTribe(loser);
                    loser.Quit = true;
                }
            }

            CheckVictory();
        }

        /// <summary>
        /// The troops in a village their owner has just won (or that their noble train reached after it was won).
        /// The player's stay to guard it, as support from the village they came from, until sent home; a lord's
        /// head straight home, back to raiding and the next conquest.
        /// </summary>
        void Guard(int ownerId, Village from, Village host, int[] troops)
        {
            if (Total(troops) <= 0) return;
            if (!IsHuman(ownerId))
            {
                var slowest = SlowestUnit(troops);
                March(CommandKind.Return, ownerId, host, from, troops, default, slowest.HasValue ? TravelSeconds(host, from, slowest.Value) : 0);
                return;
            }
            var group = host.Supports.Find(g => g.FromVillageId == from.Id);
            if (group == null) host.Supports.Add(group = new SupportGroup { FromVillageId = from.Id, OwnerId = ownerId, Troops = new int[Units.Count] });
            for (int i = 0; i < Units.Count && i < troops.Length; i++) group.Troops[i] += troops[i];
        }

        /// <summary>
        /// Marks the world won once the player holds the goal's share of the players' villages: alone, or on a
        /// diplomacy world with their tribe and its allies. On a diplomacy world, a bloc without the player that
        /// gets there first takes the world instead.
        /// </summary>
        void CheckVictory()
        {
            // A diplomacy world ends by dominance: the goal held for a while (see CheckDominance).
            if (Diplomacy) CheckDominance();
            else if (!Won && HumanPlayer != null && PlayerVillage != null && HumanShare >= Settings.ConquestGoal) Won = true;
        }

        /// <summary>
        /// After losing every village, the player starts again with a new village on the frontier of the settled
        /// world, under fresh beginner protection.
        /// </summary>
        public Village RespawnHuman()
        {
            var human = HumanPlayer;
            if (human == null || !HumanDefeated) return PlayerVillage;
            // Out on the edge of the settled world, well away from the crowded middle.
            var rng = new Random(unchecked(Settings.Seed * 31 + (int)Now));
            if (!TryFindSpot(rng, () => SpawnRadius + rng.NextDouble() * RingSpread, out int x, out int y)
                && !TryFindSpot(rng, () => rng.NextDouble() * SpawnRadius, out x, out y))
                return null;
            var village = new Village { Id = NewVillageId(), Name = NewPlaceName(rng), X = x, Y = y, OwnerId = human.Id };
            village.SetUpAsNew();
            AddVillage(village);
            human.ProtectedUntil = Now + Settings.ProtectionDays * SecondsPerDay;
            CurrentVillageId = village.Id;
            return village;
        }
    }
}
