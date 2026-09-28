using System;
using System.Collections.Generic;
using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class TerrainTests
    {
        [Test]
        public void TheSameSeedGivesTheSameLandscape()
        {
            for (int x = 0; x < World.MapSize; x += 7)
                for (int y = 0; y < World.MapSize; y += 7)
                    Assert.AreEqual(Terrain.At(123, x, y), Terrain.At(123, x, y));
        }

        [Test]
        public void DifferentSeedsGiveDifferentLandscapes()
        {
            int differences = 0;
            for (int x = 0; x < World.MapSize; x++)
                for (int y = 0; y < World.MapSize; y++)
                    if (Terrain.At(1, x, y) != Terrain.At(2, x, y)) differences++;
            Assert.Greater(differences, 500);
        }

        [Test]
        public void TheMapHasSomeOfEachTerrainButIsMostlyLand()
        {
            var counts = new Dictionary<TerrainType, int>();
            foreach (TerrainType t in Enum.GetValues(typeof(TerrainType))) counts[t] = 0;
            for (int x = 0; x < World.MapSize; x++)
                for (int y = 0; y < World.MapSize; y++)
                    counts[Terrain.At(42, x, y)]++;

            int total = World.MapSize * World.MapSize;
            foreach (var pair in counts) Assert.Greater(pair.Value, 0, $"no {pair.Key} at all");
            Assert.Less(counts[TerrainType.Water], total * 0.3, "too much water");
        }

        [Test]
        public void ThePlayersStartIsAlwaysOpenGrass()
        {
            for (int seed = 0; seed < 50; seed++)
                Assert.AreEqual(TerrainType.Grass, Terrain.At(seed, World.MapSize / 2, World.MapSize / 2), $"seed {seed}");
        }

        [Test]
        public void OffTheMapIsWater()
        {
            Assert.AreEqual(TerrainType.Water, Terrain.At(1, -1, 50));
            Assert.AreEqual(TerrainType.Water, Terrain.At(1, 50, World.MapSize));
        }
    }

    public class WorldMapTests
    {
        static World NewWorld(int seed = 99) => World.CreateNew(new WorldSettings { Seed = seed, Speed = 1f, RivalDensity = 0 });

        static double FromCentre(Village v) =>
            Math.Sqrt((v.X - World.MapSize / 2.0) * (v.X - World.MapSize / 2.0) + (v.Y - World.MapSize / 2.0) * (v.Y - World.MapSize / 2.0));

        [Test]
        public void ANewWorldIsASmallSettledCircleRoundThePlayer()
        {
            var world = NewWorld();
            Assert.AreEqual(World.MapSize / 2, world.PlayerVillage.X);
            Assert.Greater(world.Villages.Count(v => v.IsBarbarian), 5);
            foreach (var v in world.Villages) Assert.LessOrEqual(FromCentre(v), World.StartRadius + 1);
            Assert.AreEqual(World.StartRadius, world.SpawnRadius, 1e-9);
        }

        [Test]
        public void TheWorldSpreadsOutwardAsTheDaysGoBy()
        {
            var world = NewWorld();
            int before = world.Villages.Count;
            world.AdvanceTo(world.Now + 10 * World.SecondsPerDay);

            Assert.AreEqual(World.StartRadius + 10 * World.RadiusPerDay, world.SpawnRadius, 0.01);
            Assert.Greater(world.Villages.Count, before * 3, "the ring's growth brings new villages");
            // The newest villages are round the edge of the circle, give or take the spread.
            foreach (var v in world.Villages.Skip(world.Villages.Count - 10))
                Assert.That(FromCentre(v), Is.InRange(world.SpawnRadius - World.RingSpread - 3, world.SpawnRadius + World.RingSpread + 1));
        }

        [Test]
        public void VillagesAreOnLandInsideTheMapOnePerField()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 20 * World.SecondsPerDay);
            var positions = new HashSet<(int, int)>();
            foreach (var v in world.Villages)
            {
                Assert.That(v.X, Is.InRange(0, World.MapSize - 1));
                Assert.That(v.Y, Is.InRange(0, World.MapSize - 1));
                Assert.AreNotEqual(TerrainType.Water, world.TerrainAt(v.X, v.Y), $"{v.Name} at {v.X}|{v.Y} is in a lake");
                Assert.IsTrue(positions.Add((v.X, v.Y)), $"two villages at {v.X}|{v.Y}");
            }
            // Neighbours are allowed now.
            Assert.IsTrue(world.Villages.Any(a => world.Villages.Any(b => a != b && Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1)));
        }

        [Test]
        public void BarbariansAreOwnerlessAndHaveUniqueIds()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 5 * World.SecondsPerDay);
            var barbarians = world.Villages.Where(v => world.FindPlayer(v.OwnerId) == null).ToList();
            Assert.IsTrue(barbarians.All(v => v.OwnerId == -1 && v.IsBarbarian && v.Name == World.BarbarianName));
            Assert.AreEqual(world.Villages.Count, world.Villages.Select(v => v.Id).Distinct().Count());
        }

        [Test]
        public void TheSameSeedPlacesTheSameVillages()
        {
            List<(int, int, int)> Run()
            {
                var world = NewWorld(5);
                world.AdvanceTo(world.Now + 5 * World.SecondsPerDay);
                return world.Villages.Select(v => (v.X, v.Y, v.Points)).ToList();
            }
            CollectionAssert.AreEqual(Run(), Run());
        }

        [Test]
        public void OldBarbariansInTheMiddleOutgrowTheNewOnesOnTheFrontier()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 20 * World.SecondsPerDay);
            var barbs = world.Villages.Where(v => v.IsBarbarian).ToList();
            double middle = barbs.Where(v => FromCentre(v) < World.StartRadius).Average(v => v.Points);
            double frontier = barbs.Where(v => FromCentre(v) > world.SpawnRadius - 3).Average(v => v.Points);
            Assert.Greater(middle, frontier);
        }

        [Test]
        public void VillagesNearFindsEveryVillageInRangeAndNoOthers()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 15 * World.SecondsPerDay);
            var home = world.PlayerVillage;
            foreach (double radius in new[] { 0.0, 3.5, 12, 40 })
            {
                var expected = world.Villages.Where(v => World.Distance(home, v) <= radius).Select(v => v.Id).OrderBy(i => i);
                var found = world.VillagesNear(home.X, home.Y, radius).Select(v => v.Id).OrderBy(i => i);
                CollectionAssert.AreEqual(expected, found, $"radius {radius}");
            }
        }

        [Test]
        public void BarbariansProduceResourcesToo()
        {
            var world = NewWorld();
            var barb = world.Villages.First(v => v.IsBarbarian);
            barb.Wood = 0;
            world.AdvanceTo(world.Now + 3600);
            Assert.AreEqual(barb.ProductionPerHour(ResourceType.Wood), barb.Wood, 1e-6);
        }

        [Test]
        public void DistanceIsStraightLine()
        {
            var a = new Village { X = 50, Y = 50 };
            var b = new Village { X = 53, Y = 54 };
            Assert.AreEqual(5, World.Distance(a, b), 1e-9);
        }

        [Test]
        public void TravelTimeIsDistanceTimesTheSlowestUnitsPace()
        {
            var a = new Village { X = 50, Y = 50 };
            var b = new Village { X = 53, Y = 54 };
            Assert.AreEqual(5 * 18 * 60, World.TravelSeconds(a, b, UnitType.Spearman), 1e-9);
            Assert.AreEqual(5 * 9 * 60, World.TravelSeconds(a, b, UnitType.Scout), 1e-9);
            Assert.Greater(World.TravelSeconds(a, b, UnitType.Ram), World.TravelSeconds(a, b, UnitType.Swordsman));
        }

        [Test]
        public void VillagesCanBeFoundByPositionAndId()
        {
            var world = NewWorld();
            var barb = world.Villages.Last();
            Assert.AreSame(barb, world.VillageAt(barb.X, barb.Y));
            Assert.AreSame(barb, world.FindVillage(barb.Id));
            Assert.IsNull(world.FindVillage(-12345));
        }

        [Test]
        public void PointsGrowWithBuildings()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            int before = v.Points;
            v.Levels[(int)BuildingType.Warehouse]++;
            Assert.Greater(v.Points, before);
        }

        /// <summary>Moves a new world's villages to where they'd have been on the old 100 x 100 map.</summary>
        static void AsOnTheOldMap(World world)
        {
            int shift = (World.MapSize - 100) / 2;
            foreach (var v in world.Villages)
            {
                v.X -= shift;
                v.Y -= shift;
            }
        }

        [Test]
        public void OlderWorldsGetBarbariansWithoutDisturbingThePlayer()
        {
            var world = NewWorld();
            world.Villages.RemoveAll(v => v.IsBarbarian); // what a version-3 save looks like
            AsOnTheOldMap(world);
            var home = world.PlayerVillage;
            home.Levels[(int)BuildingType.TownHall] = 7;
            world.SpawnRadius = 0;

            world.UpgradeFrom(3);

            Assert.Greater(world.Villages.Count(v => v.IsBarbarian), 5);
            Assert.AreEqual(7, home.Level(BuildingType.TownHall));
            Assert.AreEqual(World.MapSize / 2, home.X, "the old map now sits in the middle of the new one");
        }

        [Test]
        public void OlderWorldsMoveIntoTheMiddleOfTheBiggerMapAndKeepGrowing()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 3 * World.SecondsPerDay);
            var a = world.Villages[3];
            var b = world.Villages[7];
            double distance = World.Distance(a, b);
            AsOnTheOldMap(world);
            world.Events = new EventQueue();
            world.SpawnRadius = 0;

            world.UpgradeFrom(8);

            Assert.AreEqual(World.MapSize / 2, world.PlayerVillage.X);
            Assert.AreEqual(distance, World.Distance(a, b), 1e-9, "distances are unchanged");
            Assert.AreSame(a, world.VillageAt(a.X, a.Y), "found at its new position");
            Assert.AreEqual(50, world.SpawnRadius, 1e-9, "the circle carries on from the old map's edge");
            Assert.AreEqual(1, world.Events.Pending.Count(e => e.Kind == EventKind.WorldGrowth));
        }
    }

    public class WallTests
    {
        [Test]
        public void NewVillagesHaveNoWall()
        {
            var world = World.CreateNew(new WorldSettings());
            Assert.AreEqual(0, world.PlayerVillage.Level(BuildingType.Wall));
        }

        [Test]
        public void TheWallNeedsABarracks()
        {
            var world = World.CreateNew(new WorldSettings());
            var v = world.PlayerVillage;
            var check = world.CheckBuild(v, BuildingType.Wall);
            Assert.AreEqual(BuildStatus.NeedsBuilding, check.Status);
            Assert.AreEqual(BuildingType.Barracks, check.Required.Building);

            v.Levels[(int)BuildingType.Barracks] = 1;
            Assert.AreEqual(BuildStatus.Ok, world.CheckBuild(v, BuildingType.Wall).Status);
        }

        [Test]
        public void EachWallLevelStrengthensDefenders()
        {
            Assert.AreEqual(1, Buildings.WallDefenseMultiplier(0), 1e-9);
            for (int level = 1; level <= 20; level++)
                Assert.Greater(Buildings.WallDefenseMultiplier(level), Buildings.WallDefenseMultiplier(level - 1));
            Assert.That(Buildings.WallDefenseMultiplier(20), Is.InRange(1.9, 2.2), "a full wall about doubles defenders");
        }
    }
}
