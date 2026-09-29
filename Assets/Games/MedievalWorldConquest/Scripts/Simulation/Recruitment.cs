using System;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>Whether units can be recruited right now, and if not, why.</summary>
    public enum RecruitStatus
    {
        Ok,
        InvalidCount,
        NeedsBuilding,
        /// <summary>The unit hasn't been researched at the smithy yet.</summary>
        NeedsResearch,
        /// <summary>On a gold-coin world: no free noble slot for another nobleman. Mint more coins.</summary>
        NeedsCoins,
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

        /// <summary>The most of a unit the village can afford right now, by resources and by free population (and, for noblemen on a coin world, by free noble slots).</summary>
        public int MaxAffordable(Village v, UnitType unit)
        {
            var c = UnitCost(unit);
            long max = int.MaxValue;
            if (c.Wood > 0) max = Math.Min(max, (long)(v.Wood / c.Wood));
            if (c.Clay > 0) max = Math.Min(max, (long)(v.Clay / c.Clay));
            if (c.Iron > 0) max = Math.Min(max, (long)(v.Iron / c.Iron));
            if (c.Population > 0) max = Math.Min(max, v.FreePopulation / c.Population);
            if (unit == UnitType.Nobleman) max = Math.Min(max, FreeNobleSlots(FindPlayer(v.OwnerId)));
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
                Total = Multiply(UnitCost(unit), Math.Max(0, count)),
                SecondsEach = Units.SecondsToTrain(unit, buildingLevel),
                MaxAffordable = MaxAffordable(v, unit),
            };
            check.SecondsTotal = check.SecondsEach * Math.Max(0, count);

            if (buildingLevel < def.RequiredLevel)
            {
                check.Status = RecruitStatus.NeedsBuilding;
                check.Required = new Requirement(def.Building, def.RequiredLevel);
            }
            else if (!v.IsResearched(unit)) check.Status = RecruitStatus.NeedsResearch;
            else if (unit == UnitType.Nobleman && count > FreeNobleSlots(FindPlayer(v.OwnerId))) check.Status = RecruitStatus.NeedsCoins;
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
            StartNextRecruit(v, building, Now);
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

            var refund = Multiply(UnitCost(order.Unit), order.Remaining);
            v.Wood += refund.Wood;
            v.Clay += refund.Clay;
            v.Iron += refund.Iron;
            if (order.Started) StartNextRecruit(v, order.Building, Now);
            return true;
        }

        /// <summary>The batch a building is working on (or will work on next), if any.</summary>
        public static RecruitOrder ActiveRecruit(Village v, BuildingType building) =>
            v.Recruitment.Find(o => o.Building == building);

        /// <summary>Sets a building's next batch (if it isn't already under way) training from time <paramref name="from"/>.</summary>
        void StartNextRecruit(Village v, BuildingType building, double from)
        {
            var order = ActiveRecruit(v, building);
            if (order == null || order.Started) return;
            order.NextAt = from + order.SecondsEach;
        }

        /// <summary>
        /// Adds to the garrison every unit whose training has finished by now, moving each building on to its
        /// next batch as one finishes. Units are worked out when they're needed (see <see cref="Touch"/>) rather
        /// than with an event apiece, which keeps big worlds fast; the result is the same.
        /// </summary>
        void CatchUpRecruitment(Village v)
        {
            if (v.Recruitment == null || v.Recruitment.Count == 0) return;
            bool moved = true;
            while (moved)
            {
                moved = false;
                for (int i = 0; i < v.Recruitment.Count; i++)
                {
                    var o = v.Recruitment[i];
                    if (!o.Started || o.NextAt > Now) continue;
                    // (A hair of tolerance, so a unit due exactly now isn't lost to rounding.)
                    int done = Math.Min(o.Remaining, 1 + (int)Math.Floor((Now - o.NextAt) / o.SecondsEach + 1e-9));
                    double lastFinished = o.NextAt + (done - 1) * o.SecondsEach;
                    o.Done += done;
                    v.Troops[(int)o.Unit] += done;
                    if (o.Remaining > 0)
                    {
                        o.NextAt = lastFinished + o.SecondsEach;
                        continue;
                    }
                    // The batch is done: the building starts its next one from the moment this one finished.
                    v.Recruitment.RemoveAt(i);
                    StartNextRecruit(v, o.Building, lastFinished);
                    moved = true;
                    break;
                }
            }
        }

        /// <summary>A unit-trained event from a save made before training was worked out lazily: just catch up.</summary>
        void TrainUnit(ScheduledEvent e)
        {
            var v = FindVillage(e.VillageId);
            if (v != null) Touch(v);
        }
    }
}
