using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>Procedurally generated one-shot sounds (explosions, gunshots) played in 3D at a position.</summary>
    public static class SimpleAudio
    {
        static AudioClip _explosion;
        static AudioClip _shot;

        public static void Explosion(Vector3 position, float size) =>
            Play(position, _explosion != null ? _explosion : _explosion = Burst("Explosion", 2.2f, 0.004f, 1.6f), Mathf.Clamp01(0.5f + size * 0.25f), 12f * size);

        public static void Shot(Vector3 position) =>
            Play(position, _shot != null ? _shot : _shot = Burst("Shot", 0.35f, 0.0005f, 0.12f), 0.6f, 5f);

        static void Play(Vector3 position, AudioClip clip, float volume, float minDistance)
        {
            var go = new GameObject("SFX_" + clip.name);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.spatialBlend = 1f;
            src.minDistance = Mathf.Max(1f, minDistance);
            src.maxDistance = 1500f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        /// <summary>Low-passed noise with a sharp attack and exponential decay.</summary>
        static AudioClip Burst(string name, float seconds, float attack, float decay)
        {
            int rate = AudioSettings.outputSampleRate;
            int n = Mathf.CeilToInt(seconds * rate);
            var samples = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = (t < attack ? t / attack : 1f) * Mathf.Exp(-(t - attack) / decay);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Lower cutoff as the sound decays: a boom that rumbles out.
                float k = Mathf.Lerp(0.5f, 0.04f, Mathf.Clamp01(t / seconds));
                lp += (white - lp) * k;
                samples[i] = Mathf.Clamp(lp * env * 3f, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
