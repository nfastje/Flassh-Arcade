using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FlasshArcade;

namespace PlanetCrasher
{
    /// <summary>
    /// Runs the whole game: spawning, physics, crashes, growth, camera and HUD.
    /// Crash into anything smaller than you to absorb it; touching anything bigger is instant death, and it pulls you in with gravity.
    /// Everything else eats everything smaller than itself too. Grow into a black hole and swallow the whole arena to win.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        enum State { Title, Playing, Paused, GameOver, Won }

        struct Stage
        {
            public readonly string Name;
            public readonly float Radius;
            public readonly BodyKind Kind;
            public Stage(string name, float radius, BodyKind kind) { Name = name; Radius = radius; Kind = kind; }
        }

        static readonly Stage[] Stages =
        {
            new Stage("Asteroid", 0.5f, BodyKind.Rock),
            new Stage("Moon", 1.5f, BodyKind.Moon),
            new Stage("Planet", 5f, BodyKind.Planet),
            new Stage("Gas Giant", 15f, BodyKind.GasGiant),
            new Stage("Star", 45f, BodyKind.Star),
            new Stage("Black Hole", 135f, BodyKind.BlackHole),
        };

        const float ArenaRadius = 500f;       // the black hole wins when it is this big; must fit ten stars with room to move
        const float BodyMaxRadius = 120f;     // nothing but the player can grow past this, so only the player becomes a black hole
        const float ViewPerRadius = 10f;      // camera orthographic size / player radius
        const float MaxViewSize = ArenaRadius * 1.1f;
        const float Thrust = 9f;              // acceleration, in player radii per second²
        const float Drag = 0.8f;
        const float GrowthPerArea = 0.5f;     // fraction of an eaten body's area the player gains
        const float BodyGrowthPerArea = 0.3f; // same, when a body eats another body
        const float BodyGrowthDecay = 2f;     // body growth shrinks by e^(-decay * (radius/spawnRadius - 1)), preventing runaway giants
        const float PlusTwoGrowthScale = 0.3f; // bodies two levels above the player grow this much slower

        // Gravity is deliberately strong for now; scale these back once it feels right.
        const float GravityStrength = 8f;     // big body -> player, at the big body's surface, in player radii per second²
        const float GravityRange = 10f;       // in radii of the pulling body
        const float AttractStrength = 6f;     // player -> smaller bodies
        const float AttractRange = 7f;        // in player radii
        const float BodyGravityStrength = 0.8f; // body -> smaller body, in the bigger body's radii per second²
        const float BodyGravityRange = 4f;    // in radii of the pulling body
        const float BlackHolePull = 3f;       // black hole -> everything in the arena (falls off with 1/distance)
        const float BlackHoleGrowthRate = 3f; // radius per second a black hole gains on its own, on top of eating...
        const float BlackHoleGrowthAcceleration = 5f; // ...rising to (1 + this) times as fast by the time it fills the arena
        const float MaxBodySpeed = 5f;        // in player radii per second; stops gravity from flinging things absurdly fast

        // Population. Levels are stage indices; tiers are a body's level relative to the player's.
        // Tier -2 and below never spawn and are never despawned: they stay until something eats them.
        const int MaxBodies = 50;             // cap on active bodies of tier -1 and up
        const int MaxPlusTwo = 1;
        const int MaxPlusOne = 10;
        const int MaxMinusOne = 30;           // the majority
        const int MaxPlayerLevel = MaxBodies - MaxPlusTwo - MaxPlusOne - MaxMinusOne; // the remaining 9
        const float PlusOneSpawnFraction = 0.25f;  // spawn within the bottom 25% of the level's size range
        const float PlayerLevelSpawnFraction = 0.5f;
        const float SpawnInterval = 0.25f;    // seconds between refills
        const int SpawnBatch = 3;             // bodies added per refill, so a level-up's worth of gaps fills quickly
        const float SpawnSpacing = 3f;        // gap between a new body and each existing one, in radii of the smaller of the two (at most the player's)

        const float AbsorbDuration = 0.25f;
        const int MaxDebris = 300;

        class Debris
        {
            public Transform T;
            public SpriteRenderer Sr;
            public Vector2 Vel;
            public Color Color;
            public float Life, MaxLife, Size;
        }

        Camera cam;
        ArcadeAudio sfx;
        Transform world;
        LineRenderer arenaEdge;
        SpaceBody player;
        float targetRadius;
        readonly List<SpaceBody> bodies = new List<SpaceBody>();
        readonly List<Debris> debris = new List<Debris>();

        State state;
        int stageIndex;
        float shake, bannerTime, spawnTimer;
        string banner;
        Vector3 camBase;

        bool IsBlackHole => Stages[stageIndex].Kind == BodyKind.BlackHole;

        void Awake()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.015f, 0.015f, 0.05f);

            world = new GameObject("World").transform;
            // Sounds and music, once there are files for them (silent until then).
            sfx = ArcadeAudio.Create(gameObject, "PlanetCrasher", "CrunchSmall", "CrunchMedium", "CrunchLarge", "LevelUp", "Death", "Win");
            new GameObject("Star Field").AddComponent<StarField>().Init(cam);
            arenaEdge = CreateArenaEdge();

            ResetWorld();
            state = State.Title;
        }

        LineRenderer CreateArenaEdge()
        {
            const int segments = 256;
            var line = new GameObject("Arena Edge").AddComponent<LineRenderer>();
            line.sharedMaterial = ProceduralArt.SpriteMaterial;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = segments;
            line.sortingOrder = -500;
            line.startColor = line.endColor = new Color(0.45f, 0.7f, 1f, 0.55f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * ArenaRadius);
            }
            return line;
        }

        // ---------------------------------------------------------------- loop

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            var kb = Keyboard.current;

            switch (state)
            {
                case State.Title:
                    if (Pressed(kb, Key.Space) || Pressed(kb, Key.Enter))
                    {
                        Restart();
                        break;
                    }
                    if (Pressed(kb, Key.Escape))
                    {
                        Arcade.LoadHome();
                        return;
                    }
                    UpdateBodies(dt);
                    MaintainPopulation(dt);
                    break;

                case State.Playing:
                    if (Pressed(kb, Key.Escape) || Pressed(kb, Key.P))
                    {
                        state = State.Paused;
                        break;
                    }
                    SimulatePlayer(dt);
                    UpdateBodies(dt);
                    if (state == State.Playing) PlayerInteractions(dt);
                    if (state == State.Playing) MaintainPopulation(dt); // not after a win or death this frame
                    break;

                case State.Paused:
                    if (Pressed(kb, Key.Escape) || Pressed(kb, Key.P)) state = State.Playing;
                    else if (Pressed(kb, Key.R)) Restart();
                    return; // freeze everything, camera included

                case State.GameOver:
                    UpdateBodies(dt);
                    MaintainPopulation(dt);
                    if (Pressed(kb, Key.Space) || Pressed(kb, Key.R)) Restart();
                    break;

                case State.Won:
                    GrowPlayer(dt);
                    player.Tick(dt);
                    UpdateBodies(dt);
                    if (Pressed(kb, Key.Space) || Pressed(kb, Key.R)) Restart();
                    break;
            }

            bannerTime -= dt;
            UpdateDebris(dt);
            UpdateCamera(dt);
        }

        static bool Pressed(Keyboard kb, Key key) => kb != null && kb[key].wasPressedThisFrame;

        static Vector2 ReadMove()
        {
            var move = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) move.x -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) move.x += 1f;
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) move.y -= 1f;
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) move.y += 1f;
            }
            var pad = Gamepad.current;
            if (pad != null) move += pad.leftStick.ReadValue();
            return Vector2.ClampMagnitude(move, 1f);
        }

        void Restart()
        {
            ResetWorld();
            state = State.Playing;
        }

        void ResetWorld()
        {
            foreach (var b in bodies) Destroy(b.gameObject);
            bodies.Clear();
            foreach (var d in debris) d.T.gameObject.SetActive(false);

            if (player == null)
            {
                player = SpaceBody.Create("Player", Stages[0].Kind, 0, Stages[0].Radius, world);
                player.SetSorting(200);
                player.SetGlow(new Color(0.35f, 0.85f, 1f, 0.35f), 1.7f);
            }
            player.gameObject.SetActive(true);
            stageIndex = 0;
            targetRadius = Stages[0].Radius;
            player.SetKind(Stages[0].Kind, Random.Range(0, ProceduralArt.VariantsPerKind));
            player.SetRadius(targetRadius);
            player.ShowImmediately();
            player.Position = Vector2.zero;
            player.Velocity = Vector2.zero;

            banner = null;
            bannerTime = 0f;
            shake = 0f;
            spawnTimer = 0f;

            cam.orthographicSize = ViewSize(targetRadius);
            camBase = new Vector3(0f, 0f, -10f);
            cam.transform.position = camBase;

            for (int i = 0; i < MaxBodies; i++) SpawnBody(true);
        }

        // ---------------------------------------------------------------- player

        void SimulatePlayer(float dt)
        {
            player.Velocity += ReadMove() * (Thrust * player.Radius * dt);
            player.Velocity *= Mathf.Exp(-Drag * dt);
            player.Position += player.Velocity * dt;
            if (IsBlackHole)
            {
                // A black hole slowly swallows space itself, so the endgame can't stall once only small food fits.
                float rate = BlackHoleGrowthRate * (1f + BlackHoleGrowthAcceleration * BlackHoleProgress);
                targetRadius = Mathf.Min(ArenaRadius, targetRadius + rate * dt);
                CheckStage();
                if (state != State.Playing) return;
            }
            GrowPlayer(dt);
            player.Tick(dt);

            Vector2 pos = player.Position;
            if (IsBlackHole)
            {
                // A black hole can't leave the arena; as it grows it gets squeezed towards the centre.
                float limit = Mathf.Max(0f, ArenaRadius - player.Radius);
                if (pos.magnitude > limit)
                {
                    Vector2 n = pos.normalized;
                    player.Position = n * limit;
                    float outward = Vector2.Dot(player.Velocity, n);
                    if (outward > 0f) player.Velocity -= n * outward;
                }
            }
            else if (pos.magnitude > ArenaRadius)
            {
                WrapPlayer();
            }
        }

        void GrowPlayer(float dt) =>
            player.SetRadius(Mathf.Lerp(player.Radius, targetRadius, 1f - Mathf.Exp(-6f * dt)));

        /// <summary>Crossing the edge sends the player to the opposite side of the circle, still moving the same way.</summary>
        void WrapPlayer()
        {
            Vector2 from = player.Position;
            Vector2 to = -from.normalized * (ArenaRadius * 0.98f);
            player.Position = to;
            camBase += (Vector3)(to - from); // keep the camera in the same place relative to the player

            // Replace whatever is out of sight at the new location (leftover small bodies stay until eaten).
            float viewRad = ViewHalfExtents().magnitude;
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var b = bodies[i];
                if (b.Dying || !Despawnable(b) || (b.Position - to).magnitude - b.Radius < viewRad * 1.3f) continue;
                Destroy(b.gameObject);
                bodies.RemoveAt(i);
            }
            for (int i = CappedCount(); i < MaxBodies; i++) SpawnBody(true);
        }

        void PlayerInteractions(float dt)
        {
            Vector2 pp = player.Position;
            float pr = player.Radius;
            bool blackHole = IsBlackHole;

            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var b = bodies[i];
                if (b.Dying) continue;

                Vector2 delta = b.Position - pp;
                float d = delta.magnitude;
                if (d < 1e-4f) continue;
                Vector2 dir = delta / d;

                if (b.Radius >= pr)
                {
                    if (d < b.Radius * GravityRange)
                    {
                        float s = b.Radius / Mathf.Max(d, b.Radius);
                        player.Velocity += dir * (GravityStrength * pr * s * s * dt);
                    }
                    if (d < pr + b.Radius)
                    {
                        Die(pp + dir * pr);
                        return;
                    }
                }
                else
                {
                    if (blackHole)
                    {
                        b.Velocity -= dir * (BlackHolePull * pr * (pr / Mathf.Max(d, pr)) * dt);
                    }
                    else if (d < pr * AttractRange)
                    {
                        float s = pr / Mathf.Max(d, pr);
                        b.Velocity -= dir * (AttractStrength * pr * s * s * dt);
                    }
                    if (d < pr + b.Radius * 0.5f) PlayerEats(b, dir);
                    if (state != State.Playing) return;
                }
            }
        }

        void PlayerEats(SpaceBody b, Vector2 dir)
        {
            Swallow(b, player);

            float pr = player.Radius;
            float relative = b.Radius / pr;
            targetRadius = Mathf.Min(ArenaRadius, Mathf.Sqrt(targetRadius * targetRadius + b.Radius * b.Radius * GrowthPerArea));

            SpawnDebris(player.Position + dir * pr, b.MainColor, 6 + (int)(10f * relative), pr * 3f, b.Radius * 0.3f);
            sfx.Play(relative < 1f / 3f ? "CrunchSmall" : relative < 2f / 3f ? "CrunchMedium" : "CrunchLarge", 0.3f + 0.4f * relative);
            shake = Mathf.Max(shake, 0.25f * relative);
            CheckStage();
        }

        void Die(Vector2 impact)
        {
            float pr = player.Radius;
            SpawnDebris(player.Position, player.MainColor, 40, pr * 6f, pr * 0.35f);
            SpawnDebris(impact, player.MainColor, 15, pr * 8f, pr * 0.2f);
            player.gameObject.SetActive(false);
            sfx.Play("Death", 0.9f);
            shake = 1f;
            state = State.GameOver;
        }

        void CheckStage()
        {
            while (stageIndex + 1 < Stages.Length && targetRadius >= Stages[stageIndex + 1].Radius)
            {
                stageIndex++;
                player.SetKind(Stages[stageIndex].Kind, Random.Range(0, ProceduralArt.VariantsPerKind));
                banner = IsBlackHole ? "You are now a Black Hole! Swallow the universe!" : $"You are now a {Stages[stageIndex].Name}!";
                bannerTime = 2.5f;
                sfx.Play("LevelUp", 0.6f);
            }

            if (targetRadius >= ArenaRadius * 0.98f) Win();
        }

        void Win()
        {
            targetRadius = ArenaRadius;
            state = State.Won;
            foreach (var b in bodies)
                if (!b.Dying) Swallow(b, player);
            sfx.Play("Win", 0.8f);
            shake = 1f;
        }

        // ---------------------------------------------------------------- bodies

        static void Swallow(SpaceBody prey, SpaceBody eater)
        {
            prey.Dying = true;
            prey.DyingTime = 0f;
            prey.Eater = eater;
        }

        void UpdateBodies(float dt)
        {
            // Movement, the arena wall, and the swallow animation.
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var b = bodies[i];

                if (b.Dying)
                {
                    b.DyingTime += dt;
                    float t = b.DyingTime / AbsorbDuration;
                    if (t >= 1f)
                    {
                        Destroy(b.gameObject);
                        bodies.RemoveAt(i);
                        continue;
                    }
                    if (b.Eater != null) b.Position = Vector2.Lerp(b.Position, b.Eater.Position, 1f - Mathf.Exp(-12f * dt));
                    b.SetVisualScale(b.Radius * (1f - t));
                    continue;
                }

                b.Position += b.Velocity * dt;
                BounceOffEdge(b);
                b.Tick(dt);
            }

            // Bodies pull in and eat anything smaller than themselves.
            for (int i = 0; i < bodies.Count; i++)
            {
                var a = bodies[i];
                if (a.Dying) continue;

                for (int j = i + 1; j < bodies.Count; j++)
                {
                    var c = bodies[j];
                    if (c.Dying) continue;

                    var big = a.Radius >= c.Radius ? a : c;
                    var small = big == a ? c : a;
                    Vector2 delta = small.Position - big.Position;
                    float d = delta.magnitude;
                    if (d > big.Radius * BodyGravityRange || d < 1e-4f) continue;

                    Vector2 dir = delta / d;
                    float s = big.Radius / Mathf.Max(d, big.Radius);
                    small.Velocity -= dir * (BodyGravityStrength * big.Radius * s * s * dt);

                    if (big.Radius > small.Radius * 1.05f && d < big.Radius + small.Radius * 0.5f)
                    {
                        BodyEats(big, small, dir);
                        if (a.Dying) break;
                    }
                }
            }

            float maxSpeed = MaxBodySpeed * targetRadius;
            foreach (var b in bodies)
                if (!b.Dying) b.Velocity = Vector2.ClampMagnitude(b.Velocity, maxSpeed);
        }

        void BodyEats(SpaceBody big, SpaceBody small, Vector2 dir)
        {
            Swallow(small, big);

            // Each meal is worth exponentially less the further the body has already grown.
            float gain = small.Radius * small.Radius * BodyGrowthPerArea
                * Mathf.Exp(-BodyGrowthDecay * (big.GrowTarget / big.SpawnRadius - 1f));
            if (big.Level - stageIndex >= 2) gain *= PlusTwoGrowthScale;
            float grown = Mathf.Min(BodyMaxRadius, Mathf.Sqrt(big.GrowTarget * big.GrowTarget + gain));
            big.Grow(grown);
            var kind = KindForRadius(grown);
            if (kind != big.Kind) big.SetKind(kind, Random.Range(0, ProceduralArt.VariantsPerKind));
            big.SetSorting(SortingFor(grown));

            if (IsOnScreen(big.Position, big.Radius))
                SpawnDebris(big.Position + dir * big.Radius, small.MainColor, 4 + (int)(6f * small.Radius / big.Radius), small.Radius * 3f, small.Radius * 0.3f);
        }

        /// <summary>Bodies that reach the edge bounce back inwards at a random angle.</summary>
        void BounceOffEdge(SpaceBody b)
        {
            float limit = ArenaRadius - b.Radius;
            Vector2 pos = b.Position;
            float m = pos.magnitude;
            if (m <= limit) return;

            Vector2 n = pos / m;
            b.Position = n * Mathf.Max(0f, limit);
            if (Vector2.Dot(b.Velocity, n) <= 0f) return;

            float speed = Mathf.Max(b.Velocity.magnitude, targetRadius * 0.3f);
            Vector2 inward = Quaternion.Euler(0f, 0f, Random.Range(-70f, 70f)) * -n;
            b.Velocity = inward * speed;
        }

        // ---------------------------------------------------------------- spawning

        static float ViewSize(float playerRadius) => Mathf.Min(playerRadius * ViewPerRadius, MaxViewSize);

        Vector2 ViewHalfExtents()
        {
            float h = ViewSize(targetRadius);
            return new Vector2(h * cam.aspect, h);
        }

        bool IsOnScreen(Vector2 pos, float radius)
        {
            Vector2 half = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
            Vector2 d = pos - (Vector2)cam.transform.position;
            return Mathf.Abs(d.x) < half.x + radius && Mathf.Abs(d.y) < half.y + radius;
        }

        static BodyKind KindForRadius(float r)
        {
            int k = 0;
            for (int i = 0; i < Stages.Length; i++)
                if (r >= Stages[i].Radius) k = i;
            return Stages[k].Kind;
        }

        static int SortingFor(float r) => Mathf.Clamp(-Mathf.RoundToInt(Mathf.Log(r) * 8f), -190, 140);

        /// <summary>Size range [min, max) of a level. Level -1 is pebbles, smaller than the smallest asteroid.</summary>
        static Vector2 LevelRange(int level)
        {
            if (level < 0) return new Vector2(Stages[0].Radius * 0.3f, Stages[0].Radius);
            float max = level + 1 < Stages.Length ? Stages[level + 1].Radius : Stages[level].Radius * 3f;
            return new Vector2(Stages[level].Radius, Mathf.Min(max, BodyMaxRadius));
        }

        /// <summary>
        /// Whether bodies of this tier can spawn: never two or more levels down, never below pebbles,
        /// and never a black hole (only the player can be one).
        /// </summary>
        bool TierExists(int tier)
        {
            int level = stageIndex + tier;
            return tier >= -1 && level >= -1 && level < Stages.Length - 1 && LevelRange(level).x < BodyMaxRadius;
        }

        int CountTier(int tier)
        {
            int n = 0;
            foreach (var b in bodies)
                if (!b.Dying && b.Level - stageIndex == tier) n++;
            return n;
        }

        /// <summary>Leftovers two or more levels down never despawn; they stay until something eats them.</summary>
        bool Despawnable(SpaceBody b) => b.Level - stageIndex >= -1;

        /// <summary>Bodies that count against <see cref="MaxBodies"/>.</summary>
        int CappedCount()
        {
            int n = 0;
            foreach (var b in bodies)
                if (!b.Dying && Despawnable(b)) n++;
            return n;
        }

        static readonly int[] SpawnTiers = { 2, 1, 0, -1 };

        static int Quota(int tier) =>
            tier == 2 ? MaxPlusTwo : tier == 1 ? MaxPlusOne : tier == 0 ? MaxPlayerLevel : MaxMinusOne;

        /// <summary>
        /// Spawn whichever tier is proportionally furthest below its quota, so every tier keeps being topped up as
        /// bodies get eaten. When a tier can't exist (e.g. nothing above a star), the spare slots go to food.
        /// </summary>
        int ChooseTier()
        {
            int best = int.MinValue;
            float bestNeed = 0f;
            foreach (int tier in SpawnTiers)
            {
                if (!TierExists(tier)) continue;
                float need = (Quota(tier) - CountTier(tier)) / (float)Quota(tier);
                if (need > bestNeed)
                {
                    bestNeed = need;
                    best = tier;
                }
            }
            if (best != int.MinValue) return best;
            return TierExists(-1) ? -1 : 0;
        }

        /// <summary>Spawn sizes are capped to the low end of each level so nothing starts out huge.</summary>
        static float RadiusForTier(int level, int tier)
        {
            Vector2 range = LevelRange(level);
            float fraction = tier >= 2 ? 0f                               // the smallest possible size
                : tier == 1 ? Random.value * PlusOneSpawnFraction           // bottom 0-25%
                : tier == 0 ? Random.value * PlayerLevelSpawnFraction
                : Random.value;                                              // anywhere: it's all smaller than the player
            return Mathf.Lerp(range.x, range.y, fraction);
        }

        bool IsSpaceFree(Vector2 pos, float r)
        {
            // Scale the gap by the smaller body so tiny leftovers don't block big areas and big bodies
            // don't crowd everything else out of the arena.
            float maxGap = Mathf.Min(Mathf.Min(r, targetRadius) * SpawnSpacing, ArenaRadius * 0.06f);
            foreach (var b in bodies)
            {
                if (b.Dying) continue;
                float gap = Mathf.Min(maxGap, b.Radius * SpawnSpacing);
                if ((b.Position - pos).magnitude < b.Radius + r + gap) return false;
            }
            return true;
        }

        void SpawnBody(bool initial)
        {
            int tier = ChooseTier();
            int level = stageIndex + tier;
            float r = RadiusForTier(level, tier);
            float p = targetRadius;

            Vector2 pp = player.Position;
            float viewRad = ViewHalfExtents().magnitude;
            if (IsBlackHole)
            {
                SpawnBlackHoleFood(level, r);
                return;
            }
            // Keep clear of the player, with extra room to react for dangerous bodies (capped so it always fits in the arena).
            float margin = r >= player.Radius ? p * 6f : Mathf.Min(p, r) * 2f;
            float clearance = player.Radius + r + Mathf.Min(margin, ArenaRadius * 0.15f);
            // Once zoomed out, the ring just off-screen lies entirely outside the arena, so don't waste attempts on it.
            bool ringReachesArena = viewRad * 1.2f < ArenaRadius + pp.magnitude;
            bool arenaFullyVisible = cam.orthographicSize >= ArenaRadius;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector2 pos;
                if (attempt < 10 && ringReachesArena)
                {
                    float dist = initial && r < p
                        ? Random.Range(p * 5f, viewRad * 2f)
                        : Random.Range(viewRad * 1.2f, viewRad * 2.2f) + r;
                    pos = pp + Random.insideUnitCircle.normalized * dist;
                }
                else
                {
                    // Anywhere inside the arena, preferring spots out of view (new bodies fade in either way).
                    pos = Random.insideUnitCircle * (ArenaRadius - r);
                    if (attempt < 18 && !initial && !arenaFullyVisible && IsOnScreen(pos, r)) continue;
                }

                if (pos.magnitude + r > ArenaRadius || (pos - pp).magnitude < clearance || !IsSpaceFree(pos, r)) continue;

                AddBody(level, r, pos);
                return;
            }
        }

        /// <summary>
        /// Black hole food spawns only in the ring of space left between the black hole and the arena wall,
        /// shrunk to fit as that ring narrows. It's all being swallowed anyway, so it can spawn close to the black hole.
        /// </summary>
        void SpawnBlackHoleFood(int level, float r)
        {
            Vector2 pp = player.Position;
            float pr = player.Radius;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                // Distance from the black hole's centre to the wall along dir: solve |pp + dir*t| = ArenaRadius.
                float along = Vector2.Dot(pp, dir);
                float toWall = -along + Mathf.Sqrt(along * along - pp.sqrMagnitude + ArenaRadius * ArenaRadius);
                float gap = toWall - pr;
                if (gap < Stages[0].Radius * 2f) continue;

                float size = Mathf.Min(r, gap * 0.4f * Random.Range(0.5f, 1f));
                float dist = Random.Range(pr + size * 1.25f, toWall - size);
                if (dist <= pr + size) continue;
                Vector2 pos = pp + dir * dist;
                if (pos.magnitude + size > ArenaRadius) continue;

                AddBody(level, size, pos);
                return;
            }
        }

        void AddBody(int level, float r, Vector2 pos)
        {
            float p = targetRadius;
            var b = SpaceBody.Create("Body", KindForRadius(r), Random.Range(0, ProceduralArt.VariantsPerKind), r, world);
            b.Position = pos;
            b.Velocity = Random.insideUnitCircle * (p * Random.Range(0.2f, 0.8f) / Mathf.Sqrt(Mathf.Max(1f, r / p)));
            b.Level = level;
            b.SpawnRadius = r;
            b.SetSorting(SortingFor(r));
            bodies.Add(b);
        }

        /// <summary>0 when the player first becomes a black hole, 1 when it fills the arena.</summary>
        float BlackHoleProgress => Mathf.InverseLerp(Stages[Stages.Length - 1].Radius, ArenaRadius, targetRadius);

        void MaintainPopulation(float dt)
        {
            Vector2 pp = player.Position;
            float viewRad = ViewHalfExtents().magnitude;

            // Bodies left far behind are recycled; leftovers two or more levels down stay until eaten.
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var b = bodies[i];
                if (b.Dying || !Despawnable(b)) continue;
                if ((b.Position - pp).magnitude - b.Radius > viewRad * 3.2f)
                {
                    Destroy(b.gameObject);
                    bodies.RemoveAt(i);
                }
            }

            // As a black hole fills the arena, spawn faster and in bigger batches so the endgame accelerates.
            float interval = SpawnInterval;
            int batch = SpawnBatch;
            if (IsBlackHole)
            {
                float t = BlackHoleProgress;
                interval *= Mathf.Lerp(1f, 0.15f, t);
                batch += Mathf.RoundToInt(t * 6f);
            }

            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = interval;
            int missing = Mathf.Min(MaxBodies - CappedCount(), batch);
            for (int i = 0; i < missing; i++) SpawnBody(false);
        }

        // ---------------------------------------------------------------- effects

        void SpawnDebris(Vector2 pos, Color color, int count, float speed, float size)
        {
            for (int i = 0; i < count; i++)
            {
                var d = GetDebris();
                if (d == null) return;
                d.T.position = pos + Random.insideUnitCircle * size;
                d.Vel = Random.insideUnitCircle.normalized * (speed * Random.Range(0.3f, 1f));
                d.MaxLife = d.Life = Random.Range(0.4f, 0.9f);
                d.Size = size * Random.Range(0.5f, 1.2f);
                d.Color = color * Random.Range(0.8f, 1.2f);
                d.Color.a = 1f;
                d.T.gameObject.SetActive(true);
            }
        }

        Debris GetDebris()
        {
            foreach (var d in debris)
                if (!d.T.gameObject.activeSelf) return d;
            if (debris.Count >= MaxDebris) return null;

            var go = new GameObject("Debris");
            go.transform.SetParent(world, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ProceduralArt.Dot;
            sr.sharedMaterial = ProceduralArt.SpriteMaterial;
            sr.sortingOrder = 160;
            var created = new Debris { T = go.transform, Sr = sr };
            debris.Add(created);
            return created;
        }

        void UpdateDebris(float dt)
        {
            foreach (var d in debris)
            {
                if (!d.T.gameObject.activeSelf) continue;
                d.Life -= dt;
                if (d.Life <= 0f)
                {
                    d.T.gameObject.SetActive(false);
                    continue;
                }
                d.T.position += (Vector3)(d.Vel * dt);
                d.Vel *= Mathf.Exp(-2f * dt);
                float a = d.Life / d.MaxLife;
                d.Sr.color = new Color(d.Color.r, d.Color.g, d.Color.b, a);
                d.T.localScale = Vector3.one * (d.Size * (0.5f + 0.5f * a));
            }
        }

        void UpdateCamera(float dt)
        {
            float unclamped = Mathf.Max(player.Radius, 0.01f) * ViewPerRadius;
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, ViewSize(player.Radius), 1f - Mathf.Exp(-2.5f * dt));

            // Once zoomed out far enough to see most of the arena, drift towards framing the whole circle.
            Vector2 focus = player.Position;
            if (state == State.Playing) focus += player.Velocity * 0.15f;
            focus = Vector2.Lerp(focus, Vector2.zero, Mathf.InverseLerp(MaxViewSize * 0.5f, MaxViewSize, unclamped));
            camBase = Vector3.Lerp(camBase, new Vector3(focus.x, focus.y, -10f), 1f - Mathf.Exp(-5f * dt));

            Vector2 jitter = Random.insideUnitCircle * (shake * cam.orthographicSize * 0.04f);
            cam.transform.position = camBase + (Vector3)jitter;
            shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);

            arenaEdge.widthMultiplier = cam.orthographicSize * 0.008f;
        }

        // ---------------------------------------------------------------- HUD

        GUIStyle center, big, medium, small, button;
        float guiScale = -1f;

        void EnsureStyles()
        {
            float s = Screen.height / 720f;
            if (center != null && Mathf.Approximately(s, guiScale)) return;
            guiScale = s;

            center = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big = new GUIStyle(center) { fontSize = Mathf.RoundToInt(72 * s) };
            medium = new GUIStyle(center) { fontSize = Mathf.RoundToInt(32 * s) };
            small = new GUIStyle(center) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Normal };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold };
        }

        // Labels switch to their hover colour under the mouse, so every state must share the colour
        // or the shadow copy lights up and the text appears doubled.
        static void SetTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;
        }

        static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            SetTextColor(style, new Color(0f, 0f, 0f, 0.8f * color.a));
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            SetTextColor(style, color);
            GUI.Label(r, text, style);
        }

        bool MenuButton(float y, string text)
        {
            float s = guiScale;
            return GUI.Button(new Rect(Screen.width / 2f - 130f * s, y, 260f * s, 48f * s), text, button);
        }

        /// <summary>Sound and Music switches, under the pause menu's buttons, once there are files for them.</summary>
        void SoundSwitches(float y, float step)
        {
            if (sfx.HasSounds)
            {
                if (MenuButton(y, sfx.Muted ? "Sound: Off" : "Sound: On")) sfx.ToggleSound();
                y += step;
            }
            if (sfx.HasMusic && MenuButton(y, sfx.MusicOff ? "Music: Off" : "Music: On")) sfx.ToggleMusic();
        }

        static void Fill(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void Bar(Rect r, float t, Color color)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * Mathf.Clamp01(t), r.height - 4f), color);
        }

        void OnGUI()
        {
            EnsureStyles();
            float s = guiScale, w = Screen.width, h = Screen.height;

            if (state != State.Title) DrawHud(s, w, h);

            switch (state)
            {
                case State.Title:
                    Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.35f));
                    Shadowed(new Rect(0, h * 0.18f, w, 90f * s), "PLANET CRASHER", big, new Color(1f, 0.85f, 0.4f));
                    Shadowed(new Rect(0, h * 0.40f, w, 30f * s), "Crash into anything smaller than you to absorb it and grow.", small, Color.white);
                    Shadowed(new Rect(0, h * 0.40f + 32f * s, w, 30f * s), "Touch anything bigger and you're crushed. Beware of its gravity.", small, Color.white);
                    Shadowed(new Rect(0, h * 0.40f + 64f * s, w, 30f * s), "Grow into a black hole and swallow the whole universe.", small, Color.white);
                    Shadowed(new Rect(0, h * 0.40f + 112f * s, w, 30f * s), "ARROWS / WASD to move      Esc to pause", small, new Color(0.7f, 0.9f, 1f));
                    if (MenuButton(h * 0.66f, "Play")) Restart();
                    if (MenuButton(h * 0.66f + 60f * s, "Main Menu")) Arcade.LoadHome();
                    break;

                case State.Paused:
                    Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.5f));
                    Shadowed(new Rect(0, h * 0.25f, w, 90f * s), "PAUSED", big, Color.white);
                    if (MenuButton(h * 0.25f + 120f * s, "Resume")) state = State.Playing;
                    if (MenuButton(h * 0.25f + 180f * s, "Restart")) Restart();
                    if (MenuButton(h * 0.25f + 240f * s, "Main Menu")) Arcade.LoadHome();
                    SoundSwitches(h * 0.25f + 300f * s, 60f * s);
                    break;

                case State.GameOver:
                    Fill(new Rect(0, 0, w, h), new Color(0.2f, 0f, 0f, 0.35f));
                    Shadowed(new Rect(0, h * 0.3f, w, 90f * s), "CRUSHED", big, new Color(1f, 0.4f, 0.3f));
                    Shadowed(new Rect(0, h * 0.3f + 110f * s, w, 40f * s), $"You made it to {Stages[stageIndex].Name}", medium, Color.white);
                    if (MenuButton(h * 0.3f + 200f * s, "Try Again")) Restart();
                    if (MenuButton(h * 0.3f + 260f * s, "Main Menu")) Arcade.LoadHome();
                    break;

                case State.Won:
                    Fill(new Rect(0, 0, w, h), new Color(0.1f, 0f, 0.15f, 0.35f));
                    Shadowed(new Rect(0, h * 0.3f, w, 90f * s), "UNIVERSE CONSUMED", big, new Color(0.85f, 0.6f, 1f));
                    Shadowed(new Rect(0, h * 0.3f + 115f * s, w, 30f * s), "You grew from a speck of rock into a black hole that swallowed everything.", small, Color.white);
                    if (MenuButton(h * 0.3f + 200f * s, "Play Again")) Restart();
                    if (MenuButton(h * 0.3f + 260f * s, "Main Menu")) Arcade.LoadHome();
                    break;
            }
        }

        void DrawHud(float s, float w, float h)
        {
            bool last = stageIndex + 1 >= Stages.Length;
            float from = Stages[stageIndex].Radius;
            float to = last ? ArenaRadius : Stages[stageIndex + 1].Radius;
            float progress = Mathf.InverseLerp(Mathf.Log(from), Mathf.Log(to), Mathf.Log(targetRadius));
            Shadowed(new Rect(w / 2f - 200f * s, 10f * s, 400f * s, 36f * s), Stages[stageIndex].Name.ToUpper(), medium, Color.white);
            Bar(new Rect(w / 2f - 150f * s, 50f * s, 300f * s, 14f * s), progress, new Color(0.4f, 0.85f, 1f));
            Shadowed(new Rect(w / 2f - 150f * s, 66f * s, 300f * s, 24f * s),
                $"next: {(last ? "Swallow the universe" : Stages[stageIndex + 1].Name)}", small, new Color(0.8f, 0.8f, 0.8f));

            if (bannerTime > 0f && banner != null)
                Shadowed(new Rect(0, h * 0.22f, w, 50f * s), banner, medium, new Color(1f, 0.9f, 0.5f, Mathf.Clamp01(bannerTime)));
        }
    }
}
