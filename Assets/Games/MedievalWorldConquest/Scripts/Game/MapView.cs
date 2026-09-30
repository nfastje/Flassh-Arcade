using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The world map, drawn in the scene well away from the village (at <see cref="Origin"/>): a generated terrain
    /// picture of the whole world plus a marker for every village. One map field is one world unit.
    /// </summary>
    public class MapView : MonoBehaviour
    {
        /// <summary>Where field (0, 0)'s corner sits in the scene. Far from the village view so they never overlap.</summary>
        public static readonly Vector2 Origin = new Vector2(1000f, 0f);
        const int PixelsPerField = 6;

        // Tribal Wars' map colors: your villages yellow, barbarians gray, everyone else in their own colors.
        public static readonly Color PlayerColor = new Color(1f, 0.86f, 0.12f);
        /// <summary>The village the player is viewing from: white, as in Tribal Wars (their other villages are yellow).</summary>
        public static readonly Color CurrentVillageColor = Color.white;
        public static readonly Color BarbarianColor = new Color(0.62f, 0.62f, 0.62f);

        /// <summary>Each rival lord's color (by <see cref="Player.ColorIndex"/>): none of them yellow or gray.</summary>
        public static readonly Color[] RivalColors =
        {
            new Color(0.88f, 0.22f, 0.18f), new Color(0.35f, 0.62f, 1f), new Color(0.62f, 0.35f, 0.85f),
            new Color(0.92f, 0.36f, 0.66f), new Color(0.98f, 0.55f, 0.12f), new Color(0.55f, 0.1f, 0.14f),
            new Color(0.25f, 0.8f, 0.7f), new Color(0.18f, 0.2f, 0.45f), new Color(0.55f, 0.88f, 0.25f),
            new Color(1f, 0.68f, 0.68f), new Color(0.55f, 0.34f, 0.16f), new Color(0.1f, 0.5f, 0.3f),
            new Color(0.75f, 0.6f, 1f), new Color(0.35f, 0.18f, 0.08f), new Color(0.2f, 0.4f, 0.75f),
            new Color(0.8f, 0.5f, 0.4f),
        };

        /// <summary>
        /// Village points at which the map shows a bigger village: Tribal Wars' own steps (under 300, 300, 1,000,
        /// 3,000, 9,000 and 11,000 points).
        /// </summary>
        public static readonly int[] TierPoints = { 300, 1000, 3000, 9000, 11000 };

        /// <summary>Which of the six map sizes a village of these points shows as (0 to 5).</summary>
        public static int TierOf(int points)
        {
            int tier = 0;
            while (tier < TierPoints.Length && points >= TierPoints[tier]) tier++;
            return tier;
        }

        // Tribal Wars' tribe colors: your tribe blue, allies turquoise, pacts purple, enemies red, the tribeless brown.
        public static readonly Color TribeMateColor = new Color(0.2f, 0.45f, 1f);
        public static readonly Color AllyColor = new Color(0.2f, 0.85f, 0.85f);
        public static readonly Color PactColor = new Color(0.7f, 0.35f, 0.9f);
        public static readonly Color EnemyColor = new Color(0.9f, 0.12f, 0.08f);
        public static readonly Color TribelessColor = new Color(0.55f, 0.4f, 0.25f);

        /// <summary>On a diplomacy world, whether the map colors villages by tribe (as Tribal Wars does) rather than by player.</summary>
        public static bool ColorByTribe = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetColorMode() => ColorByTribe = true;

        /// <summary>
        /// The color a village's marker has: the player's yellow (white for the one they're viewing from),
        /// barbarians gray, and everyone else either in their own color or, on a diplomacy world, by how their tribe
        /// stands with the player's.
        /// </summary>
        public static Color OwnerColor(World world, Village v)
        {
            if (v.IsBarbarian) return BarbarianColor;
            var owner = world.FindPlayer(v.OwnerId);
            if (owner == null) return BarbarianColor;
            if (owner.IsHuman) return v == world.PlayerVillage ? CurrentVillageColor : PlayerColor;
            if (world.Diplomacy && ColorByTribe)
            {
                var theirs = world.TribeOf(owner);
                if (theirs == null) return TribelessColor;
                var mine = world.TribeOf(world.HumanPlayer);
                if (mine == theirs) return TribeMateColor;
                switch (world.Relation(mine, theirs))
                {
                    case RelationKind.Ally: return AllyColor;
                    case RelationKind.NonAggression: return PactColor;
                    case RelationKind.Enemy: return EnemyColor;
                }
                return TribeColor(theirs);
            }
            return RivalColors[owner.ColorIndex % RivalColors.Length];
        }

        /// <summary>A tribe's own color, for tribes the player has no dealings with (never one of the relation colors).</summary>
        public static Color TribeColor(Tribe t)
        {
            var c = RivalColors[t.ColorIndex % RivalColors.Length];
            // Keep clear of the relation colors: soften towards the tribeless brown.
            return Color.Lerp(c, TribelessColor, 0.25f);
        }

        SpriteRenderer terrain, selection, homeGlow;
        readonly Dictionary<int, SpriteRenderer> markers = new Dictionary<int, SpriteRenderer>();
        /// <summary>Which picture each village's marker shows (tier × 2, plus 1 if barbarian).</summary>
        readonly Dictionary<int, int> markerKeys = new Dictionary<int, int>();
        float refreshTimer;

        public static MapView Create(World world)
        {
            var view = new GameObject("Map View").AddComponent<MapView>();
            view.Build(world);
            return view;
        }

        /// <summary>
        /// A field's width in scene units (its height is 1): fields are wider than tall, 53 by 38 like the tiles of
        /// Tribal Wars' map. Distances in the game are still counted in fields.
        /// </summary>
        public const float FieldWidth = 53f / 38f;

        /// <summary>Thin grid lines every this many fields, and heavier ones round each block of this many.</summary>
        public const int SectorSize = 5, BlockSize = 25;

        public static Vector2 FieldCenter(int x, int y) => Origin + new Vector2((x + 0.5f) * FieldWidth, y + 0.5f);

        /// <summary>A scene position in map fields (fractional; may be off the map).</summary>
        public static Vector2 ToFields(Vector2 scenePosition) =>
            new Vector2((scenePosition.x - Origin.x) / FieldWidth, scenePosition.y - Origin.y);

        /// <summary>A point in map fields as a scene position.</summary>
        public static Vector2 FromFields(Vector2 fields) => Origin + new Vector2(fields.x * FieldWidth, fields.y);

        /// <summary>The map field under a scene position (may be off the map).</summary>
        public static Vector2Int FieldAt(Vector2 scenePosition)
        {
            var p = ToFields(scenePosition);
            return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
        }

        void Build(World world)
        {
            terrain = AddSprite("Terrain", MakeTerrainSprite(world.Settings.Seed), -200);
            terrain.transform.position = Origin;
            terrain.transform.localScale = new Vector3(FieldWidth, 1f, 1f); // drawn square, stretched to wide fields

            homeGlow = AddSprite("Home", VillageArt.Glow, -150);
            homeGlow.color = new Color(1f, 1f, 1f, 0.55f);
            homeGlow.transform.localScale = new Vector3(2.6f * FieldWidth, 2.6f, 1f);

            selection = AddSprite("Selection", VillageArt.Glow, -140);
            selection.color = new Color(1f, 1f, 1f, 0.9f);
            selection.transform.localScale = new Vector3(2f * FieldWidth, 2f, 1f);
            selection.enabled = false;

            BuildGrid();

            Refresh(world, force: true);
        }

        SpriteRenderer AddSprite(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = VillageArt.Material;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Keeps village markers in step with the world (new villages, owners and sizes). Cheap; runs a few times a second.</summary>
        public void Refresh(World world, bool force = false, float dt = 0f)
        {
            refreshTimer -= dt;
            // (At once, not at the next half-second, when the player switches villages: the colors change.)
            int current = world.PlayerVillage?.Id ?? -1;
            if (!force && refreshTimer > 0f && current == shownCurrent) return;
            refreshTimer = 0.5f;
            shownCurrent = current;

            var human = world.HumanPlayer;
            foreach (var v in world.Villages)
            {
                if (!markers.TryGetValue(v.Id, out var marker))
                {
                    // In front of the terrain and glows; lower villages overlap the ones above them. Each village has
                    // three layers: the outline marking the player's own, the picture, and the owner's dot.
                    int order = (10 + World.MapSize - v.Y) * 3;
                    var center = FieldCenter(v.X, v.Y);
                    marker = AddSprite("Village", VillageArt.MapVillage(0, false), order + 1);
                    marker.transform.position = center;
                    // A field each (a little wider than tall, like the fields), so neighbors don't overlap.
                    marker.transform.localScale = new Vector3(1.15f, 0.96f, 1f);
                    markers[v.Id] = marker;
                    markerKeys[v.Id] = -1;

                    var dot = AddSprite("Owner", VillageArt.MapDot, order + 2);
                    dot.transform.position = center + new Vector2(-0.38f * FieldWidth, 0.36f);
                    dot.transform.localScale = Vector3.one * 0.28f;
                    dots[v.Id] = dot;

                    var ring = AddSprite("Own village", VillageArt.MapRing, order);
                    ring.transform.position = center;
                    ring.transform.localScale = new Vector3(0.97f * FieldWidth, 0.97f, 1f);
                    rings[v.Id] = ring;
                }

                // As on Tribal Wars' map: villages in full color by size, barbarians gray, and a dot in the corner in
                // the owner's color. Yours are outlined too: yellow, and white for the one you're viewing from.
                bool mine = human != null && v.OwnerId == human.Id;
                int key = TierOf(v.Points) * 2 + (v.IsBarbarian ? 1 : 0);
                if (markerKeys[v.Id] != key)
                {
                    markerKeys[v.Id] = key;
                    marker.sprite = VillageArt.MapVillage(key / 2, v.IsBarbarian);
                }
                dots[v.Id].enabled = !v.IsBarbarian;
                dots[v.Id].color = OwnerColor(world, v);
                rings[v.Id].enabled = mine;
                if (mine) rings[v.Id].color = dots[v.Id].color;
                if (mine && v == world.PlayerVillage) homeGlow.transform.position = FieldCenter(v.X, v.Y);
            }
        }

        readonly Dictionary<int, SpriteRenderer> dots = new Dictionary<int, SpriteRenderer>();
        int shownCurrent = -1;
        readonly Dictionary<int, SpriteRenderer> rings = new Dictionary<int, SpriteRenderer>();

        public void Select(Village v)
        {
            selection.enabled = v != null;
            if (v != null) selection.transform.position = FieldCenter(v.X, v.Y);
        }

        // ---------------------------------------------------------------- grid lines

        /// <summary>
        /// One tier of grid lines: every <see cref="Step"/> fields, a line <see cref="Pixels"/> screen pixels thick in
        /// <see cref="Color"/>, fully shown when zoomed in to <see cref="FullUntil"/> (the camera's half-height in
        /// fields) and faded away by <see cref="GoneAt"/>.
        /// </summary>
        class GridTier
        {
            public int Step;
            public float Pixels, FullUntil, GoneAt;
            public Color Color;
            public readonly List<SpriteRenderer> Vertical = new List<SpriteRenderer>(), Horizontal = new List<SpriteRenderer>();
        }

        // As on Tribal Wars' map: zoomed right in, a faint line round every field; further out only the 5 x 5
        // sectors; further still only the 25 x 25 blocks, which always show.
        readonly GridTier[] grid =
        {
            new GridTier { Step = 1, Pixels = 1, FullUntil = 6, GoneAt = 9, Color = new Color(0f, 0f, 0f, 0.12f) },
            new GridTier { Step = SectorSize, Pixels = 1, FullUntil = 28, GoneAt = 42, Color = new Color(0.05f, 0.08f, 0.02f, 0.35f) },
            new GridTier { Step = BlockSize, Pixels = 2, FullUntil = float.MaxValue, GoneAt = float.MaxValue, Color = new Color(0.05f, 0.06f, 0.02f, 0.6f) },
        };
        float gridZoom = -1, gridPixelSize = -1;

        /// <summary>
        /// Lays out every grid line as a thin sprite over the terrain. A line that a coarser tier also draws is left
        /// to that tier, so no line is drawn twice.
        /// </summary>
        void BuildGrid()
        {
            float width = World.MapSize * FieldWidth;
            for (int t = 0; t < grid.Length; t++)
            {
                var tier = grid[t];
                int coarser = t + 1 < grid.Length ? grid[t + 1].Step : int.MaxValue;
                for (int i = 0; i <= World.MapSize; i += tier.Step)
                {
                    if (i % coarser == 0 && coarser != int.MaxValue) continue;
                    var v = AddSprite("Grid", VillageArt.Pixel, -190 + t);
                    v.color = tier.Color;
                    v.transform.position = Origin + new Vector2(i * FieldWidth, World.MapSize / 2f);
                    tier.Vertical.Add(v);
                    var h = AddSprite("Grid", VillageArt.Pixel, -190 + t);
                    h.color = tier.Color;
                    h.transform.position = Origin + new Vector2(width / 2f, i);
                    tier.Horizontal.Add(h);
                }
            }
        }

        /// <summary>
        /// Shows the grid for the current zoom: finer tiers fade out as the view pulls back, and every line stays the
        /// same number of screen pixels thick whatever the zoom.
        /// </summary>
        /// <param name="zoom">The camera's orthographic size (half the fields visible top to bottom).</param>
        /// <param name="screenHeight">The screen's height in pixels.</param>
        public void ShowGrid(float zoom, int screenHeight)
        {
            float pixel = 2f * zoom / Mathf.Max(1, screenHeight); // one screen pixel, in scene units
            if (Mathf.Approximately(zoom, gridZoom) && Mathf.Approximately(pixel, gridPixelSize)) return;
            gridZoom = zoom;
            gridPixelSize = pixel;

            float width = World.MapSize * FieldWidth;
            foreach (var tier in grid)
            {
                float fade = tier.GoneAt == float.MaxValue ? 1f : 1f - Mathf.InverseLerp(tier.FullUntil, tier.GoneAt, zoom);
                bool visible = fade > 0.01f;
                var color = new Color(tier.Color.r, tier.Color.g, tier.Color.b, tier.Color.a * fade);
                float thickness = tier.Pixels * pixel;
                foreach (var v in tier.Vertical)
                {
                    v.enabled = visible;
                    if (!visible) continue;
                    v.color = color;
                    v.transform.localScale = new Vector3(thickness, World.MapSize, 1f);
                }
                foreach (var h in tier.Horizontal)
                {
                    h.enabled = visible;
                    if (!visible) continue;
                    h.color = color;
                    h.transform.localScale = new Vector3(width, thickness, 1f);
                }
            }
        }

        /// <summary>The village nearest a scene position, if one is within <paramref name="radius"/> fields.</summary>
        public static Village VillageNear(World world, Vector2 scenePosition, float radius)
        {
            Village best = null;
            float bestDistance = radius;
            var p = ToFields(scenePosition);
            // Only the villages round about (the world holds a couple of thousand).
            foreach (var v in world.VillagesNear(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), radius + 1.5f))
            {
                float d = Vector2.Distance(p, new Vector2(v.X + 0.5f, v.Y + 0.5f));
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = v;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- terrain picture

        static readonly Color Meadow = new Color(0.46f, 0.64f, 0.3f);
        static readonly Color Woods = new Color(0.22f, 0.42f, 0.2f);
        static readonly Color Rock = new Color(0.55f, 0.5f, 0.42f);
        static readonly Color Lake = new Color(0.25f, 0.45f, 0.72f);

        static Sprite MakeTerrainSprite(int seed)
        {
            int fields = World.MapSize, size = fields * PixelsPerField;
            var types = new TerrainType[fields, fields];
            for (int fx = 0; fx < fields; fx++)
                for (int fy = 0; fy < fields; fy++)
                    types[fx, fy] = Simulation.Terrain.At(seed, fx, fy); // not UnityEngine.Terrain

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int fx = x / PixelsPerField, fy = y / PixelsPerField;
                    int lx = x % PixelsPerField, ly = y % PixelsPerField;
                    float detail = Mathf.PerlinNoise(x * 0.09f + seed % 100, y * 0.09f);
                    Color c;
                    switch (types[fx, fy])
                    {
                        case TerrainType.Water:
                            c = Lake * (0.9f + 0.15f * detail);
                            if ((y + (int)(detail * 6)) % 5 == 0 && lx % 4 != 0) c = Color.Lerp(c, Color.white, 0.18f); // ripples
                            break;
                        case TerrainType.Forest:
                            c = Woods * (0.85f + 0.3f * detail);
                            // A tree clump in each forest field.
                            float half = PixelsPerField / 2f;
                            float tree = Vector2.Distance(new Vector2(lx, ly), new Vector2(half - 0.5f + (fx * 7 + fy) % 3 - 1, half));
                            if (tree < PixelsPerField * 0.33f) c = Woods * 0.7f;
                            break;
                        case TerrainType.Hills:
                            c = Rock * (0.8f + 0.4f * detail);
                            break;
                        default:
                            c = Meadow * (0.9f + 0.2f * detail);
                            break;
                    }
                    // A dark border round the world. (The grid lines are drawn separately, so they can change with
                    // the zoom: see ShowGrid.)
                    if (x < 2 || y < 2 || x >= size - 2 || y >= size - 2) c = new Color(0.15f, 0.12f, 0.08f);
                    c.a = 1f;
                    px[y * size + x] = c;
                }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "WorldTerrain",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.zero, PixelsPerField, 0, SpriteMeshType.FullRect);
        }

        void OnDestroy()
        {
            // The terrain texture is made per world, not cached, so free it with the view.
            if (terrain != null && terrain.sprite != null)
            {
                Destroy(terrain.sprite.texture);
                Destroy(terrain.sprite);
            }
        }
    }
}
