using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>
    /// Draws each building's sprite at runtime. Every building has three looks (levels 1-9, 10-19 and 20+) so the
    /// village visibly grows, plus an empty-plot signpost for level 0 and a scaffolding overlay for construction.
    /// Sprites are 128 x 128 pixels, drawn on a canvas whose ground line is <see cref="Ground"/>.
    /// </summary>
    static class BuildingArt
    {
        public const int Size = 128;
        const int Ground = 8;
        public const float PixelsPerUnit = 56f; // 128 px = about 2.3 units
        static readonly Vector2 Pivot = new Vector2(0.5f, Ground / (float)Size);

        static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
        static Sprite signpost, scaffolding;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            cache.Clear();
            signpost = scaffolding = null;
        }

        /// <summary>0 for levels 1-9, 1 for 10-19, 2 for 20 and up.</summary>
        public static int TierFor(int level) => level >= 20 ? 2 : level >= 10 ? 1 : 0;

        public static Sprite For(BuildingType type, int level)
        {
            if (level <= 0) return Signpost;
            int tier = TierFor(level);
            int key = (int)type * 10 + tier;
            if (!cache.TryGetValue(key, out var sprite) || sprite == null)
            {
                var c = new Canvas();
                switch (type)
                {
                    case BuildingType.TownHall: DrawTownHall(c, tier); break;
                    case BuildingType.TimberCamp: DrawTimberCamp(c, tier); break;
                    case BuildingType.ClayPit: DrawClayPit(c, tier); break;
                    case BuildingType.IronMine: DrawIronMine(c, tier); break;
                    case BuildingType.Farm: DrawFarm(c, tier); break;
                    case BuildingType.Warehouse: DrawWarehouse(c, tier); break;
                    case BuildingType.Barracks: DrawBarracks(c, tier); break;
                    case BuildingType.Stable: DrawStable(c, tier); break;
                    case BuildingType.Workshop: DrawWorkshop(c, tier); break;
                    case BuildingType.Wall: DrawGate(c, tier); break;
                }
                cache[key] = sprite = c.ToSprite($"{type}{tier}");
            }
            return sprite;
        }

        public static Sprite Signpost => signpost != null ? signpost : signpost = MakeSignpost();
        public static Sprite Scaffolding => scaffolding != null ? scaffolding : scaffolding = MakeScaffolding();

        // ---------------------------------------------------------------- palette

        static readonly Color Wood = new Color(0.58f, 0.4f, 0.23f);
        static readonly Color DarkWood = new Color(0.36f, 0.24f, 0.13f);
        static readonly Color Thatch = new Color(0.82f, 0.66f, 0.32f);
        static readonly Color RoofRed = new Color(0.72f, 0.26f, 0.2f);
        static readonly Color RoofBlue = new Color(0.25f, 0.35f, 0.62f);
        static readonly Color Stone = new Color(0.62f, 0.62f, 0.64f);
        static readonly Color DarkStone = new Color(0.4f, 0.4f, 0.43f);
        static readonly Color BarnRed = new Color(0.68f, 0.2f, 0.15f);
        static readonly Color Clay = new Color(0.8f, 0.45f, 0.25f);
        static readonly Color Window = new Color(0.95f, 0.82f, 0.4f);
        static readonly Color Doorway = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color Straw = new Color(0.9f, 0.78f, 0.35f);
        static readonly Color Crop = new Color(0.45f, 0.62f, 0.22f);

        // ---------------------------------------------------------------- buildings

        static void DrawTownHall(Canvas c, int tier)
        {
            if (tier == 0)
            {
                c.Planks(34, Ground, 94, 44, Wood);
                c.Rect(58, Ground, 70, 26, Doorway);
                c.Rect(40, 28, 48, 36, Window);
                c.Rect(80, 28, 88, 36, Window);
                c.Rect(82, 60, 88, 76, DarkStone); // chimney
                c.Roof(28, 100, 44, 74, Thatch, true);
            }
            else if (tier == 1)
            {
                c.Planks(26, Ground, 100, 50, Wood);
                c.Bricks(26, Ground, 100, 18, Stone);
                c.Rect(56, Ground, 70, 30, Doorway);
                for (int x = 32; x < 96; x += 16) c.Rect(x, 34, x + 7, 42, Window);
                c.Roof(20, 106, 50, 82, RoofRed, false);
                c.Bricks(98, Ground, 116, 72, Stone); // side tower
                c.Rect(104, 50, 110, 58, Window);
                c.Roof(94, 120, 72, 96, RoofRed, false);
            }
            else
            {
                c.Bricks(24, Ground, 104, 60, Stone);
                c.Crenellations(24, 104, 60, Stone);
                c.Arch(56, Ground, 72, 34, Doorway);
                for (int x = 32; x < 100; x += 18) c.Rect(x, 42, x + 6, 52, Window);
                foreach (int tx in new[] { 8, 102 })
                {
                    c.Bricks(tx, Ground, tx + 20, 84, Stone);
                    c.Rect(tx + 7, 60, tx + 13, 70, Window);
                    c.Roof(tx - 4, tx + 24, 84, 108, RoofBlue, false);
                }
                c.Rect(63, 66, 65, 100, DarkWood);          // flag pole
                c.Triangle(65, 100, 65, 88, 82, 94, RoofRed); // banner
            }
        }

        static void DrawFarm(Canvas c, int tier)
        {
            // Crop field rows on the left.
            int fieldRight = tier == 0 ? 54 : 48;
            for (int y = Ground; y < Ground + 22 + tier * 4; y += 4)
                c.Rect(4, y, fieldRight, y + 2, (y / 4) % 2 == 0 ? Crop : Straw);

            int left = tier == 0 ? 60 : 52, right = tier == 0 ? 100 : 106, top = tier == 0 ? 36 : 44;
            c.Planks(left, Ground, right, top, BarnRed);
            int doorL = (left + right) / 2 - 8;
            c.Rect(doorL, Ground, doorL + 16, top - 12, new Color(0.9f, 0.85f, 0.75f));
            c.Line(doorL, Ground, doorL + 16, top - 12, BarnRed);
            c.Line(doorL, top - 12, doorL + 16, Ground, BarnRed);
            c.Roof(left - 5, right + 5, top, top + 22, DarkWood, false);

            if (tier >= 1)
            {
                c.Ellipse(20, Ground + 8, 12, 9, Straw); // haystacks
                c.Ellipse(40, Ground + 6, 9, 7, Straw);
            }
            if (tier >= 2)
            {
                c.Rect(108, Ground, 122, 70, new Color(0.75f, 0.75f, 0.78f)); // silo
                c.Ellipse(115, 70, 7, 7, RoofRed);
            }
        }

        static void DrawWarehouse(Canvas c, int tier)
        {
            int left = 22 - tier * 4, right = 106 + tier * 4, top = 34 + tier * 10;
            c.Planks(left, Ground, right, top, Wood);
            if (tier >= 1) c.Bricks(left, Ground, right, Ground + 12, Stone);
            if (tier >= 2)
            {
                c.Planks(left + 8, top, right - 8, top + 16, DarkWood); // upper storey
                c.Rect(left + 20, top + 4, left + 28, top + 12, Window);
                c.Rect(right - 28, top + 4, right - 20, top + 12, Window);
                top += 16;
            }
            c.Rect(46, Ground, 60, top - 14 - tier * 16, Doorway);
            if (tier >= 1) c.Rect(70, Ground, 84, top - 14 - tier * 16, Doorway);
            c.Roof(left - 5, right + 5, top, top + 14 + tier * 3, DarkWood, false);

            // Crates and barrels out front.
            for (int i = 0; i <= tier; i++) c.Crate(10 + i * 14, Ground, 12);
            if (tier >= 1) c.Ellipse(110, Ground + 6, 6, 7, DarkWood);
        }

        static void DrawTimberCamp(Canvas c, int tier)
        {
            // A triangular stack of logs, seen end-on.
            int rows = 3 + tier;
            for (int row = 0; row < rows; row++)
                for (int i = 0; i < rows - row; i++)
                    c.LogEnd(18 + i * 13 + row * 6, Ground + 6 + row * 11, 6);

            c.Rect(92, Ground, 104, Ground + 10, DarkWood);      // stump
            c.Ellipse(98, Ground + 10, 7, 3, Wood);
            c.Line(96, Ground + 12, 104, Ground + 28, DarkWood); // axe handle
            c.Rect(100, Ground + 24, 108, Ground + 30, DarkStone);

            if (tier >= 1)
            {
                // Saw shed: a roof on posts.
                c.Rect(76, Ground, 79, 44, DarkWood);
                c.Rect(118, Ground, 121, 44, DarkWood);
                c.Roof(70, 126, 44, 60, Thatch, true);
            }
            if (tier >= 2)
            {
                c.Planks(4, Ground, 20, 40, Wood);
                c.Roof(0, 24, 40, 54, Thatch, true);
            }
        }

        static void DrawClayPit(Canvas c, int tier)
        {
            c.Ellipse(58, Ground + 10, 42, 12, new Color(0.4f, 0.26f, 0.15f)); // the pit
            c.Ellipse(58, Ground + 9, 34, 8, Clay);
            c.Ellipse(14, Ground + 4, 12, 9, Clay);                            // spoil mounds
            c.Ellipse(104, Ground + 4, 10, 8, Clay);
            c.Line(96, Ground + 4, 110, Ground + 30, DarkWood);               // shovel
            c.Rect(94, Ground + 2, 100, Ground + 8, DarkStone);

            // Brick stacks.
            for (int i = 0; i <= tier; i++)
                for (int y = 0; y < 3; y++)
                    c.Rect(8 + i * 18, Ground + 22 + y * 5, 22 + i * 18, Ground + 26 + y * 5, new Color(0.7f, 0.32f, 0.2f));

            if (tier >= 1) c.Kiln(96, Ground + 20, 16);
            if (tier >= 2) c.Kiln(66, Ground + 22, 13);
        }

        static void DrawIronMine(Canvas c, int tier)
        {
            int rx = 44 + tier * 6, ry = 40 + tier * 8;
            c.Hill(64, Ground, rx, ry, DarkStone, Stone);
            c.Arch(54, Ground, 74, 30 + tier * 4, Doorway);
            c.Rect(52, Ground, 55, 32 + tier * 4, Wood); // timber frame
            c.Rect(73, Ground, 76, 32 + tier * 4, Wood);
            c.Rect(52, 30 + tier * 4, 76, 33 + tier * 4, Wood);

            // Ore cart.
            c.Rect(84, Ground + 4, 102, Ground + 14, DarkWood);
            c.Ellipse(93, Ground + 14, 7, 3, new Color(0.3f, 0.3f, 0.35f));
            c.Ellipse(88, Ground + 3, 3, 3, Doorway);
            c.Ellipse(98, Ground + 3, 3, 3, Doorway);

            if (tier >= 1) c.Rect(20, Ground, 108, Ground + 2, DarkWood); // rails
            if (tier >= 2)
            {
                c.Bricks(104, Ground, 118, 78, Stone); // forge chimney
                c.Ellipse(111, 86, 6, 5, new Color(0.7f, 0.7f, 0.72f, 0.7f));
                c.Ellipse(115, 94, 5, 4, new Color(0.75f, 0.75f, 0.78f, 0.5f));
            }
        }

        static readonly Color ShieldRed = new Color(0.75f, 0.18f, 0.15f);
        static readonly Color ShieldBlue = new Color(0.2f, 0.32f, 0.65f);
        static readonly Color HorseBrown = new Color(0.5f, 0.32f, 0.18f);

        static void DrawBarracks(Canvas c, int tier)
        {
            if (tier < 2)
            {
                int left = 28 - tier * 6, right = 100 + tier * 4, top = 40 + tier * 6;
                c.Planks(left, Ground, right, top, Wood);
                if (tier >= 1) c.Bricks(left, Ground, right, Ground + 12, Stone);
                c.Rect(56, Ground, 70, top - 14, Doorway);
                c.Shield(left + 12, top - 12, ShieldRed);
                c.Shield(right - 12, top - 12, ShieldBlue);
                c.Roof(left - 5, right + 5, top, top + 22, tier == 0 ? Thatch : RoofRed, tier == 0);
            }
            else
            {
                c.Bricks(14, Ground, 114, 54, Stone);
                c.Crenellations(14, 114, 54, Stone);
                c.Arch(54, Ground, 74, 36, Doorway);
                c.Shield(32, 38, ShieldRed);
                c.Shield(96, 38, ShieldBlue);
                c.Rect(63, 60, 65, 94, DarkWood);
                c.Triangle(65, 94, 65, 82, 80, 88, ShieldRed);
            }

            // A rack of spears leaning at the left.
            for (int i = 0; i < 3 + tier; i++) c.Line(6 + i * 5, Ground, 10 + i * 5, Ground + 34, DarkWood);
            c.Rect(4, Ground + 14, 26, Ground + 16, Wood);

            if (tier >= 1)
            {
                // Training dummy: a post with a crossbar and a straw sack.
                c.Rect(115, Ground, 118, Ground + 30, DarkWood);
                c.Rect(108, Ground + 22, 125, Ground + 25, DarkWood);
                c.Ellipse(116.5f, Ground + 32, 5, 6, Straw);
            }
        }

        static void DrawStable(Canvas c, int tier)
        {
            int left = 44 - tier * 6, right = 108 + tier * 4, top = 36 + tier * 6;
            if (tier < 2) c.Planks(left, Ground, right, top, Wood);
            else c.Bricks(left, Ground, right, top, Stone);
            // Stall half-doors.
            for (int x = left + 6; x + 12 <= right - 4; x += 18)
            {
                c.Rect(x, Ground + 12, x + 12, top - 8, Doorway);
                c.Planks(x, Ground, x + 12, Ground + 12, DarkWood);
            }
            c.Roof(left - 5, right + 5, top, top + 20, tier == 0 ? Thatch : DarkWood, tier == 0);

            // A fenced paddock on the left, with a hay bale.
            for (int x = 4; x <= left - 6; x += 12) c.Rect(x, Ground, x + 3, Ground + 20, Wood);
            c.Rect(4, Ground + 10, left - 4, Ground + 12, Wood);
            c.Rect(4, Ground + 17, left - 4, Ground + 19, Wood);
            c.Ellipse(right - 4, Ground + 5, 8, 6, Straw);

            if (tier >= 1) c.Horse(20, Ground);
            if (tier >= 2)
            {
                c.Rect(62, top + 20, 64, top + 32, DarkWood); // weathervane
                c.Triangle(64, top + 32, 64, top + 26, 74, top + 29, RoofRed);
            }
        }

        static void DrawWorkshop(Canvas c, int tier)
        {
            // An open-sided shed: a roof on posts, with a workbench inside.
            foreach (int x in new[] { 30, 64, 98 }) c.Rect(x, Ground, x + 4, 48, DarkWood);
            c.Roof(24, 108, 48, 70, tier == 0 ? Thatch : DarkWood, tier == 0);
            c.Rect(40, Ground + 12, 90, Ground + 16, Wood);
            c.Rect(42, Ground, 45, Ground + 12, DarkWood);
            c.Rect(85, Ground, 88, Ground + 12, DarkWood);

            c.Wheel(14, Ground + 12, 11); // a spare wheel leaning at the side
            for (int i = 0; i < 3; i++) c.Rect(104, Ground + i * 5, 124, Ground + i * 5 + 4, Wood); // planks

            if (tier >= 1)
            {
                // A ram: a heavy log on a wheeled frame.
                c.Rect(48, Ground + 18, 92, Ground + 26, Wood);
                c.Ellipse(92, Ground + 22, 4, 5, DarkStone);
                c.Wheel(56, Ground + 6, 6);
                c.Wheel(84, Ground + 6, 6);
            }
            if (tier >= 2)
            {
                // A catapult beside the shed: a frame and a throwing arm.
                c.Triangle(100, Ground + 20, 124, Ground + 20, 112, Ground + 46, DarkWood);
                c.Line(104, Ground + 30, 124, Ground + 62, Wood);
                c.Ellipse(124, Ground + 64, 4, 4, DarkStone);
            }
        }

        /// <summary>
        /// Half the width of the gate sprite for a wall tier, in world units: the ring leaves just this much room
        /// for it. Tier 0 spans pixels 28-104 (with its palisade wings); the towered gates span 18-112.
        /// </summary>
        public static float GateHalfWidth(int tier) => (tier == 0 ? (104 - 28) : (112 - 18)) / 2f / PixelsPerUnit;

        /// <summary>The wall's gate, which sits in the gap at the front of the palisade ring.</summary>
        static void DrawGate(Canvas c, int tier)
        {
            if (tier == 2)
            {
                // Stone gatehouse with towers either side.
                foreach (int tx in new[] { 18, 92 })
                {
                    c.Bricks(tx, Ground, tx + 18, 82, Stone);
                    c.Crenellations(tx, tx + 18, 82, Stone);
                    c.Rect(tx + 7, 58, tx + 11, 66, Doorway);
                }
                c.Bricks(34, Ground, 94, 66, Stone);
                c.Crenellations(34, 94, 66, Stone);
                c.Arch(48, Ground, 80, 46, Doorway);
                c.Planks(51, Ground, 77, 38, DarkWood);
                return;
            }

            if (tier == 1)
            {
                // Wooden watchtowers either side of the gate.
                foreach (int tx in new[] { 20, 88 })
                {
                    c.Rect(tx, Ground, tx + 3, 70, DarkWood);
                    c.Rect(tx + 17, Ground, tx + 20, 70, DarkWood);
                    c.Planks(tx - 2, 62, tx + 22, 72, Wood);
                    c.Roof(tx - 4, tx + 24, 72, 88, Thatch, true);
                }
            }

            if (tier == 0)
            {
                // Short palisade wings either side, so the gate joins up with the ring of stakes.
                for (int x = 28; x < 44; x += 5) c.Rect(x, Ground, x + 4, 44, Wood);
                for (int x = 88; x < 104; x += 5) c.Rect(x, Ground, x + 4, 44, Wood);
            }

            // Two big gateposts and a pair of plank doors under a lintel.
            c.Planks(50, Ground, 78, 48, Wood);
            c.Rect(63, Ground, 65, 48, DarkWood);
            c.Rect(50, 22, 78, 25, DarkWood);
            foreach (int px in new[] { 44, 78 })
            {
                c.Rect(px, Ground, px + 6, 58, DarkWood);
                c.Triangle(px, 58, px + 6, 58, px + 3, 64, DarkWood);
            }
            c.Rect(40, 48, 88, 53, DarkWood);
        }

        static Sprite MakeSignpost()
        {
            var c = new Canvas();
            c.Rect(62, Ground, 66, 40, DarkWood);
            c.Planks(46, 32, 82, 46, Wood);
            c.Rect(52, 38, 76, 40, DarkWood); // "writing"
            // Corner stakes marking out the plot.
            foreach (int x in new[] { 22, 104 }) c.Rect(x, Ground, x + 3, Ground + 12, Wood);
            return c.ToSprite("Signpost");
        }

        static Sprite MakeScaffolding()
        {
            var c = new Canvas(outline: false);
            var pole = new Color(0.62f, 0.46f, 0.26f);
            foreach (int x in new[] { 24, 52, 78, 104 }) c.Rect(x, Ground, x + 3, 96, pole);
            foreach (int y in new[] { 30, 56, 82 }) c.Rect(20, y, 110, y + 4, pole);
            c.Line(24, 30, 52, 56, pole);
            c.Line(78, 56, 104, 82, pole);
            return c.ToSprite("Scaffolding");
        }

        // ---------------------------------------------------------------- a small pixel painter

        class Canvas
        {
            readonly Color[] px = new Color[Size * Size];
            readonly bool outline;

            public Canvas(bool outline = true) => this.outline = outline;

            /// <summary>Scales a colour's brightness but not its transparency (Color * float would scale both).</summary>
            static Color Shade(Color c, float s) => new Color(c.r * s, c.g * s, c.b * s, c.a);

            void Set(int x, int y, Color color)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size) return;
                if (color.a >= 1f) px[y * Size + x] = color;
                else px[y * Size + x] = Color.Lerp(px[y * Size + x], new Color(color.r, color.g, color.b, 1f), color.a);
            }

            public void Rect(int x0, int y0, int x1, int y1, Color color)
            {
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++) Set(x, y, color);
            }

            /// <summary>A wall of vertical planks, lit slightly towards the top.</summary>
            public void Planks(int x0, int y0, int x1, int y1, Color color)
            {
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        float shade = Mathf.Lerp(0.88f, 1.05f, (y - y0) / (float)Mathf.Max(1, y1 - y0));
                        if ((x - x0) % 6 == 0) shade *= 0.78f;
                        Set(x, y, Shade(color, shade));
                    }
            }

            /// <summary>A wall of staggered stone blocks.</summary>
            public void Bricks(int x0, int y0, int x1, int y1, Color color)
            {
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int row = (y - y0) / 6;
                        bool mortar = (y - y0) % 6 == 0 || (x - x0 + (row % 2) * 5) % 10 == 0;
                        float shade = mortar ? 0.72f : 0.95f + 0.08f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f);
                        Set(x, y, Shade(color, shade));
                    }
            }

            /// <summary>A gable roof overhanging a wall from x0 to x1, from its eaves at <paramref name="y0"/> to its ridge.</summary>
            public void Roof(int x0, int x1, int y0, int ridge, Color color, bool thatched)
            {
                float mid = (x0 + x1) / 2f, half = (x1 - x0) / 2f;
                for (int y = y0; y < ridge; y++)
                {
                    float t = (y - y0) / (float)(ridge - y0);
                    float w = half * (1f - t);
                    for (int x = Mathf.RoundToInt(mid - w); x < Mathf.RoundToInt(mid + w); x++)
                    {
                        float shade = x < mid ? 1f : 0.82f; // lit from the left
                        if (thatched) shade *= 0.9f + 0.12f * Mathf.PerlinNoise(x * 0.8f, y * 0.25f);
                        else if ((y - y0) % 5 == 0) shade *= 0.8f; // rows of tiles
                        Set(x, y, Shade(color, shade));
                    }
                }
                Rect(x0, y0, x1, y0 + 2, Shade(color, 0.6f)); // eaves
            }

            public void Crenellations(int x0, int x1, int y, Color color)
            {
                for (int x = x0; x < x1; x += 10) Bricks(x, y, Mathf.Min(x + 6, x1), y + 6, color);
            }

            /// <summary>A doorway or tunnel mouth: a rectangle with a rounded top.</summary>
            public void Arch(int x0, int y0, int x1, int y1, Color color)
            {
                float r = (x1 - x0) / 2f, cx = (x0 + x1) / 2f;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        float dy = y - (y1 - r);
                        if (dy <= 0 || (x + 0.5f - cx) * (x + 0.5f - cx) + dy * dy <= r * r) Set(x, y, color);
                    }
            }

            public void Ellipse(float cx, float cy, float rx, float ry, Color color)
            {
                for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
                    for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
                    {
                        float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                        float d = dx * dx + dy * dy;
                        if (d <= 1f) Set(x, y, Shade(color, Mathf.Lerp(1.05f, 0.85f, (dx - dy + 1.4f) / 2.8f)));
                    }
            }

            /// <summary>A hill with rocky texture, flat along the ground.</summary>
            public void Hill(float cx, int ground, float rx, float ry, Color dark, Color light)
            {
                for (int y = ground; y <= ground + ry; y++)
                    for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
                    {
                        float dx = (x + 0.5f - cx) / rx, dy = (y - ground) / ry;
                        if (dx * dx + dy * dy > 1f) continue;
                        float n = Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                        Set(x, y, Color.Lerp(dark, light, n * 0.7f + (1f - dx) * 0.2f));
                    }
            }

            public void Triangle(float ax, float ay, float bx, float by, float cx, float cy, Color color)
            {
                int x0 = Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))), x1 = Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx)));
                int y0 = Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))), y1 = Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy)));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px_ = x + 0.5f, py = y + 0.5f;
                        float d1 = (px_ - bx) * (ay - by) - (ax - bx) * (py - by);
                        float d2 = (px_ - cx) * (by - cy) - (bx - cx) * (py - cy);
                        float d3 = (px_ - ax) * (cy - ay) - (cx - ax) * (py - ay);
                        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                        if (!(neg && pos)) Set(x, y, color);
                    }
            }

            /// <summary>A thick line, for handles, beams and door braces.</summary>
            public void Line(int x0, int y0, int x1, int y1, Color color)
            {
                int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                for (int i = 0; i <= steps; i++)
                {
                    float t = steps == 0 ? 0f : i / (float)steps;
                    int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                    Rect(x - 1, y - 1, x + 1, y + 1, color);
                }
            }

            public void LogEnd(float cx, float cy, float r)
            {
                Ellipse(cx, cy, r, r, Wood);
                Ellipse(cx, cy, r * 0.6f, r * 0.6f, new Color(0.78f, 0.6f, 0.38f));
                Ellipse(cx, cy, r * 0.2f, r * 0.2f, Wood);
            }

            public void Crate(int x, int y, int size)
            {
                Planks(x, y, x + size, y + size, Wood);
                Line(x, y, x + size - 1, y + size - 1, DarkWood);
            }

            /// <summary>A domed brick kiln with a glowing mouth.</summary>
            public void Kiln(float cx, float baseY, float r)
            {
                for (int y = Mathf.FloorToInt(baseY); y <= baseY + r; y++)
                    for (int x = Mathf.FloorToInt(cx - r); x <= cx + r; x++)
                    {
                        float dx = (x + 0.5f - cx) / r, dy = (y - baseY) / r;
                        if (dx * dx + dy * dy <= 1f) Set(x, y, Shade(new Color(0.62f, 0.3f, 0.2f), 0.9f + 0.1f * Mathf.PerlinNoise(x * 0.5f, y * 0.5f)));
                    }
                Rect(Mathf.RoundToInt(cx - 4), Mathf.RoundToInt(baseY - 12), Mathf.RoundToInt(cx + 4), Mathf.RoundToInt(baseY), new Color(0.62f, 0.3f, 0.2f)); // base
                Arch(Mathf.RoundToInt(cx - 4), Mathf.RoundToInt(baseY - 12), Mathf.RoundToInt(cx + 4), Mathf.RoundToInt(baseY - 4), new Color(1f, 0.55f, 0.15f));
            }

            /// <summary>A round shield with a boss in the middle.</summary>
            public void Shield(float cx, float cy, Color color)
            {
                Ellipse(cx, cy, 6, 6, color);
                Ellipse(cx, cy, 2, 2, new Color(0.85f, 0.75f, 0.4f));
            }

            /// <summary>A cart wheel: a rim with spokes.</summary>
            public void Wheel(float cx, float cy, float r)
            {
                for (int y = Mathf.FloorToInt(cy - r); y <= cy + r; y++)
                    for (int x = Mathf.FloorToInt(cx - r); x <= cx + r; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                        if (d <= r && d >= r - 2.5f) Set(x, y, Wood);
                    }
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    Line(Mathf.RoundToInt(cx - Mathf.Cos(a) * (r - 2)), Mathf.RoundToInt(cy - Mathf.Sin(a) * (r - 2)),
                         Mathf.RoundToInt(cx + Mathf.Cos(a) * (r - 2)), Mathf.RoundToInt(cy + Mathf.Sin(a) * (r - 2)), DarkWood);
                }
            }

            /// <summary>A horse standing side-on, facing right, feet on <paramref name="ground"/>.</summary>
            public void Horse(float x, int ground)
            {
                Ellipse(x, ground + 18, 12, 6, HorseBrown);                       // body
                foreach (float lx in new[] { x - 8, x - 4, x + 5, x + 9 })   // legs
                    Rect(Mathf.RoundToInt(lx), ground, Mathf.RoundToInt(lx) + 2, ground + 15, HorseBrown);
                Line(Mathf.RoundToInt(x + 9), ground + 20, Mathf.RoundToInt(x + 15), ground + 30, HorseBrown); // neck
                Ellipse(x + 17, ground + 30, 5, 3, HorseBrown);                  // head
                Line(Mathf.RoundToInt(x - 12), ground + 20, Mathf.RoundToInt(x - 16), ground + 10, DarkWood); // tail
            }

            public Sprite ToSprite(string name)
            {
                var result = (Color[])px.Clone();
                if (outline)
                {
                    // Darken the outermost pixels of every shape for a hand-inked look.
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                        {
                            if (px[y * Size + x].a < 0.5f) continue;
                            bool edge = x == 0 || y == 0 || x == Size - 1 || y == Size - 1 ||
                                        px[y * Size + x - 1].a < 0.5f || px[y * Size + x + 1].a < 0.5f ||
                                        px[(y - 1) * Size + x].a < 0.5f || px[(y + 1) * Size + x].a < 0.5f;
                            if (edge) result[y * Size + x] = new Color(0.16f, 0.1f, 0.06f, 1f);
                        }
                }
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels(result);
                tex.Apply(false, true);
                return Sprite.Create(tex, new Rect(0, 0, Size, Size), Pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
        }
    }
}
