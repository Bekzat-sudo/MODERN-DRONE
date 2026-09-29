using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Code-built particle effects for prototyping (explosion, fire, smoke). Replace with authored VFX later.
    /// </summary>
    public static class SimpleVfx
    {
        static Material _additive;
        static Material _alpha;
        static Texture2D _soft;

        public static void Explosion(Vector3 position, float size)
        {
            SimpleAudio.Explosion(position, size);
            var root = new GameObject("VFX_Explosion");
            root.transform.position = position;

            var flash = Emitter(root.transform, "Flash", Additive, looping: false);
            var main = flash.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * size, 9f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f * size, 2.2f * size);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
            Burst(flash, 45);
            FadeOut(flash, new Color(1f, 0.6f, 0.2f), new Color(0.3f, 0.1f, 0f));
            Shape(flash, 0.4f * size);

            var smoke = Emitter(root.transform, "Smoke", Alpha, looping: false);
            main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f * size, 4f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f * size, 3.5f * size);
            main.startColor = new Color(0.18f, 0.17f, 0.16f, 0.8f);
            main.gravityModifier = -0.08f;
            Burst(smoke, 25);
            FadeOut(smoke, new Color(0.2f, 0.19f, 0.18f), new Color(0.35f, 0.35f, 0.35f));
            Shape(smoke, 0.6f * size);

            var light = new GameObject("Flash").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.35f);
            light.range = 12f * size;
            light.intensity = 12f;
            root.AddComponent<FadeLight>().Init(light, 0.25f);

            PlayAll(root);
            Object.Destroy(root, 6f);
        }

        /// <summary>Dense grey smoke cloud laid by a vehicle's smoke grenades; drifts and thins out over ~30 s.</summary>
        public static void SmokeScreen(Vector3 position)
        {
            var root = new GameObject("VFX_SmokeScreen");
            root.transform.position = position + Vector3.up * 1.5f;
            var smoke = Emitter(root.transform, "Smoke", Alpha, looping: false);
            var main = smoke.main;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startColor = new Color(0.62f, 0.63f, 0.6f, 0.85f);
            main.gravityModifier = -0.01f;
            var emission = smoke.emission;
            emission.rateOverTime = 14f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
            FadeOut(smoke, new Color(0.7f, 0.7f, 0.68f), new Color(0.75f, 0.75f, 0.75f));
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 6f;
            var vel = smoke.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            PlayAll(root);
            Object.Destroy(root, 34f);
        }

        /// <summary>Looping flames with a smoke column, parented so it follows the vehicle.</summary>
        public static GameObject Fire(Transform parent, Vector3 worldPosition, float size)
        {
            var root = new GameObject("VFX_Fire");
            root.transform.SetParent(parent, true);
            root.transform.position = worldPosition;

            var flames = Emitter(root.transform, "Flames", Additive, looping: true);
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * size, 1.3f * size);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(1f, 0.35f, 0.05f));
            var emission = flames.emission;
            emission.rateOverTime = 35f;
            FadeOut(flames, new Color(1f, 0.6f, 0.2f), new Color(0.4f, 0.05f, 0f));
            Shape(flames, 0.5f * size);
            var vel = flames.velocityOverLifetime;
            vel.enabled = true;
            // All three axes must use the same curve mode.
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var smoke = Emitter(root.transform, "Smoke", Alpha, looping: true);
            main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f * size, 3f * size);
            main.startColor = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            emission = smoke.emission;
            emission.rateOverTime = 8f;
            FadeOut(smoke, new Color(0.12f, 0.12f, 0.12f), new Color(0.4f, 0.4f, 0.4f));
            Shape(smoke, 0.5f * size);
            vel = smoke.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            vel.x = new ParticleSystem.MinMaxCurve(0.3f, 0.8f); // light wind
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            PlayAll(root);
            SimpleAudio.FireLoop(root.transform, size);
            return root;
        }

        /// <summary>Emitters are configured stopped (duration can only change then) and started together.</summary>
        static void PlayAll(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Play(false);
        }

        static ParticleSystem Emitter(Transform parent, string name, Material material, bool looping)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = looping;
            main.duration = looping ? 1f : 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            return ps;
        }

        static void Burst(ParticleSystem ps, int count)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        static void Shape(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        static void FadeOut(ParticleSystem ps, Color start, Color end)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        static Material Additive => _additive != null ? _additive : _additive = MakeMaterial(additive: true);
        static Material Alpha => _alpha != null ? _alpha : _alpha = MakeMaterial(additive: false);

        static Material MakeMaterial(bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(shader) { name = additive ? "VFX_Additive" : "VFX_Alpha" };
            mat.SetTexture("_BaseMap", SoftTexture);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        static Texture2D SoftTexture
        {
            get
            {
                if (_soft != null) return _soft;
                const int size = 64;
                _soft = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "VFX_Soft", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
                _soft.SetPixels(px);
                _soft.Apply();
                return _soft;
            }
        }
    }

    /// <summary>Fades a light to zero over a short time.</summary>
    public sealed class FadeLight : MonoBehaviour
    {
        Light _light;
        float _duration;
        float _start;
        float _t;

        public void Init(Light light, float duration)
        {
            _light = light;
            _duration = duration;
            _start = light.intensity;
        }

        void Update()
        {
            if (_light == null) return;
            _t += Time.deltaTime;
            _light.intensity = Mathf.Lerp(_start, 0f, _t / _duration);
            if (_t >= _duration) Destroy(_light.gameObject);
        }
    }
}
