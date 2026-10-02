using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// A rocket broken loose from a burning launcher: its motor lit by the fire, not by the launch sequence, so it leaves
    /// without the tube's guidance and spin and tumbles off erratically for the seconds its motor burns, trailing flame and
    /// white smoke, then falls (cooked-off MLRS rockets scattering around a burning launcher, combat footage). The fuze is
    /// not armed: it ends in a puff of dirt where it lands. Speeds and burn times are look values (abstract), far below
    /// a launched rocket's. Ages in game time (the killcam freeze holds it).
    /// </summary>
    public sealed class FxRocket : MonoBehaviour
    {
        Vector3 _velocity, _axis;
        float _burn, _age, _thrust;
        FxEffect _trail;

        public static FxRocket Launch(Vector3 position, Vector3 direction, FxRecipe exhaust)
        {
            var go = new GameObject("FX_Rocket");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            var r = go.AddComponent<FxRocket>();
            r._axis = direction.normalized;
            r._velocity = r._axis * Random.Range(4f, 10f);
            r._burn = Random.Range(1f, 3f);
            r._thrust = Random.Range(25f, 45f);
            r._trail = SimpleVfx.CookOff(go.transform, position, r._burn, -r._axis, 0.45f, exhaust);
            return r;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;
            if (_age < _burn)
            {
                // Unguided and unspun: the axis wanders as it goes.
                _axis = Quaternion.AngleAxis(Random.Range(-90f, 90f) * dt, Random.onUnitSphere) * _axis;
                _velocity += _axis * (_thrust * dt);
            }
            _velocity += Physics.gravity * dt;
            Vector3 step = _velocity * dt;
            if (Physics.Raycast(transform.position, step, out var hit, step.magnitude + 0.1f, GameLayers.TerrainMask | GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                || _age > 20f)
            {
                if (hit.collider != null) SimpleVfx.Explosion(hit.point, 0.35f, ExplosionKind.Ground);
                End();
                return;
            }
            transform.position += step;
            if (_velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(_velocity);
            if (_trail != null && _trail.transform.parent == transform)
                _trail.transform.rotation = Quaternion.FromToRotation(Vector3.up, -_axis);
        }

        void End()
        {
            if (_trail != null && _trail.transform.parent == transform)
            {
                // The trail's smoke stays in the air (world-space particles); the effect goes back to its pool when done.
                _trail.transform.SetParent(null, true);
                _trail.StopEmitting(8f);
            }
            Destroy(gameObject);
        }
    }
}
