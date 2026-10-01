using System;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>One step of the quest line: what to do, why, how far along the player is, and the reward.</summary>
    public class Quest
    {
        public string Title, Goal, Why;
        public Cost Reward;
        /// <summary>Whether the quest belongs in this world (some only on worlds with tribes, or with gold coins).</summary>
        internal Func<World, bool> Applies = w => true;
        internal Func<World, bool> Done;
        /// <summary>How far along the player is, in words (null: nothing to count).</summary>
        internal Func<World, string> Progress;

        public string ProgressIn(World world) => Progress?.Invoke(world);
    }

    /// <summary>
    /// The quest line, as Tribal Wars taught new players: one quest at a time, from the first mines to the first
    /// village won over, each with a small reward in resources to nudge the player along. Always on; a quest done
    /// stays done (even if, say, the report that did it is deleted) until its reward is claimed, and quests that
    /// don't belong in a world (tribes on a free-for-all world, coins on a flat-price one) are skipped.
    /// </summary>
    public partial class World
    {
        /// <summary>Which quest the player is on (past the last: all done), and whether it's done, its reward waiting.</summary>
        public int QuestIndex;
        public bool QuestReady;

        static int Highest(World w, BuildingType type)
        {
            int best = 0;
            var human = w.HumanPlayer;
            if (human != null)
                foreach (var v in w.VillagesOf(human.Id)) best = Math.Max(best, v.Level(type));
            return best;
        }

        static string Level(World w, BuildingType type, int wanted) => $"{Buildings.Get(type).Name} {Math.Min(Highest(w, type), wanted)}/{wanted}";

        /// <summary>The player's troops of a kind: at home, and on the march.</summary>
        static int HumanTroops(World w, UnitType type)
        {
            var human = w.HumanPlayer;
            if (human == null) return 0;
            int n = 0;
            foreach (var v in w.VillagesOf(human.Id)) n += v.TroopCount(type);
            foreach (var c in w.CommandsOf(human.Id))
                if (c.Troops != null && (int)type < c.Troops.Length) n += c.Troops[(int)type];
            return n;
        }

        static bool HasScouted(World w)
        {
            var human = w.HumanPlayer;
            if (human == null) return false;
            foreach (var c in w.CommandsOf(human.Id))
                if (c.Kind == CommandKind.Attack && c.Troops != null && c.Troops[(int)UnitType.Scout] > 0) return true;
            foreach (var r in w.Reports)
                if (r.Kind == ReportKind.Attack && r.AttackerSent != null && r.AttackerSent[(int)UnitType.Scout] > 0) return true;
            return false;
        }

        static long HumanStat(World w, StatKind kind) => w.StatOf(w.HumanPlayer, kind, StatPeriod.AllTime);

        static Cost Each(int amount) => new Cost(amount, amount, amount);

        public static readonly Quest[] QuestLine =
        {
            new Quest
            {
                Title = "Wood for the village", Goal = "Upgrade the timber camp to level 2.",
                Why = "Everything you build costs wood, clay and iron. Better camps, pits and mines bring in more of each.",
                Reward = Each(150), Done = w => Highest(w, BuildingType.TimberCamp) >= 2, Progress = w => Level(w, BuildingType.TimberCamp, 2),
            },
            new Quest
            {
                Title = "Clay and iron", Goal = "Upgrade the clay pit and the iron mine to level 2.",
                Why = "Keep all three resources growing: most buildings need a good deal of each.",
                Reward = Each(200), Done = w => Highest(w, BuildingType.ClayPit) >= 2 && Highest(w, BuildingType.IronMine) >= 2,
                Progress = w => $"{Level(w, BuildingType.ClayPit, 2)} · {Level(w, BuildingType.IronMine, 2)}",
            },
            new Quest
            {
                Title = "Room for more", Goal = "Upgrade the farm and the warehouse to level 2.",
                Why = "The farm feeds everyone who works your buildings and fills your army; the warehouse holds your stores.",
                Reward = Each(250), Done = w => Highest(w, BuildingType.Farm) >= 2 && Highest(w, BuildingType.Warehouse) >= 2,
                Progress = w => $"{Level(w, BuildingType.Farm, 2)} · {Level(w, BuildingType.Warehouse, 2)}",
            },
            new Quest
            {
                Title = "A stronger Headquarters", Goal = "Upgrade the Headquarters to level 3.",
                Why = "Every level of the Headquarters makes building faster, and level 3 lets you build barracks.",
                Reward = Each(300), Done = w => Highest(w, BuildingType.Headquarters) >= 3, Progress = w => Level(w, BuildingType.Headquarters, 3),
            },
            new Quest
            {
                Title = "Barracks", Goal = "Build the barracks.",
                Why = "Troops keep raiders away and bring in resources of their own from barbarian villages.",
                Reward = Each(300), Done = w => Highest(w, BuildingType.Barracks) >= 1, Progress = w => Level(w, BuildingType.Barracks, 1),
            },
            new Quest
            {
                Title = "First recruits", Goal = "Train 10 spearmen.",
                Why = "Spearmen are cheap and hold well against cavalry: the first line of any defense.",
                Reward = Each(300), Done = w => HumanTroops(w, UnitType.Spearman) >= 10,
                Progress = w => $"Spearmen {Math.Min(HumanTroops(w, UnitType.Spearman), 10)}/10",
            },
            new Quest
            {
                Title = "Plunder", Goal = "Raid a barbarian village and bring its resources home.",
                Why = "Raiding barbarian villages (the gray ones on the map) is the fastest way to grow early on. Spearmen carry a little; axemen and cavalry carry more. The reward includes the Loot Assistant, which can keep raiding for you.",
                Reward = Each(400), Done = w => HumanStat(w, StatKind.Loot) > 0,
            },
            new Quest
            {
                Title = "A wall", Goal = "Build the wall to level 3.",
                Why = "Your beginner protection won't last. A wall makes every defender fight harder.",
                Reward = Each(500), Done = w => Highest(w, BuildingType.Wall) >= 3, Progress = w => Level(w, BuildingType.Wall, 3),
            },
            new Quest
            {
                Title = "The market", Goal = "Build the market.",
                Why = "Merchants carry resources between villages and trade with other lords when you have too much of one.",
                Reward = Each(500), Done = w => Highest(w, BuildingType.Market) >= 1, Progress = w => Level(w, BuildingType.Market, 1),
            },
            new Quest
            {
                Title = "The smithy", Goal = "Build the smithy (it needs Headquarters 5).",
                Why = "Every kind of troop but spearmen has to be researched at the smithy before you can train it.",
                Reward = Each(600), Done = w => Highest(w, BuildingType.Smithy) >= 1, Progress = w => Level(w, BuildingType.Smithy, 1),
            },
            new Quest
            {
                Title = "Axemen", Goal = "Research axemen at the smithy (it needs smithy level 2).",
                Why = "Axemen hit hard and carry well: the backbone of raids and attacks.",
                Reward = Each(700), Done = w => w.HumanPlayer != null && w.VillagesOf(w.HumanPlayer.Id).Exists(v => v.IsResearched(UnitType.Axeman)),
            },
            new Quest
            {
                Title = "Strength in numbers", Goal = "Join a tribe, or found your own.",
                Why = "Tribe mates defend each other, share what they've seen, and win the world together.",
                Reward = Each(800), Applies = w => w.Diplomacy, Done = w => w.HumanPlayer?.TribeId >= 0,
            },
            new Quest
            {
                Title = "A proper town", Goal = "Upgrade the Headquarters to level 10.",
                Why = "Bigger buildings, and faster: the stable and the workshop need Headquarters 10, the academy 20.",
                Reward = Each(1500), Done = w => Highest(w, BuildingType.Headquarters) >= 10, Progress = w => Level(w, BuildingType.Headquarters, 10),
            },
            new Quest
            {
                Title = "Eyes on the land", Goal = "Build the stable, research scouts, and send them to a barbarian village.",
                Why = "Scouts show what's in a village before you risk an army on it. The stable needs Headquarters 10, barracks 5 and smithy 5.",
                Reward = Each(2000), Done = HasScouted,
            },
            new Quest
            {
                Title = "The academy", Goal = "Build the academy.",
                Why = "Noblemen win other villages over. The academy needs Headquarters 20, smithy 20 and market 10.",
                Reward = Each(5000), Done = w => Highest(w, BuildingType.Academy) >= 1, Progress = w => Level(w, BuildingType.Academy, 1),
            },
            new Quest
            {
                Title = "Gold coins", Goal = "Mint a gold coin at the academy.",
                Why = "On this world every nobleman needs a noble slot, and slots are bought with coins.",
                Reward = Each(3000), Applies = w => w.Settings.GoldCoins, Done = w => w.HumanPlayer?.Coins > 0,
            },
            new Quest
            {
                Title = "Your first conquest", Goal = "Win a village over with your noblemen.",
                Why = "Clear the village's defenders first, then send noblemen as a train: each one lowers its loyalty, and at zero it's yours.",
                Reward = Each(8000), Done = w => HumanStat(w, StatKind.VillagesConquered) > 0 || (w.HumanPlayer != null && w.VillagesOf(w.HumanPlayer.Id).Count > 1),
            },
        };
        /// <summary>The quest the player is on (null once they're all done).</summary>
        public Quest CurrentQuest
        {
            get
            {
                SkipQuestsThatDontApply();
                return QuestIndex < QuestLine.Length ? QuestLine[QuestIndex] : null;
            }
        }

        void SkipQuestsThatDontApply()
        {
            while (QuestIndex < QuestLine.Length && !QuestLine[QuestIndex].Applies(this))
            {
                QuestIndex++;
                QuestReady = false;
            }
        }

        /// <summary>Marks the current quest done once its goal is met (cheap: call it as often as you like).</summary>
        public void UpdateQuests()
        {
            var quest = CurrentQuest;
            if (quest != null && !QuestReady && HumanPlayer != null && PlayerVillage != null && quest.Done(this)) QuestReady = true;
        }

        /// <summary>
        /// Claims the current quest's reward into the player's current village (as much as its warehouse holds)
        /// and moves on. Returns the quest claimed, or null if there was nothing to claim.
        /// </summary>
        public Quest ClaimQuest()
        {
            var quest = CurrentQuest;
            var v = PlayerVillage;
            if (quest == null || !QuestReady || v == null) return null;
            Touch(v);
            double cap = v.StorageCapacity;
            v.Wood = Math.Min(cap, v.Wood + quest.Reward.Wood);
            v.Clay = Math.Min(cap, v.Clay + quest.Reward.Clay);
            v.Iron = Math.Min(cap, v.Iron + quest.Reward.Iron);
            if (quest.Title == LootAssistantQuest) UnlockLootAssistant();
            QuestIndex++;
            QuestReady = false;
            UpdateQuests(); // the next may already be done
            return quest;
        }
    }
}
