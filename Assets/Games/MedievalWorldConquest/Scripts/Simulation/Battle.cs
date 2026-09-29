using System;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>What happened in a fight, before it's applied to any villages.</summary>
    public class BattleResult
    {
        public bool AttackerWon;
        /// <summary>Fraction (0..1) of every fighting (non-scout) unit each side lost.</summary>
        public double AttackerLossFraction, DefenderLossFraction;
        /// <summary>Fraction of each side's scouts lost in the scouting fight.</summary>
        public double AttackerScoutLossFraction, DefenderScoutLossFraction;
        /// <summary>Whether the attacker's scouts got through (so the report can show what's in the village).</summary>
        public bool Scouted;
        /// <summary>The wall before and after the rams' work.</summary>
        public int WallBefore, WallAfter;
        /// <summary>Strength of each side after class weighting, the wall and luck (for the report).</summary>
        public double AttackStrength, DefenseStrength;
        public double Luck;
    }

    /// <summary>
    /// The battle rules, modelled on Tribal Wars, as pure functions of the armies involved:
    /// scouts fight scouts; rams knock down the wall; the attack is split by unit class and each defender counters
    /// with its matching defence (plus the villagers' base defence), boosted by the wall and swung by luck; the
    /// stronger side wins, losing (weaker ÷ stronger)^1.5 of its army while the loser is wiped out.
    /// </summary>
    public static class Battle
    {
        /// <summary>Defence every village has from its villagers, before the wall bonus (per unit class).</summary>
        public const double VillagerDefense = 20;
        /// <summary>Luck swings the attack by up to this much either way.</summary>
        public const double MaxLuck = 0.25;
        /// <summary>The winner loses (loser ÷ winner) to this power of its army.</summary>
        public const double LossExponent = 1.5;

        /// <summary>Rams needed to knock a wall down one level from <paramref name="level"/>.</summary>
        public static int RamsPerWallLevel(int level) => (level + 1) / 2 + 1;

        /// <summary>The wall level left after these rams have battered it.</summary>
        public static int WallAfterRams(int wallLevel, int rams)
        {
            while (wallLevel > 0 && rams >= RamsPerWallLevel(wallLevel))
            {
                rams -= RamsPerWallLevel(wallLevel);
                wallLevel--;
            }
            return wallLevel;
        }

        /// <summary>Catapults needed to knock a building down one level from <paramref name="level"/>.</summary>
        public static int CatapultsPerLevel(int level) => 2 + level / 2;

        /// <summary>The building level left after these catapults (the survivors of a winning attack) have hit it.</summary>
        public static int LevelAfterCatapults(int level, int catapults)
        {
            while (level > 0 && catapults >= CatapultsPerLevel(level))
            {
                catapults -= CatapultsPerLevel(level);
                level--;
            }
            return level;
        }

        public static BattleResult Fight(int[] attackers, int[] defenders, int wallLevel, double luck)
        {
            var result = new BattleResult { Luck = luck, WallBefore = wallLevel };

            // Scouts fight scouts first.
            int scoutsA = Count(attackers, UnitType.Scout), scoutsD = Count(defenders, UnitType.Scout);
            if (scoutsA > 0)
            {
                if (scoutsA > scoutsD)
                {
                    result.Scouted = true;
                    result.AttackerScoutLossFraction = Math.Pow((double)scoutsD / scoutsA, LossExponent);
                    result.DefenderScoutLossFraction = scoutsD > 0 ? 1 : 0;
                }
                else
                {
                    result.AttackerScoutLossFraction = 1;
                    result.DefenderScoutLossFraction = Math.Pow((double)scoutsA / scoutsD, LossExponent);
                }
            }

            // Rams batter the wall before the fight.
            result.WallAfter = WallAfterRams(wallLevel, Count(attackers, UnitType.Ram));

            // The main fight: every unit but scouts.
            double attackInfantry = 0, attackCavalry = 0, attackArcher = 0;
            bool anyFighters = false;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units.Get((UnitType)i);
                if (u.Type == UnitType.Scout) continue;
                int n = Count(attackers, u.Type);
                if (n > 0) anyFighters = true;
                double a = (double)n * u.Attack;
                switch (u.Class)
                {
                    case UnitClass.Cavalry: attackCavalry += a; break;
                    case UnitClass.Archer: attackArcher += a; break;
                    default: attackInfantry += a; break; // infantry and siege
                }
            }
            if (!anyFighters)
            {
                // A scouting run: no main battle.
                result.AttackerWon = result.Scouted;
                return result;
            }

            double defenseInfantry = VillagerDefense, defenseCavalry = VillagerDefense, defenseArcher = VillagerDefense;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units.Get((UnitType)i);
                if (u.Type == UnitType.Scout) continue;
                int n = Count(defenders, u.Type);
                defenseInfantry += (double)n * u.DefenseInfantry;
                defenseCavalry += (double)n * u.DefenseCavalry;
                defenseArcher += (double)n * u.DefenseArcher;
            }

            double attack = attackInfantry + attackCavalry + attackArcher;
            // Defenders meet each kind of attack with their matching defence, in proportion to the attack's make-up.
            double defense = attack > 0
                ? (defenseInfantry * attackInfantry + defenseCavalry * attackCavalry + defenseArcher * attackArcher) / attack
                : defenseInfantry;
            defense *= Buildings.WallDefenseMultiplier(result.WallAfter);
            attack *= 1 + luck;

            result.AttackStrength = attack;
            result.DefenseStrength = defense;
            if (attack > defense)
            {
                result.AttackerWon = true;
                result.AttackerLossFraction = Math.Pow(defense / attack, LossExponent);
                result.DefenderLossFraction = 1;
            }
            else
            {
                result.AttackerWon = false;
                result.AttackerLossFraction = 1;
                result.DefenderLossFraction = attack > 0 ? Math.Pow(attack / defense, LossExponent) : 0;
            }
            return result;
        }

        /// <summary>Units lost from a group given the battle's loss fractions (scouts use the scouting fight's).</summary>
        public static int[] Losses(int[] troops, double fighterFraction, double scoutFraction)
        {
            var lost = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < troops.Length; i++)
            {
                double f = (UnitType)i == UnitType.Scout ? scoutFraction : fighterFraction;
                lost[i] = Math.Min(troops[i], (int)Math.Round(troops[i] * f));
            }
            return lost;
        }

        /// <summary>How much loot these troops can carry.</summary>
        public static int CarryCapacity(int[] troops)
        {
            int carry = 0;
            for (int i = 0; i < Units.Count && i < troops.Length; i++) carry += troops[i] * Units.Get((UnitType)i).Carry;
            return carry;
        }

        /// <summary>
        /// Loot taken from a village's stocks: shared as evenly as possible across wood, clay and iron, up to what
        /// the troops can carry.
        /// </summary>
        public static Cost Loot(int capacity, double wood, double clay, double iron)
        {
            var available = new[] { (int)Math.Floor(Math.Max(0, wood)), (int)Math.Floor(Math.Max(0, clay)), (int)Math.Floor(Math.Max(0, iron)) };
            var taken = new int[3];
            int left = capacity;
            // Share what's left of the capacity between the resources that still have stock, until it's all used.
            while (left > 0)
            {
                int withStock = 0;
                for (int i = 0; i < 3; i++) if (taken[i] < available[i]) withStock++;
                if (withStock == 0) break;
                int share = Math.Max(1, left / withStock);
                for (int i = 0; i < 3 && left > 0; i++)
                {
                    int take = Math.Min(Math.Min(share, available[i] - taken[i]), left);
                    if (take <= 0) continue;
                    taken[i] += take;
                    left -= take;
                }
            }
            return new Cost(taken[0], taken[1], taken[2]);
        }

        static int Count(int[] troops, UnitType type) => troops != null && (int)type < troops.Length ? troops[(int)type] : 0;
    }
}
