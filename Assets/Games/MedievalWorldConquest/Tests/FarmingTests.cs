using System.Collections.Generic;
using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>How rival lords farm: only from what they know, scouting first, every personality but the noobs.</summary>
    public class FarmingTests
    {
        static World NewWorld() => World.CreateNew(new WorldSettings { Seed = 1234, Speed = 1f, RivalDensity = 1 });

        static (Player lord, Village home) LordOf(World world, AiPersonality personality)
        {
            var lord = world.Players.First(p => p.Personality == personality);
            return (lord, world.VillagesOf(lord.Id)[0]);
        }

        static List<Command> AttacksOnBarbarians(World world, Player lord) =>
            world.CommandsOf(lord.Id).Where(c => c.Kind == CommandKind.Attack && world.FindVillage(c.ToVillageId).IsBarbarian).ToList();

        [Test]
        public void ALordWithScoutsLooksBeforeItRaids()
        {
            var world = NewWorld();
            var (lord, home) = LordOf(world, AiPersonality.Raider);
            home.Troops[(int)UnitType.Scout] = 6;
            home.Troops[(int)UnitType.LightCavalry] = 60;

            world.AdvanceTo(world.Now + 20 * 60);

            var sent = AttacksOnBarbarians(world, lord);
            Assert.IsNotEmpty(sent, "it went looking for farms");
            foreach (var c in sent)
                Assert.AreEqual(c.Troops[(int)UnitType.Scout], c.Troops.Sum(), "villages it knows nothing about get scouts, not raiders");
        }

        [Test]
        public void LordsDontSeeABarbarianGarrisonUntilTheyMeetIt()
        {
            var world = NewWorld();
            var (lord, home) = LordOf(world, AiPersonality.Raider);
            home.Troops[(int)UnitType.LightCavalry] = 20; // no scouts: it farms blind
            var trap = world.VillagesNear(home.X, home.Y, World.AiRaidRange).Where(v => v.IsBarbarian).OrderBy(v => World.Distance(home, v)).First();
            trap.Troops[(int)UnitType.Spearman] = 500;

            world.AdvanceTo(world.Now + 4 * 3600);

            var note = lord.Notes.FirstOrDefault(n => n.VillageId == trap.Id);
            Assert.IsNotNull(note, "it raided the nearest barbarian village, not knowing about the spearmen");
            Assert.Greater(note.AvoidUntil, world.Now, "and after losing, it stays away");
        }

        [Test]
        public void ARaidThatComesBackTeachesTheLordWhatIsThere()
        {
            var world = NewWorld();
            var (lord, home) = LordOf(world, AiPersonality.Raider);
            home.Troops[(int)UnitType.Scout] = 6;
            home.Troops[(int)UnitType.LightCavalry] = 60;
            world.AdvanceTo(world.Now + 6 * 3600);
            Assert.IsTrue(lord.Notes.Any(n => n.SeenLevels != null && n.LootSeenAt >= 0), "scouts counted a village's stores and buildings");
            Assert.IsTrue(lord.Notes.Any(n => n.SeenTroops != null));
        }

        [Test]
        public void DefendersFarmToo()
        {
            var world = NewWorld();
            // (The world's first few regular lords may not include a defender: make one.)
            var (lord, home) = LordOf(world, AiPersonality.Raider);
            lord.Personality = AiPersonality.Defender;
            home.Troops[(int)UnitType.LightCavalry] = 30;
            home.Troops[(int)UnitType.Spearman] = 200;

            world.AdvanceTo(world.Now + 3600);

            var raids = AttacksOnBarbarians(world, lord);
            Assert.IsNotEmpty(raids);
            int spearsOut = raids.Sum(c => c.Troops[(int)UnitType.Spearman]);
            Assert.LessOrEqual(spearsOut, 100, "at least half the spearmen stay home to defend");
        }

        [Test]
        public void NoobsDontFarm()
        {
            var world = NewWorld();
            var (lord, home) = LordOf(world, AiPersonality.Noob);
            home.Troops[(int)UnitType.Axeman] = 100;
            world.AdvanceTo(world.Now + 6 * 3600);
            Assert.IsEmpty(world.CommandsOf(lord.Id));
        }
    }
}
