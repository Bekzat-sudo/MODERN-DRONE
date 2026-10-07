using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>Spins propeller meshes from simulated motor speed. Order matches <see cref="QuadMixer"/>.</summary>
    public sealed class PropVisuals : MonoBehaviour
    {
        [SerializeField] DroneController drone;
        [Tooltip("Front-right, front-left, rear-right, rear-left.")]
        [SerializeField] Transform[] props = new Transform[QuadMixer.MotorCount];
        [Tooltip("Visual spin at full motor output, deg/s (kept low to avoid strobing).")]
        [SerializeField] float maxVisualSpin = 2400f;

        void Reset() => drone = GetComponentInParent<DroneController>();

        public void SetProps(Transform[] propTransforms) => props = propTransforms;

        /// <summary>Propeller transforms, front-right, front-left, rear-right, rear-left (for render-only copies).</summary>
        public Transform[] Props => props;
        public float MaxVisualSpin => maxVisualSpin;

        void LateUpdate()
        {
            if (drone == null) return;
            var spin = drone.MotorSpin;
            bool pusher = drone.Config != null && drone.Config.fixedWing; // one prop on the flight axis
            for (int i = 0; i < props.Length && i < spin.Length; i++)
                if (props[i] != null)
                {
                    var axis = pusher ? props[i].InverseTransformDirection(drone.transform.forward) : Vector3.up;
                    props[i].Rotate(axis, QuadMixer.Spin[i] * spin[i] * maxVisualSpin * Time.deltaTime, Space.Self);
                }
        }
    }
}
