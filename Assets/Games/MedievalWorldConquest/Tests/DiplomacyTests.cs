using System.Linq;
using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>Tribes, diplomacy and messages (a world option).</summary>
    public class DiplomacyTests
    {
        static World NewWorld(bool diplomacy = true) =>
            World.CreateNew(new WorldSettings { Seed = 1234, Speed = 1f, RivalDensity = 1, Diplomacy = diplomacy, ProtectionDays = 0 });

        static Player[] Lords(World w) => w.Players.Where(p => !p.IsHuman && p.Personality != AiPersonality.Inactive && !p.Quit).ToArray();

        static Village HomeOf(World w, Player p) => w.VillagesOf(p.Id)[0];

        static int[] Army(UnitType type, int count)
        {
            var troops = new int[Units.Count];
            troops[(int)type] = count;
            return troops;
        }

        [Test]
        public void WithoutDiplomacyNoTribesForm()
        {
            var world = NewWorld(diplomacy: false);
            world.AdvanceTo(world.Now + 40 * World.SecondsPerDay);
            Assert.IsEmpty(world.Tribes);
            Assert.IsFalse(world.Events.Pending.Any(e => e.Kind == EventKind.TribeTick));
        }

        [Test]
        public void LordsBandTogetherIntoTribes()
        {
            var world = NewWorld();
            world.AdvanceTo(world.Now + 40 * World.SecondsPerDay);
            var tribes = world.ActiveTribes();
            Assert.IsNotEmpty(tribes);
            foreach (var t in tribes)
            {
                Assert.LessOrEqual(t.Members.Count, World.MaxTribeMembers);
                Assert.IsTrue(t.Members.Contains(t.LeaderId), "the leader is a member");
                Assert.IsTrue(t.Members.All(id => world.FindPlayer(id).TribeId == t.Id));
                Assert.IsFalse(t.Members.Any(id => world.FindPlayer(id).Personality == AiPersonality.Inactive), "inactive players don't join");
            }
        }

        [Test]
        public void TribeMatesLeaveEachOtherAlone()
        {
            var world = NewWorld();
            var lords = Lords(world).Where(p => p.Personality != AiPersonality.Noob).ToArray();
            var (a, b) = (lords[0], lords[1]);
            var tribe = world.FoundTribe(a, "The Test", "TT");
            world.JoinTribe(b, tribe);
            var home = HomeOf(world, a);
            home.Troops[(int)UnitType.Axeman] = 2000;
            home.Troops[(int)UnitType.LightCavalry] = 300;
            home.Troops[(int)UnitType.Scout] = 10;
            world.AdvanceTo(world.Now + 3 * World.SecondsPerDay);
            Assert.IsFalse(world.Commands.Any(c => c.OwnerId == a.Id && c.Kind == CommandKind.Attack && world.FindVillage(c.ToVillageId).OwnerId == b.Id));
            Assert.IsFalse(a.Notes.Any(n => world.FindVillage(n.VillageId)?.OwnerId == b.Id && n.NextRaidAt > 0), "never even raided");
        }

        [Test]
        public void TheHumanCanFoundATribeAndLordsAnswerInvitations()
        {
            var world = NewWorld();
            var human = world.HumanPlayer;
            var tribe = world.FoundTribe(human, "Player's Pride", "pp");
            Assert.AreEqual("PP", tribe.Tag);
            Assert.AreEqual(human.Id, tribe.LeaderId);
            var lord = Lords(world).OrderBy(p => World.Distance(HomeOf(world, p), world.PlayerVillage)).First();
            Assert.IsTrue(world.InviteToTribe(lord));
            world.AdvanceTo(world.Now + World.TribeTickHours * 3600 + 1);
            Assert.IsTrue(world.Messages.Any(m => m.FromPlayerId == lord.Id), "the lord answers, yes or no");
        }

        [Test]
        public void AcceptingAnInvitationJoinsTheTribe()
        {
            var world = NewWorld();
            var lord = Lords(world)[0];
            var theirs = world.FoundTribe(lord, "Their Tribe", "TH");
            // As an invitation would arrive.
            var human = world.HumanPlayer;
            world.AdvanceTo(world.Now + 1);
            var m = new Message { Id = world.NextMessageId++, Kind = MessageKind.Invitation, A = theirs.Id, FromPlayerId = lord.Id };
            world.Messages.Add(m);
            Assert.IsTrue(world.AnswerMessage(m.Id, true));
            Assert.AreEqual(theirs.Id, human.TribeId);
            Assert.IsTrue(m.Answered);
        }

        [Test]
        public void AttackingATribeMateGetsTheHumanExpelled()
        {
            var world = NewWorld();
            var lord = Lords(world)[0];
            var tribe = world.FoundTribe(lord, "Their Tribe", "TH");
            var human = world.HumanPlayer;
            world.JoinTribe(human, tribe);
            world.PlayerVillage.Troops[(int)UnitType.Spearman] = 10;
            Assert.IsNotNull(world.Send(world.PlayerVillage, HomeOf(world, lord), Army(UnitType.Spearman, 5), CommandKind.Attack));
            Assert.AreEqual(-1, human.TribeId, "out of the tribe");
            Assert.Less(human.Reputation, 0);
        }

        [Test]
        public void BreakingAPactMakesWar()
        {
            var world = NewWorld();
            var lords = Lords(world);
            var human = world.HumanPlayer;
            var mine = world.FoundTribe(human, "Mine", "MI");
            var theirs = world.FoundTribe(lords[0], "Theirs", "TH");
            world.SetRelation(mine, theirs, RelationKind.NonAggression);
            world.PlayerVillage.Troops[(int)UnitType.Spearman] = 10;
            world.Send(world.PlayerVillage, HomeOf(world, lords[0]), Army(UnitType.Spearman, 5), CommandKind.Attack);
            Assert.AreEqual(RelationKind.Enemy, world.Relation(mine, theirs));
            Assert.Less(human.Reputation, 0);
        }

        [Test]
        public void TribeMatesSendSupportWhenTheHumanIsAttacked()
        {
            var world = NewWorld();
            var human = world.HumanPlayer;
            var home = world.PlayerVillage;
            var lords = Lords(world).OrderBy(p => World.Distance(HomeOf(world, p), home)).ToArray();
            var (friend, enemy) = (lords[0], lords[lords.Length - 1]);
            var tribe = world.FoundTribe(friend, "Friends", "FR");
            world.JoinTribe(human, tribe);
            HomeOf(world, friend).Troops[(int)UnitType.Spearman] = 500;
            // An attack from far away (so there's time to help).
            var far = HomeOf(world, enemy);
            far.Troops[(int)UnitType.Ram] = 50;
            Assert.IsNotNull(world.Send(far, home, Army(UnitType.Ram, 50), CommandKind.Attack));
            Assert.IsNotEmpty(tribe.HelpCalls, "the tribe is asked to help");

            world.AdvanceTo(world.Now + 3 * 3600);
            Assert.IsTrue(world.Commands.Any(c => c.Kind == CommandKind.Support && c.OwnerId == friend.Id && c.ToVillageId == home.Id)
                          || home.Supports.Any(g => g.OwnerId == friend.Id), "the tribe mate came to help");
        }

        [Test]
        public void AnAttackOnATribeMateAsksTheHumanForHelp()
        {
            var world = NewWorld();
            var human = world.HumanPlayer;
            var lords = Lords(world);
            var tribe = world.FoundTribe(human, "Mine", "MI");
            world.JoinTribe(lords[0], tribe);
            var target = HomeOf(world, lords[0]);
            // The attacker furthest off, so the human's cavalry can get there first.
            var attacker = lords.Skip(1).Select(l => HomeOf(world, l)).OrderByDescending(v => World.Distance(v, target)).First();
            world.PlayerVillage.Troops[(int)UnitType.HeavyCavalry] = 20;
            attacker.Troops[(int)UnitType.Axeman] = 100;
            attacker.Troops[(int)UnitType.Nobleman] = 1;

            // An attack at infantry speed is left to the village: no message.
            world.Send(attacker, target, Army(UnitType.Axeman, 100), CommandKind.Attack);
            Assert.IsFalse(world.Messages.Any(m => m.Kind == MessageKind.SupportRequest));
            // Noblemen coming (nobleman speed), and the human can get there in time: they're asked.
            world.Send(attacker, target, Army(UnitType.Nobleman, 1), CommandKind.Attack);
            var request = world.Messages.LastOrDefault(m => m.Kind == MessageKind.SupportRequest);
            Assert.IsNotNull(request);
            Assert.AreEqual(target.Id, request.A);

            double before = human.Satisfaction;
            world.PlayerVillage.Troops[(int)UnitType.Spearman] = 50;
            world.Send(world.PlayerVillage, HomeOf(world, lords[0]), Army(UnitType.Spearman, 50), CommandKind.Support);
            Assert.Greater(human.Satisfaction, before, "the tribe notices who helps");
        }

        [Test]
        public void ATribeAndItsAlliesCanWinTogether()
        {
            var world = NewWorld();
            var human = world.HumanPlayer;
            var lords = Lords(world);
            var mine = world.FoundTribe(human, "Mine", "MI");
            world.JoinTribe(lords[0], mine);
            var friends = world.FoundTribe(lords[1], "Friends", "FR");
            world.SetRelation(mine, friends, RelationKind.Ally);

            // On a diplomacy world the goal is a share of every village, barbarians' included.
            int all = world.Villages.Count;
            int ours = world.Villages.Count(v => v.OwnerId == human.Id || v.OwnerId == lords[0].Id || v.OwnerId == lords[1].Id);
            Assert.AreEqual(ours / (double)all, world.BlocShare(mine), 1e-9);

            // A goal that the human alone doesn't reach, but the bloc does: held for long enough, it wins. (The world
            // is locked so no new villages dilute it while it waits.)
            world.WorldLocked = true;
            world.Settings.ConquestGoal = (float)((world.HumanShare + world.BlocShare(mine)) / 2);
            world.AdvanceTo(world.Now + World.TribeTickHours * 3600 + 1);
            Assert.IsFalse(world.Won, "not at once: the goal has to be held");
            Assert.IsTrue(world.IsHumanSide(world.HoldTribeId));
            Assert.IsTrue(world.Messages.Any(m => m.Subject.Contains("within your grasp")));
            world.AdvanceTo(world.HoldEnds + World.TribeTickHours * 3600);
            Assert.IsTrue(world.Won);
            Assert.IsFalse(world.LostToBloc);
        }

        [Test]
        public void ABlocWithoutTheHumanCanTakeTheWorld()
        {
            var world = NewWorld();
            var lords = Lords(world);
            var theirs = world.FoundTribe(lords[0], "Theirs", "TH");
            world.JoinTribe(lords[1], theirs);
            world.WorldLocked = true;
            world.Settings.ConquestGoal = (float)(world.BlocShare(theirs) * 0.7);
            world.AdvanceTo(world.Now + World.TribeTickHours * 3600 + 1);
            Assert.IsFalse(world.LostToBloc, "they have to hold it first");
            Assert.IsTrue(world.Messages.Any(m => m.Subject.Contains("is taking the realm")), "and everyone is warned");
            world.AdvanceTo(world.HoldEnds + World.TribeTickHours * 3600);
            Assert.IsTrue(world.LostToBloc);
            Assert.IsFalse(world.Won);
        }

        [Test]
        public void BreakingTheirGripStopsTheClock()
        {
            // (The test bloc is tiny, so the usual few points' margin would cover it whatever happened.)
            // (Nor should the tribe recruit its way back while the test watches.)
            double margin = World.HoldMargin, recruiting = World.RecruitChance;
            World.HoldMargin = 0;
            World.RecruitChance = 0;
            try { GripBreaks(); }
            finally
            {
                World.HoldMargin = margin;
                World.RecruitChance = recruiting;
            }
        }

        void GripBreaks()
        {
            var world = NewWorld();
            world.WorldLocked = true; // no new villages diluting the test bloc
            var lords = Lords(world);
            var theirs = world.FoundTribe(lords[0], "Theirs", "TH");
            world.JoinTribe(lords[1], theirs);
            world.Settings.ConquestGoal = (float)(world.BlocShare(theirs) * 0.9);
            world.AdvanceTo(world.Now + World.TribeTickHours * 3600 + 1);
            Assert.AreEqual(theirs.Id, world.HoldTribeId);
            // Their bloc shrinks below the goal: for a moment the clock runs on (a network can split and mend)...
            world.LeaveTribe(lords[1]);
            world.AdvanceTo(world.Now + World.TribeTickHours * 3600);
            Assert.AreEqual(theirs.Id, world.HoldTribeId, "a moment's grace");
            // ...but after a day below it, the hold is broken, and nobody holds it now.
            world.AdvanceTo(world.Now + (World.HoldGraceDays + 1) * World.SecondsPerDay);
            Assert.AreEqual(-1, world.HoldTribeId);
            Assert.IsTrue(world.Messages.Any(m => m.Subject.Contains("lost its grip")));
            world.AdvanceTo(world.Now + (World.HoldDays + 1) * World.SecondsPerDay);
            Assert.IsFalse(world.LostToBloc);
        }

        [Test]
        public void ATribeUnderStrainBreaksSooner()
        {
            var world = NewWorld();
            var lords = Lords(world);
            var tribe = world.FoundTribe(lords[0], "Strained", "ST");
            for (int i = 1; i < 6; i++) world.JoinTribe(lords[i], tribe);
            // (The tribe may recruit someone new meanwhile, so what counts is whether any of the original members went.)
            var original = tribe.Members.ToList();
            bool Gave() => tribe.Disbanded || tribe.LeaderId != lords[0].Id || original.Any(id => !tribe.Members.Contains(id));
            for (int day = 0; day < 20 && !Gave(); day++)
            {
                tribe.Tension = 100;
                tribe.MainCause = TensionCause.Abandoned;
                world.AdvanceTo(world.Now + World.SecondsPerDay);
            }
            Assert.IsTrue(Gave(), "something gave");
        }

        [Test]
        public void TheHumanCanNameATargetAndAskForSupport()
        {
            var world = NewWorld();
            var human = world.HumanPlayer;
            var lords = Lords(world);
            var tribe = world.FoundTribe(human, "Mine", "MI");
            Assert.IsTrue(world.SetTribeTarget(HomeOf(world, lords[0])));
            Assert.AreEqual(HomeOf(world, lords[0]).Id, tribe.TargetVillageId);
            Assert.IsTrue(world.RequestSupport(world.PlayerVillage));
            Assert.IsTrue(tribe.HelpCalls.Any(c => c.VillageId == world.PlayerVillage.Id));
        }
    }
}
