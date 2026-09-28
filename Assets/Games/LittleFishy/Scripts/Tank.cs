using System.Collections.Generic;
using UnityEngine;

namespace LittleFishy
{
    /// <summary>The scenery: water, sand, rocks, swaying seaweed and rising bubbles. Refits itself to the screen.</summary>
    public class Tank : MonoBehaviour
    {
        const int MaxBubbles = 80;

        class Weed { public Transform T; public float X, Phase, Speed; }
        class Bubble { public Transform T; public SpriteRenderer Sr; public Vector2 Vel; public float Wobble; }

        Camera cam;
        float waterTop, sandTop;
        SpriteRenderer water, sand, leftPanel, rightPanel;
        readonly List<Weed> weeds = new List<Weed>();
        readonly List<(Transform t, float x)> rocks = new List<(Transform, float)>();
        readonly List<Bubble> bubbles = new List<Bubble>();
        float ambientTimer;

        public void Init(Camera camera, float waterTop, float sandTop)
        {
            cam = camera;
            this.waterTop = waterTop;
            this.sandTop = sandTop;

            water = AddSprite("Water", FishArt.Water, -100);
            sand = AddSprite("Sand", FishArt.Sand, -50);

            // Dark panels either side of the playing field, in front of everything so fish slide out from behind them.
            var panelColor = new Color(0.01f, 0.05f, 0.1f);
            leftPanel = AddSprite("Left Panel", FishArt.Water, 700);
            rightPanel = AddSprite("Right Panel", FishArt.Water, 700);
            leftPanel.color = rightPanel.color = panelColor;

            // Positions are fractions of the screen width so they spread out at any aspect ratio.
            for (int i = 0; i < 9; i++)
            {
                var sr = AddSprite("Seaweed", FishArt.Seaweed, -60);
                float height = Random.Range(0.8f, 1.6f);
                sr.transform.localScale = new Vector3(Random.Range(0.8f, 1.3f), height, 1f);
                sr.color = Color.Lerp(Color.white, new Color(0.7f, 1f, 0.8f), Random.value);
                weeds.Add(new Weed { T = sr.transform, X = (i + Random.Range(0.1f, 0.9f)) / 9f, Phase = Random.Range(0f, 10f), Speed = Random.Range(0.6f, 1.1f) });
            }
            for (int i = 0; i < 4; i++)
            {
                var sr = AddSprite("Rock", FishArt.Rock, -45);
                sr.transform.localScale = Vector3.one * Random.Range(0.5f, 1.1f);
                rocks.Add((sr.transform, Random.value));
            }
        }

        SpriteRenderer AddSprite(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = FishArt.Material;
            sr.sortingOrder = order;
            return sr;
        }

        float HalfWidth => cam.orthographicSize * cam.aspect;

        /// <param name="fieldHalfWidth">Half the playing field's width; the rest of the screen is covered by side panels.</param>
        public void Tick(float dt, float fieldHalfWidth)
        {
            float halfW = HalfWidth, halfH = cam.orthographicSize;
            Vector3 c = cam.transform.position;

            float panelWidth = halfW - fieldHalfWidth + 0.5f;
            var panelScale = new Vector3(panelWidth / 4f, halfH * 2f / 128f + 0.1f, 1f); // the water sprite is 4 x 128 units
            leftPanel.transform.localScale = rightPanel.transform.localScale = panelScale;
            leftPanel.transform.position = new Vector3(c.x - fieldHalfWidth - panelWidth / 2f, c.y, 0f);
            rightPanel.transform.position = new Vector3(c.x + fieldHalfWidth + panelWidth / 2f, c.y, 0f);

            water.transform.position = new Vector3(c.x, c.y, 0f);
            water.transform.localScale = new Vector3((halfW * 2f + 1f) / 4f, halfH * 2f / 128f, 1f);

            float sandHeight = sandTop + 0.35f - (c.y - halfH);
            sand.transform.position = new Vector3(c.x, c.y - halfH, 0f);
            sand.transform.localScale = new Vector3((halfW * 2f + 1f) / 8f, sandHeight, 1f);

            float t = Time.time;
            foreach (var w in weeds)
            {
                w.T.position = new Vector3(Mathf.Lerp(-fieldHalfWidth, fieldHalfWidth, w.X), sandTop - 0.15f, 0f);
                w.T.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * w.Speed + w.Phase) * 7f);
            }
            foreach (var (rt, x) in rocks)
                rt.position = new Vector3(Mathf.Lerp(-fieldHalfWidth, fieldHalfWidth, x), sandTop - 0.1f, 0f);

            ambientTimer -= dt;
            if (ambientTimer <= 0f)
            {
                ambientTimer = Random.Range(0.15f, 0.5f);
                var src = weeds.Count > 0 ? weeds[Random.Range(0, weeds.Count)].T.position : new Vector3(0f, sandTop, 0f);
                Burst(new Vector2(src.x + Random.Range(-0.2f, 0.2f), sandTop), 1, Random.Range(0.06f, 0.14f));
            }

            foreach (var b in bubbles)
            {
                if (!b.T.gameObject.activeSelf) continue;
                b.Wobble += dt * 4f;
                b.T.position += (Vector3)((b.Vel + new Vector2(Mathf.Sin(b.Wobble) * 0.15f, 0f)) * dt);
                b.Vel.y = Mathf.MoveTowards(b.Vel.y, 1.1f, dt * 2f);
                b.Vel.x = Mathf.MoveTowards(b.Vel.x, 0f, dt * 2f);
                if (b.T.position.y > waterTop) b.T.gameObject.SetActive(false);
            }
        }

        /// <summary>Releases a puff of bubbles, e.g. from a fish's mouth.</summary>
        public void Burst(Vector2 pos, int count, float size)
        {
            for (int i = 0; i < count; i++)
            {
                var b = GetBubble();
                if (b == null) return;
                b.T.position = pos + Random.insideUnitCircle * size;
                b.T.localScale = Vector3.one * size * Random.Range(0.6f, 1.2f);
                b.Vel = new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.3f, 1f)) * (count > 1 ? 1.5f : 0.5f);
                b.Wobble = Random.Range(0f, 6f);
                b.Sr.color = new Color(1f, 1f, 1f, Random.Range(0.5f, 0.9f));
                b.T.gameObject.SetActive(true);
            }
        }

        Bubble GetBubble()
        {
            foreach (var b in bubbles)
                if (!b.T.gameObject.activeSelf) return b;
            if (bubbles.Count >= MaxBubbles) return null;
            var sr = AddSprite("Bubble", FishArt.Bubble, 600); // in front of every fish
            var created = new Bubble { T = sr.transform, Sr = sr };
            bubbles.Add(created);
            return created;
        }
    }
}
