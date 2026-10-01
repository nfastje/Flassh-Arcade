using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>The Account Manager: build templates and troop targets, carried out by the world itself.</summary>
    public class AccountManagerTests
    {
        static World NewWorld() => World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 0, ProtectionDays = 0 });

        static void Rich(Village v)
        {
            v.Levels[(int)BuildingType.Warehouse] = 20;
            v.Levels[(int)BuildingType.Farm] = 20;
            v.Wood = v.Clay = v.Iron = v.StorageCapacity;
        }

        [Test]
        public void AVillageFollowsItsTemplateInOrder()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            Rich(v);
            world.SetVillageTemplate(v, "Economy");
            Assert.AreEqual(World.MaxBuildQueue, v.Queue.Count, "the queue fills at once");
            Assert.AreEqual(BuildingType.TimberCamp, v.Queue[0].Type, "in the template's order");
            Assert.AreEqual(BuildingType.ClayPit, v.Queue[1].Type);
            Assert.AreEqual(BuildingType.IronMine, v.Queue[2].Type);
        }

        [Test]
        public void ItKeepsBuildingAsTimeGoesBy()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            Rich(v);
            world.SetVillageTemplate(v, "Economy");
            // A day away (as when a real-time world catches up): the manager carries on, round after round.
            world.AdvanceByRealSeconds(World.SecondsPerDay);
            Assert.GreaterOrEqual(v.Level(BuildingType.Headquarters), 3);
            Assert.GreaterOrEqual(v.Level(BuildingType.Farm), 20);
            StringAssert.StartsWith("Next:", world.ManagerStatus(v));
        }

        [Test]
        public void AFarmTooSmallIsUpgradedFirst()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Levels[(int)BuildingType.Warehouse] = 20;
            v.Wood = v.Clay = v.Iron = v.StorageCapacity;
            v.Troops[(int)UnitType.Spearman] = v.PopulationCapacity - v.PopulationUsed; // not a worker to spare
            Assert.AreEqual(BuildStatus.FarmTooSmall, world.CheckBuild(v, BuildingType.TimberCamp).Status);
            world.SetVillageTemplate(v, "Economy");
            Assert.AreEqual(BuildingType.Farm, v.Queue[0].Type);
        }

        [Test]
        public void TroopTargetsAreResearchedThenRecruited()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            Rich(v);
            v.Levels[(int)BuildingType.Headquarters] = 10;
            v.Levels[(int)BuildingType.Barracks] = 5;
            v.Levels[(int)BuildingType.Smithy] = 5;
            var targets = new int[Units.Count];
            targets[(int)UnitType.Axeman] = 50;
            targets[(int)UnitType.Spearman] = 30;
            world.SetTroopTargets(v, targets);
            Assert.IsTrue(v.IsBeingResearched(UnitType.Axeman), "axemen researched first");
            Assert.AreEqual(30, v.Recruitment.Where(o => o.Unit == UnitType.Spearman).Sum(o => o.Remaining), "spearmen need no research");

            world.AdvanceByRealSeconds(2 * World.SecondsPerDay);
            Assert.AreEqual(50, v.TroopCount(UnitType.Axeman) + v.Recruitment.Where(o => o.Unit == UnitType.Axeman).Sum(o => o.Remaining));
            Assert.AreEqual(30, v.TroopCount(UnitType.Spearman), "no more than the target");
        }

        [Test]
        public void ThePlayersOwnTemplatesCanBeMadeAndDeleted()
        {
            var world = NewWorld();
            Assert.IsNull(world.CreateTemplate("Mine", "Defensive"));
            Assert.IsNotNull(world.CreateTemplate("Mine", "Economy"), "names are unique");
            Assert.AreEqual(world.FindTemplate("Defensive").Steps.Count, world.FindTemplate("Mine").Steps.Count);
            world.SetVillageTemplate(world.PlayerVillage, "Mine");
            Assert.IsTrue(world.DeleteTemplate("Mine"));
            Assert.AreEqual("", world.ManagementOf(world.PlayerVillage.Id).Template);
            Assert.IsFalse(world.DeleteTemplate("Economy"), "the game's own can't be deleted");
        }
    }
}
