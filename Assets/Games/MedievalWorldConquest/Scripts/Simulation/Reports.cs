using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    public enum ReportKind
    {
        /// <summary>The player attacked someone.</summary>
        Attack = 0,
        /// <summary>Someone attacked one of the player's villages.</summary>
        Defense = 1,
        /// <summary>The player's support troops arrived at their destination.</summary>
        SupportArrived = 2,
        /// <summary>Merchants delivered resources to one of the player's villages, or delivered the player's.</summary>
        ResourcesArrived = 3,
    }

    /// <summary>
    /// A report of a battle (or troop arrival), from the player's point of view. Names and coordinates are copied
    /// in, so the report still makes sense if a village later changes.
    /// </summary>
    [Serializable]
    public class BattleReport
    {
        public int Id;
        public double Time;
        public ReportKind Kind;
        public bool Read;
        /// <summary>A raid cycle's raid that went without a hitch: kept only as the village's latest raid in the Loot Assistant, not in the list.</summary>
        public bool Routine;

        public int AttackerVillageId, DefenderVillageId;
        public string AttackerVillage, DefenderVillage;
        /// <summary>The villages' owners at the time ("Barbarians" for a barbarian village), and their ids (-1: barbarians, or unknown).</summary>
        public string AttackerPlayer, DefenderPlayer;
        public int AttackerPlayerId = -1, DefenderPlayerId = -1;
        public int AttackerX, AttackerY, DefenderX, DefenderY;

        public bool AttackerWon;
        public double Luck;
        public int[] AttackerSent, AttackerLost;
        /// <summary>
        /// Whether the defenders' troops are known. An attacker learns nothing about them if every attacking unit
        /// died and no scouts got through.
        /// </summary>
        public bool DefenderVisible;
        public int[] DefenderTroops, DefenderLost;

        public int WallBefore, WallAfter;
        /// <summary>The building catapults hit (a <see cref="BuildingType"/>), or -1 if none.</summary>
        public int CatapultBuilding = -1;
        public int CatapultBefore, CatapultAfter;

        public Cost Loot;
        public int LootCapacity;

        /// <summary>The village's loyalty before and after surviving noblemen swayed it (-1: no noblemen got through).</summary>
        public int LoyaltyBefore = -1, LoyaltyAfter = -1;
        /// <summary>Whether the attack won the village over.</summary>
        public bool Conquered;

        /// <summary>
        /// What scouts saw, if they got through (<see cref="Scouted"/>: the troops in the village). As in Tribal Wars,
        /// the more come back, the more they saw: half of them, the resources left; seven in ten, the buildings too.
        /// </summary>
        public bool Scouted;
        public Cost ScoutedResources;
        public int[] ScoutedLevels;
        /// <summary>The share of the attacking scouts that came back, in percent (-1: a report from before this was kept, which saw everything).</summary>
        public int ScoutSurvival = -1;

        /// <summary>The share of scouts (percent) that must come back to count a village's resources, and to see its buildings.</summary>
        public const int ResourcesSurvival = 50, BuildingsSurvival = 70;

        public bool SawResources => Scouted && (ScoutSurvival < 0 || ScoutSurvival >= ResourcesSurvival);
        public bool SawBuildings => Scouted && (ScoutSurvival < 0 || ScoutSurvival >= BuildingsSurvival);

        /// <summary>A shallow copy (arrays are shared, which is fine: reports never change after they're made).</summary>
        public BattleReport Clone() => (BattleReport)MemberwiseClone();

        /// <summary>Whether the player came out on top.</summary>
        public bool PlayerWon => Kind == ReportKind.Defense ? !AttackerWon : Kind == ReportKind.SupportArrived || Kind == ReportKind.ResourcesArrived || AttackerWon;
    }

    /// <summary>The player's battle reports.</summary>
    public partial class World
    {
        /// <summary>Beyond this many reports, the oldest are moved to the archive.</summary>
        public const int MaxReports = 200;

        /// <summary>The archive holds this many at most; beyond that its oldest are dropped.</summary>
        public const int MaxArchivedReports = 1000;

        /// <summary>The report list, oldest first.</summary>
        public List<BattleReport> Reports = new List<BattleReport>();
        /// <summary>Reports put away by the player, or pushed out of the full report list: oldest first.</summary>
        public List<BattleReport> ArchivedReports = new List<BattleReport>();
        public int NextReportId = 1;

        public int UnreadReports => Reports.FindAll(r => !r.Read).Count;

        /// <summary>Gives a report its id and time.</summary>
        void StampReport(BattleReport report)
        {
            report.Id = NextReportId++;
            report.Time = Now;
        }

        void AddReport(BattleReport report)
        {
            StampReport(report);
            Reports.Add(report);
            // Over the limit: the oldest moves to the archive (a raid cycle's routine raid from an older save just goes).
            while (Reports.Count > MaxReports)
            {
                var oldest = Reports[0];
                Reports.RemoveAt(0);
                if (!oldest.Routine) Archive(oldest);
            }
        }

        /// <summary>Files a report in the archive, in order (ids go up with time), dropping the oldest beyond the limit.</summary>
        void Archive(BattleReport report)
        {
            int at = ArchivedReports.BinarySearch(report, ById);
            ArchivedReports.Insert(at < 0 ? ~at : at, report);
            while (ArchivedReports.Count > MaxArchivedReports) ArchivedReports.RemoveAt(0);
        }

        static readonly Comparer<BattleReport> ById = Comparer<BattleReport>.Create((a, b) => a.Id.CompareTo(b.Id));

        /// <summary>
        /// Every report the player can still open: the list, the archive, and each raided village's latest raid
        /// (kept by the Loot Assistant; often the same report as one in the list).
        /// </summary>
        IEnumerable<BattleReport> AllReports()
        {
            foreach (var r in Reports) yield return r;
            foreach (var r in ArchivedReports) yield return r;
            foreach (var t in LootTargets)
                if (HasReport(t)) yield return t.LatestReport;
        }

        /// <summary>Whether a raided village has a latest report (a save fills in an empty one where there's none).</summary>
        public static bool HasReport(LootTarget t) => t.LatestReport != null && t.LatestReport.Id > 0;

        public BattleReport FindReport(int id)
        {
            foreach (var r in AllReports())
                if (r.Id == id) return r;
            return null;
        }

        /// <summary>Whether a report is in the archive.</summary>
        public bool IsArchived(int id) => ArchivedReports.Exists(r => r.Id == id);

        /// <summary>Every report that involves a village (on either side), newest first.</summary>
        public List<BattleReport> ReportsAbout(int villageId)
        {
            var list = new List<BattleReport>();
            var seen = new HashSet<int>();
            foreach (var r in AllReports())
                if ((r.Kind == ReportKind.Attack || r.Kind == ReportKind.Defense) && (r.AttackerVillageId == villageId || r.DefenderVillageId == villageId) && seen.Add(r.Id))
                    list.Add(r);
            list.Sort((a, b) => b.Time.CompareTo(a.Time));
            return list;
        }

        /// <summary>The newest report in which scouts saw a village's buildings and stores, if any.</summary>
        public BattleReport LatestScouting(int villageId)
        {
            BattleReport best = null;
            foreach (var r in AllReports())
                if (r.DefenderVillageId == villageId && r.Scouted && r.ScoutedLevels != null && r.ScoutedLevels.Length > 0 && (best == null || r.Time > best.Time))
                    best = r;
            return best;
        }

        /// <summary>The newest report that saw a village's defenders (a battle there with survivors or scouts), if any.</summary>
        public BattleReport LatestSighting(int villageId)
        {
            BattleReport best = null;
            foreach (var r in AllReports())
                if (r.DefenderVillageId == villageId && r.DefenderVisible && r.DefenderTroops != null && r.DefenderTroops.Length > 0 && (best == null || r.Time > best.Time))
                    best = r;
            return best;
        }

        /// <summary>Marks every report in the list (or in the archive) read.</summary>
        public void MarkAllReportsRead(bool archive = false)
        {
            foreach (var r in archive ? ArchivedReports : Reports) r.Read = true;
        }

        public bool DeleteReport(int id) => Reports.RemoveAll(r => r.Id == id) + ArchivedReports.RemoveAll(r => r.Id == id) > 0;

        /// <summary>Deletes reports from the list and the archive. Returns how many went.</summary>
        public int DeleteReports(ICollection<int> ids) =>
            Reports.RemoveAll(r => ids.Contains(r.Id)) + ArchivedReports.RemoveAll(r => ids.Contains(r.Id));

        /// <summary>Moves reports from the list to the archive. Returns how many moved.</summary>
        public int ArchiveReports(ICollection<int> ids)
        {
            var moving = Reports.FindAll(r => ids.Contains(r.Id));
            Reports.RemoveAll(r => ids.Contains(r.Id));
            foreach (var r in moving) Archive(r);
            return moving.Count;
        }
    }
}
