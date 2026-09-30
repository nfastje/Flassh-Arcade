using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The small pixel painter the buildings are drawn with.
    /// </summary>
    static partial class BuildingArt
    {
        class Canvas
        {
            readonly Color[] px = new Color[Size * Size];
            readonly bool outline;

            public Canvas(bool outline = true) => this.outline = outline;

            /// <summary>Scales a color's brightness but not its transparency (Color * float would scale both).</summary>
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
