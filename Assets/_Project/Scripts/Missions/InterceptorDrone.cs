using ModernDrone.Core;
using ModernDrone.Drone;
using ModernDrone.Vehicles;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Hostile interceptor drone (gameplay model). Faster than the player's drone but turns wider. Guided by its
    /// operator while the ground site is operational and the target stays in view; loses the track behind tree lines
    /// and terrain, crashes into obstacles, detonates when close to the target.
    /// </summary>
    public sealed class InterceptorDrone : MonoBehaviour
    {
        const float BodyRadius = 0.25f;
        const int ObstacleMask = GameLayers.TerrainMask | GameLayers.EnvironmentMask | GameLayers.VehicleHullMask | GameLayers.ArmorMask;

        float _maxSpeed = 40f;
        float _acceleration = 22f;
        float _turnRate = 140f;
        float _fuzeRadius = 2.5f;
        float _endurance = 70f;
        float _trackLossTime = 2f;
        float _killChance = 0.55f;
        float _missCooldown;

        InterceptorSite _site;
        DroneController _target;
        Vector3 _velocity;
        Vector3 _lastKnown;
        Vector3 _lastTargetPos;
        float _lostFor;
        float _age;
        bool _dead;

        public bool Alive => !_dead;

        public void Launch(InterceptorSite site, DroneController target, Vector3 initialVelocity,
            float maxSpeed, float acceleration, float turnRate, float fuzeRadius, float endurance, float killChance)
        {
            _killChance = killChance;
            _site = site;
            _target = target;
            _velocity = initialVelocity;
            _maxSpeed = maxSpeed;
            _acceleration = acceleration;
            _turnRate = turnRate;
            _fuzeRadius = fuzeRadius;
            _endurance = endurance;
            _lastKnown = target.transform.position;
            _lastTargetPos = _lastKnown;
        }

        void FixedUpdate()
        {
            if (_dead) return;
            float dt = Time.fixedDeltaTime;
            _age += dt;
            if (_age > _endurance) { Explode(); return; }

            Vector3 pos = transform.position;
            bool targetAlive = _target != null && !_target.Crashed;
            bool controlled = _site != null && _site.Operational;

            Vector3 aim;
            if (!controlled)
            {
                // Lost operator link: keeps going and sinks until it hits something.
                aim = pos + _velocity.normalized * 50f + Vector3.down * 20f;
            }
            else
            {
                Vector3 targetPos = targetAlive ? _target.transform.position : _lastKnown;
                Vector3 targetVel = (targetPos - _lastTargetPos) / dt;
                _lastTargetPos = targetPos;
                bool seen = targetAlive && !Physics.Linecast(pos, targetPos, GameLayers.TerrainMask | GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                    && SmokeVolumes.Visible(pos, targetPos);
                if (seen)
                {
                    _lostFor = 0f;
                    _lastKnown = targetPos;
                    aim = InterceptorGuidance.LeadPoint(pos, _velocity.magnitude, targetPos, targetVel);
                }
                else
                {
                    _lostFor += dt;
                    // Fly to the last known position, then climb and circle while the operator searches.
                    aim = _lostFor < _trackLossTime || Vector3.Distance(pos, _lastKnown) > 15f
                        ? _lastKnown
                        : _lastKnown + Quaternion.Euler(0f, _age * 90f, 0f) * Vector3.forward * 30f + Vector3.up * 25f;
                }
            }

            _velocity = InterceptorGuidance.Steer(_velocity, pos, aim, _maxSpeed, _acceleration, _turnRate, dt);
            Vector3 step = _velocity * dt;

            if (Physics.SphereCast(pos, BodyRadius, step.normalized, out var hit, step.magnitude, ObstacleMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                Explode();
                return;
            }
            transform.position = pos + step;
            if (_velocity.sqrMagnitude > 1f)
            {
                // Bank into turns like a quad does.
                Vector3 lateral = Vector3.Cross(Vector3.up, _velocity.normalized);
                float bank = Vector3.Dot(lateral, step - transform.forward * step.magnitude) * -40f;
                transform.rotation = Quaternion.LookRotation(_velocity) * Quaternion.Euler(20f, 0f, Mathf.Clamp(bank, -45f, 45f));
            }

            if (targetAlive)
            {
                float dist = Vector3.Distance(transform.position, _target.transform.position);
                InterceptorWarning.Report(transform.position, dist);
                _missCooldown -= dt;
                if (controlled && dist <= _fuzeRadius && _missCooldown <= 0f)
                {
                    if (Random.value < _killChance)
                    {
                        _target.ShootDown("ДРОН ПЕРЕХВАЧЕН");
                        Explode();
                    }
                    else
                    {
                        // Missed pass: overshoots and must come around again.
                        _missCooldown = 2.5f;
                        InterceptorWarning.ReportMiss();
                    }
                }
            }
        }

        /// <summary>The player's drone rammed it.</summary>
        void OnTriggerEnter(Collider other)
        {
            if (_dead || other.attachedRigidbody == null) return;
            var drone = other.attachedRigidbody.GetComponent<DroneController>();
            if (drone == null) return;
            drone.ShootDown("СТОЛКНОВЕНИЕ С ПЕРЕХВАТЧИКОМ");
            Explode();
        }

        public void Explode()
        {
            if (_dead) return;
            _dead = true;
            SimpleVfx.Explosion(transform.position, 0.5f);
            Destroy(gameObject);
        }

        /// <summary>Builds the interceptor from primitives: small X-frame quad with a trigger collider.</summary>
        public static InterceptorDrone Create(Vector3 position, Material body)
        {
            var root = new GameObject("Interceptor");
            root.transform.position = position;
            // Default layer: overlaps the player's drone (trigger = ramming) but is outside obstacle and fuze masks.
            root.layer = 0;
            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.6f;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.12f, 0.06f, 0.35f), Quaternion.identity, body);
            foreach (float yaw in new[] { 45f, -45f })
                Part(root.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(0.03f, 0.02f, 0.5f), Quaternion.Euler(0f, yaw, 0f), body);
            for (int i = 0; i < 4; i++)
            {
                var p = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward * 0.25f;
                Part(root.transform, PrimitiveType.Cylinder, p + Vector3.up * 0.03f, new Vector3(0.16f, 0.004f, 0.16f), Quaternion.identity, body);
            }
            return root.AddComponent<InterceptorDrone>();
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
