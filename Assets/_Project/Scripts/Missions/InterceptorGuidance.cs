using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>Kinematic pursuit for interceptor drones: lead aiming with a turn-rate and acceleration limit.</summary>
    public static class InterceptorGuidance
    {
        /// <summary>Predicted meeting point assuming the target keeps its velocity.</summary>
        public static Vector3 LeadPoint(Vector3 position, float speed, Vector3 targetPos, Vector3 targetVel)
        {
            float dist = Vector3.Distance(position, targetPos);
            float timeToGo = dist / Mathf.Max(speed, 5f);
            // Cap the prediction so a far, fast target does not send the interceptor wildly off course.
            return targetPos + targetVel * Mathf.Min(timeToGo, 3f);
        }

        /// <summary>New velocity: direction turned toward the aim point by at most turnRate, speed ramped to maxSpeed.</summary>
        public static Vector3 Steer(Vector3 velocity, Vector3 position, Vector3 aimPoint, float maxSpeed, float acceleration,
            float turnRateDeg, float dt)
        {
            Vector3 toAim = aimPoint - position;
            Vector3 desired = toAim.sqrMagnitude > 1e-4f ? toAim.normalized : (velocity.sqrMagnitude > 1e-4f ? velocity.normalized : Vector3.forward);
            Vector3 current = velocity.sqrMagnitude > 1e-4f ? velocity.normalized : desired;
            Vector3 dir = Vector3.RotateTowards(current, desired, turnRateDeg * Mathf.Deg2Rad * dt, 0f);
            float speed = Mathf.MoveTowards(velocity.magnitude, maxSpeed, acceleration * dt);
            return dir * speed;
        }
    }
}
