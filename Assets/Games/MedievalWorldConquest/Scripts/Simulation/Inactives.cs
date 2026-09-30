using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Filling the map, as on a Tribal Wars world: besides the lords who play, the settled land is dotted with
    /// inactive players (people who played for a while and left) and extra barbarian villages. Inactive players never
    /// take turns: their village grows by itself, a random building at a time from a list, until it reaches the size
    /// its player left it at, and then it stops. Two weeks later their account is closed and the village goes
    /// barbarian. So they cost almost nothing to run, however many there are.
    /// </summary>
    public partial class World
    {
        /// <summary>On average one inactive player's village per this many fields of settled land (at normal density).</summary>
        public const double FieldsPerInactive = 65;
        /// <summary>On average one extra barbarian village per this many fields of settled land.</summary>
        public const double FieldsPerExtraBarbarian = 120;
        /// <summary>Game hours between an inactive village's building steps (each picks its own wait in this range).</summary>
        public const double InactiveGrowthMinHours = 2, InactiveGrowthMaxHours = 6;
        /// <summary>
        /// The points an inactive player's village stops at: from this minimum, most small (half under about 230),
        /// a few up to about 1,300, like the players who give up on a real world.
        /// </summary>
        public const int InactiveMinPoints = 150, InactiveMaxExtraPoints = 3000;

        /// <summary>Game days an inactive player's village sits unchanged before its account is closed and it goes barbarian.</summary>
        public const double InactiveDaysBeforeLeaving = 14;

        /// <summary>Villages owed to the settled land's growth so far, for inactive players and for extra barbarians.</summary>
        public double InactiveBacklog, BarbarianBacklog;

        /// <summary>What an inactive player's village builds, how far, and how likely each is to be picked.</summary>
        static readonly (BuildingType building, int cap, double weight)[] InactiveGrowth =
        {
            (BuildingType.TimberCamp, 25, 3), (BuildingType.ClayPit, 25, 3), (BuildingType.IronMine, 25, 3),
            (BuildingType.Farm, 24, 2), (BuildingType.Warehouse, 24, 2), (BuildingType.Headquarters, 22, 2.5),
            (BuildingType.Barracks, 15, 1), (BuildingType.Smithy, 20, 1.5), (BuildingType.Wall, 15, 1),
            (BuildingType.Stable, 10, 0.5), (BuildingType.Market, 12, 1), (BuildingType.HidingPlace, 10, 0.5),
            (BuildingType.Workshop, 5, 0.2),
        };

        /// <summary>Inactive players come with rival lords: none without them, and more (or fewer) with the density setting.</summary>
        double InactiveShare => Math.Min(2, Math.Max(0, Settings.RivalDensity));

        public bool IsInactive(int playerId) => FindPlayer(playerId)?.Personality == AiPersonality.Inactive;

        /// <summary>
        /// Adds the inactive players' and extra barbarian villages owed for newly settled land (an area of
        /// <paramref name="area"/> fields), placed where <paramref name="where"/> says.
        /// </summary>
        void FillIn(double area, Random rng, Func<double> where)
        {
            InactiveBacklog += area / FieldsPerInactive * InactiveShare;
            BarbarianBacklog += area / FieldsPerExtraBarbarian;
            for (; InactiveBacklog >= 1; InactiveBacklog--)
                if (TryFindSpot(rng, where, out int x, out int y)) SpawnInactive(x, y, rng, 0);
            for (; BarbarianBacklog >= 1; BarbarianBacklog--)
                if (TryFindSpot(rng, where, out int x, out int y)) SpawnBarbarian(x, y, rng);
        }

        /// <summary>
        /// A new inactive player with one village. It grows from nothing over the coming days, or starts part-way
        /// there (<paramref name="headStart"/>, 0 to 1, of the way to its size) when it's filling in land that was
        /// settled long ago.
        /// </summary>
        Player SpawnInactive(int x, int y, Random rng, double headStart)
        {
            double r = rng.NextDouble();
            var player = NewPlayer(AiPersonality.Inactive, rng);
            player.TargetPoints = InactiveMinPoints + (int)(InactiveMaxExtraPoints * r * r);
            var village = new Village { Id = NewVillageId(), Name = NewPlaceName(rng), X = x, Y = y, OwnerId = player.Id };
            village.SetUpAsNew();
            AddVillage(village);

            int start = (int)(player.TargetPoints * Math.Max(0, Math.Min(1, headStart)));
            while (village.Points < start && GrowInactiveStep(village)) { }
            // One that already stopped some time ago (filling in old land) has less of its fortnight left.
            if (village.Points >= player.TargetPoints) GoInactive(village, player, 0.2 + 0.8 * rng.NextDouble());
            else ScheduleInactiveGrowth(village);
            return player;
        }

        void ScheduleInactiveGrowth(Village v)
        {
            double hours = InactiveGrowthMinHours + (InactiveGrowthMaxHours - InactiveGrowthMinHours) * GrowthRandom(v, 0);
            Schedule(hours * 3600, EventKind.InactiveGrowth, v.Id);
        }

        /// <summary>An inactive player's village builds one more level, until it's as big as its player left it.</summary>
        void GrowInactive(ScheduledEvent e)
        {
            var v = FindVillage(e.VillageId);
            var owner = v == null ? null : FindPlayer(v.OwnerId);
            if (owner == null || owner.Personality != AiPersonality.Inactive) return; // conquered, or gone barbarian
            Touch(v); // production up to now at the old levels
            if (v.Points < owner.TargetPoints && GrowInactiveStep(v) && v.Points < owner.TargetPoints) ScheduleInactiveGrowth(v);
            else GoInactive(v, owner, 1);
        }

        /// <summary>One random building level from the list (one whose requirements are met). Returns whether anything grew.</summary>
        bool GrowInactiveStep(Village v)
        {
            double total = 0;
            foreach (var g in InactiveGrowth)
                if (CanGrow(v, g.building, g.cap)) total += g.weight;
            if (total <= 0) return false;

            double pick = GrowthRandom(v, 1) * total;
            v.GrowthSteps++;
            foreach (var g in InactiveGrowth)
            {
                if (!CanGrow(v, g.building, g.cap)) continue;
                pick -= g.weight;
                if (pick > 0) continue;
                v.Levels[(int)g.building]++;
                return true;
            }
            return false;
        }

        static bool CanGrow(Village v, BuildingType type, int cap) =>
            v.Level(type) < cap && Buildings.UnmetRequirement(type, v) == null;

        /// <summary>
        /// The player has stopped: the village keeps a small garrison of whatever they'd trained and grows no more,
        /// and <paramref name="leaveIn"/> of <see cref="InactiveDaysBeforeLeaving"/> later the account is closed.
        /// </summary>
        void GoInactive(Village v, Player owner, double leaveIn)
        {
            Schedule(InactiveDaysBeforeLeaving * leaveIn * SecondsPerDay, EventKind.InactiveLeaves, -1, owner.Id);
            if (v.Level(BuildingType.Barracks) <= 0) return;
            int points = v.Points;
            v.Troops[(int)UnitType.Spearman] += (int)(points * 0.25 * GrowthRandom(v, 2));
            v.Troops[(int)UnitType.Swordsman] += (int)(points * 0.1 * GrowthRandom(v, 3));
        }

        /// <summary>An inactive player's account is closed: their village goes barbarian, garrison and all.</summary>
        void InactiveLeaves(ScheduledEvent e)
        {
            var player = FindPlayer(e.A);
            if (player != null && player.Personality == AiPersonality.Inactive) QuitLord(player);
        }

        /// <summary>
        /// Worlds from before version 15 had neither: fill the land already settled with inactive players (older
        /// ones, nearer the middle, further along) and extra barbarian villages, as if they'd been there all along.
        /// </summary>
        void FillInSettledLand()
        {
            var rng = new Random(Settings.Seed * 13 + 7);
            double area = Math.PI * SpawnRadius * SpawnRadius;
            Func<double> where = () => SpawnRadius * Math.Sqrt(rng.NextDouble());
            InactiveBacklog += area / FieldsPerInactive * InactiveShare;
            BarbarianBacklog += area / FieldsPerExtraBarbarian;
            double center = MapSize / 2.0;
            for (; InactiveBacklog >= 1; InactiveBacklog--)
            {
                if (!TryFindSpot(rng, where, out int x, out int y)) continue;
                double fromCenter = Math.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                // Settled long ago, so their beginner protection is long gone too.
                SpawnInactive(x, y, rng, 1 - fromCenter / Math.Max(1, SpawnRadius)).ProtectedUntil = Now;
            }
            for (; BarbarianBacklog >= 1; BarbarianBacklog--)
            {
                if (!TryFindSpot(rng, where, out int x, out int y)) continue;
                double fromCenter = Math.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                var village = new Village { Id = NewVillageId(), Name = BarbarianName, X = x, Y = y, OwnerId = -1 };
                SetUpBarbarian(village, (1 - fromCenter / Math.Max(1, SpawnRadius)) * (0.3 + 0.5 * rng.NextDouble()), rng);
                AddVillage(village);
                ScheduleBarbarianGrowth(village);
            }
        }
    }
}
