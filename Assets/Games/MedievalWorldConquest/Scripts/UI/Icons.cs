using System;
using System.Collections.Generic;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace MedievalWorldConquest
{
    /// <summary>
    /// Small pictures for the UI, painted at runtime: the resources (wood, clay, iron, storage, population), every
    /// unit, and a few buttons (reports, ranking, map). Each is 32 x 32 pixels with a dark outline so it reads on
    /// parchment and on the dark top bar alike.
    /// </summary>
    static class Icons
    {
        const int Size = 32;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        // Play mode starts without a domain reload, and the textures are destroyed when it stops.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => cache.Clear();

        /// <summary>A square element showing an icon, <paramref name="size"/> reference pixels across.</summary>
        public static VisualElement Element(Texture2D icon, float size, params string[] classes)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("icon");
            foreach (var c in classes) e.AddToClassList(c);
            e.style.width = size;
            e.style.height = size;
            e.style.flexShrink = 0;
            e.style.backgroundImage = new StyleBackground(icon);
            return e;
        }

        public static Texture2D Resource(ResourceType r) =>
            r == ResourceType.Wood ? Wood : r == ResourceType.Clay ? Clay : Iron;

        public static Texture2D Wood => Get("wood", DrawWood);
        public static Texture2D Clay => Get("clay", DrawClay);
        public static Texture2D Iron => Get("iron", DrawIron);
        public static Texture2D Storage => Get("storage", DrawStorage);
        public static Texture2D Population => Get("population", DrawPopulation);
        public static Texture2D Reports => Get("reports", DrawScroll);
        public static Texture2D Ranking => Get("ranking", DrawTrophy);
        public static Texture2D Map => Get("map", DrawMap);
        public static Texture2D Messages => Get("messages", DrawLetter);
        public static Texture2D Villages => Get("villages", DrawHouses);
        public static Texture2D Village => Get("village", DrawHouse);
        public static Texture2D Ledger => Get("ledger", DrawLedger);
        public static Texture2D Loot => Get("loot", DrawSack);
        public static Texture2D Tribe => Get("tribe", DrawBanner);
        public static Texture2D Unit(UnitType type) => Get("unit" + (int)type, p => DrawUnit(p, type));

        static Texture2D Get(string key, Action<Painter> draw)
        {
            if (cache.TryGetValue(key, out var tex) && tex != null) return tex;
            var painter = new Painter();
            draw(painter);
            painter.Outline(new Color(0.15f, 0.1f, 0.05f));
            return cache[key] = painter.ToTexture(key);
        }

        // ---------------------------------------------------------------- colors

        static readonly Color Bark = new Color(0.45f, 0.28f, 0.14f), LightWood = new Color(0.82f, 0.64f, 0.4f);
        static readonly Color Brick = new Color(0.78f, 0.36f, 0.2f), Mortar = new Color(0.55f, 0.24f, 0.13f);
        static readonly Color Steel = new Color(0.72f, 0.74f, 0.78f), DarkSteel = new Color(0.45f, 0.47f, 0.52f);
        static readonly Color Gold = new Color(0.98f, 0.78f, 0.2f), DarkGold = new Color(0.75f, 0.52f, 0.1f);
        static readonly Color Parchment = new Color(0.95f, 0.88f, 0.68f), Ink = new Color(0.45f, 0.32f, 0.2f);
        static readonly Color Horse = new Color(0.52f, 0.33f, 0.18f), DarkHorse = new Color(0.3f, 0.19f, 0.1f);
        static readonly Color HandleWood = new Color(0.66f, 0.38f, 0.13f), AxeHead = new Color(0.33f, 0.43f, 0.48f);

        // ---------------------------------------------------------------- resources and buttons

        static void DrawWood(Painter p)
        {
            // Three logs, end on: two below, one on top.
            foreach (var (x, y) in new[] { (10f, 22f), (22f, 22f), (16f, 12f) })
            {
                p.Circle(x, y, 6.5f, Bark);
                p.Circle(x, y, 5f, LightWood);
                p.Circle(x, y, 2.5f, new Color(0.7f, 0.5f, 0.3f));
            }
        }

        static void DrawClay(Painter p)
        {
            // A little stack of bricks.
            foreach (var (x0, y0) in new[] { (3f, 20f), (16f, 20f), (9f, 12f) })
            {
                p.Rect(x0, y0, x0 + 13, y0 + 7, Brick);
                p.Rect(x0, y0 + 6, x0 + 13, y0 + 7, Mortar);
            }
        }

        static void DrawIron(Painter p)
        {
            // Two iron ingots, one stacked on the other.
            Ingot(p, 3, 29, 26, 7);
            Ingot(p, 7, 18, 18, 7);
        }

        /// <summary>
        /// An ingot seen from the front and a little above: a bright top, a mid-gray sloping front, a dark foot and a
        /// glint. <paramref name="x"/> and <paramref name="bottom"/> place its lower left corner.
        /// </summary>
        static void Ingot(Painter p, float x, float bottom, float width, float height)
        {
            float top = bottom - height, inset = height * 0.45f;
            p.Quad(x, bottom, x + width, bottom, x + width - inset, top, x + inset, top, new Color(0.55f, 0.58f, 0.64f));             // front
            p.Quad(x + inset, top, x + width - inset, top, x + width - inset - 2, top - 3, x + inset + 2, top - 3, new Color(0.84f, 0.86f, 0.9f)); // top
            p.Rect(x, bottom - 1.5f, x + width, bottom, new Color(0.34f, 0.36f, 0.42f));                                        // foot
            p.Line(x + inset + 1, top + 1.5f, x + width * 0.5f, top + 1.5f, 1f, new Color(0.9f, 0.92f, 0.95f));                  // glint
        }

        static void DrawStorage(Painter p)
        {
            // A crate with cross-bracing.
            p.Rect(5, 7, 27, 27, LightWood);
            p.Line(6, 8, 26, 26, 2.5f, Bark);
            p.Line(26, 8, 6, 26, 2.5f, Bark);
            p.Rect(5, 7, 27, 9, Bark);
            p.Rect(5, 25, 27, 27, Bark);
        }

        static void DrawPopulation(Painter p)
        {
            // Two villagers.
            p.Circle(11, 10, 4f, new Color(0.95f, 0.78f, 0.6f));
            p.Rect(6, 15, 16, 27, new Color(0.3f, 0.45f, 0.75f));
            p.Circle(21, 12, 4f, new Color(0.9f, 0.72f, 0.55f));
            p.Rect(16, 17, 26, 28, new Color(0.65f, 0.3f, 0.25f));
        }

        static void DrawScroll(Painter p)
        {
            p.Rect(8, 6, 24, 26, Parchment);
            p.Ellipse(16, 6, 9, 3, new Color(0.85f, 0.72f, 0.45f));
            p.Ellipse(16, 26, 9, 3, new Color(0.85f, 0.72f, 0.45f));
            for (int y = 11; y <= 21; y += 4) p.Rect(11, y, 21, y + 1.3f, Ink);
        }

        static void DrawTrophy(Painter p)
        {
            p.Ellipse(16, 10, 9, 8, Gold);                  // the cup
            p.Rect(7, 4, 25, 10, Gold);
            p.Circle(6, 10, 3.5f, DarkGold);                // handles
            p.Circle(26, 10, 3.5f, DarkGold);
            p.Rect(14, 17, 18, 23, DarkGold);               // stem
            p.Rect(9, 23, 23, 28, Gold);                    // base
        }

        static void DrawMap(Painter p)
        {
            // A folded map: three panels, alternately lit, with a route marked on it.
            p.Quad(3, 8, 12, 5, 12, 25, 3, 28, new Color(0.62f, 0.78f, 0.45f));
            p.Quad(12, 5, 21, 8, 21, 28, 12, 25, new Color(0.52f, 0.68f, 0.38f));
            p.Quad(21, 8, 30, 5, 30, 25, 21, 28, new Color(0.62f, 0.78f, 0.45f));
            p.Line(7, 20, 16, 13, 1.5f, new Color(0.7f, 0.2f, 0.15f));
            p.Line(16, 13, 25, 17, 1.5f, new Color(0.7f, 0.2f, 0.15f));
            p.Circle(25, 17, 2f, new Color(0.7f, 0.2f, 0.15f));
        }

        static void DrawHouses(Painter p)
        {
            // Two little houses: the villages overview.
            p.Rect(3, 17, 15, 28, LightWood);
            p.Triangle(1, 18, 17, 18, 9, 9, new Color(0.72f, 0.26f, 0.2f));
            p.Rect(7, 22, 11, 28, Bark);
            p.Rect(16, 13, 29, 28, Parchment);
            p.Triangle(14, 14, 31, 14, 22.5f, 4, new Color(0.72f, 0.26f, 0.2f));
            p.Rect(20.5f, 21, 24.5f, 28, Bark);
        }

        static void DrawHouse(Painter p)
        {
            // One house with a door and a window: the village.
            p.Rect(7, 15, 25, 28, LightWood);
            p.Triangle(4, 16, 28, 16, 16, 5, new Color(0.72f, 0.26f, 0.2f));
            p.Rect(14, 20, 18, 28, Bark);
            p.Rect(9, 18, 12, 21, Parchment);
            p.Rect(20, 18, 23, 21, Parchment);
        }

        static void DrawLedger(Painter p)
        {
            // An open ledger with ruled pages: the Account Manager.
            p.Rect(3, 7, 15.5f, 26, Parchment);
            p.Rect(16.5f, 7, 29, 26, Parchment);
            p.Rect(15.5f, 6, 16.5f, 27, Bark);
            for (int y = 11; y <= 22; y += 4)
            {
                p.Line(5, y, 13.5f, y, 1f, Ink);
                p.Line(18.5f, y, 27, y, 1f, Ink);
            }
        }

        static void DrawSack(Painter p)
        {
            // A bulging sack of plunder, tied at the neck: the Loot Assistant.
            var burlap = new Color(0.76f, 0.6f, 0.38f);
            p.Ellipse(16, 20, 11, 9, burlap);
            p.Quad(11, 13, 21, 13, 19, 7, 13, 7, burlap);
            p.Triangle(10, 5, 16, 8, 13, 9, burlap);
            p.Triangle(22, 5, 16, 8, 19, 9, burlap);
            p.Rect(12, 10, 20, 12, Bark);                       // the cord
            p.Circle(16, 21, 3.5f, Gold);                       // a coin showing on its side
            p.Circle(16, 21, 2f, DarkGold);
        }

        static void DrawLetter(Painter p)
        {
            // A folded letter with a red wax seal.
            p.Rect(4, 8, 28, 25, Parchment);
            p.Line(4, 9, 16, 18, 1.5f, Ink);
            p.Line(28, 9, 16, 18, 1.5f, Ink);
            p.Circle(16, 18, 3.5f, new Color(0.75f, 0.12f, 0.1f));
        }

        static void DrawBanner(Painter p)
        {
            // A tribe's banner: a pole with a swallow-tailed flag bearing a gold star.
            p.Rect(6, 3, 8.5f, 29, Bark);
            p.Rect(8.5f, 5, 26, 19, new Color(0.25f, 0.4f, 0.8f));
            p.Triangle(26.5f, 4.5f, 26.5f, 19.5f, 21, 12, default, erase: true); // the swallow-tail notch
            p.Circle(15, 12, 3.5f, Gold);
        }

        // ---------------------------------------------------------------- units

        static void DrawUnit(Painter p, UnitType type)
        {
            switch (type)
            {
                case UnitType.Spearman:
                    p.Line(6, 28, 23, 9, 2.5f, Bark);
                    p.Triangle(21, 7, 28, 3, 25, 11, Steel);
                    break;
                case UnitType.Swordsman:
                    p.Line(16, 3, 16, 20, 3.5f, Steel);
                    p.Triangle(14, 4, 18, 4, 16, 1, Steel);
                    p.Rect(9, 20, 23, 23, DarkGold);
                    p.Rect(14.5f, 23, 17.5f, 28, Bark);
                    p.Circle(16, 29, 2f, DarkGold);
                    break;
                case UnitType.Axeman:
                    // Like the spear: a shaft from bottom left to top right, the head at the top. The head flares
                    // out to the lower right, dark iron with a bright cutting edge.
                    // The handle leans about 27 degrees off upright; the head sits square to it.
                    p.Line(8, 30, 19.5f, 7, 3.2f, HandleWood);
                    p.Line(8, 30, 9.8f, 26.4f, 3.6f, Bark);                                    // the grip end
                    p.Quad(18.5f, 4.5f, 29.5f, 9.5f, 24.5f, 20.5f, 15.5f, 12.5f, AxeHead);    // the head...
                    p.Quad(27.3f, 8.5f, 30.5f, 10, 25.5f, 21, 22.5f, 19.5f, Steel);            // ...its bright edge
                    p.Line(19.5f, 8.5f, 24f, 10.8f, 1.2f, new Color(0.46f, 0.57f, 0.63f));    // a glint on the iron
                    break;
                case UnitType.Archer:
                    p.Arc(12, 16, 12, -1.2f, 1.2f, 2.5f, Bark); // the bow
                    p.Line(16, 5, 16, 27, 0.8f, Parchment);      // its string
                    p.Line(4, 16, 27, 16, 1.5f, LightWood);       // an arrow
                    p.Triangle(27, 13, 27, 19, 31, 16, Steel);
                    p.Triangle(3, 13, 7, 16, 3, 19, new Color(0.85f, 0.2f, 0.2f));
                    break;
                case UnitType.Scout:
                    HorseHead(p, new Color(0.96f, 0.96f, 0.94f), new Color(0.78f, 0.78f, 0.8f)); // a white horse
                    break;
                case UnitType.MountedArcher:
                    HorseHead(p, new Color(0.74f, 0.56f, 0.34f), DarkHorse);
                    p.Line(3, 30, 26, 7, 1.8f, Parchment);                      // an arrow across it...
                    p.Triangle(24, 5, 30, 2, 28, 9, Steel);                      // ...its head
                    p.Triangle(2, 27, 6, 31, 1, 31, new Color(0.85f, 0.2f, 0.2f)); // ...and fletching
                    p.Arc(-2, 16, 13, -1.1f, 1.1f, 3f, new Color(0.35f, 0.2f, 0.08f)); // and a bow at its side
                    break;
                case UnitType.LightCavalry:
                    HorseHead(p, Horse, DarkHorse);
                    break;
                case UnitType.HeavyCavalry:
                    HorseHead(p, new Color(0.35f, 0.3f, 0.28f), new Color(0.2f, 0.17f, 0.15f));
                    p.Quad(15, 7, 26, 9, 27, 15, 16, 14, Steel);  // a steel face-plate
                    break;
                case UnitType.Ram:
                    p.Rect(3, 12, 25, 19, Bark);
                    p.Rect(25, 11, 30, 20, DarkSteel);
                    p.Circle(9, 23, 5f, DarkHorse);
                    p.Circle(9, 23, 2f, LightWood);
                    p.Circle(21, 23, 5f, DarkHorse);
                    p.Circle(21, 23, 2f, LightWood);
                    break;
                case UnitType.Catapult:
                    p.Triangle(4, 28, 28, 28, 16, 12, Bark);
                    p.Triangle(9, 28, 23, 28, 16, 18, new Color(0, 0, 0, 0), erase: true);
                    p.Line(16, 18, 26, 4, 2.2f, LightWood);
                    p.Circle(26, 5, 3.5f, new Color(0.5f, 0.5f, 0.52f));
                    break;
                case UnitType.Nobleman:
                    p.Rect(6, 19, 26, 26, Gold);                 // a crown
                    p.Triangle(6, 20, 11, 20, 6, 8, Gold);
                    p.Triangle(12, 20, 20, 20, 16, 5, Gold);
                    p.Triangle(21, 20, 26, 20, 26, 8, Gold);
                    p.Circle(16, 22.5f, 2f, new Color(0.8f, 0.15f, 0.2f));
                    p.Circle(10, 22.5f, 1.5f, new Color(0.2f, 0.4f, 0.85f));
                    p.Circle(22, 22.5f, 1.5f, new Color(0.2f, 0.4f, 0.85f));
                    break;
            }
        }

        /// <summary>A horse's head in profile, facing right.</summary>
        static void HorseHead(Painter p, Color coat, Color mane)
        {
            p.Quad(6, 29, 16, 29, 18, 10, 10, 8, coat);      // neck
            p.Ellipse(19, 12, 9, 5.5f, coat);                // head
            p.Ellipse(26, 14, 4, 4, coat);                   // muzzle
            p.Triangle(10, 9, 14, 8, 11, 2, coat);           // ear
            p.Line(8, 9, 5, 27, 2.5f, mane);                 // mane
            p.Circle(18, 10, 1.3f, new Color(0.05f, 0.05f, 0.05f));
        }

        // ---------------------------------------------------------------- a tiny painter (y counts down from the top)

        class Painter
        {
            readonly Color[] px = new Color[Size * Size];

            void Set(int x, int y, Color c, bool erase)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size) return;
                px[(Size - 1 - y) * Size + x] = erase ? Color.clear : c;
            }

            /// <summary>Fills every pixel whose center passes the test.</summary>
            void Fill(Func<float, float, bool> inside, Color c, bool erase = false)
            {
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        if (inside(x + 0.5f, y + 0.5f)) Set(x, y, c, erase);
            }

            public void Rect(float x0, float y0, float x1, float y1, Color c) => Fill((x, y) => x >= x0 && x < x1 && y >= y0 && y < y1, c);

            public void Circle(float cx, float cy, float r, Color c) => Ellipse(cx, cy, r, r, c);

            public void Ellipse(float cx, float cy, float rx, float ry, Color c, bool erase = false) =>
                Fill((x, y) => (x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry) <= 1, c, erase);

            public void Line(float x0, float y0, float x1, float y1, float width, Color c) => Fill((x, y) =>
            {
                float dx = x1 - x0, dy = y1 - y0;
                float t = Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy));
                float ex = x0 + t * dx - x, ey = y0 + t * dy - y;
                return ex * ex + ey * ey <= width * width / 4;
            }, c);

            public void Triangle(float ax, float ay, float bx, float by, float cx, float cy, Color c, bool erase = false) => Fill((x, y) =>
            {
                float d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
                float d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
                float d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            }, c, erase);

            public void Quad(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy, Color c)
            {
                Triangle(ax, ay, bx, by, cx, cy, c);
                Triangle(ax, ay, cx, cy, dx, dy, c);
            }

            /// <summary>A thick arc of a circle, between two angles (radians, 0 pointing right).</summary>
            public void Arc(float cx, float cy, float r, float from, float to, float width, Color c) => Fill((x, y) =>
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float a = Mathf.Atan2(y - cy, x - cx);
                return Mathf.Abs(d - r) <= width / 2 && a >= from && a <= to;
            }, c);

            /// <summary>A dark rim round everything drawn, one pixel wide.</summary>
            public void Outline(Color c)
            {
                var copy = (Color[])px.Clone();
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        if (copy[y * Size + x].a > 0.5f) continue;
                        bool edge = false;
                        for (int oy = -1; oy <= 1 && !edge; oy++)
                            for (int ox = -1; ox <= 1 && !edge; ox++)
                            {
                                int nx = x + ox, ny = y + oy;
                                if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && copy[ny * Size + nx].a > 0.5f) edge = true;
                            }
                        if (edge) px[y * Size + x] = c;
                    }
            }

            public Texture2D ToTexture(string name)
            {
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Icon " + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels(px);
                tex.Apply(false, true);
                return tex;
            }
        }
    }
}
