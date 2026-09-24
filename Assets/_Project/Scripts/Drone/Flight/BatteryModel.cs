using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Battery pack with an open-circuit voltage curve and internal-resistance sag.
    /// Thrust capability scales with (loaded voltage / full voltage)^2.
    /// </summary>
    public sealed class BatteryModel
    {
        // Per-cell open-circuit voltage by state of charge (generic Li-ion/LiPo blend).
        static readonly float[] SocPoints = { 0f, 0.05f, 0.15f, 0.30f, 0.50f, 0.70f, 0.85f, 1f };
        static readonly float[] OcvPoints = { 3.00f, 3.30f, 3.50f, 3.62f, 3.74f, 3.88f, 4.02f, 4.20f };

        public const float FullCellVoltage = 4.20f;

        readonly int _cells;
        readonly float _capacityMah;
        readonly float _internalResistance;

        public float UsedMah { get; private set; }
        public float Current { get; private set; }
        public float Voltage { get; private set; }

        public BatteryModel(int cells, float capacityMah, float internalResistanceOhm)
        {
            _cells = cells;
            _capacityMah = capacityMah;
            _internalResistance = internalResistanceOhm;
            Reset();
        }

        public int Cells => _cells;
        public float StateOfCharge => Mathf.Clamp01(1f - UsedMah / _capacityMah);
        public float CellVoltage => Voltage / _cells;
        public float RestingVoltage => OpenCircuitCellVoltage(StateOfCharge) * _cells;
        public bool Depleted => CellVoltage <= OcvPoints[0];

        /// <summary>Multiplier for maximum motor thrust given the current loaded voltage.</summary>
        public float ThrustFactor
        {
            get
            {
                float ratio = Voltage / (FullCellVoltage * _cells);
                return Mathf.Clamp01(ratio * ratio);
            }
        }

        public void Update(float currentAmps, float dt)
        {
            Current = Mathf.Max(0f, currentAmps);
            UsedMah = Mathf.Min(_capacityMah, UsedMah + Current * dt * (1000f / 3600f));
            Voltage = Mathf.Max(0f, RestingVoltage - Current * _internalResistance);
        }

        public void Reset()
        {
            UsedMah = 0f;
            Current = 0f;
            Voltage = RestingVoltage;
        }

        public static float OpenCircuitCellVoltage(float soc)
        {
            soc = Mathf.Clamp01(soc);
            for (int i = 1; i < SocPoints.Length; i++)
            {
                if (soc <= SocPoints[i])
                {
                    float t = Mathf.InverseLerp(SocPoints[i - 1], SocPoints[i], soc);
                    return Mathf.Lerp(OcvPoints[i - 1], OcvPoints[i], t);
                }
            }
            return OcvPoints[OcvPoints.Length - 1];
        }
    }
}
