using System;
using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>Per-axis stick-to-rate settings using the Betaflight "BETAFLIGHT" rates formula.</summary>
    [Serializable]
    public struct AxisRates
    {
        [Range(0.01f, 2.55f)] public float rcRate;
        [Range(0f, 0.99f)] public float superRate;
        [Range(0f, 1f)] public float expo;

        public AxisRates(float rcRate, float superRate, float expo)
        {
            this.rcRate = rcRate;
            this.superRate = superRate;
            this.expo = expo;
        }

        /// <summary>Rotation rate in deg/s at full stick deflection.</summary>
        public float MaxRateDeg => RatesProfile.EvaluateDeg(1f, this);
    }

    public static class RatesProfile
    {
        const float MaxRate = 1998f;

        /// <summary>
        /// Maps a stick value in [-1, 1] to a rotation rate in deg/s, matching Betaflight's
        /// applyBetaflightRates(): expo on |stick|^3, RC rate scaled by 200, super rate as 1/(1 - |stick|*sr).
        /// </summary>
        public static float EvaluateDeg(float stick, AxisRates r)
        {
            stick = Mathf.Clamp(stick, -1f, 1f);
            float abs = Mathf.Abs(stick);

            float cmd = stick;
            if (r.expo > 0f)
                cmd = stick * abs * abs * abs * r.expo + stick * (1f - r.expo);

            float rcRate = r.rcRate;
            if (rcRate > 2f)
                rcRate += (rcRate - 2f) * 14.54f;

            float rate = 200f * rcRate * cmd;
            if (r.superRate > 0f)
                rate *= 1f / Mathf.Clamp(1f - abs * r.superRate, 0.01f, 1f);

            return Mathf.Clamp(rate, -MaxRate, MaxRate);
        }

        public static float EvaluateRad(float stick, AxisRates r) => EvaluateDeg(stick, r) * Mathf.Deg2Rad;
    }
}
