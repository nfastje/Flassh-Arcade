using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>The Headquarters: rename the village, the construction queue, and every building's next upgrade.</summary>
    class HeadquartersView
    {
        public VisualElement Root { get; }
        readonly MedievalWorldConquestGame game;
        readonly TextField name;
        readonly Label queueTitle;
        readonly QueueSlots queueSlots;
        readonly Dictionary<BuildingType, (Label level, Label effect, UpgradeBox upgrade)> rows = new Dictionary<BuildingType, (Label, Label, UpgradeBox)>();
        Village shownFor;

        public HeadquartersView(MedievalWorldConquestGame game)
        {
            this.game = game;
            Root = Element("window-section");

            // Renaming the village.
            var nameRow = Element("name-row", "rename-row");
            nameRow.Add(Text("Village name", "row-title"));
            name = new TextField { maxLength = World.MaxVillageNameLength };
            name.AddToClassList("rename-field");
            nameRow.Add(name);
            nameRow.Add(ButtonWith("Rename", () => game.RenameVillage(name.value), "btn", "btn--small"));
            Root.Add(nameRow);

            // The queue always takes the same room (a slot for each order it can hold), so starting a build
            // doesn't push the list of buildings down.
            queueTitle = Text("", "heading");
            Root.Add(queueTitle);
            queueSlots = new QueueSlots(World.MaxBuildQueue, "Cancel (full refund)");
            Root.Add(queueSlots.Root);

            Root.Add(Text("Buildings", "heading"));
            foreach (var def in Buildings.Definitions)
            {
                var row = Element("build-row", "hq-row");
                var left = Element("hq-row-name");
                var header = Element("row-header");
                var type = def.Type;
                header.Add(Link(def.Name, () => game.OpenBuilding(type), "row-title-link"));
                var level = Text("", "row-level");
                header.Add(level);
                left.Add(header);
                var effect = Text("", "row-info");
                left.Add(effect);
                row.Add(left);
                var upgrade = new UpgradeBox(game);
                row.Add(upgrade.Root);
                rows[def.Type] = (level, effect, upgrade);
                Root.Add(row);
            }
        }

        /// <summary>Called when the window opens, so the name box starts with the village's current name.</summary>
        public void OnOpen() => shownFor = null;

        public void Refresh(World world, Village v)
        {
            if (shownFor != v)
            {
                shownFor = v;
                name.SetValueWithoutNotify(v.Name);
            }

            RefreshQueue(world, v);
            foreach (var def in Buildings.Definitions)
            {
                var (level, effect, upgrade) = rows[def.Type];
                int now = v.Level(def.Type), queued = v.QueuedCount(def.Type), next = v.NextLevel(def.Type);
                SetText(level, queued > 0 ? $"level {now} (+{queued} queued)" : now > 0 ? $"level {now}" : "not built");
                SetText(effect, BuildingText.Effect(world, def.Type, now, next <= def.MaxLevel ? next : (int?)null));
                upgrade.Refresh(world, v, def.Type);
            }
        }

        void RefreshQueue(World world, Village v)
        {
            SetText(queueTitle, $"Construction ({v.Queue.Count}/{World.MaxBuildQueue})");
            for (int i = 0; i < queueSlots.Count; i++)
            {
                if (i >= v.Queue.Count)
                {
                    queueSlots.SetFree(i, i == 0 ? "Nothing being built. Choose an upgrade below." : "Free slot");
                    continue;
                }
                var order = v.Queue[i];
                // Only the last order can be canceled: the ones after depend on the levels before them.
                Action cancel = i == v.Queue.Count - 1 ? game.CancelLastBuild : (Action)null;
                string title = $"{Buildings.Get(order.Type).Name} → level {order.Level}";
                if (order.Started)
                {
                    double left = Math.Max(0, order.FinishTime - world.Now);
                    queueSlots.SetUsed(i, title, Real(world, left), 1 - left / order.Seconds, cancel);
                }
                else queueSlots.SetUsed(i, title, $"waiting · {Real(world, order.Seconds)}", 0, cancel);
            }
        }
    }
}
