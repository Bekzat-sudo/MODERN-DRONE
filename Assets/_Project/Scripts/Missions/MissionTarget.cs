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
    }
}
