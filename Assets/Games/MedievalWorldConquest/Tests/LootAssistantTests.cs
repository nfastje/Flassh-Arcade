using System;
using System.Collections.Generic;
using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>The Loot Assistant: templates, one-click raids, and the raid cycle that runs while the game is closed.</summary>
    public class LootAssistantTests
    {
        static World NewWorld()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 0, ProtectionDays = 0 });
            world.QuestIndex = Array.FindIndex(World.QuestLine, q => q.Title == World.LootAssistantQuest);
            world.QuestReady = true;
            world.ClaimQuest();
            return world;
        }

        /// <summary>The nearest barbarian village, emptied of troops and wall so raids on it go cleanly.</summary>
        static Village EmptyBarbarian(World world)
        {
            var target = world.LootList(world.PlayerVillage).First();
            Array.Clear(target.Troops, 0, target.Troops.Length);
            target.Supports.Clear();
            target.Levels[(int)BuildingType.Wall] = 0;
            return target;
        }

        [Test]
        public void TheFirstRaidQuestUnlocksIt()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 0 });
            Assert.IsFalse(world.LootAssistantUnlocked);
            Assert.IsNotNull(world.SendLoot(world.PlayerVillage, world.LootList(world.PlayerVillage).First(), World.TemplateA));
            world = NewWorld();
            Assert.IsTrue(world.LootAssistantUnlocked);
            Assert.AreEqual(10, world.LootTemplateA[(int)UnitType.Spearman], "template A starts with ten spearmen");
            Assert.IsTrue(world.LootList(world.PlayerVillage).All(v => v.IsBarbarian), "barbarians around the village");
        }

        [Test]
        public void ARaidCycleKeepsRaidingWhileTimeGoesBy()
        {
            var world = NewWorld();
            var home = world.PlayerVillage;
            home.Troops[(int)UnitType.Spearman] = 40;
            var target = EmptyBarbarian(world);
            Assert.IsNull(world.StartCycle(home, target, World.TemplateA));
            Assert.AreEqual(30, home.TroopCount(UnitType.Spearman), "the first raid goes at once");

            // Two days away, as when a real-time world catches up: raid after raid, one at a time.
            int raids = 0;
            var latest = new HashSet<int>();
            int clean = 0;
            world.EventApplied += e =>
            {
                if (e.Kind != EventKind.CommandArrives) return;
                Assert.LessOrEqual(world.CommandsOf(world.HumanPlayer.Id).Count, 1, "one raid on the go at a time");
                var t = world.LootTargetFor(target.Id);
                if (t == null || !World.HasReport(t) || !latest.Add(t.LatestReport.Id)) return;
                raids++;
                if (t.LastResult == RaidResult.Clean) clean++;
            };
            world.AdvanceByRealSeconds(2 * World.SecondsPerDay);
            Assert.GreaterOrEqual(raids, 5);
            Assert.IsTrue(world.LootTargetFor(target.Id).Cycling);
            // (The village rebuilds its wall as it grows, which can cost a spearman: a small loss doesn't stop the cycle.)
            Assert.AreNotEqual(RaidResult.Defeat, world.LootTargetFor(target.Id).LastResult);
            var attacks = world.Reports.Where(r => r.Kind == ReportKind.Attack).ToList();
            Assert.IsTrue(attacks.All(r => r.AttackerLost.Sum() > 0 && !r.Routine), "only raids with losses reach the report list");
            Assert.AreEqual(raids - clean, attacks.Count(r => r.DefenderVillageId == target.Id), "clean raids stay out of it");
            var last = world.LootTargetFor(target.Id).LatestReport;
            Assert.AreEqual(target.Id, last.DefenderVillageId, "the latest raid is kept with the village");
            Assert.AreSame(last, world.FindReport(last.Id), "and can be opened");
            Assert.IsEmpty(world.ArchivedReports, "nothing is archived");
            Assert.Greater(world.StatOf(world.HumanPlayer, StatKind.Loot, StatPeriod.AllTime), 0);
        }

        [Test]
        public void ACyclesCleanRaidsAreKeptOnlyAsTheLatestReport()
        {
            var world = NewWorld();
            var home = world.PlayerVillage;
            home.Troops[(int)UnitType.LightCavalry] = 40;
            var template = new int[Units.Count];
            template[(int)UnitType.LightCavalry] = 20; // far too strong for the villagers to hurt
            world.SetLootTemplate(World.TemplateB, template);
            var target = EmptyBarbarian(world);
            Assert.IsNull(world.StartCycle(home, target, World.TemplateB));
            int before = world.Reports.Count;
            world.AdvanceByRealSeconds(World.SecondsPerDay);

            var t = world.LootTargetFor(target.Id);
            Assert.AreEqual(RaidResult.Clean, t.LastResult);
            Assert.IsTrue(World.HasReport(t));
            Assert.IsTrue(t.LatestReport.Routine && t.LatestReport.Read);
            Assert.AreEqual(before, world.Reports.Count, "no routine raid in the report list");
            Assert.AreSame(t.LatestReport, world.FindReport(t.LatestReport.Id));
            Assert.AreEqual(t.LatestReport.Id, world.ReportsAbout(target.Id)[0].Id, "the village's window lists it");
        }

        [Test]
        public void ACycleStopsWhenItsRaidIsBeaten()
        {
            var world = NewWorld();
            var home = world.PlayerVillage;
            home.Troops[(int)UnitType.Spearman] = 40;
            var target = EmptyBarbarian(world);
            target.Troops[(int)UnitType.Spearman] = 500;
            Assert.IsNull(world.StartCycle(home, target, World.TemplateA));
            world.AdvanceByRealSeconds(World.SecondsPerDay);
            var t = world.LootTargetFor(target.Id);
            Assert.IsFalse(t.Cycling);
            Assert.AreEqual(RaidResult.Defeat, t.LastResult);
            StringAssert.Contains("beaten", t.Stopped);
            Assert.AreEqual(30, home.TroopCount(UnitType.Spearman), "no more troops thrown away");
        }

        /// <summary>Notes that scouts saw a village with 1,000 of each resource and a level 10 warehouse.</summary>
        static void Scouted(World world, Village target)
        {
            var t = world.LootTargetFor(target.Id, true);
            t.ScoutedAt = world.Now;
            t.ScoutedResources = new Cost(1000, 1000, 1000);
            t.ScoutedLevels = new int[Enum.GetValues(typeof(BuildingType)).Length];
            t.ScoutedLevels[(int)BuildingType.Warehouse] = 10;
        }

        [Test]
        public void TemplateCCarriesWhatTheScoutsSaw()
        {
            var world = NewWorld();
            var target = EmptyBarbarian(world);
            var home = world.PlayerVillage;
            home.Troops[(int)UnitType.Spearman] = 200;
            home.Troops[(int)UnitType.LightCavalry] = 10;
            home.Troops[(int)UnitType.Scout] = 2;
            Assert.IsNull(world.TemplateCFor(home, target), "not without a scouting report");
            StringAssert.Contains("scouting report", world.WhyNoC(home, target));
            Scouted(world, target);
            // 3,000 to carry: the light cavalry first (800, being fastest), then spearmen for the other 2,200 at 25
            // each; and a scout to see what's left.
            var c = world.TemplateCFor(home, target);
            Assert.AreEqual(10, c[(int)UnitType.LightCavalry]);
            Assert.AreEqual(88, c[(int)UnitType.Spearman]);
            Assert.AreEqual(1, c[(int)UnitType.Scout]);
            Assert.AreEqual(0, c[(int)UnitType.Axeman]);
        }

        [Test]
        public void TemplateCSendsWhatsAtHomeIfThatsFewer()
        {
            var world = NewWorld();
            var home = world.PlayerVillage;
            var target = EmptyBarbarian(world);
            Scouted(world, target);
            home.Troops[(int)UnitType.Spearman] = 50;
            Assert.IsNull(world.SendLoot(home, target, World.TemplateC));
            Assert.AreEqual(0, home.TroopCount(UnitType.Spearman), "all 50 went (120 were wanted)");
        }

        [Test]
        public void TemplateCUsesScoutingReportsFromBeforeTheAssistant()
        {
            var world = NewWorld();
            var home = world.PlayerVillage;
            var target = EmptyBarbarian(world);
            home.Troops[(int)UnitType.Scout] = 1;
            var scout = new int[Units.Count];
            scout[(int)UnitType.Scout] = 1;
            world.AdvanceTo(world.Send(home, target, scout, CommandKind.Attack).ArriveTime);
            world.LootTargets.Clear(); // as in a world saved before the Loot Assistant kept notes
            Assert.IsTrue(world.IsScouted(target));
            Assert.Greater(world.ExpectedLoot(target), 0);
        }

        [Test]
        public void ARaidThatCameBackWithRoomToSpareEmptiedTheVillage()
        {
            var world = NewWorld();
            var target = EmptyBarbarian(world);
            Scouted(world, target);
            var t = world.LootTargetFor(target.Id);
            t.ScoutedAt = world.Now - 3600; // scouted an hour before the raid
            t.LastRaidAt = world.Now;
            t.LastResult = RaidResult.Clean;
            t.FullHaul = false;
            Assert.AreEqual(0, world.ExpectedLoot(target), 1e-6, "nothing left the moment it was emptied");
        }
        [Test]
        public void ARaidNeedsTheTroops()
        {
            var world = NewWorld();
            var target = EmptyBarbarian(world);
            world.PlayerVillage.Troops[(int)UnitType.Spearman] = 5;
            StringAssert.StartsWith("Not enough troops", world.SendLoot(world.PlayerVillage, target, World.TemplateA));
            world.PlayerVillage.Troops[(int)UnitType.Spearman] = 10;
            Assert.IsNull(world.SendLoot(world.PlayerVillage, target, World.TemplateA));
            Assert.AreEqual(0, world.PlayerVillage.TroopCount(UnitType.Spearman));
        }
    }
}
