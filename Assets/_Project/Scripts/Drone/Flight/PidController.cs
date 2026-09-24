using System;
using UnityEngine;

namespace ModernDrone.Drone
{
    [Serializable]
    public struct PidGains
    {
        public float kp;
        public float ki;
        public float kd;
        [Tooltip("Clamp for the accumulated integral term (output units).")]
        public float iLimit;

        public PidGains(float kp, float ki, float kd, float iLimit)
        {
            this.kp = kp;
            this.ki = ki;
            this.kd = kd;
            this.iLimit = iLimit;
        }
    }

    /// <summary>PID with derivative on measurement (no setpoint kick) and a clamped integral.</summary>
    public sealed class PidController
    {
        public PidGains Gains;

        float _integral;
        float _prevMeasurement;
        bool _hasPrev;

        public PidController(PidGains gains) => Gains = gains;

        public float Integral => _integral;

        public float Update(float setpoint, float measurement, float dt, bool integrate)
        {
            float error = setpoint - measurement;

            if (integrate)
                _integral = Mathf.Clamp(_integral + error * Gains.ki * dt, -Gains.iLimit, Gains.iLimit);
            else
                _integral = 0f;

            float derivative = _hasPrev && dt > 0f ? -(measurement - _prevMeasurement) / dt : 0f;
            _prevMeasurement = measurement;
            _hasPrev = true;

            return Gains.kp * error + _integral + Gains.kd * derivative;
        }

        public void Reset()
        {
            _integral = 0f;
            _hasPrev = false;
        }
    }
}
