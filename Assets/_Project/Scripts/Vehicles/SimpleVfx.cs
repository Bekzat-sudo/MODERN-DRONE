using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    public enum ExplosionKind
    {
        /// <summary>Decide from the surroundings: near the ground, against a vehicle, or in the air.</summary>
        Auto,
        /// <summary>HE burst on or just above the ground: dirt thrown up, a dust ring rolling out.</summary>
        Ground,
        /// <summary>Warhead against a vehicle: a shower of molten liner metal and fragments, no dirt.</summary>
        Armor,
        /// <summary>Burst in the air (interceptor, drone shot down).</summary>
        Air,
        /// <summary>Explosive reactive block: sharp flash and a grey puff.</summary>
        Era,
        /// <summary>Atomised fuel igniting: a big orange fireball and oily black smoke, no fragments.</summary>
        Fuel,
        /// <summary>Ammunition detonating inside a vehicle.</summary>
        Ammo
    }

    /// <summary>
    /// Code-built particle effects: explosions, fires, smoke screens. Uses procedural volumetric flipbooks
    /// (Resources/VFX: fireball cooling to soot, billowing smoke) on soft particles.
    /// Timing follows real detonations: a hemispherical fireball visible for ~100 ms with the brightest light in the
    /// first ms (arxiv.org 2010.13537, Beirut fireball study); oxygen-deficient explosives such as TNT leave black
    /// soot smoke (en.wikipedia.org/wiki/Oxygen_balance); a ground burst throws dirt and rolls out a dust ring; burning
    /// propellant in a cook-off jets flame out of every opening for seconds (en.wikipedia.org/wiki/Cooking_off).
    /// </summary>
    public static class SimpleVfx
    {
        static Material _additive, _alpha, _smoke, _fireball, _jet;
        static Texture2D _soft;

        public static void Explosion(Vector3 position, float size, ExplosionKind kind = ExplosionKind.Auto)
        {
            SimpleAudio.Explosion(position, size);
            if (kind == ExplosionKind.Auto) kind = Classify(position);
            var root = new GameObject("VFX_Explosion_" + kind);
            root.transform.position = position;
            float s = size;

            bool fuel = kind == ExplosionKind.Fuel, ammo = kind == ExplosionKind.Ammo, era = kind == ExplosionKind.Era;
            Flash(root.transform, s * (ammo ? 1.6f : 1f), era ? 0.05f : 0.1f);
            // An ammunition detonation is not a short HE flash: propellant and shells go together into a fireball
            // that fills the view for 1.5-2 s (T-80BVM catastrophic kill footage).
            Glow(root.transform, s * (ammo ? 2.6f : fuel ? 1.8f : 1f), ammo ? 1.6f : fuel ? 0.45f : 0.18f);
            Fireball(root.transform, ammo ? s * 1.7f : s, count: fuel ? 12 : ammo ? 18 : era ? 4 : 10, life: ammo ? 2.2f : fuel ? 1.2f : 0.6f);
            if (ammo) Embers(root.transform, s, 110);
            // The soot cloud rolls out of the fireball once it cools, not over it.
            Smoke(root.transform, s, dark: !era, count: fuel || ammo ? 22 : era ? 6 : 16, life: fuel || ammo ? 9f : 6f, delay: ammo ? 0.6f : fuel ? 0.3f : 0.05f);
            if (!fuel) Sparks(root.transform, s, kind == ExplosionKind.Armor || era ? 90 : ammo ? 120 : 50);
            if (kind == ExplosionKind.Ground) Dirt(root.transform, s);

            var light = new GameObject("Flash").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.45f);
            light.range = 18f * s;
            light.intensity = 30f;
            root.AddComponent<FadeLight>().Init(light, fuel || ammo ? 0.5f : 0.12f);

            PlayAll(root);
            Object.Destroy(root, 12f);
        }

        /// <summary>Ground, vehicle or air, from what is right around the burst.</summary>
        static ExplosionKind Classify(Vector3 p)
        {
            if (Physics.CheckSphere(p, 0.9f, GameLayers.VehicleHullMask | GameLayers.ArmorMask | GameLayers.ERAMask | GameLayers.ScreenMask, QueryTriggerInteraction.Collide))
                return ExplosionKind.Armor;
            return Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, 1.8f, GameLayers.TerrainMask, QueryTriggerInteraction.Ignore)
                ? ExplosionKind.Ground : ExplosionKind.Air;
        }

        /// <summary>Burning propellant venting through hatches: a roaring flame jet for several seconds, then smoke.</summary>
        public static GameObject CookOff(Transform parent, Vector3 worldPosition, float seconds)
        {
            var root = new GameObject("VFX_CookOff");
            root.transform.SetParent(parent, true);
            root.transform.position = worldPosition;
            VentJet(root.transform, seconds, new Vector3(-90f, 0f, 0f), 1f);
            // Flames also burst sideways out of the torn hull now and then.
            VentJet(root.transform, seconds * 0.7f, new Vector3(-10f, Random.Range(0f, 360f), 0f), 0.6f);
            SimpleAudio.FireLoop(root.transform, 2.5f);
            // Burning nitrocellulose propellant smokes white-grey, apart from the black fuel smoke.
            Smoke(root.transform, 1.5f, dark: false, count: 30, life: 10f, duration: seconds);
            PlayAll(root);
            Object.Destroy(root, seconds + 12f);
            return root;
        }

        static void VentJet(Transform root, float seconds, Vector3 direction, float strength)
        {
            var jet = Emitter(root, "Jet", Jet, looping: false);
            var main = jet.main;
            main.duration = seconds;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 18f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startColor = new Color(1f, 0.85f, 0.6f, 1f);
            var em = jet.emission;
            // Uneven venting: the jet flares and dies back as charges burn through.
            var flare = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(0.5f, 0.3f),
                new Keyframe(0.65f, 0.9f), new Keyframe(1f, 0.1f));
            em.rateOverTime = new ParticleSystem.MinMaxCurve(40f, flare);
            var shape = jet.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.35f;
            shape.rotation = direction;
            Grow(jet, 1f, 2.2f);
            Flipbook(jet, fireball: true);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f * strength, 18f * strength);
            em.rateOverTimeMultiplier = 40f * strength;
        }

        static void Flash(Transform root, float s, float life)
        {
            var ps = Emitter(root, "Flash", Additive, looping: false);
            var main = ps.main;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(3.5f * s, 5f * s);
            main.startColor = new Color(1f, 0.95f, 0.8f, 1f);
            Burst(ps, 2);
            FadeOut(ps, new Color(1f, 0.95f, 0.85f), new Color(1f, 0.55f, 0.2f));
        }

        /// <summary>The first instants: an additive burst of the fireball flipbook that glows over the scene.</summary>
        static void Glow(Transform root, float s, float life)
        {
            var ps = Emitter(root, "Glow", Jet, looping: false);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * s, 2.5f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f * s, 4.5f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 0.9f, 0.7f, 1f);
            Burst(ps, 10);
            Shape(ps, 0.3f * s);
            Grow(ps, 0.6f, 1.3f);
            Flipbook(ps, fireball: true);
            // Only the hot frames and a fade to nothing: added-up smoke frames would haze the whole scene.
            var sheet = ps.textureSheetAnimation;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.3f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.3f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 0.8f) });
            col.color = g;
        }

        static void Fireball(Transform root, float s, int count, float life)
        {
            var ps = Emitter(root, "Fireball", FireballMat, looping: false);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f * s, 4f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(2.4f * s, 4.2f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(ps, count);
            Shape(ps, 0.45f * s);
            Grow(ps, 0.7f, 1.6f);
            Flipbook(ps, fireball: true);
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.5f * s;
            limit.dampen = 0.3f;
        }

        static void Smoke(Transform root, float s, bool dark, int count, float life, float duration = 0.2f, float delay = 0f)
        {
            var ps = Emitter(root, "Smoke", SmokeMat, looping: false);
            var main = ps.main;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f * s, 3f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(4.5f * s, 7.5f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // TNT-type soot is near black; ERA and propellant-free puffs are grey. Many overlapping, see-through puffs
            // read as one cloud instead of separate balls.
            main.startColor = dark ? new Color(0.15f, 0.14f, 0.13f, 0.7f) : new Color(0.5f, 0.5f, 0.49f, 0.6f);
            main.gravityModifier = -0.03f;
            if (duration > 0.3f)
            {
                var em = ps.emission;
                em.rateOverTime = count / duration;
            }
            else
            {
                main.duration = delay + 0.2f;
                ps.emission.SetBursts(new[] { new ParticleSystem.Burst(delay, (short)count) });
            }
            Shape(ps, 0.9f * s);
            Grow(ps, 0.8f, 2.4f);
            Flipbook(ps, fireball: false);
            Wind(ps, 0.6f, 1.4f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1.6f, 1.6f, 1.6f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1.5f;
            limit.dampen = 0.15f;
        }

        /// <summary>
        /// Burning propellant grains and hot debris: long-lived glowing embers thrown out, landing and glowing on the
        /// ground around the wreck.
        /// </summary>
        static void Embers(Transform root, float s, int count)
        {
            var ps = Emitter(root, "Embers", Additive, looping: false);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f * s, 16f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.4f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = 0.8f;
            Burst(ps, count);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.8f * s;
            var coll = ps.collision;
            coll.enabled = true;
            coll.type = ParticleSystemCollisionType.World;
            coll.quality = ParticleSystemCollisionQuality.Low;
            coll.collidesWith = GameLayers.TerrainMask;
            coll.bounce = 0.15f;
            coll.dampen = 0.7f;
            coll.lifetimeLoss = 0f;
            FadeOut(ps, new Color(1f, 0.85f, 0.5f), new Color(0.9f, 0.25f, 0.05f));
        }

        /// <summary>Incandescent fragments and molten metal: stretched streaks falling under gravity.</summary>
        static void Sparks(Transform root, float s, int count)
        {
            var ps = Emitter(root, "Sparks", Additive, looping: false);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(25f * Mathf.Sqrt(s), 70f * Mathf.Sqrt(s));
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.55f, 0.15f));
            main.gravityModifier = 1f;
            Burst(ps, count);
            Shape(ps, 0.2f * s);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f;
            r.lengthScale = 1f;
            FadeOut(ps, new Color(1f, 0.85f, 0.5f), new Color(1f, 0.3f, 0.05f));
        }

        /// <summary>Ground burst: dirt clods thrown in a cone, a brown column, and a dust ring rolling out along the ground.</summary>
        static void Dirt(Transform root, float s)
        {
            var clods = Emitter(root, "Clods", Alpha, looping: false);
            var main = clods.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f * s, 16f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f * s, 0.3f * s);
            main.startColor = new Color(0.16f, 0.12f, 0.08f, 1f);
            main.gravityModifier = 1f;
            Burst(clods, 70);
            var shape = clods.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.3f * s;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var column = Emitter(root, "DirtColumn", SmokeMat, looping: false);
            main = column.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f * s, 9f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f * s, 2.8f * s);
            main.startColor = new Color(0.36f, 0.29f, 0.21f, 0.9f);
            main.gravityModifier = 0.35f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(column, 10);
            shape = column.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.4f * s;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Grow(column, 0.8f, 2f);
            Flipbook(column, fireball: false);

            var ring = Emitter(root, "DustRing", SmokeMat, looping: false);
            main = ring.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f * s, 11f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f * s, 2.6f * s);
            main.startColor = new Color(0.5f, 0.43f, 0.33f, 0.75f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(ring, 22);
            shape = ring.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f * s;
            shape.rotation = new Vector3(-90f, 0f, 0f); // flat on the ground, particles fly outward
            var limit = ring.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.6f;
            limit.dampen = 0.08f;
            Grow(ring, 1f, 2.4f);
            Flipbook(ring, fireball: false);
            Wind(ring, 0.3f, 0.8f);
        }

        /// <summary>Debris burning on the ground around a wreck: a few small fires that die down after a while.</summary>
        public static void ScatterFires(Vector3 centre, int count, float radius)
        {
            for (int i = 0; i < count; i++)
            {
                var p = centre + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(radius * 0.4f, radius);
                if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 40f, GameLayers.TerrainMask, QueryTriggerInteraction.Ignore))
                    p = hit.point;
                var fire = Fire(null, p, Random.Range(0.3f, 0.6f));
                Object.Destroy(fire, Random.Range(25f, 50f));
            }
        }

        /// <summary>Dense grey smoke cloud laid by a vehicle's smoke grenades; drifts and thins out over ~30 s.</summary>
        public static void SmokeScreen(Vector3 position)
        {
            var root = new GameObject("VFX_SmokeScreen");
            root.transform.position = position + Vector3.up * 1.5f;
            var smoke = Emitter(root.transform, "Smoke", SmokeMat, looping: false);
            var main = smoke.main;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startColor = new Color(0.75f, 0.76f, 0.73f, 0.9f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.01f;
            var emission = smoke.emission;
            emission.rateOverTime = 14f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 6f;
            Grow(smoke, 1f, 1.6f);
            Flipbook(smoke, fireball: false);
            Wind(smoke, 0.4f, 1.0f);
            PlayAll(root);
            Object.Destroy(root, 34f);
        }

        /// <summary>Looping flames with an oily black smoke column, parented so it follows the vehicle.</summary>
        public static GameObject Fire(Transform parent, Vector3 worldPosition, float size)
        {
            var root = new GameObject("VFX_Fire");
            root.transform.SetParent(parent, true);
            root.transform.position = worldPosition;

            var flames = Emitter(root.transform, "Flames", Jet, looping: true);
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f * size, 1.8f * size);
            main.startColor = new Color(1f, 0.9f, 0.75f, 1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = flames.emission;
            emission.rateOverTime = 14f;
            Shape(flames, 0.5f * size);
            var vel = flames.velocityOverLifetime;
            vel.enabled = true;
            // All three axes must use the same curve mode.
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Grow(flames, 1f, 0.4f);
            Flipbook(flames, fireball: true);

            var smoke = Emitter(root.transform, "Smoke", SmokeMat, looping: true);
            main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Max(2.2f, 2.4f * size), Mathf.Max(3.5f, 4f * size));
            main.startColor = new Color(0.08f, 0.075f, 0.07f, 0.5f); // burning diesel, rubber and paint: black, oily
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            emission = smoke.emission;
            // Particle budget: a burning wreck can have several of these at once (Graphics Ring Buffer); larger,
            // see-through puffs still merge into one column.
            emission.rateOverTime = 8f;
            Shape(smoke, 0.5f * size);
            vel = smoke.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 1.2f); // light wind
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0.3f);
            Grow(smoke, 1f, 3.5f);
            Flipbook(smoke, fireball: false, startFrame: 0.4f); // spread-out frames: a column, not a string of balls

            PlayAll(root);
            SimpleAudio.FireLoop(root.transform, size);
            return root;
        }

        // ---- building blocks

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
            main.maxParticles = 250;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

        static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }

        /// <summary>8 x 8 flipbook over the particle's life, random start rotation handled by the caller.</summary>
        static void Flipbook(ParticleSystem ps, bool fireball, float startFrame = 0.1f)
        {
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 8;
            sheet.numTilesY = 8;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            // Fireballs race through the hot frames; smoke eases through its billowing.
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, fireball ? AnimationCurve.Linear(0f, 0f, 1f, 1f) : AnimationCurve.EaseInOut(0f, startFrame, 1f, 1f));
        }

        static void Wind(ParticleSystem ps, float min, float max)
        {
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(min, max);
            vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.3f);
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

        static Material Additive => _additive != null ? _additive : _additive = MakeMaterial("VFX_Additive", SoftTexture, true, new Color(3f, 2.6f, 2.2f));
        static Material Alpha => _alpha != null ? _alpha : _alpha = MakeMaterial("VFX_Alpha", SoftTexture, false);
        static Material SmokeMat => _smoke != null ? _smoke : _smoke = MakeMaterial("VFX_Smoke", Load("VFX/SmokeFlipbook"), false);
        // Fire is far brighter than daylight: HDR tints (> 1) push it over the bloom threshold and blow it out to
        // white-yellow like a camera does (T-80BVM footage), while the cooled smoke frames stay dark.
        static Material FireballMat => _fireball != null ? _fireball : _fireball = MakeMaterial("VFX_Fireball", Load("VFX/FireballFlipbook"), false, new Color(1.5f, 1.35f, 1.2f));
        /// <summary>Flames: the fireball flipbook blended additively so it glows.</summary>
        static Material Jet => _jet != null ? _jet : _jet = MakeMaterial("VFX_Flames", Load("VFX/FireballFlipbook"), true, new Color(4f, 3.2f, 2.4f));

        static Texture2D Load(string path) => Resources.Load<Texture2D>(path) ?? SoftTexture;

        static Material MakeMaterial(string name, Texture2D texture, bool additive, Color? tint = null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(shader) { name = name };
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", tint ?? Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // Soft particles: fade where the sprite meets the ground or a hull instead of a hard cut (needs the depth texture).
            mat.SetFloat("_SoftParticlesEnabled", 1f);
            mat.EnableKeyword("_SOFTPARTICLES_ON");
            mat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 1.5f, 0f, 0f));
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
