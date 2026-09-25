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
        Vector3 _mountPosition;

        public float TiltDeg
        {
            get => tiltDeg;
            set => tiltDeg = Mathf.Clamp(value, 0f, 60f);
        }

        public float HorizontalFov
        {
            get => horizontalFov;
            set
            {
                horizontalFov = Mathf.Clamp(value, 60f, 160f);
                if (_camera != null) ApplyFov();
            }
        }

        bool _mountSet;

        /// <summary>Moves the camera onto a model's lens (drone space).</summary>
        public void SetMount(Vector3 localPosition)
        {
            _mountPosition = localPosition;
            _mountSet = true;
            transform.localPosition = localPosition;
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (!_mountSet) _mountPosition = transform.localPosition;
            if (drone == null) drone = GetComponentInParent<DroneController>();
            if (drone != null && drone.Config != null)
            {
                tiltDeg = drone.Config.cameraTilt;
                horizontalFov = drone.Config.cameraHorizontalFov;
            }
            ApplyFov();
        }

        float _frozenUntil;
        Vector3 _frozenPos;
        Quaternion _frozenRot;

        /// <summary>Holds the current view for a moment (thermal shutter calibration freezes the picture).</summary>
        public void Freeze(float seconds)
        {
            _frozenUntil = Time.time + seconds;
            _frozenPos = transform.position;
            _frozenRot = transform.rotation;
        }

        void LateUpdate()
        {
            if (Time.time < _frozenUntil)
            {
                transform.SetPositionAndRotation(_frozenPos, _frozenRot);
                return;
            }
            transform.localPosition = _mountPosition;
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
