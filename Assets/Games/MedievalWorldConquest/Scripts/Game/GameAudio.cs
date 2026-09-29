using UnityEngine;

namespace MedievalWorldConquest
{
    /// <summary>
    /// Medieval World Conquest's sound effects. The game calls <see cref="Play"/> at the moments that deserve a
    /// sound (construction or research finished, a battle report won or lost, an attack seen coming, coins or
    /// merchants' goods); each plays the clip of the same name from Resources/MedievalWorldConquest/Sounds (for
    /// example "Incoming.wav"), if there is one. With no clips there, the game is silent (as it is for now) and the
    /// menu has no sound switch. Each sound waits a moment before it can play again, so a fast world doesn't turn
    /// into a racket. Switching sound off is remembered.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public enum Sound { BuildDone, ResearchDone, Victory, Defeat, Incoming, Coins }

        const string ClipPath = "MedievalWorldConquest/Sounds/";
        const string MutedKey = "MedievalWorldConquest.SoundOff";
        const float MinGap = 0.6f; // real seconds before the same sound can play again

        AudioSource source;
        AudioClip[] clips;
        float[] lastPlayed;

        public bool Muted { get; private set; }

        /// <summary>Whether there are any sounds to play (and so anything for the menu's switch to do).</summary>
        public bool HasSounds { get; private set; }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            Muted = PlayerPrefs.GetInt(MutedKey, 0) == 1;
            var sounds = (Sound[])System.Enum.GetValues(typeof(Sound));
            clips = new AudioClip[sounds.Length];
            lastPlayed = new float[sounds.Length];
            foreach (var s in sounds)
            {
                clips[(int)s] = Resources.Load<AudioClip>(ClipPath + s);
                HasSounds |= clips[(int)s] != null;
                lastPlayed[(int)s] = -10f;
            }
        }

        public void Play(Sound sound, float volume = 0.6f)
        {
            if (Muted || clips == null) return;
            int i = (int)sound;
            if (clips[i] == null || Time.unscaledTime - lastPlayed[i] < MinGap) return;
            lastPlayed[i] = Time.unscaledTime;
            source.PlayOneShot(clips[i], volume);
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
