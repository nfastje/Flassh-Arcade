using System;
using MedievalWorldConquest.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static MedievalWorldConquest.Ui;

namespace MedievalWorldConquest
{
    /// <summary>
    /// The whole world in a small square: dim terrain, a dot for every village (the player's yellow, barbarians
    /// grey, other lords in their colours), the unsettled wilds beyond the growing circle darker still, and a frame
    /// showing the part of the map in view. Clicking or dragging on it moves the map there.
    /// </summary>
    public class MiniMap
    {
        /// <summary>Size on screen, in reference pixels (1280 x 720).</summary>
        public const float Size = 220f;
        const int PixelsPerField = 2;
        const float RedrawSeconds = 1f;

        public VisualElement Root { get; }

        readonly VisualElement frame;
        readonly Action<Vector2> centreOn;
        Texture2D texture;
        Color32[] terrain, pixels;
        int terrainSeed;
        float nextDraw;
        bool dragging;

        static readonly Color32 Grass = new Color32(74, 98, 52, 255), Forest = new Color32(46, 70, 38, 255);
        static readonly Color32 Hills = new Color32(98, 90, 76, 255), Water = new Color32(40, 66, 106, 255);

        /// <param name="centreOn">Moves the map view to a point, in map fields.</param>
        public MiniMap(Action<Vector2> centreOn)
        {
            this.centreOn = centreOn;
            Root = Element("minimap");
            Root.style.width = Size;
            Root.style.height = Size;
            frame = Element("minimap-frame");
            frame.pickingMode = PickingMode.Ignore;
            Root.Add(frame);

            Root.RegisterCallback<PointerDownEvent>(e =>
            {
                dragging = true;
                Root.CapturePointer(e.pointerId);
                Pick(e.localPosition);
                e.StopPropagation();
            });
            Root.RegisterCallback<PointerMoveEvent>(e => { if (dragging) Pick(e.localPosition); });
            Root.RegisterCallback<PointerUpEvent>(e =>
            {
                dragging = false;
                Root.ReleasePointer(e.pointerId);
            });
            // The texture is made here, so it's freed here too.
            Root.RegisterCallback<DetachFromPanelEvent>(_ => FreeTexture());
        }

        /// <summary>A point on the minimap (from its top left) to map fields (from the map's bottom left).</summary>
        void Pick(Vector2 local)
        {
            float w = Math.Max(1f, Root.layout.width), h = Math.Max(1f, Root.layout.height);
            centreOn(new Vector2(Mathf.Clamp01(local.x / w) * World.MapSize, (1f - Mathf.Clamp01(local.y / h)) * World.MapSize));
        }

        public void Refresh(World world, Rect viewInFields)
        {
            if (texture == null || terrainSeed != world.Settings.Seed) BuildTerrain(world);
            if (Time.unscaledTime >= nextDraw)
            {
                nextDraw = Time.unscaledTime + RedrawSeconds;
                Draw(world);
            }

            // The frame round the part of the map in view.
            float scale = Size / World.MapSize;
            float left = Mathf.Clamp(viewInFields.xMin, 0, World.MapSize), right = Mathf.Clamp(viewInFields.xMax, 0, World.MapSize);
            float bottom = Mathf.Clamp(viewInFields.yMin, 0, World.MapSize), top = Mathf.Clamp(viewInFields.yMax, 0, World.MapSize);
            frame.style.left = left * scale;
            frame.style.width = Math.Max(2f, (right - left) * scale);
            frame.style.top = (World.MapSize - top) * scale;
            frame.style.height = Math.Max(2f, (top - bottom) * scale);
        }

        void BuildTerrain(World world)
        {
            FreeTexture();
            terrainSeed = world.Settings.Seed;
            int size = World.MapSize * PixelsPerField;
            terrain = new Color32[size * size];
            pixels = new Color32[size * size];
            for (int fy = 0; fy < World.MapSize; fy++)
                for (int fx = 0; fx < World.MapSize; fx++)
                {
                    var t = world.TerrainAt(fx, fy);
                    var c = t == TerrainType.Water ? Water : t == TerrainType.Forest ? Forest : t == TerrainType.Hills ? Hills : Grass;
                    for (int py = 0; py < PixelsPerField; py++)
                        for (int px = 0; px < PixelsPerField; px++)
                            terrain[(fy * PixelsPerField + py) * size + fx * PixelsPerField + px] = c;
                }
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MiniMap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Root.style.backgroundImage = new StyleBackground(texture);
            nextDraw = 0f;
        }

        void Draw(World world)
        {
            int size = World.MapSize * PixelsPerField;
            // The wilds beyond the settled circle (and its ragged edge) are darker.
            double centre = size / 2.0, settled = (world.SpawnRadius + World.RingSpread) * PixelsPerField;
            double settled2 = settled * settled;
            for (int y = 0; y < size; y++)
            {
                double dy = y + 0.5 - centre;
                for (int x = 0; x < size; x++)
                {
                    double dx = x + 0.5 - centre;
                    var c = terrain[y * size + x];
                    if (dx * dx + dy * dy > settled2) c = new Color32((byte)(c.r / 2), (byte)(c.g / 2), (byte)(c.b / 2), 255);
                    pixels[y * size + x] = c;
                }
            }

            // Barbarians first, then lords, then the player's own on top.
            var human = world.HumanPlayer;
            for (int pass = 0; pass < 3; pass++)
                foreach (var v in world.Villages)
                {
                    int layer = v.IsBarbarian ? 0 : human != null && v.OwnerId == human.Id ? 2 : 1;
                    if (layer == pass) Dot(v.X, v.Y, MapView.OwnerColor(world, v), pass + 1, size); // lords' and yours bigger
                }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        /// <summary>A square dot centred on a field; <paramref name="radius"/> 1 is 3 x 3 pixels (1.5 fields).</summary>
        void Dot(int fx, int fy, Color color, int radius, int size)
        {
            Color32 c = color;
            int cx = fx * PixelsPerField + PixelsPerField / 2, cy = fy * PixelsPerField + PixelsPerField / 2;
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                    if (x >= 0 && y >= 0 && x < size && y < size) pixels[y * size + x] = c;
        }

        void FreeTexture()
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
        }
    }
}
