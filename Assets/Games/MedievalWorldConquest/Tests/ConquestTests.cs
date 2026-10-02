using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class ConquestTests
    {
        /// <summary>A world with no rivals or protection, the player's village built up with an academy and an army.</summary>
        static World ConquestWorld(out Village home, out Village target, float rivals = 0)
        {
            var world = World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = rivals, ProtectionDays = 0 });
            home = world.PlayerVillage;
            home.Levels[(int)BuildingType.Headquarters] = 20;
            home.Levels[(int)BuildingType.Barracks] = 10;
            home.Levels[(int)BuildingType.Academy] = 1;
            home.Levels[(int)BuildingType.Farm] = 28;
            home.Troops[(int)UnitType.Axeman] = 200;
            home.Troops[(int)UnitType.Nobleman] = 6;
            // Barbarians are rare early on; if there are none yet, a noob gives up and leaves one.
            if (!world.Villages.Any(v => v.IsBarbarian)) world.QuitLord(world.Players.First(p => p.Personality == AiPersonality.Noob));
            var h = home;
            target = world.Villages.Where(v => v.IsBarbarian).OrderBy(v => World.Distance(h, v)).First();
            return world;
        }

        static int[] Army(params (UnitType type, int count)[] units)
        {
            var troops = new int[Units.Count];
            foreach (var (type, count) in units) troops[(int)type] = count;
            return troops;
        }

        [Test]
        public void TheAcademyIsOneDearLevelWorth512Points()
        {
            var academy = Buildings.Get(BuildingType.Academy);
            Assert.AreEqual(1, academy.MaxLevel);
            Assert.AreEqual(512, Buildings.PointsAtLevel(BuildingType.Academy, 1));

            var world = World.CreateNew(new WorldSettings { RivalDensity = 0 });
            var v = world.PlayerVillage;
            Assert.AreEqual(BuildStatus.NeedsBuilding, world.CheckBuild(v, BuildingType.Academy).Status);
            // As in Tribal Wars: Headquarters 20, Smithy 20 and Market 10.
            v.Levels[(int)BuildingType.Headquarters] = 19;
            v.Levels[(int)BuildingType.Smithy] = 20;
            v.Levels[(int)BuildingType.Market] = 10;
            Assert.AreEqual(BuildStatus.NeedsBuilding, world.CheckBuild(v, BuildingType.Academy).Status, "Headquarters 20");
            v.Levels[(int)BuildingType.Headquarters] = 20;
            v.Levels[(int)BuildingType.Market] = 9;
            Assert.AreEqual(BuildingType.Market, world.CheckBuild(v, BuildingType.Academy).Required.Building, "Market 10");
            v.Levels[(int)BuildingType.Market] = 10;
            v.Levels[(int)BuildingType.Headquarters] = 20;
            Assert.AreNotEqual(BuildStatus.NeedsBuilding, world.CheckBuild(v, BuildingType.Academy).Status);
            var cost = Buildings.CostOf(BuildingType.Academy, 1);
            Assert.AreEqual((15000, 25000, 10000), (cost.Wood, cost.Clay, cost.Iron));
            Assert.AreEqual(RecruitStatus.NeedsBuilding, world.CheckRecruit(v, UnitType.Nobleman, 1).Status, "noblemen need the academy");
        }

        [Test]
        public void ANoblemanWhoSurvivesLowersLoyalty()
        {
            var world = ConquestWorld(out var home, out var target);
            var attack = world.Send(home, target, Army((UnitType.Axeman, 50), (UnitType.Nobleman, 1)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);

            var report = world.Reports.Last();
            Assert.AreEqual(100, report.LoyaltyBefore);
            Assert.That(report.LoyaltyAfter, Is.InRange(100 - World.NobleLoyaltyMax, 100 - World.NobleLoyaltyMin));
            Assert.AreEqual(report.LoyaltyAfter, target.Loyalty, 1);
            Assert.IsTrue(target.IsBarbarian, "not yet won over");
            Assert.AreEqual(1, world.Commands.Single(c => c.Kind == CommandKind.Return).Troops[(int)UnitType.Nobleman], "the nobleman comes home");
        }

        [Test]
        public void LoyaltyComesBackAPointAnHour()
        {
            var world = ConquestWorld(out _, out var target);
            target.Loyalty = 50;
            world.AdvanceTo(world.Now + 10 * 3600);
            Assert.AreEqual(60, target.Loyalty, 1e-6);
            world.AdvanceTo(world.Now + 100 * 3600);
            Assert.AreEqual(World.MaxLoyalty, target.Loyalty, 1e-6, "no higher than 100");
        }

        [Test]
        public void SeveralNoblemenInOneAttackSwayItOnlyOnce()
        {
            var world = ConquestWorld(out var home, out var target);
            var attack = world.Send(home, target, Army((UnitType.Axeman, 100), (UnitType.Nobleman, 5)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);
            Assert.That(target.Loyalty, Is.InRange(100 - World.NobleLoyaltyMax, 100 - World.NobleLoyaltyMin), "it takes a train");
            Assert.IsTrue(target.IsBarbarian);
        }

        [Test]
        public void ANoblemanWhoBringsLoyaltyToZeroWinsTheVillageOver()
        {
            var world = ConquestWorld(out var home, out var target);
            target.Wood = target.Clay = target.Iron = 400;
            target.Loyalty = 15; // (worn down by the noblemen before him)
            var attack = world.Send(home, target, Army((UnitType.Axeman, 100), (UnitType.Nobleman, 1)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);

            var report = world.Reports.Last();
            Assert.IsTrue(report.Conquered);
            Assert.AreEqual(home.OwnerId, target.OwnerId);
            Assert.AreNotEqual(World.BarbarianName, target.Name, "a won-over barbarian village gets a proper name");
            Assert.AreEqual(World.LoyaltyAfterConquest, target.Loyalty, 1e-6);
            // The survivors stay to guard it, as support from the village they came from (less the nobleman who
            // now rules it); nobody marches home until sent.
            int axes = 100 - report.AttackerLost[(int)UnitType.Axeman];
            var guard = target.Supports.Single(g => g.FromVillageId == home.Id);
            Assert.AreEqual(axes, guard.Troops[(int)UnitType.Axeman]);
            Assert.AreEqual(0, guard.Troops[(int)UnitType.Nobleman], "the nobleman rules it now");
            Assert.AreEqual(0, target.TroopCount(UnitType.Axeman), "they aren't the new village's own troops");
            Assert.IsFalse(world.Commands.Any(c => c.Kind == CommandKind.Return));
            Assert.AreEqual(axes * Units.Get(UnitType.Axeman).Cost.Population, home.AwayPopulation, "they count against their own village's farm, not the new one's");
            Assert.IsNotNull(world.Recall(target, home.Id), "and go home when sent");
            Assert.AreEqual(0, report.Loot.Wood, "the stores stay: they're the conqueror's now");
            Assert.GreaterOrEqual(target.Wood, 400);
            Assert.AreEqual(2, world.HumanVillages().Count);
            Assert.IsFalse(target.IsBarbarian);
        }

        [Test]
        public void LoyaltyBelowOneIsAVillageWonOver()
        {
            // Loyalty grows back a bit at a time: a nobleman taking at least the least he can from that plus a half
            // leaves under 1, which is shown as 0, so the village is won (no report may say "0" of a village that held).
            var world = ConquestWorld(out var home, out var target);
            target.Loyalty = World.NobleLoyaltyMin + 0.5;
            var attack = world.Send(home, target, Army((UnitType.Axeman, 100), (UnitType.Nobleman, 1)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);
            Assert.IsTrue(world.Reports.Last().Conquered);
            Assert.AreEqual(home.OwnerId, target.OwnerId);
        }

        [Test]
        public void AConqueredVillagesTroopsElsewhereAreGoneWithIt()
        {
            var world = ConquestWorld(out var home, out var target, rivals: 1);
            // A lord's village, with 25 light cavalry of its own stationed in the lord's other village.
            var lord = world.Players.First(p => !p.IsHuman && p.Personality != AiPersonality.Inactive && world.VillagesOf(p.Id).Count > 0);
            var theirs = world.VillagesOf(lord.Id)[0];
            var elsewhere = world.Villages.First(v => v != theirs && v != home);
            var stationed = new int[Units.Count];
            stationed[(int)UnitType.LightCavalry] = 25;
            elsewhere.Supports.Add(new SupportGroup { FromVillageId = theirs.Id, OwnerId = lord.Id, Troops = stationed });
            lord.ProtectedUntil = 0;
            System.Array.Clear(theirs.Troops, 0, theirs.Troops.Length);
            theirs.Loyalty = 10;
            var attack = world.Send(home, theirs, Army((UnitType.Axeman, 100), (UnitType.Nobleman, 1)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);
            Assert.AreEqual(home.OwnerId, theirs.OwnerId, "won over");
            Assert.IsFalse(elsewhere.Supports.Any(g => g.FromVillageId == theirs.Id && g.OwnerId == lord.Id), "its troops elsewhere are gone");
        }

        [Test]
        public void ALostAttackSwaysNobody()
        {
            var world = ConquestWorld(out var home, out var target);
            target.Troops[(int)UnitType.Spearman] = 2000;
            var attack = world.Send(home, target, Army((UnitType.Axeman, 20), (UnitType.Nobleman, 5)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);
            Assert.AreEqual(World.MaxLoyalty, target.Loyalty, 1e-6);
            Assert.IsTrue(target.IsBarbarian);
            Assert.AreEqual(-1, world.Reports.Last().LoyaltyBefore);
        }

        [Test]
        public void ThePlayerCanSwitchBetweenTheirVillages()
        {
            var world = ConquestWorld(out var home, out var target);
            Assert.IsFalse(world.SelectVillage(target.Id), "not theirs yet");
            target.Loyalty = 15;
            var attack = world.Send(home, target, Army((UnitType.Axeman, 100), (UnitType.Nobleman, 1)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);

            Assert.AreSame(home, world.PlayerVillage);
            Assert.IsTrue(world.SelectVillage(target.Id));
            Assert.AreSame(target, world.PlayerVillage);
            Assert.IsTrue(world.SelectVillage(home.Id));
            Assert.AreSame(home, world.PlayerVillage);
        }

        [Test]
        public void ReachingTheConquestGoalWins()
        {
            var world = ConquestWorld(out var home, out var target, rivals: 1);
            foreach (var b in world.Villages.Where(v => v.IsBarbarian)) b.Troops = new int[Units.Count];
            // The goal counts every village in the realm: holding two of them will do here.
            world.Settings.ConquestGoal = 1.5f / world.GoalVillageCount;
            Assert.Less(world.HumanShare, world.Settings.ConquestGoal);
            Assert.IsFalse(world.Won);
            target.Loyalty = 15;
            var attack = world.Send(home, target, Army((UnitType.Axeman, 100), (UnitType.Nobleman, 1)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);
            Assert.IsTrue(world.Won);
            Assert.Greater(world.HumanShare, world.Settings.ConquestGoal);
        }

        [Test]
        public void LosingTheLastVillageLetsThePlayerStartAgainOnTheFrontier()
        {
            var world = ConquestWorld(out var home, out _, rivals: 1);
            var lord = world.Players.First(p => !p.IsHuman && !p.Quit && p.Personality != AiPersonality.Inactive);
            var camp = world.Villages.First(v => v.OwnerId == lord.Id);
            camp.Levels[(int)BuildingType.Farm] = 28;
            camp.Troops[(int)UnitType.Axeman] = 500;
            camp.Troops[(int)UnitType.Nobleman] = 5;
            home.Troops[(int)UnitType.Axeman] = 0;
            home.Troops[(int)UnitType.Nobleman] = 0;

            home.Loyalty = 15;
            var attack = world.Send(camp, home, Army((UnitType.Axeman, 500), (UnitType.Nobleman, 1)), CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);

            Assert.AreEqual(lord.Id, home.OwnerId);
            Assert.IsTrue(world.HumanDefeated);
            Assert.IsNull(world.PlayerVillage);
            var defense = world.Reports.Last();
            Assert.AreEqual(ReportKind.Defense, defense.Kind, "the player hears about losing it");
            Assert.IsTrue(defense.Conquered);

            world.Settings.ProtectionDays = 3; // (this test's world started without it)
            var fresh = world.RespawnHuman();
            Assert.IsNotNull(fresh);
            Assert.IsFalse(world.HumanDefeated);
            Assert.AreSame(fresh, world.PlayerVillage);
            Assert.IsTrue(world.IsProtected(world.HumanPlayer.Id), "a fresh start comes with protection");
        }

        [Test]
        public void ALordWithNoblemenWinsOverABarbarianVillage()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 1, ProtectionDays = 0 });
            var lord = world.Players.First(p => !p.IsHuman && p.Personality != AiPersonality.Noob && p.Personality != AiPersonality.Inactive);
            var camp = world.Villages.First(v => v.OwnerId == lord.Id);
            camp.Levels[(int)BuildingType.Headquarters] = 20;
            camp.Levels[(int)BuildingType.Barracks] = 10;
            camp.Levels[(int)BuildingType.Academy] = 1;
            camp.Levels[(int)BuildingType.Farm] = 28;
            camp.Troops[(int)UnitType.Axeman] = 150;
            camp.Troops[(int)UnitType.Nobleman] = World.AiNoblesWanted;

            world.AdvanceTo(world.Now + 3 * World.SecondsPerDay);

            Assert.Greater(world.Villages.Count(v => v.OwnerId == lord.Id), 1, "the lord should have won a village over");
        }

        [Test]
        public void ALordWithAnAcademyTrainsNoblemen()
        {
            var world = World.CreateNew(new WorldSettings { Seed = 6, Speed = 1f, RivalDensity = 1 });
            var lord = world.Players.First(p => !p.IsHuman && p.Personality != AiPersonality.Noob && p.Personality != AiPersonality.Inactive);
            var camp = world.Villages.First(v => v.OwnerId == lord.Id);
            camp.Levels[(int)BuildingType.Headquarters] = 20;
            camp.Levels[(int)BuildingType.Barracks] = 10;
            camp.Levels[(int)BuildingType.Academy] = 1;
            camp.Levels[(int)BuildingType.Warehouse] = 22;
            camp.Levels[(int)BuildingType.Farm] = 25;
            camp.Wood = camp.Clay = camp.Iron = 60000;

            world.AdvanceTo(world.Now + World.SecondsPerDay);

            int nobles = camp.TroopCount(UnitType.Nobleman) + camp.Recruitment.Where(o => o.Unit == UnitType.Nobleman).Sum(o => o.Remaining);
            Assert.Greater(nobles, 0);
        }

        [Test]
        public void OlderSavesStartEveryVillageFullyLoyal()
        {
            var world = World.CreateNew(new WorldSettings { RivalDensity = 0 });
            foreach (var v in world.Villages) v.Loyalty = 0; // as loaded from a save without the field
            world.UpgradeFrom(9);
            Assert.IsTrue(world.Villages.All(v => v.Loyalty == World.MaxLoyalty));
            Assert.AreSame(world.Villages[0], world.PlayerVillage);
        }
    }
}
