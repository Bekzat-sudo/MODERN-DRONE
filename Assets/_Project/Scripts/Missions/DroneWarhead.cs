using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Vehicles;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Contact-fuzed payload carried by the drone. Arms a few seconds after the drone is armed,
    /// detonates on impact with anything in the fuze mask and resolves damage along the charge axis.
    /// </summary>
    [RequireComponent(typeof(DroneController))]
    public sealed class DroneWarhead : MonoBehaviour
    {
        [SerializeField] WarheadData warhead;
        [Tooltip("Seconds after arming before the fuze becomes live (safe launch).")]
        [SerializeField] float fuzeDelay = 2f;
        [SerializeField] float minImpactSpeed = 3f;
        [SerializeField] Transform visual;
        [SerializeField] float reportDuration = 10f;
        [SerializeField] int seed = 12345;

        DroneController _drone;
        System.Random _random;
        GUIStyle _style;

        public WarheadData Warhead => warhead;
        public bool Detonated { get; private set; }
        /// <summary>Latched before each physics step: the crash handler may disarm the drone before our collision callback runs.</summary>
        public bool FuzeLive { get; private set; }
        public StrikeReport LastReport { get; private set; }

        public event System.Action<StrikeReport> StrikeResolved;

        /// <summary>Show the built-in strike summary overlay (off when a killcam presents it).</summary>
        public bool ShowReportOverlay { get; set; } = true;

        public void SetWarhead(WarheadData data) => warhead = data;

        void Awake()
        {
            _drone = GetComponent<DroneController>();
            _random = new System.Random(seed);
            if (visual == null) visual = transform.Find("Visual");
        }

        void FixedUpdate() => FuzeLive = _drone.Armed && !Detonated && _drone.Telemetry.flightTime >= fuzeDelay;

        void OnEnable() => _drone.OnReset += Rearm;
        void OnDisable() => _drone.OnReset -= Rearm;

        void OnCollisionEnter(Collision collision)
        {
            if (warhead == null || !FuzeLive) return;
            if (!GameLayers.IsInMask(collision.collider.gameObject, GameLayers.FuzeTriggerMask)) return;

            Vector3 velocity = _drone.PreImpactVelocity;
            if (velocity.magnitude < minImpactSpeed) return;

            // The jet forms along the liner axis (the payload points along body forward), not along the flight path:
            // tip speed is 7-14 km/s (en.wikipedia.org/wiki/Shaped_charge), so the drone's ~40 m/s bends it by <0.5 deg.
            Detonate(collision.GetContact(0).point, _drone.PreImpactForward);
        }

        public void Detonate(Vector3 point, Vector3 direction)
        {
            Detonated = true;
            FuzeLive = false;
            LastReport = DamageSolver.Detonate(warhead, point, direction, _random);
            SimpleVfx.Explosion(point, 1f);
            Debug.Log($"[ModernDrone] Strike: {warhead.displayName} at {point}\n{LastReport.Summary()}");

            _drone.MarkDestroyed();
            if (visual != null) visual.gameObject.SetActive(false);
            var rb = _drone.Body;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            StrikeResolved?.Invoke(LastReport);
        }

        void Rearm()
        {
            Detonated = false;
            if (visual != null) visual.gameObject.SetActive(true);
            _drone.Body.isKinematic = false;
        }

        void OnGUI()
        {
            if (!ShowReportOverlay || LastReport == null || Time.time - LastReport.time > reportDuration) return;
            if (_style == null || _style.fontSize != Mathf.RoundToInt(Screen.height * 0.024f))
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.024f),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter,
                    wordWrap = true
                };

            string text = LastReport.Summary();
            var rect = new Rect(Screen.width * 0.2f, Screen.height * 0.12f, Screen.width * 0.6f, Screen.height * 0.4f);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            float h = _style.CalcHeight(new GUIContent(text), rect.width) + 16f;
            GUI.DrawTexture(new Rect(rect.x, rect.y - 8f, rect.width, h), Texture2D.whiteTexture);
            GUI.color = prev;
            _style.normal.textColor = Color.white;
            GUI.Label(rect, text, _style);
        }
    }
}
