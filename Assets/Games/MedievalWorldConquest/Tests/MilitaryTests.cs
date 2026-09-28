using System;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class UnitDefinitionTests
    {
        [Test]
        public void EveryUnitIsTrainedSomewhereAndCostsSomething()
        {
            foreach (var u in Units.Definitions)
            {
                Assert.Greater(Buildings.Get(u.Building).TrainingSpeedup, 1, $"{u.Name}'s building should train faster with levels");
                Assert.Greater(u.Cost.Wood + u.Cost.Clay + u.Cost.Iron, 0, u.Name);
                Assert.Greater(u.Cost.Population, 0, u.Name);
                Assert.Greater(u.BaseSeconds, 0, u.Name);
                Assert.Greater(u.MinutesPerField, 0, u.Name);
            }
        }

        [Test]
        public void TrainedAtListsEachBuildingsUnits()
        {
            CollectionAssert.AreEqual(
                new[] { UnitType.Spearman, UnitType.Swordsman, UnitType.Axeman, UnitType.Archer },
                Array.ConvertAll(Units.TrainedAt(BuildingType.Barracks), u => u.Type));
            Assert.AreEqual(3, Units.TrainedAt(BuildingType.Stable).Length);
            Assert.AreEqual(2, Units.TrainedAt(BuildingType.Workshop).Length);
        }

        [Test]
        public void HigherBuildingLevelsTrainFaster()
        {
            double level1 = Units.SecondsToTrain(UnitType.Spearman, 1);
            double level11 = Units.SecondsToTrain(UnitType.Spearman, 11);
            Assert.AreEqual(Units.Get(UnitType.Spearman).BaseSeconds, level1, 1e-9);
            Assert.AreEqual(level1 / Math.Pow(1.06, 10), level11, 1e-6);
        }
    }

    public class RecruitmentTests
    {
        /// <summary>A world whose village has a level-5 barracks, a big farm and plenty of resources.</summary>
        static World ArmedWorld(out Village v)
        {
            var world = World.CreateNew(new WorldSettings { Speed = 1f, Seed = 3 });
            v = world.PlayerVillage;
            v.Levels[(int)BuildingType.TownHall] = 10;
            v.Levels[(int)BuildingType.Barracks] = 5;
            v.Levels[(int)BuildingType.Farm] = 20;
            v.Levels[(int)BuildingType.Warehouse] = 20;
            v.Wood = v.Clay = v.Iron = 50000;
            return world;
        }

        [Test]
        public void UnitsNeedTheirBuildingAtTheRightLevel()
        {
            var world = World.CreateNew(new WorldSettings());
            var v = world.PlayerVillage;

            var noBarracks = world.CheckRecruit(v, UnitType.Spearman, 1);
            Assert.AreEqual(RecruitStatus.NeedsBuilding, noBarracks.Status);
            Assert.AreEqual(BuildingType.Barracks, noBarracks.Required.Building);
            Assert.AreEqual(1, noBarracks.Required.Level);

            v.Levels[(int)BuildingType.Barracks] = 1;
            Assert.AreEqual(RecruitStatus.Ok, world.CheckRecruit(v, UnitType.Spearman, 1).Status);
            var archer = world.CheckRecruit(v, UnitType.Archer, 1);
            Assert.AreEqual(RecruitStatus.NeedsBuilding, archer.Status);
            Assert.AreEqual(5, archer.Required.Level);
        }

        [Test]
        public void RecruitingPaysUpFrontAndReservesPopulation()
        {
            var world = ArmedWorld(out var v);
            int popBefore = v.PopulationUsed;

            var check = world.Recruit(v, UnitType.Swordsman, 10);

            Assert.AreEqual(RecruitStatus.Ok, check.Status);
            Assert.AreEqual(50000 - 300, v.Wood, 1e-6);
            Assert.AreEqual(50000 - 700, v.Iron, 1e-6);
            Assert.AreEqual(popBefore + 10, v.PopulationUsed, "units in training already count against the farm");
            Assert.AreEqual(0, v.TroopCount(UnitType.Swordsman));
        }

        [Test]
        public void UnitsFinishOneAtATime()
        {
            var world = ArmedWorld(out var v);
            world.Recruit(v, UnitType.Spearman, 3);
            double each = Units.SecondsToTrain(UnitType.Spearman, 5);

            world.AdvanceTo(world.Now + each - 1);
            Assert.AreEqual(0, v.TroopCount(UnitType.Spearman));
            world.AdvanceTo(world.Now + 1);
            Assert.AreEqual(1, v.TroopCount(UnitType.Spearman));
            world.AdvanceTo(world.Now + each);
            Assert.AreEqual(2, v.TroopCount(UnitType.Spearman));
            world.AdvanceTo(world.Now + each);
            Assert.AreEqual(3, v.TroopCount(UnitType.Spearman));
            Assert.AreEqual(0, v.Recruitment.Count, "a finished batch leaves the queue");
        }

        [Test]
        public void BatchesInTheSameBuildingRunInTurn()
        {
            var world = ArmedWorld(out var v);
            world.Recruit(v, UnitType.Spearman, 2);
            world.Recruit(v, UnitType.Axeman, 2);
            double spear = Units.SecondsToTrain(UnitType.Spearman, 5);
            double axe = Units.SecondsToTrain(UnitType.Axeman, 5);

            world.AdvanceTo(world.Now + spear * 2);
            Assert.AreEqual(2, v.TroopCount(UnitType.Spearman));
            Assert.AreEqual(0, v.TroopCount(UnitType.Axeman), "axemen wait for the spearmen");

            world.AdvanceTo(world.Now + axe * 2);
            Assert.AreEqual(2, v.TroopCount(UnitType.Axeman));
        }

        [Test]
        public void DifferentBuildingsTrainAtTheSameTime()
        {
            var world = ArmedWorld(out var v);
            v.Levels[(int)BuildingType.Stable] = 3;
            world.Recruit(v, UnitType.Spearman, 1);
            world.Recruit(v, UnitType.LightCavalry, 1);

            double longest = Math.Max(Units.SecondsToTrain(UnitType.Spearman, 5), Units.SecondsToTrain(UnitType.LightCavalry, 3));
            world.AdvanceTo(world.Now + longest);

            Assert.AreEqual(1, v.TroopCount(UnitType.Spearman));
            Assert.AreEqual(1, v.TroopCount(UnitType.LightCavalry));
        }

        [Test]
        public void MaxAffordableIsLimitedByResourcesAndPopulation()
        {
            var world = ArmedWorld(out var v);
            v.Wood = 500; // 10 spearmen's worth of wood
            Assert.AreEqual(10, World.MaxAffordable(v, UnitType.Spearman));

            v.Wood = 50000;
            v.Levels[(int)BuildingType.Farm] = 1; // room for only a few dozen more people
            Assert.Less(v.FreePopulation, 1000);
            Assert.AreEqual(v.FreePopulation, World.MaxAffordable(v, UnitType.Spearman), "with plenty of resources, the farm is the limit");
        }

        [Test]
        public void RecruitingMoreThanTheFarmHoldsIsRefused()
        {
            var world = ArmedWorld(out var v);
            var check = world.CheckRecruit(v, UnitType.Spearman, v.FreePopulation + 1);
            Assert.AreEqual(RecruitStatus.FarmTooSmall, check.Status);
        }

        [Test]
        public void RecruitingMoreThanYouCanAffordSaysWhen()
        {
            var world = ArmedWorld(out var v);
            v.Wood = v.Clay = v.Iron = 0;
            var check = world.CheckRecruit(v, UnitType.Spearman, 2);
            Assert.AreEqual(RecruitStatus.NotEnoughResources, check.Status);
            Assert.AreEqual(100 / Buildings.ProductionPerHour(1) * 3600, check.AffordableIn, 1e-6); // 100 wood at 30/h
        }

        [Test]
        public void ZeroOrNegativeCountsAreRejected()
        {
            var world = ArmedWorld(out var v);
            Assert.AreEqual(RecruitStatus.InvalidCount, world.CheckRecruit(v, UnitType.Spearman, 0).Status);
            Assert.AreEqual(RecruitStatus.InvalidCount, world.Recruit(v, UnitType.Spearman, -5).Status);
            Assert.AreEqual(50000, v.Wood, 1e-6, "nothing was charged");
        }

        [Test]
        public void EachBuildingHasALimitedQueue()
        {
            var world = ArmedWorld(out var v);
            for (int i = 0; i < World.MaxRecruitQueue; i++)
                Assert.AreEqual(RecruitStatus.Ok, world.Recruit(v, UnitType.Spearman, 1).Status);
            Assert.AreEqual(RecruitStatus.QueueFull, world.CheckRecruit(v, UnitType.Swordsman, 1).Status);
        }

        [Test]
        public void CancellingRefundsUntrainedUnitsAndStartsTheNextBatch()
        {
            var world = ArmedWorld(out var v);
            world.Recruit(v, UnitType.Spearman, 4);
            world.Recruit(v, UnitType.Axeman, 1);
            double spear = Units.SecondsToTrain(UnitType.Spearman, 5);
            world.AdvanceTo(world.Now + spear); // one spearman done, three to go
            double woodBefore = v.Wood;
            int spearOrder = v.Recruitment[0].Id;

            Assert.IsTrue(world.CancelRecruit(v, spearOrder));

            Assert.AreEqual(1, v.TroopCount(UnitType.Spearman), "the trained spearman stays");
            Assert.AreEqual(woodBefore + 3 * 50, v.Wood, 1e-6, "three untrained spearmen refunded");
            Assert.IsTrue(v.Recruitment[0].Started, "the axemen start straight away");

            // The cancelled batch's pending event must not train a spearman.
            world.AdvanceTo(world.Now + Units.SecondsToTrain(UnitType.Axeman, 5));
            Assert.AreEqual(1, v.TroopCount(UnitType.Spearman));
            Assert.AreEqual(1, v.TroopCount(UnitType.Axeman));
        }

        [Test]
        public void CancellingAWaitingBatchLeavesTheActiveOneAlone()
        {
            var world = ArmedWorld(out var v);
            world.Recruit(v, UnitType.Spearman, 2);
            world.Recruit(v, UnitType.Axeman, 2);
            double activeNext = v.Recruitment[0].NextAt;

            world.CancelRecruit(v, v.Recruitment[1].Id);

            Assert.AreEqual(1, v.Recruitment.Count);
            Assert.AreEqual(activeNext, v.Recruitment[0].NextAt);
        }

        [Test]
        public void TroopsCountAgainstTheFarmForBuildings()
        {
            var world = ArmedWorld(out var v);
            v.Levels[(int)BuildingType.Farm] = 1;
            v.Troops[(int)UnitType.Spearman] = v.PopulationCapacity; // the farm is full of soldiers
            Assert.AreEqual(BuildStatus.FarmTooSmall, world.CheckBuild(v, BuildingType.IronMine).Status);
        }

        [Test]
        public void CatchingUpTrainsEverything()
        {
            var world = ArmedWorld(out var v);
            world.Recruit(v, UnitType.Spearman, 20);
            world.Recruit(v, UnitType.Swordsman, 5);
            world.AdvanceByRealSeconds(7 * 24 * 3600);
            Assert.AreEqual(20, v.TroopCount(UnitType.Spearman));
            Assert.AreEqual(5, v.TroopCount(UnitType.Swordsman));
            Assert.AreEqual(0, v.Recruitment.Count);
        }

        [Test]
        public void OlderSavesGetEmptyArmiesAndNewBuildingsAtLevelZero()
        {
            var world = World.CreateNew(new WorldSettings());
            var v = world.PlayerVillage;
            v.Levels = new int[6]; // a version-2 village: six buildings
            v.Levels[(int)BuildingType.TownHall] = 4;
            v.Troops = null;
            v.Recruitment = null;

            world.UpgradeFrom(2);

            Assert.AreEqual(Buildings.Count, v.Levels.Length);
            Assert.AreEqual(4, v.Level(BuildingType.TownHall), "existing levels are kept");
            Assert.AreEqual(0, v.Level(BuildingType.Barracks));
            Assert.AreEqual(Units.Count, v.Troops.Length);
            Assert.IsNotNull(v.Recruitment);
        }

        [Test]
        public void SaveAndLoadKeepTroopsAndTraining()
        {
            var world = ArmedWorld(out var v);
            v.Troops[(int)UnitType.Axeman] = 12;
            world.Recruit(v, UnitType.Spearman, 3);

            var loaded = SaveGame.FromJson(SaveGame.ToJson(world, DateTime.UtcNow), out _);
            var lv = loaded.PlayerVillage;

            Assert.AreEqual(12, lv.TroopCount(UnitType.Axeman));
            Assert.AreEqual(1, lv.Recruitment.Count);
            loaded.AdvanceTo(loaded.Now + 3 * Units.SecondsToTrain(UnitType.Spearman, 5));
            Assert.AreEqual(3, lv.TroopCount(UnitType.Spearman));
        }
    }
}
