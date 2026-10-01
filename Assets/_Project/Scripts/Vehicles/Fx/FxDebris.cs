using System.Collections.Generic;
using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Physical fragments thrown by big blasts: torn hull pieces, bits of track and stowage. Rigid bodies on the Debris
    /// layer (they collide with the terrain, hulls and each other, never with the drone) with continuous collision so a
    /// fast piece cannot tunnel through the ground. Hot pieces trail smoke and glow, cool down, and are pooled after a
    /// while. Throw speeds are look values (abstract), sized so pieces land within tens of metres.
    /// </summary>
    public sealed class FxDebris : MonoBehaviour
    {
        const int PoolSize = 24;
        static readonly List<FxDebris> _all = new List<FxDebris>();
        static int _next;
        static Mesh[] _meshes;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        Rigidbody _rb;
        ParticleSystem _trail;
        Renderer _renderer;
        MaterialPropertyBlock _block;
        float _age, _life, _heat;

        /// <summary>Throws <paramref name="count"/> pieces from <paramref name="centre"/> (sizes ~0.1-0.6 m, scaled by <paramref name="size"/>).</summary>
        public static void Throw(Vector3 centre, int count, float speed, float size = 1f, bool hot = true)
        {
            for (int i = 0; i < count; i++)
            {
                var d = Get();
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 1.5f + 0.4f; // blasts lift debris
                d.Launch(centre + dir.normalized * 0.4f * size, dir.normalized * speed * Random.Range(0.4f, 1.1f),
                    Random.Range(0.08f, 0.35f) * Mathf.Sqrt(size), hot);
            }
        }

        public static int Active { get { int n = 0; foreach (var d in _all) if (d != null && d.gameObject.activeSelf) n++; return n; } }

        static FxDebris Get()
        {
            _all.RemoveAll(d => d == null);
            if (_all.Count < PoolSize)
            {
                var d = Create();
                _all.Add(d);
                return d;
            }
            _next = (_next + 1) % _all.Count;
            return _all[_next];
        }

        static FxDebris Create()
        {
            if (_meshes == null || _meshes[0] == null) _meshes = BuildMeshes();
            var go = new GameObject("VFX_Debris") { layer = GameLayers.Debris };
            go.AddComponent<MeshFilter>().sharedMesh = _meshes[Random.Range(0, _meshes.Length)];
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = FxAssets.Debris;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one * 0.8f;
            var rb = go.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.2f;
            var d = go.AddComponent<FxDebris>();
            d._rb = rb;
            d._renderer = r;
            d._trail = SimpleVfx.DebrisTrail(go.transform);
            d._block = new MaterialPropertyBlock();
            go.SetActive(false);
            return d;
        }

        void Launch(Vector3 at, Vector3 velocity, float scale, bool hot)
        {
            transform.SetParent(null);
            transform.SetPositionAndRotation(at, Random.rotation);
            transform.localScale = new Vector3(scale * Random.Range(0.6f, 1.4f), scale * Random.Range(0.2f, 0.6f), scale * Random.Range(0.6f, 1.6f));
            gameObject.SetActive(true);
            _rb.mass = Mathf.Max(0.5f, 60f * scale * scale * scale);
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Random.insideUnitSphere * 15f;
            _age = 0f;
            _life = Random.Range(25f, 40f);
            _heat = hot ? 1f : 0f;
            if (_trail != null)
            {
                _trail.Clear();
                if (hot) _trail.Play(); else _trail.Stop();
            }
            SetGlow();
        }

        void SetGlow()
        {
            float h = _heat * _heat;
            _block.SetColor(EmissionId, new Color(4f, 1.3f, 0.3f) * h);
            _renderer.SetPropertyBlock(_block);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;
            if (_heat > 0f)
            {
                _heat = Mathf.Max(0f, _heat - dt / 6f); // glowing steel dims within seconds
                SetGlow();
                if (_heat <= 0.3f && _trail != null && _trail.isEmitting) _trail.Stop();
            }
            if (_age > _life || transform.position.y < -500f) gameObject.SetActive(false);
        }

        /// <summary>A few torn-plate shapes: boxes with some vertices pulled in, so pieces read as fragments, not cubes.</summary>
        static Mesh[] BuildMeshes()
        {
            var src = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cube = src.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(src);
            var rng = new System.Random(5);
            var meshes = new Mesh[4];
            for (int m = 0; m < meshes.Length; m++)
            {
                var v = cube.vertices;
                var offsets = new Dictionary<Vector3, Vector3>();
                for (int i = 0; i < v.Length; i++)
                {
                    if (!offsets.TryGetValue(v[i], out var o))
                    {
                        o = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.45f;
                        offsets[v[i]] = o;
                    }
                    v[i] += o;
                }
                meshes[m] = new Mesh { name = "Fragment" + m, vertices = v, triangles = cube.triangles, uv = cube.uv };
                meshes[m].RecalculateNormals();
                meshes[m].RecalculateBounds();
            }
            return meshes;
        }
    }
}
