using System;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class BuildingFormulaTests
    {
        [Test]
        public void LevelOneCostsMatchTheTable()
        {
            var cost = Buildings.CostOf(BuildingType.Headquarters, 1);
            Assert.AreEqual(new Cost(90, 80, 70, 5), cost);
        }

        [Test]
        public void CostsGrowEachLevel()
        {
            foreach (var d in Buildings.Definitions)
                for (int level = 2; level <= d.MaxLevel; level++)
                    Assert.Greater(Buildings.CostOf(d.Type, level).Wood, Buildings.CostOf(d.Type, level - 1).Wood, $"{d.Name} level {level}");
        }

        [Test]
        public void HeadquartersMakesBuildingFaster()
        {
            double slow = Buildings.BuildSeconds(BuildingType.Farm, 5, 1);
            double fast = Buildings.BuildSeconds(BuildingType.Farm, 5, 11);
            Assert.AreEqual(slow / Math.Pow(Buildings.HeadquartersSpeedup, 10), fast, 1e-6);
        }

        [Test]
        public void ProductionStorageAndFarmCapacity()
        {
            Assert.AreEqual(Buildings.BaseProductionPerHour, Buildings.ProductionPerHour(0));
            Assert.AreEqual(30, Buildings.ProductionPerHour(1), 1e-9);
            Assert.Greater(Buildings.ProductionPerHour(30), 2000);
            Assert.AreEqual(1000, Buildings.StorageCapacity(1));
            Assert.AreEqual(240, Buildings.FarmCapacity(1));
            Assert.Greater(Buildings.StorageCapacity(30), 300000);
            Assert.Greater(Buildings.FarmCapacity(30), 20000);
        }

        [Test]
        public void PopulationAtLevelIsTheSumOfEachLevelsStep()
        {
            // As in Tribal Wars, a building's population is base × factor^(level − 1) in all: iron mine 7 is 10 × 1.17^6.
            Assert.AreEqual(26, Buildings.PopulationAtLevel(BuildingType.IronMine, 7));
            int sum = 0;
            for (int l = 1; l <= 7; l++) sum += Buildings.PopulationOfLevel(BuildingType.IronMine, l);
            Assert.AreEqual(sum, Buildings.PopulationAtLevel(BuildingType.IronMine, 7));
            Assert.AreEqual(0, Buildings.PopulationAtLevel(BuildingType.Farm, 20)); // farms and warehouses need no workers
        }
    }

    public class EconomyTests
    {
        static World NewWorld() => World.CreateNew(new WorldSettings { Speed = 1f, Seed = 1, RivalDensity = 0 }); // no raiders

        [Test]
        public void NewVillageStartsWithLevelOneBuildingsAndResources()
        {
            var v = NewWorld().PlayerVillage;
            foreach (var d in Buildings.Definitions) Assert.AreEqual(d.StartingLevel, v.Level(d.Type), d.Name);
            Assert.AreEqual(500, v.Wood);
            Assert.AreEqual(500, v.Clay);
            Assert.AreEqual(500, v.Iron);
            Assert.LessOrEqual(v.PopulationUsed, v.PopulationCapacity);
        }

        [Test]
        public void ResourcesAreProducedPerHour()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Wood = v.Clay = v.Iron = 0;

            world.AdvanceTo(world.Now + 3600);

            Assert.AreEqual(Buildings.ProductionPerHour(1), v.Wood, 1e-6);
            Assert.AreEqual(Buildings.ProductionPerHour(1), v.Clay, 1e-6);
            Assert.AreEqual(Buildings.ProductionPerHour(1), v.Iron, 1e-6);
        }

        [Test]
        public void ProductionStopsAtWarehouseCapacity()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            world.AdvanceTo(world.Now + 1000 * 3600.0);
            Assert.AreEqual(v.StorageCapacity, v.Wood, 1e-6);
        }

        [Test]
        public void AVillageWithoutAMineStillTrickles()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Levels[(int)BuildingType.IronMine] = 0;
            v.Iron = 0;
            world.AdvanceTo(world.Now + 3600);
            Assert.AreEqual(Buildings.BaseProductionPerHour, v.Iron, 1e-6);
        }

        [Test]
        public void QueueingPaysAndCompletesAfterTheBuildTime()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            var cost = Buildings.CostOf(BuildingType.TimberCamp, 2);
            double seconds = Buildings.BuildSeconds(BuildingType.TimberCamp, 2, 1);

            var check = world.QueueBuild(v, BuildingType.TimberCamp);

            Assert.AreEqual(BuildStatus.Ok, check.Status);
            Assert.AreEqual(500 - cost.Wood, v.Wood, 1e-6);
            Assert.AreEqual(1, v.Level(BuildingType.TimberCamp));

            world.AdvanceTo(world.Now + seconds - 1);
            Assert.AreEqual(1, v.Level(BuildingType.TimberCamp), "not finished a second early");

            world.AdvanceTo(world.Now + 1);
            Assert.AreEqual(2, v.Level(BuildingType.TimberCamp));
            Assert.AreEqual(0, v.Queue.Count);
        }

        [Test]
        public void QueuedOrdersRunOneAfterAnother()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Wood = v.Clay = v.Iron = 1000;
            world.QueueBuild(v, BuildingType.ClayPit);
            world.QueueBuild(v, BuildingType.ClayPit);
            Assert.AreEqual(3, v.Queue[1].Level, "the second order builds on top of the first");
            Assert.AreEqual(4, v.NextLevel(BuildingType.ClayPit));

            double first = Buildings.BuildSeconds(BuildingType.ClayPit, 2, 1);
            double second = Buildings.BuildSeconds(BuildingType.ClayPit, 3, 1);

            world.AdvanceTo(world.Now + first);
            Assert.AreEqual(2, v.Level(BuildingType.ClayPit));
            Assert.IsTrue(v.Queue[0].Started, "the next order starts as soon as the first finishes");

            world.AdvanceTo(world.Now + second);
            Assert.AreEqual(3, v.Level(BuildingType.ClayPit));
        }

        [Test]
        public void AFullQueueRefusesMoreOrders()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Wood = v.Clay = v.Iron = 1000;
            for (int i = 0; i < World.MaxBuildQueue; i++)
                Assert.AreEqual(BuildStatus.Ok, world.QueueBuild(v, BuildingType.Warehouse).Status);
            Assert.AreEqual(BuildStatus.QueueFull, world.CheckBuild(v, BuildingType.Farm).Status);
        }

        [Test]
        public void NotEnoughResourcesSaysWhenItWillBeAffordable()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Wood = v.Clay = v.Iron = 0;

            var check = world.CheckBuild(v, BuildingType.IronMine);

            Assert.AreEqual(BuildStatus.NotEnoughResources, check.Status);
            // All mines produce 30/h at level 1, so the wait is set by the most expensive resource in the price.
            int most = Math.Max(check.Cost.Wood, Math.Max(check.Cost.Clay, check.Cost.Iron));
            Assert.AreEqual(most / Buildings.ProductionPerHour(1) * 3600, check.AffordableIn, 1e-6);

            world.AdvanceTo(world.Now + check.AffordableIn + 1);
            Assert.AreEqual(BuildStatus.Ok, world.CheckBuild(v, BuildingType.IronMine).Status);
        }

        [Test]
        public void ASmallFarmBlocksBuildingsThatNeedWorkers()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Levels[(int)BuildingType.IronMine] = 20; // takes most of a level-1 farm's 240 people
            v.Wood = v.Clay = v.Iron = 1000;
            Assume.That(v.PopulationUsed + Buildings.CostOf(BuildingType.IronMine, 21).Population, Is.GreaterThan(v.PopulationCapacity));

            Assert.AreEqual(BuildStatus.FarmTooSmall, world.CheckBuild(v, BuildingType.IronMine).Status);
            Assert.AreNotEqual(BuildStatus.FarmTooSmall, world.CheckBuild(v, BuildingType.Farm).Status, "farms need no workers");
        }

        [Test]
        public void ASmallWarehouseBlocksExpensiveUpgrades()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Levels[(int)BuildingType.Headquarters] = 15; // level 16 costs more than 1000 wood
            v.Levels[(int)BuildingType.Farm] = 30;
            Assert.AreEqual(BuildStatus.WarehouseTooSmall, world.CheckBuild(v, BuildingType.Headquarters).Status);
        }

        [Test]
        public void MaxLevelCannotBeExceeded()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Levels[(int)BuildingType.Warehouse] = Buildings.Get(BuildingType.Warehouse).MaxLevel;
            Assert.AreEqual(BuildStatus.MaxLevel, world.CheckBuild(v, BuildingType.Warehouse).Status);
        }

        [Test]
        public void BuildingsCanRequireOtherBuildings()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Wood = v.Clay = v.Iron = 1000;

            var barracks = world.CheckBuild(v, BuildingType.Barracks);
            Assert.AreEqual(BuildStatus.NeedsBuilding, barracks.Status);
            Assert.AreEqual(BuildingType.Headquarters, barracks.Required.Building);
            Assert.AreEqual(3, barracks.Required.Level);

            // The stable needs two things; it reports whichever is still missing.
            v.Levels[(int)BuildingType.Headquarters] = 10;
            var stable = world.CheckBuild(v, BuildingType.Stable);
            Assert.AreEqual(BuildStatus.NeedsBuilding, stable.Status);
            Assert.AreEqual(BuildingType.Barracks, stable.Required.Building);
            Assert.AreEqual(5, stable.Required.Level);

            // ...and, as in Tribal Wars, a smithy at 5.
            v.Levels[(int)BuildingType.Barracks] = 5;
            v.Levels[(int)BuildingType.Farm] = 10;
            Assert.AreEqual(BuildingType.Smithy, world.CheckBuild(v, BuildingType.Stable).Required.Building);
            v.Levels[(int)BuildingType.Smithy] = 5;
            Assert.AreEqual(BuildStatus.Ok, world.CheckBuild(v, BuildingType.Stable).Status);
        }

        [Test]
        public void ALockedBuildingSaysWhatItNeedsEvenWhenTheQueueIsFull()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Wood = v.Clay = v.Iron = 1000;
            for (int i = 0; i < World.MaxBuildQueue; i++) world.QueueBuild(v, BuildingType.Warehouse);
            Assert.AreEqual(BuildStatus.NeedsBuilding, world.CheckBuild(v, BuildingType.Barracks).Status);
        }

        [Test]
        public void CancelingTheLastOrderRefundsIt()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            world.QueueBuild(v, BuildingType.Farm);

            Assert.IsTrue(world.CancelLastBuild(v));

            Assert.AreEqual(500, v.Wood, 1e-6);
            Assert.AreEqual(500, v.Clay, 1e-6);
            Assert.AreEqual(500, v.Iron, 1e-6);
            Assert.AreEqual(0, v.Queue.Count);
            Assert.IsFalse(world.CancelLastBuild(v), "nothing left to cancel");
        }

        [Test]
        public void ACanceledOrdersTimerIsIgnored()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            world.QueueBuild(v, BuildingType.Farm);
            world.CancelLastBuild(v);
            world.QueueBuild(v, BuildingType.Warehouse); // starts later, with its own timer

            world.AdvanceTo(world.Now + Buildings.BuildSeconds(BuildingType.Farm, 2, 1) + 1);

            Assert.AreEqual(1, v.Level(BuildingType.Farm), "the canceled farm never completes");
        }

        [Test]
        public void CatchingUpRunsTheWholeQueue()
        {
            // Simulates coming back after a long time away: everything queued finishes, in order.
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Wood = v.Clay = v.Iron = 1000;
            world.QueueBuild(v, BuildingType.Headquarters);
            world.QueueBuild(v, BuildingType.TimberCamp);
            world.QueueBuild(v, BuildingType.TimberCamp);

            world.AdvanceByRealSeconds(24 * 3600);

            Assert.AreEqual(2, v.Level(BuildingType.Headquarters));
            Assert.AreEqual(3, v.Level(BuildingType.TimberCamp));
            Assert.AreEqual(0, v.Queue.Count);
        }

        [Test]
        public void SaveAndLoadKeepTheEconomyAndTheQueueStillFinishes()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            world.QueueBuild(v, BuildingType.TimberCamp);
            world.QueueBuild(v, BuildingType.Farm);
            world.AdvanceTo(world.Now + 10); // part-way through the first order

            var loaded = SaveGame.FromJson(SaveGame.ToJson(world, DateTime.UtcNow), out _);
            var lv = loaded.PlayerVillage;

            Assert.AreEqual(v.Wood, lv.Wood, 1e-9);
            CollectionAssert.AreEqual(v.Levels, lv.Levels);
            Assert.AreEqual(2, lv.Queue.Count);
            Assert.AreEqual(v.Queue[0].FinishTime, lv.Queue[0].FinishTime, 1e-9);

            loaded.AdvanceTo(loaded.Now + 3600);
            Assert.AreEqual(2, lv.Level(BuildingType.TimberCamp));
            Assert.AreEqual(2, lv.Level(BuildingType.Farm));
        }

        [Test]
        public void PhaseZeroWorldsAreUpgradedWithAStartingVillage()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Levels = new int[0]; // what a Phase 0 save loads as
            v.Wood = v.Clay = v.Iron = 0;

            world.UpgradeFrom(1);

            Assert.AreEqual(Buildings.Count, v.Levels.Length);
            Assert.AreEqual(1, v.Level(BuildingType.Headquarters));
            Assert.AreEqual(500, v.Wood);
            Assert.AreEqual(World.CurrentVersion, world.Version);
        }
    }
}
