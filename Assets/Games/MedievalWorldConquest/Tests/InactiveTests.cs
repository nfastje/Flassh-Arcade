using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>Inactive players and the extra barbarian villages that fill the map.</summary>
    public class InactiveTests
    {
        static World NewWorld(float density = 1f) =>
            World.CreateNew(new WorldSettings { Seed = 1234, Speed = 1f, RivalDensity = density });

        static Player[] Inactives(World world) => world.Players.Where(p => p.Personality == AiPersonality.Inactive).ToArray();

        [Test]
        public void TheStartingAreaHasInactivePlayersToo()
        {
            var world = NewWorld();
            int inactive = Inactives(world).Length;
            int lords = world.Players.Count(p => !p.IsHuman && p.Personality != AiPersonality.Inactive);
            Assert.Greater(inactive, lords / 4, "a good number of inactive players among the lords");
            Assert.IsTrue(Inactives(world).All(p => world.VillagesOf(p.Id).Count == 1));
            Assert.IsTrue(Inactives(world).All(p => p.TargetPoints >= World.InactiveMinPoints));
        }

        [Test]
        public void NoRivalsMeansNoInactivePlayers()
        {
            var world = NewWorld(0f);
            world.AdvanceTo(world.Now + 3 * World.SecondsPerDay);
            Assert.AreEqual(0, Inactives(world).Length);
            Assert.Greater(world.Villages.Count(v => v.IsBarbarian), 10, "barbarians still fill in");
        }

        [Test]
        public void AnInactiveVillageGrowsToItsSizeAndThenStops()
        {
            var world = NewWorld();
            var player = Inactives(world).OrderBy(p => p.TargetPoints).First();
            var v = world.VillagesOf(player.Id)[0];
            int start = v.Points;
            world.AdvanceTo(world.Now + 20 * World.SecondsPerDay);
            Assert.Greater(v.Points, start, "it built something");
            Assert.GreaterOrEqual(v.Points, player.TargetPoints, "it reached its size");
            int reached = v.Points;
            Assert.IsFalse(world.Events.Pending.Any(e => e.Kind == EventKind.InactiveGrowth && e.VillageId == v.Id), "and then stopped");
            world.AdvanceTo(world.Now + World.SecondsPerDay);
            if (!v.IsBarbarian) Assert.AreEqual(reached, v.Points, "no more building while it waits to be closed");
            Assert.IsFalse(world.Events.Pending.Any(e => e.Kind == EventKind.AiThink && e.A == player.Id), "inactive players never take turns");
        }

        [Test]
        public void TwoWeeksAfterStoppingTheVillageGoesBarbarian()
        {
            var world = NewWorld();
            var player = Inactives(world).OrderBy(p => p.TargetPoints).First();
            var v = world.VillagesOf(player.Id)[0];
            double stopped = -1;
            world.EventApplied += e =>
            {
                if (stopped < 0 && e.Kind == EventKind.InactiveGrowth && e.VillageId == v.Id && v.Points >= player.TargetPoints) stopped = world.Now;
            };
            world.AdvanceTo(world.Now + 20 * World.SecondsPerDay);
            Assert.Greater(stopped, 0, "it reached its size");
            if (world.Now < stopped + World.InactiveDaysBeforeLeaving * World.SecondsPerDay)
            {
                Assert.IsFalse(v.IsBarbarian, "not before its fortnight is up");
                world.AdvanceTo(stopped + World.InactiveDaysBeforeLeaving * World.SecondsPerDay + 1);
            }
            Assert.IsTrue(v.IsBarbarian);
            Assert.IsTrue(player.Quit);
        }

        [Test]
        public void InactiveVillagesCountTowardsTheConquestGoal()
        {
            var world = NewWorld();
            Assert.AreEqual(world.Villages.Count(v => !v.IsBarbarian), world.LordVillageCount);
            Assert.Greater(Inactives(world).Length, 0);
        }

        [Test]
        public void OlderWorldsAreFilledInWhenLoaded()
        {
            // A version-14 world had no inactive players or extra barbarians (the ones this one already has only
            // make it a little more crowded).
            var world = NewWorld();
            world.AdvanceTo(world.Now + 10 * World.SecondsPerDay);
            var old = Inactives(world).Select(p => p.Id).ToHashSet();
            int before = world.Villages.Count;

            world.UpgradeFrom(14);

            var filled = Inactives(world).Where(p => !old.Contains(p.Id)).ToArray();
            Assert.Greater(filled.Length, 20, "the settled land gets its inactive players");
            Assert.Greater(world.Villages.Count, before + filled.Length, "and some more barbarians");
            // Those nearest the middle were settled longest ago: they start further along.
            double center = World.MapSize / 2.0;
            double Dist(Player p) { var v = world.VillagesOf(p.Id)[0]; return System.Math.Sqrt((v.X - center) * (v.X - center) + (v.Y - center) * (v.Y - center)); }
            Assert.IsTrue(filled.All(p => !world.IsProtected(p.Id)), "long past beginner protection");
            var inner = filled.Where(p => Dist(p) < world.SpawnRadius / 3).ToArray();
            Assert.IsTrue(inner.Any(p => world.VillagesOf(p.Id)[0].Points > 200));
        }
    }
}
