using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class NobleTrainTests
    {
        static int[] Army(params (UnitType type, int count)[] units)
        {
            var troops = new int[Units.Count];
            foreach (var (type, count) in units) troops[(int)type] = count;
            return troops;
        }

        static int Sum(System.Collections.Generic.List<int[]> waves, UnitType type) => waves.Sum(w => w[(int)type]);

        [Test]
        public void EachNoblemanGetsHisOwnAttackWithJustEnoughEscort()
        {
            var troops = Army((UnitType.Axeman, 200), (UnitType.LightCavalry, 40), (UnitType.Scout, 5), (UnitType.Ram, 10), (UnitType.Nobleman, 4));
            var waves = World.SplitTrain(troops, TrainEscort.Minimal, 20);
            Assert.AreEqual(4, waves.Count);
            Assert.IsTrue(waves.All(w => w[(int)UnitType.Nobleman] == 1));
            var empty = new int[Units.Count];
            for (int w = 1; w < 4; w++)
            {
                Assert.AreEqual(0, waves[w][(int)UnitType.Axeman] + waves[w][(int)UnitType.Scout] + waves[w][(int)UnitType.Ram], "one kind only: light cavalry first");
                int wall = Battle.WallAfterRams(20, 10);
                var fight = Battle.Fight(waves[w], empty, wall, -Battle.MaxLuck);
                Assert.IsTrue(fight.AttackerWon && fight.AttackerLossFraction < 0.5, "the nobleman survives an emptied village");
                var fewer = (int[])waves[w].Clone();
                if (fewer[(int)UnitType.LightCavalry] > 0)
                {
                    fewer[(int)UnitType.LightCavalry]--;
                    var weaker = Battle.Fight(fewer, empty, wall, -Battle.MaxLuck);
                    Assert.IsFalse(weaker.AttackerWon && weaker.AttackerLossFraction < 0.5, "and no more go than needed");
                }
            }
            foreach (var type in new[] { UnitType.Axeman, UnitType.LightCavalry, UnitType.Scout, UnitType.Ram, UnitType.Nobleman })
                Assert.AreEqual(troops[(int)type], Sum(waves, type), $"every {type} goes somewhere");
        }

        [Test]
        public void AnEvenSplitSharesEverything()
        {
            var waves = World.SplitTrain(Army((UnitType.Axeman, 202), (UnitType.Nobleman, 4)), TrainEscort.Even, 0);
            Assert.AreEqual(new[] { 52, 50, 50, 50 }, waves.Select(w => w[(int)UnitType.Axeman]).ToArray());
        }

        [Test]
        public void OneNoblemanIsJustAnAttack()
        {
            var waves = World.SplitTrain(Army((UnitType.Axeman, 50), (UnitType.Nobleman, 1)), TrainEscort.Minimal, 0);
            Assert.AreEqual(1, waves.Count);
            Assert.AreEqual(50, waves[0][(int)UnitType.Axeman]);
        }

        [Test]
        public void ATrainLandsInOrderAndTheRestTurnBackOnceItsWon()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 0, ProtectionDays = 0 });
            var home = world.PlayerVillage;
            home.Levels[(int)BuildingType.Farm] = 28;
            home.Troops[(int)UnitType.Axeman] = 200;
            home.Troops[(int)UnitType.Nobleman] = 6;
            if (!world.Villages.Any(v => v.IsBarbarian)) world.QuitLord(world.Players.First(p => p.Personality == AiPersonality.Noob));
            var target = world.Villages.Where(v => v.IsBarbarian).OrderBy(v => World.Distance(home, v)).First();
            System.Array.Clear(target.Troops, 0, target.Troops.Length);
            target.Supports.Clear();

            // Six noblemen: at most five (20 loyalty each at the least) are needed, so at least one is too late.
            var train = world.SendTrain(home, target, Army((UnitType.Axeman, 100), (UnitType.Nobleman, 6)), TrainEscort.Minimal);
            Assert.AreEqual(6, train.Count);
            for (int i = 1; i < train.Count; i++)
                Assert.AreEqual(World.TrainGapSeconds, train[i].ArriveTime - train[i - 1].ArriveTime, 1e-6, "a split second apart");
            Assert.AreEqual(100, home.TroopCount(UnitType.Axeman));
            Assert.AreEqual(0, home.TroopCount(UnitType.Nobleman));

            world.AdvanceTo(train[train.Count - 1].ArriveTime);
            Assert.AreEqual(home.OwnerId, target.OwnerId, "noblemen in a row win it");
            Assert.IsFalse(world.Reports.Any(r => r.DefenderPlayerId == home.OwnerId), "nobody attacked the village once it was ours");
            Assert.Less(world.Reports.Count(r => r.Kind == ReportKind.Attack), 6, "the last got there too late to fight");
            Assert.AreEqual(5, world.Commands.Count(c => c.Kind == CommandKind.Return && c.Troops[(int)UnitType.Nobleman] == 1),
                "every nobleman but the one who won it heads home");
        }
    }
}
