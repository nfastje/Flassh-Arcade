using MedievalWorldConquest.Simulation;
using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The illustrated village: a clearing among trees, with each building standing on its own plot. The town
    /// buildings stand inside the clearing; the four resource buildings work the land outside it, one per corner
    /// (timber in the forest, iron in the hills, clay in the pits, and the farm's fields). Buildings change their
    /// look as they level up, and show scaffolding while being upgraded. Once a wall is built it rings the clearing
    /// (wooden stakes, then iron-banded stakes, then stone), with its gate at the front.
    /// </summary>
    public class VillageView : MonoBehaviour
    {
        const float ClearingWidth = 8.6f, ClearingHeight = 5f;

        // Draw order: the ground is far behind everything; everything else is ordered by height on screen
        // (DepthOrder), which spans roughly -1000 to 1000.
        const int GrassOrder = -20000, PatchOrder = -19500, ClearingOrder = -19000, SelectionOrder = -18990;
        const int StakeCount = 100;
        /// <summary>How far ring pieces tuck behind the gate's edges, so there's no visible gap either side of it.</summary>
        const float GateOverlap = 0.12f;

        /// <summary>
        /// Where each building stands (the middle of its base), indexed by <see cref="BuildingType"/>. Inside the
        /// clearing: the Town Hall in the middle, barracks and warehouse behind, stable and workshop in front, and the
        /// gate. Outside: a resource building in each corner.
        /// </summary>
        static readonly Vector2[] Plots =
        {
            new Vector2(0f, 0.15f),     // Town Hall
            new Vector2(5.3f, -3.3f),   // Timber Camp: bottom-right, in the forest
            new Vector2(-5.3f, -3.3f),  // Clay Pit: bottom-left
            new Vector2(-5.3f, 1.9f),   // Iron Mine: top-left, in the hills
            new Vector2(5.3f, 1.9f),    // Farm: top-right, among its fields
            new Vector2(2.3f, 0.35f),   // Warehouse
            new Vector2(-2.3f, 0.35f),  // Barracks
            new Vector2(1.9f, -1.5f),   // Stable (clear of the gate's towers)
            new Vector2(-1.9f, -1.5f),  // Workshop
            new Vector2(0f, -2.4f),     // Wall: its gate, in the gap at the front of the ring
        };

        /// <summary>The ground each corner's resource building works, tinted over the grass around it.</summary>
        static readonly (BuildingType building, Color tint, Vector2 size)[] CornerGrounds =
        {
            (BuildingType.TimberCamp, new Color(0.3f, 0.36f, 0.2f), new Vector2(4.2f, 3.2f)),  // forest floor
            (BuildingType.IronMine, new Color(0.62f, 0.6f, 0.56f), new Vector2(4.2f, 3.2f)),   // rocky ground
            (BuildingType.ClayPit, new Color(0.78f, 0.52f, 0.32f), new Vector2(4.2f, 2.8f)),   // clay earth
            (BuildingType.Farm, new Color(0.86f, 0.78f, 0.42f), new Vector2(4.4f, 2.8f)),      // golden field
        };
        const float PlotHalfWidth = 1.1f;

        /// <summary>How far above its base a click still counts as a building. The gate is short, so it doesn't steal clicks from the buildings behind it.</summary>
        static float PlotHeight(int index) => index == (int)BuildingType.Wall ? 0.9f : 1.9f;

        SpriteRenderer grass;
        Camera cam;
        SpriteRenderer[] buildings, scaffolds, ring;
        int[] shownLevels;
        int shownRingTier = int.MinValue;

        /// <summary>Where the given building stands, for placing its level badge.</summary>
        public static Vector2 PlotOf(BuildingType type) => Plots[(int)type];

        /// <summary>The building whose plot contains a world position, if any.</summary>
        public static BuildingType? BuildingAt(Vector2 world)
        {
            // Check front (lower) plots first, since they're drawn on top.
            BuildingType? hit = null;
            float bestY = float.MaxValue;
            for (int i = 0; i < Plots.Length; i++)
            {
                var p = Plots[i];
                bool inside = Mathf.Abs(world.x - p.x) <= PlotHalfWidth && world.y >= p.y - 0.2f && world.y <= p.y + PlotHeight(i);
                if (inside && p.y < bestY)
                {
                    bestY = p.y;
                    hit = (BuildingType)i;
                }
            }
            return hit;
        }

        public static VillageView Create(Camera camera, int seed)
        {
            var view = new GameObject("Village View").AddComponent<VillageView>();
            view.cam = camera;
            view.Build(seed);
            return view;
        }

        void Build(int seed)
        {
            grass = AddSprite("Grass", VillageArt.Grass, GrassOrder);
            grass.drawMode = SpriteDrawMode.Tiled;

            var clearing = AddSprite("Clearing", VillageArt.Clearing, ClearingOrder);
            clearing.transform.localScale = new Vector3(ClearingWidth, ClearingHeight, 1f);

            foreach (var (building, tint, size) in CornerGrounds)
            {
                var ground = AddSprite("Corner Ground", VillageArt.Patch, PatchOrder);
                ground.color = tint;
                ground.transform.localScale = new Vector3(size.x, size.y, 1f);
                ground.transform.position = Plots[(int)building] + new Vector2(0f, 0.7f);
            }

            // The wall's ring, around the clearing's edge (hidden until a wall is built). Lower pieces are nearer the
            // viewer, so they draw on top. Pieces where the gate stands are hidden in ShowRing.
            ring = new SpriteRenderer[StakeCount];
            for (int i = 0; i < StakeCount; i++)
            {
                float angle = i / (float)StakeCount * Mathf.PI * 2f;
                var pos = new Vector2(Mathf.Cos(angle) * ClearingWidth * 0.47f, Mathf.Sin(angle) * ClearingHeight * 0.47f);
                var piece = AddSprite("Wall", VillageArt.Stake, DepthOrder(pos.y));
                piece.transform.position = pos;
                piece.enabled = false;
                ring[i] = piece;
            }

            // Woods on the outskirts of the view, never covering a building. Placed the same way every time for a
            // given world.
            var rng = new System.Random(seed);
            for (int placed = 0, attempts = 0; placed < 45 && attempts < 3000; attempts++)
            {
                var pos = new Vector2((float)(rng.NextDouble() * 26 - 13), (float)(rng.NextDouble() * 14 - 7));
                float scale = 0.8f + (float)rng.NextDouble() * 0.5f;
                if (!OnTheOutskirts(pos) || InACorner(pos) || CoversABuilding(pos, scale)) continue;
                var tree = AddSprite("Tree", VillageArt.Tree, DepthOrder(pos.y));
                tree.transform.position = pos;
                tree.transform.localScale = Vector3.one * scale;
                placed++;
            }

            buildings = new SpriteRenderer[Buildings.Count];
            scaffolds = new SpriteRenderer[Buildings.Count];
            shownLevels = new int[Buildings.Count];
            for (int i = 0; i < Buildings.Count; i++)
            {
                var plot = Plots[i];
                buildings[i] = AddSprite(Buildings.Get((BuildingType)i).Name, BuildingArt.Signpost, DepthOrder(plot.y));
                buildings[i].transform.position = plot;
                scaffolds[i] = AddSprite("Scaffolding", BuildingArt.Scaffolding, DepthOrder(plot.y) + 1);
                scaffolds[i].transform.position = plot;
                scaffolds[i].enabled = false;
                shownLevels[i] = -1;
            }
        }

        /// <summary>Shows each building at its current level, with scaffolding on the one being upgraded.</summary>
        public void ShowVillage(Village village)
        {
            for (int i = 0; i < Buildings.Count; i++)
            {
                var type = (BuildingType)i;
                int level = village.Level(type);
                if (level != shownLevels[i])
                {
                    buildings[i].sprite = BuildingArt.For(type, level);
                    shownLevels[i] = level;
                }
                scaffolds[i].enabled = village.Queue.Count > 0 && village.Queue[0].Type == type;
            }
            ShowRing(village.Level(BuildingType.Wall));
        }

        /// <summary>No ring without a wall; then wooden stakes, iron-banded stakes, and stone as the wall levels up.</summary>
        void ShowRing(int wallLevel)
        {
            int tier = wallLevel <= 0 ? -1 : BuildingArt.TierFor(wallLevel);
            if (tier == shownRingTier) return;
            shownRingTier = tier;
            var sprite = tier == 2 ? VillageArt.StoneWall : tier == 1 ? VillageArt.ReinforcedStake : VillageArt.Stake;
            // Leave exactly enough room at the front for this tier's gate, with the neighbours tucked behind its edges.
            var gate = Plots[(int)BuildingType.Wall];
            float gap = tier >= 0 ? BuildingArt.GateHalfWidth(tier) - GateOverlap : 0f;
            foreach (var piece in ring)
            {
                Vector2 p = piece.transform.position;
                bool whereTheGateIs = p.y < 0f && Mathf.Abs(p.x - gate.x) < gap;
                piece.enabled = tier >= 0 && !whereTheGateIs;
                piece.sprite = sprite;
            }
        }

        /// <summary>Marks one building (e.g. the one selected in the building list) with a glow on the ground, or none.</summary>
        public void Highlight(BuildingType? type)
        {
            if (selectionGlow == null)
            {
                selectionGlow = AddSprite("Selection", VillageArt.Glow, SelectionOrder);
                selectionGlow.color = new Color(1f, 0.85f, 0.35f, 0.8f);
                selectionGlow.transform.localScale = new Vector3(PlotHalfWidth * 2.2f, 0.8f, 1f);
            }
            selectionGlow.enabled = type.HasValue;
            if (type.HasValue) selectionGlow.transform.position = Plots[(int)type.Value] + new Vector2(0f, 0.05f);
        }

        SpriteRenderer selectionGlow;

        /// <summary>
        /// Whether a tree's base is out near the edges of the village view (or beyond), rather than among the
        /// buildings. The middle band spans the clearing and the corner buildings.
        /// </summary>
        static bool OnTheOutskirts(Vector2 pos) => Mathf.Abs(pos.x) > 6.4f || pos.y > 4.1f || pos.y < -4.5f;

        /// <summary>Whether a tree at this spot (its base) and scale would overlap any building's plot.</summary>
        static bool CoversABuilding(Vector2 pos, float scale)
        {
            // The tree sprite is 1.4 units wide and about 1.75 tall at scale 1, standing on its base.
            var tree = new Rect(pos.x - 0.7f * scale, pos.y, 1.4f * scale, 1.75f * scale);
            foreach (var p in Plots)
            {
                var building = new Rect(p.x - 1.3f, p.y - 0.4f, 2.6f, 2.8f); // a building's sprite, plus its badge
                if (tree.Overlaps(building)) return true;
            }
            return false;
        }

        /// <summary>Whether a spot is on one of the corner resource buildings' patches of ground.</summary>
        static bool InACorner(Vector2 pos)
        {
            foreach (var (building, _, size) in CornerGrounds)
            {
                var centre = Plots[(int)building] + new Vector2(0f, 0.7f);
                if (Mathf.Abs(pos.x - centre.x) < size.x * 0.55f && Mathf.Abs(pos.y - centre.y) < size.y * 0.6f) return true;
            }
            return false;
        }

        /// <summary>Things lower on screen are closer to the viewer and draw in front.</summary>
        static int DepthOrder(float y) => Mathf.RoundToInt(-y * 100f);

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

        void LateUpdate()
        {
            if (cam == null) return;
            // Keep the grass covering the whole view at any aspect ratio.
            float h = cam.orthographicSize * 2f + 2f;
            grass.size = new Vector2(h * cam.aspect + 2f, h);
            grass.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
        }
    }
}
