using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>Where links go: the windows and views a player or village name can open.</summary>
    public class UiLinks
    {
        public Action<int> OpenVillage, OpenPlayer, OpenReport, ShowOnMap, SendTroops, SwitchTo, SendResources, ShowInRanking;
        /// <summary>Tribes (diplomacy worlds): a tribe's window, inviting or expelling a player, naming a target village.</summary>
        public Action<int> OpenTribe, InviteToTribe, ExpelFromTribe, SetTribeTarget;
    }

    /// <summary>
    /// A window floating over the right of the screen, closable, that doesn't stop the map or village underneath
    /// from being used: the village and player windows.
    /// </summary>
    public abstract class InfoWindow
    {
        public VisualElement Root { get; }
        public bool IsOpen => Root.style.display == DisplayStyle.Flex;
        protected readonly UiLinks links;
        protected readonly Label title;
        protected readonly VisualElement body;

        protected InfoWindow(UiLinks links)
        {
            this.links = links;
            Root = Element("panel", "info-window");
            var header = Element("window-header");
            title = Text("", "window-title");
            header.Add(title);
            header.Add(ButtonWith("Close", Close, "btn", "btn--small"));
            Root.Add(header);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("window-body");
            body = Element();
            scroll.Add(body);
            Root.Add(scroll);
            Show(Root, false);
        }

        public virtual void Close() => Show(Root, false);

        public abstract void Refresh(World world);

        // Text that changes by the second (countdowns, stores) is updated in place, so the window is only rebuilt
        // when what it shows changes: rebuilding replaces its buttons, and a click that starts on the old button
        // and ends on the new one does nothing.
        readonly List<(Label label, Func<string> text)> live = new List<(Label, Func<string>)>();

        /// <summary>Empties the window for rebuilding.</summary>
        protected void Clear()
        {
            body.Clear();
            live.Clear();
        }

        /// <summary>A line of text kept up to date every frame.</summary>
        protected Label Live(Func<string> text, params string[] classes)
        {
            var label = Text(text(), classes);
            live.Add((label, text));
            return label;
        }

        protected void RefreshLive()
        {
            foreach (var (label, text) in live) SetText(label, text());
        }

        /// <summary>A line of text with link buttons in it: pieces are strings (plain) or (text, action) pairs (links).</summary>
        protected static VisualElement Line(params object[] pieces)
        {
            var line = Element("link-line");
            foreach (var piece in pieces)
            {
                if (piece is string s) line.Add(Text(s, "row-info", "link-text"));
                else if (piece is ValueTuple<string, Action> link) line.Add(Link(link.Item1, link.Item2));
                else if (piece is VisualElement e) line.Add(e);
            }
            return line;
        }

        protected static string Clock(double time) => World.FormatClock(time);
    }

    // -------------------------------------------------------------------- village

    /// <summary>
    /// Everything the player knows about one village: who owns it, where it is, its size, and what their reports
    /// have shown (what scouts saw, the defenders last seen, the wall and loyalty), plus the attacks and returns on
    /// their way to it. For the player's own villages, the real figures. Opened by clicking a village on the map or
    /// a village's name anywhere.
    /// </summary>
    public class VillageWindow : InfoWindow
    {
        public int? VillageId { get; private set; }
        string signature;

        public VillageWindow(UiLinks links) : base(links) { }

        public void Open(int villageId)
        {
            VillageId = villageId;
            signature = null;
            Show(Root, true);
        }

        public override void Close()
        {
            VillageId = null;
            base.Close();
        }

        public override void Refresh(World world)
        {
            if (!IsOpen || !VillageId.HasValue) return;
            var v = world.FindVillage(VillageId.Value);
            if (v == null)
            {
                Close();
                return;
            }
            SetText(title, v.Name);

            // Rebuild only when something shown changes (it's all small text, so a coarse signature will do).
            var home = world.PlayerVillage;
            var about = world.ReportsAbout(v.Id);
            var moving = world.HumanPlayer == null ? new List<Command>() : world.CommandsOf(world.HumanPlayer.Id).FindAll(c => c.ToVillageId == v.Id || c.FromVillageId == v.Id);
            bool mine = v.OwnerId == world.HumanPlayer?.Id;
            string now = $"{v.OwnerId}|{v.Name}|{v.Points}|{about.Count}|{moving.Count}|{home?.Id}|{(int)v.Loyalty}|{world.IsProtected(v.OwnerId)}" +
                         (mine ? "|" + string.Join(",", v.Troops) : "");
            if (now == signature)
            {
                RefreshLive();
                return;
            }
            signature = now;
            Clear();

            var owner = world.FindPlayer(v.OwnerId);
            var ownerTribe = world.TribeOf(owner);
            body.Add(owner == null
                ? Line("Owner: ", "barbarians (nobody)")
                : ownerTribe == null
                    ? Line("Owner: ", (owner.IsHuman ? $"{owner.Name} (you)" : owner.Name, (Action)(() => links.OpenPlayer(owner.Id))))
                    : Line("Owner: ", (owner.IsHuman ? $"{owner.Name} (you)" : owner.Name, (Action)(() => links.OpenPlayer(owner.Id))),
                        ($"[{ownerTribe.Tag}]", (Action)(() => links.OpenTribe(ownerTribe.Id)))));
            if (owner != null && !mine && world.AreFriendly(world.HumanPlayer.Id, owner.Id))
                body.Add(Text(world.TribeOf(world.HumanPlayer) == ownerTribe ? "A tribe mate: attacking them gets you thrown out of the tribe."
                    : "Your tribe has a pact with theirs: attacking them breaks it.", "row-info", "protection-note"));
            body.Add(Line($"Location: ({v.X}|{v.Y}) {World.ContinentName(v.X, v.Y)}  ·  {world.TerrainAt(v.X, v.Y)}" + (home != null && home != v ? $"  ·  {World.Distance(home, v):0.0} fields from {home.Name}" : "")));
            body.Add(Line($"Points: {v.Points:N0}" + (mine && v.Loyalty < World.MaxLoyalty ? $"  ·  loyalty {Math.Floor(v.Loyalty):0}" : "")));
            if (owner != null && !mine && world.IsProtected(owner.Id))
                body.Add(Live(() => $"Under beginner protection for {Real(world, Math.Max(0, owner.ProtectedUntil - world.Now))}: it can't be attacked yet.", "row-info", "protection-note"));

            // What can be done from here.
            var actions = Element("option-row", "info-actions");
            if (!mine) actions.Add(ButtonWith("Attack", () => links.SendTroops(v.Id), "btn", "btn--small"));
            if (v != home) actions.Add(ButtonWith("Support", () => links.SendTroops(v.Id), "btn", "btn--small"));
            if (v != home && home != null && home.Level(BuildingType.Market) > 0)
                actions.Add(ButtonWith("Send resources", () => links.SendResources(v.Id), "btn", "btn--small"));
            if (mine && v != home) actions.Add(ButtonWith("Switch to", () => links.SwitchTo(v.Id), "btn", "btn--small"));
            // A tribe leader names targets for the tribe.
            var myTribe = world.TribeOf(world.HumanPlayer);
            if (myTribe != null && myTribe.LeaderId == world.HumanPlayer.Id && !mine && !world.AreFriendly(world.HumanPlayer.Id, v.OwnerId))
                actions.Add(ButtonWith(myTribe.TargetVillageId == v.Id && myTribe.TargetUntil > world.Now ? "Tribe target" : "Make tribe target",
                    () => links.SetTribeTarget(v.Id), "btn", "btn--small"));
            actions.Add(ButtonWith("Show on map", () => links.ShowOnMap(v.Id), "btn", "btn--small"));
            body.Add(actions);

            if (mine) ShowOwnVillage(world, v);
            else ShowIntel(world, v, about);

            if (moving.Count > 0)
            {
                body.Add(Text("Your troops on the way", "heading"));
                moving.Sort((a, b) => a.ArriveTime.CompareTo(b.ArriveTime));
                foreach (var c in moving)
                {
                    string what = c.Kind == CommandKind.Attack ? "Attack" : c.Kind == CommandKind.Support ? "Support"
                        : c.Kind == CommandKind.Transport ? "Merchants" : c.Kind == CommandKind.TransportReturn ? "Merchants returning" : "Returning home";
                    var line = Element("link-line");
                    line.Add(Live(() => $"{what}: arrives {Clock(c.ArriveTime)} (in {Real(world, Math.Max(0, c.ArriveTime - world.Now))})", "row-info", "link-text"));
                    body.Add(line);
                }
            }

            if (about.Count > 0)
            {
                body.Add(Text("Reports", "heading"));
                for (int i = 0; i < about.Count && i < 8; i++)
                {
                    var r = about[i];
                    body.Add(Line(Clock(r.Time) + "  ", (ReportsPanel.Title(r), (Action)(() => links.OpenReport(r.Id)))));
                }
            }
        }

        /// <summary>The player's own village: its real troops, stores and buildings.</summary>
        void ShowOwnVillage(World world, Village v)
        {
            body.Add(Text("Troops at home", "heading"));
            body.Add(TroopIcons(v.Troops));
            body.Add(Text("Resources", "heading"));
            var stores = Element("link-line");
            stores.Add(Live(() => $"Wood {Math.Floor(v.Stock(ResourceType.Wood)):N0} · clay {Math.Floor(v.Stock(ResourceType.Clay)):N0} · iron {Math.Floor(v.Stock(ResourceType.Iron)):N0} (holds {v.StorageCapacity:N0})", "row-info", "link-text"));
            body.Add(stores);
            body.Add(Text("Buildings", "heading"));
            body.Add(Line(BuildingSummary(v.Levels)));
        }

        /// <summary>Another village: only what reports have shown.</summary>
        void ShowIntel(World world, Village v, List<BattleReport> about)
        {
            body.Add(Text("What you know", "heading"));
            var scouted = world.LatestScouting(v.Id);
            var seen = world.LatestSighting(v.Id);
            // What tribe mates have seen, as Tribal Wars tribes share their reports.
            var intel = world.TribeIntel(v.Id);
            if (intel.note != null)
            {
                body.Add(Line($"{intel.seer.Name} (tribe mate) saw it {Clock(intel.note.SeenAt)}:"));
                body.Add(TroopIcons(intel.note.SeenTroops));
                body.Add(Line($"Wall: level {intel.note.SeenWall}"));
            }
            if (scouted == null && seen == null)
            {
                if (intel.note == null) body.Add(Text("Nothing yet. Send scouts to see its buildings, stores and defenders.", "row-info"));
                return;
            }
            if (seen != null)
            {
                body.Add(Line($"Defenders seen {Clock(seen.Time)} (before the fight):"));
                body.Add(TroopIcons(seen.DefenderTroops));
                body.Add(Line($"Wall: level {seen.WallAfter}" + (seen.LoyaltyAfter >= 0 ? $"  ·  loyalty {seen.LoyaltyAfter} after noblemen" : "")));
            }
            if (scouted != null)
            {
                var res = scouted.ScoutedResources;
                body.Add(Line($"Scouted {Clock(scouted.Time)}: wood {res.Wood:N0} · clay {res.Clay:N0} · iron {res.Iron:N0}"));
                body.Add(Line(BuildingSummary(scouted.ScoutedLevels)));
            }
        }

        static string BuildingSummary(int[] levels)
        {
            var parts = new List<string>();
            for (int i = 0; i < Buildings.Count && i < levels.Length; i++)
                if (levels[i] > 0) parts.Add($"{Buildings.Get((BuildingType)i).Name} {levels[i]}");
            return string.Join(" · ", parts);
        }

        /// <summary>A row of unit icons with counts (only the units present).</summary>
        static VisualElement TroopIcons(int[] troops)
        {
            var grid = Element("troop-grid");
            bool any = false;
            foreach (var type in Units.InDisplayOrder)
            {
                int i = (int)type;
                if (i >= troops.Length || troops[i] <= 0) continue;
                any = true;
                var cell = Element("troop-grid-cell");
                cell.Add(Icons.Element(Icons.Unit(type), 20));
                cell.Add(Text($"{troops[i]:N0}", "garrison-count"));
                cell.tooltip = Units.Get(type).Name;
                grid.Add(cell);
            }
            if (!any) grid.Add(Text("None.", "row-info"));
            return grid;
        }
    }

    // -------------------------------------------------------------------- player

    /// <summary>A player's profile: their standing and every village they hold, each a link to its window.</summary>
    public class PlayerWindow : InfoWindow
    {
        public int? PlayerId { get; private set; }
        string signature;

        public PlayerWindow(UiLinks links) : base(links) { }

        public void Open(int playerId)
        {
            PlayerId = playerId;
            signature = null;
            Show(Root, true);
        }

        public override void Close()
        {
            PlayerId = null;
            base.Close();
        }

        public override void Refresh(World world)
        {
            if (!IsOpen || !PlayerId.HasValue) return;
            var p = world.FindPlayer(PlayerId.Value);
            if (p == null)
            {
                Close();
                return;
            }
            var villages = new List<Village>(world.VillagesOf(p.Id));
            int points = 0;
            foreach (var v in villages) points += v.Points;
            string now = $"{villages.Count}|{points}|{world.PlayerVillage?.Id}|{world.IsProtected(p.Id)}|{p.TribeId}|{world.HumanPlayer?.TribeId}|{world.HumanPlayer?.Invited.Count}";
            if (now == signature)
            {
                RefreshLive();
                return;
            }
            signature = now;
            SetText(title, p.IsHuman ? $"{p.Name} (you)" : p.Name);
            Clear();

            // Their tribe, and what the player can do about it.
            if (world.Diplomacy)
            {
                var theirs = world.TribeOf(p);
                var mine = world.TribeOf(world.HumanPlayer);
                body.Add(theirs == null ? Line("Tribe: none")
                    : Line("Tribe: ", ($"{theirs.Name} [{theirs.Tag}]", (Action)(() => links.OpenTribe(theirs.Id))), theirs.LeaderId == p.Id ? "  (leader)" : ""));
                if (!p.IsHuman && mine != null && mine.LeaderId == world.HumanPlayer.Id)
                {
                    var tribeActions = Element("option-row", "info-actions");
                    int pid = p.Id;
                    if (theirs == mine) tribeActions.Add(ButtonWith("Expel from tribe", () => links.ExpelFromTribe(pid), "btn", "btn--small"));
                    else if (p.Personality != AiPersonality.Inactive && !p.Quit)
                        tribeActions.Add(ButtonWith(world.HumanPlayer.Invited.Contains(pid) ? "Invited" : "Invite to your tribe", () => links.InviteToTribe(pid), "btn", "btn--small"));
                    body.Add(tribeActions);
                }
            }

            var rankings = world.Rankings();
            int rank = rankings.FindIndex(r => r.Player == p) + 1;
            if (rank > 0)
                body.Add(Line(($"Rank {rank:N0} of {rankings.Count:N0}", (Action)(() => links.ShowInRanking(p.Id))),
                    $"  ·  {points:N0} points  ·  {villages.Count} village{(villages.Count == 1 ? "" : "s")}"));
            else body.Add(Line(p.Quit ? "Has given up: their villages went barbarian." : "No villages left."));
            if (world.IsProtected(p.Id))
                body.Add(Live(() => $"Under beginner protection for {Real(world, Math.Max(0, p.ProtectedUntil - world.Now))}.", "row-info", "protection-note"));

            body.Add(Text("Villages", "heading"));
            var home = world.PlayerVillage;
            villages.Sort((a, b) => b.Points.CompareTo(a.Points));
            foreach (var v in villages)
            {
                var row = Element("info-village-row");
                row.Add(Link(v.Name, () => links.OpenVillage(v.Id)));
                row.Add(Text($"({v.X}|{v.Y}) {World.ContinentName(v.X, v.Y)}", "row-level"));
                row.Add(Element("spacer"));
                row.Add(Text($"{v.Points:N0} pts" + (home != null && home != v ? $"  ·  {World.Distance(home, v):0.0} fields" : ""), "row-level"));
                body.Add(row);
            }
            if (villages.Count == 0) body.Add(Text("None.", "row-info"));
        }
    }

    // -------------------------------------------------------------------- a troop movement, with links

    /// <summary>
    /// One troop movement as a line: what it is, the village it's from or bound for (a link), its owner (a link),
    /// and when it arrives. Click the line to see what's in it (the player's own troops, or what their merchants
    /// carry; what's coming at the player stays unknown until it lands). Updated in place every frame.
    /// </summary>
    public class MovementRow
    {
        /// <summary>The movements whose contents are showing, by command id (not saved).</summary>
        static readonly HashSet<int> Expanded = new HashSet<int>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Expanded.Clear();

        public VisualElement Root { get; }
        public int CommandId { get; private set; }
        readonly VisualElement line, details, speedIcon;
        AttackSpeed? speedShown;
        readonly Label caret, kind, time, when;
        readonly Button village, player;
        readonly Label target;
        int villageId = -1, playerId = -1, targetId = -1;
        /// <summary>What the details show (the command and whether they're open), to rebuild them only when it changes.</summary>
        string detailsShown;

        public MovementRow(UiLinks links)
        {
            Root = Element("movement-item");
            line = Element("movement-row");
            line.tooltip = "Click to see what's in it";
            caret = Text("▸", "movement-caret");
            line.Add(caret);
            // An incoming attack's speed as a unit icon: all the player can tell about it before it lands.
            speedIcon = Icons.Element(Icons.Unit(UnitType.Axeman), 18, "attack-speed-icon");
            speedIcon.pickingMode = PickingMode.Position;
            line.Add(speedIcon);
            kind = Text("", "movement-kind");
            line.Add(kind);
            village = Link("", () => { if (villageId >= 0) links.OpenVillage(villageId); });
            line.Add(village);
            player = Link("", () => { if (playerId >= 0) links.OpenPlayer(playerId); }, "link--owner");
            line.Add(player);
            target = Text("", "row-info", "movement-target");
            target.RegisterCallback<ClickEvent>(_ => { if (targetId >= 0) links.OpenVillage(targetId); });
            line.Add(target);
            line.Add(Element("spacer"));
            when = Text("", "row-level", "movement-when");
            line.Add(when);
            time = Text("", "row-title", "movement-countdown");
            line.Add(time);
            // A click anywhere on the line but its links opens or closes the details.
            line.RegisterCallback<ClickEvent>(e =>
            {
                var hit = e.target as VisualElement;
                if (hit == target || hit is Button || hit?.parent is Button) return;
                if (!Expanded.Remove(CommandId)) Expanded.Add(CommandId);
            });
            Root.Add(line);
            details = Element("movement-details");
            Root.Add(details);
        }

        /// <summary>The troops (or goods) in a movement, as icons and counts.</summary>
        void ShowDetails(World world, Command c, bool incoming)
        {
            details.Clear();
            if (incoming)
            {
                details.Add(Text("You can't tell what's in it until it arrives. Scouts only see what's at home.", "row-info"));
                return;
            }
            if (c.IsTrade)
            {
                if (c.Kind == CommandKind.TransportReturn)
                {
                    details.Add(Text($"{c.Merchants:N0} merchants on their way home, empty.", "row-info"));
                    return;
                }
                details.Add(Text($"{c.Merchants:N0} merchants carrying", "row-info"));
                AddGoods(c.Loot);
                return;
            }
            bool any = false;
            foreach (var type in Units.InDisplayOrder)
            {
                int n = c.Troops != null && (int)type < c.Troops.Length ? c.Troops[(int)type] : 0;
                if (n <= 0) continue;
                any = true;
                var chip = Element("movement-unit");
                chip.tooltip = Units.Get(type).Name;
                chip.Add(Icons.Element(Icons.Unit(type), 18, "cost-icon"));
                chip.Add(Text($"{n:N0}", "cost-value"));
                details.Add(chip);
            }
            if (!any) details.Add(Text("No troops.", "row-info"));
            if (c.Kind == CommandKind.Return && c.Loot.Wood + c.Loot.Clay + c.Loot.Iron > 0)
            {
                details.Add(Text("  carrying", "row-info"));
                AddGoods(c.Loot);
            }
        }

        void AddGoods(Cost goods)
        {
            for (int i = 0; i < 3; i++)
            {
                var r = (ResourceType)i;
                int amount = goods.Get(r);
                if (amount <= 0) continue;
                var chip = Element("movement-unit");
                chip.Add(Icons.Element(Icons.Resource(r), 18, "cost-icon"));
                chip.Add(Text($"{amount:N0}", "cost-value"));
                details.Add(chip);
            }
        }

        public void Update(World world, Command c)
        {
            CommandId = c.Id;
            var human = world.HumanPlayer;
            bool incoming = human != null && c.OwnerId != human.Id;
            var from = world.FindVillage(c.FromVillageId);
            var to = world.FindVillage(c.ToVillageId);

            // Incoming: where it's from and whose it is. Outgoing: the village it's bound for and its owner. Coming
            // home: the village it's coming back from.
            bool back = c.Kind == CommandKind.Return || c.Kind == CommandKind.TransportReturn;
            var shown = incoming || back ? from : to;
            SetText(kind, incoming
                ? (c.Kind == CommandKind.Attack ? "Incoming attack from" : c.Kind == CommandKind.Transport ? "Merchants coming from"
                    : c.Kind == CommandKind.Support ? "Support coming from" : "Troops coming from")
                : c.Kind switch
                {
                    CommandKind.Attack => "Attack on",
                    CommandKind.Support => "Support to",
                    CommandKind.Transport => "Merchants to",
                    CommandKind.TransportReturn => "Merchants back from",
                    _ => "Returning from",
                });
            villageId = shown?.Id ?? -1;
            SetText(village, shown != null ? $"{shown.Name} ({shown.X}|{shown.Y})" : "?");
            var owner = shown != null ? world.FindPlayer(shown.OwnerId) : null;
            playerId = owner?.Id ?? -1;
            Show(player, shown != null && !(owner?.IsHuman ?? false));
            SetText(player, owner != null ? owner.Name : "barbarians");
            player.SetEnabled(owner != null);
            // With several villages, which of the player's it concerns.
            var own = incoming || back ? to : from;
            targetId = own?.Id ?? -1;
            bool several = human != null && world.VillagesOf(human.Id).Count > 1;
            Show(target, several && own != null);
            if (several && own != null) SetText(target, incoming || back ? $"→ {own.Name}" : $"from {own.Name}");
            // When it gets there (for troops coming home: when they're back), as a countdown and on the clock; the
            // player's own attacks also say roughly when the survivors will be home again.
            SetText(time, Real(world, Math.Max(0, c.ArriveTime - world.Now)));
            SetText(when, World.FormatClock(c.ArriveTime) +
                          (!incoming && c.Kind == CommandKind.Attack ? $"  ·  back about {World.FormatClock(c.ArriveTime + (c.ArriveTime - c.DepartTime))}" : ""));
            line.EnableInClassList("movement-row--incoming", incoming && c.Kind == CommandKind.Attack);
            line.EnableInClassList("movement-row--return", back || c.IsTrade);
            bool attackOnUs = incoming && c.Kind == CommandKind.Attack;
            Show(speedIcon, attackOnUs);
            AttackSpeed? speed = attackOnUs ? AttackSpeeds.Of(c.Troops) : (AttackSpeed?)null;
            if (speed != speedShown)
            {
                speedShown = speed;
                if (speed.HasValue)
                {
                    speedIcon.style.backgroundImage = new StyleBackground(Icons.Unit(AttackSpeeds.Icon(speed.Value)));
                    speedIcon.tooltip = AttackSpeeds.Describe(speed.Value);
                }
            }
            line.EnableInClassList("movement-row--danger", speed.HasValue && AttackSpeeds.IsDangerous(speed.Value));

            bool open = Expanded.Contains(c.Id);
            SetText(caret, open ? "▾" : "▸");
            Show(details, open);
            string shownNow = open ? $"{c.Id}" : "";
            if (shownNow != detailsShown)
            {
                detailsShown = shownNow;
                if (open) ShowDetails(world, c, incoming);
            }
        }
    }
}
