using System.Collections.Generic;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Temporarily turns struck vehicles into an x-ray view: armor becomes a ghost shell, modules are
    /// drawn as boxes coloured by state, and the strike traces are drawn as lines.
    /// </summary>
    public sealed class XRayView
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        readonly KillcamAssets _assets;
        readonly List<(Renderer renderer, Material[] materials, MaterialPropertyBlock block)> _swapped = new List<(Renderer, Material[], MaterialPropertyBlock)>();
        readonly List<GameObject> _temp = new List<GameObject>();

        public XRayView(KillcamAssets assets) => _assets = assets;

        public bool Visible { get; private set; }

        public void Show(StrikeReport report)
        {
            Hide();
            Visible = true;

            var hitModules = new HashSet<VehicleModule>();
            foreach (var h in report.hits) hitModules.Add(h.module);

            foreach (var vehicle in report.stateBefore.Keys)
            {
                if (vehicle == null) continue;
                GhostRenderers(vehicle.transform);
                foreach (var module in vehicle.Modules)
                    DrawModule(module, hitModules.Contains(module));
            }

            var lineRoot = new GameObject("XRay_Traces");
            _temp.Add(lineRoot);
            foreach (var t in report.traces)
                DrawTrace(lineRoot.transform, t);
        }

        public void Hide()
        {
            foreach (var (renderer, materials, block) in _swapped)
            {
                if (renderer == null) continue;
                renderer.sharedMaterials = materials;
                renderer.SetPropertyBlock(block);
            }
            _swapped.Clear();
            foreach (var go in _temp)
                if (go != null) Object.Destroy(go);
            _temp.Clear();
            Visible = false;
        }

        void GhostRenderers(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                var original = r.sharedMaterials;
                // Burnt paint is a property block; park it so it does not tint the ghost material.
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                r.SetPropertyBlock(null);
                _swapped.Add((r, original, block));
                var ghosts = new Material[original.Length];
                for (int i = 0; i < ghosts.Length; i++) ghosts[i] = _assets.ghost;
                r.sharedMaterials = ghosts;
            }
        }

        void DrawModule(VehicleModule module, bool wasHit)
        {
            if (!(module.GetComponent<Collider>() is BoxCollider box)) return;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(cube.GetComponent<Collider>());
            cube.name = "XRay_" + module.name;
            cube.transform.SetParent(module.transform, false);
            cube.transform.localPosition = box.center;
            cube.transform.localScale = box.size;
            _temp.Add(cube);

            var r = cube.GetComponent<Renderer>();
            r.sharedMaterial = _assets.module;
            var color = module.State switch
            {
                ModuleState.Destroyed => _assets.moduleDestroyed,
                ModuleState.Damaged => _assets.moduleDamaged,
                _ => _assets.moduleIntact
            };
            if (!wasHit && module.State == ModuleState.Operational)
                color.a *= 0.6f;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, color);
            r.SetPropertyBlock(block);
        }

        void DrawTrace(Transform parent, in Trace trace)
        {
            var go = new GameObject("Trace_" + trace.kind);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _assets.line;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, trace.from);
            lr.SetPosition(1, trace.to);
            float width = trace.kind == TraceKind.Jet || trace.kind == TraceKind.JetStopped ? 0.1f : 0.035f;
            lr.startWidth = width;
            lr.endWidth = width * 0.6f;
            var c = DamageSolver.ColorOf(trace.kind);
            lr.startColor = c;
            lr.endColor = new Color(c.r, c.g, c.b, 0.4f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            // Where the jet ended: a bright marker, so even a jet stopped a hand's width in is easy to find.
            if (trace.kind != TraceKind.Jet && trace.kind != TraceKind.JetStopped) return;
            var end = new GameObject("TraceEnd");
            end.transform.SetParent(go.transform, false);
            var ring = end.AddComponent<LineRenderer>();
            ring.sharedMaterial = _assets.line;
            ring.useWorldSpace = true;
            ring.loop = true;
            const int n = 16;
            ring.positionCount = n;
            var dir = (trace.to - trace.from).sqrMagnitude > 1e-6f ? (trace.to - trace.from).normalized : Vector3.forward;
            var rot = Quaternion.LookRotation(dir);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                ring.SetPosition(i, trace.to + rot * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.18f);
            }
            ring.startWidth = ring.endWidth = 0.04f;
            ring.startColor = ring.endColor = c;
        }
    }
}
