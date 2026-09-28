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

        // Tribal Wars' map colours: your villages yellow, barbarians grey, everyone else in their own colours.
        public static readonly Color PlayerColor = new Color(1f, 0.86f, 0.12f);
        public static readonly Color BarbarianColor = new Color(0.62f, 0.62f, 0.62f);

        /// <summary>Each rival lord's colour (by <see cref="Player.ColorIndex"/>): none of them yellow or grey.</summary>
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

        /// <summary>The colour a village's marker has: blue for the player's, beige for barbarians, the lord's own for rivals.</summary>
        public static Color OwnerColor(World world, Village v)
        {
            if (v.IsBarbarian) return BarbarianColor;
            var owner = world.FindPlayer(v.OwnerId);
            if (owner == null) return BarbarianColor;
            return owner.IsHuman ? PlayerColor : RivalColors[owner.ColorIndex % RivalColors.Length];
        }

        SpriteRenderer terrain, selection, homeGlow;
        readonly Dictionary<int, SpriteRenderer> markers = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, int> markerPoints = new Dictionary<int, int>();
        float refreshTimer;

        public static MapView Create(World world)
        {
            var view = new GameObject("Map View").AddComponent<MapView>();
            view.Build(world);
            return view;
        }

        public static Vector2 FieldCentre(int x, int y) => Origin + new Vector2(x + 0.5f, y + 0.5f);

        /// <summary>The map field under a scene position (may be off the map).</summary>
        public static Vector2Int FieldAt(Vector2 scenePosition)
        {
            var p = scenePosition - Origin;
            return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
        }

        void Build(World world)
        {
            terrain = AddSprite("Terrain", MakeTerrainSprite(world.Settings.Seed), -200);
            terrain.transform.position = Origin;

            homeGlow = AddSprite("Home", VillageArt.Glow, -150);
            homeGlow.color = new Color(1f, 1f, 1f, 0.55f);
            homeGlow.transform.localScale = Vector3.one * 2.6f;

            selection = AddSprite("Selection", VillageArt.Glow, -140);
            selection.color = new Color(1f, 1f, 1f, 0.9f);
            selection.transform.localScale = Vector3.one * 2f;
            selection.enabled = false;

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
            if (!force && refreshTimer > 0f) return;
            refreshTimer = 0.5f;

            var human = world.HumanPlayer;
            foreach (var v in world.Villages)
            {
                if (!markers.TryGetValue(v.Id, out var marker))
                {
                    // In front of the terrain and glows; lower markers overlap the ones above them.
                    marker = AddSprite("Village", VillageArt.MapVillage(0), 10 + World.MapSize - v.Y);
                    marker.transform.position = FieldCentre(v.X, v.Y);
                    marker.transform.localScale = Vector3.one * 0.96f; // a field each, so neighbours don't overlap
                    markers[v.Id] = marker;
                    markerPoints[v.Id] = -1;
                }
                bool mine = human != null && v.OwnerId == human.Id;
                marker.color = OwnerColor(world, v);
                int points = v.Points;
                if (markerPoints[v.Id] != points)
                {
                    markerPoints[v.Id] = points;
                    marker.sprite = VillageArt.MapVillage(TierOf(points));
                }
                if (mine && v == world.PlayerVillage) homeGlow.transform.position = FieldCentre(v.X, v.Y);
            }
        }

        public void Select(Village v)
        {
            selection.enabled = v != null;
            if (v != null) selection.transform.position = FieldCentre(v.X, v.Y);
        }

        /// <summary>The village nearest a scene position, if one is within <paramref name="radius"/> fields.</summary>
        public static Village VillageNear(World world, Vector2 scenePosition, float radius)
        {
            Village best = null;
            float bestDistance = radius;
            var p = scenePosition - Origin;
            foreach (var v in world.Villages)
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
                    // Faint lines every 10 fields, and a dark border round the world.
                    if (x % (PixelsPerField * 10) == 0 || y % (PixelsPerField * 10) == 0) c *= 0.82f;
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
