using System;

namespace MedievalWorldConquest.Simulation
{
    public enum MintStatus
    {
        Ok,
        /// <summary>This world prices noblemen flat, without coins.</summary>
        NotACoinWorld,
        NoAcademy,
        InvalidCount,
        NotEnoughResources,
    }

    public struct MintCheck
    {
        public MintStatus Status;
        public int Count;
        public Cost Total;
        /// <summary>The most coins the village could mint right now.</summary>
        public int MaxAffordable;
        /// <summary>For <see cref="MintStatus.NotEnoughResources"/>: game seconds until production covers the cost.</summary>
        public double AffordableIn;
    }

    /// <summary>
    /// Gold coins, as on Tribal Wars' coin worlds (a choice made when the world is created): noblemen cost more
    /// (40,000 wood, 50,000 clay, 50,000 iron), and each needs a free noble slot. Slots come from gold coins minted
    /// at an academy (28,000 wood, 30,000 clay, 25,000 iron each), and every slot needs one more coin than the last:
    /// 1 coin for the first, 3 in all for two, 6 for three, 10 for four. Every nobleman a player has (at home, in
    /// training or on the march) takes a slot, and so does every village they hold beyond their first, so each
    /// conquest makes the next one dearer. That's what keeps any one lord from running away with the world.
    /// </summary>
    public partial class World
    {
        public static readonly Cost CoinCost = new Cost(28000, 30000, 25000);
        public static readonly Cost CoinWorldNobleCost = new Cost(40000, 50000, 50000, 100);

        /// <summary>What a unit costs in this world: noblemen are dearer where they need gold coins.</summary>
        public Cost UnitCost(UnitType unit) =>
            unit == UnitType.Nobleman && Settings.GoldCoins ? CoinWorldNobleCost : Units.Get(unit).Cost;

        /// <summary>Noble slots this many coins buy: the most n with 1 + 2 + … + n coins or fewer.</summary>
        public static int SlotsFor(int coins)
        {
            int slots = 0;
            while (CoinsForSlots(slots + 1) <= coins) slots++;
            return slots;
        }

        /// <summary>Coins needed in all for this many noble slots (1, 3, 6, 10, …).</summary>
        public static int CoinsForSlots(int slots) => slots * (slots + 1) / 2;

        /// <summary>Slots a player is using: their noblemen wherever they are, and their villages beyond the first.</summary>
        public int NobleSlotsUsed(Player p)
        {
            int noble = (int)UnitType.Nobleman;
            var own = VillagesOf(p.Id);
            int used = Math.Max(0, own.Count - 1);
            foreach (var v in own)
            {
                used += v.TroopCount(UnitType.Nobleman);
                foreach (var o in v.Recruitment)
                    if (o.Unit == UnitType.Nobleman) used += o.Remaining;
            }
            foreach (var c in CommandsOf(p.Id))
                if (c.Troops != null && noble < c.Troops.Length) used += c.Troops[noble];
            // Noblemen sent to support someone else's village (rare, but they still count).
            foreach (var v in Villages)
                foreach (var g in v.Supports)
                    if (g.OwnerId == p.Id && noble < g.Troops.Length) used += g.Troops[noble];
            return used;
        }

        /// <summary>Slots free for new noblemen (always plenty on a world without coins).</summary>
        public int FreeNobleSlots(Player p) => !Settings.GoldCoins || p == null ? int.MaxValue : Math.Max(0, SlotsFor(p.Coins) - NobleSlotsUsed(p));

        public MintCheck CheckMint(Village v, int count)
        {
            Touch(v);
            var check = new MintCheck { Count = count, Total = Multiply(CoinCost, Math.Max(0, count)) };
            check.MaxAffordable = (int)Math.Min(v.Wood / CoinCost.Wood, Math.Min(v.Clay / CoinCost.Clay, v.Iron / CoinCost.Iron));
            if (!Settings.GoldCoins) check.Status = MintStatus.NotACoinWorld;
            else if (v.Level(BuildingType.Academy) <= 0) check.Status = MintStatus.NoAcademy;
            else if (count <= 0) check.Status = MintStatus.InvalidCount;
            else if (!v.CanAfford(check.Total))
            {
                check.Status = MintStatus.NotEnoughResources;
                check.AffordableIn = Math.Max(check.Total.Wood, Math.Max(check.Total.Clay, check.Total.Iron)) > v.StorageCapacity
                    ? double.PositiveInfinity
                    : SecondsUntilAffordable(v, check.Total);
            }
            else check.Status = MintStatus.Ok;
            return check;
        }

        /// <summary>Mints gold coins at a village's academy (at once, as in Tribal Wars). They belong to its owner.</summary>
        public MintCheck MintCoins(Village v, int count)
        {
            var check = CheckMint(v, count);
            var owner = FindPlayer(v.OwnerId);
            if (check.Status != MintStatus.Ok || owner == null) return check;
            v.Wood -= check.Total.Wood;
            v.Clay -= check.Total.Clay;
            v.Iron -= check.Total.Iron;
            owner.Coins += count;
            return check;
        }
    }
}
