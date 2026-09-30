using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Lords helping their tribe: support for tribe mates under attack, called home once the danger has passed.
    /// </summary>
    public partial class World
    {
        /// <summary>How far (in fields) lords send support to tribe mates under attack, and the most helpers per attack.</summary>
        public const double AiSupportRange = 20;
        public const int AiMaxSupporters = 3;
        static readonly UnitType[] DefensiveUnits = { UnitType.Spearman, UnitType.Swordsman, UnitType.Archer, UnitType.HeavyCavalry };

        /// <summary>
        /// A tribe mate's village is under attack: a lord near enough to get there first sends part of its defenders
        /// (defenders send more, warlords less), and calls them home once the danger has passed.
        /// </summary>
        void AiSupportTribe(Player lord, Village v, AiStyle style)
        {
            var tribe = TribeOf(lord);
            if (tribe == null || tribe.HelpCalls.Count == 0) return;
            double share = lord.Personality == AiPersonality.Defender ? 0.5 : lord.Personality == AiPersonality.Warlord ? 0.15 : lord.Personality == AiPersonality.Noob ? 0.2 : 0.3;
            foreach (var call in tribe.HelpCalls)
            {
                if (call.OwnerId == lord.Id || call.Supporters >= AiMaxSupporters || call.ArriveTime <= Now) continue;
                var host = FindVillage(call.VillageId);
                if (host == null || host.OwnerId != call.OwnerId || Distance(v, host) > AiSupportRange) continue;
                var party = new int[Units.Count];
                int total = 0;
                foreach (var type in DefensiveUnits)
                {
                    party[(int)type] = (int)(v.TroopCount(type) * share);
                    total += party[(int)type];
                }
                if (total < 20) return; // too little to matter (and to spare)
                var slowest = SlowestUnit(party);
                if (slowest == null || Now + TravelSeconds(v, host, slowest.Value) > call.ArriveTime) continue; // too late to help
                if (Send(v, host, party, CommandKind.Support) == null) continue;
                lord.SupportPlacements.Add(new SupportPlacement { HostId = host.Id, FromId = v.Id, Until = call.ArriveTime + 3600 });
                return; // one at a time
            }
        }

        /// <summary>Calls home support sent to tribe mates once the attacks it was sent against are over.</summary>
        void AiRecallSupport(Player lord)
        {
            for (int i = lord.SupportPlacements.Count - 1; i >= 0; i--)
            {
                var p = lord.SupportPlacements[i];
                if (p.Until > Now) continue;
                lord.SupportPlacements.RemoveAt(i);
                var host = FindVillage(p.HostId);
                if (host != null) Recall(host, p.FromId);
            }
        }

        /// <summary>A village's own troops of a kind out on attacks or on their way home.</summary>
        int Away(Village v, UnitType type)
        {
            int n = 0;
            foreach (var c in CommandsOf(v.OwnerId))
                if ((c.Kind == CommandKind.Attack && c.FromVillageId == v.Id) || (c.Kind == CommandKind.Return && c.ToVillageId == v.Id))
                    n += c.Troops[(int)type];
            return n;
        }

        /// <summary>
        /// The defenders a lord expects at a village, from what it last saw there: a player's may have trained more
        /// since (the older the sighting, the more), a barbarian village's can't. Either way it allows its safety
        /// margin.
        /// </summary>
        int[] ExpectedDefenders(AiNote note, Village t)
        {
            double days = Math.Max(0, Now - note.SeenAt) / SecondsPerDay;
            bool trains = !t.IsBarbarian && FindPlayer(t.OwnerId)?.Personality != AiPersonality.Inactive;
            double factor = (trains ? 1 + 0.3 * days : 1) * SkillAttackMargin;
            var expected = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < note.SeenTroops.Length; i++) expected[i] = (int)Math.Ceiling(note.SeenTroops[i] * factor);
            return expected;
        }

        /// <summary>
        /// How much a lord expects to carry off from a village: what was there to take when it last knew (from its
        /// scouts, or an earlier raid), plus what the village's mines have made since, up to what its warehouse
        /// holds, less what its hiding place keeps. The mines and warehouse are the ones its scouts saw, or a guess
        /// from the village's size. A village it knows nothing about is guessed at half a day's production.
        /// </summary>
        double ExpectedHaul(AiNote note, Village t)
        {
            var levels = note?.SeenLevels;
            int guess = Math.Min(25, 1 + t.Points / 60);
            int Level(BuildingType type, int fallback) => levels != null && (int)type < levels.Length ? levels[(int)type] : fallback;
            double cap = Buildings.StorageCapacity(Level(BuildingType.Warehouse, Math.Min(20, guess))) - Buildings.HiddenCapacity(Level(BuildingType.HidingPlace, 0));
            if (cap <= 0) return 0;
            bool known = note != null && note.LootSeenAt >= 0;
            double hours = known ? Math.Max(0, Now - note.LootSeenAt) / 3600 : 12;
            double haul = 0;
            foreach (ResourceType r in ResourceTypes)
            {
                double made = Buildings.ProductionPerHour(Level(Village.MineFor(r), guess)) * hours;
                double had = !known ? 0 : r == ResourceType.Wood ? note.LootWood : r == ResourceType.Clay ? note.LootClay : note.LootIron;
                haul += Math.Min(cap, had + made);
            }
            return haul;
        }

        /// <summary>
        /// Enough raiders (in the personality's order) to carry off <paramref name="haul"/> and beat the expected
        /// defenders and wall even with the worst luck, or null if the troops available can't do it.
        /// </summary>
        static int[] RaidParty(AiStyle style, int[] available, double haul, int[] defenders, int wall)
        {
            var party = new int[Units.Count];
            int wanted = (int)Math.Min(haul, 20000), carry = 0;
            foreach (var type in style.RaidWith)
            {
                int each = Units.Get(type).Carry;
                int take = Math.Min(available[(int)type], (int)Math.Ceiling(Math.Max(0, wanted - carry) / (double)each));
                party[(int)type] = take;
                carry += take * each;
            }
            if (carry < Math.Min(AiMinHaul, wanted)) return null;
            if (Battle.Fight(party, defenders, wall, -Battle.MaxLuck).AttackerWon) return party;
            // Too few to win: all the raiders on hand, if that does it (they'll just come back with room to spare).
            foreach (var type in style.RaidWith) party[(int)type] = available[(int)type];
            return Battle.Fight(party, defenders, wall, -Battle.MaxLuck).AttackerWon ? party : null;
        }
    }
}
