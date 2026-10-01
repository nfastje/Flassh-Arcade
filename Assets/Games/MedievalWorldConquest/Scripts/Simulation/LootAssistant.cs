using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>How the player's last raid on a village went: the colored dot in the Loot Assistant.</summary>
    public enum RaidResult
    {
        None = 0,
        /// <summary>Won without a loss (green).</summary>
        Clean = 1,
        /// <summary>Won, with losses (yellow).</summary>
        Losses = 2,
        /// <summary>Lost: no one came back, or no one won (red).</summary>
        Defeat = 3,
    }

    /// <summary>
    /// What the player knows of a village they've raided or scouted, kept with the Loot Assistant so it outlasts
    /// the reports; and, if they've put it in their raid cycle, which village raids it and with which template.
    /// </summary>
    [Serializable]
    public class LootTarget
    {
        public int VillageId;
        public double LastRaidAt = -1;
        public RaidResult LastResult;
        /// <summary>Whether the last raid came back with all it could carry (there was likely more).</summary>
        public bool FullHaul;
        public Cost LastLoot;
        /// <summary>What scouts last saw there (-1: never scouted): resources, and building levels.</summary>
        public double ScoutedAt = -1;
        public Cost ScoutedResources;
        public int[] ScoutedLevels;

        /// <summary>In the raid cycle: raided again from <see cref="FromVillageId"/> each time its raiders get home.</summary>
        public bool Cycling;
        public int FromVillageId = -1;
        /// <summary>Which template the cycle sends: 0 = A, 1 = B, 2 = C (sized from the last scouting).</summary>
        public int Template;
        /// <summary>Why the cycle stopped by itself (empty if it didn't).</summary>
        public string Stopped = "";
    }

    /// <summary>
    /// The Loot Assistant, as in Tribal Wars (Farm Assist): two troop templates, A and B, and a third, C, sized to
    /// carry what scouts last saw in a village; a list of the barbarian villages around (and any village the
    /// player has raided), each with how the last raid went; one-click raids; and a raid cycle that sends the same
    /// raid again whenever its troops get home, all in the world itself, so it goes on while the game is closed.
    /// A cycle stops by itself when a raid is beaten or loses more than a tenth of its troops, when the village
    /// changes hands, or when the village it raids from is lost. Unlocked by the quest line's first raid.
    /// </summary>
    public partial class World
    {
        public const int TemplateA = 0, TemplateB = 1, TemplateC = 2;
        /// <summary>How far from the player's village the Loot Assistant looks for barbarian villages, in fields.</summary>
        public const double LootRange = 20;
        /// <summary>At most this many villages in the Loot Assistant's list.</summary>
        public const int LootListSize = 60;
        /// <summary>A raid cycle stops when a raid loses more than this share of its troops.</summary>
        public const double CycleLossLimit = 0.1;

        public bool LootAssistantUnlocked;
        public int[] LootTemplateA, LootTemplateB;
        public List<LootTarget> LootTargets = new List<LootTarget>();

        /// <summary>The quest whose reward unlocks the Loot Assistant (the first raid).</summary>
        public const string LootAssistantQuest = "Plunder";

        static int[] DefaultTemplateA()
        {
            var t = new int[Units.Count];
            t[(int)UnitType.Spearman] = 10;
            return t;
        }

        static int[] DefaultTemplateB()
        {
            var t = new int[Units.Count];
            t[(int)UnitType.LightCavalry] = 5;
            return t;
        }

        public int[] LootTemplate(int which) => which == TemplateA ? LootTemplateA : LootTemplateB;

        public void SetLootTemplate(int which, int[] troops)
        {
            var t = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < troops.Length; i++) t[i] = Math.Max(0, troops[i]);
            if (which == TemplateA) LootTemplateA = t;
            else if (which == TemplateB) LootTemplateB = t;
        }

        public LootTarget LootTargetFor(int villageId, bool create = false)
        {
            var t = LootTargets.Find(x => x.VillageId == villageId);
            if (t == null && create) LootTargets.Add(t = new LootTarget { VillageId = villageId });
            return t;
        }

        /// <summary>Whether the player can raid a village with the Loot Assistant: not theirs, a friend's, or one under protection.</summary>
        public bool IsLootable(Village t)
        {
            var human = HumanPlayer;
            if (t == null || human == null || t.OwnerId == human.Id) return false;
            if (t.IsBarbarian) return true;
            return !IsProtected(t.OwnerId) && !AreFriendly(human.Id, t.OwnerId);
        }

        /// <summary>
        /// The villages the Loot Assistant lists for raiding from <paramref name="from"/>: the barbarians in range,
        /// plus every village the player has raided or scouted (or put in the cycle) that's still fair game, nearest first.
        /// </summary>
        public List<Village> LootList(Village from)
        {
            var list = new List<Village>();
            if (from == null) return list;
            foreach (var v in VillagesNear(from.X, from.Y, LootRange))
                if (v.IsBarbarian) list.Add(v);
            foreach (var t in LootTargets)
            {
                var v = FindVillage(t.VillageId);
                if (v != null && !v.IsBarbarian && IsLootable(v) && !list.Contains(v)) list.Add(v);
                else if (v != null && v.IsBarbarian && !list.Contains(v) && (t.Cycling || t.LastRaidAt >= 0)) list.Add(v);
            }
            list.Sort((a, b) => Distance(from, a).CompareTo(Distance(from, b)));
            if (list.Count > LootListSize) list.RemoveRange(LootListSize, list.Count - LootListSize);
            return list;
        }

        /// <summary>
        /// The order template C picks carriers in: the fastest first (so the raid gets there and back soonest), and
        /// of equally fast ones, those that carry most.
        /// </summary>
        static readonly UnitType[] CarriersFastestFirst = SortedCarriers();

        static UnitType[] SortedCarriers()
        {
            var list = new List<UnitType>();
            foreach (var type in Units.InDisplayOrder)
                if (Units.Get(type).Carry > 0) list.Add(type);
            list.Sort((a, b) =>
            {
                var ua = Units.Get(a);
                var ub = Units.Get(b);
                return ua.MinutesPerField != ub.MinutesPerField ? ua.MinutesPerField.CompareTo(ub.MinutesPerField) : ub.Carry.CompareTo(ua.Carry);
            });
            return list.ToArray();
        }

        /// <summary>
        /// What template C sends from <paramref name="from"/> to <paramref name="target"/>: from the troops at home,
        /// just enough to carry off what should be there (see <see cref="ExpectedLoot"/>), fastest carriers first,
        /// or every carrier at home if that's not enough; and one scout, if there is one, to see what's left. Null
        /// if the village's stores are unknown (never scouted), nothing is expected there, or no carriers are home.
        /// </summary>
        public int[] TemplateCFor(Village from, Village target)
        {
            if (from == null) return null;
            double remaining = ExpectedLoot(target);
            if (remaining <= 0) return null;
            var troops = new int[Units.Count];
            int carry = 0;
            foreach (var type in CarriersFastestFirst)
            {
                if (remaining <= 0) break;
                int each = Units.Get(type).Carry;
                int take = Math.Min(from.TroopCount(type), (int)Math.Ceiling(remaining / each));
                if (take <= 0) continue;
                troops[(int)type] = take;
                carry += take * each;
                remaining -= take * each;
            }
            if (carry <= 0) return null;
            if (from.TroopCount(UnitType.Scout) > 0) troops[(int)UnitType.Scout] = 1;
            return troops;
        }
        /// <summary>
        /// The newest look at a village's stores: the Loot Assistant's own note, or (for scouting done before it
        /// kept notes) the newest scouting report. False if it's never been scouted.
        /// </summary>
        bool LatestStores(int villageId, out double seenAt, out Cost seen, out int[] levels)
        {
            var t = LootTargetFor(villageId);
            var report = LatestScouting(villageId);
            bool noted = t != null && t.ScoutedAt >= 0 && t.ScoutedLevels != null;
            if (report != null && (!noted || report.Time > t.ScoutedAt))
            {
                seenAt = report.Time;
                seen = report.ScoutedResources;
                levels = report.ScoutedLevels;
                return true;
            }
            seenAt = noted ? t.ScoutedAt : -1;
            seen = noted ? t.ScoutedResources : default;
            levels = noted ? t.ScoutedLevels : null;
            return noted;
        }

        /// <summary>Whether the player's scouts have ever seen a village's stores.</summary>
        public bool IsScouted(Village target) => target != null && LatestStores(target.Id, out _, out _, out _);

        /// <summary>The wall the player's scouts last saw at a village (-1: never scouted).</summary>
        public int ScoutedWall(Village target) =>
            target != null && LatestStores(target.Id, out _, out _, out var levels) && levels.Length > (int)BuildingType.Wall ? levels[(int)BuildingType.Wall] : -1;

        /// <summary>
        /// What there should be to carry off from a scouted village by now: what the scouts saw, plus what its mines
        /// (the levels they saw) have made since, within its warehouse, less what its hiding place keeps. A raid
        /// since then is allowed for: one that came back with room to spare emptied it; a full one took its haul
        /// and left the rest.
        /// </summary>
        public double ExpectedLoot(Village target)
        {
            if (target == null || !LatestStores(target.Id, out double seenAt, out var seen, out var levels)) return 0;
            int Level(BuildingType type) => (int)type < levels.Length ? levels[(int)type] : 0;
            double cap = Buildings.StorageCapacity(Level(BuildingType.Warehouse));
            int hidden = Buildings.HiddenCapacity(Level(BuildingType.HidingPlace));
            var t = LootTargetFor(target.Id);
            bool raidedSince = t != null && t.LastRaidAt > seenAt && t.LastResult != RaidResult.Defeat;
            double haul = 0;
            foreach (ResourceType r in ResourceTypes)
            {
                double perHour = Buildings.ProductionPerHour(Level(Village.MineFor(r)));
                double stock = r == ResourceType.Wood ? seen.Wood : r == ResourceType.Clay ? seen.Clay : seen.Iron;
                double since = seenAt;
                if (raidedSince)
                {
                    double atRaid = Math.Min(cap, stock + perHour * Math.Max(0, t.LastRaidAt - seenAt) / 3600);
                    double taken = r == ResourceType.Wood ? t.LastLoot.Wood : r == ResourceType.Clay ? t.LastLoot.Clay : t.LastLoot.Iron;
                    stock = t.FullHaul ? Math.Max(hidden, atRaid - taken) : Math.Min(atRaid, hidden);
                    since = t.LastRaidAt;
                }
                double now = Math.Min(cap, stock + perHour * Math.Max(0, Now - since) / 3600);
                haul += Math.Max(0, now - hidden);
            }
            return haul;
        }

/// <summary>Why template C can't raid a village from another right now.</summary>
        public string WhyNoC(Village from, Village target)
        {

            if (!IsScouted(target)) return "Template C needs a scouting report of that village: send scouts first.";
            if (ExpectedLoot(target) <= 0) return "Nothing to carry off there yet, by the last scouting.";
            return $"No troops that carry loot are at home in {from?.Name}.";
        }

        /// <summary>The troops a template sends from a village to a target (null: C can't be worked out).</summary>
        public int[] LootTroops(int which, Village from, Village target) => which == TemplateC ? TemplateCFor(from, target) : LootTemplate(which);
        /// <summary>
        /// Sends a raid with a template. Returns null if it went, or why it couldn't (not enough troops, and so on).
        /// </summary>
        public string SendLoot(Village from, Village target, int which)
        {
            if (!LootAssistantUnlocked) return "The Loot Assistant isn't unlocked yet.";
            if (from == null || from.OwnerId != HumanPlayer?.Id) return "That village isn't yours.";
            if (!IsLootable(target)) return "That village can't be raided.";
            var troops = LootTroops(which, from, target);
            if (troops == null) return which == TemplateC ? WhyNoC(from, target) : "That template is empty.";
            var check = CheckSend(from, target, troops, CommandKind.Attack);
            switch (check.Status)
            {
                case SendStatus.Ok: break;
                case SendStatus.NoTroops: return "That template is empty.";
                case SendStatus.NotEnoughTroops: return $"Not enough troops in {from.Name} for template {"ABC"[which]}.";
                case SendStatus.TargetProtected: return "That player is under beginner protection.";
                default: return "That village can't be raided.";
            }
            Send(from, target, troops, CommandKind.Attack);
            LootTargetFor(target.Id, true);
            return null;
        }

        /// <summary>Whether a raid of the player's is on its way to a village from another, or on its way back.</summary>
        public bool RaidUnderway(int fromId, int targetId)
        {
            var human = HumanPlayer;
            if (human == null) return false;
            foreach (var c in CommandsOf(human.Id))
                if ((c.Kind == CommandKind.Attack && c.FromVillageId == fromId && c.ToVillageId == targetId) ||
                    (c.Kind == CommandKind.Return && c.FromVillageId == targetId && c.ToVillageId == fromId))
                    return true;
            return false;
        }

        /// <summary>Puts a village in the raid cycle (raided from <paramref name="from"/> with a template), and sends the first raid if it can.</summary>
        public string StartCycle(Village from, Village target, int which)
        {
            if (!LootAssistantUnlocked) return "The Loot Assistant isn't unlocked yet.";
            if (from == null || from.OwnerId != HumanPlayer?.Id) return "That village isn't yours.";
            if (!IsLootable(target)) return "That village can't be raided.";
            if (which == TemplateC ? !IsScouted(target) : LootTroops(which, from, target) == null) return which == TemplateC ? "Template C needs a scouting report of that village: send scouts first." : "That template is empty.";
            var t = LootTargetFor(target.Id, true);
            t.Cycling = true;
            t.FromVillageId = from.Id;
            t.Template = which;
            t.Stopped = "";
            RunCycle(from);
            return null;
        }

        public void StopCycle(int targetId)
        {
            var t = LootTargetFor(targetId);
            if (t == null) return;
            t.Cycling = false;
            t.Stopped = "";
        }

        /// <summary>The villages in the raid cycle.</summary>
        public List<LootTarget> CycleTargets() => LootTargets.FindAll(t => t.Cycling);

        /// <summary>What a cycle is doing right now, in words.</summary>
        public string CycleStatus(LootTarget t)
        {
            if (!t.Cycling) return string.IsNullOrEmpty(t.Stopped) ? "" : "Stopped: " + t.Stopped;
            var from = FindVillage(t.FromVillageId);
            if (from == null) return "";
            if (RaidUnderway(t.FromVillageId, t.VillageId)) return "Raiding";
            var troops = LootTroops(t.Template, from, FindVillage(t.VillageId));
            return troops == null && !IsScouted(FindVillage(t.VillageId)) ? "Waiting for a scouting report" : "Waiting for troops";
        }

        /// <summary>
        /// Sends every cycle raid from a village whose last raid is home (and that the troops at home can make up),
        /// in the order the villages were put in the cycle. Run when troops get home, and on the Account Manager's
        /// rounds (in case troops were short before).
        /// </summary>
        void RunCycle(Village from)
        {
            if (!LootAssistantUnlocked || from == null || LootTargets.Count == 0) return;
            var human = HumanPlayer;
            if (human == null || from.OwnerId != human.Id) return;
            foreach (var t in LootTargets)
            {
                if (!t.Cycling || t.FromVillageId != from.Id) continue;
                var target = FindVillage(t.VillageId);
                if (!IsLootable(target))
                {
                    t.Cycling = false;
                    t.Stopped = target == null ? "the village is gone." : "the village can't be raided now.";
                    continue;
                }
                if (RaidUnderway(from.Id, target.Id)) continue;
                var troops = LootTroops(t.Template, from, target);
                if (troops == null) continue;
                if (CheckSend(from, target, troops, CommandKind.Attack).Status == SendStatus.Ok) Send(from, target, troops, CommandKind.Attack);
            }
        }

        /// <summary>The raid cycles of every village of the player's (on the Account Manager's rounds).</summary>
        void RunCycles()
        {
            var human = HumanPlayer;
            if (human == null || !LootAssistantUnlocked || LootTargets.Count == 0) return;
            foreach (var t in LootTargets)
                if (t.Cycling && FindVillage(t.FromVillageId)?.OwnerId != human.Id)
                {
                    t.Cycling = false;
                    t.Stopped = "the village it raided from was lost.";
                }
            foreach (var v in new List<Village>(VillagesOf(human.Id))) RunCycle(v);
        }

        /// <summary>
        /// Notes how one of the player's attacks went, for the Loot Assistant: the result, the haul, and what any
        /// scouts saw. A cycle whose raid was beaten or lost more than a tenth of its troops stops.
        /// </summary>
        void NoteRaid(Command command, Village target, BattleReport report)
        {
            var t = LootTargetFor(target.Id, true);
            int sent = Total(report.AttackerSent), lost = Total(report.AttackerLost);
            t.LastRaidAt = Now;
            t.LastResult = !report.AttackerWon || lost >= sent ? RaidResult.Defeat : lost > 0 ? RaidResult.Losses : RaidResult.Clean;
            t.LastLoot = report.Loot;
            int hauled = report.Loot.Wood + report.Loot.Clay + report.Loot.Iron;
            t.FullHaul = report.AttackerWon && report.LootCapacity > 0 && hauled >= report.LootCapacity;
            if (report.Scouted && report.ScoutedLevels != null)
            {
                t.ScoutedAt = Now;
                t.ScoutedResources = report.ScoutedResources;
                t.ScoutedLevels = report.ScoutedLevels;
            }
            if (t.Cycling && t.FromVillageId == command.FromVillageId)
            {
                if (t.LastResult == RaidResult.Clean) report.Read = report.Routine = true;
                if (t.LastResult == RaidResult.Defeat) Stop(t, "the last raid was beaten.");
                else if (lost > sent * CycleLossLimit) Stop(t, $"the last raid lost {lost:N0} of {sent:N0} troops.");
            }
        }

        static void Stop(LootTarget t, string why)
        {
            t.Cycling = false;
            t.Stopped = why;
        }

        /// <summary>Unlocks the Loot Assistant (the first raid quest's reward).</summary>
        void UnlockLootAssistant()
        {
            LootAssistantUnlocked = true;
            EnsureLootTemplates();
        }

        /// <summary>Templates A and B as they should be: the defaults if never set, one count per kind of unit.</summary>
        void EnsureLootTemplates()
        {
            LootTemplateA = LootTemplateA == null || LootTemplateA.Length == 0 ? DefaultTemplateA() : Resized(LootTemplateA, Units.Count);
            LootTemplateB = LootTemplateB == null || LootTemplateB.Length == 0 ? DefaultTemplateB() : Resized(LootTemplateB, Units.Count);
        }
    }
}
