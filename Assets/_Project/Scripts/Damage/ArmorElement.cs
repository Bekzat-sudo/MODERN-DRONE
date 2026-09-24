using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>
    /// Armor plate, reactive block or screen. Lives on its own collider; the layer must match the kind
    /// (Armor / ERA / Screen) so the damage solver's raycasts find it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ArmorElement : MonoBehaviour
    {
        [SerializeField] ArmorKind kind = ArmorKind.Plate;
        [Tooltip("Plate: armor points. Reactive: fraction of penetration removed. Screen: disrupt chance.")]
        [SerializeField] float value = 50f;
        [Tooltip("Reactive blocks and screens are consumed when triggered.")]
        [SerializeField] bool consumable;

        public ArmorKind Kind => kind;
        public float Value => value;
        public bool Consumed { get; private set; }
        public VehicleDamageModel Owner { get; internal set; }

        public void Configure(ArmorKind kind, float value)
        {
            this.kind = kind;
            this.value = value;
            consumable = kind == ArmorKind.Reactive || kind == ArmorKind.Screen;
            gameObject.layer = LayerFor(kind);
        }

        public void Consume()
        {
            if (!consumable || Consumed) return;
            Consumed = true;
            // Hide and stop blocking raycasts / the drone; keeps the object for the debrief view.
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
        }

        public static int LayerFor(ArmorKind kind) => kind switch
        {
            ArmorKind.Reactive => GameLayers.ERA,
            ArmorKind.Screen => GameLayers.Screen,
            _ => GameLayers.Armor
        };
    }
}
