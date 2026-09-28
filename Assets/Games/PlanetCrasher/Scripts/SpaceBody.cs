using UnityEngine;

namespace PlanetCrasher
{
    /// <summary>
    /// A round celestial body (the player or anything floating around). Movement is driven by GameManager.
    /// </summary>
    public class SpaceBody : MonoBehaviour
    {
        const float AppearDuration = 0.4f;

        public Vector2 Velocity;
        public bool Dying;
        public float DyingTime;
        public SpaceBody Eater; // what is swallowing this body while Dying
        public int Level;         // stage index this body was spawned as (-1 = pebbles smaller than an asteroid)
        public float SpawnRadius; // growth from eating slows down the further a body grows past this

        public float Radius { get; private set; }
        public float GrowTarget { get; private set; }
        public BodyKind Kind { get; private set; }
        public Color MainColor { get; private set; }

        SpriteRenderer surface, glow;
        bool customGlow;
        float spin, appear;

        public Vector2 Position
        {
            get => transform.position;
            set => transform.position = new Vector3(value.x, value.y, 0f);
        }

        public static SpaceBody Create(string name, BodyKind kind, int variant, float radius, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var body = go.AddComponent<SpaceBody>();
            body.glow = AddLayer(go.transform, "Glow", ProceduralArt.Glow);
            body.surface = AddLayer(go.transform, "Surface", null);
            body.SetKind(kind, variant);
            body.SetRadius(radius);
            return body;
        }

        static SpriteRenderer AddLayer(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = ProceduralArt.SpriteMaterial;
            sr.sprite = sprite;
            return sr;
        }

        public void SetKind(BodyKind kind, int variant)
        {
            Kind = kind;
            surface.sprite = ProceduralArt.Body(kind, variant);
            MainColor = ProceduralArt.BodyColor(kind, variant);

            bool tumbles = kind == BodyKind.Rock || kind == BodyKind.Moon;
            spin = kind == BodyKind.BlackHole ? 60f : tumbles ? Random.Range(-45f, 45f) : 0f;
            surface.transform.localRotation = tumbles ? Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)) : Quaternion.identity;

            if (!customGlow)
            {
                glow.enabled = kind == BodyKind.Star || kind == BodyKind.BlackHole;
                glow.color = new Color(MainColor.r, MainColor.g, MainColor.b, 0.45f);
                glow.transform.localScale = Vector3.one * 2.3f;
            }
        }

        public void SetGlow(Color color, float scale)
        {
            customGlow = true;
            glow.enabled = true;
            glow.color = color;
            glow.transform.localScale = Vector3.one * scale;
        }

        /// <summary>Sets the size immediately.</summary>
        public void SetRadius(float radius)
        {
            Radius = GrowTarget = radius;
            ApplyScale();
        }

        /// <summary>Grows smoothly towards <paramref name="radius"/> over the next few frames.</summary>
        public void Grow(float radius) => GrowTarget = radius;

        /// <summary>Skips the fade-in that new bodies get so they don't pop into view.</summary>
        public void ShowImmediately()
        {
            appear = 1f;
            ApplyScale();
        }

        public void SetVisualScale(float scale) => transform.localScale = Vector3.one * scale;

        public void SetSorting(int order)
        {
            glow.sortingOrder = order - 1;
            surface.sortingOrder = order;
        }

        public void Tick(float dt)
        {
            if (spin != 0f) surface.transform.Rotate(0f, 0f, spin * dt);
            if (appear < 1f) appear = Mathf.Min(1f, appear + dt / AppearDuration);
            if (Radius != GrowTarget) Radius = Mathf.Lerp(Radius, GrowTarget, 1f - Mathf.Exp(-6f * dt));
            ApplyScale();
        }

        void ApplyScale()
        {
            float t = 1f - (1f - appear) * (1f - appear);
            transform.localScale = Vector3.one * (Radius * t);
        }
    }
}
