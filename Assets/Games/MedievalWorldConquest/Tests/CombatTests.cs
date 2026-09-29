using System;
using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class BattleFormulaTests
    {
        static int[] Army(params (UnitType type, int count)[] units)
        {
            var troops = new int[Units.Count];
            foreach (var (type, count) in units) troops[(int)type] = count;
            return troops;
        }

        [Test]
        public void RamsKnockTheWallDownLevelByLevel()
        {
            Assert.AreEqual(5, Battle.WallAfterRams(5, 0));
            Assert.AreEqual(4, Battle.WallAfterRams(5, Battle.RamsPerWallLevel(5)));
            Assert.AreEqual(0, Battle.WallAfterRams(3, 100));
            Assert.Less(Battle.RamsPerWallLevel(2), Battle.RamsPerWallLevel(18), "higher walls take more rams per level");
        }

        [Test]
        public void CatapultsKnockBuildingsDown()
        {
            Assert.AreEqual(10, Battle.LevelAfterCatapults(10, 0));
            Assert.AreEqual(9, Battle.LevelAfterCatapults(10, Battle.CatapultsPerLevel(10)));
            Assert.AreEqual(0, Battle.LevelAfterCatapults(4, 1000));
        }

        [Test]
        public void AStrongerAttackWinsAndTheWinnerLosesSome()
        {
            var attackers = Army((UnitType.Axeman, 100));   // 4000 infantry attack
            var defenders = Army((UnitType.Spearman, 50));  // 20 + 50 × 15 = 770 infantry defence
            var r = Battle.Fight(attackers, defenders, 0, 0);

            Assert.IsTrue(r.AttackerWon);
            Assert.AreEqual(1, r.DefenderLossFraction);
            Assert.AreEqual(Math.Pow(770.0 / 4000.0, 1.5), r.AttackerLossFraction, 1e-9);
        }

        [Test]
        public void AWeakerAttackIsWipedOut()
        {
            var attackers = Army((UnitType.Axeman, 10));     // 400
            var defenders = Army((UnitType.Swordsman, 30));  // 20 + 1500 = 1520 vs infantry
            var r = Battle.Fight(attackers, defenders, 0, 0);

            Assert.IsFalse(r.AttackerWon);
            Assert.AreEqual(1, r.AttackerLossFraction);
            Assert.AreEqual(Math.Pow(400.0 / 1520.0, 1.5), r.DefenderLossFraction, 1e-9);
        }

        [Test]
        public void DefendersUseTheirDefenceAgainstTheAttackersClass()
        {
            // Spearmen defend far better against cavalry (45) than infantry (15).
            var spears = Army((UnitType.Spearman, 100));
            var infantry = Battle.Fight(Army((UnitType.Axeman, 100)), spears, 0, 0);
            var cavalry = Battle.Fight(Army((UnitType.LightCavalry, 31)), spears, 0, 0); // about the same attack (4030)
            Assert.Greater(cavalry.DefenseStrength, infantry.DefenseStrength * 2.5);
        }

        [Test]
        public void TheWallStrengthensDefenders()
        {
            var attackers = Army((UnitType.Axeman, 50));
            var defenders = Army((UnitType.Spearman, 50));
            var open = Battle.Fight(attackers, defenders, 0, 0);
            var walled = Battle.Fight(attackers, defenders, 10, 0);
            Assert.AreEqual(open.DefenseStrength * Buildings.WallDefenseMultiplier(10), walled.DefenseStrength, 1e-6);
        }

        [Test]
        public void RamsBreachTheWallBeforeTheFight()
        {
            var withRams = Battle.Fight(Army((UnitType.Axeman, 50), (UnitType.Ram, 40)), Army((UnitType.Spearman, 20)), 8, 0);
            Assert.AreEqual(8, withRams.WallBefore);
            Assert.Less(withRams.WallAfter, 8);
        }

        [Test]
        public void LuckSwingsTheAttack()
        {
            var a = Army((UnitType.Axeman, 100));
            var d = Army((UnitType.Spearman, 10));
            Assert.AreEqual(Battle.Fight(a, d, 0, 0).AttackStrength * 1.25, Battle.Fight(a, d, 0, 0.25).AttackStrength, 1e-6);
        }

        [Test]
        public void ScoutsGetThroughWhenTheyOutnumberTheDefendersScouts()
        {
            var r = Battle.Fight(Army((UnitType.Scout, 5)), Army((UnitType.Spearman, 100)), 0, 0);
            Assert.IsTrue(r.Scouted, "no defending scouts: they get through");
            Assert.AreEqual(0, r.AttackerScoutLossFraction);
            Assert.AreEqual(0, r.AttackerLossFraction, "a scouting run has no main battle");

            var blocked = Battle.Fight(Army((UnitType.Scout, 5)), Army((UnitType.Scout, 10)), 0, 0);
            Assert.IsFalse(blocked.Scouted);
            Assert.AreEqual(1, blocked.AttackerScoutLossFraction);
        }

        [Test]
        public void LootIsSharedEvenlyUpToCapacity()
        {
            Assert.AreEqual(new Cost(100, 100, 100), Battle.Loot(300, 1000, 1000, 1000));
            // When one resource runs out, the others fill the rest of the capacity.
            Assert.AreEqual(new Cost(20, 140, 140), Battle.Loot(300, 20, 1000, 1000));
            // Can't take more than there is.
            Assert.AreEqual(new Cost(50, 60, 70), Battle.Loot(10000, 50, 60, 70));
            Assert.AreEqual(new Cost(0, 0, 0), Battle.Loot(0, 50, 60, 70));
        }

        [Test]
        public void CarryCapacityAddsUpEachUnit()
        {
            Assert.AreEqual(10 * 25 + 5 * 80, Battle.CarryCapacity(Army((UnitType.Spearman, 10), (UnitType.LightCavalry, 5))));
        }
    }

    public class CommandTests
    {
        /// <summary>A world with a strong player army at home and a nearby barbarian village with a known garrison.</summary>
        static World WarWorld(out Village home, out Village target)
        {
            // No rivals to interfere, and no beginner protection so the barbarian test attackers can hit the player.
            var world = World.CreateNew(new WorldSettings { Seed = 4, Speed = 1f, RivalDensity = 0, ProtectionDays = 0 });
            home = world.PlayerVillage;
            home.Levels[(int)BuildingType.Farm] = 25;
            home.Levels[(int)BuildingType.Warehouse] = 20;
            home.Troops[(int)UnitType.Axeman] = 300;
            home.Troops[(int)UnitType.LightCavalry] = 100;
            home.Troops[(int)UnitType.Scout] = 10;
            var h = home;
            target = world.Villages.Where(v => v.IsBarbarian).OrderBy(v => World.Distance(h, v)).First();
            Array.Clear(target.Troops, 0, target.Troops.Length);
            target.Troops[(int)UnitType.Spearman] = 20;
            target.Levels[(int)BuildingType.Wall] = 0;
            target.Wood = target.Clay = target.Iron = 500;
            return world;
        }

        static int[] Army(params (UnitType type, int count)[] units)
        {
            var troops = new int[Units.Count];
            foreach (var (type, count) in units) troops[(int)type] = count;
            return troops;
        }

        [Test]
        public void SendingTakesTroopsFromTheGarrisonButTheyStillCountAgainstTheFarm()
        {
            var world = WarWorld(out var home, out var target);
            int popBefore = home.PopulationUsed;

            var command = world.Send(home, target, Army((UnitType.Axeman, 100)), CommandKind.Attack);

            Assert.IsNotNull(command);
            Assert.AreEqual(200, home.TroopCount(UnitType.Axeman));
            Assert.AreEqual(popBefore, home.PopulationUsed, "troops away still use farm space");
            Assert.AreEqual(world.Now + World.TravelSeconds(home, target, UnitType.Axeman), command.ArriveTime, 1e-6);
        }

        [Test]
        public void YouCantSendTroopsYouDontHave()
        {
            var world = WarWorld(out var home, out var target);
            Assert.AreEqual(SendStatus.NotEnoughTroops, world.CheckSend(home, target, Army((UnitType.Axeman, 301)), CommandKind.Attack).Status);
            Assert.AreEqual(SendStatus.NoTroops, world.CheckSend(home, target, new int[Units.Count], CommandKind.Attack).Status);
            Assert.AreEqual(SendStatus.SameVillage, world.CheckSend(home, home, Army((UnitType.Axeman, 1)), CommandKind.Attack).Status);
            Assert.IsNull(world.Send(home, target, Army((UnitType.Axeman, 301)), CommandKind.Attack));
            Assert.AreEqual(300, home.TroopCount(UnitType.Axeman), "nothing left home on a failed send");
        }

        [Test]
        public void TheArmyMovesAtItsSlowestUnitsPace()
        {
            var world = WarWorld(out var home, out var target);
            var check = world.CheckSend(home, target, Army((UnitType.LightCavalry, 10), (UnitType.Axeman, 1)), CommandKind.Attack);
            Assert.AreEqual(UnitType.Axeman, check.Slowest);
        }

        [Test]
        public void AWinningAttackKillsTheDefendersAndBringsLootHome()
        {
            var world = WarWorld(out var home, out var target);
            home.Wood = home.Clay = home.Iron = 0;
            var command = world.Send(home, target, Army((UnitType.Axeman, 100)), CommandKind.Attack);
            double travel = command.ArriveTime - world.Now;

            world.AdvanceTo(command.ArriveTime);

            Assert.AreEqual(0, target.TroopCount(UnitType.Spearman), "defenders wiped out");
            var report = world.Reports.Last();
            Assert.AreEqual(ReportKind.Attack, report.Kind);
            Assert.IsTrue(report.AttackerWon);
            Assert.AreEqual(20, report.DefenderLost[(int)UnitType.Spearman]);
            Assert.AreEqual((100 - report.AttackerLost[(int)UnitType.Axeman]) * 10, report.LootCapacity, "survivors carry 10 each");
            Assert.Greater(report.Loot.Wood, 0);
            // The barbarians kept producing while the attack marched there; the loot came out of that.
            double atArrival = Math.Min(target.StorageCapacity, 500 + target.ProductionPerHour(ResourceType.Wood) * travel / 3600);
            Assert.AreEqual(atArrival - report.Loot.Wood, target.Wood, 1e-6);

            // The survivors march home with the loot.
            var returning = world.Commands.Single(c => c.Kind == CommandKind.Return);
            Assert.AreEqual(travel, returning.ArriveTime - world.Now, 1e-6);
            world.AdvanceTo(returning.ArriveTime);

            int survivors = 100 - report.AttackerLost[(int)UnitType.Axeman];
            Assert.AreEqual(200 + survivors, home.TroopCount(UnitType.Axeman));
            Assert.GreaterOrEqual(home.Wood, report.Loot.Wood, "loot added to the village's stock");
            Assert.AreEqual(0, home.AwayPopulation, "nobody's away any more");
        }

        [Test]
        public void ALosingAttackDiesAndLearnsNothingOfTheDefenders()
        {
            var world = WarWorld(out var home, out var target);
            target.Troops[(int)UnitType.Swordsman] = 500;
            int popBefore = home.PopulationUsed;
            var command = world.Send(home, target, Army((UnitType.Axeman, 10)), CommandKind.Attack);

            world.AdvanceTo(command.ArriveTime);

            var report = world.Reports.Last();
            Assert.IsFalse(report.AttackerWon);
            Assert.AreEqual(10, report.AttackerLost[(int)UnitType.Axeman]);
            Assert.IsFalse(report.DefenderVisible, "no survivors and no scouts: nothing seen");
            Assert.IsFalse(world.Commands.Any(c => c.Kind == CommandKind.Return), "nobody comes back");
            Assert.AreEqual(popBefore - 10, home.PopulationUsed, "the dead free up farm space");
        }

        [Test]
        public void ScoutsRevealTheVillage()
        {
            var world = WarWorld(out var home, out var target);
            var command = world.Send(home, target, Army((UnitType.Scout, 3)), CommandKind.Attack);
            world.AdvanceTo(command.ArriveTime);

            var report = world.Reports.Last();
            Assert.IsTrue(report.Scouted);
            Assert.IsTrue(report.DefenderVisible);
            Assert.AreEqual(20, report.DefenderTroops[(int)UnitType.Spearman]);
            Assert.AreEqual(0, report.DefenderLost.Sum(), "scouts don't fight the garrison");
            Assert.AreEqual(target.Level(BuildingType.TimberCamp), report.ScoutedLevels[(int)BuildingType.TimberCamp]);
        }

        [Test]
        public void LootThatDoesntFitAtHomeIsLost()
        {
            var world = WarWorld(out var home, out var target);
            home.Levels[(int)BuildingType.Warehouse] = 1;
            int cap = home.StorageCapacity;
            home.Wood = home.Clay = home.Iron = cap - 10;
            var command = world.Send(home, target, Army((UnitType.LightCavalry, 100)), CommandKind.Attack);
            world.AdvanceTo(command.ArriveTime);

            // The raiders fill their packs regardless of how much room there is at home...
            var report = world.Reports.Last();
            Assert.Greater(report.LootCapacity, 3 * target.StorageCapacity, "more room in the packs than the village has");
            Assert.Less(target.Wood, 1, "the village is cleaned out");
            Assert.Less(target.Iron, 1);
            Assert.Greater(report.Loot.Wood, 10);

            // ...and what doesn't fit in the warehouse when they get back is lost.
            var returning = world.Commands.Single(c => c.Kind == CommandKind.Return);
            world.AdvanceTo(returning.ArriveTime);
            Assert.AreEqual(cap, home.Wood, 1e-6);
            Assert.AreEqual(cap, home.Iron, 1e-6);
        }

        [Test]
        public void RamsDamageTheWallAndCatapultsTheChosenBuilding()
        {
            var world = WarWorld(out var home, out var target);
            target.Levels[(int)BuildingType.Wall] = 5;
            target.Levels[(int)BuildingType.Barracks] = 10;
            home.Troops[(int)UnitType.Ram] = 30;
            home.Troops[(int)UnitType.Catapult] = 40;
            int pointsBefore = target.Points;
            var command = world.Send(home, target, Army((UnitType.Axeman, 200), (UnitType.Ram, 30), (UnitType.Catapult, 40)),
                CommandKind.Attack, BuildingType.Barracks);

            world.AdvanceTo(command.ArriveTime);

            var report = world.Reports.Last();
            Assert.AreEqual(5, report.WallBefore);
            Assert.AreEqual(0, target.Level(BuildingType.Wall));
            Assert.AreEqual((int)BuildingType.Barracks, report.CatapultBuilding);
            Assert.AreEqual(10, report.CatapultBefore);
            Assert.Less(report.CatapultAfter, report.CatapultBefore);
            Assert.AreEqual(report.CatapultAfter, target.Level(BuildingType.Barracks));
            Assert.Less(target.Points, pointsBefore);
        }

        [Test]
        public void CatapultingTheWarehouseComesAfterTheLooting()
        {
            var world = WarWorld(out var home, out var target);
            target.Levels[(int)BuildingType.Warehouse] = 15;
            target.Wood = target.Clay = target.Iron = target.StorageCapacity;
            home.Troops[(int)UnitType.Catapult] = 200;
            var command = world.Send(home, target, Army((UnitType.LightCavalry, 100), (UnitType.Catapult, 200)),
                CommandKind.Attack, BuildingType.Warehouse);

            world.AdvanceTo(command.ArriveTime);

            var report = world.Reports.Last();
            Assert.AreEqual(report.LootCapacity, report.Loot.Wood + report.Loot.Clay + report.Loot.Iron, 3, "a full haul");
            Assert.Less(target.Level(BuildingType.Warehouse), 15);
            Assert.LessOrEqual(target.Wood, target.StorageCapacity, "what the smaller warehouse can't hold is gone");
        }

        [Test]
        public void CatapultsCantRazeTheHeadquarters()
        {
            var world = WarWorld(out var home, out var target);
            target.Levels[(int)BuildingType.Headquarters] = 3;
            home.Troops[(int)UnitType.Catapult] = 200;
            var command = world.Send(home, target, Army((UnitType.Axeman, 200), (UnitType.Catapult, 200)), CommandKind.Attack, BuildingType.Headquarters);

            world.AdvanceTo(command.ArriveTime);

            Assert.AreEqual(1, target.Level(BuildingType.Headquarters));
        }

        [Test]
        public void BattlesAreRepeatable()
        {
            BattleReport Run()
            {
                var world = WarWorld(out var home, out var target);
                target.Troops[(int)UnitType.Swordsman] = 40;
                var c = world.Send(home, target, Army((UnitType.Axeman, 120)), CommandKind.Attack);
                world.AdvanceTo(c.ArriveTime);
                return world.Reports.Last();
            }
            var a = Run();
            var b = Run();
            Assert.AreEqual(a.Luck, b.Luck);
            CollectionAssert.AreEqual(a.AttackerLost, b.AttackerLost);
            Assert.That(a.Luck, Is.InRange(-Battle.MaxLuck, Battle.MaxLuck));
        }

        [Test]
        public void SupportCanGoToAnyVillageAndBeRecalled()
        {
            var world = WarWorld(out var home, out var target);
            Assert.IsTrue(target.IsBarbarian);
            var command = world.Send(home, target, Army((UnitType.Axeman, 5)), CommandKind.Support);
            world.AdvanceTo(command.ArriveTime);

            Assert.AreEqual(1, target.Supports.Count);
            Assert.AreEqual(5, target.Supports[0].Troops[(int)UnitType.Axeman]);
            Assert.AreEqual(ReportKind.SupportArrived, world.Reports.Last().Kind);
            Assert.AreEqual(World.PopulationOf(Army((UnitType.Axeman, 5))), home.AwayPopulation);

            var back = world.Recall(target, home.Id);
            Assert.IsNotNull(back);
            world.AdvanceTo(back.ArriveTime);
            Assert.AreEqual(0, target.Supports.Count);
            Assert.AreEqual(300, home.TroopCount(UnitType.Axeman));
            Assert.AreEqual(0, home.AwayPopulation);
        }

        [Test]
        public void SupportFightsAlongsideTheDefenders()
        {
            var world = WarWorld(out var home, out var target);
            target.OwnerId = home.OwnerId;
            var support = world.Send(home, target, Army((UnitType.Axeman, 50)), CommandKind.Support);
            world.AdvanceTo(support.ArriveTime);

            // An enemy attacks the village the support is in.
            var enemy = world.Villages.Where(v => v.IsBarbarian && v != target).First();
            enemy.Troops[(int)UnitType.Axeman] = 400;
            var attack = world.Send(enemy, target, Army((UnitType.Axeman, 400)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);

            var defense = world.Reports.Last(r => r.Kind == ReportKind.Defense);
            Assert.AreEqual(20 + 50, defense.DefenderTroops[(int)UnitType.Spearman] + defense.DefenderTroops[(int)UnitType.Axeman]);
        }

        [Test]
        public void SupportInABarbarianVillageReportsTheAttackToItsOwner()
        {
            var world = WarWorld(out var home, out var target);
            var support = world.Send(home, target, Army((UnitType.Axeman, 50)), CommandKind.Support);
            world.AdvanceTo(support.ArriveTime);

            var enemy = world.Villages.Where(v => v.IsBarbarian && v != target).First();
            enemy.Troops[(int)UnitType.Axeman] = 400;
            var attack = world.Send(enemy, target, Army((UnitType.Axeman, 400)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);

            Assert.AreEqual(ReportKind.Defense, world.Reports.Last().Kind);
            Assert.AreEqual(50, world.Reports.Last().DefenderTroops[(int)UnitType.Axeman]);
        }

        [Test]
        public void IncomingAttacksOnThePlayerAreListed()
        {
            var world = WarWorld(out var home, out var target);
            target.Troops[(int)UnitType.Axeman] = 10;
            world.Send(target, home, Army((UnitType.Axeman, 10)), CommandKind.Attack);
            var incoming = world.IncomingAttacks(world.HumanPlayer.Id);
            Assert.AreEqual(1, incoming.Count);
            Assert.AreEqual(home.Id, incoming[0].ToVillageId);
        }

        [Test]
        public void DefendingAgainstAnAttackGivesTheDefenderAReport()
        {
            var world = WarWorld(out var home, out var target);
            target.Troops[(int)UnitType.Axeman] = 10;
            var attack = world.Send(target, home, Army((UnitType.Axeman, 10)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);

            var report = world.Reports.Last();
            Assert.AreEqual(ReportKind.Defense, report.Kind);
            Assert.IsTrue(report.PlayerWon, "300 axemen, 100 light cavalry and villagers beat 10 axemen");
            Assert.IsTrue(report.DefenderVisible);
        }

        [Test]
        public void ReportsTellWhatIsKnownAboutAVillage()
        {
            var world = WarWorld(out var home, out var target);
            Assert.IsNull(world.LatestScouting(target.Id), "nothing known yet");

            var scouts = world.Send(home, target, Army((UnitType.Scout, 3)), CommandKind.Attack);
            world.AdvanceTo(scouts.ArriveTime);
            var raid = world.Send(home, target, Army((UnitType.Axeman, 50)), CommandKind.Attack);
            world.AdvanceTo(raid.ArriveTime);

            var about = world.ReportsAbout(target.Id);
            Assert.AreEqual(2, about.Count);
            Assert.GreaterOrEqual(about[0].Time, about[1].Time, "newest first");
            Assert.AreEqual(scouts.ArriveTime, world.LatestScouting(target.Id).Time, 1e-6, "the scouting run saw the buildings");
            Assert.AreEqual(raid.ArriveTime, world.LatestSighting(target.Id).Time, 1e-6, "the raid saw the defenders last");
            Assert.AreEqual(home.OwnerId, about[0].AttackerPlayerId);
            Assert.AreEqual(-1, about[0].DefenderPlayerId, "barbarians");
        }

        [Test]
        public void ReportsCanBeReadAndDeleted()
        {
            var world = WarWorld(out var home, out var target);
            var c = world.Send(home, target, Army((UnitType.Scout, 1)), CommandKind.Attack);
            world.AdvanceTo(c.ArriveTime);
            Assert.AreEqual(1, world.UnreadReports);
            world.MarkAllReportsRead();
            Assert.AreEqual(0, world.UnreadReports);
            Assert.IsTrue(world.DeleteReport(world.Reports[0].Id));
            Assert.AreEqual(0, world.Reports.Count);
        }
    }

    public class BarbarianTroopTests
    {
        [Test]
        public void BarbariansHaveNoTroopsAndNeverTrainAny()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 8, Speed = 1f });
            Assert.IsTrue(world.Villages.Where(v => v.IsBarbarian).All(v => v.TroopPopulation == 0));

            world.AdvanceTo(world.Now + 5 * World.SecondsPerDay);

            Assert.IsTrue(world.Villages.Where(v => v.IsBarbarian).All(v => v.TroopPopulation == 0), "growing doesn't bring troops");
        }

        [Test]
        public void OlderSavesDisbandBarbarianGarrisons()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 8 });
            var barb = world.Villages.First(v => v.IsBarbarian);
            barb.Troops[(int)UnitType.Spearman] = 40;
            world.UpgradeFrom(6);
            Assert.AreEqual(0, barb.TroopCount(UnitType.Spearman));
        }
    }
}
