using System.Collections.Generic;
using UnityEngine;

namespace ToweringSurvival
{
    /// <summary>
    /// The player: an upright bar of soap that runs, jumps, slides down walls and wall-jumps off the sides of
    /// blocks, including blocks that are still falling. Position is the centre of its bottom edge. It wraps
    /// around the well's left and right edges.
    /// </summary>
    public class Climber : MonoBehaviour
    {
        // Matches the soap sprite exactly, so what you see is the hitbox.
        public const float HalfWidth = 0.275f;
        public const float Height = 0.88f;

        const float RunSpeed = 6f;
        const float AirSpeed = 3.8f;            // slower sideways in the air, so jumps go up more than across
        const float GroundAccel = 60f;
        const float AirAccel = 40f;             // responsive enough to line up over a one-block gap
        const float Gravity = 32f;
        const float MaxFallSpeed = 18f;
        const float JumpSpeed = 12.5f;          // about 2.4 blocks high
        const float JumpCutFactor = 0.5f;       // releasing jump early cuts the rise short
        // Wall jumps. Holding towards the wall climbs it: a mostly vertical kick you can steer straight back from,
        // so repeated wall jumps gain height on a single wall. Otherwise you're kicked well clear of it.
        const float WallJumpUp = 12f;
        const float WallJumpAway = 6f;
        const float WallJumpLock = 0.15f;       // after a kick-away jump, steering is ignored briefly so the kick carries you away
        const float WallClimbUp = 13.5f;        // about 2.8 blocks high
        const float WallClimbAway = 2f;
        const float WallClimbLock = 0.05f;
        const float WallSlideSpeed = 2f;        // falling while touching a wall slows to this, no need to hold towards it
        const float WallReach = 0.1f;           // how close a wall must be to slide down or jump off it
        const float WallCoyoteTime = 0.12f;     // you can still wall-jump just after sliding off a wall
        const float CoyoteTime = 0.1f;          // you can still jump just after running off a ledge
        const float JumpBuffer = 0.12f;         // a jump pressed just before landing still counts
        const float Skin = 0.001f;

        public Vector2 Velocity;
        public bool Grounded { get; private set; }

        /// <summary>Whether the last <see cref="Tick"/> jumped off the ground, or off a wall (for their sounds).</summary>
        public bool Jumped { get; private set; }
        public bool WallJumped { get; private set; }

        // The soap is drawn three times, one well-width apart. The opaque panels outside the well hide the extra
        // copies, so when you straddle an edge, half of you shows on each side of the screen.
        SpriteRenderer body, wrapLeft, wrapRight;
        float sinceGrounded = 1f, sinceJumpPressed = 1f, sinceOnWall = 1f, sinceWallJump = 1f;
        float wallJumpLock;

        // Crush death: flatten, hold a moment, then pop into a burst of soap bubbles.
        const float SquishTime = 0.12f, SquishHold = 0.25f, PopTime = 0.12f, BubbleLife = 0.7f;
        static readonly Vector2 SquishedScale = new Vector2(1.5f, 0.25f);
        class PopBubble { public SpriteRenderer Sr; public Vector2 Velocity; public float Size; }
        readonly List<PopBubble> popBubbles = new List<PopBubble>();
        float deathTime = -1f;
        int lastWallSide; // -1 = wall on the left, 1 = on the right
        readonly List<Rect> hits = new List<Rect>();

        public Vector2 Position
        {
            get => transform.position;
            set => transform.position = new Vector3(value.x, value.y, 0f);
        }

        Rect BoxAt(Vector2 feet) => new Rect(feet.x - HalfWidth, feet.y, HalfWidth * 2f, Height);

        public static Climber Create(Transform parent)
        {
            var go = new GameObject("Climber");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Climber>();
            c.body = AddSprite(go.transform, "Body");
            c.wrapLeft = AddSprite(go.transform, "Wrap Copy Left");
            c.wrapRight = AddSprite(go.transform, "Wrap Copy Right");
            c.wrapLeft.transform.localPosition = new Vector3(-Stack.Width, 0f, 0f);
            c.wrapRight.transform.localPosition = new Vector3(Stack.Width, 0f, 0f);
            return c;
        }

        static SpriteRenderer AddSprite(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = TowerArt.Soap;
            sr.sharedMaterial = TowerArt.Material;
            sr.sortingOrder = 50;
            return sr;
        }

        public void ResetAt(Vector2 feet)
        {
            Position = feet;
            Velocity = Vector2.zero;
            sinceGrounded = sinceJumpPressed = sinceOnWall = sinceWallJump = 1f;
            deathTime = -1f;
            SetSpriteScale(Vector2.one);
            SetTint(Color.white);
            foreach (var b in popBubbles) b.Sr.enabled = false;
        }

        void SetSpriteScale(Vector2 scale)
        {
            // Scale each copy rather than the root, so the wrap copies stay exactly one well-width away.
            var s = new Vector3(scale.x, scale.y, 1f);
            body.transform.localScale = wrapLeft.transform.localScale = wrapRight.transform.localScale = s;
        }

        /// <summary>Starts the crushed animation: squish flat, then pop. Advance it with <see cref="AnimateDeath"/>.</summary>
        public void Crush() => deathTime = 0f;

        public void AnimateDeath(float dt)
        {
            if (deathTime < 0f) return;
            float before = deathTime;
            deathTime += dt;

            if (deathTime < SquishTime)
            {
                SetSpriteScale(Vector2.Lerp(Vector2.one, SquishedScale, deathTime / SquishTime));
            }
            else if (deathTime < SquishTime + SquishHold)
            {
                float wobble = 1f + 0.06f * Mathf.Sin((deathTime - SquishTime) * 40f);
                SetSpriteScale(new Vector2(SquishedScale.x * wobble, SquishedScale.y / wobble));
            }
            else
            {
                if (before < SquishTime + SquishHold) ReleaseBubbles();
                float k = Mathf.Clamp01((deathTime - SquishTime - SquishHold) / PopTime);
                SetSpriteScale(SquishedScale * (1f + 0.6f * k));
                SetTint(new Color(1f, 1f, 1f, 1f - k));
            }

            foreach (var b in popBubbles)
            {
                if (!b.Sr.enabled) continue;
                float age = deathTime - SquishTime - SquishHold;
                if (age > BubbleLife)
                {
                    b.Sr.enabled = false;
                    continue;
                }
                b.Velocity *= Mathf.Exp(-3f * dt);
                b.Velocity.y += 1.5f * dt; // bubbles drift upwards
                b.Sr.transform.position += (Vector3)(b.Velocity * dt);
                b.Sr.transform.localScale = Vector3.one * b.Size * (1f + age);
                b.Sr.color = new Color(1f, 1f, 1f, 1f - age / BubbleLife);
            }
        }

        void ReleaseBubbles()
        {
            const int count = 12;
            Vector2 centre = Position + new Vector2(0f, Height * SquishedScale.y * 0.5f);
            for (int i = 0; i < count; i++)
            {
                if (i >= popBubbles.Count)
                {
                    var go = new GameObject("Pop Bubble");
                    go.transform.SetParent(transform, false);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = TowerArt.SoapBubble;
                    sr.sharedMaterial = TowerArt.Material;
                    sr.sortingOrder = 55;
                    popBubbles.Add(new PopBubble { Sr = sr });
                }
                var b = popBubbles[i];
                float angle = (i + Random.value * 0.5f) / count * Mathf.PI * 2f;
                b.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f + 0.4f) * Random.Range(1.5f, 3.5f);
                b.Size = Random.Range(0.12f, 0.3f);
                b.Sr.transform.position = centre + Random.insideUnitCircle * 0.2f;
                b.Sr.enabled = true;
            }
        }

        public void SetTint(Color color) => body.color = wrapLeft.color = wrapRight.color = color;

        public void Tick(float dt, Stack stack, float moveInput, bool jumpPressed, bool jumpHeld)
        {
            sinceGrounded += dt;
            sinceOnWall += dt;
            sinceWallJump += dt;
            sinceJumpPressed = jumpPressed ? 0f : sinceJumpPressed + dt;
            Jumped = WallJumped = false;

            // Horizontal: accelerate towards the input direction; slower and floatier in the air.
            // Right after a wall jump, input is ignored so the kick away isn't cancelled.
            if (sinceWallJump >= wallJumpLock)
            {
                float target = moveInput * (Grounded ? RunSpeed : AirSpeed);
                float accel = Grounded ? GroundAccel : AirAccel;
                Velocity.x = Mathf.MoveTowards(Velocity.x, target, accel * dt);
            }

            bool wallLeft = !Grounded && Blocked(stack, Position + Vector2.left * WallReach);
            bool wallRight = !Grounded && Blocked(stack, Position + Vector2.right * WallReach);
            if (wallLeft || wallRight)
            {
                sinceOnWall = 0f;
                lastWallSide = wallLeft ? -1 : 1;
            }

            // Jumping: from the ground (or just after leaving it), or off a wall (or just after leaving one).
            if (sinceJumpPressed < JumpBuffer)
            {
                if (Grounded || sinceGrounded < CoyoteTime)
                {
                    Velocity.y = JumpSpeed;
                    Velocity.x = Mathf.Clamp(Velocity.x, -AirSpeed, AirSpeed); // take off mostly upwards
                    sinceJumpPressed = sinceGrounded = 1f;
                    Jumped = true;
                }
                else if (sinceOnWall < WallCoyoteTime)
                {
                    bool climbing = moveInput * lastWallSide > 0f; // holding towards the wall
                    Velocity.y = climbing ? WallClimbUp : WallJumpUp;
                    Velocity.x = -lastWallSide * (climbing ? WallClimbAway : WallJumpAway);
                    wallJumpLock = climbing ? WallClimbLock : WallJumpLock;
                    sinceJumpPressed = sinceOnWall = 1f;
                    sinceWallJump = 0f;
                    WallJumped = true;
                }
            }
            if (!jumpHeld && Velocity.y > 0f) Velocity.y *= Mathf.Pow(JumpCutFactor, dt * 20f);

            // Touching a wall while falling slows you to a slide.
            bool sliding = (wallLeft || wallRight) && Velocity.y <= 0f;
            Velocity.y = Mathf.Max(Velocity.y - Gravity * dt, sliding ? -WallSlideSpeed : -MaxFallSpeed);

            MoveX(stack, Velocity.x * dt);
            MoveY(stack, Velocity.y * dt);

            // Wrap around the well.
            var pos = Position;
            pos.x = Mathf.Repeat(pos.x, Stack.Width);
            Position = pos;
        }

        /// <summary>
        /// Call after the falling pieces have moved. A piece that came down onto the climber pushes it down with it;
        /// if there's something solid underneath to be pushed into, the climber is crushed. Returns true if crushed.
        /// </summary>
        public bool ResolveFallingBlocks(Stack stack)
        {
            stack.CollectSolids(BoxAt(Position), hits, Skin);
            if (hits.Count == 0) return false;

            // Pieces fall onto us from above, except that a piece snaps up slightly when it lands, which can nudge
            // our feet if we're riding it. Step up onto anything overlapping only our feet; get pushed down by the rest.
            float lift = float.NegativeInfinity, press = float.PositiveInfinity;
            foreach (var h in hits)
            {
                if (h.yMax - Position.y < 0.5f) lift = Mathf.Max(lift, h.yMax);
                else press = Mathf.Min(press, h.yMin);
            }
            var pushed = Position;
            if (!float.IsNegativeInfinity(lift)) pushed.y = lift;
            if (!float.IsPositiveInfinity(press)) pushed.y = press - Height - Skin;

            stack.CollectSolids(BoxAt(pushed), hits, Skin);
            if (hits.Count > 0) return true; // pinned between the piece and whatever is below

            Position = pushed;
            Velocity.y = Mathf.Min(Velocity.y, 0f);
            return false;
        }

        bool Blocked(Stack stack, Vector2 feet)
        {
            stack.CollectSolids(BoxAt(feet), hits, Skin);
            return hits.Count > 0;
        }

        void MoveX(Stack stack, float dx)
        {
            if (dx == 0f) return;
            var pos = Position + new Vector2(dx, 0f);
            stack.CollectSolids(BoxAt(pos), hits, Skin);
            if (hits.Count > 0)
            {
                // Stop flush against the nearest box we ran into.
                if (dx > 0f)
                {
                    float wall = float.PositiveInfinity;
                    foreach (var h in hits) wall = Mathf.Min(wall, h.xMin);
                    pos.x = wall - HalfWidth - Skin;
                }
                else
                {
                    float wall = float.NegativeInfinity;
                    foreach (var h in hits) wall = Mathf.Max(wall, h.xMax);
                    pos.x = wall + HalfWidth + Skin;
                }
                Velocity.x = 0f;
            }
            Position = pos;
        }

        void MoveY(Stack stack, float dy)
        {
            var pos = Position + new Vector2(0f, dy);
            Grounded = false;
            stack.CollectSolids(BoxAt(pos), hits, Skin);
            if (hits.Count > 0)
            {
                if (dy <= 0f)
                {
                    // Land on the highest surface under us (a landed or still-falling block).
                    float floor = float.NegativeInfinity;
                    foreach (var h in hits) floor = Mathf.Max(floor, h.yMax);
                    pos.y = floor;
                    Grounded = true;
                    sinceGrounded = 0f;
                }
                else
                {
                    float ceiling = float.PositiveInfinity;
                    foreach (var h in hits) ceiling = Mathf.Min(ceiling, h.yMin);
                    pos.y = ceiling - Height - Skin;
                }
                Velocity.y = 0f;
            }
            Position = pos;
        }
    }
}
