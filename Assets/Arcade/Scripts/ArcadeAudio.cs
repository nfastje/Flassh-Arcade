using System.Collections.Generic;
using UnityEngine;

namespace FlasshArcade
{
    /// <summary>
    /// A game's sound effects and music, ready for audio files that haven't been made yet. A game names the
    /// moments that deserve a sound and calls <see cref="Play"/> at each; the sound plays the clip of that name from
    /// Resources/&lt;Game&gt;/Sounds (for example Assets/Games/PlanetCrasher/Resources/PlanetCrasher/Sounds/Death.wav), if
    /// there is one. Music is one track, Resources/&lt;Game&gt;/Music/Theme, played on a loop. With no files there, the
    /// game is silent and its pause menu shows no Sound or Music switch. Switching either off is remembered per game.
    /// </summary>
    public class ArcadeAudio : MonoBehaviour
    {
        const float MusicVolume = 0.35f;
        /// <summary>Real seconds before the same sound can play again, so a burst of events doesn't stack up.</summary>
        const float MinGap = 0.05f;

        string game;
        AudioSource source, music;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

        public bool Muted { get; private set; }
        public bool MusicOff { get; private set; }

        /// <summary>Whether any of the game's sounds exist (and so anything for a Sound switch to do).</summary>
        public bool HasSounds { get; private set; }

        /// <summary>Whether there's a music track (and so anything for a Music switch to do).</summary>
        public bool HasMusic => music != null && music.clip != null;

        string SoundOffKey => game + ".SoundOff";
        string MusicOffKey => game + ".MusicOff";

        /// <summary>
        /// Adds a game's audio to <paramref name="host"/>: <paramref name="game"/> names its Resources folder, and
        /// <paramref name="sounds"/> lists every sound it may play (each looked for once, here).
        /// </summary>
        public static ArcadeAudio Create(GameObject host, string game, params string[] sounds)
        {
            var audio = host.AddComponent<ArcadeAudio>();
            audio.Init(game, sounds);
            return audio;
        }

        void Init(string gameName, string[] sounds)
        {
            game = gameName;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            Muted = PlayerPrefs.GetInt(SoundOffKey, 0) == 1;
            foreach (var name in sounds)
            {
                var clip = Resources.Load<AudioClip>($"{game}/Sounds/{name}");
                clips[name] = clip;
                HasSounds |= clip != null;
            }

            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.volume = MusicVolume;
            music.clip = Resources.Load<AudioClip>($"{game}/Music/Theme");
            MusicOff = PlayerPrefs.GetInt(MusicOffKey, 0) == 1;
            if (HasMusic && !MusicOff) music.Play();
        }

        /// <summary>Plays one of the game's sounds, if it exists and sound is on.</summary>
        public void Play(string sound, float volume = 0.7f)
        {
            if (Muted || !clips.TryGetValue(sound, out var clip) || clip == null) return;
            if (lastPlayed.TryGetValue(sound, out float last) && Time.unscaledTime - last < MinGap) return;
            lastPlayed[sound] = Time.unscaledTime;
            source.PlayOneShot(clip, volume);
        }

        public void ToggleSound()
        {
            Muted = !Muted;
            PlayerPrefs.SetInt(SoundOffKey, Muted ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void ToggleMusic()
        {
            MusicOff = !MusicOff;
            PlayerPrefs.SetInt(MusicOffKey, MusicOff ? 1 : 0);
            PlayerPrefs.Save();
            if (!HasMusic) return;
            if (MusicOff) music.Stop();
            else if (!music.isPlaying) music.Play();
        }
    }
}
