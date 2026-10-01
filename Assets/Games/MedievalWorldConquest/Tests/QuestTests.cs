using System;
using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>The quest line and the tips.</summary>
    public class QuestTests
    {
        static World NewWorld(bool diplomacy = false, bool coins = false) =>
            World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 0, Diplomacy = diplomacy, GoldCoins = coins, ProtectionDays = 0 });

        [Test]
        public void AQuestDoneCanBeClaimedForItsReward()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            Assert.AreEqual("Wood for the village", world.CurrentQuest.Title);
            world.UpdateQuests();
            Assert.IsFalse(world.QuestReady);
            Assert.IsNull(world.ClaimQuest(), "nothing to claim yet");

            v.Levels[(int)BuildingType.TimberCamp] = 2;
            world.UpdateQuests();
            Assert.IsTrue(world.QuestReady);
            double wood = v.Wood;
            var claimed = world.ClaimQuest();
            Assert.AreEqual("Wood for the village", claimed.Title);
            Assert.AreEqual(Math.Min(v.StorageCapacity, wood + claimed.Reward.Wood), v.Wood, 1e-6);
            Assert.AreEqual("Clay and iron", world.CurrentQuest.Title);
        }

        [Test]
        public void AQuestDoneStaysDoneUntilClaimed()
        {
            var world = NewWorld();
            var v = world.PlayerVillage;
            v.Levels[(int)BuildingType.TimberCamp] = 2;
            world.UpdateQuests();
            v.Levels[(int)BuildingType.TimberCamp] = 1; // (say, a catapult knocked it down)
            world.UpdateQuests();
            Assert.IsTrue(world.QuestReady);
        }

        [Test]
        public void QuestsThatDontBelongInTheWorldAreSkipped()
        {
            int tribeQuest = Array.FindIndex(World.QuestLine, q => q.Title == "Strength in numbers");
            int coinQuest = Array.FindIndex(World.QuestLine, q => q.Title == "Gold coins");

            var flat = NewWorld();
            flat.QuestIndex = tribeQuest;
            Assert.AreEqual(World.QuestLine[tribeQuest + 1].Title, flat.CurrentQuest.Title, "no tribes on a free-for-all world");
            flat.QuestIndex = coinQuest;
            Assert.AreEqual(World.QuestLine[coinQuest + 1].Title, flat.CurrentQuest.Title, "no coins at a flat price");

            var tribes = NewWorld(diplomacy: true, coins: true);
            tribes.QuestIndex = tribeQuest;
            Assert.AreEqual("Strength in numbers", tribes.CurrentQuest.Title);
            tribes.QuestIndex = coinQuest;
            Assert.AreEqual("Gold coins", tribes.CurrentQuest.Title);

            flat.QuestIndex = World.QuestLine.Length;
            Assert.IsNull(flat.CurrentQuest, "all done");
        }

        [Test]
        public void EachTipIsShownOncePerWorld()
        {
            var world = NewWorld();
            Assert.IsNull(world.NextTip());
            world.Reports.Add(new BattleReport { Id = world.NextReportId++, Kind = ReportKind.Attack, Time = world.Now });
            var tip = world.NextTip();
            Assert.IsNotNull(tip, "the first report");
            world.MarkTipShown(tip);
            Assert.AreNotSame(tip, world.NextTip());
        }
    }
}
