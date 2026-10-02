using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Sounds of one kind of event, as data: takes for each layer (a blast's near crack and far boom, falling debris,
    /// a fire's loop, the pops and bangs of cooking-off ammunition) with volume, distance and pitch spread. Played by
    /// ModernDrone.Vehicles.SimpleAudio with outdoor propagation (delay, air absorption, echo).
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/Sound Bank", fileName = "SoundBank")]
    public sealed class SoundBank : ScriptableObject
    {
        [Tooltip("Close-range takes (crack, body, first debris).")]
        public AudioClip[] near = new AudioClip[0];
        [Tooltip("Close-range takes for small events (size below Small Size); empty = the near takes.")]
        public AudioClip[] small = new AudioClip[0];
        [Tooltip("Distant takes (low boom and its rolling echo); also used for the first echo.")]
        public AudioClip[] far = new AudioClip[0];
        [Tooltip("Falling debris after the blast.")]
        public AudioClip[] debris = new AudioClip[0];
        [Tooltip("Continuous loop (fire roar, torch).")]
        public AudioClip[] loop = new AudioClip[0];
        [Tooltip("Second loop layered over the first (crackle of cooking-off rounds).")]
        public AudioClip[] crackle = new AudioClip[0];
        [Tooltip("One-shot at the start (a flame jet lighting).")]
        public AudioClip[] start = new AudioClip[0];
        [Tooltip("Small random pops while it lasts (primers, small rounds).")]
        public AudioClip[] pops = new AudioClip[0];
        [Tooltip("Occasional bigger bangs while it lasts (a charge or shell going).")]
        public AudioClip[] bangs = new AudioClip[0];

        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Distance within which it plays at full volume, m (scaled by the cube root of the event size).")]
        public float minDistance = 22f;
        [Tooltip("Events smaller than this use the small takes (quieter, sharper).")]
        public float smallSize = 0.7f;
        [Tooltip("Events this big or bigger play the distant take at full volume.")]
        public float bigSize = 2f;
        public float pitchMin = 0.95f, pitchMax = 1.05f;
        [Tooltip("Pitch at size 0.3 and at size 3 (bigger events sound deeper).")]
        public float pitchSmall = 1.12f, pitchBig = 0.82f;
        [Range(0f, 1f)] public float echoVolume = 0.35f;
        [Range(0f, 1f)] public float debrisVolume = 0.55f;
        [Tooltip("Mean seconds between pops / chance of a bang instead.")]
        public float popInterval = 0.9f;
        [Range(0f, 1f)] public float bangChance = 0.12f;

        public static AudioClip Pick(AudioClip[] clips) =>
            clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    }
}
