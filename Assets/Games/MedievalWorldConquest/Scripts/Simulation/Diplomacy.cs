using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// The life of tribes on a diplomacy world, worked out twice a game day (a <see cref="EventKind.TribeTick"/>):
    /// lords founding and joining tribes; invitations, answers and warnings to the human; tribes making pacts and
    /// war with each other; tribe leaders naming targets; and "social turbulence": the strains that build up in a
    /// tribe (members left alone under attack, a weak leader, an ambitious member who has outgrown it, members
    /// spread too far, a long dull peace, fear of a bloc about to win) and, now and then, break it: a member
    /// leaves, a group splinters off, the leadership changes hands, tribes merge or are absorbed, pacts sour. Calm
    /// tribes can last a whole world; strained ones rarely do. Also the tribe-and-allies ("bloc") victory.
    /// </summary>
    public partial class World
    {
        public const double TribeTickHours = 12;
        /// <summary>A bloc (a tribe and its allies) holding this share of the players' villages makes others uneasy.</summary>
        public const double BlocFearShare = 0.45;

        public int TribeTicks;
        /// <summary>An AI bloc reached the conquest goal (the player lost the world), which tribe led it, and whether the player has been told.</summary>
        public bool LostToBloc, LossShown;
        public int WinningTribeId = -1;

        /// <summary>Attacks between tribes since the last tick (not saved): grudges that sour relations.</summary>
        [NonSerialized] Dictionary<(int, int), int> incidents;

        void ScheduleTribeTick() => Schedule(TribeTickHours * 3600, EventKind.TribeTick);

        /// <summary>A repeatable random number for this tick, from the world seed and a salt.</summary>
        double TribeRandom(int a, int b = 0) => Terrain.Hash(Settings.Seed ^ 0x1656667B, a * 7919 + b, TribeTicks);

        static bool Warlike(Player p) => p.Personality == AiPersonality.Warlord || p.Personality == AiPersonality.Raider;

        static bool CanJoinTribes(Player p) =>
            p != null && !p.Quit && p.Personality != AiPersonality.Inactive;

        void TribeTick(ScheduledEvent e)
        {
            TribeTicks++;
            ScheduleTribeTick();
            if (!Diplomacy) return;
            double days = TribeTickHours / 24;

            ExpireHelpCalls();
            FormTribes();
            RecruitAndCull();
            BeatenLords(days);
            AnswerHuman();
            foreach (var t in ActiveTribes()) Turbulence(t, days);
            MergeAndAbsorb();
            Relate();
            FactionTick(days);
            NameTargets();
            HumanStanding();
            incidents?.Clear();
            var human = HumanPlayer;
            if (human != null && human.Reputation < 0) human.Reputation = Math.Min(0, human.Reputation + 1);
            CheckVictory();
        }
    }
}
