using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class PointsTests
    {
        [Test]
        public void LevelOneIsWorthEachBuildingsBaseValue()
        {
            Assert.AreEqual(10, Buildings.PointsOfLevel(BuildingType.TownHall, 1));
            Assert.AreEqual(6, Buildings.PointsOfLevel(BuildingType.TimberCamp, 1));
            Assert.AreEqual(5, Buildings.PointsOfLevel(BuildingType.Farm, 1));
            Assert.AreEqual(16, Buildings.PointsOfLevel(BuildingType.Barracks, 1));
            Assert.AreEqual(24, Buildings.PointsOfLevel(BuildingType.Workshop, 1));
            Assert.AreEqual(8, Buildings.PointsOfLevel(BuildingType.Wall, 1));
        }

        [Test]
        public void EachLevelIsWorthTwentyPercentMoreThanTheOneBelow()
        {
            // As in Tribal Wars: a building's points at level n are its base value × 1.2^(n-1).
            Assert.AreEqual(7, Buildings.PointsAtLevel(BuildingType.TimberCamp, 2));   // 6 × 1.2 = 7.2
            Assert.AreEqual(9, Buildings.PointsAtLevel(BuildingType.TimberCamp, 3));   // 6 × 1.44 = 8.64
            Assert.AreEqual(1978, Buildings.PointsAtLevel(BuildingType.TownHall, 30)); // 10 × 1.2^29, as in Tribal Wars
            Assert.AreEqual(2, Buildings.PointsOfLevel(BuildingType.TimberCamp, 3), "level 3 adds 9 - 7");
            Assert.AreEqual(0, Buildings.PointsOfLevel(BuildingType.TimberCamp, 0));
        }

        [Test]
        public void TheStepsAddUpToTheLevelsPoints()
        {
            foreach (var d in Buildings.Definitions)
            {
                int sum = 0;
                for (int level = 1; level <= d.MaxLevel; level++)
                {
                    sum += Buildings.PointsOfLevel(d.Type, level);
                    Assert.AreEqual(sum, Buildings.PointsAtLevel(d.Type, level), $"{d.Name} level {level}");
                }
                Assert.AreEqual(0, Buildings.PointsAtLevel(d.Type, 0));
            }
        }

        [Test]
        public void ANewVillageIsWorth39Points()
        {
            // Town Hall 10 + three mines at 6 + farm 5 + warehouse 6.
            Assert.AreEqual(39, World.CreateNew(new WorldSettings()).PlayerVillage.Points);
        }

        [Test]
        public void MilitaryBuildingsAreWorthMoreThanEconomyOnes()
        {
            Assert.Greater(Buildings.PointsAtLevel(BuildingType.Barracks, 10), Buildings.PointsAtLevel(BuildingType.TimberCamp, 10));
            Assert.Greater(Buildings.PointsAtLevel(BuildingType.Workshop, 5), Buildings.PointsAtLevel(BuildingType.Farm, 5));
        }
    }

    public class BarbarianGrowthTests
    {
        static World NewWorld(int seed = 11) => World.CreateNew(new WorldSettings { Seed = seed, Speed = 1f, RivalDensity = 0 });

        static int BarbarianPoints(World w) => w.Villages.Where(v => v.IsBarbarian).Sum(v => v.Points);

        [Test]
        public void BarbariansGrowOverTimeButThePlayerDoesNot()
        {
            var world = NewWorld();
            int before = BarbarianPoints(world);
            int playerBefore = world.PlayerVillage.Points;

            world.AdvanceTo(world.Now + 5 * World.SecondsPerDay);

            Assert.Greater(BarbarianPoints(world), before);
            Assert.AreEqual(playerBefore, world.PlayerVillage.Points, "only barbarians grow by themselves");
        }

        [Test]
        public void EveryBarbarianHasExactlyOneGrowthPending()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 3 * World.SecondsPerDay);
            int barbarians = world.Villages.Count(v => v.IsBarbarian);
            int pending = world.Events.Pending.Count(e => e.Kind == EventKind.BarbarianGrowth);
            Assert.AreEqual(barbarians, pending);
        }

        [Test]
        public void GrowthStopsAtTheCaps()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 400 * World.SecondsPerDay); // long enough to max everything out

            foreach (var v in world.Villages.Where(v => v.IsBarbarian))
                foreach (var d in Buildings.Definitions)
                {
                    int cap = World.BarbarianGrowthCap(d.Type);
                    if (cap > 0) Assert.LessOrEqual(v.Level(d.Type), cap, $"{d.Name} at {v.X}|{v.Y}");
                }
            Assert.AreEqual(0, world.Events.Pending.Count(e => e.Kind == EventKind.BarbarianGrowth), "fully grown villages stop growing");
        }

        [Test]
        public void TheSameSeedGrowsTheSameWay()
        {
            var a = NewWorld(21);
            var b = NewWorld(21);
            a.AdvanceTo(a.Now + 4 * World.SecondsPerDay);
            b.AdvanceTo(b.Now + 4 * World.SecondsPerDay);
            CollectionAssert.AreEqual(a.Villages.Select(v => v.Points), b.Villages.Select(v => v.Points));
        }

        [Test]
        public void GrowthHappensEveryFewGameHours()
        {
            var world = NewWorld();
            var barb = world.Villages.First(v => v.IsBarbarian);
            world.AdvanceTo(world.Now + World.BarbarianGrowthMaxHours * 3600);
            Assert.GreaterOrEqual(barb.GrowthSteps, 1, "at least one step within the longest wait");
            world.AdvanceTo(world.Now + 10 * World.SecondsPerDay);
            Assert.GreaterOrEqual(barb.GrowthSteps, 10);
        }

        [Test]
        public void ConqueredBarbariansStopGrowing()
        {
            var world = NewWorld();
            var taken = world.Villages.First(v => v.IsBarbarian);
            taken.OwnerId = world.HumanPlayer.Id; // as if conquered (Phase 6)
            var levels = (int[])taken.Levels.Clone();

            world.AdvanceTo(world.Now + 5 * World.SecondsPerDay);

            CollectionAssert.AreEqual(levels, taken.Levels);
        }

        [Test]
        public void OlderSavesStartTheirBarbariansGrowing()
        {
            var world = NewWorld();
            world.Events = new EventQueue(); // a version-4 save: barbarians, but no growth scheduled

            world.UpgradeFrom(4);

            Assert.AreEqual(world.Villages.Count(v => v.IsBarbarian), world.Events.Pending.Count(e => e.Kind == EventKind.BarbarianGrowth));
        }
    }
}
