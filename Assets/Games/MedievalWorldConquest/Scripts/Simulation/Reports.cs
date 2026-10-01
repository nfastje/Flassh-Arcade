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
        /// <summary>A raid cycle's raid that went without a hitch: filed as read, and the first to go when the list is full.</summary>
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

        /// <summary>What scouts saw, if they got through: resources left and building levels.</summary>
        public bool Scouted;
        public Cost ScoutedResources;
        public int[] ScoutedLevels;

        /// <summary>A shallow copy (arrays are shared, which is fine: reports never change after they're made).</summary>
        public BattleReport Clone() => (BattleReport)MemberwiseClone();

        /// <summary>Whether the player came out on top.</summary>
        public bool PlayerWon => Kind == ReportKind.Defense ? !AttackerWon : Kind == ReportKind.SupportArrived || Kind == ReportKind.ResourcesArrived || AttackerWon;
    }

    /// <summary>The player's battle reports.</summary>
    public partial class World
    {
        /// <summary>Oldest reports are dropped beyond this many.</summary>
        public const int MaxReports = 200;

        public List<BattleReport> Reports = new List<BattleReport>();
        public int NextReportId = 1;

        public int UnreadReports => Reports.FindAll(r => !r.Read).Count;

        void AddReport(BattleReport report)
        {
            report.Id = NextReportId++;
            report.Time = Now;
            Reports.Add(report);
            // Over the limit: the oldest routine raid goes first (an overnight raid cycle shouldn't push out a
            // battle that matters), then the oldest of any kind.
            while (Reports.Count > MaxReports)
            {
                int routine = Reports.FindIndex(r => r.Routine);
                Reports.RemoveAt(routine >= 0 && routine < Reports.Count - 1 ? routine : 0);
            }
        }

        public BattleReport FindReport(int id) => Reports.Find(r => r.Id == id);

        /// <summary>Every report that involves a village (on either side), newest first.</summary>
        public List<BattleReport> ReportsAbout(int villageId)
        {
            var list = Reports.FindAll(r => (r.Kind == ReportKind.Attack || r.Kind == ReportKind.Defense) && (r.AttackerVillageId == villageId || r.DefenderVillageId == villageId));
            list.Sort((a, b) => b.Time.CompareTo(a.Time));
            return list;
        }

        /// <summary>The newest report in which scouts saw a village's buildings and stores, if any.</summary>
        public BattleReport LatestScouting(int villageId)
        {
            BattleReport best = null;
            foreach (var r in Reports)
                if (r.DefenderVillageId == villageId && r.Scouted && r.ScoutedLevels != null && r.ScoutedLevels.Length > 0 && (best == null || r.Time > best.Time))
                    best = r;
            return best;
        }

        /// <summary>The newest report that saw a village's defenders (a battle there with survivors or scouts), if any.</summary>
        public BattleReport LatestSighting(int villageId)
        {
            BattleReport best = null;
            foreach (var r in Reports)
                if (r.DefenderVillageId == villageId && r.DefenderVisible && r.DefenderTroops != null && r.DefenderTroops.Length > 0 && (best == null || r.Time > best.Time))
                    best = r;
            return best;
        }

        public void MarkAllReportsRead()
        {
            foreach (var r in Reports) r.Read = true;
        }

        public bool DeleteReport(int id) => Reports.RemoveAll(r => r.Id == id) > 0;
    }
}
