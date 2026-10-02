using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Sets up and checks a vehicle's <see cref="VehicleFxProfile"/>: compartments fitted around its modules, openings
    /// suggested from its model (Hatch_* nodes become hinged lids) or its hull's top surfaces, and a validator for what
    /// breaks fires and detonations (modules outside compartments, compartments with no way out, hatches without hinge,
    /// openings buried in armour). Used by the vehicle builders, the profile's inspector and an EditMode test over all
    /// vehicle prefabs. Suggestions are a starting point: everything stays editable on the prefab.
    /// </summary>
    public static class VehicleFxSetup
    {
        /// <summary>Name prefix of suggested openings (re-running the suggestion replaces them, hand-placed ones stay).</summary>
        public const string AutoPrefix = "Vent_Auto_";

        public struct Issue
        {
            public bool error;
            public string message;
            public Object context;
            public override string ToString() => (error ? "Ошибка: " : "Замечание: ") + message;
        }

        /// <summary>Has real armour plates (otherwise cabs and beds are open to the air).</summary>
        public static bool Armored(Transform root)
        {
            foreach (var a in root.GetComponentsInChildren<ArmorElement>(true))
                if (a.Kind == ArmorKind.Plate && a.Value >= 15f) return true;
            return false;
        }

        static bool Mobility(ModuleType t) => t == ModuleType.Track || t == ModuleType.Wheels || t == ModuleType.Rotor || t == ModuleType.TailRotor;

        /// <summary>A module's box in the root's space.</summary>
        static Bounds LocalBox(Transform root, VehicleModule m)
        {
            var box = m.GetComponent<BoxCollider>();
            Vector3 c = box != null ? box.center : Vector3.zero, e = box != null ? box.size * 0.5f : Vector3.one * 0.25f;
            var b = new Bounds(root.InverseTransformPoint(m.transform.TransformPoint(c)), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(root.InverseTransformPoint(m.transform.TransformPoint(c + Vector3.Scale(e,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))));
            return b;
        }

        /// <summary>
        /// Compartments around the modules: engine bay; under armour one fighting compartment (fuel tanks join the nearest);
        /// on soft-skinned vehicles a cab, a cargo body and the fuel tank outside; each external launcher on its own.
        /// </summary>
        public static void FitCompartments(VehicleFxProfile p)
        {
            var root = p.transform;
            bool armored = Armored(root);
            var names = new List<string>();
            var kinds = new List<CompartmentKind>();
            var boxes = new List<Bounds>();
            var deferred = new List<Bounds>();
            void Add(string name, CompartmentKind kind, Bounds b)
            {
                int i = names.IndexOf(name);
                if (i < 0) { names.Add(name); kinds.Add(kind); boxes.Add(b); }
                else { var bb = boxes[i]; bb.Encapsulate(b); boxes[i] = bb; }
            }
            foreach (var m in root.GetComponentsInChildren<VehicleModule>(true))
            {
                if (Mobility(m.Type)) continue;
                var b = LocalBox(root, m);
                switch (m.Type)
                {
                    case ModuleType.Engine:
                    case ModuleType.Transmission:
                        Add("Engine", CompartmentKind.Engine, b);
                        break;
                    case ModuleType.Launcher:
                        Add(m.name.Replace("Module_", ""), CompartmentKind.Open, b);
                        break;
                    case ModuleType.FuelTank:
                        if (armored) deferred.Add(b);
                        else Add("Fuel", CompartmentKind.Fuel, b);
                        break;
                    case ModuleType.ControlStation:
                        if (armored) Add("Fighting", CompartmentKind.Fighting, b);
                        else Add("Cab", CompartmentKind.Cab, b);
                        break;
                    default:
                        if (armored) Add("Fighting", CompartmentKind.Fighting, b);
                        else Add("Cargo", CompartmentKind.Cargo, b);
                        break;
                }
            }
            foreach (var b in deferred)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (kinds[i] == CompartmentKind.Open) continue;
                    float d = (boxes[i].center - b.center).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) Add("Fighting", CompartmentKind.Fighting, b);
                else { var bb = boxes[best]; bb.Encapsulate(b); boxes[best] = bb; }
            }
            p.compartments = new Compartment[names.Count];
            for (int i = 0; i < names.Count; i++)
                p.compartments[i] = new Compartment { name = names[i], kind = kinds[i], center = boxes[i].center, size = boxes[i].size + Vector3.one * 0.3f };
        }

        /// <summary>
        /// Suggests the openings: hinged hatches from model nodes named Hatch_* (or, under armour without any, a hatch on
        /// the hull top over each crew compartment), the turret ring, grilles over the engine bay, open cabs and beds on
        /// soft-skinned vehicles, and a seam over every compartment for smoke. Links every opening to its compartment.
        /// </summary>
        public static void Suggest(VehicleFxProfile p, Transform moving = null)
        {
            var root = p.transform;
            if (p.compartments.Length == 0) FitCompartments(p);
            foreach (var v in root.GetComponentsInChildren<FireVent>(true))
                if (v.name.StartsWith(AutoPrefix)) Kill(v.gameObject);
            SuggestHatches(root, moving != null ? moving : p.turret);
            Physics.SyncTransforms();
            bool armored = Armored(root);
            var vents = root.GetComponentsInChildren<FireVent>(true);
            foreach (var v in vents)
                if (v.Compartment < 0) v.Compartment = Nearest(p, v.transform.position - v.transform.up * 0.3f);

            if (p.turret != null)
                AddVent(root, "TurretRing", p.turret.position, root.up, VentKind.TurretRing, 3f, Nearest(p, p.turret.position - root.up * 0.3f));
            for (int i = 0; i < p.compartments.Length; i++)
            {
                var c = p.compartments[i];
                var (at, normal) = TopSurface(root, root.TransformPoint(c.center));
                switch (c.kind)
                {
                    case CompartmentKind.Engine:
                        AddVent(root, "Grille_" + c.name, at, normal, VentKind.Grille, 0.8f, i);
                        break;
                    case CompartmentKind.Fighting:
                    case CompartmentKind.Crew:
                        if (armored && !HasVent(vents, i, VentKind.Hatch))
                        {
                            var hatch = AddVent(root, "Hatch_" + c.name, at, normal, VentKind.Hatch, 0.3f, i);
                            var lid = hatch.gameObject.AddComponent<Hatch>();
                            lid.Configure(115f, 30f, 0.7f, Vector3.right);
                            hatch.Configure(VentKind.Hatch, 0.3f, lid, null, i);
                        }
                        break;
                    default:
                        if (!armored) AddVent(root, "Open_" + c.name, at, normal, VentKind.Open, 1.2f, i);
                        break;
                }
                AddVent(root, "Seam_" + c.name, at, normal, VentKind.Seam, 0.05f, i);
            }
        }

        /// <summary>The compartment containing <paramref name="world"/> or, failing that, the closest one (-1 if there are none).</summary>
        public static int Nearest(VehicleFxProfile p, Vector3 world)
        {
            int c = p.CompartmentAt(world);
            if (c >= 0) return c;
            Vector3 local = p.transform.InverseTransformPoint(world);
            float best = float.MaxValue;
            for (int i = 0; i < p.compartments.Length; i++)
            {
                float d = p.compartments[i].Bounds.SqrDistance(local);
                if (d < best) { best = d; c = i; }
            }
            return c;
        }

        static bool HasVent(FireVent[] vents, int compartment, VentKind kind)
        {
            foreach (var v in vents) if (v != null && v.Kind == kind && v.Compartment == compartment) return true;
            return false;
        }

        static FireVent AddVent(Transform root, string name, Vector3 at, Vector3 normal, VentKind kind, float area, int compartment)
        {
            var go = new GameObject(AutoPrefix + name);
            go.transform.SetParent(root, true);
            go.transform.SetPositionAndRotation(at + normal * 0.05f, Quaternion.FromToRotation(Vector3.up, normal));
            var v = go.AddComponent<FireVent>();
            v.Configure(kind, area, null, null, compartment);
            return v;
        }

        static readonly RaycastHit[] _hits = new RaycastHit[32];

        /// <summary>The vehicle's own outer top surface above <paramref name="inside"/> (its hull and armour colliders).</summary>
        public static (Vector3 point, Vector3 normal) TopSurface(Transform root, Vector3 inside)
        {
            Vector3 up = root.up;
            var scene = root.gameObject.scene;
            var physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            int n = physics.Raycast(inside + up * 8f, -up, _hits, 12f, GameLayers.VehicleHullMask | GameLayers.ArmorMask, QueryTriggerInteraction.Ignore);
            Vector3 at = inside + up * 0.8f, normal = up;
            float best = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (!h.collider.transform.IsChildOf(root)) continue;
                float height = Vector3.Dot(h.point, up);
                if (height <= best) continue;
                best = height;
                at = h.point;
                normal = h.normal;
            }
            return (at, normal);
        }

        /// <summary>
        /// Model nodes named Hatch_* (one per LOD copy) become one hatch: a hinge on the lid's own origin in the model
        /// (Tools/Blender/t72_hatches.py puts it on the front edge) carrying a <see cref="Hatch"/> that references the lid
        /// parts, a <see cref="FireVent"/> in the opening and a dark plate inside it that glows while the vehicle burns.
        /// Hinge, vent and plate hang off the vehicle root (or the turret for lids on it), not off the model, so a new
        /// model only needs its lids assigned. Lid mass ~30 kg (estimate).
        /// </summary>
        public static void SuggestHatches(Transform root, Transform moving)
        {
            var groups = new Dictionary<string, List<Transform>>();
            var taken = new HashSet<Transform>();
            foreach (var h in root.GetComponentsInChildren<Hatch>(true))
                foreach (var part in h.Lid) if (part != null) taken.Add(part);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Hatch_") || taken.Contains(t) || t.GetComponentInParent<Hatch>(true) != null || t.GetComponent<FireVent>() != null) continue;
                if (t.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                if (!groups.TryGetValue(t.name, out var list)) groups[t.name] = list = new List<Transform>();
                list.Add(t);
            }
            foreach (var kv in groups)
            {
                var parts = kv.Value;
                var parent = moving != null && parts[0].IsChildOf(moving) ? moving : root;
                var renderers = parts[0].GetComponentsInChildren<Renderer>(true);
                // Lid outline in the hull's frame.
                var b = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
                foreach (var r in renderers)
                {
                    var rb = r.bounds;
                    for (int i = 0; i < 8; i++)
                        b.Encapsulate(root.InverseTransformPoint(rb.center + Vector3.Scale(rb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                }
                var hinge = new GameObject(kv.Key + "_Hinge").transform;
                hinge.SetParent(parent, false);
                hinge.SetPositionAndRotation(parts[0].position, root.rotation);
                var hatch = hinge.gameObject.AddComponent<Hatch>();
                hatch.Configure(115f, 30f, 0.7f, Vector3.right, parts.ToArray());

                Vector3 centre = root.TransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
                var glow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                glow.name = kv.Key + "_Inside";
                Kill(glow.GetComponent<Collider>());
                glow.transform.SetParent(parent, false);
                glow.transform.SetPositionAndRotation(centre - root.up * 0.08f, root.rotation);
                glow.transform.localScale = new Vector3(b.size.x * 0.85f, 0.005f, b.size.z * 0.85f);
                var gr = glow.GetComponent<Renderer>();
                gr.sharedMaterial = FxAssets.HatchGlow;
                gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var vent = new GameObject(kv.Key + "_Vent");
                vent.transform.SetParent(parent, false);
                vent.transform.SetPositionAndRotation(centre + root.up * 0.05f, root.rotation);
                vent.AddComponent<FireVent>().Configure(VentKind.Hatch, Mathf.PI * 0.25f * b.size.x * b.size.z, hatch, gr);
            }
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        // ---------------------------------------------------------------- validation

        /// <summary>What would break the vehicle's fires and detonations. Errors must be fixed; remarks are worth a look.</summary>
        public static List<Issue> Validate(GameObject vehicle)
        {
            var issues = new List<Issue>();
            void Error(string m, Object c) => issues.Add(new Issue { error = true, message = m, context = c });
            void Note(string m, Object c) => issues.Add(new Issue { error = false, message = m, context = c });
            var root = vehicle.transform;
            var modules = root.GetComponentsInChildren<VehicleModule>(true);
            bool burns = false;
            foreach (var m in modules) if (m.Burnable || m.HighExplosive) { burns = true; break; }
            var p = vehicle.GetComponent<VehicleFxProfile>();
            if (p == null)
            {
                if (burns) Error("нет VehicleFxProfile на корне", vehicle);
                return issues;
            }
            int n = p.compartments.Length;
            if (burns && n == 0) Error("нет отсеков", p);

            foreach (var m in modules)
            {
                if (Mobility(m.Type) || !(m.Burnable || m.HighExplosive || m.Type == ModuleType.Engine)) continue;
                if (n > 0 && p.CompartmentAt(m.transform.position) < 0) Error($"модуль {m.name} вне отсеков", m);
                if (m.Burnable && m.Amount <= 0f) Error($"модуль {m.name}: {m.Substance.displayName} в количестве 0", m);
            }

            var vents = root.GetComponentsInChildren<FireVent>(true);
            var reached = new bool[n];
            foreach (var v in vents)
            {
                if (v.Compartment >= n) Error($"проём {v.name}: отсек {v.Compartment} не существует", v);
                else if (v.Compartment >= 0) reached[v.Compartment] = true;
                else if (n > 0) Note($"проём {v.name} не привязан к отсеку (ведёт во все)", v);
                if (v.Kind == VentKind.Hatch)
                {
                    if (v.Hatch == null) Error($"люк {v.name} без петли (Hatch)", v);
                    else if (v.Hatch.Lid.Count == 0) Note($"люк {v.name}: крышка не назначена (откроется без видимой крышки)", v.Hatch);
                    else foreach (var part in v.Hatch.Lid) if (part == null) Error($"люк {v.name}: пустая ссылка на крышку", v.Hatch);
                }
                if (v.Kind == VentKind.TurretRing && (p.turret == null || !p.turret.IsChildOf(root)))
                    Error($"погон {v.name} есть, а башня в профиле не назначена", p);
                // The turret ring only opens once the turret (and its armour) has gone.
                var ignore = v.Kind == VentKind.TurretRing ? p.turret : null;
                if (v.Kind != VentKind.Hole && InsideArmor(root, v.transform.position, ignore, out var plate))
                    Error($"проём {v.name} внутри брони ({plate.name}): пламя будет скрыто", v);
            }
            for (int i = 0; i < n; i++)
                if (!reached[i]) Error($"отсек {p.compartments[i].name} без проёмов (огню и дыму некуда выйти)", p);
            foreach (var h in root.GetComponentsInChildren<Hatch>(true))
            {
                bool linked = false;
                foreach (var v in vents) if (v.Hatch == h) { linked = true; break; }
                if (!linked) Error($"петля {h.name} не связана ни с одним проёмом", h);
            }
            foreach (var m in modules)
                if (m.Type == ModuleType.Rotor && p.rotor == null) { Note("вертолёт: несущий винт в профиле не назначен", p); break; }
            return issues;
        }

        /// <summary>Inside one of the vehicle's armour boxes (more than 2 cm deep).</summary>
        static bool InsideArmor(Transform root, Vector3 world, Transform ignore, out Component plate)
        {
            foreach (var a in root.GetComponentsInChildren<ArmorElement>(true))
            {
                if (ignore != null && a.transform.IsChildOf(ignore)) continue;
                if (!a.TryGetComponent(out BoxCollider box)) continue;
                Vector3 local = a.transform.InverseTransformPoint(world) - box.center;
                Vector3 half = box.size * 0.5f;
                Vector3 margin = a.transform.InverseTransformVector(Vector3.one * 0.02f);
                margin = new Vector3(Mathf.Abs(margin.x), Mathf.Abs(margin.y), Mathf.Abs(margin.z));
                if (Mathf.Abs(local.x) < half.x - margin.x && Mathf.Abs(local.y) < half.y - margin.y && Mathf.Abs(local.z) < half.z - margin.z)
                {
                    plate = a;
                    return true;
                }
            }
            plate = null;
            return false;
        }
    }
}
