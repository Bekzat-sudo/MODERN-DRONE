using System.Collections.Generic;
using ModernDrone.Drone;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Hangar turntable for the loadout screen: the drone model on a pedestal far above the map, rendered by its own
    /// camera into a texture. Props are swapped in place; the part of the selected slot is pulled out next to its
    /// mount (batteries, cameras and VTX are baked into the drone meshes) and outlined.
    /// </summary>
    public sealed class HangarPreview : MonoBehaviour
    {
        static readonly Vector3 StagePosition = new Vector3(0f, 3000f, 0f);
        const float TurnSpeed = 14f; // deg/s

        [SerializeField] Material outline;

        Transform _stage, _turntable;
        Camera _camera;
        RenderTexture _texture;
        GameObject _drone, _exploded;
        readonly List<GameObject> _props = new List<GameObject>();
        readonly List<Renderer> _modelProps = new List<Renderer>();
        DroneConfig _shownModel;
        DronePart _shownPart, _shownProps;
        float _yaw = 200f, _pitch = 22f, _distance = 1f;

        public void SetOutline(Material m) => outline = m;

        public Texture Texture => _texture;

        /// <summary>Shows the model with its fitted props and the selected slot's part pulled out (null = none).</summary>
        public void Show(DroneConfig model, DronePart props, DronePart highlight, Vector2Int size)
        {
            if (_stage == null) BuildStage();
            _stage.gameObject.SetActive(true);
            _camera.enabled = true;
            if (_texture == null || _texture.width != size.x || _texture.height != size.y)
            {
                if (_texture != null) _texture.Release();
                _texture = new RenderTexture(Mathf.Max(64, size.x), Mathf.Max(64, size.y), 24) { antiAliasing = 4 };
                _camera.targetTexture = _texture;
            }
            if (model != _shownModel) SetModel(model);
            if (props != _shownProps) SetProps(props, highlight == props);
            if (highlight != _shownPart) SetExploded(highlight);
        }

        public void Hide()
        {
            if (_stage == null) return;
            _stage.gameObject.SetActive(false);
            _camera.enabled = false;
        }

        /// <summary>Mouse drag orbits the turntable; otherwise it turns slowly.</summary>
        public void Orbit(Vector2 dragPixels)
        {
            _yaw += dragPixels.x * 0.4f;
            _pitch = Mathf.Clamp(_pitch + dragPixels.y * 0.3f, -10f, 70f);
        }

        void LateUpdate()
        {
            if (_stage == null || !_stage.gameObject.activeSelf) return;
            _yaw += TurnSpeed * Time.unscaledDeltaTime;
            _turntable.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            var look = Quaternion.Euler(_pitch, 0f, 0f);
            _camera.transform.position = _stage.position + Vector3.up * 0.05f + look * new Vector3(0f, 0f, -_distance);
            _camera.transform.rotation = look;
        }

        void BuildStage()
        {
            _stage = new GameObject("HangarStage").transform;
            _stage.SetParent(transform, false);
            _stage.position = StagePosition;
            _turntable = new GameObject("Turntable").transform;
            _turntable.SetParent(_stage, false);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(_stage, false);
            floor.transform.localPosition = new Vector3(0f, -0.16f, 0f);
            floor.transform.localScale = new Vector3(0.9f, 0.005f, 0.9f);
            floor.GetComponent<Renderer>().material.color = new Color(0.16f, 0.18f, 0.13f);

            // Own lights: the stage is lit the same in day and night missions.
            Light(new Vector3(0.6f, 0.9f, -0.7f), 3f, new Color(1f, 0.95f, 0.88f));
            Light(new Vector3(-0.8f, 0.4f, 0.6f), 1.5f, new Color(0.7f, 0.8f, 1f));
            Light(new Vector3(0f, -0.1f, -1f), 0.8f, Color.white);

            _camera = new GameObject("HangarCamera").AddComponent<Camera>();
            _camera.transform.SetParent(_stage, false);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = ModernDrone.Core.UiTheme.PanelRaised;
            _camera.nearClipPlane = 0.02f;
            _camera.farClipPlane = 8f; // the map is 3 km below
            _camera.fieldOfView = 30f;
            _camera.depth = -10f;
        }

        void Light(Vector3 dir, float intensity, Color color)
        {
            var l = new GameObject("HangarLight").AddComponent<Light>();
            l.transform.SetParent(_stage, false);
            l.type = LightType.Point;
            l.range = 4f;
            l.intensity = intensity;
            l.color = color;
            l.transform.localPosition = dir.normalized * 1.2f;
        }

        void SetModel(DroneConfig model)
        {
            _shownModel = model;
            // Swapped props and the pulled-out part hang on the turntable, not on the drone: remove them too.
            if (_drone != null) Destroy(_drone);
            foreach (var p in _props) Destroy(p);
            if (_exploded != null) Destroy(_exploded);
            _modelProps.Clear();
            _props.Clear();
            _shownProps = null;
            _shownPart = null;
            _exploded = null;
            if (model == null || model.visualPrefab == null) return;
            _drone = Instantiate(model.visualPrefab, _turntable, false);
            foreach (var r in _drone.GetComponentsInChildren<Renderer>())
                if (r.name.StartsWith("Prop_")) _modelProps.Add(r);
            var b = Bounds(_drone);
            _distance = b.extents.magnitude * 3.4f;
        }

        void SetProps(DronePart part, bool outlined)
        {
            _shownProps = part;
            foreach (var p in _props) Destroy(p);
            _props.Clear();
            bool swap = part != null && part.model != null;
            foreach (var mp in _modelProps)
            {
                mp.enabled = !swap;
                if (!swap) continue;
                var p = Instantiate(part.model, mp.transform.position, _turntable.rotation, _turntable);
                // Size the part prop to the model's own prop; alternate spin direction mirrors the blades.
                var target = mp.bounds.size;
                var own = Bounds(p).size;
                float s = Mathf.Max(target.x, target.z) / Mathf.Max(1e-4f, Mathf.Max(own.x, own.z));
                bool ccw = mp.name.EndsWith("FL") || mp.name.EndsWith("RR");
                p.transform.localScale = new Vector3(ccw ? -s : s, s, s);
                _props.Add(p);
            }
            if (outlined) foreach (var p in _props) AddOutline(p);
        }

        void SetExploded(DronePart part)
        {
            _shownPart = part;
            if (_exploded != null) Destroy(_exploded);
            _exploded = null;
            if (part == null || _shownModel == null) return;
            if (part.slot == PartSlot.Propellers)
            {
                SetProps(_shownProps, true);
                return;
            }
            if (_shownProps != null) SetProps(_shownProps, false);
            if (part.model == null) return;

            var (mount, pull) = Mount(_shownModel, part);
            _exploded = new GameObject("Exploded");
            _exploded.transform.SetParent(_turntable, false);
            var m = Instantiate(part.model, _exploded.transform, false);
            // Part models face their lens / connector along -X; turn it to the drone's nose (+Z).
            m.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            m.transform.localPosition = mount + pull - m.transform.localRotation * Bounds(m).center;
            AddOutline(m);
        }

        /// <summary>Where the part sits on this model and which way it is pulled out for display.</summary>
        static (Vector3 mount, Vector3 pull) Mount(DroneConfig c, DronePart p) => p.slot switch
        {
            PartSlot.Camera => (c.cameraPosition, Vector3.forward * 0.1f),
            PartSlot.Battery => (c.batteryMount, Vector3.up * 0.12f),
            _ when p.fiber => (c.spoolMount, Vector3.down * 0.05f + Vector3.back * 0.15f),
            _ => (c.antennaMount, Vector3.back * 0.12f + Vector3.up * 0.04f),
        };

        void AddOutline(GameObject go)
        {
            if (outline == null) return;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                if (System.Array.IndexOf(mats, outline) >= 0) continue;
                // One outline pass per submesh.
                var mf = r.GetComponent<MeshFilter>();
                int subs = mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 1;
                var hull = Instantiate(r.gameObject, r.transform.parent, true);
                foreach (Transform child in hull.transform) Destroy(child.gameObject);
                var hr = hull.GetComponent<MeshRenderer>();
                var om = new Material[subs];
                for (int i = 0; i < subs; i++) om[i] = outline;
                hr.sharedMaterials = om;
            }
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            b.center = go.transform.InverseTransformPoint(b.center);
            return b;
        }

        void OnDestroy()
        {
            if (_texture != null) _texture.Release();
        }
    }
}
