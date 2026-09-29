using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class VillageTests
    {
        [Test]
        public void ThePlayerCanRenameTheirVillage()
        {
            var world = World.CreateNew(new WorldSettings { RivalDensity = 1 });
            var v = world.PlayerVillage;
            Assert.IsTrue(world.RenameVillage(v, "  Castle Rock  "));
            Assert.AreEqual("Castle Rock", v.Name, "trimmed");
            Assert.IsFalse(world.RenameVillage(v, "   "), "not blank");
            Assert.AreEqual("Castle Rock", v.Name);
            world.RenameVillage(v, new string('x', 50));
            Assert.AreEqual(World.MaxVillageNameLength, v.Name.Length, "cut to the longest allowed");

            var theirs = world.Villages.First(x => x.OwnerId != world.HumanPlayer.Id);
            string before = theirs.Name;
            Assert.IsFalse(world.RenameVillage(theirs, "Mine now"), "only the player's own villages");
            Assert.AreEqual(before, theirs.Name);
        }

        [Test]
        public void EveryVillageHasARallyPointWorthNoPoints()
        {
            var world = World.CreateNew(new WorldSettings());
            Assert.IsTrue(world.Villages.All(v => v.Level(BuildingType.RallyPoint) == 1));
            Assert.AreEqual(39, world.PlayerVillage.Points, "the rally point adds no points");

            foreach (var v in world.Villages) v.Levels[(int)BuildingType.RallyPoint] = 0; // an older save
            world.UpgradeFrom(11);
            Assert.IsTrue(world.Villages.All(v => v.Level(BuildingType.RallyPoint) == 1));
        }

        [Test]
        public void BuildingsTakeTribalWarsPopulation()
        {
            // A new village: Headquarters 5, timber camp 5, clay pit 10, iron mine 10 (the farm and warehouse need nobody).
            var world = World.CreateNew(new WorldSettings { RivalDensity = 0 });
            Assert.AreEqual(30, world.PlayerVillage.BuildingPopulation);
            Assert.AreEqual(240, world.PlayerVillage.PopulationCapacity);
            // Fully built, the buildings take a few thousand of the farm's 24,000, leaving the rest for troops.
            int full = Buildings.Definitions.Sum(d => Buildings.PopulationAtLevel(d.Type, d.MaxLevel));
            Assert.That(full, Is.InRange(2500, 4500));
        }
    }
}
