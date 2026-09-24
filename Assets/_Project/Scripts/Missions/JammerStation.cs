using ModernDrone.Damage;
using ModernDrone.Video;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>Ties an EW emitter to its vehicle: it stops jamming once its antennas or generator are knocked out.</summary>
    [RequireComponent(typeof(VehicleDamageModel), typeof(SignalJammer))]
    public sealed class JammerStation : MonoBehaviour
    {
        VehicleDamageModel _model;
        SignalJammer _jammer;

        void Awake() => Bind();

        void Bind()
        {
            _model = GetComponent<VehicleDamageModel>();
            _jammer = GetComponent<SignalJammer>();
        }

        void Update() => Refresh();

        /// <summary>Recomputes emitter state from module damage.</summary>
        public void Refresh()
        {
            if (_model == null) Bind();
            bool silenced = (_model.State & (KillState.Sensors | KillState.Destroyed)) != 0;
            bool powerLost = false;
            float antennas = 0f, radiating = 0f;
            foreach (var m in _model.Modules)
            {
                if (m.Type == ModuleType.Generator && m.State == ModuleState.Destroyed) powerLost = true;
                if (m.Type != ModuleType.Antenna) continue;
                antennas += 1f;
                radiating += m.State == ModuleState.Operational ? 1f : m.State == ModuleState.Damaged ? 0.5f : 0f;
            }
            _jammer.Emitting = !silenced && !powerLost;
            _jammer.PowerScale = antennas > 0f ? radiating / antennas : 1f;
        }
    }
}
