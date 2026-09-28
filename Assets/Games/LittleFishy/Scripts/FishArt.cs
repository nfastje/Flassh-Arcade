using UnityEngine;

namespace LittleFishy
{
    /// <summary>
    /// Generates every sprite in Little Fishy at runtime. Fish parts are white with baked-in shading so one set
    /// of sprites can be tinted to any colour.
    /// </summary>
    static class FishArt
    {
        static Sprite body, tail, eye, bubble, seaweed, sand, rock, water;
        static Texture2D bone;
        static Material material;

        // Play mode starts without a domain reload in this project, and the textures behind these sprites are
        // destroyed when it stops, so start every session with a clean cache.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            body = tail = eye = bubble = seaweed = sand = rock = water = null;
            bone = null;
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

        // Body: 2 x 1 units, facing +x, centred on the fish's origin.
        public const float BodyHalfLength = 0.95f;
        public const float BodyHalfHeight = 0.31f;
        // Tail: 0.8 units, hinged at its right edge, tucked into the back of the body so it reads as one shape.
        public const float TailAttachX = -0.84f;
        public const float TailLength = 0.8f;
        public const float TailHalfHeight = 0.3f;                                // average half-height, for the hitbox
        public const float FullLength = BodyHalfLength - TailAttachX + TailLength; // nose to tail tip at scale 1

        public static Sprite Body => body != null ? body : body = MakeBody();
        public static Sprite Tail => tail != null ? tail : tail = MakeTail();
        public static Sprite Eye => eye != null ? eye : eye = MakeEye();
        public static Sprite Bubble => bubble != null ? bubble : bubble = MakeBubble();
        public static Sprite Seaweed => seaweed != null ? seaweed : seaweed = MakeSeaweed();
        public static Sprite Sand => sand != null ? sand : sand = MakeSand();
        public static Sprite Rock => rock != null ? rock : rock = MakeRock();
        public static Sprite Water => water != null ? water : water = MakeWater();

        /// <summary>A cartoon bone for the HUD's eaten-fish tally (a GUI texture, 64 x 24, not a sprite).</summary>
        public static Texture2D Bone => bone != null ? bone : bone = MakeBone();

        static Texture2D MakeBone()
        {
            const int w = 64, h = 24;
            var px = new Color32[w * h];
            Vector2[] knobs = { new Vector2(8f, 7f), new Vector2(8f, 17f), new Vector2(56f, 7f), new Vector2(56f, 17f) };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                // Signed distance to the bone shape: a shaft plus a pair of round knobs at each end.
                float shaft = Mathf.Max(Mathf.Abs(p.x - 32f) - 23f, Mathf.Abs(p.y - 12f) - 4.5f);
                float d = shaft;
                foreach (var k in knobs) d = Mathf.Min(d, Vector2.Distance(p, k) - 6f);

                float alpha = Mathf.Clamp01(0.5f - d);
                float shade = d > -1.6f ? 0.45f : Mathf.Lerp(0.85f, 1f, (p.y - 4f) / 16f); // dark outline, lit from above
                px[y * w + x] = new Color(shade, shade * 0.97f, shade * 0.9f, alpha);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Bone", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Sprite ToSprite(string name, int w, int h, Color32[] px, float ppu, Vector2 pivot)
        {
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

        static Color Grey(float shade, float alpha) => new Color(shade, shade, shade, alpha);

        static float Fbm(float x, float y)
        {
            float sum = 0f, amp = 0.5f, norm = 0f, freq = 1f;
            for (int i = 0; i < 4; i++)
            {
                sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum / norm;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static Sprite MakeBody()
        {
            const int w = 128, h = 64;
            var px = new Color32[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Local units: X in -1..1, Y in -0.5..0.5.
                float X = (x + 0.5f) / (w / 2f) - 1f;
                float Y = ((y + 0.5f) / (h / 2f) - 1f) * 0.5f;
                float ex = X / BodyHalfLength, ey = Y / BodyHalfHeight;
                float e = ex * ex + ey * ey;
                var p = new Vector2(X, Y);

                if (e <= 1.02f)
                {
                    float alpha = Mathf.Clamp01((1.02f - e) * 12f);
                    float t = Y / BodyHalfHeight; // -1 belly .. 1 back
                    float shade = 1f - 0.22f * Mathf.Max(0f, t) - 0.08f * Mathf.Max(0f, -t);
                    if (InTriangle(p, new Vector2(0.15f, -0.08f), new Vector2(-0.12f, -0.2f), new Vector2(0.05f, -0.03f))) shade *= 0.8f; // side fin
                    if (X > 0.84f && Mathf.Abs(Y + 0.03f) < 0.02f) shade *= 0.45f; // mouth
                    if (e > 0.82f) shade *= Mathf.Lerp(1f, 0.55f, (e - 0.82f) / 0.2f); // outline
                    px[y * w + x] = Grey(shade, alpha);
                }
                else
                {
                    px[y * w + x] = new Color32(0, 0, 0, 0);
                }
            }
            return ToSprite("FishBody", w, h, px, w / 2f, new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeTail()
        {
            const int n = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / (n / 2f) - 1f; // -1 tip .. 1 hinge
                float v = (y + 0.5f) / (n / 2f) - 1f;
                float t = (1f - u) / 2f;               // 0 at hinge .. 1 at tip
                float half = 0.3f + 0.64f * t; // as wide as the body where it joins, so there's no pinched gap
                bool inside = Mathf.Abs(v) < half && !(u < -0.5f && Mathf.Abs(v) < (-0.5f - u) * 1.5f);
                if (!inside)
                {
                    px[y * n + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float rays = 0.82f + 0.1f * Mathf.Cos(Mathf.Atan2(v, 1f - u) * 16f);
                float edge = half - Mathf.Abs(v);
                if (edge < 0.07f) rays *= Mathf.Lerp(0.6f, 1f, edge / 0.07f);
                px[y * n + x] = Grey(rays, Mathf.Clamp01(edge * 30f));
            }
            return ToSprite("FishTail", n, n, px, n / TailLength, new Vector2(1f, 0.5f));
        }

        static Sprite MakeEye()
        {
            const int n = 32;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / (n / 2f) - 1f, v = (y + 0.5f) / (n / 2f) - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                if (d > 1f)
                {
                    px[y * n + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                // Pupil dead centre with no glint: a blank, mindless stare.
                Color c = d < 0.45f ? new Color(0.05f, 0.05f, 0.08f) : Color.white;
                if (d > 0.85f) c = new Color(0.15f, 0.15f, 0.2f);
                c.a = Mathf.Clamp01((1f - d) * 16f);
                px[y * n + x] = c;
            }
            return ToSprite("FishEye", n, n, px, n, new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeBubble()
        {
            const int n = 32;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / (n / 2f) - 1f, v = (y + 0.5f) / (n / 2f) - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.15f);
                float fill = d < 0.85f ? 0.15f : 0f;
                float glint = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(-0.35f, 0.35f)) / 0.25f);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Max(rim * 0.8f, Mathf.Max(fill, glint)) * (d < 1f ? 1f : 0f));
            }
            return ToSprite("Bubble", n, n, px, n / 2f, new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeSeaweed()
        {
            const int w = 32, h = 128;
            var px = new Color32[w * h];
            var dark = new Color(0.1f, 0.45f, 0.2f);
            var light = new Color(0.35f, 0.8f, 0.35f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float t = y / (float)(h - 1);                       // 0 root .. 1 tip
                float centre = 0.5f + 0.22f * Mathf.Sin(t * 9f) * t; // wavy stalk
                float half = Mathf.Lerp(0.2f, 0.04f, t);
                float d = Mathf.Abs((x + 0.5f) / w - centre);
                if (d > half)
                {
                    px[y * w + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                var c = Color.Lerp(dark, light, 1f - d / half * 0.7f);
                c.a = Mathf.Clamp01((half - d) * w * 0.5f);
                px[y * w + x] = c;
            }
            return ToSprite("Seaweed", w, h, px, h / 2f, new Vector2(0.5f, 0f)); // 0.5 x 2 units, rooted at the bottom
        }

        static Sprite MakeSand()
        {
            const int w = 512, h = 64;
            var px = new Color32[w * h];
            var a = new Color(0.93f, 0.82f, 0.55f);
            var b = new Color(0.78f, 0.64f, 0.4f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float top = h - 6f - 5f * Mathf.PerlinNoise(x * 0.02f, 3.3f); // wavy surface
                if (y > top)
                {
                    px[y * w + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float n = Fbm(x * 0.05f, y * 0.12f);
                float speck = Mathf.PerlinNoise(x * 0.9f, y * 0.9f) > 0.72f ? 0.85f : 1f;
                var c = Color.Lerp(b, a, n) * speck * Mathf.Lerp(0.8f, 1f, y / top);
                c.a = Mathf.Clamp01(top - y);
                px[y * w + x] = c;
            }
            return ToSprite("Sand", w, h, px, h, new Vector2(0.5f, 0f)); // 8 x 1 units
        }

        static Sprite MakeRock()
        {
            const int n = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / (n / 2f) - 1f, v = (y + 0.5f) / (n / 2f) - 1f;
                float ang = Mathf.Atan2(v, u);
                float edge = 0.85f - 0.15f * Mathf.PerlinNoise(Mathf.Cos(ang) + 5f, Mathf.Sin(ang) + 5f);
                float d = Mathf.Sqrt(u * u + v * v * 1.8f);
                if (d > edge || v < -0.55f)
                {
                    px[y * n + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float shade = 0.5f + 0.35f * Fbm(u * 3f + 9f, v * 3f + 9f) + 0.15f * v;
                var c = new Color(shade * 0.85f, shade * 0.88f, shade * 0.95f, Mathf.Clamp01((edge - d) * 20f));
                px[y * n + x] = c;
            }
            return ToSprite("Rock", n, n, px, n / 2f, new Vector2(0.5f, 0.2f));
        }

        static Sprite MakeWater()
        {
            const int w = 4, h = 128;
            var px = new Color32[w * h];
            var deep = new Color(0.03f, 0.2f, 0.42f);
            var shallow = new Color(0.2f, 0.6f, 0.85f);
            var surface = new Color(0.6f, 0.88f, 1f);
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                Color c = t > 0.92f ? Color.Lerp(shallow, surface, (t - 0.92f) / 0.08f) : Color.Lerp(deep, shallow, t / 0.92f);
                for (int x = 0; x < w; x++) px[y * w + x] = c;
            }
            return ToSprite("Water", w, h, px, 1f, new Vector2(0.5f, 0.5f)); // 4 x 128 units; scaled to fill the view
        }
    }
}
