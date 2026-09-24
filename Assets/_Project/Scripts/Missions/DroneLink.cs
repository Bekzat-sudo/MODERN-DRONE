using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Drone;
using ModernDrone.Video;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Simulates the drone's video/control link each frame: radio (range, line of sight from the operator
    /// antenna, jammers) or fiber (cable length). Feeds link quality and latency to the flight controller
    /// (failsafe on loss) and drives the analog-video full-screen effect.
    /// </summary>
    [RequireComponent(typeof(DroneController))]
    public sealed class DroneLink : MonoBehaviour
    {
        const int MaxFoliageHits = 6;
        const float CablePointSpacing = 4f;

        [SerializeField] LinkType type = LinkType.Radio;
        [SerializeField] RadioParams radio = RadioParams.Default;
        [SerializeField] float operatorAntennaHeight = 2.5f;

        [Header("Fiber")]
        [SerializeField] float spoolLength = 5000f;
        [SerializeField] float spoolMass = 0.35f;
        [SerializeField] Material cableMaterial;

        static readonly RaycastHit[] Hits = new RaycastHit[32];
        readonly JamSample[] _jam = new JamSample[16];
        readonly List<Vector3> _cablePoints = new List<Vector3>();

        DroneController _drone;
        Camera _fpvCamera;
        Vector3 _operator;
        Vector3 _lastPosition;
        float _cableUsed;
        float _nextCablePoint;
        LineRenderer _cable;
        LinkTelemetry _telemetry;

        public LinkType Type => type;
        public LinkTelemetry Telemetry => _telemetry;
        public Vector3 OperatorPosition => _operator;

        void Awake()
        {
            _drone = GetComponent<DroneController>();
            _fpvCamera = GetComponentInChildren<Camera>();
        }

        void OnEnable()
        {
            _drone.OnReset += ResetLink;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable()
        {
            _drone.OnReset -= ResetLink;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            VideoFeed.Clear();
        }

        /// <summary>The full-screen video pass runs for every camera; enable its effect only for the FPV feed.</summary>
        void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext _, Camera cam) =>
            VideoFeed.SetEnabled(cam == _fpvCamera && !GamePause.IsPaused);

        void Start()
        {
            _operator = transform.position + Vector3.up * operatorAntennaHeight;
            SetType(type);
        }

        public void SetType(LinkType linkType)
        {
            type = linkType;
            _drone.SetExtraMass(type == LinkType.Fiber ? spoolMass : 0f);
            ResetLink();
        }

        void ResetLink()
        {
            _cableUsed = 0f;
            _nextCablePoint = 0f;
            _lastPosition = transform.position;
            _cablePoints.Clear();
            if (_cable != null) _cable.positionCount = 0;
        }

        void FixedUpdate()
        {
            if (type != LinkType.Fiber) return;
            // Cable pays out along the flown path and settles on the ground behind the drone.
            Vector3 pos = transform.position;
            _cableUsed += Vector3.Distance(_lastPosition, pos);
            _lastPosition = pos;
            if (_cableUsed >= _nextCablePoint && _cableUsed < spoolLength)
            {
                _nextCablePoint = _cableUsed + CablePointSpacing;
                _cablePoints.Add(GroundPoint(pos));
            }
        }

        void Update()
        {
            var q = type == LinkType.Fiber ? LinkModel.Fiber(_cableUsed, spoolLength) : RadioQuality(out _telemetry.jam);
            if (type == LinkType.Fiber) _telemetry.jam = 0f;

            _drone.SetLinkQuality(q.control);
            _drone.InputLatency = type == LinkType.Fiber ? 0.01f : LinkModel.ControlLatency(q.control);

            _telemetry.type = type;
            _telemetry.video = q.video;
            _telemetry.control = q.control;
            _telemetry.latency = _drone.InputLatency;
            _telemetry.cableUsed = _cableUsed;
            _telemetry.spoolLength = spoolLength;
            _telemetry.cableCut = type == LinkType.Fiber && _cableUsed >= spoolLength;
            _telemetry.failsafe = _drone.Failsafe;

            VideoFeed.Publish(_telemetry);
            UpdateCableVisual();
        }

        LinkQuality RadioQuality(out float strongestJam)
        {
            Vector3 drone = transform.position;
            Vector3 delta = drone - _operator;
            float distance = delta.magnitude;

            int terrain = 0, foliage = 0;
            if (distance > 0.5f)
            {
                int n = Physics.RaycastNonAlloc(_operator, delta / distance, Hits, distance,
                    GameLayers.TerrainMask | GameLayers.EnvironmentMask | GameLayers.VehicleHullMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (Hits[i].collider.gameObject.layer == GameLayers.Terrain) terrain++;
                    else foliage++;
                }
            }

            int jamCount = 0;
            strongestJam = 0f;
            foreach (var jammer in SignalJammer.All)
            {
                var sample = jammer.Sample(drone);
                if (sample.distanceToDrone > sample.radius || jamCount == _jam.Length) continue;
                _jam[jamCount++] = sample;
                strongestJam = Mathf.Max(strongestJam, LinkModel.JamLevel(sample));
            }

            var q = LinkModel.Radio(radio, distance, terrain, Mathf.Min(foliage, MaxFoliageHits),
                new System.ReadOnlySpan<JamSample>(_jam, 0, jamCount));

            // Analog links flicker: multipath and interference are never steady.
            float flicker = Mathf.PerlinNoise(Time.time * 3.1f, 0.37f);
            if (q.video < 0.97f) q.video *= Mathf.Lerp(0.8f, 1.05f, flicker);
            q.video = Mathf.Clamp01(q.video);
            return q;
        }

        Vector3 GroundPoint(Vector3 p)
        {
            if (Physics.Raycast(p, Vector3.down, out var hit, 200f, GameLayers.TerrainMask | GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;
            return p;
        }

        void UpdateCableVisual()
        {
            if (type != LinkType.Fiber || cableMaterial == null)
            {
                if (_cable != null) _cable.positionCount = 0;
                return;
            }
            if (_cable == null)
            {
                var go = new GameObject("FiberCable");
                _cable = go.AddComponent<LineRenderer>();
                _cable.sharedMaterial = cableMaterial;
                _cable.startWidth = _cable.endWidth = 0.025f;
                _cable.useWorldSpace = true;
                _cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cable.numCapVertices = 0;
            }
            bool cut = _cableUsed >= spoolLength;
            int count = _cablePoints.Count + 1 + (cut ? 0 : 1);
            _cable.positionCount = count;
            _cable.SetPosition(0, GroundPoint(_operator));
            for (int i = 0; i < _cablePoints.Count; i++)
                _cable.SetPosition(i + 1, _cablePoints[i]);
            if (!cut) _cable.SetPosition(count - 1, transform.position);
        }
    }
}
