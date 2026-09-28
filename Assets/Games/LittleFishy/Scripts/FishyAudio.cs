using UnityEngine;

namespace LittleFishy
{
    /// <summary>
    /// Little Fishy's sound effects, synthesized at startup. Music and an eating sound are still to be decided.
    /// </summary>
    public class FishyAudio : MonoBehaviour
    {
        const int Rate = 44100;

        AudioSource sfx;
        AudioClip death, win;

        void Awake()
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            death = MakeDeath();
            win = MakeWin();
        }

        public void PlayDeath() => sfx.PlayOneShot(death, 0.8f);

        public void PlayWin() => sfx.PlayOneShot(win, 0.7f);

        static AudioClip Clip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeDeath()
        {
            const float duration = 1.1f;
            var data = new float[(int)(duration * Rate)];
            float phase = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate, k = t / duration;
                float f = Mathf.Lerp(420f, 70f, k) * (1f + 0.08f * Mathf.Sin(t * 40f));
                phase += f / Rate;
                data[i] = (Mathf.Sin(2f * Mathf.PI * phase) + 0.3f * Mathf.Sin(4f * Mathf.PI * phase)) * (1f - k) * 0.6f;
            }
            return Clip("Death", data);
        }

        static AudioClip MakeWin()
        {
            int[] notes = { 72, 76, 79, 84, 79, 84, 88 };
            const float step = 0.11f;
            var data = new float[(int)((notes.Length * step + 0.6f) * Rate)];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * step * Rate);
                float f = 440f * Mathf.Pow(2f, (notes[n] - 69) / 12f);
                for (int i = start; i < data.Length; i++)
                {
                    float t = (i - start) / (float)Rate;
                    data[i] += Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 7f) * 0.3f;
                }
            }
            return Clip("Win", data);
        }
    }
}
