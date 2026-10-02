using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>How a noble train shares out the troops that go with the noblemen.</summary>
    public enum TrainEscort
    {
        /// <summary>
        /// Each nobleman after the first takes the fewest troops that keep him alive against an emptied village
        /// (its villagers and what's left of its wall); everything else goes with the first attack.
        /// </summary>
        Minimal = 0,
        /// <summary>The troops are split evenly between the attacks.</summary>
        Even = 1,
    }

    /// <summary>
    /// Noble trains, as in Tribal Wars: several attacks from one village, each with a nobleman, sent together so
    /// they land one straight after another. Only one nobleman in an attack sways the village, so it takes a train:
    /// the first attack (with most of the army) clears the defenders and each nobleman after it lowers the loyalty
    /// again before the defender can do anything about it. In Tribal Wars
    /// players timed their clicks to the millisecond; here the train is sent as one order and its attacks arrive
    /// <see cref="TrainGapSeconds"/> apart, in order.
    /// </summary>
    public partial class World
    {
        /// <summary>Game seconds between one attack of a train landing and the next.</summary>
        public const double TrainGapSeconds = 0.05;

        /// <summary>
        /// Splits troops into a noble train: one attack per nobleman, the first carrying everything that isn't an
        /// escort. With fewer than two noblemen it's a single attack. <paramref name="wall"/> is the target's wall
        /// (the first attack's rams batter it before the escorts get there).
        /// </summary>
        public static List<int[]> SplitTrain(int[] troops, TrainEscort escort, int wall)
        {
            int noble = (int)UnitType.Nobleman;
            int nobles = noble < troops.Length ? troops[noble] : 0;
            var waves = new List<int[]>();
            if (nobles < 2)
            {
                var single = new int[Units.Count];
                Array.Copy(troops, single, Math.Min(troops.Length, single.Length));
                waves.Add(single);
                return waves;
            }
            for (int w = 0; w < nobles; w++)
            {
                var wave = new int[Units.Count];
                wave[noble] = 1;
                waves.Add(wave);
            }
            var guard = escort == TrainEscort.Minimal
                ? MinimalEscort(troops, nobles, Battle.WallAfterRams(wall, troops[(int)UnitType.Ram]))
                : null;
            for (int i = 0; i < Units.Count && i < troops.Length; i++)
            {
                if (i == noble || troops[i] <= 0) continue;
                int each = escort == TrainEscort.Even ? troops[i] / nobles : guard[i];
                for (int w = 1; w < nobles; w++) waves[w][i] = each;
                waves[0][i] = troops[i] - each * (nobles - 1);
            }
            return waves;
        }

        /// <summary>Troops that make the best escorts, first to last: quick and strong in attack.</summary>
        static readonly UnitType[] EscortPreference =
        {
            UnitType.LightCavalry, UnitType.Axeman, UnitType.MountedArcher, UnitType.HeavyCavalry,
            UnitType.Swordsman, UnitType.Spearman, UnitType.Archer,
        };

        /// <summary>
        /// The fewest troops of one kind (as many of them as every later nobleman can have) that win against an
        /// empty village with this wall without the nobleman falling; nothing if he'd win alone. If no kind can,
        /// as many of the best kind as there are.
        /// </summary>
        static int[] MinimalEscort(int[] troops, int nobles, int wall)
        {
            var escort = new int[Units.Count];
            var empty = new int[Units.Count];
            bool Safe(int[] wave)
            {
                var result = Battle.Fight(wave, empty, wall, -Battle.MaxLuck);
                return result.AttackerWon && Math.Round(result.AttackerLossFraction) < 1;
            }
            var wave = new int[Units.Count];
            wave[(int)UnitType.Nobleman] = 1;
            if (Safe(wave)) return escort;
            UnitType? fallback = null;
            foreach (var type in EscortPreference)
            {
                int most = (int)type < troops.Length ? troops[(int)type] / nobles : 0;
                if (most <= 0) continue;
                fallback ??= type;
                wave[(int)type] = most;
                if (!Safe(wave))
                {
                    wave[(int)type] = 0;
                    continue;
                }
                // The fewest that do it (a bigger escort never does worse).
                int lo = 1, hi = most;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    wave[(int)type] = mid;
                    if (Safe(wave)) hi = mid;
                    else lo = mid + 1;
                }
                escort[(int)type] = lo;
                return escort;
            }
            if (fallback != null) escort[(int)fallback.Value] = troops[(int)fallback.Value] / nobles;
            return escort;
        }

        /// <summary>
        /// Sends a noble train (see <see cref="SplitTrain"/>) as attacks landing <see cref="TrainGapSeconds"/> apart,
        /// in order. Returns the attacks, or null if the troops can't be sent (all of them are checked together).
        /// </summary>
        public List<Command> SendTrain(Village from, Village to, int[] troops, TrainEscort escort, BuildingType catapultTarget = BuildingType.Headquarters)
        {
            var check = CheckSend(from, to, troops, CommandKind.Attack);
            if (check.Status != SendStatus.Ok) return null;
            var waves = SplitTrain(troops, escort, to.Level(BuildingType.Wall));
            var sent = new List<Command>();
            // All of them march at a nobleman's pace, so each lands just after the one before.
            double seconds = TravelSeconds(from, to, UnitType.Nobleman);
            if (waves.Count == 1) seconds = check.Seconds;
            for (int w = 0; w < waves.Count; w++)
            {
                var wave = waves[w];
                for (int i = 0; i < Units.Count; i++) from.Troops[i] -= wave[i];
                from.AwayPopulation += PopulationOf(wave);
                var command = March(CommandKind.Attack, from.OwnerId, from, to, wave, default, seconds + w * TrainGapSeconds);
                command.CatapultTarget = catapultTarget;
                OnAttackSent(command, from, to);
                sent.Add(command);
            }
            return sent;
        }

        /// <summary>
        /// An attack that arrives at a village its own side has won since it set out doesn't fight: if its own
        /// owner won it (an earlier nobleman of the train), the troops stay to guard it; if a tribe mate or ally did,
        /// they turn round and go home. (An attack sent at a friend on purpose still lands.)
        /// </summary>
        bool TurnBackIfFriendly(Command command, Village from, Village to)
        {
            if (to.IsBarbarian || command.TargetOwnerId == -2 || to.OwnerId == command.TargetOwnerId) return false;
            // The rest of a noble train reaching a village the train has just won: it stays to guard it.
            if (to.OwnerId == command.OwnerId)
            {
                Guard(command.OwnerId, from, to, command.Troops);
                return true;
            }
            if (!AreFriendly(command.OwnerId, to.OwnerId)) return false;
            var slowest = SlowestUnit(command.Troops);
            double seconds = slowest.HasValue ? TravelSeconds(to, from, slowest.Value) : 0;
            March(CommandKind.Return, command.OwnerId, to, from, command.Troops, default, seconds);
            return true;
        }
    }
}
