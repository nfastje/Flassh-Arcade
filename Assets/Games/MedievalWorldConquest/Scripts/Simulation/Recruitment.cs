using System;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>Whether units can be recruited right now, and if not, why.</summary>
    public enum RecruitStatus
    {
        Ok,
        InvalidCount,
        NeedsBuilding,
        QueueFull,
        FarmTooSmall,
        NotEnoughResources,
    }

    /// <summary>Everything the UI needs to show about recruiting some number of a unit.</summary>
    public struct RecruitCheck
    {
        public RecruitStatus Status;
        public int Count;
        /// <summary>Price of the whole batch (population included).</summary>
        public Cost Total;
        /// <summary>Game seconds per unit, and for the whole batch.</summary>
        public double SecondsEach, SecondsTotal;
        /// <summary>The most that could be recruited right now with the resources and population available.</summary>
        public int MaxAffordable;
        /// <summary>For <see cref="RecruitStatus.NeedsBuilding"/>: the building and level still required.</summary>
        public Requirement Required;
        /// <summary>For <see cref="RecruitStatus.NotEnoughResources"/>: game seconds until production covers the cost.</summary>
        public double AffordableIn;
    }

    /// <summary>Training troops: checking, ordering, cancelling, and units finishing one at a time.</summary>
    public partial class World
    {
        /// <summary>How many batches each training building can have queued at once, including the one in training.</summary>
        public const int MaxRecruitQueue = 5;

        public static Cost Multiply(Cost each, int count) =>
            new Cost(each.Wood * count, each.Clay * count, each.Iron * count, each.Population * count);

        /// <summary>The most of a unit the village can afford right now, by resources and by free population.</summary>
        public static int MaxAffordable(Village v, UnitType unit)
        {
            var c = Units.Get(unit).Cost;
            long max = int.MaxValue;
            if (c.Wood > 0) max = Math.Min(max, (long)(v.Wood / c.Wood));
            if (c.Clay > 0) max = Math.Min(max, (long)(v.Clay / c.Clay));
            if (c.Iron > 0) max = Math.Min(max, (long)(v.Iron / c.Iron));
            if (c.Population > 0) max = Math.Min(max, v.FreePopulation / c.Population);
            return (int)Math.Max(0, max);
        }

        public int QueuedBatches(Village v, BuildingType building)
        {
            int n = 0;
            foreach (var o in v.Recruitment)
                if (o.Building == building) n++;
            return n;
        }

        public RecruitCheck CheckRecruit(Village v, UnitType unit, int count)
        {
            Touch(v);
            var def = Units.Get(unit);
            int buildingLevel = v.Level(def.Building);
            var check = new RecruitCheck
            {
                Count = count,
                Total = Multiply(def.Cost, Math.Max(0, count)),
                SecondsEach = Units.SecondsToTrain(unit, buildingLevel),
                MaxAffordable = MaxAffordable(v, unit),
            };
            check.SecondsTotal = check.SecondsEach * Math.Max(0, count);

            if (buildingLevel < def.RequiredLevel)
            {
                check.Status = RecruitStatus.NeedsBuilding;
                check.Required = new Requirement(def.Building, def.RequiredLevel);
            }
            else if (count <= 0) check.Status = RecruitStatus.InvalidCount;
            else if (QueuedBatches(v, def.Building) >= MaxRecruitQueue) check.Status = RecruitStatus.QueueFull;
            else if (check.Total.Population > v.FreePopulation) check.Status = RecruitStatus.FarmTooSmall;
            else if (!v.CanAfford(check.Total))
            {
                check.Status = RecruitStatus.NotEnoughResources;
                check.AffordableIn = Math.Max(check.Total.Wood, Math.Max(check.Total.Clay, check.Total.Iron)) > v.StorageCapacity
                    ? double.PositiveInfinity // more than the warehouse holds: recruit fewer at a time
                    : SecondsUntilAffordable(v, check.Total);
            }
            else check.Status = RecruitStatus.Ok;
            return check;
        }

        /// <summary>Pays for and queues a batch of units. Returns the check, whose status says whether it worked.</summary>
        public RecruitCheck Recruit(Village v, UnitType unit, int count)
        {
            var check = CheckRecruit(v, unit, count);
            if (check.Status != RecruitStatus.Ok) return check;

            v.Wood -= check.Total.Wood;
            v.Clay -= check.Total.Clay;
            v.Iron -= check.Total.Iron;
            var building = Units.Get(unit).Building;
            v.Recruitment.Add(new RecruitOrder
            {
                Id = v.NextOrderId++,
                Unit = unit,
                Building = building,
                Total = count,
                SecondsEach = check.SecondsEach,
            });
            StartNextRecruit(v, building);
            return check;
        }

        /// <summary>
        /// Cancels a batch and refunds the units not yet trained (units already trained stay). If it was in training,
        /// the building moves on to its next batch.
        /// </summary>
        public bool CancelRecruit(Village v, int orderId)
        {
            int index = v.Recruitment.FindIndex(o => o.Id == orderId);
            if (index < 0) return false;
            Touch(v);
            var order = v.Recruitment[index];
            v.Recruitment.RemoveAt(index);

            var refund = Multiply(Units.Get(order.Unit).Cost, order.Remaining);
            v.Wood += refund.Wood;
            v.Clay += refund.Clay;
            v.Iron += refund.Iron;
            // Its next-unit event, if any, is now stale and will be ignored.
            if (order.Started) StartNextRecruit(v, order.Building);
            return true;
        }

        /// <summary>The batch a building is working on (or will work on next), if any.</summary>
        public static RecruitOrder ActiveRecruit(Village v, BuildingType building) =>
            v.Recruitment.Find(o => o.Building == building);

        void StartNextRecruit(Village v, BuildingType building)
        {
            var order = ActiveRecruit(v, building);
            if (order == null || order.Started) return;
            order.NextAt = Now + order.SecondsEach;
            Schedule(order.SecondsEach, EventKind.UnitTrained, v.Id, order.Id);
        }

        void TrainUnit(ScheduledEvent e)
        {
            var v = FindVillage(e.VillageId);
            var order = v?.Recruitment.Find(o => o.Id == e.A);
            if (order == null) return; // cancelled: stale event

            order.Done++;
            v.Troops[(int)order.Unit]++;
            if (order.Remaining > 0)
            {
                order.NextAt = Now + order.SecondsEach;
                Schedule(order.SecondsEach, EventKind.UnitTrained, v.Id, order.Id);
            }
            else
            {
                v.Recruitment.Remove(order);
                StartNextRecruit(v, order.Building);
            }
        }
    }
}
