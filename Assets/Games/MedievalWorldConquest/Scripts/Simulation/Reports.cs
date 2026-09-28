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

        public int AttackerVillageId, DefenderVillageId;
        public string AttackerVillage, DefenderVillage;
        /// <summary>The villages' owners at the time ("Barbarians" for a barbarian village).</summary>
        public string AttackerPlayer, DefenderPlayer;
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

        /// <summary>What scouts saw, if they got through: resources left and building levels.</summary>
        public bool Scouted;
        public Cost ScoutedResources;
        public int[] ScoutedLevels;

        /// <summary>A shallow copy (arrays are shared, which is fine: reports never change after they're made).</summary>
        public BattleReport Clone() => (BattleReport)MemberwiseClone();

        /// <summary>Whether the player came out on top.</summary>
        public bool PlayerWon => Kind == ReportKind.Defense ? !AttackerWon : Kind == ReportKind.SupportArrived || AttackerWon;
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
            if (Reports.Count > MaxReports) Reports.RemoveRange(0, Reports.Count - MaxReports);
        }

        public BattleReport FindReport(int id) => Reports.Find(r => r.Id == id);

        public void MarkAllReportsRead()
        {
            foreach (var r in Reports) r.Read = true;
        }

        public bool DeleteReport(int id) => Reports.RemoveAll(r => r.Id == id) > 0;
    }
}
