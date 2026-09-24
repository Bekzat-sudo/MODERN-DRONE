using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Video
{
    /// <summary>Electronic-warfare emitter. Registered globally so drones can sample all active jammers.</summary>
    public sealed class SignalJammer : MonoBehaviour
    {
        static readonly List<SignalJammer> Active = new List<SignalJammer>();

        [SerializeField] float radius = 300f;
        [Range(0f, 1f)] [SerializeField] float strength = 0.9f;
        [SerializeField] JamBands bands = JamBands.Both;
        [Tooltip("Emitting point (antenna); defaults to this transform.")]
        [SerializeField] Transform antenna;

        public static IReadOnlyList<SignalJammer> All => Active;
        public float Radius => radius;
        public float Strength => strength;
        public JamBands Bands => bands;
        public Vector3 Position => (antenna != null ? antenna : transform).position;

        /// <summary>Switched off when the station is destroyed.</summary>
        public bool Emitting { get; set; } = true;

        /// <summary>Fraction of emitter power left (damaged antennas radiate less).</summary>
        public float PowerScale { get; set; } = 1f;

        public void Configure(float radius, float strength, JamBands bands, Transform antenna)
        {
            this.radius = radius;
            this.strength = strength;
            this.bands = bands;
            this.antenna = antenna;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public JamSample Sample(Vector3 dronePosition) => new JamSample
        {
            strength = Emitting ? strength * Mathf.Clamp01(PowerScale) : 0f,
            radius = radius,
            distanceToDrone = Vector3.Distance(Position, dronePosition),
            bands = bands
        };

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.5f);
            Gizmos.DrawWireSphere(Position, radius);
            Gizmos.DrawWireSphere(Position, radius * 0.3f);
        }
    }
}
