using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// One wind for the whole scene, used by smoke, dust and vegetation alike. Mean wind plus slow gusts.
    /// Default is a gentle breeze of 3.5 m/s at 10 m (Beaufort 3, "gentle breeze" 3.4-5.4 m/s,
    /// en.wikipedia.org/wiki/Beaufort_scale); gusts of +-35 % over ~10-20 s are a typical gust factor for open
    /// farmland (abstract: gameplay choice, not measured). Near the ground the wind is weaker: log wind profile with a
    /// roughness length of 0.1 m (crops / grassland, en.wikipedia.org/wiki/Log_wind_profile).
    /// Shader globals: _WindVector (xyz = velocity at 10 m now, w = gustiness 0..1), _FxTime (scaled game time, frozen
    /// with the world during the killcam replay).
    /// </summary>
    public static class Wind
    {
        static readonly int WindVectorId = Shader.PropertyToID("_WindVector");
        static readonly int FxTimeId = Shader.PropertyToID("_FxTime");

        public const float Roughness = 0.1f;
        public const float ReferenceHeight = 10f;

        /// <summary>Mean wind at 10 m, m/s (direction the air moves to).</summary>
        public static Vector3 Mean { get; set; } = new Vector3(2.6f, 0f, 2.3f);

        /// <summary>Game time that only runs while the world does (Time.time also stops at timeScale 0).</summary>
        public static float Time => UnityEngine.Time.time;

        /// <summary>Wind at 10 m at game time <paramref name="t"/>: the mean with slow, smooth gusts.</summary>
        public static Vector3 At(float t)
        {
            float gust = 1f + 0.35f * (Mathf.PerlinNoise(t * 0.07f, 0.3f) * 2f - 1f);
            float veer = (Mathf.PerlinNoise(0.8f, t * 0.05f) * 2f - 1f) * 15f; // the direction wanders +-15 deg
            return Quaternion.Euler(0f, veer, 0f) * Mean * gust;
        }

        /// <summary>Wind now at height <paramref name="height"/> above the ground (log profile, calm below the roughness).</summary>
        public static Vector3 At(float t, float height) => At(t) * HeightFactor(height);

        public static float HeightFactor(float height) =>
            height <= Roughness ? 0f : Mathf.Log(height / Roughness) / Mathf.Log(ReferenceHeight / Roughness);

        public static Vector3 Current => At(Time);

        /// <summary>Pushes the wind and the effects clock to shaders (called every frame by <see cref="FxClock"/>).</summary>
        public static void Publish()
        {
            Vector3 w = Current;
            float gustiness = Mathf.PerlinNoise(Time * 0.9f, 4.2f);
            Shader.SetGlobalVector(WindVectorId, new Vector4(w.x, w.y, w.z, gustiness));
            Shader.SetGlobalFloat(FxTimeId, Time);
        }
    }

    /// <summary>Per-frame host for the shader globals of <see cref="Wind"/> and <see cref="Shockwaves"/>.</summary>
    public sealed class FxClock : MonoBehaviour
    {
        static FxClock _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();

        // Play mode may start without a domain reload: drop blast waves and smoke from the last session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Shockwaves.Clear();
            SmokeVolumes.Clear();
        }

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("FxClock") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FxClock>();
        }

        void Update()
        {
            Wind.Publish();
            Shockwaves.Publish(UnityEngine.Time.time);
            SmokeVolumes.Prune(UnityEngine.Time.time);
        }
    }
}
