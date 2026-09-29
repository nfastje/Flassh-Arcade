using System;
using System.Collections.Generic;
using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class EventQueueTests
    {
        [Test]
        public void PopsEventsInTimeOrder()
        {
            var queue = new EventQueue();
            foreach (double t in new[] { 50.0, 10.0, 40.0, 20.0, 30.0 })
                queue.Push(new ScheduledEvent { Time = t });

            var popped = new List<double>();
            while (queue.Count > 0) popped.Add(queue.Pop().Time);

            CollectionAssert.AreEqual(new[] { 10.0, 20.0, 30.0, 40.0, 50.0 }, popped);
        }

        [Test]
        public void EventsAtTheSameTimeKeepTheirSchedulingOrder()
        {
            var queue = new EventQueue();
            for (int i = 0; i < 20; i++) queue.Push(new ScheduledEvent { Time = 5, A = i });

            for (int i = 0; i < 20; i++) Assert.AreEqual(i, queue.Pop().A);
        }

        [Test]
        public void PeekOnEmptyQueueThrows()
        {
            Assert.Throws<InvalidOperationException>(() => new EventQueue().Peek());
        }
    }

    public class WorldTests
    {
        static World NewWorld(float speed = 1f, TimeMode mode = TimeMode.RealTime) =>
            World.CreateNew(new WorldSettings { Speed = speed, TimeMode = mode, Seed = 42 });

        [Test]
        public void NewWorldStartsAtDawnOnDayOneWithAPlayerVillage()
        {
            var world = NewWorld();

            Assert.AreEqual(World.StartTime, world.Now);
            Assert.AreEqual("Day 1, 06:00", World.FormatClock(world.Now));
            Assert.IsNotNull(world.HumanPlayer);
            Assert.IsNotNull(world.PlayerVillage);
            Assert.AreEqual(world.HumanPlayer.Id, world.PlayerVillage.OwnerId);
        }

        [Test]
        public void SameSeedGivesTheSameWorld()
        {
            Assert.AreEqual(NewWorld().PlayerVillage.Name, NewWorld().PlayerVillage.Name);
        }

        [Test]
        public void RealSecondsAreScaledByWorldSpeed()
        {
            var world = NewWorld(speed: 20f);
            world.AdvanceByRealSeconds(3);
            Assert.AreEqual(World.StartTime + 60, world.Now, 1e-9);
        }

        [Test]
        public void AdvanceAppliesDueEventsInOrderAndLeavesLaterOnes()
        {
            var world = NewWorld();
            var applied = new List<int>();
            world.EventApplied = e => applied.Add(e.A);
            world.Schedule(30, EventKind.None, a: 3);
            world.Schedule(10, EventKind.None, a: 1);
            world.Schedule(20, EventKind.None, a: 2);
            world.Schedule(100, EventKind.None, a: 4);

            world.AdvanceTo(World.StartTime + 50);

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, applied);
            // Only this test's own events: barbarian villages always have a growth event pending too.
            Assert.AreEqual(1, world.Events.Pending.Count(e => e.Kind == EventKind.None));
            Assert.AreEqual(World.StartTime + 50, world.Now);
        }

        [Test]
        public void EachEventSeesTheClockAtItsOwnTime()
        {
            var world = NewWorld();
            var times = new List<double>();
            world.EventApplied = e => times.Add(world.Now);
            world.Schedule(10, EventKind.None);
            world.Schedule(25, EventKind.None);

            world.AdvanceTo(World.StartTime + 100);

            CollectionAssert.AreEqual(new[] { World.StartTime + 10, World.StartTime + 25 }, times);
        }

        [Test]
        public void EventsScheduledDuringAnAdvanceRunIfTheyFallInsideIt()
        {
            // A chain: each event schedules the next one 10 seconds later (like a build queue).
            var world = NewWorld();
            int count = 0;
            world.EventApplied = e =>
            {
                count++;
                world.Schedule(10, EventKind.None);
            };
            world.Schedule(10, EventKind.None);

            world.AdvanceTo(World.StartTime + 55);

            Assert.AreEqual(5, count); // at +10, +20, +30, +40, +50
            Assert.AreEqual(World.StartTime + 60, world.Events.Peek().Time, 1e-9);
        }

        [Test]
        public void TimeNeverGoesBackwards()
        {
            var world = NewWorld();
            world.AdvanceTo(World.StartTime + 100);
            world.AdvanceTo(World.StartTime + 50);
            Assert.AreEqual(World.StartTime + 100, world.Now);
        }

        [Test]
        public void ClockAndDurationFormatting()
        {
            Assert.AreEqual("Day 2, 13:05", World.FormatClock(World.SecondsPerDay + 13 * 3600 + 5 * 60 + 30));
            Assert.AreEqual("45s", World.FormatDuration(45));
            Assert.AreEqual("2m 5s", World.FormatDuration(125));
            Assert.AreEqual("2h 14m", World.FormatDuration(2 * 3600 + 14 * 60));
            Assert.AreEqual("3d 4h", World.FormatDuration(3 * World.SecondsPerDay + 4 * 3600));
        }
    }

    public class SaveGameTests
    {
        [Test]
        public void SaveAndLoadRoundTripsTheWorld()
        {
            var world = World.CreateNew(new WorldSettings { Speed = 20f, TimeMode = TimeMode.PausedWhenClosed, Seed = 7, ConquestGoal = 0.75f });
            world.AdvanceTo(World.StartTime + 1234.5);
            world.Schedule(500, EventKind.None, villageId: 0, a: 11, b: 22);
            var savedAt = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

            var loaded = SaveGame.FromJson(SaveGame.ToJson(world, savedAt), out var loadedSavedAt);

            Assert.AreEqual(savedAt, loadedSavedAt);
            Assert.AreEqual(world.Now, loaded.Now);
            Assert.AreEqual(20f, loaded.Settings.Speed);
            Assert.AreEqual(TimeMode.PausedWhenClosed, loaded.Settings.TimeMode);
            Assert.AreEqual(0.75f, loaded.Settings.ConquestGoal);
            Assert.AreEqual(world.PlayerVillage.Name, loaded.PlayerVillage.Name);
            // The test's event, plus the barbarians' growth and the rival lords' turns and orders.
            Assert.AreEqual(world.Events.Count, loaded.Events.Count);
            Assert.AreEqual(1, loaded.Events.Pending.Count(x => x.Kind == EventKind.None));
            // The rival lords come back as they were.
            Assert.AreEqual(world.Players.Count, loaded.Players.Count);
            var lord = world.Players.First(p => !p.IsHuman && p.Personality != AiPersonality.Inactive);
            var loadedLord = loaded.FindPlayer(lord.Id);
            Assert.AreEqual(lord.Name, loadedLord.Name);
            Assert.AreEqual(lord.Personality, loadedLord.Personality);
            Assert.AreEqual(lord.ProtectedUntil, loadedLord.ProtectedUntil);
            Assert.AreEqual(lord.SpentOnBuildings, loadedLord.SpentOnBuildings);
            var e = loaded.Events.Pending.Single(x => x.Kind == EventKind.None);
            Assert.AreEqual(world.Now + 500, e.Time, 1e-9);
            Assert.AreEqual(11, e.A);
            Assert.AreEqual(22, e.B);
        }

        [Test]
        public void EventsScheduledAfterLoadingStillComeAfterEarlierOnes()
        {
            // The queue's tie-breaking counter must survive saving, or order at equal times could change.
            var world = World.CreateNew(new WorldSettings());
            world.Schedule(10, EventKind.None, a: 1);
            var loaded = SaveGame.FromJson(SaveGame.ToJson(world, DateTime.UtcNow), out _);
            loaded.Schedule(10, EventKind.None, a: 2);

            Assert.AreEqual(1, loaded.Events.Pop().A);
            Assert.AreEqual(2, loaded.Events.Pop().A);
        }

        [Test]
        public void GarbageIsRejectedAsAFormatError()
        {
            Assert.Throws<FormatException>(() => SaveGame.FromJson("this is not a save", out _));
            Assert.Throws<FormatException>(() => SaveGame.FromJson("{}", out _));
        }

        [Test]
        public void RealTimeWorldsCatchUpOnTimeAway()
        {
            var world = World.CreateNew(new WorldSettings { TimeMode = TimeMode.RealTime });
            var saved = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(3600, SaveGame.CatchUpRealSeconds(world, saved, saved.AddHours(1)), 1e-6);
        }

        [Test]
        public void PausedWorldsDoNotCatchUp()
        {
            var world = World.CreateNew(new WorldSettings { TimeMode = TimeMode.PausedWhenClosed });
            var saved = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(0, SaveGame.CatchUpRealSeconds(world, saved, saved.AddHours(1)));
        }

        [Test]
        public void CatchUpIsCappedAndIgnoresClocksThatWentBackwards()
        {
            var world = World.CreateNew(new WorldSettings { TimeMode = TimeMode.RealTime });
            var saved = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(SaveGame.MaxCatchUpSeconds, SaveGame.CatchUpRealSeconds(world, saved, saved.AddDays(365)));
            Assert.AreEqual(0, SaveGame.CatchUpRealSeconds(world, saved, saved.AddHours(-3)));
        }
    }
}
