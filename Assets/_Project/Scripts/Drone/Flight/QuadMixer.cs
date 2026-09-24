using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Quad-X mixer in thrust space. Motor order: 0 front-right, 1 front-left, 2 rear-right, 3 rear-left.
    /// Body axes follow Unity: +x right, +y up, +z forward. Torque about +x pitches the nose down,
    /// about +y yaws right, about +z rolls left.
    /// </summary>
    public static class QuadMixer
    {
        public const int MotorCount = 4;

        /// <summary>+1 for front motors, -1 for rear.</summary>
        public static readonly float[] Front = { 1f, 1f, -1f, -1f };

        /// <summary>+1 for right motors, -1 for left.</summary>
        public static readonly float[] Right = { 1f, -1f, 1f, -1f };

        /// <summary>Sign of the reaction (yaw) torque each motor produces per newton of thrust.</summary>
        public static readonly float[] Spin = { 1f, -1f, -1f, 1f };

        /// <summary>Motor position in body space; <paramref name="armOffset"/> is the per-axis offset (diagonal / (2*sqrt2)).</summary>
        public static Vector3 MotorPosition(int i, float armOffset) =>
            new Vector3(Right[i] * armOffset, 0f, Front[i] * armOffset);

        /// <summary>
        /// Solves per-motor thrust for a collective thrust and body torque, with airmode-style saturation:
        /// torque authority is kept by scaling the differential part to fit, then shifting the collective.
        /// </summary>
        public static void Mix(float collective, Vector3 torque, float armOffset, float yawCoefficient, float maxThrust, float[] thrust)
        {
            float pitchTerm = -torque.x / (4f * armOffset);
            float rollTerm = torque.z / (4f * armOffset);
            float yawTerm = torque.y / (4f * yawCoefficient);

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < MotorCount; i++)
            {
                float d = Front[i] * pitchTerm + Right[i] * rollTerm + Spin[i] * yawTerm;
                thrust[i] = d;
                if (d < min) min = d;
                if (d > max) max = d;
            }

            float span = max - min;
            if (span > maxThrust)
            {
                float scale = maxThrust / span;
                for (int i = 0; i < MotorCount; i++) thrust[i] *= scale;
                min *= scale;
                max *= scale;
            }

            float perMotor = Mathf.Clamp(collective * 0.25f, -min, maxThrust - max);
            for (int i = 0; i < MotorCount; i++)
                thrust[i] = Mathf.Clamp(perMotor + thrust[i], 0f, maxThrust);
        }

        /// <summary>Body torque produced by the given motor thrusts (inverse of <see cref="Mix"/> when unsaturated).</summary>
        public static Vector3 TorqueFromThrust(float[] thrust, float armOffset, float yawCoefficient)
        {
            Vector3 t = Vector3.zero;
            for (int i = 0; i < MotorCount; i++)
            {
                t.x -= armOffset * Front[i] * thrust[i];
                t.y += yawCoefficient * Spin[i] * thrust[i];
                t.z += armOffset * Right[i] * thrust[i];
            }
            return t;
        }
    }
}
