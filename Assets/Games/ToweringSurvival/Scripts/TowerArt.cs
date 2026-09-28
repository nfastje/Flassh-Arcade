using UnityEngine;

namespace ToweringSurvival
{
    /// <summary>Generates Towering Survival's sprites at runtime. Blocks are white with baked-in bevels so they can be tinted.</summary>
    static class TowerArt
    {
        static Sprite soap, soapBubble, pixel, lava, lavaBubble, sky;
        static readonly System.Collections.Generic.Dictionary<int, Sprite> pieceBlocks = new System.Collections.Generic.Dictionary<int, Sprite>();
        static Material material;

        // Play mode starts without a domain reload in this project, and the textures behind these sprites are
        // destroyed when it stops, so start every session with a clean cache.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            soap = soapBubble = pixel = lava = lavaBubble = sky = null;
            pieceBlocks.Clear();
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

        // Bits of a piece-block key: which neighbours belong to the same piece, and which inner (concave) corners it has.
        public const int Up = 1, Right = 2, Down = 4, Left = 8;
        public const int InnerUpLeft = 16, InnerUpRight = 32, InnerDownRight = 64, InnerDownLeft = 128;

        /// <summary>
        /// One 1 x 1 unit block of a piece, pivot in the centre. Only the piece's outer edges are outlined and its
        /// outer corners rounded, so the blocks of a piece join up into one solid cartoon shape.
        /// </summary>
        public static Sprite PieceBlock(int key)
        {
            if (!pieceBlocks.TryGetValue(key, out var sprite) || sprite == null)
                pieceBlocks[key] = sprite = MakePieceBlock(key);
            return sprite;
        }
        /// <summary>A white bar of soap standing upright, 0.55 x 0.88 units, pivot at the bottom centre. The player.</summary>
        public static Sprite Soap => soap != null ? soap : soap = MakeSoap();
        /// <summary>1 x 1 unit plain white square, pivot in the centre, for tinted panels and lines.</summary>
        public static Sprite Pixel => pixel != null ? pixel : pixel = Make("Pixel", 4, 4, 4f, new Vector2(0.5f, 0.5f), (x, y) => Color.white);
        /// <summary>1 x 1 unit lava body, pivot at the top; bright at the surface, darker below.</summary>
        public static Sprite Lava => lava != null ? lava : lava = MakeLava();
        /// <summary>1 unit wide translucent soap bubble, pivot in the centre. Released when the soap pops.</summary>
        public static Sprite SoapBubble => soapBubble != null ? soapBubble : soapBubble = Make("SoapBubble", 32, 32, 32f, new Vector2(0.5f, 0.5f), (x, y) =>
        {
            float u = (x + 0.5f) / 16f - 1f, v = (y + 0.5f) / 16f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            if (d > 1f) return Color.clear;
            float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.15f);
            float glint = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(-0.35f, 0.35f)) / 0.25f);
            var tint = Color.Lerp(new Color(0.8f, 0.9f, 1f), new Color(1f, 0.85f, 1f), (u + 1f) / 2f); // faint rainbow sheen
            return new Color(tint.r, tint.g, tint.b, Mathf.Max(rim * 0.85f, Mathf.Max(0.12f, glint)));
        });

        /// <summary>1 unit wide lava bubble, pivot in the centre.</summary>
        public static Sprite LavaBubble => lavaBubble != null ? lavaBubble : lavaBubble = MakeLavaBubble();
        /// <summary>1 x 1 unit vertical sky gradient, pivot in the centre.</summary>
        public static Sprite Sky => sky != null ? sky : sky = MakeSky();

        static Sprite Make(string name, int w, int h, float ppu, Vector2 pivot, System.Func<int, int, Color> pixelAt)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = pixelAt(x, y);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
        }

        static Sprite MakePieceBlock(int key)
        {
            const int n = 32;
            const float radius = 9f, outline = 3.5f;
            bool up = (key & Up) != 0, right = (key & Right) != 0, down = (key & Down) != 0, left = (key & Left) != 0;

            return Make($"PieceBlock{key}", n, n, n, new Vector2(0.5f, 0.5f), (x, y) =>
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);

                // Distance to the piece's outer boundary; connected sides have no boundary.
                float d = float.MaxValue;
                if (!left) d = Mathf.Min(d, p.x);
                if (!right) d = Mathf.Min(d, n - p.x);
                if (!down) d = Mathf.Min(d, p.y);
                if (!up) d = Mathf.Min(d, n - p.y);

                // Round the outer corners.
                float Corner(bool a, bool b, float cx, float cy, bool inX, bool inY) =>
                    a && b && inX && inY ? radius - Vector2.Distance(p, new Vector2(cx, cy)) : float.MaxValue;
                float c = Mathf.Min(
                    Mathf.Min(Corner(!up, !left, radius, n - radius, p.x < radius, p.y > n - radius),
                              Corner(!up, !right, n - radius, n - radius, p.x > n - radius, p.y > n - radius)),
                    Mathf.Min(Corner(!down, !right, n - radius, radius, p.x > n - radius, p.y < radius),
                              Corner(!down, !left, radius, radius, p.x < radius, p.y < radius)));
                if (c != float.MaxValue) d = c;

                // Inner corners: the boundary passes through the cell's corner point.
                if ((key & InnerUpLeft) != 0) d = Mathf.Min(d, Vector2.Distance(p, new Vector2(0f, n)));
                if ((key & InnerUpRight) != 0) d = Mathf.Min(d, Vector2.Distance(p, new Vector2(n, n)));
                if ((key & InnerDownRight) != 0) d = Mathf.Min(d, Vector2.Distance(p, new Vector2(n, 0f)));
                if ((key & InnerDownLeft) != 0) d = Mathf.Min(d, Vector2.Distance(p, new Vector2(0f, 0f)));

                float alpha = Mathf.Clamp01(d + 0.5f);
                if (alpha <= 0f) return Color.clear;

                // Cartoon shading: flat fill, bright band along the top edge, darker band along the bottom edge.
                float shade = 0.8f;
                if (!up) shade = Mathf.Lerp(1f, shade, Mathf.Clamp01((n - p.y - outline) / 7f));
                if (!down) shade *= Mathf.Lerp(0.65f, 1f, Mathf.Clamp01((p.y - outline) / 7f));
                if (!up && !left && Vector2.Distance(p, new Vector2(10f, 22f)) < 3f) shade = 1f; // glossy dot
                if (d < outline) shade = 0.25f;
                return new Color(shade, shade, shade, alpha);
            });
        }

        /// <summary>Signed distance from a point to a rounded rectangle centred on the origin (negative inside).</summary>
        static float RoundedRect(float x, float y, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(x) - halfW + radius, qy = Mathf.Abs(y) - halfH + radius;
            return Mathf.Sqrt(Mathf.Pow(Mathf.Max(qx, 0f), 2f) + Mathf.Pow(Mathf.Max(qy, 0f), 2f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        static Sprite MakeSoap()
        {
            // Standing upright: 30 x 48 pixels shown at 0.55 x 0.88 units.
            const int w = 30, h = 48;
            return Make("Soap", w, h, h / 0.88f, new Vector2(0.5f, 0f), (x, y) =>
            {
                float px = x + 0.5f - w / 2f, py = y + 0.5f - h / 2f;
                float d = RoundedRect(px, py, w / 2f - 0.5f, h / 2f - 0.5f, 9f);
                if (d > 0.5f) return Color.clear;

                // Soft, slightly bluish white, shaded darker towards the right like a rounded bar.
                float shade = Mathf.Lerp(1f, 0.8f, (px + w / 2f) / w);
                var c = new Color(0.93f * shade, 0.96f * shade, 1f * shade);
                // Glossy highlight at the top left.
                float gloss = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(px, py), new Vector2(-5f, 14f)) / 6f);
                c = Color.Lerp(c, Color.white, gloss * 0.9f);
                if (d > -1.2f) c *= 0.75f; // soft outline
                c.a = Mathf.Clamp01(0.5f - d);
                return c;
            });
        }

        static Sprite MakeLavaBubble()
        {
            const int n = 32;
            return Make("LavaBubble", n, n, n, new Vector2(0.5f, 0.5f), (x, y) =>
            {
                float u = (x + 0.5f) / (n / 2f) - 1f, v = (y + 0.5f) / (n / 2f) - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                if (d > 1f) return Color.clear;
                var c = Color.Lerp(new Color(1f, 0.85f, 0.35f), new Color(0.9f, 0.3f, 0.05f), d);
                float glint = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(-0.35f, 0.35f)) / 0.3f);
                c = Color.Lerp(c, new Color(1f, 1f, 0.8f), glint);
                if (d > 0.82f) c = new Color(0.6f, 0.12f, 0.03f);
                c.a = Mathf.Clamp01((1f - d) * 16f);
                return c;
            });
        }

        static Sprite MakeLava()
        {
            const int w = 4, h = 64;
            var top = new Color(1f, 0.55f, 0.1f);
            var bottom = new Color(0.45f, 0.05f, 0.02f);
            return Make("Lava", w, h, h, new Vector2(0.5f, 1f), (x, y) => Color.Lerp(bottom, top, Mathf.Pow(y / (h - 1f), 2f)));
        }

        static Sprite MakeSky()
        {
            const int w = 4, h = 64;
            var low = new Color(0.22f, 0.07f, 0.1f);
            var high = new Color(0.05f, 0.04f, 0.14f);
            return Make("Sky", w, h, h, new Vector2(0.5f, 0.5f), (x, y) => Color.Lerp(low, high, y / (h - 1f)));
        }
    }
}
