using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    public enum CommandKind
    {
        Attack = 0,
        Support = 1,
        Return = 2,
        /// <summary>Merchants carrying resources to another village (see <see cref="World.SendResources"/>).</summary>
        Transport = 3,
        /// <summary>Merchants going home, empty, after a delivery.</summary>
        TransportReturn = 4,
    }

    /// <summary>
    /// Troops on the march (an attack, support going to a village, or troops and loot heading home), or merchants
    /// on the road.
    /// </summary>
    [Serializable]
    public class Command
    {
        public int Id;
        public CommandKind Kind;
        /// <summary>The player whose troops these are.</summary>
        public int OwnerId;
        public int FromVillageId, ToVillageId;
        public int[] Troops;
        public double DepartTime, ArriveTime;
        /// <summary>Loot being carried home (returns only).</summary>
        public Cost Loot;
        /// <summary>The building an attack's catapults aim at.</summary>
        public BuildingType CatapultTarget;
        /// <summary>For merchants: how many are on the road (the goods they carry are in <see cref="Loot"/>).</summary>
        public int Merchants;
        /// <summary>Who held the target when the command set out (-2: not known, from an older save).</summary>
        public int TargetOwnerId = -2;

        public bool IsTrade => Kind == CommandKind.Transport || Kind == CommandKind.TransportReturn;
    }

    /// <summary>Troops from another village stationed in this one to help defend it.</summary>
    [Serializable]
    public class SupportGroup
    {
        public int FromVillageId;
        public int OwnerId;
        public int[] Troops;
    }

    public enum SendStatus
    {
        Ok,
        NoTroops,
        NotEnoughTroops,
        SameVillage,
        InvalidTarget,
        /// <summary>The target's owner is still under beginner protection.</summary>
        TargetProtected,
    }

    /// <summary>Whether troops can be sent, and how the march would go.</summary>
    public struct SendCheck
    {
        public SendStatus Status;
        public UnitType Slowest;
        /// <summary>Game seconds to get there.</summary>
        public double Seconds;
        public int Attack, Carry;
    }

    /// <summary>Sending troops, marching, fighting, looting, support, and coming home.</summary>
    public partial class World
    {
        public List<Command> Commands = new List<Command>();
        public int NextCommandId = 1;

        // Each player's own commands, built when first needed (not saved) and kept in step as commands come and go.
        [NonSerialized] Dictionary<int, List<Command>> commandsByOwner;

        /// <summary>A player's own troops on the march (attacks, support and returns). Don't modify the list.</summary>
        public List<Command> CommandsOf(int ownerId)
        {
            if (commandsByOwner == null)
            {
                commandsByOwner = new Dictionary<int, List<Command>>();
                foreach (var c in Commands) OwnCommands(c.OwnerId).Add(c);
            }
            return OwnCommands(ownerId);
        }

        List<Command> OwnCommands(int ownerId)
        {
            if (!commandsByOwner.TryGetValue(ownerId, out var list)) commandsByOwner[ownerId] = list = new List<Command>();
            return list;
        }

        // Where each command is in the list, by id (not saved; rebuilt when first needed, or if the list changed
        // behind its back), so an arriving army is found at once however many are on the move.
        [NonSerialized] Dictionary<int, int> commandIndex;

        Dictionary<int, int> CommandIndex()
        {
            if (commandIndex == null || commandIndex.Count != Commands.Count)
            {
                commandIndex = new Dictionary<int, int>(Commands.Count);
                for (int i = 0; i < Commands.Count; i++) commandIndex[Commands[i].Id] = i;
            }
            return commandIndex;
        }

        /// <summary>Population taken by a set of troops.</summary>
        public static int PopulationOf(int[] troops)
        {
            int pop = 0;
            for (int i = 0; i < Units.Count && i < troops.Length; i++) pop += troops[i] * Units.Get((UnitType)i).Cost.Population;
            return pop;
        }

        static int Total(int[] troops)
        {
            int n = 0;
            foreach (int t in troops) n += t;
            return n;
        }

        /// <summary>The slowest unit type present, which sets the whole army's pace; null if there are no troops.</summary>
        public static UnitType? SlowestUnit(int[] troops)
        {
            UnitType? slowest = null;
            for (int i = 0; i < Units.Count && i < troops.Length; i++)
                if (troops[i] > 0 && (slowest == null || Units.Get((UnitType)i).MinutesPerField > Units.Get(slowest.Value).MinutesPerField))
                    slowest = (UnitType)i;
            return slowest;
        }

        public SendCheck CheckSend(Village from, Village to, int[] troops, CommandKind kind)
        {
            var check = new SendCheck();
            if (to == null || from == null) { check.Status = SendStatus.InvalidTarget; return check; }
            if (to == from) { check.Status = SendStatus.SameVillage; return check; }

            var slowest = SlowestUnit(troops);
            if (slowest == null) { check.Status = SendStatus.NoTroops; return check; }
            check.Slowest = slowest.Value;
            check.Seconds = TravelSeconds(from, to, slowest.Value);
            for (int i = 0; i < Units.Count && i < troops.Length; i++)
            {
                check.Attack += troops[i] * Units.Get((UnitType)i).Attack;
                if (troops[i] < 0 || troops[i] > from.TroopCount((UnitType)i)) { check.Status = SendStatus.NotEnoughTroops; return check; }
            }
            check.Carry = Battle.CarryCapacity(troops);
            // Support can go to any village, barbarians' included; attacks can't touch another player under protection.
            check.Status = kind == CommandKind.Attack && !to.IsBarbarian && to.OwnerId != from.OwnerId && IsProtected(to.OwnerId)
                ? SendStatus.TargetProtected
                : SendStatus.Ok;
            return check;
        }

        /// <summary>Sends troops from a village's garrison. Returns the command, or null if the check failed.</summary>
        /// <param name="catapultTarget">The building an attack's catapults aim at.</param>
        public Command Send(Village from, Village to, int[] troops, CommandKind kind, BuildingType catapultTarget = BuildingType.Headquarters)
        {
            if (kind == CommandKind.Return) throw new ArgumentException("Troops are sent home by recalling or after an attack.");
            var check = CheckSend(from, to, troops, kind);
            if (check.Status != SendStatus.Ok) return null;

            var sent = new int[Units.Count];
            for (int i = 0; i < Units.Count && i < troops.Length; i++)
            {
                sent[i] = troops[i];
                from.Troops[i] -= troops[i];
            }
            from.AwayPopulation += PopulationOf(sent); // troops away still count against their home farm
            var command = March(kind, from.OwnerId, from, to, sent, default, check.Seconds);
            command.CatapultTarget = catapultTarget;
            // Tribes hear of it: a call for help, a grudge, or help arriving.
            if (kind == CommandKind.Attack) OnAttackSent(command, from, to);
            else OnSupportSent(command, to);
            return command;
        }

        Command March(CommandKind kind, int ownerId, Village from, Village to, int[] troops, Cost loot, double seconds)
        {
            var command = new Command
            {
                Id = NextCommandId++,
                Kind = kind,
                OwnerId = ownerId,
                FromVillageId = from.Id,
                ToVillageId = to.Id,
                Troops = troops,
                DepartTime = Now,
                ArriveTime = Now + seconds,
                Loot = loot,
                TargetOwnerId = to.OwnerId,
            };
            CommandIndex()[command.Id] = Commands.Count;
            Commands.Add(command);
            if (commandsByOwner != null) OwnCommands(ownerId).Add(command);
            Schedule(seconds, EventKind.CommandArrives, to.Id, command.Id);
            return command;
        }

        /// <summary>Sends support troops stationed in <paramref name="host"/> back to the village they came from.</summary>
        public Command Recall(Village host, int fromVillageId)
        {
            var group = host.Supports.Find(g => g.FromVillageId == fromVillageId);
            var home = FindVillage(fromVillageId);
            if (group == null || home == null) return null;
            host.Supports.Remove(group);
            var slowest = SlowestUnit(group.Troops);
            double seconds = slowest.HasValue ? TravelSeconds(host, home, slowest.Value) : 0;
            return March(CommandKind.Return, group.OwnerId, host, home, group.Troops, default, seconds);
        }

        /// <summary>Attacks heading for a player's villages from anyone else, soonest first.</summary>
        public List<Command> IncomingAttacks(int playerId)
        {
            var incoming = Commands.FindAll(c =>
                c.Kind == CommandKind.Attack && c.OwnerId != playerId && FindVillage(c.ToVillageId)?.OwnerId == playerId);
            incoming.Sort((a, b) => a.ArriveTime.CompareTo(b.ArriveTime));
            return incoming;
        }

        /// <summary>Everything on the march to or from a player's villages (their own commands and incoming attacks).</summary>
        public List<Command> MovementsFor(int playerId)
        {
            var list = Commands.FindAll(c => c.OwnerId == playerId || FindVillage(c.ToVillageId)?.OwnerId == playerId);
            list.Sort((a, b) => a.ArriveTime.CompareTo(b.ArriveTime));
            return list;
        }

        void CommandArrives(ScheduledEvent e)
        {
            if (!CommandIndex().TryGetValue(e.A, out int index)) return;
            var command = Commands[index];
            // Out of the list in one step: the last command takes its place (the list's order doesn't matter).
            var last = Commands[Commands.Count - 1];
            Commands[index] = last;
            commandIndex[last.Id] = index;
            Commands.RemoveAt(Commands.Count - 1);
            commandIndex.Remove(command.Id);
            if (commandsByOwner != null) OwnCommands(command.OwnerId).Remove(command);

            var from = FindVillage(command.FromVillageId);
            var to = FindVillage(command.ToVillageId);
            switch (command.Kind)
            {
                case CommandKind.Attack:
                    if (to != null && from != null && !TurnBackIfFriendly(command, from, to)) ResolveAttack(command, from, to);
                    break;
                case CommandKind.Support:
                    if (to != null) Station(command, to);
                    break;
                case CommandKind.Return:
                    if (to != null) ComeHome(command, to);
                    break;
                case CommandKind.Transport:
                    if (to != null) Deliver(command, from, to);
                    break;
                // Merchants back home are simply free again.
            }
        }

        void Station(Command command, Village host)
        {
            var group = host.Supports.Find(g => g.FromVillageId == command.FromVillageId);
            if (group == null) host.Supports.Add(group = new SupportGroup { FromVillageId = command.FromVillageId, OwnerId = command.OwnerId, Troops = new int[Units.Count] });
            for (int i = 0; i < Units.Count; i++) group.Troops[i] += command.Troops[i];

            if (IsHuman(command.OwnerId))
                AddReport(new BattleReport
                {
                    Kind = ReportKind.SupportArrived,
                    AttackerVillageId = command.FromVillageId, DefenderVillageId = host.Id,
                    AttackerVillage = FindVillage(command.FromVillageId)?.Name ?? "?", DefenderVillage = host.Name,
                    DefenderX = host.X, DefenderY = host.Y,
                    AttackerSent = (int[])command.Troops.Clone(),
                });
        }

        /// <summary>Troops (and any loot) arriving back at their home village.</summary>
        void ComeHome(Command command, Village home)
        {
            // Home lost while they were away: the troops scatter (and were no longer counted against it).
            if (home.OwnerId != command.OwnerId) return;
            Touch(home);
            home.AwayPopulation = Math.Max(0, home.AwayPopulation - PopulationOf(command.Troops));

            for (int i = 0; i < Units.Count; i++) home.Troops[i] += command.Troops[i];
            double cap = home.StorageCapacity;
            // Loot goes into storage; anything that doesn't fit is lost.
            home.Wood = Math.Max(home.Wood, Math.Min(cap, home.Wood + command.Loot.Wood));
            home.Clay = Math.Max(home.Clay, Math.Min(cap, home.Clay + command.Loot.Clay));
            home.Iron = Math.Max(home.Iron, Math.Min(cap, home.Iron + command.Loot.Iron));
        }

        bool IsHuman(int playerId) => FindPlayer(playerId)?.IsHuman == true;

        /// <summary>The luck of an attack: a repeatable value in ±<see cref="Battle.MaxLuck"/> from the world seed and the command.</summary>
        double LuckFor(Command command) => (Terrain.Hash(Settings.Seed ^ 0x2545F491, command.Id, 7) * 2 - 1) * Battle.MaxLuck;

        void ResolveAttack(Command command, Village attacker, Village target)
        {
            Touch(target); // its stores (and loyalty) as they are now, before looting
            int defenderOwner = target.OwnerId; // it may change hands below
            // Everyone defending: the village's own troops plus any support stationed there. (Nobody, in a village
            // its owner has handed over to this attacker: they stand aside.)
            bool handedOver = FedTo(target, command.OwnerId, Now);
            var defenders = handedOver ? new int[Units.Count] : (int[])target.Troops.Clone();
            if (!handedOver)
                foreach (var g in target.Supports)
                    for (int i = 0; i < Units.Count; i++) defenders[i] += g.Troops[i];

            // Human players with support here hear about the fight too, even if the village isn't theirs.
            var supportOwners = new List<int>();
            foreach (var g in target.Supports)
                if (!supportOwners.Contains(g.OwnerId)) supportOwners.Add(g.OwnerId);

            var result = Battle.Fight(command.Troops, defenders, target.Level(BuildingType.Wall), LuckFor(command));

            // Attacker losses: the dead no longer count against the attacker's farm.
            var attackerLost = Battle.Losses(command.Troops, result.AttackerLossFraction, result.AttackerScoutLossFraction);
            var survivors = new int[Units.Count];
            for (int i = 0; i < Units.Count; i++) survivors[i] = command.Troops[i] - attackerLost[i];
            attacker.AwayPopulation = Math.Max(0, attacker.AwayPopulation - PopulationOf(attackerLost));

            // Defender losses, from the village's own troops and each support group alike.
            var defenderLost = handedOver ? new int[Units.Count] : Battle.Losses(target.Troops, result.DefenderLossFraction, result.DefenderScoutLossFraction);
            for (int i = 0; i < Units.Count; i++) target.Troops[i] -= defenderLost[i];
            AddStat(defenderOwner, StatKind.TroopsLost, Total(defenderLost));
            foreach (var g in handedOver ? new List<SupportGroup>() : target.Supports)
            {
                var lost = Battle.Losses(g.Troops, result.DefenderLossFraction, result.DefenderScoutLossFraction);
                for (int i = 0; i < Units.Count; i++)
                {
                    g.Troops[i] -= lost[i];
                    defenderLost[i] += lost[i];
                }
                AddStat(g.OwnerId, StatKind.TroopsLost, Total(lost));
                var home = FindVillage(g.FromVillageId);
                if (home != null) home.AwayPopulation = Math.Max(0, home.AwayPopulation - PopulationOf(lost));
            }
            target.Supports.RemoveAll(g => Total(g.Troops) == 0);

            // Rams' damage to the wall stands whatever the outcome.
            if (result.WallAfter < target.Level(BuildingType.Wall)) ForgetPlanProgress(target);
            target.Levels[(int)BuildingType.Wall] = result.WallAfter;

            var report = new BattleReport
            {
                AttackerVillageId = attacker.Id, DefenderVillageId = target.Id,
                AttackerVillage = attacker.Name, DefenderVillage = target.Name,
                AttackerX = attacker.X, AttackerY = attacker.Y, DefenderX = target.X, DefenderY = target.Y,
                AttackerWon = result.AttackerWon, Luck = result.Luck,
                AttackerSent = (int[])command.Troops.Clone(), AttackerLost = attackerLost,
                DefenderTroops = defenders, DefenderLost = defenderLost,
                WallBefore = result.WallBefore, WallAfter = result.WallAfter,
                Scouted = result.Scouted,
                AttackerPlayer = OwnerName(attacker), DefenderPlayer = OwnerName(target),
                AttackerPlayerId = attacker.OwnerId, DefenderPlayerId = target.OwnerId,
            };

            var loot = default(Cost);
            if (result.AttackerWon)
            {
                // The survivors fill their packs as far as the village's stock allows, apart from what's in its
                // hiding place. What won't fit in their own warehouse when they get home is lost then, not left
                // behind now.
                report.LootCapacity = Battle.CarryCapacity(survivors);
                int hidden = target.HiddenCapacity;
                loot = Battle.Loot(report.LootCapacity, target.Wood - hidden, target.Clay - hidden, target.Iron - hidden);
                target.Wood -= loot.Wood;
                target.Clay -= loot.Clay;
                target.Iron -= loot.Iron;
                report.Loot = loot;
            }

            // Scouts count what's there once the looting is done, before the catapults knock anything down.
            if (result.Scouted)
            {
                report.ScoutedResources = new Cost((int)target.Wood, (int)target.Clay, (int)target.Iron);
                report.ScoutedLevels = (int[])target.Levels.Clone();
            }

            // Catapults fire last, after the looting, so hitting the warehouse can't shrink the haul.
            int catapults = survivors[(int)UnitType.Catapult];
            if (result.AttackerWon && catapults > 0)
            {
                var hit = command.CatapultTarget;
                report.CatapultBuilding = (int)hit;
                report.CatapultBefore = target.Level(hit);
                report.CatapultAfter = Math.Max(Buildings.LowestLevel(hit), Battle.LevelAfterCatapults(report.CatapultBefore, catapults));
                report.CatapultAfter = Math.Min(report.CatapultAfter, report.CatapultBefore);
                target.Levels[(int)hit] = report.CatapultAfter;
                ForgetPlanProgress(target); // its owner may need to rebuild it
                // A smaller warehouse can't hold what the bigger one did.
                double cap = target.StorageCapacity;
                target.Wood = Math.Min(target.Wood, cap);
                target.Clay = Math.Min(target.Clay, cap);
                target.Iron = Math.Min(target.Iron, cap);
            }

            // Noblemen who survived a victory sway the village, and may win it. If they do, the troops stay there and
            // so does the loot: it's all theirs now.
            bool conquered = result.AttackerWon && Sway(command, attacker, target, survivors, report);
            if (conquered)
            {
                target.Wood += loot.Wood;
                target.Clay += loot.Clay;
                target.Iron += loot.Iron;
                loot = default;
                report.Loot = default;
            }

            AddStat(command.OwnerId, StatKind.DefeatedAttacking, Total(defenderLost));
            AddStat(command.OwnerId, StatKind.TroopsLost, Total(attackerLost));
            AddStat(defenderOwner, StatKind.DefeatedDefending, Total(attackerLost));
            AddStat(command.OwnerId, StatKind.Loot, loot.Wood + loot.Clay + loot.Iron);

            AiLearnFromBattle(command, target, result, defenders, defenderLost, Total(survivors) > 0, loot, report.LootCapacity, report);

            // Reports for whoever's human: the attacker sees the defenders only if someone came back or scouts got through.
            if (IsHuman(command.OwnerId))
            {
                report.Kind = ReportKind.Attack;
                report.DefenderVisible = Total(survivors) > 0 || result.Scouted;
                AddReport(report);
            }
            bool defenderHearsOfIt = IsHuman(defenderOwner);
            foreach (int owner in supportOwners)
                if (owner != command.OwnerId && IsHuman(owner)) defenderHearsOfIt = true;
            if (defenderHearsOfIt)
            {
                var defense = report.Clone();
                defense.Kind = ReportKind.Defense;
                defense.DefenderVisible = true;
                AddReport(defense);
            }

            if (Total(survivors) > 0 && !conquered)
            {
                var slowest = SlowestUnit(survivors).Value;
                March(CommandKind.Return, command.OwnerId, target, attacker, survivors, loot, TravelSeconds(target, attacker, slowest));
            }
        }
    }
}
