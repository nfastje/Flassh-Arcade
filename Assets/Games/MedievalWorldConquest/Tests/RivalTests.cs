using System;
using System.Collections.Generic;
using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    public class RivalTests
    {
        static World NewWorld(float density = 1f, AiSkill skill = AiSkill.Normal, int seed = 1234) =>
            World.CreateNew(new WorldSettings { Seed = seed, Speed = 1f, RivalDensity = density, RivalSkill = skill, PlayerName = "Tester" });

        static List<Player> Lords(World world) => world.Players.Where(p => !p.IsHuman).ToList();

        static Village HomeOf(World world, Player p) => world.Villages.First(v => v.OwnerId == p.Id);

        static double FromCentre(Village v) =>
            Math.Sqrt((v.X - World.MapSize / 2.0) * (v.X - World.MapSize / 2.0) + (v.Y - World.MapSize / 2.0) * (v.Y - World.MapSize / 2.0));

        [Test]
        public void ThePlayerIsCalledWhatTheyChose()
        {
            Assert.AreEqual("Tester", NewWorld().HumanPlayer.Name);
            Assert.AreEqual(World.DefaultPlayerName, World.CreateNew(new WorldSettings { PlayerName = "  " }).HumanPlayer.Name);
        }

        [Test]
        public void TheFirstLordsSettleRoundTheStartingCircle()
        {
            var world = NewWorld();
            var lords = Lords(world);
            Assert.GreaterOrEqual(lords.Count, World.InitialLords);
            Assert.AreEqual(lords.Count, lords.Select(p => p.Name).Distinct().Count(), "every lord has its own name");
            Assert.AreEqual(4, lords.Take(4).Select(p => p.Personality).Distinct().Count(), "the first four have all four personalities");
            foreach (var p in lords)
                Assert.LessOrEqual(FromCentre(HomeOf(world, p)), World.StartRadius + World.RingSpread + 1);

            var villages = world.Villages.Where(v => !v.IsBarbarian).ToList();
            Assert.AreEqual(villages.Count, villages.Select(v => v.Name).Distinct().Count(), "every village has its own name");
        }

        [Test]
        public void MoreLordsArriveAsTheWorldGrows()
        {
            var world = NewWorld();
            int first = Lords(world).Count;
            world.AdvanceTo(world.Now + 12 * World.SecondsPerDay);

            var lords = Lords(world);
            Assert.Greater(lords.Count, first + 3, "no fixed number: newcomers keep settling");
            var newest = lords.Last();
            Assert.Greater(FromCentre(HomeOf(world, newest)), World.StartRadius + World.RingSpread, "newcomers settle on the frontier");
            Assert.Greater(newest.ProtectedUntil, world.ProtectionEnd, "each newcomer gets its own protection");
            Assert.IsTrue(world.IsProtected(newest.Id) || world.Now > newest.ProtectedUntil);
        }

        [Test]
        public void MoreDensityMeansMoreLords()
        {
            var few = NewWorld(0.5f);
            var many = NewWorld(2f);
            few.AdvanceTo(few.Now + 10 * World.SecondsPerDay);
            many.AdvanceTo(many.Now + 10 * World.SecondsPerDay);
            Assert.Greater(Lords(many).Count, Lords(few).Count * 2);
        }

        [Test]
        public void NoRivalsIfNoneAreChosen()
        {
            var world = NewWorld(0f);
            world.AdvanceTo(world.Now + 5 * World.SecondsPerDay);
            Assert.AreEqual(1, world.Players.Count);
        }

        [Test]
        public void EveryoneStartsUnderBeginnerProtection()
        {
            var world = NewWorld();
            var lord = Lords(world)[0];
            Assert.IsTrue(world.IsProtected(world.HumanPlayer.Id));
            Assert.IsTrue(world.IsProtected(lord.Id));

            // Nobody can attack a protected player's village, though barbarians and support are fine.
            var home = world.PlayerVillage;
            home.Troops[(int)UnitType.Spearman] = 10;
            var army = new int[Units.Count];
            army[(int)UnitType.Spearman] = 5;
            Assert.AreEqual(SendStatus.TargetProtected, world.CheckSend(home, HomeOf(world, lord), army, CommandKind.Attack).Status);
            Assert.AreEqual(SendStatus.Ok, world.CheckSend(home, HomeOf(world, lord), army, CommandKind.Support).Status);
            var barbarian = world.Villages.First(v => v.IsBarbarian);
            Assert.AreEqual(SendStatus.Ok, world.CheckSend(home, barbarian, army, CommandKind.Attack).Status);

            world.AdvanceTo(world.ProtectionEnd + 1);
            Assert.IsFalse(world.IsProtected(lord.Id));
            Assert.AreEqual(SendStatus.Ok, world.CheckSend(home, HomeOf(world, lord), army, CommandKind.Attack).Status);
        }

        [Test]
        public void NoOneAttacksThePlayerDuringProtection()
        {
            var world = NewWorld(skill: AiSkill.Hard);
            int attacks = 0;
            world.EventApplied += e => { if (e.Kind == EventKind.CommandArrives && e.VillageId == world.PlayerVillage.Id) attacks++; };
            world.AdvanceTo(world.ProtectionEnd - 60);
            Assert.AreEqual(0, attacks);
            Assert.IsEmpty(world.IncomingAttacks(world.HumanPlayer.Id));
        }

        [Test]
        public void LordsBuildUpTheirVillagesAndArmies()
        {
            var world = NewWorld();
            var first = Lords(world);
            world.AdvanceTo(world.Now + 6 * World.SecondsPerDay);
            foreach (var lord in first)
            {
                var v = HomeOf(world, lord);
                Assert.Greater(v.Points, 80, $"{lord.Name} ({lord.Personality}) should have grown");
                Assert.Greater(v.Level(BuildingType.Barracks), 0, $"{lord.Name} should have a barracks");
                Assert.Greater(lord.SpentOnTroops, 0, $"{lord.Name} should have trained troops");
                Assert.LessOrEqual(v.PopulationUsed, v.PopulationCapacity, "lords keep to the farm's limit like anyone");
            }
        }

        [Test]
        public void LordsRaidBarbariansForResources()
        {
            var world = NewWorld();
            int raids = 0;
            world.EventApplied += e =>
            {
                var target = world.FindVillage(e.VillageId);
                if (e.Kind == EventKind.CommandArrives && target != null && target.IsBarbarian) raids++;
            };
            world.AdvanceTo(world.Now + 10 * World.SecondsPerDay);
            Assert.Greater(raids, 10);
            Assert.IsTrue(Lords(world).Any(p => p.Notes.Any(n => world.FindVillage(n.VillageId).IsBarbarian && n.NextRaidAt > 0)));
        }

        [Test]
        public void AWarlikeLordAttacksAWeakNeighbourOnceProtectionEnds()
        {
            var world = NewWorld();
            var lord = Lords(world).First(p => p.Personality == AiPersonality.Warlord);
            var camp = HomeOf(world, lord);
            // A ready-made army, and every other lord off limits, so the player is the only target.
            camp.Levels[(int)BuildingType.Barracks] = 5;
            camp.Levels[(int)BuildingType.Farm] = 20;
            camp.Troops[(int)UnitType.Axeman] = 400;
            foreach (var other in Lords(world).Where(p => p != lord)) other.ProtectedUntil = double.MaxValue;

            world.AdvanceTo(world.ProtectionEnd + 12 * 3600);

            var incoming = world.IncomingAttacks(world.HumanPlayer.Id);
            bool landed = world.Reports.Any(r => r.Kind == ReportKind.Defense);
            Assert.IsTrue(incoming.Any(c => c.OwnerId == lord.Id) || landed, "the warlord should have come for the player");
            if (landed) Assert.AreEqual(lord.Name, world.Reports.First(r => r.Kind == ReportKind.Defense).AttackerPlayer);
        }

        [Test]
        public void LordsRememberWhatTheirBattlesShowedThem()
        {
            var world = NewWorld();
            var lord = Lords(world)[0];
            var camp = HomeOf(world, lord);
            var target = world.PlayerVillage;
            foreach (var p in world.Players) p.ProtectedUntil = 0;
            target.Troops[(int)UnitType.Swordsman] = 300;
            camp.Troops[(int)UnitType.Axeman] = 20;
            var army = new int[Units.Count];
            army[(int)UnitType.Axeman] = 20;

            var attack = world.Send(camp, target, army, CommandKind.Attack);
            world.AdvanceTo(attack.ArriveTime);

            var note = lord.Notes.Single(n => n.VillageId == target.Id);
            Assert.Greater(note.AvoidUntil, world.Now, "a failed attack keeps the lord away for a while");
            var report = world.Reports.Last();
            Assert.AreEqual(ReportKind.Defense, report.Kind);
            Assert.AreEqual(lord.Name, report.AttackerPlayer);
            Assert.AreEqual("Tester", report.DefenderPlayer);
        }

        [Test]
        public void TheSameSeedPlaysOutTheSameWay()
        {
            int[] Run()
            {
                var world = NewWorld(seed: 77);
                world.AdvanceTo(world.Now + 5 * World.SecondsPerDay);
                return world.Villages.Select(v => v.Points * 1000 + v.OwnerId).ToArray();
            }
            CollectionAssert.AreEqual(Run(), Run());
        }

        [Test]
        public void RankingsPutTheBiggestFirst()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 4 * World.SecondsPerDay);
            var rankings = world.Rankings();
            Assert.AreEqual(world.Players.Count, rankings.Count);
            for (int i = 1; i < rankings.Count; i++) Assert.GreaterOrEqual(rankings[i - 1].Points, rankings[i].Points);
            Assert.IsTrue(rankings.All(r => r.Villages == 1));
            Assert.AreEqual(world.PlayerVillage.Points, rankings.Single(r => r.Player.IsHuman).Points);
        }

        [Test]
        public void OlderWorldsWithoutRivalsGetTheirFirstLordsAndAFreshSpellOfProtection()
        {
            var world = NewWorld(0f);
            world.AdvanceTo(world.Now + 10 * World.SecondsPerDay);
            int shift = (World.MapSize - 100) / 2;
            foreach (var v in world.Villages)
            {
                v.X -= shift;
                v.Y -= shift;
            }
            world.Settings.RivalDensity = 1; // what an older save's settings load as (the field didn't exist)

            world.UpgradeFrom(7);

            Assert.AreEqual(World.InitialLords, Lords(world).Count);
            Assert.IsTrue(world.IsProtected(world.HumanPlayer.Id));
            Assert.IsTrue(Lords(world).All(p => world.IsProtected(p.Id)));
            Assert.IsTrue(world.Events.Pending.Any(e => e.Kind == EventKind.AiThink));
        }
    }
}
