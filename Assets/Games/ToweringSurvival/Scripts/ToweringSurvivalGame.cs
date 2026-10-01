using FlasshArcade;
using UnityEngine;
using UnityEngine.InputSystem;
using static FlasshArcade.ArcadeGui;

namespace ToweringSurvival
{
    /// <summary>
    /// Runs Towering Survival: Tetris-style pieces fall at random into a well that wraps left to right, while lava
    /// slowly rises from the floor. Climb the pile (you can wall-jump off blocks) as high as you can before the lava
    /// catches you. A piece landing on you crushes you.
    /// </summary>
    public class ToweringSurvivalGame : MonoBehaviour
    {
        enum State { Title, Playing, Paused, GameOver }

        const float ViewSize = 6f;           // camera orthographic size: 12 blocks tall
        const float FallSpeed = 2.5f;        // blocks per second
        // Balance: each piece is 4 blocks, so the pile rises on average 4 / (SpawnInterval * Stack.Width)
        // = 4 / (1.5 * 12) ≈ 0.22 rows per second. Lava must stay below that for the climb to be endless.
        const float SpawnInterval = 1.5f;    // seconds between pieces
        const float LavaStart = -3f;
        const float LavaGracePeriod = 30f;   // seconds before the lava starts rising
        const float LavaSpeed = 0.15f;       // blocks per second, constant once it starts (about 2/3 of the pile's growth)
        const float CullDepth = 20f;         // rows this far under the lava are deleted
        const int MaxLavaBubbles = 40;
        const string BestKey = "ToweringSurvival.BestHeight";

        Camera cam;
        Transform world;
        Stack stack;
        Climber climber;
        SpriteRenderer sky, lava, lavaLine, leftPanel, rightPanel, leftEdge, rightEdge;

        class LavaBubble { public SpriteRenderer Sr; public float X, Depth, Size, Speed, Pop; }
        readonly System.Collections.Generic.List<LavaBubble> bubbles = new System.Collections.Generic.List<LavaBubble>();
        float bubbleTimer;

        ArcadeAudio sounds;
        State state;
        float lavaHeight, playTime, cameraY;
        int height, best;
        /// <summary>The best height when this climb began, and whether the climb has passed it yet (for its sound).</summary>
        int bestAtStart;
        bool newBest;
        string deathReason;

        float WellCentre => Stack.Width / 2f;

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
            SetBackground(Color.black);

            world = new GameObject("World").transform;
            sky = AddSprite("Sky", TowerArt.Sky, -100, Color.white);
            // Opaque, so nothing shows outside the well (the climber's wrap copies rely on this).
            leftPanel = AddSprite("Left Panel", TowerArt.Pixel, 80, new Color(0.02f, 0.02f, 0.05f));
            rightPanel = AddSprite("Right Panel", TowerArt.Pixel, 80, new Color(0.02f, 0.02f, 0.05f));
            leftEdge = AddSprite("Left Edge", TowerArt.Pixel, 81, new Color(0.4f, 0.85f, 1f, 0.6f));
            rightEdge = AddSprite("Right Edge", TowerArt.Pixel, 81, new Color(0.4f, 0.85f, 1f, 0.6f));
            lava = AddSprite("Lava", TowerArt.Lava, 60, Color.white);
            lavaLine = AddSprite("Lava Line", TowerArt.Pixel, 62, new Color(1f, 0.12f, 0.08f));

            // The stone floor everything starts on (row -1 and below are solid).
            var floor = AddSprite("Floor", TowerArt.Pixel, 5, new Color(0.28f, 0.22f, 0.22f));
            floor.transform.position = new Vector3(WellCentre, -5f, 0f);
            floor.transform.localScale = new Vector3(Stack.Width, 10f, 1f);

            stack = new GameObject("Stack").AddComponent<Stack>();
            stack.transform.SetParent(world, false);
            climber = Climber.Create(world);
            best = PlayerPrefs.GetInt(BestKey, 0);
            // Sounds and music, once there are files for them (silent until then).
            sounds = ArcadeAudio.Create(gameObject, "ToweringSurvival", "Jump", "WallJump", "NewBest", "Crushed", "Burned");

            ResetGame();
            state = State.Title;
        }

        SpriteRenderer AddSprite(string name, Sprite sprite, int order, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(world, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = TowerArt.Material;
            sr.sortingOrder = order;
            sr.color = color;
            return sr;
        }

        void ResetGame()
        {
            stack.Clear();
            climber.ResetAt(new Vector2(WellCentre, 0f));
            lavaHeight = LavaStart;
            playTime = 0f;
            height = 0;
            bestAtStart = best;
            newBest = false;
            cameraY = ViewSize - 2f;
            deathReason = null;
        }

        void Restart()
        {
            ResetGame();
            state = State.Playing;
        }

        // ---------------------------------------------------------------- loop

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            var kb = Keyboard.current;

            switch (state)
            {
                case State.Title:
                    if (Pressed(kb, Key.Enter) || Pressed(kb, Key.Space))
                    {
                        Restart();
                        break;
                    }
                    if (Pressed(kb, Key.Escape))
                    {
                        Arcade.LoadHome();
                        return;
                    }
                    stack.Tick(dt, FallSpeed, SpawnInterval, CameraTop + 2f);
                    break;

                case State.Playing:
                    if (Pressed(kb, Key.Escape) || Pressed(kb, Key.P))
                    {
                        state = State.Paused;
                        break;
                    }
                    Play(dt, kb);
                    break;

                case State.Paused:
                    if (Pressed(kb, Key.Escape) || Pressed(kb, Key.P)) state = State.Playing;
                    else if (Pressed(kb, Key.R)) Restart();
                    return;

                case State.GameOver:
                    stack.Tick(dt, FallSpeed, SpawnInterval, CameraTop + 2f);
                    lavaHeight += LavaSpeed * dt;
                    climber.AnimateDeath(dt);
                    if (Pressed(kb, Key.Space) || Pressed(kb, Key.R)) Restart();
                    break;
            }

            UpdateCamera(dt);
            UpdateScenery(dt);
        }

        static bool Pressed(Keyboard kb, Key key) => kb != null && kb[key].wasPressedThisFrame;

        void Play(float dt, Keyboard kb)
        {
            playTime += dt;
            if (playTime > LavaGracePeriod) lavaHeight += LavaSpeed * dt;
            stack.Tick(dt, FallSpeed, SpawnInterval, CameraTop + 2f);
            stack.CullBelow(Mathf.FloorToInt(lavaHeight - CullDepth));

            // A piece that came down on the climber pushes it down; only being pinned against something crushes it.
            if (climber.ResolveFallingBlocks(stack))
            {
                sounds.Play("Crushed", 0.9f);
                Die("CRUSHED", Color.white);
                climber.Crush(); // squish flat, then pop into bubbles
                return;
            }

            float move = 0f;
            bool jumpPressed = false, jumpHeld = false;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) move -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) move += 1f;
                jumpPressed = kb.spaceKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
                jumpHeld = kb.spaceKey.isPressed || kb.upArrowKey.isPressed || kb.wKey.isPressed;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                float stick = pad.leftStick.ReadValue().x;
                if (Mathf.Abs(stick) > 0.3f) move = Mathf.Sign(stick);
                jumpPressed |= pad.buttonSouth.wasPressedThisFrame;
                jumpHeld |= pad.buttonSouth.isPressed;
            }

            climber.Tick(dt, stack, move, jumpPressed, jumpHeld);
            if (climber.Jumped) sounds.Play("Jump", 0.5f);
            if (climber.WallJumped) sounds.Play("WallJump", 0.5f);
            height = Mathf.Max(height, Mathf.FloorToInt(climber.Position.y));
            // Climbing past the best height so far (once a climb, and only if there was one to beat).
            if (!newBest && bestAtStart > 0 && height > bestAtStart)
            {
                newBest = true;
                sounds.Play("NewBest", 0.7f);
            }

            if (climber.Position.y < lavaHeight - 0.1f)
            {
                sounds.Play("Burned", 0.9f);
                Die("BURNED", new Color(0.25f, 0.1f, 0.05f));
            }
        }

        void Die(string reason, Color tint)
        {
            deathReason = reason;
            climber.SetTint(tint);
            state = State.GameOver;
            if (height > best)
            {
                best = height;
                PlayerPrefs.SetInt(BestKey, best);
                PlayerPrefs.Save();
            }
        }

        // ---------------------------------------------------------------- view

        float CameraTop => cameraY + ViewSize;

        void UpdateCamera(float dt)
        {
            // Follow the climber upwards (never showing much below the ground), easing so jumps don't jolt the view.
            float target = Mathf.Max(ViewSize - 2f, climber.Position.y + 1.5f);
            cameraY = Mathf.Lerp(cameraY, target, 1f - Mathf.Exp(-4f * dt));
            cam.transform.position = new Vector3(WellCentre, cameraY, -10f);
        }

        void UpdateScenery(float dt)
        {
            float halfH = ViewSize, halfW = ViewSize * cam.aspect;
            float bottom = cameraY - halfH, width = halfW * 2f + 1f;

            sky.transform.position = new Vector3(WellCentre, cameraY, 0f);
            sky.transform.localScale = new Vector3(width / 0.0625f, halfH * 2f + 1f, 1f); // the sky sprite is 1/16 unit wide

            // Dark panels outside the well, with glowing lines marking the edges you wrap through.
            float sideWidth = Mathf.Max(0f, halfW - WellCentre) + 0.5f;
            leftPanel.transform.position = new Vector3(-sideWidth / 2f, cameraY, 0f);
            rightPanel.transform.position = new Vector3(Stack.Width + sideWidth / 2f, cameraY, 0f);
            leftPanel.transform.localScale = rightPanel.transform.localScale = new Vector3(sideWidth, halfH * 2f + 1f, 1f);
            leftEdge.transform.position = new Vector3(0f, cameraY, 0f);
            rightEdge.transform.position = new Vector3(Stack.Width, cameraY, 0f);
            leftEdge.transform.localScale = rightEdge.transform.localScale = new Vector3(0.06f, halfH * 2f + 1f, 1f);

            // Lava fills from its surface down to the bottom of the view.
            float depth = Mathf.Max(0f, lavaHeight - bottom + 0.5f);
            lava.transform.position = new Vector3(WellCentre, lavaHeight, 0f);
            lava.transform.localScale = new Vector3(width / 0.0625f, depth, 1f);

            // A straight red line marks the surface.
            lavaLine.transform.position = new Vector3(WellCentre, lavaHeight, 0f);
            lavaLine.transform.localScale = new Vector3(width, 0.12f, 1f);

            UpdateLavaBubbles(dt, halfW, bottom);
        }

        /// <summary>Bubbles rise through the lava, swell as they near the surface and pop at the red line.</summary>
        void UpdateLavaBubbles(float dt, float halfW, float viewBottom)
        {
            bubbleTimer -= dt;
            if (bubbleTimer <= 0f && lavaHeight > viewBottom)
            {
                bubbleTimer = Random.Range(0.05f, 0.2f);
                var b = GetLavaBubble();
                if (b != null)
                {
                    b.X = WellCentre + Random.Range(-halfW, halfW);
                    b.Depth = Random.Range(0.4f, Mathf.Min(3f, lavaHeight - viewBottom));
                    b.Size = Random.Range(0.15f, 0.45f);
                    b.Speed = Random.Range(0.3f, 0.9f);
                    b.Pop = 0f;
                    b.Sr.enabled = true;
                }
            }

            foreach (var b in bubbles)
            {
                if (!b.Sr.enabled) continue;
                float size = b.Size, alpha = 1f;
                if (b.Depth > b.Size * 0.3f)
                {
                    b.Depth -= b.Speed * dt;
                    size *= Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(b.Depth / 3f)); // swell on the way up
                }
                else
                {
                    b.Pop += dt;
                    const float popTime = 0.2f;
                    if (b.Pop >= popTime)
                    {
                        b.Sr.enabled = false;
                        continue;
                    }
                    size *= 1f + b.Pop / popTime * 0.6f;
                    alpha = 1f - b.Pop / popTime;
                }
                b.Sr.transform.position = new Vector3(b.X, lavaHeight - b.Depth, 0f);
                b.Sr.transform.localScale = Vector3.one * size;
                b.Sr.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        LavaBubble GetLavaBubble()
        {
            foreach (var b in bubbles)
                if (!b.Sr.enabled) return b;
            if (bubbles.Count >= MaxLavaBubbles) return null;
            var created = new LavaBubble { Sr = AddSprite("Lava Bubble", TowerArt.LavaBubble, 61, Color.white) };
            bubbles.Add(created);
            return created;
        }

        // ---------------------------------------------------------------- HUD

        GUIStyle big, medium, small, left, button;
        float guiScale = -1f;

        /// <summary>The well in screen pixels. All UI is laid out inside it, never over the side panels.</summary>
        Rect WellOnScreen
        {
            get
            {
                float left = cam.WorldToScreenPoint(new Vector3(0f, cameraY, 0f)).x;
                float right = cam.WorldToScreenPoint(new Vector3(Stack.Width, cameraY, 0f)).x;
                return new Rect(left, 0f, right - left, Screen.height);
            }
        }

        void EnsureStyles(Rect well)
        {
            // Scale with the screen height, but shrink further if the well is too narrow for the text.
            float s = Mathf.Min(Screen.height / 720f, well.width / 640f);
            if (big != null && Mathf.Approximately(s, guiScale)) return;
            guiScale = s;

            var label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            big = new GUIStyle(label) { fontSize = Mathf.RoundToInt(56 * s) };
            medium = new GUIStyle(label) { fontSize = Mathf.RoundToInt(30 * s) };
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(19 * s), fontStyle = FontStyle.Normal };
            left = new GUIStyle(label) { fontSize = Mathf.RoundToInt(24 * s), alignment = TextAnchor.MiddleLeft, wordWrap = false };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold };
        }

        bool MenuButton(Rect well, float y, string text)
        {
            float s = guiScale;
            return GUI.Button(new Rect(well.center.x - 130f * s, y, 260f * s, 48f * s), text, button);
        }

        /// <summary>Sound and Music switches, under the pause menu's buttons, once there are files for them.</summary>
        void SoundSwitches(Rect well, float y, float step)
        {
            if (sounds.HasSounds)
            {
                if (MenuButton(well, y, sounds.Muted ? "Sound: Off" : "Sound: On")) sounds.ToggleSound();
                y += step;
            }
            if (sounds.HasMusic && MenuButton(well, y, sounds.MusicOff ? "Music: Off" : "Music: On")) sounds.ToggleMusic();
        }

        void OnGUI()
        {
            var well = WellOnScreen;
            EnsureStyles(well);
            float s = guiScale, h = Screen.height, pad = 12f * s;
            float x = well.x + pad, w = well.width - pad * 2f;
            var accent = new Color(0.55f, 1f, 0.45f);
            var info = new Color(0.7f, 0.9f, 1f);

            if (state != State.Title)
            {
                Text(new Rect(x, 10f * s, w, 32f * s), $"HEIGHT  {height} m", left, Color.white);
                Text(new Rect(x, 42f * s, w, 28f * s), $"BEST  {Mathf.Max(best, height)} m", left, new Color(0.75f, 0.75f, 0.8f));
            }

            switch (state)
            {
                case State.Title:
                    Fill(well, new Color(0f, 0f, 0f, 0.45f));
                    Text(new Rect(x, h * 0.1f, w, 140f * s), "TOWERING SURVIVAL", big, accent);
                    Text(new Rect(x, h * 0.36f, w, 52f * s), "Blocks fall from the sky. Climb them before the lava gets you.", small, Color.white);
                    Text(new Rect(x, h * 0.36f + 56f * s, w, 52f * s), "Slide down and jump off the sides of blocks, and don't get crushed.", small, Color.white);
                    Text(new Rect(x, h * 0.36f + 112f * s, w, 26f * s), "The left and right edges wrap around.", small, Color.white);
                    Text(new Rect(x, h * 0.36f + 150f * s, w, 26f * s), "A / D or ARROWS to move      SPACE / W / UP to jump", small, info);
                    Text(new Rect(x, h * 0.36f + 176f * s, w, 26f * s), "Hold towards a wall while jumping off it to climb      Esc to pause", small, info);
                    if (MenuButton(well, h * 0.74f, "Play")) Restart();
                    if (MenuButton(well, h * 0.74f + 58f * s, "Main Menu")) Arcade.LoadHome();
                    if (best > 0) Text(new Rect(x, h * 0.74f + 112f * s, w, 26f * s), $"Best: {best} m", small, Color.gray);
                    break;

                case State.Paused:
                    Fill(well, new Color(0f, 0f, 0f, 0.5f));
                    Text(new Rect(x, h * 0.25f, w, 90f * s), "PAUSED", big, Color.white);
                    if (MenuButton(well, h * 0.25f + 120f * s, "Resume")) state = State.Playing;
                    if (MenuButton(well, h * 0.25f + 180f * s, "Restart")) Restart();
                    if (MenuButton(well, h * 0.25f + 240f * s, "Main Menu")) Arcade.LoadHome();
                    SoundSwitches(well, h * 0.25f + 300f * s, 58f * s);
                    break;

                case State.GameOver:
                    Fill(well, new Color(0.2f, 0.03f, 0f, 0.35f));
                    Text(new Rect(x, h * 0.3f, w, 90f * s), deathReason ?? "GAME OVER", big, new Color(1f, 0.5f, 0.25f));
                    Text(new Rect(x, h * 0.3f + 110f * s, w, 40f * s), $"You climbed {height} m", medium, Color.white);
                    if (MenuButton(well, h * 0.3f + 200f * s, "Try Again")) Restart();
                    if (MenuButton(well, h * 0.3f + 260f * s, "Main Menu")) Arcade.LoadHome();
                    break;
            }
        }
    }
}
