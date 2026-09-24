using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>FPV camera: fixed uptilt relative to the frame and a horizontal-FOV lens.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class DroneCamera : MonoBehaviour
    {
        [SerializeField] DroneController drone;
        [Range(0f, 60f)] [SerializeField] float tiltDeg = 25f;
        [Range(60f, 160f)] [SerializeField] float horizontalFov = 118f;

        Camera _camera;
        float _appliedAspect;

        public float TiltDeg
        {
            get => tiltDeg;
            set => tiltDeg = Mathf.Clamp(value, 0f, 60f);
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (drone == null) drone = GetComponentInParent<DroneController>();
            if (drone != null && drone.Config != null)
            {
                tiltDeg = drone.Config.cameraTilt;
                horizontalFov = drone.Config.cameraHorizontalFov;
            }
            ApplyFov();
        }

        void LateUpdate()
        {
            transform.localRotation = Quaternion.Euler(-tiltDeg, 0f, 0f);
            if (!Mathf.Approximately(_appliedAspect, _camera.aspect))
                ApplyFov();
        }

        void ApplyFov()
        {
            _appliedAspect = _camera.aspect;
            _camera.fieldOfView = Mathf.Min(Camera.HorizontalToVerticalFieldOfView(horizontalFov, _camera.aspect), 150f);
        }
    }
}
