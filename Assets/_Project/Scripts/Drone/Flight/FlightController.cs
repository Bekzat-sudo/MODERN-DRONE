using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Betaflight-style flight controller: sticks -> target body rates (acro) or target attitude (angle),
    /// then a rate PID per axis. Output is desired angular acceleration in body space (rad/s^2).
    /// Body axes follow Unity: x = pitch (nose down +), y = yaw (right +), z = roll (left +).
    /// </summary>
    public sealed class FlightController
    {
        readonly DroneConfig _cfg;
        readonly PidController _pitch;
        readonly PidController _yaw;
        readonly PidController _roll;

        public Vector3 TargetRate { get; private set; }

        // Smoothed stick values (roll, pitch, yaw): PT1 low-pass like Betaflight RC smoothing.
        Vector3 _sticks;

        public FlightController(DroneConfig cfg)
        {
            _cfg = cfg;
            _pitch = new PidController(cfg.pitchPid);
            _yaw = new PidController(cfg.yawPid);
            _roll = new PidController(cfg.rollPid);
        }

        public void Reset()
        {
            _pitch.Reset();
            _yaw.Reset();
            _roll.Reset();
            TargetRate = Vector3.zero;
            _sticks = Vector3.zero;
        }

        /// <summary>First-order low-pass of the stick setpoints (no-op if the cutoff is 0).</summary>
        Vector3 SmoothSticks(in DroneInputState input, float dt)
        {
            var raw = new Vector3(input.roll, input.pitch, input.yaw);
            if (_cfg.setpointCutoffHz <= 0f) return _sticks = raw;
            float rc = 1f / (2f * Mathf.PI * _cfg.setpointCutoffHz);
            float k = dt / (dt + rc);
            _sticks += (raw - _sticks) * k;
            return _sticks;
        }

        /// <param name="omegaBody">Angular velocity in body space, rad/s.</param>
        /// <param name="worldUpInBody">World up vector expressed in body space.</param>
        public Vector3 Compute(in DroneInputState input, Vector3 omegaBody, Vector3 worldUpInBody, float dt)
        {
            Vector3 sticks = SmoothSticks(input, dt);
            float yawRate = RatesProfile.EvaluateRad(sticks.z, _cfg.yawRates);

            Vector3 target;
            if (input.mode == FlightMode.Acro)
            {
                float pitchRate = RatesProfile.EvaluateRad(sticks.y, _cfg.pitchRates);
                float rollRate = RatesProfile.EvaluateRad(sticks.x, _cfg.rollRates);
                target = new Vector3(pitchRate, yawRate, -rollRate);
            }
            else
            {
                Vector3 level = AngleModeRate(sticks.x, sticks.y, worldUpInBody,
                    _cfg.maxAngle, _cfg.angleGain, _cfg.angleMaxRate * Mathf.Deg2Rad);
                target = new Vector3(level.x, yawRate, level.z);
            }
            TargetRate = target;

            // I-term only accumulates with meaningful throttle, so it does not wind up on the ground.
            bool integrate = input.throttle > 0.05f;
            _pitch.Gains = _cfg.pitchPid;
            _yaw.Gains = _cfg.yawPid;
            _roll.Gains = _cfg.rollPid;
            return new Vector3(
                _pitch.Update(target.x, omegaBody.x, dt, integrate),
                _yaw.Update(target.y, omegaBody.y, dt, integrate),
                _roll.Update(target.z, omegaBody.z, dt, integrate));
        }

        /// <summary>
        /// Body rates (x and z only) that rotate the drone toward the stick-commanded attitude.
        /// A world-fixed vector seen from the body evolves as du/dt = -omega x u, so choosing
        /// omega along (uTarget x uCurrent) moves the current up vector toward the target.
        /// </summary>
        public static Vector3 AngleModeRate(float rollStick, float pitchStick, Vector3 worldUpInBody,
            float maxAngleDeg, float gain, float maxRateRad)
        {
            Quaternion desired = Quaternion.Euler(pitchStick * maxAngleDeg, 0f, -rollStick * maxAngleDeg);
            Vector3 targetUp = Quaternion.Inverse(desired) * Vector3.up;
            Vector3 currentUp = worldUpInBody.normalized;

            Vector3 axis = Vector3.Cross(targetUp, currentUp);
            float sin = axis.magnitude;
            if (sin < 1e-6f)
                return Vector3.zero;

            float angle = Mathf.Atan2(sin, Vector3.Dot(targetUp, currentUp));
            Vector3 rate = axis / sin * (angle * gain);
            rate.y = 0f;
            return Vector3.ClampMagnitude(rate, maxRateRad);
        }
    }
}
