using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>Marks a vehicle as a mission objective.</summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class MissionTarget : MonoBehaviour
    {
        [SerializeField] TargetRequirement requirement = TargetRequirement.OutOfAction;
        [SerializeField] string label = "Цель";

        public VehicleDamageModel Model { get; private set; }
        public TargetRequirement Requirement => requirement;
        public string Label => label;
        public bool Neutralized => MissionRules.IsNeutralized(Model.State, requirement);

        public void Configure(string label, TargetRequirement requirement)
        {
            this.label = label;
            this.requirement = requirement;
        }

        void Awake() => Model = GetComponent<VehicleDamageModel>();

        MeshRenderer[] _renderers;
        Bounds _hull;
        int _hullFrame = -1, _sightFrame = -1;
        Vector3 _sightEye;
        bool _inSight;

        /// <summary>
        /// The visible hull (enabled mesh renderers), at most once per frame: the HUD and the target lock ask for it
        /// every frame and the lock at the physics rate, and a vehicle has up to ~170 renderers.
        /// </summary>
        public Bounds HullBounds
        {
            get
            {
                if (_hullFrame == Time.frameCount) return _hull;
                _hullFrame = Time.frameCount;
                if (_renderers == null || System.Array.Exists(_renderers, r => r == null))
                    _renderers = GetComponentsInChildren<MeshRenderer>(true);
                _hull = new Bounds(transform.position + Vector3.up * 1.5f, Vector3.one * 3f);
                foreach (var r in _renderers)
                    if (r.enabled && r.gameObject.activeInHierarchy) _hull.Encapsulate(r.bounds);
                return _hull;
            }
        }

        /// <summary>Whether the terrain leaves a clear line from <paramref name="eye"/> to the hull (cached per frame and eye).</summary>
        public bool InSightFrom(Vector3 eye)
        {
            if (_sightFrame == Time.frameCount && _sightEye == eye) return _inSight;
            _sightFrame = Time.frameCount;
            _sightEye = eye;
            return _inSight = !Physics.Linecast(eye, HullBounds.center, Core.GameLayers.TerrainMask, QueryTriggerInteraction.Ignore);
        }
    }
}
