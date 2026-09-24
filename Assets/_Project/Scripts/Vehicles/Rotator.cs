using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>Spins a part (search radar) while its vehicle's sensors are working.</summary>
    public sealed class Rotator : MonoBehaviour
    {
        [SerializeField] float degreesPerSecond = 60f;
        VehicleDamageModel _model;

        void Awake() => _model = GetComponentInParent<VehicleDamageModel>();

        void Update()
        {
            if (_model != null && (_model.State & (KillState.Sensors | KillState.Destroyed)) != 0) return;
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
