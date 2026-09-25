using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Fades translucent prop discs in with simulated motor speed: spinning blades read as a blur disc on camera.
    /// Order matches <see cref="QuadMixer"/>.
    /// </summary>
    public sealed class PropVisuals : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] DroneController drone;
        [Tooltip("Front-right, front-left, rear-right, rear-left.")]
        [SerializeField] Renderer[] discs = new Renderer[QuadMixer.MotorCount];
        [SerializeField] Color discColor = new Color(0.06f, 0.06f, 0.06f, 1f);
        [Tooltip("Disc opacity at full motor output.")]
        [SerializeField, Range(0f, 1f)] float maxAlpha = 0.35f;

        MaterialPropertyBlock _block;

        void Reset() => drone = GetComponentInParent<DroneController>();

        void LateUpdate()
        {
            if (drone == null) return;
            _block ??= new MaterialPropertyBlock();
            var spin = drone.MotorSpin;
            for (int i = 0; i < discs.Length && i < spin.Length; i++)
            {
                if (discs[i] == null) continue;
                var c = discColor;
                c.a = maxAlpha * Mathf.Clamp01(spin[i]);
                discs[i].enabled = c.a > 0.01f;
                _block.SetColor(BaseColor, c);
                discs[i].SetPropertyBlock(_block);
            }
        }
    }
}
