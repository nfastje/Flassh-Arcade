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

        /// <summary>2 x 2 unit tileable grass, pivot in the centre.</summary>
        public static Sprite Grass => grass != null ? grass : grass = Make("Grass", 128, 128, 64f, new Vector2(0.5f, 0.5f), TextureWrapMode.Repeat, (x, y) =>
        {
            // Tileable noise: sample Perlin noise around a torus so the edges match up.
            float n = Seamless(x / 128f, y / 128f, 4f) * 0.6f + Seamless(x / 128f, y / 128f, 12f) * 0.4f;
            var c = Color.Lerp(new Color(0.33f, 0.52f, 0.2f), new Color(0.45f, 0.66f, 0.27f), n);
            return c;
        });

        /// <summary>1 x 1 unit trodden-earth oval, pivot in the centre (stretched to size).</summary>
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

        /// <summary>1 x 1 unit soft white oval, pivot in the centre, tinted to mark a selection.</summary>
        public static Sprite Glow => glow != null ? glow : glow = Make("Glow", 64, 64, 64f, new Vector2(0.5f, 0.5f), TextureWrapMode.Clamp, (x, y) =>
        {
            float u = (x + 0.5f) / 32f - 1f, v = (y + 0.5f) / 32f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 1.5f);
        });

        /// <summary>
        /// 1 x 1 unit ragged-edged ground patch, pivot in the centre (stretched to size). Light and textured, for
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

        /// <summary>A little house for marking villages on the world map, 1 unit wide, pivot in the centre. White, for tinting.</summary>
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
        static readonly Sprite[] mapVillages = new Sprite[MapVillageTiers];

        /// <summary>
        /// A village on the world map, one field (1 unit) across, pivot in the centre, in greys for tinting with its
        /// owner's colour. Tier 0 is a lone hut; each tier adds more and bigger buildings, then a palisade, a stone
        /// wall and towers, up to a castle at tier 5, like Tribal Wars' six village sizes.
        /// </summary>
        public static Sprite MapVillage(int tier)
        {
            tier = Mathf.Clamp(tier, 0, MapVillageTiers - 1);
            if (mapVillages[tier] != null) return mapVillages[tier];
            const int size = 48;
            var px = new Color[size * size];
            DrawMapVillage(px, size, tier);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = $"MapVillage{tier}", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply(false, true);
            return mapVillages[tier] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        static void DrawMapVillage(Color[] px, int size, int tier)
        {
            void Set(int x, int y, float shade)
            {
                if (x < 0 || y < 0 || x >= size || y >= size) return;
                px[y * size + x] = new Color(shade, shade, shade, 1f);
            }

            // A house: walls from (x0, y0), w wide and h tall, with a pointed roof of height roof. Stone houses get
            // mortar lines; the outline is dark so houses stand apart from each other.
            void House(int x0, int y0, int w, int h, int roof, bool stone)
            {
                for (int y = y0; y < y0 + h; y++)
                    for (int x = x0; x < x0 + w; x++)
                    {
                        bool edge = x == x0 || x == x0 + w - 1 || y == y0;
                        bool door = w >= 6 && Mathf.Abs(x - (x0 + w / 2)) <= 1 && y < y0 + Mathf.Max(2, h / 2);
                        bool mortar = stone && ((y - y0) % 3 == 2 || (x + ((y - y0) / 3 % 2) * 2) % 4 == 0);
                        Set(x, y, edge ? 0.4f : door ? 0.3f : mortar ? 0.78f : 1f);
                    }
                for (int r = 0; r < roof; r++)
                {
                    int half = (w / 2 + 1) - (r * (w / 2 + 1)) / Mathf.Max(1, roof);
                    for (int x = x0 + w / 2 - half; x <= x0 + w / 2 + half - (w % 2 == 0 ? 1 : 0); x++)
                    {
                        bool edge = x == x0 + w / 2 - half || x == x0 + w / 2 + half - (w % 2 == 0 ? 1 : 0) || r == roof - 1;
                        Set(x, y0 + h + r, edge ? 0.4f : 0.72f);
                    }
                }
            }

            // A tower: a tall stone block with battlements.
            void Tower(int x0, int y0, int w, int h)
            {
                House(x0, y0, w, h, 0, true);
                for (int x = x0; x < x0 + w; x += 2)
                    for (int y = y0 + h; y < y0 + h + 2; y++) Set(x, y, x == x0 || x >= x0 + w - 2 ? 0.4f : 0.9f);
            }

            // A ring round the village: a palisade of stakes, or a stone wall.
            void Ring(int x0, int y0, int x1, int y1, bool stone)
            {
                for (int x = x0; x <= x1; x++)
                    for (int t = 0; t < (stone ? 3 : 2); t++)
                    {
                        float shade = stone ? (t == 0 ? 0.45f : 0.85f) : (x % 2 == 0 ? 0.62f : 0.5f);
                        Set(x, y0 + t, shade);
                        Set(x, y1 - t, shade);
                    }
                for (int y = y0; y <= y1; y++)
                    for (int t = 0; t < (stone ? 3 : 2); t++)
                    {
                        float shade = stone ? (t == 0 ? 0.45f : 0.85f) : (y % 2 == 0 ? 0.62f : 0.5f);
                        Set(x0 + t, y, shade);
                        Set(x1 - t, y, shade);
                    }
            }

            switch (tier)
            {
                case 0: // a lone hut
                    House(17, 12, 14, 10, 9, false);
                    break;
                case 1: // two huts
                    House(8, 16, 13, 9, 8, false);
                    House(25, 10, 15, 10, 9, false);
                    break;
                case 2: // a hamlet
                    House(5, 20, 12, 9, 8, false);
                    House(19, 24, 12, 8, 7, false);
                    House(14, 6, 18, 11, 10, false);
                    House(34, 12, 10, 9, 7, false);
                    break;
                case 3: // a village behind a palisade
                    Ring(3, 3, 44, 44, false);
                    House(7, 26, 11, 8, 7, false);
                    House(22, 28, 11, 8, 7, false);
                    House(9, 8, 13, 9, 8, false);
                    House(26, 8, 15, 11, 10, false);
                    break;
                case 4: // a stone-walled town with a tower
                    Ring(2, 2, 45, 45, true);
                    Tower(19, 20, 10, 18);
                    House(6, 24, 11, 8, 7, true);
                    House(32, 24, 10, 8, 7, true);
                    House(6, 6, 13, 9, 8, true);
                    House(28, 6, 14, 9, 8, true);
                    break;
                default: // a castle
                    Ring(1, 1, 46, 46, true);
                    Tower(2, 2, 8, 14);
                    Tower(38, 2, 8, 14);
                    Tower(2, 32, 8, 14);
                    Tower(38, 32, 8, 14);
                    House(13, 8, 22, 18, 0, true); // the keep
                    Tower(17, 26, 14, 12);
                    break;
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
