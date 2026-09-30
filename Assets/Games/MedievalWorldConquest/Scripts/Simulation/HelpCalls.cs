using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// Calls for help: attacks on tribe members, judged by their speed, and the support that answers them.
    /// </summary>
    public partial class World
    {
        /// <summary>
        /// When an attack is sent at a tribe member's village, the tribe can only judge it as a defender would: by
        /// its speed. Swordsman speed or slower (a real attack, or a fake meant to look like one) and the tribe is
        /// asked to help; scouts and cavalry raids are left to the village. The human, if a tribe mate, only hears
        /// of nobleman-speed attacks (the village may fall), and only if one of their villages could get there in
        /// time; everything else is in the tribe's list of villages under attack.
        /// </summary>
        void OnAttackSent(Command c, Village from, Village to)
        {
            if (!Diplomacy || to.IsBarbarian) return;
            var victim = FindPlayer(to.OwnerId);
            var tribe = TribeOf(victim);
            if (tribe != null && c.OwnerId != to.OwnerId)
            {
                var attackerTribe = TribeOf(c.OwnerId);
                if (attackerTribe != null) CountIncident(attackerTribe, tribe);
                var speed = AttackSpeeds.Of(c.Troops);
                int power = 0;
                for (int i = 0; i < Units.Count && i < c.Troops.Length; i++) power += c.Troops[i] * Units.Get((UnitType)i).Attack;
                bool worthHelp = HelpBySpeed ? AttackSpeeds.WorthHelp(speed) : power >= 500 || victim.IsHuman;
                if (worthHelp && tribe.HelpCalls.Count < 30)
                {
                    tribe.HelpCalls.Add(new HelpCall { VillageId = to.Id, OwnerId = victim.Id, AttackerId = c.OwnerId, ArriveTime = c.ArriveTime });
                    var human = HumanPlayer;
                    if (human != null && human.TribeId == tribe.Id && !victim.IsHuman && speed == AttackSpeed.Nobleman
                        && HumanCanReach(to, c.ArriveTime) && SupportRequestsToday() < 4
                        && !Messages.Exists(m => m.Kind == MessageKind.SupportRequest && m.A == to.Id && Now - m.Time < 12 * 3600))
                        Write(MessageKind.SupportRequest, victim, $"Noblemen coming for {to.Name} ({to.X}|{to.Y})",
                            $"{NameWithTag(FindPlayer(c.OwnerId))} is sending an attack at nobleman speed: they mean to take my village. It lands {FormatClock(c.ArriveTime)}. " +
                            "You're near enough to get there first. Can you send support?", to.Id, tribeId: tribe.Id);
                }
            }
            // The human breaking faith: attacking a tribe mate or a friendly tribe.
            if (FindPlayer(c.OwnerId)?.IsHuman == true && victim != null && AreFriendly(c.OwnerId, victim.Id)) HumanBetrays(victim);
        }

        /// <summary>Whether tribes judge attacks by speed alone, as a defender must (false: by their real strength, as before). A tuning value.</summary>
        public static bool HelpBySpeed = true;

        static readonly UnitType[] DefendersFastestFirst = { UnitType.HeavyCavalry, UnitType.Spearman, UnitType.Archer, UnitType.Swordsman };

        /// <summary>Whether one of the human's villages has defenders who could reach a village before this time.</summary>
        public bool HumanCanReach(Village to, double arriveTime)
        {
            var human = HumanPlayer;
            if (human == null) return false;
            foreach (var v in VillagesOf(human.Id))
            {
                if (v == to) continue;
                foreach (var type in DefendersFastestFirst)
                {
                    if (v.TroopCount(type) <= 0) continue;
                    if (Now + TravelSeconds(v, to, type) <= arriveTime) return true;
                    break; // its fastest defenders are too slow: the rest are slower still
                }
            }
            return false;
        }

        int SupportRequestsToday() => Messages.FindAll(m => m.Kind == MessageKind.SupportRequest && Now - m.Time < SecondsPerDay).Count;

        void CountIncident(Tribe a, Tribe b)
        {
            if (a == b) return;
            if (incidents == null) incidents = new Dictionary<(int, int), int>();
            var key = (Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id));
            incidents[key] = (incidents.TryGetValue(key, out int n) ? n : 0) + 1;
        }

        int Incidents(Tribe a, Tribe b) =>
            incidents != null && incidents.TryGetValue((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)), out int n) ? n : 0;

        /// <summary>The human attacked a friend: a tribe mate (expelled, or the victim leaves the human's tribe) or a friendly tribe (the pact is broken).</summary>
        void HumanBetrays(Player victim)
        {
            var human = HumanPlayer;
            var mine = TribeOf(human);
            var theirs = TribeOf(victim);
            if (mine == theirs)
            {
                human.Reputation -= 25;
                if (mine.LeaderId == human.Id)
                {
                    LeaveTribe(victim);
                    Write(MessageKind.Note, victim, "Traitor!", "You attack your own tribe mate? I'm leaving, and everyone will hear of it.", tribeId: mine.Id);
                }
                else
                {
                    LeaveTribe(human);
                    Write(MessageKind.Note, FindPlayer(mine.LeaderId), $"Expelled from {mine.Name}", "We don't attack our own. You're out.", tribeId: mine.Id);
                }
                return;
            }
            human.Reputation -= 15;
            SetRelation(mine, theirs, RelationKind.Enemy);
            Write(MessageKind.Note, FindPlayer(theirs.LeaderId), "Betrayal", $"You broke your word. {theirs.Name} is at war with {mine.Name}.", tribeId: theirs.Id);
        }

        /// <summary>
        /// The human sends support: if it goes to a tribe mate whose village is under attack, the tribe notices.
        /// </summary>
        void OnSupportSent(Command c, Village to)
        {
            if (!Diplomacy) return;
            var sender = FindPlayer(c.OwnerId);
            var tribe = TribeOf(sender);
            if (tribe == null || to.OwnerId == c.OwnerId) return;
            foreach (var call in tribe.HelpCalls)
                if (call.VillageId == to.Id && call.ArriveTime > Now)
                {
                    call.Supporters++;
                    if (sender.IsHuman) sender.Satisfaction = Math.Min(100, sender.Satisfaction + 8);
                    var helped = FindPlayer(call.OwnerId);
                    if (helped != null) helped.Satisfaction = Math.Min(100, helped.Satisfaction + 6);
                    tribe.Tension = Math.Max(0, tribe.Tension - 3);
                    return;
                }
        }

        /// <summary>
        /// Attacks that have landed: nobody went to help, and the tribe feels it (the victim most); the human, asked
        /// and absent, is noticed too.
        /// </summary>
        void ExpireHelpCalls()
        {
            var human = HumanPlayer;
            foreach (var tribe in ActiveTribes())
                for (int i = tribe.HelpCalls.Count - 1; i >= 0; i--)
                {
                    var call = tribe.HelpCalls[i];
                    if (call.ArriveTime > Now) continue;
                    tribe.HelpCalls.RemoveAt(i);
                    if (call.Supporters > 0) continue;
                    var victim = FindPlayer(call.OwnerId);
                    if (victim != null) victim.Satisfaction = Math.Max(0, victim.Satisfaction - 10);
                    tribe.AbandonedSinceTick++;
                    if (human != null && human.TribeId == tribe.Id && victim != null && !victim.IsHuman
                        && Messages.Exists(m => m.Kind == MessageKind.SupportRequest && m.A == call.VillageId && Now - m.Time < SecondsPerDay))
                        human.Satisfaction = Math.Max(0, human.Satisfaction - 8);
                }
        }
    }
}
