using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Winning the world on a diplomacy world: a side holding the goal for long enough.
    /// </summary>
    public partial class World
    {
        // Villages per tribe, and all the players' villages, counted once and reused until something changes (not saved).
        [NonSerialized] Dictionary<int, int> tribeVillages;
        [NonSerialized] int playerVillages;
        [NonSerialized] double tribeVillagesAt = -1;

        /// <summary>Forgets the village counts (a village changed hands, or someone joined or left a tribe).</summary>
        void RecountTribes() => tribeVillagesAt = -1;

        void CountTribeVillages()
        {
            if (tribeVillages != null && tribeVillagesAt == Now) return;
            tribeVillages = tribeVillages ?? new Dictionary<int, int>();
            tribeVillages.Clear();
            playerVillages = 0;
            foreach (var v in Villages)
            {
                if (v.IsBarbarian) continue;
                playerVillages++;
                var owner = FindPlayer(v.OwnerId);
                if (owner != null && owner.TribeId >= 0) tribeVillages[owner.TribeId] = (tribeVillages.TryGetValue(owner.TribeId, out int n) ? n : 0) + 1;
            }
            tribeVillagesAt = Now;
        }

        /// <summary>The share of the players' villages held by a tribe and its allies.</summary>
        public double BlocShare(Tribe t) => t == null ? 0 : ShareOf(BlocOf(t));

        /// <summary>
        /// The share of the world held by a set of tribes: of every village, barbarians' included, on a diplomacy
        /// world (so winning barbarian villages is progress, not dilution); of the players' villages otherwise.
        /// </summary>
        public double ShareOf(HashSet<int> tribeIds)
        {
            CountTribeVillages();
            int held = 0;
            foreach (int id in tribeIds) held += tribeVillages.TryGetValue(id, out int n) ? n : 0;
            int all = GoalVillageCount;
            return all == 0 ? 0 : held / (double)all;
        }

        /// <summary>
        /// Whether the goal counts every village, barbarians' included (on every world, for now); if not, only the
        /// players' villages count.
        /// </summary>
        public static bool GoalOverAllVillages = true;

        /// <summary>How many villages the conquest goal is a share of.</summary>
        public int GoalVillageCount => GoalOverAllVillages ? Villages.Count : LordVillageCount;

        /// <summary>How the goal's villages are described ("villages in the realm", or "villages players rule").</summary>
        public string GoalVillagesLabel => GoalOverAllVillages ? "villages in the realm" : "villages players rule";

        /// <summary>The biggest share any bloc holds.</summary>
        public double BiggestBlocShare()
        {
            double best = 0;
            foreach (var t in ActiveTribes()) best = Math.Max(best, BlocShare(t));
            return best;
        }

        /// <summary>The tribe whose bloc (with its allies) is biggest among those that include <paramref name="t"/> or its allies.</summary>
        Tribe BiggestBlocAround(Tribe t)
        {
            Tribe best = null;
            double bestShare = -1;
            foreach (var candidate in AlliesOf(t))
            {
                double share = BlocShare(candidate);
                if (share > bestShare)
                {
                    bestShare = share;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// Game days a side must hold the conquest goal to win a diplomacy world, as in Tribal Wars' dominance
        /// endings: time for everyone else to break its grip. (A tuning value while the endgame is balanced.)
        /// </summary>
        public static double HoldDays = 10;

        /// <summary>
        /// Once a side is holding the goal, its grip only breaks if it falls more than this far below it (so a village
        /// or two changing hands doesn't reset the count). (Tuning value.)
        /// </summary>
        public static double HoldMargin = 0.03;

        /// <summary>
        /// And only if it stays that low for this many game days: a network that splits for a moment and mends
        /// keeps its clock. (Tuning value.)
        /// </summary>
        public static double HoldGraceDays = 1;

        /// <summary>When the side holding the goal fell clearly below it (-1: it hasn't).</summary>
        public double HoldBelowSince = -1;

        /// <summary>
        /// Who is holding the goal now: -1 nobody, -2 the human alone, otherwise a tribe (and its bloc); and since
        /// when.
        /// </summary>
        public int HoldTribeId = -1;
        public double HoldSince = -1;
        /// <summary>The tribes on the side holding the goal (its bloc as it stands), so the side keeps its clock if its network splits and the main part holds on.</summary>
        public List<int> HoldBloc = new List<int>();

        /// <summary>The day the side holding the goal wins, if it keeps it.</summary>
        public double HoldEnds => HoldSince + HoldDays * SecondsPerDay;

        /// <summary>Whether a holder (-2 or a tribe) is on the human's side: the human alone, or their tribe's bloc.</summary>
        public bool IsHumanSide(int holder)
        {
            if (holder == -2) return true;
            var mine = TribeOf(HumanPlayer);
            return mine != null && holder >= 0 && BlocOf(mine).Contains(holder);
        }

        /// <summary>
        /// On a diplomacy world, the dominance ending: whoever holds the goal (the human alone, their bloc, or any
        /// other bloc) for <see cref="HoldDays"/> wins the world. Everyone is told when a hold begins and when it's
        /// broken.
        /// </summary>
        void CheckDominance()
        {
            if (!Diplomacy || LostToBloc || Won) return;
            var human = HumanPlayer;
            var mine = TribeOf(human);
            double goal = Settings.ConquestGoal;
            // The side already holding keeps its grip unless it falls clearly below the goal. A side is its bloc as
            // it stood: if the network splits, the biggest part of it carries the clock on.
            if (HoldTribeId != -1)
            {
                double share = 0;
                if (HoldTribeId == -2) share = PlayerVillage != null ? HumanShare : 0;
                else
                    foreach (int id in HoldBloc ?? new List<int>())
                    {
                        var part = FindTribe(id);
                        if (part == null) continue;
                        double s = BlocShare(part);
                        if (s > share)
                        {
                            share = s;
                            HoldTribeId = part.Id;
                        }
                    }
                // Clearly below it, but only for a moment so far: the clock runs on.
                bool grace = share < goal - HoldMargin && (HoldBelowSince < 0 || Now - HoldBelowSince < HoldGraceDays * SecondsPerDay);
                if (share < goal - HoldMargin && HoldBelowSince < 0) HoldBelowSince = Now;
                if (share >= goal - HoldMargin) HoldBelowSince = -1;
                if (share >= goal - HoldMargin || grace)
                {
                    if (grace) return; // no win while it's below the goal
                    if (HoldTribeId >= 0) HoldBloc = new List<int>(BlocOf(FindTribe(HoldTribeId)));
                    if (Now < HoldEnds) return;
                    if (IsHumanSide(HoldTribeId)) Won = true;
                    else
                    {
                        LostToBloc = true;
                        WinningTribeId = HoldTribeId;
                    }
                    return;
                }
            }
            int holder = -1;
            if (human != null && PlayerVillage != null && HumanShare >= goal) holder = -2;
            else if (mine != null && PlayerVillage != null && BlocShare(mine) >= goal) holder = mine.Id;
            else
            {
                double best = goal;
                foreach (var t in ActiveTribes())
                {
                    double share = BlocShare(t);
                    if (share >= best)
                    {
                        best = share;
                        holder = t.Id;
                    }
                }
            }

            if (holder == -1)
            {
                if (HoldTribeId != -1) AnnounceHoldBroken();
                HoldTribeId = -1;
                HoldSince = -1;
                HoldBelowSince = -1;
                HoldBloc = new List<int>();
                return;
            }
            // The same side keeps its clock (the human alone and the human's bloc count as one side, and so does a bloc under another of its tribes).
            bool sameSide = HoldTribeId != -1 && (IsHumanSide(holder) && IsHumanSide(HoldTribeId)
                || HoldTribeId >= 0 && holder >= 0 && FindTribe(HoldTribeId) is Tribe held && BlocOf(held).Contains(holder));
            if (!sameSide)
            {
                if (HoldTribeId != -1) AnnounceHoldBroken();
                HoldTribeId = holder;
                HoldSince = Now;
                HoldBelowSince = -1;
                HoldBloc = holder >= 0 ? new List<int>(BlocOf(FindTribe(holder))) : new List<int>();
                AnnounceHold(holder);
                return;
            }
            HoldTribeId = holder;
            HoldBloc = holder >= 0 ? new List<int>(BlocOf(FindTribe(holder))) : new List<int>();
            if (Now < HoldEnds) return;
            if (IsHumanSide(holder)) Won = true;
            else
            {
                LostToBloc = true;
                WinningTribeId = holder;
            }
        }

        void AnnounceHold(int holder)
        {
            string until = FormatClock(HoldSince + HoldDays * SecondsPerDay);
            if (IsHumanSide(holder))
                Write(MessageKind.Note, null, "The realm is within your grasp",
                    $"Your side holds {Settings.ConquestGoal:P0} of the {GoalVillagesLabel}. Keep it until {until} and the world is yours.");
            else
            {
                var t = FindTribe(holder);
                Write(MessageKind.Note, null, $"{t?.Name} is taking the realm",
                    $"{t?.Name} [{t?.Tag}] and its allies hold {Settings.ConquestGoal:P0} of the {GoalVillagesLabel}. If they keep it until {until}, the world is theirs. Break their grip before then.");
            }
        }

        void AnnounceHoldBroken()
        {
            if (IsHumanSide(HoldTribeId))
                Write(MessageKind.Note, null, "Your hold is broken", $"Your side has fallen below {Settings.ConquestGoal:P0}. Win it back to start the count again.");
            else
                Write(MessageKind.Note, null, $"{FindTribe(HoldTribeId)?.Name ?? "The leading bloc"} has lost its grip",
                    $"They have fallen below {Settings.ConquestGoal:P0} of the {GoalVillagesLabel}. The realm is open again.");
        }
    }
}
