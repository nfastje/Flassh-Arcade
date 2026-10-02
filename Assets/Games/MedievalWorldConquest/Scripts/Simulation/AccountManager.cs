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

    /// <summary>
    /// A named set of troop targets (how many of each unit), for villages to follow. The game's own can be viewed
    /// and copied; the player's are kept in their profile, so every world they play has them.
    /// </summary>
    [Serializable]
    public class TroopTemplate
    {
        public string Name;
        public int[] Troops = new int[Units.Count];
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
        /// <summary>
        /// The troop template the village follows ("": none, its own numbers). Its numbers are kept in
        /// <see cref="TroopTargets"/> too, so the village carries on as it was if the template is deleted.
        /// </summary>
        public string TroopTemplate = "";
        /// <summary>
        /// The percentage of the village's spending that goes to troops (and their research) while it has both
        /// buildings and troops still to get: whichever side is behind its share gets first call on the stores.
        /// </summary>
        public int TroopShare = World.DefaultTroopShare;
        /// <summary>Recent spending on each side (fading by half every <see cref="World.ManagerMemorySeconds"/>), and when it was last updated.</summary>
        public double SpentOnBuildings, SpentOnTroops, SpentAt;
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

        /// <summary>A village's share of spending on troops until the player changes it (percent).</summary>
        public const int DefaultTroopShare = 50;

        /// <summary>The troop shares the player can choose from (percent).</summary>
        public static readonly int[] TroopShares = { 0, 25, 50, 75, 100 };

        /// <summary>
        /// How quickly the Account Manager forgets what it spent: half every two game days, so the split follows
        /// what the village has been doing lately (and a new share takes hold within days).
        /// </summary>
        public const double ManagerMemorySeconds = 2 * SecondsPerDay;

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

        /// <summary>
        /// The game's own build templates: an even economy first, or a village for defense or attack. Each pushes its
        /// Headquarters to 10 before the mines go past 7 (it speeds every build after it), walls up to 20 by the
        /// middle-to-late game, and ends with an academy once its requirements are met.
        /// </summary>
        public static readonly BuildTemplate[] BuiltInTemplates =
        {
            Template("Economy",
                (Timber, 2), (Clay, 2), (Iron, 2), (Store, 2), (Farm, 2), (HQ, 3), (Timber, 4), (Clay, 4), (Iron, 4), (Store, 4), (Farm, 4),
                (HQ, 5), (BuildingType.Barracks, 1), (Timber, 7), (Clay, 7), (Iron, 7), (Store, 7), (Farm, 7), (HQ, 10), (BuildingType.Market, 1),
                (BuildingType.Smithy, 1), (BuildingType.Wall, 3), (Timber, 10), (Clay, 10), (Iron, 10), (Store, 10), (Farm, 10), (BuildingType.Barracks, 5),
                (BuildingType.HidingPlace, 3), (Timber, 15), (Clay, 15), (Iron, 15), (Store, 15), (Farm, 15), (HQ, 15), (BuildingType.Market, 5),
                (BuildingType.Wall, 10), (BuildingType.Barracks, 10), (Timber, 20), (Clay, 20), (Iron, 20), (Store, 20), (Farm, 20), (HQ, 20),
                (BuildingType.Smithy, 10), (BuildingType.Wall, 15), (BuildingType.Market, 10), (Timber, 25), (Clay, 25), (Iron, 25), (Store, 25), (Farm, 25),
                (BuildingType.Smithy, 20), (BuildingType.Academy, 1), (BuildingType.Wall, 20), (Timber, 30), (Clay, 30), (Iron, 30), (Store, 30), (Farm, 30)),
            Template("Defensive",
                (Timber, 2), (Clay, 2), (Iron, 2), (Store, 2), (Farm, 2), (HQ, 3), (BuildingType.Barracks, 1), (BuildingType.Wall, 3),
                (Timber, 5), (Clay, 5), (Iron, 5), (Store, 5), (Farm, 5), (HQ, 5), (BuildingType.Smithy, 1), (BuildingType.Barracks, 5),
                (BuildingType.Wall, 10), (HQ, 10), (Timber, 10), (Clay, 10), (Iron, 10), (Store, 10), (Farm, 10), (BuildingType.Smithy, 5),
                (BuildingType.Market, 1), (BuildingType.Stable, 1), (BuildingType.Barracks, 10), (BuildingType.Wall, 15), (Timber, 15),
                (Clay, 15), (Iron, 15), (Store, 15), (Farm, 15), (BuildingType.Smithy, 10), (BuildingType.Stable, 10), (BuildingType.Wall, 20),
                (BuildingType.Market, 5), (Timber, 20), (Clay, 20), (Iron, 20), (Store, 20), (Farm, 20), (HQ, 20), (BuildingType.Barracks, 20),
                (BuildingType.Smithy, 20), (BuildingType.Market, 10), (BuildingType.Academy, 1),
                (Timber, 25), (Clay, 25), (Iron, 25), (Store, 25), (Farm, 30), (BuildingType.Barracks, 25), (Timber, 30), (Clay, 30), (Iron, 30), (Store, 30)),
            Template("Offensive",
                (Timber, 2), (Clay, 2), (Iron, 2), (Store, 2), (Farm, 2), (HQ, 3), (BuildingType.Barracks, 1), (Timber, 5), (Clay, 5),
                (Iron, 5), (Store, 5), (Farm, 5), (HQ, 5), (BuildingType.Smithy, 2), (BuildingType.Barracks, 5), (BuildingType.Wall, 5), (HQ, 10),
                (Timber, 10), (Clay, 10), (Iron, 10), (Store, 10), (Farm, 10), (BuildingType.Smithy, 5), (BuildingType.Market, 1),
                (BuildingType.Stable, 3), (BuildingType.Smithy, 10), (BuildingType.Workshop, 2), (BuildingType.Barracks, 10), (BuildingType.Stable, 10),
                (Timber, 15), (Clay, 15), (Iron, 15), (Store, 15), (Farm, 15), (HQ, 15), (BuildingType.Wall, 15), (BuildingType.Barracks, 15), (BuildingType.Stable, 15),
                (Timber, 20), (Clay, 20), (Iron, 20), (Store, 20), (Farm, 20), (HQ, 20), (BuildingType.Smithy, 20), (BuildingType.Market, 10),
                (BuildingType.Academy, 1), (BuildingType.Barracks, 20), (BuildingType.Stable, 20), (BuildingType.Workshop, 10), (Timber, 25), (Clay, 25), (Iron, 25),
                (Store, 25), (Farm, 30), (BuildingType.Wall, 20), (Timber, 30), (Clay, 30), (Iron, 30), (Store, 30)),
        };

        /// <summary>The game's own troop templates: a defensive village, or an attacking one.</summary>
        public static readonly TroopTemplate[] BuiltInTroopTemplates =
        {
            new TroopTemplate { Name = "Defensive", BuiltIn = true, Troops = Troops((UnitType.Spearman, 4000), (UnitType.Swordsman, 3000), (UnitType.Archer, 1500), (UnitType.Scout, 200), (UnitType.HeavyCavalry, 400)) },
            new TroopTemplate { Name = "Offensive", BuiltIn = true, Troops = Troops((UnitType.Axeman, 6000), (UnitType.LightCavalry, 2500), (UnitType.MountedArcher, 300), (UnitType.Scout, 200), (UnitType.Ram, 250), (UnitType.Catapult, 50)) },
        };

        /// <summary>
        /// The player's own troop templates. They live in the player's profile (shared by all their worlds), not
        /// in the world's save: the game hands them over when a world is opened (see <see cref="SyncTroopTemplates"/>).
        /// </summary>
        [NonSerialized] public List<TroopTemplate> CustomTroopTemplates = new List<TroopTemplate>();

        /// <summary>Every troop template: the game's own, then the player's.</summary>
        public List<TroopTemplate> AllTroopTemplates()
        {
            var all = new List<TroopTemplate>(BuiltInTroopTemplates);
            if (CustomTroopTemplates != null) all.AddRange(CustomTroopTemplates);
            return all;
        }

        public TroopTemplate FindTroopTemplate(string name) =>
            string.IsNullOrEmpty(name) ? null : AllTroopTemplates().Find(t => t.Name == name);

        /// <summary>A village follows a troop template (its targets become the template's), or none ("": keeps its numbers).</summary>
        public void SetVillageTroopTemplate(Village v, string name)
        {
            if (v == null || v.OwnerId != HumanPlayer?.Id) return;
            var m = ManagementOf(v.Id, true);
            var t = FindTroopTemplate(name);
            m.TroopTemplate = t?.Name ?? "";
            if (t != null) m.TroopTargets = Resized((int[])t.Troops.Clone(), Units.Count);
            Manage(v);
        }

        /// <summary>
        /// Brings the villages following troop templates up to date with them: after a template is edited, or when
        /// a world is opened with the profile's templates. A village whose template is gone keeps its numbers.
        /// </summary>
        public void SyncTroopTemplates()
        {
            if (ManagedVillages == null) return;
            foreach (var m in ManagedVillages)
            {
                if (string.IsNullOrEmpty(m.TroopTemplate)) continue;
                var t = FindTroopTemplate(m.TroopTemplate);
                if (t == null) m.TroopTemplate = "";
                else m.TroopTargets = Resized((int[])t.Troops.Clone(), Units.Count);
            }
        }

        /// <summary>
        /// A new troop template of the player's, copied from another (or empty). Returns why not, or null. Names
        /// must be unique among the troop templates, and not empty.
        /// </summary>
        public string CreateTroopTemplate(string name, string copyFrom)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "Give the template a name.";
            if (name.Length > 24) name = name.Substring(0, 24).TrimEnd();
            if (FindTroopTemplate(name) != null) return "There's already a troop template by that name.";
            var t = new TroopTemplate { Name = name };
            var from = FindTroopTemplate(copyFrom);
            if (from != null) t.Troops = Resized((int[])from.Troops.Clone(), Units.Count);
            if (CustomTroopTemplates == null) CustomTroopTemplates = new List<TroopTemplate>();
            CustomTroopTemplates.Add(t);
            return null;
        }

        /// <summary>Deletes one of the player's troop templates; villages that followed it keep its numbers as their own.</summary>
        public bool DeleteTroopTemplate(string name)
        {
            var t = CustomTroopTemplates?.Find(x => x.Name == name);
            if (t == null) return false;
            CustomTroopTemplates.Remove(t);
            SyncTroopTemplates();
            return true;
        }

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

        /// <summary>Sets a village's troop targets, and the share of its spending that goes to troops (percent).</summary>
        /// <remarks>
        /// Numbers different from the village's troop template's make them its own (it stops following the
        /// template); the same numbers (only the share changed, say) keep it following.
        /// </remarks>
        public void SetTroopTargets(Village v, int[] targets, int troopShare = DefaultTroopShare)
        {
            if (v == null || v.OwnerId != HumanPlayer?.Id || targets == null) return;
            var m = ManagementOf(v.Id, true);
            m.TroopTargets = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < targets.Length; i++) m.TroopTargets[i] = Math.Max(0, targets[i]);
            m.TroopShare = Math.Max(0, Math.Min(100, troopShare));
            var t = FindTroopTemplate(m.TroopTemplate);
            if (t == null || !SameTroops(t.Troops, m.TroopTargets)) m.TroopTemplate = "";
            Manage(v);
        }

        static bool SameTroops(int[] a, int[] b)
        {
            for (int i = 0; i < Units.Count; i++)
                if ((a != null && i < a.Length ? a[i] : 0) != (b != null && i < b.Length ? b[i] : 0)) return false;
            return true;
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

        /// <summary>
        /// One round of the Account Manager for a village. While it has both buildings and troops to get, spending
        /// is split by the village's troop share: whichever side is behind its share goes first, and the other gets
        /// what's left (so troops are trained even while a building is being saved up for, and the other way round).
        /// With nothing left to build, troops get everything; and stores about to overflow go on troops rather than waste.
        /// </summary>
        public void Manage(Village v)
        {
            var m = ManagementOf(v.Id);
            if (m == null || v.OwnerId != HumanPlayer?.Id) return;
            Touch(v);
            ForgetOldSpending(m);
            var template = FindTemplate(m.Template);
            bool building = template != null && NextStep(v, template) != null;
            bool troopsFirst = !building || TroopsDue(m);
            if (troopsFirst) ManageTroops(v, m);
            bool buildingWaits = building && ManageConstruction(v, template, m);
            if (!troopsFirst && (!buildingWaits || FullestStock(v) > 0.9)) ManageTroops(v, m);
        }

        /// <summary>Whether troops are behind their share of the village's recent spending.</summary>
        static bool TroopsDue(ManagedVillage m)
        {
            double share = m.TroopShare / 100.0;
            if (share <= 0) return false;
            if (share >= 1) return true;
            return m.SpentOnTroops < share * (m.SpentOnBuildings + m.SpentOnTroops);
        }

        void ForgetOldSpending(ManagedVillage m)
        {
            double fade = Math.Pow(0.5, Math.Max(0, Now - m.SpentAt) / ManagerMemorySeconds);
            m.SpentOnBuildings *= fade;
            m.SpentOnTroops *= fade;
            m.SpentAt = Now;
        }

        static double Spent(Cost c) => (double)c.Wood + c.Clay + c.Iron;

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
        bool ManageConstruction(Village v, BuildTemplate template, ManagedVillage m)
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
                if (QueueBuild(v, type).Status == BuildStatus.Ok) m.SpentOnBuildings += Spent(check.Cost);
            }
            return false;
        }

        /// <summary>
        /// Recruits towards the village's troop targets, researching at the smithy what it must first, and making
        /// room for them at the farm when it's full (on the troops' side of the spending).
        /// </summary>
        void ManageTroops(Village v, ManagedVillage m)
        {
            if (m.TroopTargets == null) return;
            var have = ArmyOf(v, stationed: true);
            bool wanted = false;
            for (int i = 0; i < Units.Count && i < m.TroopTargets.Length; i++) wanted |= have[i] < m.TroopTargets[i];
            if (!wanted) return;
            if (v.FreePopulation < Math.Max(20, v.PopulationCapacity * 0.05) && v.QueuedCount(BuildingType.Farm) == 0 && v.Queue.Count < MaxBuildQueue)
            {
                var room = CheckBuild(v, BuildingType.Farm).Status == BuildStatus.WarehouseTooSmall ? BuildingType.Warehouse : BuildingType.Farm;
                if (v.QueuedCount(room) == 0 && CheckBuild(v, room).Status == BuildStatus.Ok)
                {
                    var built = QueueBuild(v, room);
                    if (built.Status == BuildStatus.Ok) m.SpentOnTroops += Spent(built.Cost);
                }
            }
            // The units still wanted that can be trained (the others researched first).
            var due = new List<(UnitType type, int missing, double weight)>();
            var perBuilding = new Dictionary<BuildingType, int>();
            foreach (var type in Units.InDisplayOrder)
            {
                int target = (int)type < m.TroopTargets.Length ? m.TroopTargets[(int)type] : 0;
                if (target <= 0 || have[(int)type] >= target) continue;
                if (!v.IsResearched(type))
                {
                    if (!v.IsBeingResearched(type) && CheckResearch(v, type).Status == ResearchStatus.Ok)
                    {
                        var research = StartResearch(v, type);
                        if (research.Status == ResearchStatus.Ok) m.SpentOnTroops += Spent(research.Cost);
                    }
                    continue;
                }
                int missing = target - have[(int)type];
                due.Add((type, missing, missing * Spent(UnitCost(type))));
                var building = Units.Get(type).Building;
                perBuilding[building] = perBuilding.TryGetValue(building, out int n) ? n + 1 : 1;
            }
            if (due.Count == 0) return;

            // Each gets a share of what's in store in proportion to what it still needs (in resources), so they all
            // grow towards their targets together rather than one after another; and each building may queue a
            // batch for each of its units (at least two).
            double total = 0;
            foreach (var d in due) total += d.weight;
            double wood = v.Wood, clay = v.Clay, iron = v.Iron, people = v.FreePopulation;
            foreach (var (type, missing, weight) in due)
            {
                var building = Units.Get(type).Building;
                if (QueuedBatches(v, building) >= Math.Min(MaxRecruitQueue, Math.Max(2, perBuilding[building]))) continue;
                double share = total > 0 ? weight / total : 0;
                int count = Math.Min(missing, Math.Min(MaxAffordable(v, type), AffordableWith(wood * share, clay * share, iron * share, people * share, UnitCost(type))));
                if (count <= 0 || CheckRecruit(v, type, count).Status != RecruitStatus.Ok) continue;
                var recruited = Recruit(v, type, count);
                if (recruited.Status == RecruitStatus.Ok) m.SpentOnTroops += Spent(recruited.Total);
            }
        }

        /// <summary>How many of a unit some resources and room at the farm pay for.</summary>
        static int AffordableWith(double wood, double clay, double iron, double people, Cost each)
        {
            double most = int.MaxValue;
            if (each.Wood > 0) most = Math.Min(most, wood / each.Wood);
            if (each.Clay > 0) most = Math.Min(most, clay / each.Clay);
            if (each.Iron > 0) most = Math.Min(most, iron / each.Iron);
            if (each.Population > 0) most = Math.Min(most, people / each.Population);
            return (int)Math.Max(0, Math.Floor(most));
        }

        /// <summary>What the Account Manager is doing in a village, in words.</summary>
        public string ManagerStatus(Village v)
        {
            var m = ManagementOf(v.Id);
            var template = m == null ? null : FindTemplate(m.Template);
            if (template == null)
            {
                string alone = m == null ? null : TroopWarning(v, m, null);
                return alone == null ? "No build template." : $"No build template.  ·  {alone}";
            }
            string warning = m == null ? null : TroopWarning(v, m, template);
            string building;
            var step = NextStep(v, template);
            if (step == null) building = $"{template.Name}: finished.";
            else
            {
                var check = CheckBuild(v, step.Building);
                string what = $"{Buildings.Get(step.Building).Name} {step.Level}";
                building = check.Status switch
                {
                    BuildStatus.QueueFull => $"Next: {what} (the queue is full)",
                    BuildStatus.NotEnoughResources => $"Next: {what} (saving up)",
                    BuildStatus.NeedsBuilding => $"Next: {what} (first {Buildings.Get(check.Required.Building).Name} {check.Required.Level})",
                    BuildStatus.FarmTooSmall => $"Next: {what} (first a bigger farm)",
                    BuildStatus.WarehouseTooSmall => $"Next: {what} (first a bigger warehouse)",
                    _ => $"Next: {what}",
                };
            }
            return warning == null ? building : $"{building}  ·  {warning}";
        }

        /// <summary>
        /// A troop target the village can't train and its build template will never make possible (light cavalry with
        /// no stable to come, say), in words, or null: the Manager says so rather than build what wasn't asked for.
        /// </summary>
        string TroopWarning(Village v, ManagedVillage m, BuildTemplate template)
        {
            if (m.TroopTargets == null) return null;
            foreach (var type in Units.InDisplayOrder)
            {
                int target = (int)type < m.TroopTargets.Length ? m.TroopTargets[(int)type] : 0;
                if (target <= 0) continue;
                var u = Units.Get(type);
                int need = Math.Max(1, u.RequiredLevel);
                if (v.Level(u.Building) + v.QueuedCount(u.Building) >= need) continue;
                int planned = 0;
                if (template != null)
                    foreach (var s in template.Steps)
                        if (s.Building == u.Building) planned = Math.Max(planned, s.Level);
                if (planned >= need) continue;
                return $"Can't train {u.Name}: needs {Buildings.Get(u.Building).Name} {need}";
            }
            return null;
        }
    }
}
