using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>Generates the illustrated village's sprites at runtime.</summary>
    static class VillageArt
    {
        static Sprite grass, clearing, stake, tree, glow, reinforcedStake, stoneWall, mapHut, patch;
        static Material material;

        // Play mode starts without a domain reload in this project, and the textures behind these sprites are
        // destroyed when it stops, so start every session with a clean cache.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            grass = clearing = stake = tree = glow = reinforcedStake = stoneWall = mapHut = patch = null;
            System.Array.Clear(mapVillages, 0, mapVillages.Length);
            mapDot = pixel = null;
            material = null;
        }

        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    material = new Material(shader);
                }
                return material;
            }
        }

        /// <summary>2 x 2 unit tileable grass, pivot in the center.</summary>
        public static Sprite Grass => grass != null ? grass : grass = Make("Grass", 128, 128, 64f, new Vector2(0.5f, 0.5f), TextureWrapMode.Repeat, (x, y) =>
        {
            // Tileable noise: sample Perlin noise around a torus so the edges match up.
            float n = Seamless(x / 128f, y / 128f, 4f) * 0.6f + Seamless(x / 128f, y / 128f, 12f) * 0.4f;
            var c = Color.Lerp(new Color(0.33f, 0.52f, 0.2f), new Color(0.45f, 0.66f, 0.27f), n);
            return c;
        });

        /// <summary>1 x 1 unit trodden-earth oval, pivot in the center (stretched to size).</summary>
        public static Sprite Clearing => clearing != null ? clearing : clearing = Make("Clearing", 256, 256, 256f, new Vector2(0.5f, 0.5f), TextureWrapMode.Clamp, (x, y) =>
        {
            float u = (x + 0.5f) / 128f - 1f, v = (y + 0.5f) / 128f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            float edge = 0.92f + 0.06f * Mathf.PerlinNoise(Mathf.Atan2(v, u) * 2f + 10f, 3f);
            if (d > edge) return Color.clear;
            float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
            var c = Color.Lerp(new Color(0.55f, 0.43f, 0.28f), new Color(0.66f, 0.54f, 0.36f), n);
            c.a = Mathf.Clamp01((edge - d) * 25f);
            return c;
        });

        /// <summary>1 x 1 unit soft white oval, pivot in the center, tinted to mark a selection.</summary>
        public static Sprite Glow => glow != null ? glow : glow = Make("Glow", 64, 64, 64f, new Vector2(0.5f, 0.5f), TextureWrapMode.Clamp, (x, y) =>
        {
            float u = (x + 0.5f) / 32f - 1f, v = (y + 0.5f) / 32f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 1.5f);
        });

        /// <summary>
        /// 1 x 1 unit ragged-edged ground patch, pivot in the center (stretched to size). Light and textured, for
        /// tinting: forest floor, clay, rock or field around the resource buildings.
        /// </summary>
        public static Sprite Patch => patch != null ? patch : patch = Make("Patch", 128, 128, 128f, new Vector2(0.5f, 0.5f), TextureWrapMode.Clamp, (x, y) =>
        {
            float u = (x + 0.5f) / 64f - 1f, v = (y + 0.5f) / 64f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            float edge = 0.85f + 0.12f * Mathf.PerlinNoise(Mathf.Atan2(v, u) * 2.5f + 30f, 7f);
            if (d > edge) return Color.clear;
            float n = 0.8f + 0.2f * Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
            return new Color(n, n, n, Mathf.Clamp01((edge - d) * 12f));
        });

        /// <summary>A sharpened palisade stake, 0.25 x 0.9 units, pivot at the bottom.</summary>
        public static Sprite Stake => stake != null ? stake : stake = Make("Stake", 16, 56, 62f, new Vector2(0.5f, 0f), TextureWrapMode.Clamp, (x, y) =>
        {
            float u = (x + 0.5f) / 8f - 1f;
            float top = 56f - 10f * Mathf.Abs(u) * 1.6f; // pointed tip
            if (y > top || Mathf.Abs(u) > 0.95f) return Color.clear;
            float shade = Mathf.Lerp(1f, 0.65f, (u + 1f) / 2f); // lit from the left
            var c = new Color(0.55f, 0.37f, 0.2f) * shade;
            if (Mathf.Abs(u) > 0.75f || y > top - 1.5f) c *= 0.6f;  // outline
            if (Mathf.PerlinNoise(x * 0.8f, y * 0.15f) > 0.7f) c *= 0.85f; // wood grain
            c.a = 1f;
            return c;
        });

        /// <summary>A taller stake bound with iron bands (wall levels 10-19), 0.29 x 1 units, pivot at the bottom.</summary>
        public static Sprite ReinforcedStake => reinforcedStake != null ? reinforcedStake : reinforcedStake = Make("ReinforcedStake", 18, 64, 64f, new Vector2(0.5f, 0f), TextureWrapMode.Clamp, (x, y) =>
        {
            float u = (x + 0.5f) / 9f - 1f;
            float top = 64f - 11f * Mathf.Abs(u) * 1.6f;
            if (y > top || Mathf.Abs(u) > 0.95f) return Color.clear;
            float shade = Mathf.Lerp(1f, 0.62f, (u + 1f) / 2f);
            var c = new Color(0.45f, 0.3f, 0.16f) * shade;
            if ((y >= 16 && y < 20) || (y >= 38 && y < 42)) c = new Color(0.42f, 0.42f, 0.46f) * shade; // iron bands
            if (Mathf.Abs(u) > 0.75f || y > top - 1.5f) c *= 0.6f;
            c.a = 1f;
            return c;
        });

        /// <summary>A stretch of crenellated stone wall (wall level 20+), 0.42 x 0.9 units, pivot at the bottom.</summary>
        public static Sprite StoneWall => stoneWall != null ? stoneWall : stoneWall = Make("StoneWall", 26, 56, 62f, new Vector2(0.5f, 0f), TextureWrapMode.Clamp, (x, y) =>
        {
            bool crenel = y >= 46 && x >= 9 && x < 17; // a notch in the top for archers
            if (crenel) return Color.clear;
            int row = y / 7;
            bool mortar = y % 7 == 0 || (x + (row % 2) * 6) % 12 == 0;
            float shade = mortar ? 0.7f : 0.92f + 0.1f * Mathf.PerlinNoise(x * 0.4f, y * 0.4f);
            if (x == 0 || x == 25 || y == 55 || (y == 45 && x >= 9 && x < 17)) shade *= 0.6f; // outline, including under the notch
            var c = new Color(0.62f, 0.62f, 0.64f) * shade;
            c.a = 1f;
            return c;
        });

        /// <summary>A little house for marking villages on the world map, 1 unit wide, pivot in the center. White, for tinting.</summary>
        public static Sprite MapHut => mapHut != null ? mapHut : mapHut = Make("MapHut", 32, 32, 32f, new Vector2(0.5f, 0.5f), TextureWrapMode.Clamp, (x, y) =>
        {
            float px = x + 0.5f, py = y + 0.5f;
            bool walls = px > 7 && px < 25 && py > 4 && py < 18;
            bool roof = py >= 17 && py < 29 && Mathf.Abs(px - 16) < (29 - py) * 0.9f + 1;
            if (!walls && !roof) return Color.clear;
            bool door = px > 13 && px < 19 && py > 4 && py < 12;
            bool edge = (walls && (px < 9 || px > 23 || py < 6)) || (roof && (Mathf.Abs(px - 16) > (29 - py) * 0.9f - 1 || py < 19));
            float shade = door ? 0.35f : edge ? 0.45f : roof ? 0.8f : 1f;
            return new Color(shade, shade, shade, 1f);
        });

        /// <summary>How many sizes of village the world map shows.</summary>
        public const int MapVillageTiers = 6;
        static readonly Sprite[] mapVillages = new Sprite[MapVillageTiers * 2];
        static Sprite mapDot;

        /// <summary>
        /// A village on the world map, one field (1 unit) across, pivot in the center, in full color like Tribal
        /// Wars' map: tiers 0 and 1 a ring of palisade round a dirt yard with a few huts, tiers 2 to 5 a stone-walled
        /// octagon that fills up with houses, towers and finally a keep. Barbarian villages are the same pictures in
        /// dull grays.
        /// </summary>
        public static Sprite MapVillage(int tier, bool barbarian)
        {
            tier = Mathf.Clamp(tier, 0, MapVillageTiers - 1);
            int key = tier * 2 + (barbarian ? 1 : 0);
            if (mapVillages[key] != null) return mapVillages[key];
            const int size = 48;
            var px = new Color[size * size];
            DrawMapVillage(px, size, tier);
            Outline(px, size, new Color(0.16f, 0.12f, 0.08f));
            if (barbarian)
                for (int i = 0; i < px.Length; i++)
                {
                    float g = (px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f) * 0.85f;
                    px[i] = new Color(g, g, g, px[i].a);
                }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = $"MapVillage{tier}{(barbarian ? "b" : "")}", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply(false, true);
            return mapVillages[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        static Sprite pixel;

        /// <summary>A plain white square, 1 unit across, pivot in the center: stretched and tinted for lines.</summary>
        public static Sprite Pixel => pixel != null ? pixel : pixel = Make("Pixel", 4, 4, 4f, new Vector2(0.5f, 0.5f), TextureWrapMode.Clamp, (x, y) => Color.white);

        /// <summary>The owner's marker in the corner of a map village: a white disc with a dark rim, for tinting.</summary>
        public static Sprite MapDot => mapDot != null ? mapDot : mapDot = Make("MapDot", 16, 16, 16f, new Vector2(0.5f, 0.5f), TextureWrapMode.Clamp, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8f, 8f));
            if (d > 7.5f) return Color.clear;
            return d > 5.8f ? new Color(0.12f, 0.1f, 0.08f, 1f) : Color.white;
        });

        static readonly Color Dirt = new Color(0.78f, 0.66f, 0.45f), Palisade = new Color(0.52f, 0.34f, 0.18f);
        static readonly Color WallStone = new Color(0.68f, 0.68f, 0.7f), HouseWall = new Color(0.94f, 0.89f, 0.76f);
        static readonly Color RedRoof = new Color(0.72f, 0.26f, 0.17f), BlueRoof = new Color(0.3f, 0.42f, 0.72f), Thatch = new Color(0.8f, 0.64f, 0.34f);

        static void DrawMapVillage(Color[] px, int size, int tier)
        {
            void Set(int x, int y, Color c)
            {
                if (x < 0 || y < 0 || x >= size || y >= size) return;
                px[y * size + x] = new Color(c.r, c.g, c.b, 1f);
            }
            float c0 = size / 2f;

            void Disc(float r, Color c)
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c0, c0)) <= r) Set(x, y, c);
            }

            // A ring of stakes: alternately lit and shaded, so it reads as a palisade.
            void PalisadeRing(float r0, float r1)
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c0, c0));
                        if (d < r0 || d > r1) continue;
                        float a = Mathf.Atan2(y + 0.5f - c0, x + 0.5f - c0);
                        bool lit = Mathf.FloorToInt((a + Mathf.PI) / (Mathf.PI * 2f) * 40f) % 2 == 0;
                        Set(x, y, lit ? Palisade : Palisade * 0.75f);
                    }
            }

            // An octagon (filled or as a wall of the given thickness).
            bool InOctagon(float x, float y, float r) =>
                Mathf.Abs(x - c0) <= r && Mathf.Abs(y - c0) <= r && Mathf.Abs(x - c0) + Mathf.Abs(y - c0) <= r * 1.4f;

            void Octagon(float r, Color c)
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (InOctagon(x + 0.5f, y + 0.5f, r)) Set(x, y, c);
            }

            void StoneWall(float r, float thickness)
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float fx = x + 0.5f, fy = y + 0.5f;
                        if (!InOctagon(fx, fy, r) || InOctagon(fx, fy, r - thickness)) continue;
                        bool mortar = (x + y) % 4 == 0;
                        Set(x, y, mortar ? WallStone * 0.8f : WallStone);
                    }
            }

            // A house seen from the front: walls with a door, under a pointed roof.
            void House(int x0, int y0, int w, int h, int roof, Color roofColor)
            {
                for (int y = y0; y < y0 + h; y++)
                    for (int x = x0; x < x0 + w; x++)
                        Set(x, y, Mathf.Abs(x - (x0 + w / 2)) <= 0 && y < y0 + Mathf.Max(2, h / 2) ? new Color(0.3f, 0.2f, 0.12f) : HouseWall);
                for (int r = 0; r < roof; r++)
                {
                    int half = (w + 1) / 2 + 1 - (r * ((w + 1) / 2 + 1)) / Mathf.Max(1, roof);
                    for (int x = x0 + w / 2 - half; x <= x0 + (w - 1) / 2 + half; x++)
                        Set(x, y0 + h + r, x < x0 + w / 2 ? roofColor : roofColor * 0.8f);
                }
            }

            // A round tower of stone with a blue cone roof.
            void Tower(int cx, int y0, int w, int h)
            {
                for (int y = y0; y < y0 + h; y++)
                    for (int x = cx - w / 2; x < cx - w / 2 + w; x++)
                        Set(x, y, (y - y0) % 3 == 2 ? WallStone * 0.8f : WallStone);
                for (int r = 0; r < w; r++)
                    for (int x = cx - w / 2 - 1 + r / 2; x <= cx + w / 2 - r / 2; x++)
                        Set(x, y0 + h + r, x < cx ? BlueRoof : BlueRoof * 0.8f);
            }

            switch (tier)
            {
                case 0: // a small palisaded clearing with two huts
                    Disc(16, Dirt);
                    PalisadeRing(14, 17);
                    House(15, 17, 8, 6, 5, Thatch);
                    House(25, 21, 8, 6, 5, RedRoof);
                    break;
                case 1: // a bigger ring and three houses
                    Disc(20, Dirt);
                    PalisadeRing(17.5f, 21);
                    House(12, 15, 9, 7, 5, RedRoof);
                    House(25, 13, 9, 7, 5, Thatch);
                    House(18, 25, 10, 7, 6, RedRoof);
                    break;
                case 2: // a stone-walled octagon with four houses
                    Octagon(20, Dirt);
                    StoneWall(21, 3);
                    House(10, 12, 9, 7, 5, RedRoof);
                    House(27, 12, 9, 7, 5, RedRoof);
                    House(11, 25, 9, 7, 5, Thatch);
                    House(26, 25, 10, 7, 6, RedRoof);
                    break;
                case 3: // fuller, with a tower
                    Octagon(21, Dirt);
                    StoneWall(22, 3);
                    House(8, 10, 9, 7, 5, RedRoof);
                    House(30, 10, 9, 7, 5, RedRoof);
                    House(8, 25, 9, 7, 5, Thatch);
                    House(30, 25, 9, 7, 5, RedRoof);
                    House(18, 8, 11, 7, 6, RedRoof);
                    Tower(24, 22, 6, 11);
                    break;
                case 4: // towers on the wall and a tall central tower
                    Octagon(22, Dirt);
                    StoneWall(23, 4);
                    foreach (var (tx, ty) in new[] { (8, 30), (40, 30), (8, 5), (40, 5) }) Tower(tx, ty, 6, 6);
                    House(10, 12, 9, 7, 5, RedRoof);
                    House(29, 12, 9, 7, 5, RedRoof);
                    House(12, 26, 9, 6, 5, RedRoof);
                    House(28, 26, 9, 6, 5, RedRoof);
                    Tower(24, 14, 8, 16);
                    break;
                default: // a castle: corner towers and a keep
                    Octagon(23, Dirt);
                    StoneWall(24, 4);
                    foreach (var (tx, ty) in new[] { (7, 31), (41, 31), (7, 4), (41, 4) }) Tower(tx, ty, 7, 7);
                    for (int y = 12; y < 30; y++)
                        for (int x = 14; x < 34; x++)
                            Set(x, y, (y % 3 == 2 || (x + (y / 3 % 2) * 2) % 5 == 0) ? WallStone * 0.8f : WallStone);
                    for (int x = 14; x < 34; x += 3) Set(x, 30, WallStone);
                    Tower(24, 30, 8, 8);
                    for (int y = 12; y < 18; y++) Set(24, y, new Color(0.3f, 0.2f, 0.12f)); // the keep's gate
                    break;
            }
        }

        /// <summary>A dark rim, one pixel wide, round everything drawn, so pictures stand out on the grass.</summary>
        static void Outline(Color[] px, int size, Color rim)
        {
            var copy = (Color[])px.Clone();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (copy[y * size + x].a > 0.5f) continue;
                    bool edge = false;
                    for (int oy = -1; oy <= 1 && !edge; oy++)
                        for (int ox = -1; ox <= 1 && !edge; ox++)
                        {
                            int nx = x + ox, ny = y + oy;
                            if (nx >= 0 && ny >= 0 && nx < size && ny < size && copy[ny * size + nx].a > 0.5f) edge = true;
                        }
                    if (edge) px[y * size + x] = rim;
                }
        }
        /// <summary>A round leafy tree, 1.4 units wide, pivot at the base of the trunk.</summary>
        public static Sprite Tree => tree != null ? tree : tree = Make("Tree", 64, 80, 64f / 1.4f, new Vector2(0.5f, 0f), TextureWrapMode.Clamp, (x, y) =>
        {
            float px = x + 0.5f, py = y + 0.5f;
            bool trunk = Mathf.Abs(px - 32f) < 4f && py < 26f;
            float d = Vector2.Distance(new Vector2(px, py), new Vector2(32f, 48f));
            float bump = 3f * Mathf.PerlinNoise(Mathf.Atan2(py - 48f, px - 32f) * 2f + 5f, 1f);
            if (d < 28f + bump)
            {
                float shade = Mathf.Lerp(1.1f, 0.7f, Mathf.Clamp01((px - py + 30f) / 60f)); // lit from the top left
                var c = new Color(0.22f, 0.5f, 0.2f) * shade;
                if (d > 26f + bump) c *= 0.55f; // outline
                c.a = 1f;
                return c;
            }
            if (trunk) return Mathf.Abs(px - 32f) > 2.5f ? new Color(0.25f, 0.15f, 0.08f) : new Color(0.45f, 0.3f, 0.16f);
            return Color.clear;
        });

        static float Seamless(float u, float v, float scale)
        {
            float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
            // Map the square onto two circles and blend Perlin samples: close enough to seamless for grass.
            float x = Mathf.Cos(a) * scale / 6.28f + 50f, y = Mathf.Sin(a) * scale / 6.28f + 50f;
            float z = Mathf.Cos(b) * scale / 6.28f + 80f, w = Mathf.Sin(b) * scale / 6.28f + 80f;
            return (Mathf.PerlinNoise(x + z, y + w) + Mathf.PerlinNoise(x - w, y + z)) / 2f;
        }

        static Sprite Make(string name, int w, int h, float ppu, Vector2 pivot, TextureWrapMode wrap, System.Func<int, int, Color> pixelAt)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = pixelAt(x, y);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = wrap };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
        }
    }
}
