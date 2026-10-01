using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>A one-time hint, shown the first time its moment comes.</summary>
    public class Tip
    {
        public int Id;
        internal Func<World, bool> When;
        internal Func<World, string> Text;

        public string TextIn(World world) => Text(world);
    }

    /// <summary>
    /// Tips: a hint the first time something new happens in a world (an attack coming, beginner protection running
    /// out, a full warehouse or farm, the first report…), each shown once per world. Whether they're shown at all
    /// is the player's choice (in the menu); the quests are always there.
    /// </summary>
    public partial class World
    {
        /// <summary>The tips already shown in this world.</summary>
        public List<int> TipsShown = new List<int>();

        static bool AnyHumanVillage(World w, Func<Village, bool> test)
        {
            var human = w.HumanPlayer;
            if (human == null) return false;
            foreach (var v in w.VillagesOf(human.Id)) if (test(v)) return true;
            return false;
        }

        public static readonly Tip[] AllTips =
        {
            new Tip
            {
                Id = 1,
                When = w => w.HumanPlayer != null && w.IncomingAttacks(w.HumanPlayer.Id).Count > 0,
                Text = w => "An attack is coming! You can't see what's in it, only how fast it marches: the icon shows its slowest unit. " +
                            "Scouts and cavalry raids are quick; rams and noblemen are slow, and dangerous. Keep defenders at home, or send support from another village.",
            },
            new Tip
            {
                Id = 2,
                When = w => w.HumanPlayer != null && w.IsProtected(w.HumanPlayer.Id) && w.HumanPlayer.ProtectedUntil - w.Now < 12 * 3600,
                Text = w => "Your beginner protection is about to end. After that anyone can attack you: build a wall, and keep some defenders at home.",
            },
            new Tip
            {
                Id = 3,
                When = w => w.Reports.Count > 0,
                Text = w => "Your first report has arrived. Open Reports in the top row to see how it went: what fought, what fell, and what was carried home.",
            },
            new Tip
            {
                Id = 4,
                When = w => AnyHumanVillage(w, v => { double cap = v.StorageCapacity; return v.Stock(ResourceType.Wood) >= cap || v.Stock(ResourceType.Clay) >= cap || v.Stock(ResourceType.Iron) >= cap; }),
                Text = w => "Your warehouse is full: what your village produces now goes to waste. Spend it, or upgrade the warehouse.",
            },
            new Tip
            {
                Id = 5,
                When = w => AnyHumanVillage(w, v => v.PopulationUsed >= v.PopulationCapacity),
                Text = w => "Your farm is full: nothing more can be built or trained until you upgrade it.",
            },
            new Tip
            {
                Id = 6,
                When = w => w.Diplomacy && w.HumanPlayer?.TribeId < 0 && DayOf(w.Now) >= 5,
                Text = w => "Lords band together into tribes that defend each other and win together. Open Tribe in the top row to find one near you, or found your own.",
            },
            new Tip
            {
                Id = 7,
                When = w => w.Settings.GoldCoins && w.HumanPlayer != null && w.HumanPlayer.Coins == 0 && AnyHumanVillage(w, v => v.Level(BuildingType.Academy) > 0),
                Text = w => "Your academy is built. On this world noblemen need noble slots, bought with gold coins: mint them at the academy.",
            },
            new Tip
            {
                Id = 8,
                When = w => AnyHumanVillage(w, v => v.Loyalty < MaxLoyalty),
                Text = w => "Someone's noblemen have reached one of your villages: its loyalty has dropped. It recovers a point an hour, " +
                            "but a few more noblemen and the village is theirs. Send support, and strike back at the village they came from.",
            },
        };

        /// <summary>The next tip whose moment has come and that hasn't been shown in this world, or null.</summary>
        public Tip NextTip()
        {
            if (HumanPlayer == null || PlayerVillage == null) return null;
            if (TipsShown == null) TipsShown = new List<int>();
            foreach (var tip in AllTips)
                if (!TipsShown.Contains(tip.Id) && tip.When(this)) return tip;
            return null;
        }

        public void MarkTipShown(Tip tip)
        {
            if (tip != null && !TipsShown.Contains(tip.Id)) TipsShown.Add(tip.Id);
        }
    }
}
