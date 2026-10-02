using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>What the world keeps count of, as Tribal Wars' statistics did.</summary>
    public enum StatKind
    {
        /// <summary>Resources plundered.</summary>
        Loot = 0,
        /// <summary>Troops defeated while attacking (Tribal Wars' "opponents defeated as attacker").</summary>
        DefeatedAttacking = 1,
        /// <summary>Troops defeated while defending one's own villages.</summary>
        DefeatedDefending = 2,
        /// <summary>Troops lost, attacking or defending (support included).</summary>
        TroopsLost = 3,
        VillagesConquered = 4,
        VillagesLost = 5,
    }

    /// <summary>Which span of time a statistic covers.</summary>
    public enum StatPeriod
    {
        Today,
        ThisWeek,
        AllTime,
    }

    /// <summary>The whole world's counts for one game day.</summary>
    [Serializable]
    public class DayStats
    {
        public int Day;
        public long[] Values;
    }

    /// <summary>One of the realm's records: how much, who set it (and against whom), where, and on which day (0: none yet).</summary>
    [Serializable]
    public class RealmRecord
    {
        public long Value;
        public string Who = "", Against = "", Where = "";
        public int Day;
    }

    /// <summary>The realm's records, for the statistics page.</summary>
    [Serializable]
    public class RealmRecords
    {
        /// <summary>The most plunder carried off by one attack.</summary>
        public RealmRecord BiggestHaul = new RealmRecord();
        /// <summary>The most troops killed in one battle, both sides together.</summary>
        public RealmRecord BloodiestBattle = new RealmRecord();
        /// <summary>The most villages one player conquered in a single day.</summary>
        public RealmRecord MostConquestsInADay = new RealmRecord();
    }

    /// <summary>
    /// World statistics: each player's counts (all time, and where they stood when the day and the week began, so
    /// today's and this week's are the difference), and the world's own for each of the last
    /// <see cref="MaxStatHistory"/> days. Weeks are days 1–7, 8–14 and so on.
    /// </summary>
    public partial class World
    {
        public const int StatKinds = 6;
        /// <summary>Game days of the world's own statistics kept.</summary>
        public const int MaxStatHistory = 60;

        /// <summary>The game day the counts were last brought up to date for.</summary>
        public int StatsDay = 1;
        public long[] WorldStats = new long[StatKinds];
        public long[] WorldStatsDayStart = new long[StatKinds];
        public List<DayStats> StatHistory = new List<DayStats>();
        public RealmRecords Records = new RealmRecords();

        /// <summary>Notes a battle for the records: the haul, and the troops killed on both sides.</summary>
        void NoteBattleRecords(Player attacker, Village target, long haul, long killed)
        {
            Records ??= new RealmRecords();
            string where = $"{target.Name} ({target.X}|{target.Y})";
            string against = target.IsBarbarian ? "Barbarians" : FindPlayer(target.OwnerId)?.Name ?? "";
            if (haul > Records.BiggestHaul.Value)
                Records.BiggestHaul = new RealmRecord { Value = haul, Who = attacker?.Name ?? "", Against = against, Where = where, Day = DayOf(Now) };
            if (killed > Records.BloodiestBattle.Value)
                Records.BloodiestBattle = new RealmRecord { Value = killed, Who = attacker?.Name ?? "", Against = against, Where = where, Day = DayOf(Now) };
        }

        static int WeekOf(int day) => (day - 1) / 7;

        /// <summary>A new game day (and maybe week): today's (and this week's) counts start again from here.</summary>
        void RollStats()
        {
            int today = DayOf(Now);
            if (today <= StatsDay) return;
            var finished = new long[StatKinds];
            for (int i = 0; i < StatKinds; i++) finished[i] = WorldStats[i] - WorldStatsDayStart[i];
            StatHistory.Add(new DayStats { Day = StatsDay, Values = finished });
            if (StatHistory.Count > MaxStatHistory) StatHistory.RemoveRange(0, StatHistory.Count - MaxStatHistory);
            bool newWeek = WeekOf(today) != WeekOf(StatsDay);
            Records ??= new RealmRecords();
            foreach (var p in Players)
            {
                EnsureStats(p);
                // The day's conquests, for the records, before the day's counts start again.
                long conquered = p.Stats[(int)StatKind.VillagesConquered] - p.StatsDayStart[(int)StatKind.VillagesConquered];
                if (conquered > Records.MostConquestsInADay.Value)
                    Records.MostConquestsInADay = new RealmRecord { Value = conquered, Who = p.Name, Day = StatsDay };
                Array.Copy(p.Stats, p.StatsDayStart, StatKinds);
                if (newWeek) Array.Copy(p.Stats, p.StatsWeekStart, StatKinds);
            }
            Array.Copy(WorldStats, WorldStatsDayStart, StatKinds);
            StatsDay = today;
        }

        static void EnsureStats(Player p)
        {
            if (p.Stats == null || p.Stats.Length != StatKinds) p.Stats = Resized(p.Stats, StatKinds);
            if (p.StatsDayStart == null || p.StatsDayStart.Length != StatKinds) p.StatsDayStart = Resized(p.StatsDayStart, StatKinds);
            if (p.StatsWeekStart == null || p.StatsWeekStart.Length != StatKinds) p.StatsWeekStart = Resized(p.StatsWeekStart, StatKinds);
        }

        static long[] Resized(long[] values, int length)
        {
            var sized = new long[length];
            if (values != null) Array.Copy(values, sized, Math.Min(values.Length, length));
            return sized;
        }

        /// <summary>Counts something towards a player (none for barbarians) and the world.</summary>
        void AddStat(int playerId, StatKind kind, long amount)
        {
            if (amount <= 0) return;
            RollStats();
            WorldStats[(int)kind] += amount;
            var p = playerId >= 0 ? FindPlayer(playerId) : null;
            if (p == null) return;
            EnsureStats(p);
            p.Stats[(int)kind] += amount;
        }

        /// <summary>A player's count over a span of time.</summary>
        public long StatOf(Player p, StatKind kind, StatPeriod period)
        {
            if (p?.Stats == null || p.Stats.Length != StatKinds) return 0;
            int today = DayOf(Now);
            long all = p.Stats[(int)kind];
            switch (period)
            {
                case StatPeriod.Today:
                    return today > StatsDay ? 0 : all - (p.StatsDayStart?.Length == StatKinds ? p.StatsDayStart[(int)kind] : 0);
                case StatPeriod.ThisWeek:
                    return WeekOf(today) != WeekOf(StatsDay) ? 0 : all - (p.StatsWeekStart?.Length == StatKinds ? p.StatsWeekStart[(int)kind] : 0);
                default:
                    return all;
            }
        }

        /// <summary>The world's count over a span of time.</summary>
        public long WorldStatOf(StatKind kind, StatPeriod period)
        {
            int today = DayOf(Now);
            long all = WorldStats[(int)kind];
            switch (period)
            {
                case StatPeriod.Today:
                    return today > StatsDay ? 0 : all - WorldStatsDayStart[(int)kind];
                case StatPeriod.ThisWeek:
                    // The last day counted (today, or an earlier day not yet rolled over), and the days before it.
                    long week = WeekOf(StatsDay) == WeekOf(today) ? all - WorldStatsDayStart[(int)kind] : 0;
                    foreach (var d in StatHistory)
                        if (WeekOf(d.Day) == WeekOf(today)) week += d.Values[(int)kind];
                    return week;
                default:
                    return all;
            }
        }

        /// <summary>The world's counts for its most recent days, today first (days nothing happened on are left out).</summary>
        public List<DayStats> RecentDays(int count)
        {
            int today = DayOf(Now);
            var latest = new long[StatKinds];
            for (int i = 0; i < StatKinds; i++) latest[i] = WorldStats[i] - WorldStatsDayStart[i];
            var days = new List<DayStats>();
            if (today > StatsDay) days.Add(new DayStats { Day = today, Values = new long[StatKinds] });
            days.Add(new DayStats { Day = StatsDay, Values = latest });
            for (int i = StatHistory.Count - 1; i >= 0 && days.Count < count; i--) days.Add(StatHistory[i]);
            return days;
        }

        /// <summary>A tribe's count over a span of time: its members' now (whenever they earned it).</summary>
        public long StatOf(Tribe t, StatKind kind, StatPeriod period)
        {
            long sum = 0;
            foreach (int id in t.Members) sum += StatOf(FindPlayer(id), kind, period);
            return sum;
        }
    }
}
