using System;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>Whether a building can be upgraded right now, and if not, why.</summary>
    public enum BuildStatus
    {
        Ok,
        MaxLevel,
        QueueFull,
        NeedsBuilding,
        FarmTooSmall,
        WarehouseTooSmall,
        NotEnoughResources,
    }

    /// <summary>Everything the UI needs to show about the next upgrade of a building.</summary>
    public struct BuildCheck
    {
        public BuildStatus Status;
        public int TargetLevel;
        public Cost Cost;
        /// <summary>Game seconds the upgrade takes.</summary>
        public double Seconds;
        /// <summary>For <see cref="BuildStatus.NotEnoughResources"/>: game seconds until production covers the cost.</summary>
        public double AffordableIn;
        /// <summary>For <see cref="BuildStatus.NeedsBuilding"/>: the building and level still required.</summary>
        public Requirement Required;
    }

    /// <summary>Building upgrades: checking, queueing, canceling and completing them.</summary>
    public partial class World
    {
        /// <summary>How many upgrades a village can have queued at once, including the one under construction.</summary>
        public const int MaxBuildQueue = 3;

        public BuildCheck CheckBuild(Village v, BuildingType type)
        {
            Touch(v);
            var def = Buildings.Get(type);
            int target = v.NextLevel(type);
            var check = new BuildCheck { TargetLevel = target };
            var unmet = Buildings.UnmetRequirement(type, v);

            if (target > def.MaxLevel)
            {
                check.Status = BuildStatus.MaxLevel;
                return check;
            }

            check.Cost = Buildings.CostOf(type, target);
            check.Seconds = Buildings.BuildSeconds(type, target, v.Level(BuildingType.Headquarters));

            // A locked building says what it needs, rather than that the queue happens to be full.
            if (unmet.HasValue)
            {
                check.Status = BuildStatus.NeedsBuilding;
                check.Required = unmet.Value;
            }
            else if (v.Queue.Count >= MaxBuildQueue) check.Status = BuildStatus.QueueFull;
            // Only upgrades that need more workers are blocked by the farm, so a village over its limit can still grow its farm.
            else if (check.Cost.Population > 0 && v.PopulationUsed + check.Cost.Population > v.PopulationCapacity) check.Status = BuildStatus.FarmTooSmall;
            else if (Math.Max(check.Cost.Wood, Math.Max(check.Cost.Clay, check.Cost.Iron)) > v.StorageCapacity) check.Status = BuildStatus.WarehouseTooSmall;
            else if (!v.CanAfford(check.Cost))
            {
                check.Status = BuildStatus.NotEnoughResources;
                check.AffordableIn = SecondsUntilAffordable(v, check.Cost);
            }
            else check.Status = BuildStatus.Ok;
            return check;
        }

        static double SecondsUntilAffordable(Village v, Cost cost)
        {
            double wait = 0;
            foreach (ResourceType r in Enum.GetValues(typeof(ResourceType)))
            {
                double missing = cost.Get(r) - v.Stock(r);
                if (missing > 0) wait = Math.Max(wait, missing / v.ProductionPerHour(r) * 3600);
            }
            return wait;
        }

        /// <summary>Pays for and queues the next upgrade of a building. Returns the check, whose status says whether it worked.</summary>
        public BuildCheck QueueBuild(Village v, BuildingType type)
        {
            var check = CheckBuild(v, type);
            if (check.Status != BuildStatus.Ok) return check;

            v.Wood -= check.Cost.Wood;
            v.Clay -= check.Cost.Clay;
            v.Iron -= check.Cost.Iron;
            v.Queue.Add(new BuildOrder { Id = v.NextOrderId++, Type = type, Level = check.TargetLevel, Seconds = check.Seconds, Paid = check.Cost });
            if (v.Queue.Count == 1) StartNextBuild(v);
            return check;
        }

        /// <summary>
        /// Cancels the last queued upgrade and refunds it in full (it may overflow the warehouse, like any refund).
        /// Only the last one can be canceled, since later orders depend on the levels before them.
        /// </summary>
        public bool CancelLastBuild(Village v)
        {
            if (v.Queue.Count == 0) return false;
            Touch(v);
            var order = v.Queue[v.Queue.Count - 1];
            v.Queue.RemoveAt(v.Queue.Count - 1);
            v.Wood += order.Paid.Wood;
            v.Clay += order.Paid.Clay;
            v.Iron += order.Paid.Iron;
            // If it was under construction, its completion event is still queued; it'll be ignored as stale.
            return true;
        }

        void StartNextBuild(Village v)
        {
            if (v.Queue.Count == 0) return;
            var order = v.Queue[0];
            if (order.Started) return;
            order.FinishTime = Now + order.Seconds;
            Schedule(order.Seconds, EventKind.BuildingComplete, v.Id, order.Id);
        }

        void CompleteBuild(ScheduledEvent e)
        {
            var v = FindVillage(e.VillageId);
            // Ignore stale events (the order was canceled after it started).
            if (v == null || v.Queue.Count == 0 || v.Queue[0].Id != e.A) return;

            var order = v.Queue[0];
            v.Queue.RemoveAt(0);
            Touch(v); // production up to now at the old level
            v.Levels[(int)order.Type] = order.Level;
            StartNextBuild(v);
        }
    }
}
