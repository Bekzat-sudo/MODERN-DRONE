using System;
using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Per-axis stick-to-rate settings in Betaflight "Actual Rates" form (the Betaflight default since 4.3):
    /// rotation rate at centre stick, rate at full stick, and expo that shapes the curve between them.
    /// Source: betaflight/src/main/fc/rc.c applyActualRates(); ranges per oscarliang.com/rates.
    /// </summary>
    [Serializable]
    public struct AxisRates
    {
        [Tooltip("Centre sensitivity, deg/s (slope of the curve at centre stick).")]
        [Range(10f, 500f)] public float centerRate;
        [Tooltip("Rotation rate at full stick deflection, deg/s.")]
        [Range(50f, 1500f)] public float maxRate;
        [Tooltip("0..1: higher = softer around centre, same max rate.")]
        [Range(0f, 1f)] public float expo;

        public AxisRates(float centerRate, float maxRate, float expo)
        {
            this.centerRate = centerRate;
            this.maxRate = maxRate;
            this.expo = expo;
        }

        /// <summary>Rotation rate in deg/s at full stick deflection.</summary>
        public float MaxRateDeg => RatesProfile.EvaluateDeg(1f, this);
    }

    public static class RatesProfile
    {
        /// <summary>
        /// Maps a stick value in [-1, 1] to a rotation rate in deg/s:
        /// rate = x * center + max(0, max - center) * |x| * (x^5 * expo + x * (1 - expo)).
        /// </summary>
        public static float EvaluateDeg(float stick, AxisRates r)
        {
            float x = Mathf.Clamp(stick, -1f, 1f);
            float abs = Mathf.Abs(x);
            float x5 = x * x * x * x * x;
            float expof = abs * (x5 * r.expo + x * (1f - r.expo));
            float stickMovement = Mathf.Max(0f, r.maxRate - r.centerRate);
            return x * r.centerRate + stickMovement * expof;
        }

        public static float EvaluateRad(float stick, AxisRates r) => EvaluateDeg(stick, r) * Mathf.Deg2Rad;
    }
}