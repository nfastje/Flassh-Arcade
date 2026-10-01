using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>
    /// Draws each building's sprite at runtime. Every building has three looks (levels 1-9, 10-19 and 20+) so the
    /// village visibly grows, plus a scaffolding overlay for construction (an empty plot shows nothing).
    /// Sprites are 128 x 128 pixels, drawn on a canvas whose ground line is <see cref="Ground"/>.
    /// </summary>
    static partial class BuildingArt
    {
        public const int Size = 128;
        const int Ground = 8;
        public const float PixelsPerUnit = 56f; // 128 px = about 2.3 units
        static readonly Vector2 Pivot = new Vector2(0.5f, Ground / (float)Size);

        static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
        static Sprite scaffolding;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            cache.Clear();
            scaffolding = null;
        }

        /// <summary>0 for levels 1-9, 1 for 10-19, 2 for 20 and up.</summary>
        public static int TierFor(int level) => level >= 20 ? 2 : level >= 10 ? 1 : 0;

        public static Sprite For(BuildingType type, int level)
        {
            if (level <= 0) return null;
            int tier = TierFor(level);
            int key = (int)type * 10 + tier;
            if (!cache.TryGetValue(key, out var sprite) || sprite == null)
            {
                var c = new Canvas();
                switch (type)
                {
                    case BuildingType.Headquarters: DrawHeadquarters(c, tier); break;
                    case BuildingType.TimberCamp: DrawTimberCamp(c, tier); break;
                    case BuildingType.ClayPit: DrawClayPit(c, tier); break;
                    case BuildingType.IronMine: DrawIronMine(c, tier); break;
                    case BuildingType.Farm: DrawFarm(c, tier); break;
                    case BuildingType.Warehouse: DrawWarehouse(c, tier); break;
                    case BuildingType.Barracks: DrawBarracks(c, tier); break;
                    case BuildingType.Stable: DrawStable(c, tier); break;
                    case BuildingType.Workshop: DrawWorkshop(c, tier); break;
                    case BuildingType.Wall: DrawGate(c, tier); break;
                    case BuildingType.Academy: DrawAcademy(c); break;
                    case BuildingType.RallyPoint: DrawRallyPoint(c); break;
                    case BuildingType.Smithy: DrawSmithy(c, tier); break;
                    case BuildingType.Market: DrawMarket(c, tier); break;
                    case BuildingType.HidingPlace: DrawHidingPlace(c, tier); break;
                }
                cache[key] = sprite = c.ToSprite($"{type}{tier}");
            }
            return sprite;
        }

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

        static void DrawHeadquarters(Canvas c, int tier)
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
                c.Planks(left + 8, top, right - 8, top + 16, DarkWood); // upper story
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
        /// The rally point (one level): a tall pole flying a red banner, a small tent and a rack of spears, low enough
        /// not to hide the Headquarters behind it.
        /// </summary>
        static void DrawRallyPoint(Canvas c)
        {
            c.Rect(62, Ground, 66, 70, DarkWood);                        // the pole
            c.Rect(66, 52, 82, 68, RoofRed);                             // the banner...
            c.Triangle(82, 68, 92, 68, 82, 60, RoofRed);                 // ...with swallowtails
            c.Triangle(82, 52, 92, 52, 82, 60, RoofRed);
            c.Rect(70, 58, 82, 62, Window);                              // a stripe on it
            c.Triangle(18, Ground, 54, Ground, 36, 38, Straw);           // a tent
            c.Triangle(32, Ground, 40, Ground, 36, 22, Doorway);         // its doorway
            c.Rect(80, Ground, 110, Ground + 3, DarkWood);               // a weapon rack...
            foreach (int x in new[] { 84, 92, 100, 106 }) c.Line(x, Ground, x + 4, Ground + 30, Wood);
            foreach (int x in new[] { 88, 96, 104, 110 }) c.Triangle(x - 2, Ground + 30, x + 2, Ground + 30, x, Ground + 35, Stone);
        }

        /// <summary>
        /// A scholars' hall (it only has the one level): pale columns before stone walls, a pediment, a blue dome
        /// and a pennant on top.
        /// </summary>
        static void DrawAcademy(Canvas c)
        {
            var marble = new Color(0.88f, 0.87f, 0.82f);
            c.Ellipse(64, 72, 24, 24, RoofBlue);                        // the dome, mostly hidden behind the front
            c.Rect(62, 94, 66, 100, DarkStone);                          // its lantern
            c.Rect(63, 100, 65, 118, DarkWood);                          // flagpole
            c.Triangle(65, 118, 65, 108, 80, 113, RoofRed);              // pennant
            c.Bricks(20, Ground, 108, 56, Stone);
            c.Rect(16, Ground, 112, Ground + 4, DarkStone);              // steps
            for (int x = 24; x < 104; x += 14) c.Rect(x, Ground + 4, x + 7, 56, marble); // columns
            c.Arch(57, Ground + 4, 71, 38, Doorway);
            c.Rect(16, 56, 112, 62, marble);                             // architrave
            c.Triangle(14, 62, 114, 62, 64, 84, Stone);                  // pediment
            c.Ellipse(64, 70, 5, 5, Window);                             // round window in the pediment
        }

        static readonly Color Ember = new Color(1f, 0.5f, 0.12f);
        static readonly Color Smoke = new Color(0.78f, 0.78f, 0.8f, 0.6f);
        static readonly Color Iron = new Color(0.32f, 0.34f, 0.38f);
        static readonly Color Cloth = new Color(0.95f, 0.9f, 0.78f);
        static readonly Color Earth = new Color(0.42f, 0.3f, 0.18f);
        static readonly Color Turf = new Color(0.36f, 0.52f, 0.22f);

        /// <summary>
        /// The smithy: a forge with its front open on the glowing hearth, an anvil outside and a smoking chimney;
        /// then a stone footing, a tiled roof and a rack of blades; at the top tier, a stone hall with two chimneys.
        /// </summary>
        static void DrawSmithy(Canvas c, int tier)
        {
            int left = 30 - tier * 4, right = 100 + tier * 4, top = 40 + tier * 6;
            if (tier < 2) c.Planks(left, Ground, right, top, Wood);
            else c.Bricks(left, Ground, right, top, Stone);
            if (tier == 1) c.Bricks(left, Ground, right, Ground + 12, Stone);
            c.Rect(left + 6, Ground, left + 36, top - 10, Doorway);          // the open front...
            c.Bricks(left + 10, Ground, left + 30, Ground + 10, DarkStone);  // ...the hearth inside...
            c.Ellipse(left + 20, Ground + 12, 7, 4, Ember);                  // ...and its fire
            c.Rect(right - 20, Ground + 16, right - 10, Ground + 26, Window);
            c.Roof(left - 5, right + 5, top, top + 20, tier == 0 ? Thatch : tier == 1 ? RoofRed : DarkStone, tier == 0);

            // Chimneys, in front of the roof, with smoke.
            int chimneys = tier == 2 ? 2 : 1;
            for (int i = 0; i < chimneys; i++)
            {
                int cx = right - 18 - i * 44;
                c.Bricks(cx - 5, top + 4, cx + 5, top + 30 + tier * 3, Stone);
                c.Ellipse(cx, top + 36 + tier * 3, 5, 4, Smoke);
                c.Ellipse(cx + 5, top + 44 + tier * 3, 6, 4, Smoke);
            }

            // The anvil on its block, out front.
            int ax = right - 30;
            c.Rect(ax - 3, Ground, ax + 4, Ground + 7, DarkWood);
            c.Rect(ax - 8, Ground + 7, ax + 7, Ground + 11, Iron);
            c.Triangle(ax + 7, Ground + 11, ax + 7, Ground + 8, ax + 13, Ground + 10, Iron); // its horn

            if (tier >= 1)
            {
                // A rack of new blades at the right.
                int rx = System.Math.Min(right + 6, Size - 18);
                c.Rect(rx, Ground, rx + 3, Ground + 30, DarkWood);
                c.Rect(rx + 12, Ground, rx + 15, Ground + 30, DarkWood);
                c.Rect(rx, Ground + 24, rx + 15, Ground + 27, DarkWood);
                foreach (int x in new[] { rx + 5, rx + 9 }) c.Rect(x, Ground + 4, x + 2, Ground + 24, Stone);
            }
        }

        /// <summary>A market stall: a counter of goods under a striped awning on two posts.</summary>
        static void Stall(Canvas c, int x0, int x1, Color stripe)
        {
            c.Rect(x0, Ground, x0 + 3, Ground + 34, DarkWood);
            c.Rect(x1 - 3, Ground, x1, Ground + 34, DarkWood);
            c.Planks(x0 + 2, Ground, x1 - 2, Ground + 13, Wood);
            // Goods on the counter: apples, cabbages, a pot.
            c.Ellipse(x0 + 9, Ground + 15, 4, 3, new Color(0.8f, 0.2f, 0.15f));
            c.Ellipse(x0 + 17, Ground + 15, 4, 3, Crop);
            c.Ellipse(x1 - 10, Ground + 16, 4, 4, Clay);
            // The awning, in stripes, with a scalloped edge.
            for (int x = x0 - 3; x < x1 + 3; x++)
                c.Rect(x, Ground + 33, x + 1, Ground + 43, (x - x0 + 3) / 5 % 2 == 0 ? stripe : Cloth);
            for (int x = x0; x < x1; x += 6) c.Ellipse(x + 2, Ground + 33, 3, 2, (x - x0) / 6 % 2 == 0 ? stripe : Cloth);
        }

        /// <summary>The market: a stall and a cart of goods; then two stalls; at the top tier, a stone market hall behind them.</summary>
        static void DrawMarket(Canvas c, int tier)
        {
            if (tier == 2)
            {
                c.Bricks(12, Ground, 116, 54, Stone);
                foreach (int x in new[] { 18, 52, 86 }) c.Arch(x, Ground, x + 24, 42, Doorway);
                c.Roof(6, 122, 54, 76, RoofRed, false);
            }
            Stall(c, tier == 0 ? 36 : 12, tier == 0 ? 82 : 58, ShieldRed);
            if (tier >= 1) Stall(c, 70, 116, ShieldBlue);
            if (tier == 0)
            {
                // A handcart and a couple of crates.
                c.Rect(92, Ground + 6, 120, Ground + 16, Wood);
                c.Wheel(104, Ground + 6, 6);
                c.Line(92, Ground + 12, 84, Ground + 4, DarkWood);
                c.Crate(8, Ground, 12);
                c.Crate(20, Ground, 10);
            }
            else c.Crate(58, Ground, 11);
        }

        /// <summary>The hiding place: a low grassy bank with a small door in it, and bushes to hide it; later a stone-framed door and more cover.</summary>
        static void DrawHidingPlace(Canvas c, int tier)
        {
            c.Hill(64, Ground, 40 + tier * 8, 32 + tier * 6, Earth, Turf);
            if (tier >= 1) c.Arch(53, Ground, 75, 27, Stone);             // a stone frame round the door
            c.Arch(56, Ground, 72, 24, Doorway);
            c.Planks(57, Ground, 71, 18, DarkWood);                     // the door, half open
            c.Ellipse(18, Ground + 8, 12, 9, Crop);                     // bushes
            c.Ellipse(108, Ground + 7, 11, 8, Crop);
            if (tier >= 1)
            {
                c.Ellipse(30, Ground + 5, 9, 6, Crop);
                c.Ellipse(96, Ground + 12, 8, 7, Crop);
                c.Rect(78, Ground, 80, Ground + 20, DarkWood);            // a lantern on a post
                c.Rect(76, Ground + 20, 83, Ground + 26, Window);
            }
        }

        /// <summary>
        /// Half the width of the gate sprite for a wall tier, in world units: the ring leaves just this much room
        /// for it. Every gate is symmetric about the sprite's middle (pixel 64): tier 0 spans pixels 26-102 (with its
        /// palisade wings), tier 1 16-112 (its towers' roofs), tier 2 18-110.
        /// </summary>
        public static float GateHalfWidth(int tier) => (tier == 0 ? 38 : tier == 1 ? 48 : 46) / PixelsPerUnit;

        /// <summary>The wall's gate, which sits in the gap at the front of the palisade ring, centered on the sprite.</summary>
        static void DrawGate(Canvas c, int tier)
        {
            if (tier == 2)
            {
                // Stone gatehouse with towers either side.
                foreach (int tx in new[] { 18, 92 })
                {
                    c.Bricks(tx, Ground, tx + 18, 82, Stone);
                    c.Crenellations(tx + 1, tx + 17, 82, Stone);
                    c.Rect(tx + 7, 58, tx + 11, 66, Doorway);
                }
                c.Bricks(34, Ground, 94, 66, Stone);
                c.Crenellations(36, 94, 66, Stone);
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
                for (int x = 26; x < 40; x += 5) c.Rect(x, Ground, x + 4, 44, Wood);
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
    }
}
