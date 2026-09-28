using System.Collections.Generic;
using FlasshArcade;
using UnityEngine;
using UnityEngine.InputSystem;
using static FlasshArcade.ArcadeGui;

namespace LittleFishy
{
    /// <summary>
    /// Runs Little Fishy: you're a goldfish in a tank. Eat fish smaller than you to grow, and touch anything bigger
    /// and you're eaten. Fish swim across the screen at random sizes and speeds, coloured by size. The left and
    /// right edges wrap. Grow into a massive goldfish to win.
    /// </summary>
    public class LittleFishyGame : MonoBehaviour
    {
        enum State { Title, Playing, Paused, GameOver, Won }

        const float ViewSize = 5f;            // camera orthographic size: the tank is 10 units tall
        const float FieldWidthFraction = 0.95f; // the playing field is 95% of the screen's width
        const float WaterTop = 4.2f;
        const float SandTop = -3.6f;
        const float StartLength = 0.6f;
        const float WinLength = 4f;
        // Heavy, floaty movement: low thrust and low drag give a top speed of Accel / Drag (~5.1 units/s),
        // but it takes about a second to get going and momentum makes quick turns hard.
        const float Accel = 6.6f;             // units per second²
        const float Drag = 1.3f;
        const float StartSpeedScale = 0.8f;   // a new goldfish swims at 80% of that, reaching full speed as it grows
        const float GrowthPerArea = 0.1f;     // fraction of an eaten fish's area the player gains

        const int MaxFish = 18;
        const float MinSpawnDelay = 0.35f;
        const float MaxSpawnDelay = 0.9f;
        const float MinFishLength = 0.3f;     // any size in this range can spawn at any time, independent of the player
        const float MaxFishLength = 6.5f;
        const float MinFishSpeed = 0.8f;
        const float MaxFishSpeed = 5f;
        const float EatDuration = 0.15f;

        static readonly Color GoldfishColor = new Color(1f, 0.6f, 0.1f);

        // Fish colour by size, smallest to largest: goldfish orange, then pink, purple and finally dark blue.
        static readonly Color[] SizeColors =
        {
            GoldfishColor,
            new Color(1f, 0.45f, 0.65f),  // pink
            new Color(0.6f, 0.25f, 0.8f), // purple
            new Color(0.12f, 0.15f, 0.55f), // dark blue
        };

        // Eaten fish are tallied as bones: 5 small make a medium, 10 medium make a large.
        const int SmallPerMedium = 5;
        const int MediumPerLarge = 10;

        Camera cam;
        FishyAudio sounds;
        Tank tank;
        Transform school;
        Fish player;
        float targetLength;
        readonly List<Fish> fish = new List<Fish>();

        State state;
        float spawnTimer;
        int eaten;

        /// <summary>Half the width of the playing field: fish spawn, wrap and leave at its edges. Outside it is covered by the tank's side panels.</summary>
        float FieldHalfWidth => cam.orthographicSize * cam.aspect * FieldWidthFraction;

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
            cam.orthographicSize = ViewSize;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            SetBackground(new Color(0.03f, 0.2f, 0.42f));

            tank = new GameObject("Tank").AddComponent<Tank>();
            tank.Init(cam, WaterTop, SandTop);
            school = new GameObject("Fish").transform;
            sounds = gameObject.AddComponent<FishyAudio>();

            ResetGame();
            state = State.Title;
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
                    player.Tick(dt);
                    UpdateSchool(dt, false);
                    Spawn(dt);
                    break;

                case State.Playing:
                    if (Pressed(kb, Key.Escape) || Pressed(kb, Key.P))
                    {
                        state = State.Paused;
                        break;
                    }
                    UpdatePlayer(dt);
                    UpdateSchool(dt, true);
                    if (state == State.Playing) Spawn(dt);
                    break;

                case State.Paused:
                    if (Pressed(kb, Key.Escape) || Pressed(kb, Key.P)) state = State.Playing;
                    else if (Pressed(kb, Key.R)) Restart();
                    return; // freeze everything

                case State.GameOver:
                    FloatDeadPlayer(dt);
                    UpdateSchool(dt, false);
                    Spawn(dt);
                    if (Pressed(kb, Key.Space) || Pressed(kb, Key.R)) Restart();
                    break;

                case State.Won:
                    GrowPlayer(dt);
                    player.Tick(dt);
                    UpdateSchool(dt, false);
                    if (Pressed(kb, Key.Space) || Pressed(kb, Key.R)) Restart();
                    break;
            }

            tank.Tick(dt, FieldHalfWidth);
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
            ResetGame();
            state = State.Playing;
        }

        void ResetGame()
        {
            foreach (var f in fish) Destroy(f.gameObject);
            fish.Clear();

            // Recreate the player so nothing (like floating belly-up) carries over from the last round.
            if (player != null) Destroy(player.gameObject);
            player = Fish.Create("Goldfish", GoldfishColor, StartLength, school);
            player.SetSorting(500);
            player.Position = new Vector2(0f, 0.3f);
            targetLength = StartLength;
            eaten = 0;

            spawnTimer = 0f; // the tank starts empty; fish start swimming in from the sides straight away
        }

        // ---------------------------------------------------------------- player

        void UpdatePlayer(float dt)
        {
            float speedScale = Mathf.Lerp(StartSpeedScale, 1f, Mathf.InverseLerp(StartLength, WinLength, player.Length));
            Vector2 input = ReadMove();
            player.Velocity += input * (Accel * speedScale * dt);
            // Face where the player is steering, even while momentum still carries the fish the other way.
            if (Mathf.Abs(input.x) > 0.1f) player.Face(input.x > 0f);
            player.Velocity *= Mathf.Exp(-Drag * dt);

            Vector2 pos = player.Position + player.Velocity * dt;

            // Swimming off the left or right edge brings you in from the other side.
            float reach = FieldHalfWidth + player.Length * 0.5f;
            if (pos.x > reach) pos.x -= reach * 2f;
            else if (pos.x < -reach) pos.x += reach * 2f;

            float top = WaterTop - player.HalfHeight, bottom = SandTop + player.HalfHeight;
            if (pos.y > top)
            {
                pos.y = top;
                player.Velocity.y = Mathf.Min(0f, player.Velocity.y);
            }
            else if (pos.y < bottom)
            {
                pos.y = bottom;
                player.Velocity.y = Mathf.Max(0f, player.Velocity.y);
            }
            player.Position = pos;

            GrowPlayer(dt);
            player.Tick(dt);

            if (Random.value < dt * 0.6f) tank.Burst(player.Mouth, 1, 0.04f + player.Length * 0.015f);
        }

        void GrowPlayer(float dt) =>
            player.SetLength(Mathf.Lerp(player.Length, targetLength, 1f - Mathf.Exp(-6f * dt)));

        void FloatDeadPlayer(float dt)
        {
            Vector2 pos = player.Position;
            pos.y = Mathf.MoveTowards(pos.y, WaterTop - player.HalfHeight, dt * 0.6f);
            player.Position = pos;
        }

        /// <summary>Fish touch when any part of one (body or tail oval) overlaps any part of the other.</summary>
        static bool Touching(Fish a, Fish b) =>
            Overlap(a.BodyOval, b.BodyOval) || Overlap(a.BodyOval, b.TailOval) ||
            Overlap(a.TailOval, b.BodyOval) || Overlap(a.TailOval, b.TailOval);

        /// <summary>Approximate oval overlap, shrunk slightly so grazing contact at the very edge doesn't count.</summary>
        static bool Overlap((Vector2 centre, Vector2 radii) a, (Vector2 centre, Vector2 radii) b)
        {
            Vector2 d = b.centre - a.centre;
            Vector2 r = (a.radii + b.radii) * 0.9f;
            return (d.x / r.x) * (d.x / r.x) + (d.y / r.y) * (d.y / r.y) < 1f;
        }

        void Eat(Fish f)
        {
            f.Dying = true;
            f.DyingTime = 0f;

            float relative = f.Length / player.Length;
            targetLength = Mathf.Min(WinLength, Mathf.Sqrt(targetLength * targetLength + GrowthPerArea * f.Length * f.Length));
            eaten++;
            tank.Burst(player.Mouth, 3 + (int)(relative * 5f), 0.05f + f.Length * 0.05f);

            if (targetLength >= WinLength) Win();
        }

        void Die()
        {
            state = State.GameOver;
            player.SetBellyUp();
            player.Velocity = Vector2.zero;
            sounds.PlayDeath();
            tank.Burst(player.Position, 12, player.Length * 0.1f);
        }

        void Win()
        {
            state = State.Won;
            sounds.PlayWin();
            tank.Burst(player.Position, 30, player.Length * 0.3f);
        }

        // ---------------------------------------------------------------- other fish

        static Color ColorForLength(float length)
        {
            float t = Mathf.InverseLerp(Mathf.Log(MinFishLength), Mathf.Log(MaxFishLength), Mathf.Log(length));
            float scaled = t * (SizeColors.Length - 1);
            int i = Mathf.Min((int)scaled, SizeColors.Length - 2);
            return Color.Lerp(SizeColors[i], SizeColors[i + 1], scaled - i);
        }

        void Spawn(float dt)
        {
            spawnTimer -= dt;
            if (spawnTimer > 0f || fish.Count >= MaxFish) return;
            spawnTimer = Random.Range(MinSpawnDelay, MaxSpawnDelay);
            SpawnFish();
        }

        /// <summary>Sends a fish in from the left or right edge of the field.</summary>
        void SpawnFish()
        {
            // Any size from smallest to largest, equally likely on a log scale (as many tiny fish as huge ones).
            float length = MinFishLength * Mathf.Pow(MaxFishLength / MinFishLength, Random.value);

            float halfW = FieldHalfWidth;
            float halfH = length * FishArt.BodyHalfHeight / FishArt.FullLength;
            float lo = SandTop + halfH + 0.1f, hi = WaterTop - halfH - 0.1f;
            float y = lo < hi ? Random.Range(lo, hi) : (SandTop + WaterTop) / 2f;

            bool swimRight = Random.value < 0.5f;
            float x = swimRight ? -halfW - length : halfW + length;

            float speed = Random.Range(MinFishSpeed, MaxFishSpeed) * Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(MinFishLength, MaxFishLength, length));

            var f = Fish.Create("Fish", ColorForLength(length), length, school);
            f.Face(swimRight);
            f.Velocity = new Vector2(swimRight ? speed : -speed, 0f);
            f.Position = new Vector2(x, y);
            f.SetSorting(Mathf.Clamp(120 - Mathf.RoundToInt(length * 15f), 0, 120) * 3); // bigger fish swim behind smaller ones
            fish.Add(f);
        }

        void UpdateSchool(float dt, bool interact)
        {
            float halfW = FieldHalfWidth;
            for (int i = fish.Count - 1; i >= 0; i--)
            {
                var f = fish[i];

                if (f.Dying)
                {
                    f.DyingTime += dt;
                    float t = f.DyingTime / EatDuration;
                    if (t >= 1f)
                    {
                        Destroy(f.gameObject);
                        fish.RemoveAt(i);
                        continue;
                    }
                    f.Position = Vector2.Lerp(f.Position, player.Mouth, 1f - Mathf.Exp(-25f * dt));
                    f.ApplyScale(f.Length * (1f - t));
                    continue;
                }

                f.Position += f.Velocity * dt;
                f.Tick(dt);

                bool gone = f.Velocity.x > 0f ? f.Position.x > halfW + f.Length : f.Position.x < -halfW - f.Length;
                if (gone)
                {
                    Destroy(f.gameObject);
                    fish.RemoveAt(i);
                    continue;
                }

                if (!interact || state != State.Playing || !Touching(player, f)) continue;
                if (f.Length < player.Length) Eat(f);
                else Die();
            }
        }

        // ---------------------------------------------------------------- HUD

        GUIStyle big, medium, small, button;
        float guiScale = -1f;

        void EnsureStyles()
        {
            float s = Screen.height / 720f;
            if (big != null && Mathf.Approximately(s, guiScale)) return;
            guiScale = s;

            var label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big = new GUIStyle(label) { fontSize = Mathf.RoundToInt(72 * s) };
            medium = new GUIStyle(label) { fontSize = Mathf.RoundToInt(32 * s) };
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Normal };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold };
        }

        bool MenuButton(float y, string text)
        {
            float s = guiScale;
            return GUI.Button(new Rect(Screen.width / 2f - 130f * s, y, 260f * s, 48f * s), text, button);
        }

        /// <summary>Top-left tally of fish eaten: large bones, then medium, then small (5 small = 1 medium, 10 medium = 1 large).</summary>
        void DrawBones(float s)
        {
            int large = eaten / (SmallPerMedium * MediumPerLarge);
            int medium = eaten / SmallPerMedium % MediumPerLarge;
            int small = eaten % SmallPerMedium;

            float x = FieldOnScreen.x + 14f * s, y = 14f * s;
            var bone = FishArt.Bone;
            void Row(int count, float width, float gap)
            {
                if (count == 0) return;
                float height = width * bone.height / bone.width;
                for (int i = 0; i < count; i++)
                    GUI.DrawTexture(new Rect(x + i * (width + gap), y, width, height), bone);
                y += height + 6f * s;
            }
            Row(large, 60f * s, 6f * s);
            Row(medium, 40f * s, 5f * s);
            Row(small, 24f * s, 4f * s);
        }

        /// <summary>The playing field in screen pixels. All UI stays inside it, never over the side panels.</summary>
        static Rect FieldOnScreen
        {
            get
            {
                float panel = Screen.width * (1f - FieldWidthFraction) / 2f;
                return new Rect(panel, 0f, Screen.width - panel * 2f, Screen.height);
            }
        }

        void OnGUI()
        {
            EnsureStyles();
            float s = guiScale, h = Screen.height;
            var field = FieldOnScreen;
            float fx = field.x, fw = field.width;
            var gold = new Color(1f, 0.8f, 0.3f);
            var info = new Color(0.8f, 0.95f, 1f);
            if (state != State.Title) DrawBones(s);

            switch (state)
            {
                case State.Title:
                    Fill(field, new Color(0f, 0.05f, 0.15f, 0.3f));
                    Text(new Rect(fx, h * 0.16f, fw, 90f * s), "LITTLE FISHY", big, gold);
                    Text(new Rect(fx, h * 0.38f, fw, 30f * s), "Eat fish smaller than you to grow.", small, Color.white);
                    Text(new Rect(fx, h * 0.38f + 32f * s, fw, 30f * s), "Touch a bigger fish and you're lunch.", small, Color.white);
                    Text(new Rect(fx, h * 0.38f + 64f * s, fw, 30f * s), "Grow into a massive goldfish to win.", small, Color.white);
                    Text(new Rect(fx, h * 0.38f + 112f * s, fw, 30f * s), "ARROWS / WASD to swim      Esc to pause", small, info);
                    if (MenuButton(h * 0.66f, "Play")) Restart();
                    if (MenuButton(h * 0.66f + 60f * s, "Main Menu")) Arcade.LoadHome();
                    break;

                case State.Paused:
                    Fill(field, new Color(0f, 0f, 0f, 0.45f));
                    Text(new Rect(fx, h * 0.25f, fw, 90f * s), "PAUSED", big, Color.white);
                    if (MenuButton(h * 0.25f + 120f * s, "Resume")) state = State.Playing;
                    if (MenuButton(h * 0.25f + 180f * s, "Restart")) Restart();
                    if (MenuButton(h * 0.25f + 240f * s, "Main Menu")) Arcade.LoadHome();
                    break;

                case State.GameOver:
                    Fill(field, new Color(0.2f, 0f, 0f, 0.3f));
                    Text(new Rect(fx, h * 0.3f, fw, 90f * s), "EATEN!", big, new Color(1f, 0.45f, 0.35f));
                    Text(new Rect(fx, h * 0.3f + 110f * s, fw, 40f * s), $"Fish eaten: {eaten}", medium, Color.white);
                    if (MenuButton(h * 0.3f + 200f * s, "Try Again")) Restart();
                    if (MenuButton(h * 0.3f + 260f * s, "Main Menu")) Arcade.LoadHome();
                    break;

                case State.Won:
                    Fill(field, new Color(0.15f, 0.1f, 0f, 0.3f));
                    Text(new Rect(fx, h * 0.3f, fw, 90f * s), "KING OF THE POND", big, gold);
                    Text(new Rect(fx, h * 0.3f + 115f * s, fw, 30f * s), "You ate everything. The pond will never recover.", small, Color.white);
                    if (MenuButton(h * 0.3f + 200f * s, "Play Again")) Restart();
                    if (MenuButton(h * 0.3f + 260f * s, "Main Menu")) Arcade.LoadHome();
                    break;
            }
        }
    }
}
