using System.Collections.Generic;
using ModernDrone.Damage;
using ModernDrone.Vehicles;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Inspector of a vehicle's fire and detonation description: fit compartments around the modules, suggest openings
    /// and hatches (Hatch_* model nodes, else the hull's top), check the setup, and edit the selected compartment's box in
    /// the scene view. Everything it creates stays editable by hand.
    /// </summary>
    [CustomEditor(typeof(VehicleFxProfile))]
    sealed class VehicleFxProfileEditor : UnityEditor.Editor
    {
        List<VehicleFxSetup.Issue> _issues;
        int _selected = -1;
        readonly BoxBoundsHandle _box = new BoxBoundsHandle();

        public override void OnInspectorGUI()
        {
            var p = (VehicleFxProfile)target;
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var names = new string[p.compartments.Length + 1];
            names[0] = "— отсек для правки в сцене —";
            for (int i = 0; i < p.compartments.Length; i++) names[i + 1] = $"{i}: {p.compartments[i].name} ({p.compartments[i].kind})";
            _selected = EditorGUILayout.Popup(_selected + 1, names) - 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Отсеки по модулям"))
                {
                    Undo.RecordObject(p, "Fit compartments");
                    VehicleFxSetup.FitCompartments(p);
                    EditorUtility.SetDirty(p);
                }
                if (GUILayout.Button("Предложить проёмы и люки"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(p.gameObject, "Suggest openings");
                    VehicleFxSetup.Suggest(p);
                    EditorUtility.SetDirty(p);
                    _issues = VehicleFxSetup.Validate(p.gameObject);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Проверить")) _issues = VehicleFxSetup.Validate(p.gameObject);
                if (GUILayout.Button("Полигон эффектов")) VehicleFxPolygon.Open(p.gameObject);
            }
            if (_issues == null) return;
            if (_issues.Count == 0) EditorGUILayout.HelpBox("Ошибок нет.", MessageType.Info);
            foreach (var issue in _issues)
            {
                EditorGUILayout.HelpBox(issue.message, issue.error ? MessageType.Error : MessageType.Warning);
                if (issue.context != null && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown)
                    EditorGUIUtility.PingObject(issue.context);
            }
        }

        void OnSceneGUI()
        {
            var p = (VehicleFxProfile)target;
            if (_selected < 0 || _selected >= p.compartments.Length) return;
            var c = p.compartments[_selected];
            using (new Handles.DrawingScope(Color.yellow, p.transform.localToWorldMatrix))
            {
                _box.center = c.center;
                _box.size = c.size;
                EditorGUI.BeginChangeCheck();
                _box.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(p, "Edit compartment");
                    c.center = _box.center;
                    c.size = _box.size;
                    EditorUtility.SetDirty(p);
                }
                Handles.Label(c.center, c.name);
            }
        }
    }

    /// <summary>
    /// Effects test bench (play mode): stands a chosen vehicle in front of the camera and plays any of its fires, mist
    /// flashes and detonations with one click, plus every standard explosion beside it.
    /// </summary>
    sealed class VehicleFxPolygon : EditorWindow
    {
        GameObject _prefab, _instance;
        Vector2 _scroll;

        [MenuItem("Modern Drone/Tools/FX Polygon")]
        static void Menu() => Open(null);

        public static void Open(GameObject vehicle)
        {
            var w = GetWindow<VehicleFxPolygon>("Полигон эффектов");
            if (vehicle != null)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(vehicle);
                w._prefab = source != null ? source : vehicle;
            }
        }

        void OnGUI()
        {
            _prefab = (GameObject)EditorGUILayout.ObjectField("Машина", _prefab, typeof(GameObject), false);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Запустите Play Mode (например, в сцене ProvingGround): эффекты живут в игровом времени.", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _prefab != null;
                if (GUILayout.Button(_instance == null ? "Поставить" : "Поставить заново")) Spawn();
                GUI.enabled = true;
                if (_instance != null && GUILayout.Button("Убрать")) Destroy(_instance);
            }
            if (_instance == null) return;
            var model = _instance.GetComponent<VehicleDamageModel>();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(model.DisplayName, EditorStyles.boldLabel);
            foreach (var m in model.Modules)
            {
                if (!m.Burnable && !m.HighExplosive) continue;
                string what = m.Burnable ? $"{m.Substance.displayName} {m.Remaining:0.#} {m.Substance.unit}" : "";
                if (m.HighExplosive) what += $" + {m.Explosive.displayName} {m.ExplosiveCount * m.Load:0}";
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{m.name}: {what}", GUILayout.MinWidth(220));
                    GUI.enabled = m.Burnable && !m.OnFire;
                    if (GUILayout.Button("Поджечь", GUILayout.Width(80))) model.Ignite(m);
                    GUI.enabled = m.Burnable && m.Substance.mistFireball;
                    if (GUILayout.Button("Вспышка", GUILayout.Width(80))) model.FlashMist(m);
                    GUI.enabled = true;
                    if (GUILayout.Button("Подрыв", GUILayout.Width(80))) model.Detonate(m);
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Взрывы рядом (без урона)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
                foreach (ExplosionKind k in System.Enum.GetValues(typeof(ExplosionKind)))
                    if (k != ExplosionKind.Auto && GUILayout.Button(k.ToString()))
                        SimpleVfx.Explosion(_instance.transform.position - _instance.transform.right * 7f + Vector3.up * (k == ExplosionKind.Ground ? 0.3f : 1.5f),
                            k == ExplosionKind.Ammo ? 2.5f : 1f, k);
            EditorGUILayout.Space();
            foreach (var line in model.Log) EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        void Update()
        {
            if (EditorApplication.isPlaying && _instance != null) Repaint();
        }

        void Spawn()
        {
            if (_instance != null) Destroy(_instance);
            var cam = Camera.main != null ? Camera.main.transform : SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera.transform : null;
            Vector3 at = cam != null ? cam.position + Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized * 25f : Vector3.zero;
            if (Physics.Raycast(at + Vector3.up * 200f, Vector3.down, out var hit, 400f, ModernDrone.Core.GameLayers.TerrainMask)) at = hit.point;
            var rot = cam != null ? Quaternion.LookRotation(Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(cam.forward, Vector3.up))) : Quaternion.identity;
            _instance = Instantiate(_prefab, at, rot);
            _instance.name = _prefab.name + " (полигон)";
        }
    }
}
