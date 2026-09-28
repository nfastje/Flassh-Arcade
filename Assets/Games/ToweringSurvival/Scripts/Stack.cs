using System.Collections.Generic;
using UnityEngine;

namespace ToweringSurvival
{
    /// <summary>
    /// The well: a grid of landed blocks plus the Tetris-style pieces falling into it. The well is
    /// <see cref="Width"/> columns wide. Pieces always fall fully inside it; the player wraps around its edges.
    /// Rows count up from 0; everything below the lowest kept row is treated as solid ground.
    /// </summary>
    public class Stack : MonoBehaviour
    {
        public const int Width = 12;

        static readonly Vector2Int[][] Shapes =
        {
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) }, // I
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }, // O
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(1, 1) }, // T
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(2, 1) }, // S
            new[] { new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }, // Z
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(0, 1) }, // J
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(2, 1) }, // L
        };

        static readonly Color[] ShapeColors =
        {
            new Color(0.3f, 0.85f, 0.95f), new Color(0.95f, 0.85f, 0.25f), new Color(0.7f, 0.4f, 0.9f),
            new Color(0.4f, 0.85f, 0.35f), new Color(0.95f, 0.35f, 0.35f), new Color(0.3f, 0.45f, 0.95f),
            new Color(0.95f, 0.6f, 0.2f),
        };

        class Piece
        {
            public Vector2Int[] Cells;
            public int Column;
            public float Y; // bottom of the piece's lowest row
            public SpriteRenderer[] Blocks;
        }

        readonly Dictionary<int, SpriteRenderer[]> rows = new Dictionary<int, SpriteRenderer[]>();
        readonly List<Piece> falling = new List<Piece>();
        int solidBelow;  // rows below this are solid ground (the floor, or rows culled under the lava)
        float spawnTimer;

        /// <summary>The highest row with a landed block in it, or -1 if the well is empty.</summary>
        public int TopRow { get; private set; } = -1;

        public static int Wrap(int column) => ((column % Width) + Width) % Width;

        /// <summary>Horizontal distance from a to b, taking the shorter way round the wrapped well.</summary>
        public static float WrapDelta(float a, float b) => Mathf.Repeat(b - a + Width / 2f, Width) - Width / 2f;

        public bool Solid(int column, int row)
        {
            if (row < solidBelow) return true;
            return rows.TryGetValue(row, out var r) && r[Wrap(column)] != null;
        }

        public void Clear()
        {
            foreach (var r in rows.Values)
                foreach (var b in r)
                    if (b != null) Destroy(b.gameObject);
            rows.Clear();
            foreach (var p in falling)
                foreach (var b in p.Blocks) Destroy(b.gameObject);
            falling.Clear();
            solidBelow = 0;
            TopRow = -1;
            spawnTimer = 0f;
        }

        /// <param name="spawnAbove">Pieces appear at this height or above, so they fall in from off-screen.</param>
        public void Tick(float dt, float fallSpeed, float spawnInterval, float spawnAbove)
        {
            spawnTimer -= dt;
            if (spawnTimer <= 0f)
            {
                spawnTimer = spawnInterval;
                float above = Mathf.Max(spawnAbove, TopRow + 4);
                foreach (var p in falling)
                    foreach (var c in p.Cells) above = Mathf.Max(above, p.Y + c.y + 1f); // never overlap a piece still in the air
                Spawn(above);
            }

            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var p = falling[i];
                p.Y -= fallSpeed * dt;

                // If any block has moved into a solid cell, the piece lands just above the highest such cell.
                float landY = float.NegativeInfinity;
                foreach (var c in p.Cells)
                {
                    int row = Mathf.FloorToInt(p.Y + c.y);
                    if (Solid(p.Column + c.x, row)) landY = Mathf.Max(landY, row + 1 - c.y);
                }

                if (float.IsNegativeInfinity(landY))
                {
                    Place(p);
                    continue;
                }

                p.Y = landY;
                Lock(p);
                falling.RemoveAt(i);
            }
        }

        void Spawn(float y)
        {
            int shape = Random.Range(0, Shapes.Length);
            var cells = Rotate(Shapes[shape], Random.Range(0, 4));
            int pieceWidth = 0;
            foreach (var c in cells) pieceWidth = Mathf.Max(pieceWidth, c.x + 1);
            int column = Random.Range(0, Width - pieceWidth + 1); // fully inside the well, never across the edge
            var p = new Piece { Cells = cells, Column = column, Y = Mathf.Ceil(y), Blocks = new SpriteRenderer[cells.Length] };
            for (int i = 0; i < cells.Length; i++)
            {
                var go = new GameObject("Block");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = TowerArt.PieceBlock(ConnectionKey(cells, cells[i]));
                sr.sharedMaterial = TowerArt.Material;
                sr.color = ShapeColors[shape];
                sr.sortingOrder = 10;
                p.Blocks[i] = sr;
            }
            Place(p);
            falling.Add(p);
        }

        /// <summary>Which of a block's neighbours are part of the same piece, so it can be drawn joined to them.</summary>
        static int ConnectionKey(Vector2Int[] cells, Vector2Int cell)
        {
            bool Has(int dx, int dy) => System.Array.IndexOf(cells, cell + new Vector2Int(dx, dy)) >= 0;
            bool up = Has(0, 1), right = Has(1, 0), down = Has(0, -1), left = Has(-1, 0);
            int key = (up ? TowerArt.Up : 0) | (right ? TowerArt.Right : 0) | (down ? TowerArt.Down : 0) | (left ? TowerArt.Left : 0);
            if (up && left && !Has(-1, 1)) key |= TowerArt.InnerUpLeft;
            if (up && right && !Has(1, 1)) key |= TowerArt.InnerUpRight;
            if (down && right && !Has(1, -1)) key |= TowerArt.InnerDownRight;
            if (down && left && !Has(-1, -1)) key |= TowerArt.InnerDownLeft;
            return key;
        }

        /// <summary>Rotates a shape by quarter turns, then shifts it so its lowest, leftmost cell is at (0, 0).</summary>
        static Vector2Int[] Rotate(Vector2Int[] shape, int turns)
        {
            var cells = (Vector2Int[])shape.Clone();
            for (int t = 0; t < turns; t++)
                for (int i = 0; i < cells.Length; i++) cells[i] = new Vector2Int(cells[i].y, -cells[i].x);
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var c in cells)
            {
                minX = Mathf.Min(minX, c.x);
                minY = Mathf.Min(minY, c.y);
            }
            for (int i = 0; i < cells.Length; i++) cells[i] -= new Vector2Int(minX, minY);
            return cells;
        }

        static void Place(Piece p)
        {
            for (int i = 0; i < p.Cells.Length; i++)
            {
                var c = p.Cells[i];
                p.Blocks[i].transform.position = new Vector3(Wrap(p.Column + c.x) + 0.5f, p.Y + c.y + 0.5f, 0f);
            }
        }

        void Lock(Piece p)
        {
            int baseRow = Mathf.RoundToInt(p.Y);
            for (int i = 0; i < p.Cells.Length; i++)
            {
                var c = p.Cells[i];
                int row = baseRow + c.y, column = Wrap(p.Column + c.x);
                if (!rows.TryGetValue(row, out var r)) rows[row] = r = new SpriteRenderer[Width];
                if (r[column] != null) Destroy(r[column].gameObject); // shouldn't happen; never leak a block
                r[column] = p.Blocks[i];
                p.Blocks[i].transform.position = new Vector3(column + 0.5f, row + 0.5f, 0f);
                TopRow = Mathf.Max(TopRow, row);
            }
        }

        /// <summary>
        /// Adds every solid 1 x 1 box (floor, landed blocks and falling blocks) that overlaps the query box to
        /// <paramref name="result"/>. Box x positions are given on the same side of the wrap as the query, so a
        /// query straddling an edge sees the blocks on the far side next to it.
        /// </summary>
        public void CollectSolids(Rect query, List<Rect> result, float skin)
        {
            result.Clear();
            int x0 = Mathf.FloorToInt(query.xMin + skin), x1 = Mathf.FloorToInt(query.xMax - skin);
            int y0 = Mathf.FloorToInt(query.yMin + skin), y1 = Mathf.FloorToInt(query.yMax - skin);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (Solid(x, y)) result.Add(new Rect(x, y, 1f, 1f));

            foreach (var p in falling)
                foreach (var c in p.Cells)
                {
                    float centre = Wrap(p.Column + c.x) + 0.5f;
                    float left = query.center.x + WrapDelta(query.center.x, centre) - 0.5f;
                    var box = new Rect(left, p.Y + c.y, 1f, 1f);
                    if (box.xMin < query.xMax - skin && box.xMax > query.xMin + skin &&
                        box.yMin < query.yMax - skin && box.yMax > query.yMin + skin)
                        result.Add(box);
                }
        }

        /// <summary>Deletes rows below <paramref name="row"/> (long since swallowed by lava); they count as solid from now on.</summary>
        public void CullBelow(int row)
        {
            if (row <= solidBelow) return;
            for (int y = solidBelow; y < row; y++)
            {
                if (!rows.TryGetValue(y, out var r)) continue;
                foreach (var b in r)
                    if (b != null) Destroy(b.gameObject);
                rows.Remove(y);
            }
            solidBelow = row;
        }
    }
}
