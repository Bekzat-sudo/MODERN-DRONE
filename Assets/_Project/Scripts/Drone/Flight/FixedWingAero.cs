using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Fixed-wing aerodynamics as plain rules (no scene access): lift from the angle of attack with a stall, parabolic
    /// drag polar, a propeller whose thrust is capped by its static thrust at low speed and by shaft power at high speed,
    /// and the weathervane moments that point the nose into the airflow. Body axes follow Unity: x right, y up, z forward.
    /// Standard small-UAV relations (Anderson, "Introduction to Flight": CL = CL0 + a*alpha, CD = CD0 + CL^2 / (pi e AR)).
    /// </summary>
    public static class FixedWingAero
    {
        public struct Result
        {
            /// <summary>Aerodynamic + thrust force in body space, N.</summary>
            public Vector3 force;
            /// <summary>Weathervane moments in body space (x pitch nose-down +, y yaw right +), N*m.</summary>
            public Vector3 torque;
            public float alpha, sideslip, liftCoefficient;
        }

        static float Density => Core.SimConstants.AirDensitySeaLevel;

        public static float AspectRatio(DroneConfig c) => c.wingSpan * c.wingSpan / c.wingArea;

        /// <summary>Lift coefficient at angle of attack <paramref name="alpha"/> (rad): linear up to CLmax, then a stall drop.</summary>
        public static float LiftCoefficient(DroneConfig c, float alpha)
        {
            float cl = c.liftAtZeroAlpha + c.liftSlope * alpha;
            float stallAlpha = (c.maxLift - c.liftAtZeroAlpha) / c.liftSlope;
            if (alpha > stallAlpha)
                cl = Mathf.Lerp(c.maxLift, 0.6f * c.maxLift, Mathf.InverseLerp(stallAlpha, stallAlpha + 0.25f, alpha));
            else if (alpha < -stallAlpha)
                cl = -Mathf.Lerp(c.maxLift, 0.6f * c.maxLift, Mathf.InverseLerp(-stallAlpha, -stallAlpha - 0.25f, alpha));
            return cl;
        }

        public static float DragCoefficient(DroneConfig c, float cl) =>
            c.zeroLiftDrag + cl * cl / (Mathf.PI * c.oswald * AspectRatio(c));

        /// <summary>Propeller thrust along the body axis, N: throttle times min(static thrust, shaft power / airspeed).</summary>
        public static float Thrust(DroneConfig c, float airspeed, float throttle) =>
            Mathf.Clamp01(throttle) * Mathf.Min(c.staticThrust, c.thrustPower / Mathf.Max(airspeed, 1f));

        /// <summary>Forces and weathervane moments for a body-space air velocity (thrust excluded).</summary>
        public static Result Aero(DroneConfig c, Vector3 vBody)
        {
            var r = new Result();
            float v = vBody.magnitude;
            if (v < 0.5f) return r;
            float q = 0.5f * Density * v * v;
            r.alpha = Mathf.Atan2(-vBody.y, vBody.z);
            r.sideslip = Mathf.Atan2(vBody.x, Mathf.Sqrt(vBody.y * vBody.y + vBody.z * vBody.z));
            r.liftCoefficient = LiftCoefficient(c, r.alpha);

            Vector3 dir = vBody / v;
            Vector3 liftDir = Vector3.Cross(dir, Vector3.right).normalized; // perpendicular to the flow, in the symmetry plane
            float S = c.wingArea;
            r.force = liftDir * (q * S * r.liftCoefficient)
                      - dir * (q * S * DragCoefficient(c, r.liftCoefficient))
                      - Vector3.right * (q * S * c.sideForceSlope * r.sideslip);
            // Tail and fin turn the nose into the flow: angle of attack pitches it down, sideslip yaws it toward the flow.
            r.torque = new Vector3(q * S * c.pitchStability * r.alpha, q * S * c.yawStability * r.sideslip, 0f);
            return r;
        }

        /// <summary>Drag in steady level flight at <paramref name="airspeed"/> (lift = weight), N.</summary>
        public static float LevelDrag(DroneConfig c, float mass, float airspeed)
        {
            float q = 0.5f * Density * airspeed * airspeed;
            float cl = mass * Core.SimConstants.Gravity / (q * c.wingArea);
            return q * c.wingArea * DragCoefficient(c, cl);
        }

        /// <summary>Highest airspeed where full-throttle thrust still matches level-flight drag, m/s.</summary>
        public static float MaxLevelSpeed(DroneConfig c, float mass)
        {
            float best = 0f;
            for (float v = 10f; v < 150f; v += 0.25f)
                if (Thrust(c, v, 1f) >= LevelDrag(c, mass, v)) best = v;
            return best;
        }

        /// <summary>Lowest speed the wing holds the weight at, m/s.</summary>
        public static float StallSpeed(DroneConfig c, float mass) =>
            Mathf.Sqrt(2f * mass * Core.SimConstants.Gravity / (Density * c.wingArea * c.maxLift));

        /// <summary>Throttle that holds level flight at the cruise speed.</summary>
        public static float CruiseThrottle(DroneConfig c, float mass) =>
            Mathf.Clamp01(LevelDrag(c, mass, c.cruiseSpeed) / Thrust(c, c.cruiseSpeed, 1f));

        /// <summary>
        /// Terminal speed in an unpowered straight dive at <paramref name="diveDeg"/> below the horizon, m/s:
        /// weight along the path = drag, with the lift the path needs (weight across the path).
        /// </summary>
        public static float DiveSpeed(DroneConfig c, float mass, float diveDeg)
        {
            float w = mass * Core.SimConstants.Gravity, a = diveDeg * Mathf.Deg2Rad;
            for (float v = 10f; v < 200f; v += 0.25f)
            {
                float q = 0.5f * Density * v * v;
                float cl = w * Mathf.Cos(a) / (q * c.wingArea);
                if (q * c.wingArea * DragCoefficient(c, cl) >= w * Mathf.Sin(a)) return v;
            }
            return 200f;
        }
    }
}
