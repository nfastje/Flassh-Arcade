using System;
using System.Collections.Generic;

namespace MedievalWorldConquest.Simulation
{
    /// <summary>How two tribes stand with each other.</summary>
    public enum RelationKind
    {
        Neutral = 0,
        /// <summary>Allies: they don't attack each other, and win together.</summary>
        Ally = 1,
        /// <summary>A non-aggression pact: they leave each other alone.</summary>
        NonAggression = 2,
        /// <summary>At war.</summary>
        Enemy = 3,
    }

    /// <summary>
    /// A tribe, as in Tribal Wars: players (lords, and the human if they like) banded together under a leader, who
    /// don't attack each other, help each other defend, share what they've seen, pick targets together, and can win
    /// the world together with their allies. Only on worlds created with diplomacy on.
    /// </summary>
    [Serializable]
    public class Tribe
    {
        public int Id;
        public string Name, Tag;
        public int LeaderId;
        /// <summary>Player ids, the leader included.</summary>
        public List<int> Members = new List<int>();
        public double Founded;
        public int ColorIndex;
        /// <summary>
        /// How much strain the tribe is under, 0 (calm) to 100 (in turmoil): see <see cref="World.TribeTick"/>.
        /// The higher it is, the likelier something breaks: a member leaving, a splinter, a new leader.
        /// </summary>
        public double Tension;
        /// <summary>What's been straining it most lately (a <see cref="TensionCause"/>), which shapes what happens if it breaks.</summary>
        public TensionCause MainCause;
        /// <summary>The village the leader has named as the tribe's target, and until when, or -1.</summary>
        public int TargetVillageId = -1;
        public double TargetUntil;
        /// <summary>Attacks on members that tribe mates are asked to help against.</summary>
        public List<HelpCall> HelpCalls = new List<HelpCall>();
        /// <summary>Members' villages lost to outsiders, and conquests won, since the last tick (they sway the tension).</summary>
        public int LossesSinceTick, ConquestsSinceTick, AbandonedSinceTick;
        /// <summary>Its bloc's share of the world at the last tick: a bloc on the rise keeps its tribes content.</summary>
        public double LastBlocShare;
        /// <summary>
        /// Once the realm splits into factions: the leading tribe of this one's (its own id if it leads one; -1: not
        /// in one yet; -2: turned them down), and when it will choose one (0: not yet decided).
        /// </summary>
        public int FactionId = -1;
        public double FactionJoinAt;
        public bool Disbanded;
    }

    /// <summary>What's straining a tribe, which decides what happens when it gives.</summary>
    public enum TensionCause
    {
        None = 0,
        /// <summary>Members attacked and left to fend for themselves.</summary>
        Abandoned = 1,
        /// <summary>A weak or absent leader.</summary>
        WeakLeader = 2,
        /// <summary>An ambitious member who has outgrown the tribe.</summary>
        Ambition = 3,
        /// <summary>Members too spread out to help each other.</summary>
        Spread = 4,
        /// <summary>A long peace that bores its fighters.</summary>
        Restless = 5,
        /// <summary>Fear of being swallowed by a bloc close to winning.</summary>
        Fear = 6,
        /// <summary>Villages lost, wars going badly.</summary>
        Losing = 7,
    }

    /// <summary>A member's village under attack, and whether any tribe mate has sent help.</summary>
    [Serializable]
    public class HelpCall
    {
        public int VillageId, OwnerId, AttackerId;
        public double ArriveTime;
        public int Supporters;
    }

    /// <summary>A standing between two tribes (stored once, with <see cref="A"/> the lower id).</summary>
    [Serializable]
    public class TribeRelation
    {
        public int A, B;
        public RelationKind Kind;
        public double Since;
    }

    public partial class World
    {
        /// <summary>The most members a tribe takes. (A tuning knob for now, while the endgame is being balanced.)</summary>
        public static int MaxTribeMembers = 20;

        /// <summary>
        /// Whether a whole network of allies (allies of allies too) counts as one bloc for winning the world, rather
        /// than just a tribe and its own allies. On: with at most two alliances a tribe, networks form real sides.
        /// </summary>
        public static bool AllianceNetworks;

        /// <summary>The most alliances a tribe may have (user's choice: two); non-aggression pacts are unlimited.</summary>
        public static int MaxAlliances = 2;

        /// <summary>
        /// With alliance networks: tribes won't ally if the network they'd form would hold more than this share of the
        /// players' villages. (A tuning knob for now; 1 is no limit.)
        /// </summary>
        public static double MaxNetworkToJoin = 1;

        /// <summary>Whether two tribes may ally: each has room for another alliance, and together they'd not make too big a network.</summary>
        public bool CanAlly(Tribe a, Tribe b)
        {
            if (a == null || b == null || a == b) return false;
            if (AlliesOf(a).Count >= MaxAlliances || AlliesOf(b).Count >= MaxAlliances) return false;
            if (AllianceNetworks && MaxNetworkToJoin < 1)
            {
                var joined = BlocOf(a);
                joined.UnionWith(BlocOf(b));
                if (ShareOf(joined) > MaxNetworkToJoin) return false;
            }
            return true;
        }

        /// <summary>The tribes that win (or lose) together with a tribe: it and its allies, or its whole alliance network.</summary>
        public HashSet<int> BlocOf(Tribe t)
        {
            var bloc = new HashSet<int>();
            if (t == null) return bloc;
            bloc.Add(t.Id);
            var frontier = new List<Tribe> { t };
            for (int i = 0; i < frontier.Count; i++)
                foreach (var ally in AlliesOf(frontier[i]))
                    if (bloc.Add(ally.Id) && AllianceNetworks) frontier.Add(ally);
            return bloc;
        }

        public List<Tribe> Tribes = new List<Tribe>();
        public List<TribeRelation> Relations = new List<TribeRelation>();
        public int NextTribeId = 1;

        public bool Diplomacy => Settings.Diplomacy;

        // Tribes and relations by id (not saved; rebuilt when first needed or when the lists change behind their back).
        [NonSerialized] Dictionary<int, Tribe> tribesById;
        [NonSerialized] Dictionary<(int, int), TribeRelation> relationsByPair;

        public Tribe FindTribe(int id)
        {
            if (id < 0) return null;
            if (tribesById == null || tribesById.Count != Tribes.Count)
            {
                tribesById = new Dictionary<int, Tribe>(Tribes.Count);
                foreach (var t in Tribes) tribesById[t.Id] = t;
            }
            return tribesById.TryGetValue(id, out var tribe) && !tribe.Disbanded ? tribe : null;
        }

        /// <summary>A player's tribe, or null.</summary>
        public Tribe TribeOf(Player p) => p == null ? null : FindTribe(p.TribeId);

        public Tribe TribeOf(int playerId) => TribeOf(FindPlayer(playerId));

        /// <summary>The tribes still going.</summary>
        public List<Tribe> ActiveTribes() => Tribes.FindAll(t => !t.Disbanded);

        /// <summary>A player's name with their tribe's tag, e.g. "Aldric the Bold [IW]".</summary>
        public string NameWithTag(Player p)
        {
            if (p == null) return "Barbarians";
            var t = TribeOf(p);
            return t == null ? p.Name : $"{p.Name} [{t.Tag}]";
        }

        // ---------------------------------------------------------------- relations

        public RelationKind Relation(Tribe a, Tribe b)
        {
            if (a == null || b == null || a == b) return RelationKind.Neutral;
            var r = FindRelation(a.Id, b.Id);
            return r?.Kind ?? RelationKind.Neutral;
        }

        TribeRelation FindRelation(int a, int b)
        {
            if (relationsByPair == null || relationsByPair.Count != Relations.Count)
            {
                relationsByPair = new Dictionary<(int, int), TribeRelation>(Relations.Count);
                foreach (var r in Relations) relationsByPair[(r.A, r.B)] = r;
            }
            return relationsByPair.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var found) ? found : null;
        }

        /// <summary>Sets how two tribes stand (neutral removes the record).</summary>
        public void SetRelation(Tribe a, Tribe b, RelationKind kind)
        {
            if (a == null || b == null || a == b) return;
            var r = FindRelation(a.Id, b.Id);
            if (kind == RelationKind.Neutral)
            {
                if (r != null)
                {
                    Relations.Remove(r);
                    relationsByPair?.Remove((r.A, r.B));
                }
                return;
            }
            if (r == null)
            {
                Relations.Add(r = new TribeRelation { A = Math.Min(a.Id, b.Id), B = Math.Max(a.Id, b.Id) });
                relationsByPair?.Add((r.A, r.B), r);
            }
            if (r.Kind != kind) r.Since = Now;
            r.Kind = kind;
        }

        /// <summary>
        /// Whether two players leave each other alone: tribe mates, or members of allied tribes or tribes with a
        /// non-aggression pact (only on diplomacy worlds).
        /// </summary>
        public bool AreFriendly(int playerA, int playerB)
        {
            if (!Diplomacy || playerA < 0 || playerB < 0 || playerA == playerB) return false;
            var a = TribeOf(playerA);
            var b = TribeOf(playerB);
            if (a == null || b == null) return false;
            if (a == b) return true;
            var kind = Relation(a, b);
            return kind == RelationKind.Ally || kind == RelationKind.NonAggression;
        }

        /// <summary>Whether two players' tribes are at war.</summary>
        public bool AtWar(int playerA, int playerB)
        {
            if (!Diplomacy) return false;
            return Relation(TribeOf(playerA), TribeOf(playerB)) == RelationKind.Enemy;
        }

        /// <summary>A tribe's allies.</summary>
        public List<Tribe> AlliesOf(Tribe t)
        {
            var allies = new List<Tribe>();
            if (t == null) return allies;
            foreach (var r in Relations)
            {
                if (r.Kind != RelationKind.Ally || (r.A != t.Id && r.B != t.Id)) continue;
                var other = FindTribe(r.A == t.Id ? r.B : r.A);
                if (other != null) allies.Add(other);
            }
            return allies;
        }

        // ---------------------------------------------------------------- membership

        /// <summary>Founds a tribe with this player as its leader (they leave any tribe they were in).</summary>
        public Tribe FoundTribe(Player founder, string name, string tag)
        {
            if (founder == null || !Diplomacy) return null;
            LeaveTribe(founder);
            name = (name ?? "").Trim();
            tag = (tag ?? "").Trim().ToUpperInvariant();
            if (name.Length == 0 || tag.Length == 0) return null;
            if (name.Length > 32) name = name.Substring(0, 32);
            if (tag.Length > 6) tag = tag.Substring(0, 6);
            var tribe = new Tribe
            {
                Id = NextTribeId++, Name = name, Tag = UniqueTag(tag), LeaderId = founder.Id, Founded = Now, ColorIndex = NextTribeId * 7,
            };
            Tribes.Add(tribe);
            tribeVillagesAt = -1;
            AddMember(tribe, founder);
            return tribe;
        }

        void AddMember(Tribe tribe, Player p)
        {
            tribeVillagesAt = -1; // village counts per tribe have changed
            if (tribe.Members.Contains(p.Id)) return;
            tribe.Members.Add(p.Id);
            p.TribeId = tribe.Id;
            p.JoinedTribeAt = Now;
            p.Satisfaction = 60;
        }

        /// <summary>Whether a tribe has room for another member.</summary>
        public static bool HasRoom(Tribe t) => t.Members.Count < MaxTribeMembers;

        /// <summary>A player joins a tribe (leaving any other). Returns whether they did.</summary>
        public bool JoinTribe(Player p, Tribe tribe)
        {
            if (p == null || tribe == null || tribe.Disbanded || !HasRoom(tribe) || p.TribeId == tribe.Id) return false;
            LeaveTribe(p);
            AddMember(tribe, p);
            return true;
        }

        /// <summary>
        /// A player leaves their tribe. A leader who leaves hands over to the strongest member left; a tribe with no
        /// one left is disbanded (its pacts go with it).
        /// </summary>
        public void LeaveTribe(Player p)
        {
            var tribe = TribeOf(p);
            p.TribeId = -1;
            tribeVillagesAt = -1;
            if (tribe == null) return;
            tribe.Members.Remove(p.Id);
            if (tribe.Members.Count == 0)
            {
                Disband(tribe);
                return;
            }
            if (tribe.LeaderId == p.Id) tribe.LeaderId = StrongestMember(tribe)?.Id ?? tribe.Members[0];
        }

        void Disband(Tribe tribe)
        {
            if (AlliesOf(tribe).Count > 0) Log("allied tribe disbanded");
            tribe.Disbanded = true;
            foreach (int id in tribe.Members)
            {
                var member = FindPlayer(id);
                if (member != null && member.TribeId == tribe.Id) member.TribeId = -1;
            }
            tribe.Members.Clear();
            tribe.HelpCalls.Clear();
            Relations.RemoveAll(r => r.A == tribe.Id || r.B == tribe.Id);
            relationsByPair = null;
        }

        public Player StrongestMember(Tribe tribe, int except = -1)
        {
            Player best = null;
            int bestPoints = -1;
            foreach (int id in tribe.Members)
            {
                if (id == except) continue;
                var p = FindPlayer(id);
                if (p == null || p.Quit) continue;
                int points = PointsOf(p);
                if (points > bestPoints)
                {
                    bestPoints = points;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>A tribe's total points and villages.</summary>
        public (int points, int villages) TribeStrength(Tribe t)
        {
            int points = 0, villages = 0;
            foreach (int id in t.Members)
                foreach (var v in VillagesOf(id))
                {
                    points += v.Points;
                    villages++;
                }
            return (points, villages);
        }

        /// <summary>The middle of a tribe's villages (in fields), or the map's middle if it has none.</summary>
        public (double x, double y) TribeCenter(Tribe t)
        {
            double x = 0, y = 0;
            int n = 0;
            foreach (int id in t.Members)
                foreach (var v in VillagesOf(id))
                {
                    x += v.X;
                    y += v.Y;
                    n++;
                }
            return n == 0 ? (MapSize / 2.0, MapSize / 2.0) : (x / n, y / n);
        }

        /// <summary>Distance in fields from a player's first village to a tribe's middle.</summary>
        public double DistanceToTribe(Player p, Tribe t)
        {
            var own = VillagesOf(p.Id);
            if (own.Count == 0) return double.MaxValue;
            var (cx, cy) = TribeCenter(t);
            return Math.Sqrt((own[0].X - cx) * (own[0].X - cx) + (own[0].Y - cy) * (own[0].Y - cy));
        }

        // ---------------------------------------------------------------- shared sightings

        /// <summary>
        /// The freshest sighting of a village's defenders among a player's tribe mates (themselves included), and who
        /// made it, as Tribal Wars tribes share their reports. Null if none of them has seen it.
        /// </summary>
        public (Player seer, AiNote note) SharedSighting(Player p, int villageId)
        {
            Player bestSeer = null;
            AiNote best = null;
            void Consider(Player q)
            {
                if (q == null || q.IsHuman) return;
                var note = NoteFor(q, villageId, false);
                if (note == null || note.SeenAt < 0 || note.SeenTroops == null) return;
                if (best == null || note.SeenAt > best.SeenAt)
                {
                    best = note;
                    bestSeer = q;
                }
            }
            Consider(p);
            var tribe = Diplomacy ? TribeOf(p) : null;
            if (tribe != null)
                foreach (int id in tribe.Members)
                    if (id != p.Id) Consider(FindPlayer(id));
            return (bestSeer, best);
        }

        /// <summary>What the human's (AI) tribe mates have seen of a village, for its window.</summary>
        public (Player seer, AiNote note) TribeIntel(int villageId)
        {
            var human = HumanPlayer;
            return human == null || TribeOf(human) == null ? (null, null) : SharedSighting(human, villageId);
        }

        // ---------------------------------------------------------------- names

        static readonly string[] TribeAdjectives =
        {
            "Iron", "Crimson", "Northern", "Silver", "Black", "Golden", "Gray", "Storm", "Raven", "Oak", "Stone",
            "Burning", "Hidden", "Last", "Free", "Wild", "Holy", "Red", "White", "Broken", "Ashen", "Border",
        };

        static readonly string[] TribeNouns =
        {
            "Wolves", "Oath", "Crown", "Blades", "Shields", "Company", "Brotherhood", "Hawks", "Legion", "Banners",
            "Guard", "Lords", "Keep", "Riders", "Spears", "Council", "Pact", "Watch", "Hammers", "Kin",
        };

        /// <summary>A tribe name not in use, like "Iron Wolves", and its tag ("IW").</summary>
        (string name, string tag) NewTribeName(Random rng)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                string adjective = TribeAdjectives[rng.Next(TribeAdjectives.Length)], noun = TribeNouns[rng.Next(TribeNouns.Length)];
                string name = attempt < 20 ? $"The {adjective} {noun}" : $"{adjective} {noun} of {PlacePrefixes[rng.Next(PlacePrefixes.Length)]}";
                if (Tribes.Exists(t => !t.Disbanded && t.Name == name)) continue;
                return (name, UniqueTag($"{adjective[0]}{noun[0]}"));
            }
            return ($"Tribe {NextTribeId}", UniqueTag($"T{NextTribeId}"));
        }

        /// <summary>The tag, or the tag with a number after it if another tribe has it.</summary>
        string UniqueTag(string tag)
        {
            if (!Tribes.Exists(t => !t.Disbanded && t.Tag == tag)) return tag;
            for (int n = 2; ; n++)
                if (!Tribes.Exists(t => !t.Disbanded && t.Tag == tag + n)) return tag + n;
        }
    }
}
