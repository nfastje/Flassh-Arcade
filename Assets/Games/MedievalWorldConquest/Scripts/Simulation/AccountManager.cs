using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>One step of a build template: a building, to this level.</summary>
    [Serializable]
    public class BuildStep
    {
        public BuildingType Building;
        public int Level;

        public BuildStep() { }
        public BuildStep(BuildingType building, int level)
        {
            Building = building;
            Level = level;
        }
    }

    /// <summary>A named build order: steps taken in turn, each once its level is reached.</summary>
    [Serializable]
    public class BuildTemplate
    {
        public string Name;
        public List<BuildStep> Steps = new List<BuildStep>();
        /// <summary>One of the game's own (they can be copied, not changed).</summary>
        [NonSerialized] public bool BuiltIn;
    }

    /// <summary>What the Account Manager does for one of the player's villages.</summary>
    [Serializable]
    public class ManagedVillage
    {
        public int VillageId;
        /// <summary>The build template it follows ("": none).</summary>
        public string Template = "";
        /// <summary>How many of each unit the village should have (at home, out, or in training); 0: none wanted.</summary>
        public int[] TroopTargets = new int[Units.Count];
    }

    /// <summary>
    /// The Account Manager, as in Tribal Wars: for each of the player's villages, a build template whose steps are
    /// queued in order whenever there's room in the construction queue and the resources (a farm or warehouse too
    /// small is upgraded first), and troop targets recruited towards (researching what's needed at the smithy) with
    /// what the buildings don't need. It runs in the world itself every quarter of a game hour, so it keeps working
    /// while a real-time world catches up on time the game was closed.
    /// </summary>
    public partial class World
    {
        /// <summary>Game seconds between the Account Manager's rounds.</summary>
        public const double ManagerTickSeconds = 15 * 60;

        public List<ManagedVillage> ManagedVillages = new List<ManagedVillage>();
        public List<BuildTemplate> CustomTemplates = new List<BuildTemplate>();

        static BuildTemplate Template(string name, params (BuildingType b, int level)[] steps)
        {
            var t = new BuildTemplate { Name = name, BuiltIn = true };
            foreach (var (b, level) in steps) t.Steps.Add(new BuildStep(b, level));
            return t;
        }

        const BuildingType Timber = BuildingType.TimberCamp, Clay = BuildingType.ClayPit, Iron = BuildingType.IronMine,
            Farm = BuildingType.Farm, Store = BuildingType.Warehouse, HQ = BuildingType.Headquarters;

        /// <summary>The game's own build templates: an even economy first, or a village for defense or attack.</summary>
        public static readonly BuildTemplate[] BuiltInTemplates =
        {
            Template("Economy",
                (Timber, 2), (Clay, 2), (Iron, 2), (Store, 2), (Farm, 2), (HQ, 3), (Timber, 4), (Clay, 4), (Iron, 4), (Store, 4), (Farm, 4),
                (HQ, 5), (BuildingType.Barracks, 1), (Timber, 7), (Clay, 7), (Iron, 7), (Store, 7), (Farm, 7), (BuildingType.Market, 1),
                (BuildingType.Smithy, 1), (BuildingType.Wall, 3), (Timber, 10), (Clay, 10), (Iron, 10), (Store, 10), (Farm, 10), (HQ, 10),
                (BuildingType.HidingPlace, 3), (Timber, 15), (Clay, 15), (Iron, 15), (Store, 15), (Farm, 15), (HQ, 15), (BuildingType.Market, 5),
                (BuildingType.Wall, 10), (Timber, 20), (Clay, 20), (Iron, 20), (Store, 20), (Farm, 20), (HQ, 20), (BuildingType.Smithy, 10),
                (BuildingType.Market, 10), (Timber, 25), (Clay, 25), (Iron, 25), (Store, 25), (Farm, 25), (BuildingType.Smithy, 20),
                (Timber, 30), (Clay, 30), (Iron, 30), (Store, 30), (Farm, 30), (BuildingType.Wall, 20)),
            Template("Defensive",
                (Timber, 2), (Clay, 2), (Iron, 2), (Store, 2), (Farm, 2), (HQ, 3), (BuildingType.Barracks, 1), (BuildingType.Wall, 3),
                (Timber, 5), (Clay, 5), (Iron, 5), (Store, 5), (Farm, 5), (HQ, 5), (BuildingType.Smithy, 1), (BuildingType.Barracks, 5),
                (BuildingType.Wall, 10), (Timber, 10), (Clay, 10), (Iron, 10), (Store, 10), (Farm, 10), (HQ, 10), (BuildingType.Smithy, 5),
                (BuildingType.Market, 1), (BuildingType.Stable, 1), (BuildingType.Barracks, 10), (BuildingType.Wall, 15), (Timber, 15),
                (Clay, 15), (Iron, 15), (Store, 15), (Farm, 15), (BuildingType.Smithy, 10), (BuildingType.Stable, 10), (BuildingType.Wall, 20),
                (Timber, 20), (Clay, 20), (Iron, 20), (Store, 20), (Farm, 20), (HQ, 20), (BuildingType.Barracks, 20), (BuildingType.Smithy, 20),
                (Timber, 25), (Clay, 25), (Iron, 25), (Store, 25), (Farm, 30), (BuildingType.Barracks, 25), (Timber, 30), (Clay, 30), (Iron, 30), (Store, 30)),
            Template("Offensive",
                (Timber, 2), (Clay, 2), (Iron, 2), (Store, 2), (Farm, 2), (HQ, 3), (BuildingType.Barracks, 1), (Timber, 5), (Clay, 5),
                (Iron, 5), (Store, 5), (Farm, 5), (HQ, 5), (BuildingType.Smithy, 2), (BuildingType.Barracks, 5), (BuildingType.Wall, 5),
                (Timber, 10), (Clay, 10), (Iron, 10), (Store, 10), (Farm, 10), (HQ, 10), (BuildingType.Smithy, 5), (BuildingType.Market, 1),
                (BuildingType.Stable, 3), (BuildingType.Smithy, 10), (BuildingType.Workshop, 2), (BuildingType.Barracks, 10), (BuildingType.Stable, 10),
                (Timber, 15), (Clay, 15), (Iron, 15), (Store, 15), (Farm, 15), (HQ, 15), (BuildingType.Barracks, 15), (BuildingType.Stable, 15),
                (Timber, 20), (Clay, 20), (Iron, 20), (Store, 20), (Farm, 20), (HQ, 20), (BuildingType.Smithy, 20), (BuildingType.Market, 10),
                (BuildingType.Barracks, 20), (BuildingType.Stable, 20), (BuildingType.Workshop, 10), (Timber, 25), (Clay, 25), (Iron, 25),
                (Store, 25), (Farm, 30), (Timber, 30), (Clay, 30), (Iron, 30), (Store, 30), (BuildingType.Wall, 20)),
        };

        /// <summary>Troop targets to start from: a defensive village, or an attacking one.</summary>
        public static readonly (string name, int[] troops)[] TroopPresets =
        {
            ("Defensive", Troops((UnitType.Spearman, 4000), (UnitType.Swordsman, 3000), (UnitType.Archer, 1500), (UnitType.Scout, 200), (UnitType.HeavyCavalry, 400))),
            ("Offensive", Troops((UnitType.Axeman, 6000), (UnitType.LightCavalry, 2500), (UnitType.MountedArcher, 300), (UnitType.Scout, 200), (UnitType.Ram, 250), (UnitType.Catapult, 50))),
        };

        static int[] Troops(params (UnitType type, int count)[] units)
        {
            var troops = new int[Units.Count];
            foreach (var (type, count) in units) troops[(int)type] = count;
            return troops;
        }

        /// <summary>Every template: the game's own, then the player's.</summary>
        public List<BuildTemplate> AllTemplates()
        {
            var all = new List<BuildTemplate>(BuiltInTemplates);
            if (CustomTemplates != null) all.AddRange(CustomTemplates);
            return all;
        }

        public BuildTemplate FindTemplate(string name) =>
            string.IsNullOrEmpty(name) ? null : AllTemplates().Find(t => t.Name == name);

        /// <summary>A village's Account Manager settings (made if asked for and there are none).</summary>
        public ManagedVillage ManagementOf(int villageId, bool create = false)
        {
            if (ManagedVillages == null) ManagedVillages = new List<ManagedVillage>();
            var m = ManagedVillages.Find(x => x.VillageId == villageId);
            if (m == null && create) ManagedVillages.Add(m = new ManagedVillage { VillageId = villageId });
            return m;
        }

        // ---------------------------------------------------------------- the player's settings

        public void SetVillageTemplate(Village v, string templateName)
        {
            if (v == null || v.OwnerId != HumanPlayer?.Id) return;
            ManagementOf(v.Id, true).Template = FindTemplate(templateName)?.Name ?? "";
            Manage(v);
        }

        public void SetTroopTargets(Village v, int[] targets)
        {
            if (v == null || v.OwnerId != HumanPlayer?.Id || targets == null) return;
            var m = ManagementOf(v.Id, true);
            m.TroopTargets = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < targets.Length; i++) m.TroopTargets[i] = Math.Max(0, targets[i]);
            Manage(v);
        }

        /// <summary>
        /// A new template of the player's, copied from another (or empty). Returns why not, or null. Names must be
        /// unique and not empty.
        /// </summary>
        public string CreateTemplate(string name, string copyFrom)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "Give the template a name.";
            if (name.Length > 24) name = name.Substring(0, 24).TrimEnd();
            if (FindTemplate(name) != null) return "There's already a template by that name.";
            var t = new BuildTemplate { Name = name };
            var from = FindTemplate(copyFrom);
            if (from != null) foreach (var s in from.Steps) t.Steps.Add(new BuildStep(s.Building, s.Level));
            if (CustomTemplates == null) CustomTemplates = new List<BuildTemplate>();
            CustomTemplates.Add(t);
            return null;
        }

        /// <summary>Deletes one of the player's templates; villages that followed it follow none.</summary>
        public bool DeleteTemplate(string name)
        {
            var t = CustomTemplates?.Find(x => x.Name == name);
            if (t == null) return false;
            CustomTemplates.Remove(t);
            foreach (var m in ManagedVillages)
                if (m.Template == name) m.Template = "";
            return true;
        }

        // ---------------------------------------------------------------- running it

        void ScheduleManagerTick() => Schedule(ManagerTickSeconds, EventKind.ManagerTick);

        void ManagerTick(ScheduledEvent e)
        {
            ScheduleManagerTick();
            RunCycles();
            var human = HumanPlayer;
            if (human == null || ManagedVillages == null || ManagedVillages.Count == 0) return;
            foreach (var v in new List<Village>(VillagesOf(human.Id))) Manage(v);
        }

        /// <summary>One round of the Account Manager for a village: construction first, then troops with what's left.</summary>
        public void Manage(Village v)
        {
            var m = ManagementOf(v.Id);
            if (m == null || v.OwnerId != HumanPlayer?.Id) return;
            Touch(v);
            var template = FindTemplate(m.Template);
            bool buildingWaits = template != null && ManageConstruction(v, template);
            // Troops get the resources the buildings aren't waiting for.
            if (!buildingWaits) ManageTroops(v, m);
        }

        /// <summary>The next step of a template the village hasn't reached (counting what's queued), or null if it's done.</summary>
        public BuildStep NextStep(Village v, BuildTemplate template)
        {
            if (template == null) return null;
            foreach (var s in template.Steps)
            {
                int level = Math.Min(s.Level, Buildings.Get(s.Building).MaxLevel);
                if (v.NextLevel(s.Building) - 1 < level) return s;
            }
            return null;
        }

        /// <summary>
        /// Queues template steps while there's room. Returns whether construction is waiting for resources (so troops
        /// shouldn't spend them).
        /// </summary>
        bool ManageConstruction(Village v, BuildTemplate template)
        {
            for (int guard = 0; guard < MaxBuildQueue && v.Queue.Count < MaxBuildQueue; guard++)
            {
                var step = NextStep(v, template);
                if (step == null) return false;
                var type = step.Building;
                var check = CheckBuild(v, type);
                // What's in the way first (and what's in its way): a farm or warehouse too small, or a building the
                // step needs.
                for (int depth = 0; depth < 4; depth++)
                {
                    if (check.Status == BuildStatus.FarmTooSmall) type = BuildingType.Farm;
                    else if (check.Status == BuildStatus.WarehouseTooSmall) type = BuildingType.Warehouse;
                    else if (check.Status == BuildStatus.NeedsBuilding) type = check.Required.Building;
                    else break;
                    check = CheckBuild(v, type);
                }
                if (check.Status == BuildStatus.NotEnoughResources) return true;
                if (check.Status != BuildStatus.Ok) return false;
                QueueBuild(v, type);
            }
            return false;
        }

        /// <summary>Recruits towards the village's troop targets, researching at the smithy what it must first.</summary>
        void ManageTroops(Village v, ManagedVillage m)
        {
            if (m.TroopTargets == null) return;
            var have = ArmyOf(v);
            foreach (var type in Units.InDisplayOrder)
            {
                int target = (int)type < m.TroopTargets.Length ? m.TroopTargets[(int)type] : 0;
                if (target <= 0 || have[(int)type] >= target) continue;
                if (!v.IsResearched(type))
                {
                    if (!v.IsBeingResearched(type) && CheckResearch(v, type).Status == ResearchStatus.Ok) StartResearch(v, type);
                    continue;
                }
                // A couple of batches at a time per building keeps it busy without tying up everything.
                var building = Units.Get(type).Building;
                if (v.Recruitment.FindAll(o => o.Building == building).Count >= 2) continue;
                int count = Math.Min(target - have[(int)type], MaxAffordable(v, type));
                if (count > 0 && CheckRecruit(v, type, count).Status == RecruitStatus.Ok) Recruit(v, type, count);
            }
        }

        /// <summary>What the Account Manager is doing in a village, in words.</summary>
        public string ManagerStatus(Village v)
        {
            var m = ManagementOf(v.Id);
            var template = m == null ? null : FindTemplate(m.Template);
            if (template == null) return "No build template.";
            var step = NextStep(v, template);
            if (step == null) return $"{template.Name}: finished.";
            var check = CheckBuild(v, step.Building);
            string what = $"{Buildings.Get(step.Building).Name} {step.Level}";
            return check.Status switch
            {
                BuildStatus.QueueFull => $"Next: {what} (the queue is full)",
                BuildStatus.NotEnoughResources => $"Next: {what} (saving up)",
                BuildStatus.NeedsBuilding => $"Next: {what} (first {Buildings.Get(check.Required.Building).Name} {check.Required.Level})",
                BuildStatus.FarmTooSmall => $"Next: {what} (first a bigger farm)",
                BuildStatus.WarehouseTooSmall => $"Next: {what} (first a bigger warehouse)",
                _ => $"Next: {what}",
            };
        }
    }
}
