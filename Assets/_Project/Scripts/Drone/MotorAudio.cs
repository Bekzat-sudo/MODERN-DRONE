using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Procedural motor sound: a harmonic buzz whose pitch follows the average motor speed, plus prop-wash noise.
    /// Generated on the audio thread, so no audio assets are needed.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MotorAudio : MonoBehaviour
    {
        [SerializeField] DroneController drone;
        [SerializeField] float idleFrequency = 90f;
        [SerializeField] float maxFrequency = 420f;
        [SerializeField] float volume = 0.35f;

        // Written on the main thread, read on the audio thread.
        volatile float _targetFreq;
        volatile float _targetGain;
        float _freq, _gain, _phase;
        uint _noise = 22222;
        int _sampleRate;

        void Reset() => drone = GetComponentInParent<DroneController>();

        void Awake()
        {
            if (drone == null) drone = GetComponentInParent<DroneController>();
            _sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true;
            src.loop = true;
            src.spatialBlend = 0f; // pilot hears his own drone "in the goggles"
            if (!src.isPlaying) src.Play();
        }

        void Update()
        {
            if (drone == null) return;
            var spin = drone.MotorSpin;
            float avg = 0f;
            for (int i = 0; i < spin.Length; i++) avg += spin[i];
            avg /= Mathf.Max(1, spin.Length);
            _targetFreq = Mathf.Lerp(idleFrequency, maxFrequency, avg);
            _targetGain = avg > 0.01f ? volume * Mathf.Lerp(0.25f, 1f, avg) : 0f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float step = 1f / Mathf.Max(1, _sampleRate);
            for (int i = 0; i < data.Length; i += channels)
            {
                _freq += (_targetFreq - _freq) * 0.0015f;
                _gain += (_targetGain - _gain) * 0.002f;
                _phase += _freq * step;
                if (_phase > 1f) _phase -= 1f;

                // Fundamental + harmonics (motor/prop blade pass) and a little white noise for prop wash.
                float t = _phase * Mathf.PI * 2f;
                float tone = Mathf.Sin(t) * 0.55f + Mathf.Sin(t * 2f) * 0.25f + Mathf.Sin(t * 3f) * 0.12f + Mathf.Sin(t * 4f) * 0.06f;
                _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
                float n = (_noise / (float)uint.MaxValue) * 2f - 1f;
                float s = (tone + n * 0.12f) * _gain;
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
