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
        public void TroopsSupportingAnotherVillageStillCount()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            Rich(v);
            v.Levels[(int)BuildingType.Barracks] = 1;
            // 40 of the village's spearmen are stationed elsewhere, supporting a neighbor.
            var host = world.Villages.First(x => x != v);
            host.Supports.Add(new SupportGroup { FromVillageId = v.Id, OwnerId = v.OwnerId, Troops = new int[Units.Count] });
            host.Supports[host.Supports.Count - 1].Troops[(int)UnitType.Spearman] = 40;
            var targets = new int[Units.Count];
            targets[(int)UnitType.Spearman] = 50;
            world.SetTroopTargets(v, targets, 100);
            Assert.AreEqual(10, v.Recruitment.Where(o => o.Unit == UnitType.Spearman).Sum(o => o.Remaining), "only the ten still missing");
        }

        [Test]
        public void UnitsFromTheSameBuildingAreTrainedSideBySide()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            Rich(v);
            v.Levels[(int)BuildingType.Barracks] = 5;
            v.Research[(int)UnitType.Swordsman] = 1;
            var targets = new int[Units.Count];
            targets[(int)UnitType.Spearman] = 5000;
            targets[(int)UnitType.Swordsman] = 5000;
            world.SetTroopTargets(v, targets, 100);
            int Queued(UnitType t) => v.Recruitment.Where(o => o.Unit == t).Sum(o => o.Remaining);
            int spears = Queued(UnitType.Spearman), swords = Queued(UnitType.Swordsman);
            Assert.Greater(spears, 0);
            Assert.Greater(swords, 0, "swordsmen are trained alongside the spearmen, not after them");
            Assert.That((double)spears / swords, Is.InRange(0.5, 2.0), $"{spears} spearmen, {swords} swordsmen");
        }

        /// <summary>A village with mines at 10 and a barracks, following the Economy template and wanting spearmen, run for days.</summary>
        static (World world, Village v) Managed(int troopShare, double days)
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            foreach (var b in new[] { BuildingType.TimberCamp, BuildingType.ClayPit, BuildingType.IronMine, BuildingType.Warehouse })
                v.Levels[(int)b] = 10;
            v.Levels[(int)BuildingType.Farm] = 5;
            v.Levels[(int)BuildingType.Headquarters] = 5;
            v.Levels[(int)BuildingType.Barracks] = 3;
            world.SetVillageTemplate(v, "Economy");
            var targets = new int[Units.Count];
            targets[(int)UnitType.Spearman] = 5000;
            world.SetTroopTargets(v, targets, troopShare);
            world.AdvanceByRealSeconds(days * World.SecondsPerDay);
            return (world, v);
        }

        static int Spearmen(Village v) => v.TroopCount(UnitType.Spearman) + v.Recruitment.Where(o => o.Unit == UnitType.Spearman).Sum(o => o.Remaining);

        [Test]
        public void TroopsGetTheirShareWhileBuildingsAreSavedFor()
        {
            var (_, none) = Managed(0, 4);
            var (world, half) = Managed(50, 4);
            Assert.Greater(Spearmen(half), Spearmen(none) + 100, $"troops are trained while the template saves up ({Spearmen(half)} vs {Spearmen(none)})");
            int before = Managed(50, 0).v.Points;
            Assert.Greater(half.Points, before + 30, $"and the template still moves on ({before} → {half.Points}; {none.Points} with no troops)");
            var m = world.ManagementOf(half.Id);
            double share = m.SpentOnTroops / (m.SpentOnTroops + m.SpentOnBuildings);
            Assert.That(share, Is.InRange(0.3, 0.7), "about half of the spending went on troops");
        }

        [Test]
        public void AllTroopsMeansTheTemplateOnlyGetsTheRest()
        {
            var (_, all) = Managed(100, 4);
            var (_, half) = Managed(50, 4);
            Assert.Greater(Spearmen(all), Spearmen(half), $"{Spearmen(all)} vs {Spearmen(half)}");
            Assert.Less(all.Points, half.Points, $"{all.Points} vs {half.Points}");
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
