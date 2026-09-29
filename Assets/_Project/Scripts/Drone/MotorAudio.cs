using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Procedural motor sound, one voice per motor so the four slightly different speeds beat against each other.
    /// Each voice is the blade-pass tone BPF = blades x rpm / 60 with its harmonics (rpm = spin x KV x pack voltage,
    /// ~80 % of no-load speed under load), a thin electrical whine at the commutation frequency (rpm / 60 x 7 pole pairs,
    /// 12N14P motors), and broadband prop-wash noise that grows with thrust. Small drones put their dominant tones at the
    /// blade-pass frequency with strong harmonics to several kHz and turbulence noise to ~8 kHz (acentech.com
    /// "Drone Noise"; frontiersin.org 2022 low-noise propeller study). Heard "in the goggles": an onboard microphone
    /// feeding the analog VTX audio channel (oscarliang.com/microphone-fpv).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MotorAudio : MonoBehaviour
    {
        const int Motors = 4;
        const int PolePairs = 7;

        [SerializeField] DroneController drone;
        [SerializeField] float volume = 0.35f;

        // Written on the main thread, read on the audio thread.
        readonly float[] _targetFreq = new float[Motors];
        volatile float _targetGain, _targetWash, _whineRatio = PolePairs / 3f;
        readonly float[] _freq = new float[Motors];
        readonly float[] _phase = new float[Motors];
        readonly float[] _whine = new float[Motors];
        float _gain, _wash, _lp, _lp2;
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
            src.spatialBlend = 0f; // the pilot hears the drone's microphone in the goggles
            if (!src.isPlaying) src.Play();
        }

        void Update()
        {
            if (drone == null || drone.Config == null) return;
            var cfg = drone.Config;
            var spin = drone.MotorSpin;
            float rpmMax = cfg.motorKv * cfg.cells * 3.7f * 0.8f;
            float avg = 0f;
            for (int i = 0; i < Motors; i++)
            {
                float s = i < spin.Length ? spin[i] : 0f;
                avg += s;
                _targetFreq[i] = Mathf.Max(20f, cfg.propBlades * s * rpmMax / 60f);
            }
            avg /= Motors;
            _whineRatio = PolePairs / (float)Mathf.Max(1, cfg.propBlades);
            float master = volume * ControlSettings.Current.motorVolume;
            _targetGain = avg > 0.01f ? master * Mathf.Lerp(0.3f, 1f, avg) : 0f;
            _targetWash = avg > 0.01f ? master * avg * avg : 0f; // prop wash ~ thrust ~ spin^2
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float step = 1f / Mathf.Max(1, _sampleRate);
            for (int i = 0; i < data.Length; i += channels)
            {
                _gain += (_targetGain - _gain) * 0.002f;
                _wash += (_targetWash - _wash) * 0.002f;
                float tone = 0f;
                for (int m = 0; m < Motors; m++)
                {
                    _freq[m] += (_targetFreq[m] - _freq[m]) * 0.0015f;
                    _phase[m] += _freq[m] * step;
                    if (_phase[m] > 1f) _phase[m] -= 1f;
                    // Commutation whine: pole pairs x rotation rate = PolePairs / blades x BPF.
                    _whine[m] += _freq[m] * _whineRatio * step;
                    if (_whine[m] > 1f) _whine[m] -= 1f;
                    float t = _phase[m] * Mathf.PI * 2f;
                    tone += Mathf.Sin(t) * 0.5f + Mathf.Sin(t * 2f) * 0.28f + Mathf.Sin(t * 3f) * 0.16f + Mathf.Sin(t * 4f) * 0.08f
                          + Mathf.Sin(_whine[m] * Mathf.PI * 2f) * 0.04f;
                }
                _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
                float n = (_noise / (float)uint.MaxValue) * 2f - 1f;
                // Turbulence: band-limited noise (two one-pole low-passes, ~1-6 kHz body).
                _lp += (n - _lp) * 0.5f;
                _lp2 += (_lp - _lp2) * 0.08f;
                float s = tone * 0.25f * _gain + (_lp - _lp2) * 0.9f * _wash;
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
