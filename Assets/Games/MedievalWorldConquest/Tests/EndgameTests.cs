using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>Factions, villages handed over, and world statistics.</summary>
    public class EndgameTests
    {
        static int[] Army(params (UnitType type, int count)[] units)
        {
            var troops = new int[Units.Count];
            foreach (var (type, count) in units) troops[(int)type] = count;
            return troops;
        }

        [Test]
        public void AnAttacksSpeedIsItsSlowestUnit()
        {
            Assert.AreEqual(AttackSpeed.Scout, AttackSpeeds.Of(Army((UnitType.Scout, 5))));
            Assert.AreEqual(AttackSpeed.Cavalry, AttackSpeeds.Of(Army((UnitType.Scout, 5), (UnitType.LightCavalry, 50))));
            Assert.AreEqual(AttackSpeed.Infantry, AttackSpeeds.Of(Army((UnitType.Axeman, 100), (UnitType.LightCavalry, 50))));
            Assert.AreEqual(AttackSpeed.Siege, AttackSpeeds.Of(Army((UnitType.Axeman, 100), (UnitType.Ram, 1))), "one ram slows the lot: a fake looks real");
            Assert.AreEqual(AttackSpeed.Nobleman, AttackSpeeds.Of(Army((UnitType.Ram, 10), (UnitType.Nobleman, 1))));
            Assert.IsTrue(AttackSpeeds.IsDangerous(AttackSpeed.Siege) && AttackSpeeds.IsDangerous(AttackSpeed.Nobleman));
            Assert.IsFalse(AttackSpeeds.WorthHelp(AttackSpeed.Cavalry));
            Assert.AreEqual(UnitType.Ram, AttackSpeeds.Icon(AttackSpeed.Siege));
        }

        [Test]
        public void TheRealmSplitsAlongItsFriendships()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 1234, Speed = 1f, RivalDensity = 1, Diplomacy = true, ProtectionDays = 0 });
            var lords = world.Players.Where(p => !p.IsHuman && p.Personality != AiPersonality.Inactive && !p.Quit && world.VillagesOf(p.Id).Count > 0).ToArray();
            Assert.GreaterOrEqual(lords.Length, 15);
            Tribe Make(int from, string tag)
            {
                var t = world.FoundTribe(lords[from], "The " + tag, tag);
                for (int i = 1; i < 5; i++) world.JoinTribe(lords[from + i], t);
                return t;
            }
            var a = Make(0, "AA");
            var b = Make(5, "BB");
            var c = Make(10, "CC");
            world.SetRelation(a, b, RelationKind.Ally);
            world.SetRelation(a, c, RelationKind.Enemy);

            world.FormFactions();
            Assert.IsTrue(world.FactionsFormed);
            Assert.That(world.Cores().Count, Is.InRange(2, World.MaxFactions));
            Assert.AreEqual(a.FactionId, b.FactionId, "allies stand together");
            Assert.AreNotEqual(a.FactionId, c.FactionId, "enemies lead rival factions");
            Assert.IsTrue(world.OnCoreSide(a) && world.OnCoreSide(b));

            world.AdvanceTo(world.Now + World.TribeTickHours * 3600);
            Assert.AreEqual(RelationKind.Enemy, world.Relation(world.CoreOf(a), world.CoreOf(c)), "the factions' leaders go to war");
        }

        [Test]
        public void AHandedOverVillageDoesNotFight()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 1, ProtectionDays = 0 });
            var home = world.PlayerVillage;
            home.Levels[(int)BuildingType.Farm] = 28;
            home.Troops[(int)UnitType.Axeman] = 50;
            home.Troops[(int)UnitType.Nobleman] = 1;
            var target = world.Villages.Where(v => !v.IsBarbarian && v.OwnerId != home.OwnerId).OrderBy(v => World.Distance(home, v)).First();
            target.Troops[(int)UnitType.Spearman] = 2000;
            target.Loyalty = 15;
            target.FedTo = home.OwnerId;
            target.FedUntil = world.Now + World.FeedDays * World.SecondsPerDay;

            var attack = world.Send(home, target, Army((UnitType.Axeman, 50), (UnitType.Nobleman, 1)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);
            var report = world.Reports.Last();
            Assert.AreEqual(0, report.DefenderTroops.Sum(), "its defenders stood aside");
            Assert.IsTrue(report.Conquered);
            Assert.AreEqual(home.OwnerId, target.OwnerId);
            Assert.AreEqual(0, target.TroopCount(UnitType.Spearman), "the old owner's troops left; they weren't handed over too");
            Assert.AreEqual(-1, target.FedTo);
        }

        [Test]
        public void StatisticsCountTodayThisWeekAndForever()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 0, ProtectionDays = 0 });
            var home = world.PlayerVillage;
            home.Levels[(int)BuildingType.Farm] = 28;
            home.Troops[(int)UnitType.Axeman] = 200;
            if (!world.Villages.Any(v => v.IsBarbarian)) world.QuitLord(world.Players.First(p => p.Personality == AiPersonality.Noob));
            var target = world.Villages.Where(v => v.IsBarbarian).OrderBy(v => World.Distance(home, v)).First();
            System.Array.Clear(target.Troops, 0, target.Troops.Length);
            target.Troops[(int)UnitType.Spearman] = 10;
            target.Wood = target.Clay = target.Iron = 500;
            var human = world.HumanPlayer;

            var attack = world.Send(home, target, Army((UnitType.Axeman, 200)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);
            var report = world.Reports.Last();
            long loot = report.Loot.Wood + report.Loot.Clay + report.Loot.Iron;
            Assert.Greater(loot, 0);
            Assert.AreEqual(10, world.StatOf(human, StatKind.DefeatedAttacking, StatPeriod.Today));
            Assert.AreEqual(loot, world.StatOf(human, StatKind.Loot, StatPeriod.Today));
            Assert.AreEqual(report.AttackerLost.Sum(), world.StatOf(human, StatKind.TroopsLost, StatPeriod.AllTime));
            Assert.AreEqual(loot, world.WorldStatOf(StatKind.Loot, StatPeriod.Today));

            // The next day (still the first week): today starts again, the week and all time keep it.
            world.AdvanceTo((World.DayOf(world.Now) * World.SecondsPerDay) + 3600);
            Assert.AreEqual(0, world.StatOf(human, StatKind.DefeatedAttacking, StatPeriod.Today));
            Assert.AreEqual(10, world.StatOf(human, StatKind.DefeatedAttacking, StatPeriod.ThisWeek));
            Assert.AreEqual(10, world.StatOf(human, StatKind.DefeatedAttacking, StatPeriod.AllTime));
            Assert.AreEqual(loot, world.WorldStatOf(StatKind.Loot, StatPeriod.ThisWeek));
        }
    }
}
