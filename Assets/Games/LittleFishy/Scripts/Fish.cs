using UnityEngine;

namespace LittleFishy
{
    /// <summary>A fish: tinted body and wagging tail. Always swims horizontally, facing left or right.</summary>
    public class Fish : MonoBehaviour
    {
        const float TailWag = 5f; // degrees either way; kept small so the fish's outline (its hitbox) stays readable

        public Vector2 Velocity;
        public bool Dying;
        public float DyingTime;

        /// <summary>Nose to tail tip, in world units. This is the fish's "size" for eating rules.</summary>
        public float Length { get; private set; }
        public bool FacingRight { get; private set; } = true;
        public Color Color { get; private set; }

        SpriteRenderer body, tail, eye;
        float wagPhase;
        bool bellyUp;

        float Scale => Length / FishArt.FullLength;
        public float HalfLength => FishArt.BodyHalfLength * Scale;
        public float HalfHeight => FishArt.BodyHalfHeight * Scale;

        public Vector2 Position
        {
            get => transform.position;
            set => transform.position = new Vector3(value.x, value.y, 0f);
        }

        public Vector2 Mouth => Position + new Vector2(FacingRight ? HalfLength : -HalfLength, 0f);

        /// <summary>The fish's hitbox is two ovals matching what you see: its body and its tail.</summary>
        public (Vector2 centre, Vector2 radii) BodyOval => (Position, new Vector2(HalfLength, HalfHeight));

        public (Vector2 centre, Vector2 radii) TailOval
        {
            get
            {
                float back = (FishArt.TailAttachX - FishArt.TailLength / 2f) * Scale;
                return (Position + new Vector2(FacingRight ? back : -back, 0f),
                        new Vector2(FishArt.TailLength / 2f, FishArt.TailHalfHeight) * Scale);
            }
        }

        public static Fish Create(string name, Color color, float length, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var fish = go.AddComponent<Fish>();
            fish.tail = AddPart(go.transform, "Tail", FishArt.Tail, new Vector3(FishArt.TailAttachX, 0f, 0f), 1f);
            fish.body = AddPart(go.transform, "Body", FishArt.Body, Vector3.zero, 1f);
            fish.eye = AddPart(go.transform, "Eye", FishArt.Eye, new Vector3(0.58f, 0.07f, 0f), 0.2f);
            fish.wagPhase = Random.Range(0f, 10f);
            fish.SetColor(color);
            fish.SetLength(length);
            return fish;
        }

        static SpriteRenderer AddPart(Transform parent, string name, Sprite sprite, Vector3 position, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = FishArt.Material;
            return sr;
        }

        public void SetColor(Color color)
        {
            Color = color;
            body.color = color;
            tail.color = Color.Lerp(color, Color.black, 0.12f);
        }

        public void SetLength(float length)
        {
            Length = length;
            ApplyScale(length);
        }

        /// <summary>Changes how big the fish looks without changing its size for gameplay (used while being eaten).</summary>
        public void ApplyScale(float visualLength)
        {
            float s = visualLength / FishArt.FullLength;
            transform.localScale = new Vector3(FacingRight ? s : -s, bellyUp ? -s : s, 1f);
        }

        public void Face(bool right)
        {
            if (FacingRight == right) return;
            FacingRight = right;
            ApplyScale(Length);
        }

        public void SetSorting(int order)
        {
            tail.sortingOrder = order;
            body.sortingOrder = order + 1;
            eye.sortingOrder = order + 2;
        }

        /// <summary>Dead fish float belly-up, washed out.</summary>
        public void SetBellyUp()
        {
            bellyUp = true;
            SetColor(Color.Lerp(Color, new Color(0.75f, 0.75f, 0.75f), 0.6f));
            ApplyScale(Length);
        }

        public void Tick(float dt)
        {
            float speed = Mathf.Abs(Velocity.x) / Mathf.Max(Length, 0.1f);
            wagPhase += dt * (bellyUp ? 0f : 5f + speed * 3f);
            tail.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(wagPhase) * TailWag);
        }
    }
}
