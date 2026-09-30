using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>Phase 7: the smithy's research, the hiding place and the market.</summary>
    public class DepthTests
    {
        /// <summary>A world with no rivals (every settler a barbarian village), the player's village well stocked.</summary>
        static World QuietWorld(out Village home)
        {
            var world = World.CreateNew(new WorldSettings { Seed = 11, Speed = 1f, RivalDensity = 0 });
            home = world.PlayerVillage;
            home.Levels[(int)BuildingType.Headquarters] = 10;
            home.Levels[(int)BuildingType.Barracks] = 5;
            home.Levels[(int)BuildingType.Farm] = 20;
            home.Levels[(int)BuildingType.Warehouse] = 20;
            home.Wood = home.Clay = home.Iron = 20000;
            return world;
        }

        static Village NearestBarbarian(World world, Village home) =>
            world.Villages.Where(v => v.IsBarbarian).OrderBy(v => World.Distance(home, v)).First();

        // ---------------------------------------------------------------- research

        [Test]
        public void SpearmenNeedNoResearchButSwordsmenDo()
        {
            var world = QuietWorld(out var v);
            Assert.AreEqual(RecruitStatus.Ok, world.CheckRecruit(v, UnitType.Spearman, 1).Status);
            Assert.AreEqual(RecruitStatus.NeedsResearch, world.CheckRecruit(v, UnitType.Swordsman, 1).Status);
            Assert.AreEqual(ResearchStatus.NotNeeded, world.CheckResearch(v, UnitType.Spearman).Status);
            Assert.AreEqual(ResearchStatus.NotNeeded, world.CheckResearch(v, UnitType.Nobleman).Status);

            var noSmithy = world.CheckResearch(v, UnitType.Swordsman);
            Assert.AreEqual(ResearchStatus.NeedsBuilding, noSmithy.Status);
            Assert.AreEqual(BuildingType.Smithy, noSmithy.Required.Building);
        }

        [Test]
        public void ResearchTakesTimeAndThenTheUnitCanBeTrained()
        {
            var world = QuietWorld(out var v);
            v.Levels[(int)BuildingType.Smithy] = 1;
            var check = world.StartResearch(v, UnitType.Swordsman);
            Assert.AreEqual(ResearchStatus.Ok, check.Status);
            Assert.AreEqual(20000 - 900, v.Wood, 1e-6, "paid up front");
            Assert.AreEqual(ResearchStatus.InProgress, world.CheckResearch(v, UnitType.Swordsman).Status);

            world.AdvanceTo(world.Now + check.Seconds - 1);
            Assert.IsFalse(v.IsResearched(UnitType.Swordsman));
            world.AdvanceTo(world.Now + 1);
            Assert.IsTrue(v.IsResearched(UnitType.Swordsman));
            Assert.AreEqual(RecruitStatus.Ok, world.CheckRecruit(v, UnitType.Swordsman, 1).Status);
        }

        [Test]
        public void ResearchNeedsItsBuildingsAtTheirLevels()
        {
            var world = QuietWorld(out var v);
            v.Levels[(int)BuildingType.Smithy] = 1;
            var axe = world.CheckResearch(v, UnitType.Axeman);
            Assert.AreEqual(ResearchStatus.NeedsBuilding, axe.Status, "axemen need a smithy at 2, as in Tribal Wars");
            Assert.AreEqual(2, axe.Required.Level);
            v.Levels[(int)BuildingType.Smithy] = 2;
            Assert.AreEqual(ResearchStatus.Ok, world.CheckResearch(v, UnitType.Axeman).Status);
            var heavy = world.CheckResearch(v, UnitType.HeavyCavalry);
            Assert.AreEqual(BuildingType.Stable, heavy.Required.Building);
        }

        [Test]
        public void CancelingResearchRefundsItAndStartsTheNext()
        {
            var world = QuietWorld(out var v);
            v.Levels[(int)BuildingType.Smithy] = 2;
            world.StartResearch(v, UnitType.Swordsman);
            world.StartResearch(v, UnitType.Axeman);
            Assert.AreEqual(2, v.Researching.Count);
            double wood = v.Wood;
            Assert.IsTrue(world.CancelResearch(v, v.Researching[0].Id));
            Assert.AreEqual(wood + 900, v.Wood, 1e-6);
            Assert.IsTrue(v.Researching[0].Started, "the axemen move up and start");
            world.AdvanceTo(v.Researching[0].FinishTime);
            Assert.IsTrue(v.IsResearched(UnitType.Axeman));
            Assert.IsFalse(v.IsResearched(UnitType.Swordsman));
        }

        [Test]
        public void OlderSavesKeepTheUnitsTheyCouldAlreadyTrain()
        {
            var world = QuietWorld(out var v);
            v.Levels[(int)BuildingType.Stable] = 3;
            v.Research = new int[0]; // as loaded from a version-13 save
            world.UpgradeFrom(13);
            Assert.IsTrue(v.IsResearched(UnitType.Archer), "barracks 5");
            Assert.IsTrue(v.IsResearched(UnitType.LightCavalry), "stable 3");
            Assert.IsFalse(v.IsResearched(UnitType.HeavyCavalry), "stable 10 not reached");
        }

        // ---------------------------------------------------------------- hiding place

        [Test]
        public void TheHidingPlaceKeepsItsShareFromRaiders()
        {
            Assert.AreEqual(150, Buildings.HiddenCapacity(1));
            Assert.AreEqual(2000, Buildings.HiddenCapacity(10));

            var world = QuietWorld(out var home);
            home.Troops[(int)UnitType.Axeman] = 1000; // carries 10,000
            var target = NearestBarbarian(world, home);
            world.Touch(target);
            target.Levels[(int)BuildingType.Wall] = 0;
            target.Levels[(int)BuildingType.HidingPlace] = 10;
            target.Levels[(int)BuildingType.Warehouse] = 20;
            var attack = world.Send(home, target, Enumerable.Range(0, Units.Count).Select(i => i == (int)UnitType.Axeman ? 1000 : 0).ToArray(), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime - 1);
            target.Wood = target.Clay = target.Iron = 2500;
            target.StockTime = world.Now;
            world.AdvanceTo(attack.ArriveTime);

            var report = world.Reports.Last(r => r.Kind == ReportKind.Attack);
            int hidden = Buildings.HiddenCapacity(10);
            Assert.AreEqual(2500 - hidden, report.Loot.Wood, 2);
            Assert.AreEqual(2500 - hidden, report.Loot.Iron, 2);
        }

        // ---------------------------------------------------------------- market

        [Test]
        public void MerchantsFollowTribalWarsTable()
        {
            Assert.AreEqual(1, Buildings.Merchants(1));
            Assert.AreEqual(10, Buildings.Merchants(10));
            Assert.AreEqual(14, Buildings.Merchants(12));
            Assert.AreEqual(235, Buildings.Merchants(25));
        }

        [Test]
        public void MerchantsCarryResourcesThereAndComeBack()
        {
            var world = QuietWorld(out var home);
            var target = NearestBarbarian(world, home);
            Assert.AreEqual(TradeStatus.NoMarket, world.SendResources(home, target, new Cost(100, 0, 0)));
            home.Levels[(int)BuildingType.Market] = 3; // 3 merchants

            Assert.AreEqual(TradeStatus.NotEnoughMerchants, world.SendResources(home, target, new Cost(2000, 1000, 1)));
            world.Touch(target);
            double before = target.Wood;
            Assert.AreEqual(TradeStatus.Ok, world.SendResources(home, target, new Cost(1500, 0, 0)));
            Assert.AreEqual(20000 - 1500, home.Wood, 1e-6);
            Assert.AreEqual(1, world.MerchantsFree(home), "two merchants on the road");

            double trip = World.MerchantSeconds(home, target);
            world.AdvanceTo(world.Now + trip);
            Assert.GreaterOrEqual(target.Wood, System.Math.Min(target.StorageCapacity, before + 1500) - 1);
            Assert.AreEqual(1, world.MerchantsFree(home), "still walking home");
            world.AdvanceTo(world.Now + trip);
            Assert.AreEqual(3, world.MerchantsFree(home));
            Assert.IsTrue(world.Reports.Any(r => r.Kind == ReportKind.ResourcesArrived));
        }

        [Test]
        public void AnOfferSetsItsGoodsAsideUntilItIsTaken()
        {
            var world = QuietWorld(out var home);
            home.Levels[(int)BuildingType.Market] = 10;
            Assert.AreEqual(TradeStatus.UnfairRatio, world.CheckOffer(home, ResourceType.Wood, 1000, ResourceType.Clay, 3000, 1));

            var offer = world.PostOffer(home, ResourceType.Wood, 1000, ResourceType.Clay, 1000, 3);
            Assert.IsNotNull(offer);
            Assert.AreEqual(20000 - 3000, home.Wood, 1e-6);
            Assert.AreEqual(7, world.MerchantsFree(home), "three merchants set aside");

            // Another village (a barbarian one, made a lord's for the test) takes two lots.
            var buyer = NearestBarbarian(world, home);
            buyer.OwnerId = 99;
            buyer.Levels[(int)BuildingType.Market] = 5;
            buyer.Levels[(int)BuildingType.Warehouse] = 20;
            world.Touch(buyer);
            buyer.Clay = 5000;
            buyer.Wood = 0;
            Assert.AreEqual(TradeStatus.Ok, world.AcceptOffer(buyer, offer.Id, 2));
            Assert.AreEqual(1, offer.Lots);
            Assert.AreEqual(3000, buyer.Clay, 1e-6);

            world.AdvanceTo(world.Now + World.MerchantSeconds(home, buyer));
            Assert.GreaterOrEqual(buyer.Wood, 2000 - 1e-6, "the wood arrived");
            Assert.GreaterOrEqual(home.Clay, 20000 + 2000 - 1e-6, "and the clay");

            // The last lot is withdrawn: its wood comes back.
            double wood = home.Wood;
            Assert.IsTrue(world.WithdrawOffer(offer.Id));
            Assert.AreEqual(wood + 1000, home.Wood, 1e-6);
            Assert.AreEqual(0, world.Offers.Count);
        }

        [Test]
        public void OffersRunOutAfterADay()
        {
            var world = QuietWorld(out var home);
            home.Levels[(int)BuildingType.Market] = 5;
            world.PostOffer(home, ResourceType.Iron, 1000, ResourceType.Wood, 1000, 2);
            Assert.AreEqual(20000 - 2000, home.Iron, 1e-6);
            world.AdvanceTo(world.Now + World.OfferHours * 3600 - 1);
            Assert.AreEqual(1, world.Offers.Count);
            double iron = home.Iron;
            world.AdvanceTo(world.Now + 1);
            Assert.AreEqual(0, world.Offers.Count);
            Assert.AreEqual(iron + 2000, home.Iron, 1, "the iron comes back");
        }

        [Test]
        public void LordsResearchAndTradeOnTheirOwn()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 1234, Speed = 1f, RivalDensity = 1 });
            world.AdvanceTo(world.Now + 20 * World.SecondsPerDay);
            var lords = world.Players.Where(p => !p.IsHuman && p.Personality != AiPersonality.Noob && p.Personality != AiPersonality.Inactive).ToList();
            Assert.IsTrue(lords.Any(p => world.VillagesOf(p.Id).Any(v => v.IsResearched(UnitType.Axeman))), "someone has researched axemen");
            Assert.IsTrue(world.Villages.Any(v => !v.IsBarbarian && v.Level(BuildingType.Market) > 0), "someone has a market");
        }
    }
}
