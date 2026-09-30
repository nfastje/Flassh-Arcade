using System;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>Whether a unit can be researched at the smithy right now, and if not, why.</summary>
    public enum ResearchStatus
    {
        Ok,
        /// <summary>Spearmen and noblemen need no research.</summary>
        NotNeeded,
        AlreadyResearched,
        /// <summary>It's being researched, or waiting in the smithy's queue.</summary>
        InProgress,
        NeedsBuilding,
        QueueFull,
        NotEnoughResources,
    }

    /// <summary>Everything the UI needs to show about researching a unit.</summary>
    public struct ResearchCheck
    {
        public ResearchStatus Status;
        public Cost Cost;
        /// <summary>Game seconds the research takes.</summary>
        public double Seconds;
        /// <summary>For <see cref="ResearchStatus.NeedsBuilding"/>: the building and level still required.</summary>
        public Requirement Required;
        /// <summary>For <see cref="ResearchStatus.NotEnoughResources"/>: game seconds until production covers the cost.</summary>
        public double AffordableIn;
    }

    /// <summary>
    /// Research at the smithy, as in Tribal Wars' simple research: every unit but the spearman (and the nobleman) is
    /// researched once, which needs the smithy (and sometimes other buildings) at a certain level; after that the
    /// village can train it. Research stays with the village, even when it changes hands.
    /// </summary>
    public partial class World
    {
        /// <summary>How many researches a smithy can have ordered at once, including the one under way.</summary>
        public const int MaxResearchQueue = 3;

        public ResearchCheck CheckResearch(Village v, UnitType unit)
        {
            Touch(v);
            var def = Units.Get(unit);
            var check = new ResearchCheck
            {
                Cost = def.ResearchCost,
                Seconds = Units.ResearchSeconds(unit, v.Level(BuildingType.Smithy)),
            };
            var unmet = Units.UnmetResearchRequirement(unit, v);
            if (!def.NeedsResearch) check.Status = ResearchStatus.NotNeeded;
            else if (v.IsResearched(unit)) check.Status = ResearchStatus.AlreadyResearched;
            else if (v.IsBeingResearched(unit)) check.Status = ResearchStatus.InProgress;
            else if (unmet.HasValue)
            {
                check.Status = ResearchStatus.NeedsBuilding;
                check.Required = unmet.Value;
            }
            else if (v.Level(BuildingType.Smithy) <= 0)
            {
                check.Status = ResearchStatus.NeedsBuilding;
                check.Required = new Requirement(BuildingType.Smithy, 1);
            }
            else if (v.Researching.Count >= MaxResearchQueue) check.Status = ResearchStatus.QueueFull;
            else if (!v.CanAfford(check.Cost))
            {
                check.Status = ResearchStatus.NotEnoughResources;
                check.AffordableIn = Math.Max(check.Cost.Wood, Math.Max(check.Cost.Clay, check.Cost.Iron)) > v.StorageCapacity
                    ? double.PositiveInfinity
                    : SecondsUntilAffordable(v, check.Cost);
            }
            else check.Status = ResearchStatus.Ok;
            return check;
        }

        /// <summary>Pays for and orders a unit's research. Returns the check, whose status says whether it worked.</summary>
        public ResearchCheck StartResearch(Village v, UnitType unit)
        {
            var check = CheckResearch(v, unit);
            if (check.Status != ResearchStatus.Ok) return check;
            v.Wood -= check.Cost.Wood;
            v.Clay -= check.Cost.Clay;
            v.Iron -= check.Cost.Iron;
            v.Researching.Add(new ResearchOrder { Id = v.NextOrderId++, Unit = unit, Seconds = check.Seconds, Paid = check.Cost });
            StartNextResearch(v);
            return check;
        }

        /// <summary>Cancels an ordered research and refunds it in full.</summary>
        public bool CancelResearch(Village v, int orderId)
        {
            int index = v.Researching.FindIndex(o => o.Id == orderId);
            if (index < 0) return false;
            Touch(v);
            var order = v.Researching[index];
            v.Researching.RemoveAt(index);
            v.Wood += order.Paid.Wood;
            v.Clay += order.Paid.Clay;
            v.Iron += order.Paid.Iron;
            // If it was under way its event is still queued; it'll be ignored as stale.
            StartNextResearch(v);
            return true;
        }

        void StartNextResearch(Village v)
        {
            if (v.Researching.Count == 0) return;
            var order = v.Researching[0];
            if (order.Started) return;
            order.FinishTime = Now + order.Seconds;
            Schedule(order.Seconds, EventKind.ResearchComplete, v.Id, order.Id);
        }

        void CompleteResearch(ScheduledEvent e)
        {
            var v = FindVillage(e.VillageId);
            // Ignore stale events (canceled after it started).
            if (v == null || v.Researching.Count == 0 || v.Researching[0].Id != e.A) return;
            var order = v.Researching[0];
            v.Researching.RemoveAt(0);
            v.Research[(int)order.Unit] = 1;
            StartNextResearch(v);
        }
    }
}
