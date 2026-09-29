using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>Gold coins for noblemen (an option when creating a world), as on Tribal Wars' coin worlds.</summary>
    public class CoinTests
    {
        /// <summary>A world with no rivals, the player's village rich and with an academy.</summary>
        static World AcademyWorld(bool coins, out Village v)
        {
            var world = World.CreateNew(new WorldSettings { Seed = 5, Speed = 1f, RivalDensity = 0, GoldCoins = coins });
            v = world.PlayerVillage;
            v.Levels[(int)BuildingType.Headquarters] = 20;
            v.Levels[(int)BuildingType.Academy] = 1;
            v.Levels[(int)BuildingType.Farm] = 30;
            v.Levels[(int)BuildingType.Warehouse] = 30;
            v.Wood = v.Clay = v.Iron = 1000000;
            return world;
        }

        [Test]
        public void EachSlotTakesOneMoreCoinThanTheLast()
        {
            Assert.AreEqual(new[] { 0, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4 }, Enumerable.Range(0, 11).Select(World.SlotsFor).ToArray());
            Assert.AreEqual(10, World.CoinsForSlots(4));
        }

        [Test]
        public void NoblemenCostMoreOnACoinWorld()
        {
            var flat = AcademyWorld(false, out _);
            var coins = AcademyWorld(true, out _);
            var f = flat.UnitCost(UnitType.Nobleman);
            var c = coins.UnitCost(UnitType.Nobleman);
            Assert.AreEqual((20000, 25000, 20000), (f.Wood, f.Clay, f.Iron));
            Assert.AreEqual((40000, 50000, 50000), (c.Wood, c.Clay, c.Iron));
            Assert.AreEqual(Units.Get(UnitType.Axeman).Cost.Wood, coins.UnitCost(UnitType.Axeman).Wood, "other units are the same");
        }

        [Test]
        public void EveryNoblemanNeedsAFreeSlot()
        {
            var world = AcademyWorld(true, out var v);
            var human = world.HumanPlayer;
            Assert.AreEqual(RecruitStatus.NeedsCoins, world.CheckRecruit(v, UnitType.Nobleman, 1).Status);

            Assert.AreEqual(MintStatus.Ok, world.MintCoins(v, 1).Status);
            Assert.AreEqual(1, human.Coins);
            Assert.AreEqual(1000000 - 28000, v.Wood, 1e-6);
            Assert.AreEqual(RecruitStatus.Ok, world.CheckRecruit(v, UnitType.Nobleman, 1).Status);
            Assert.AreEqual(RecruitStatus.NeedsCoins, world.CheckRecruit(v, UnitType.Nobleman, 2).Status);

            world.Recruit(v, UnitType.Nobleman, 1);
            Assert.AreEqual(1, world.NobleSlotsUsed(human), "one in training takes the slot");
            Assert.AreEqual(RecruitStatus.NeedsCoins, world.CheckRecruit(v, UnitType.Nobleman, 1).Status);
            world.MintCoins(v, 1);
            Assert.AreEqual(RecruitStatus.NeedsCoins, world.CheckRecruit(v, UnitType.Nobleman, 1).Status, "two coins still buy one slot");
            world.MintCoins(v, 1);
            Assert.AreEqual(RecruitStatus.Ok, world.CheckRecruit(v, UnitType.Nobleman, 1).Status, "three buy two");
            Assert.AreEqual(1, world.MaxAffordable(v, UnitType.Nobleman), "limited by slots, not resources");
        }

        [Test]
        public void EveryVillageBeyondTheFirstTakesASlot()
        {
            var world = AcademyWorld(true, out var v);
            var human = world.HumanPlayer;
            world.MintCoins(v, 1);
            Assert.AreEqual(0, world.NobleSlotsUsed(human));

            // A second village (as if conquered).
            var other = world.Villages.First(x => x.IsBarbarian);
            other.OwnerId = human.Id;
            world.AdvanceTo(world.Now + 1); // the owner index catches up
            Assert.AreEqual(1, world.NobleSlotsUsed(human));
            Assert.AreEqual(RecruitStatus.NeedsCoins, world.CheckRecruit(v, UnitType.Nobleman, 1).Status);
        }

        [Test]
        public void AFlatWorldHasNoCoins()
        {
            var world = AcademyWorld(false, out var v);
            Assert.AreEqual(MintStatus.NotACoinWorld, world.MintCoins(v, 1).Status);
            Assert.AreEqual(RecruitStatus.Ok, world.CheckRecruit(v, UnitType.Nobleman, 5).Status);
        }
    }
}
