using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    public enum ExplosionKind
    {
        /// <summary>Decide from the surroundings: near the ground, against a vehicle, or in the air.</summary>
        Auto,
        /// <summary>HE burst on or just above the ground: dirt thrown up, a dust skirt rolling out.</summary>
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
    /// Code-built, pooled particle effects: explosions, fires, smoke screens. Smoke and fire are lit six-way flipbooks
    /// baked from Mantaflow gas simulations (Tools/Blender/vfx_bake.py; shader ModernDrone/Particles/SixWay): the sun,
    /// the sky and every nearby light (flashes, burning vehicles) shade them, the ground and hulls cut them softly.
    /// A detonation plays in phases seen on strike footage and in fireball studies: a flash brightest in the first
    /// milliseconds with the visible fireball gone in ~100 ms for small charges (arxiv.org 2010.13537, Beirut study),
    /// a refraction ring as the blast front leaves (en.wikipedia.org/wiki/Blast_wave), sparks of liner metal and
    /// fragments, dirt and a brown dust skirt for ground bursts, then a column of soot that rises and drifts with the
    /// wind (TNT-type charges are oxygen-poor and smoke black: en.wikipedia.org/wiki/Oxygen_balance). Ammunition
    /// detonations fill the view with fire for 1.5-2 s (T-80BVM / T-72 catastrophic-kill footage), burning propellant
    /// jets out of every opening for seconds with white-grey smoke, fuel fires burn with black smoke
    /// (en.wikipedia.org/wiki/Cooking_off). Sizes and counts are look values (abstract), size 1 = a drone warhead.
    /// </summary>
    public static class SimpleVfx
    {
        // ---------------------------------------------------------------- public API

        public static void Explosion(Vector3 position, float size, ExplosionKind kind = ExplosionKind.Auto)
        {
            if (kind == ExplosionKind.Auto) kind = Classify(position);
            SimpleAudio.Explosion(position, size, kind);
            Register();
            var fx = FxPool.Spawn("exp_" + kind, position, Quaternion.identity);
            fx.SetScale(size);
            fx.Lifetime = kind == ExplosionKind.Ammo || kind == ExplosionKind.Fuel ? 22f : 16f;
            SetWind(fx);
            bool ground = kind == ExplosionKind.Ground || GroundBelow(position, 1.5f * size, out _);
            fx.OnStart(f =>
            {
                Shockwaves.Emit(position, size);
                // A puff of smoke the size of the cloud hides what is behind it for a while.
                SmokeVolumes.Add(position + Vector3.up * 2f * size, 3.5f * Mathf.Sqrt(size) + 1.5f, kind == ExplosionKind.Era ? 0.25f : 0.6f, Time.time + 9f);
                if (ground && kind != ExplosionKind.Air) Scorch(position, size, kind);
                if (kind == ExplosionKind.Ammo) FxDebris.Throw(position, 6, 22f, 1.2f);
                else if (kind == ExplosionKind.Armor && size >= 0.8f) FxDebris.Throw(position, 2, 12f, 0.6f);
                ShakeTrees(position, size);
            });
        }

        /// <summary>Ground, vehicle or air, from what is right around the burst.</summary>
        static ExplosionKind Classify(Vector3 p)
        {
            if (Physics.CheckSphere(p, 0.9f, GameLayers.VehicleHullMask | GameLayers.ArmorMask | GameLayers.ERAMask | GameLayers.ScreenMask, QueryTriggerInteraction.Collide))
                return ExplosionKind.Armor;
            return GroundBelow(p, 1.8f, out _) ? ExplosionKind.Ground : ExplosionKind.Air;
        }

        static bool GroundBelow(Vector3 p, float within, out RaycastHit hit) =>
            Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out hit, within + 0.3f, GameLayers.TerrainMask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// Burning propellant venting through an opening: a roaring flame jet along <paramref name="direction"/> for
        /// <paramref name="seconds"/>, flaring and dying back as charges burn through, with white-grey smoke.
        /// </summary>
        public static FxEffect CookOff(Transform parent, Vector3 worldPosition, float seconds, Vector3? direction = null, float strength = 1f)
        {
            Register();
            Vector3 dir = direction ?? Vector3.up;
            var fx = FxPool.Spawn("jet", worldPosition, Quaternion.FromToRotation(Vector3.up, dir), parent);
            fx.SetScale(Mathf.Clamp(strength, 0.4f, 2f));
            fx.Lifetime = seconds + 14f;
            SetWind(fx);
            fx.OnStart(f => SimpleAudio.FlameJet(f.transform, seconds, strength));
            // Duration can only change on a stopped system: stop, set, restart.
            var jet = fx.transform.Find("Jet").GetComponent<ParticleSystem>();
            jet.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = jet.main;
            main.duration = Mathf.Max(0.5f, seconds);
            var smoke = fx.transform.Find("Smoke").GetComponent<ParticleSystem>();
            smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = smoke.main;
            sm.duration = Mathf.Max(0.5f, seconds + 4f);
            jet.Play(false);
            smoke.Play(false);
            return fx;
        }

        /// <summary>
        /// Looping flames with a smoke column, parented so it follows the vehicle. <paramref name="direction"/> is the
        /// opening the fire burns out of (flames lean out of it, then rise); <paramref name="blackSmoke"/> for fuel,
        /// rubber and paint, grey-white for propellant.
        /// </summary>
        public static FxEffect Fire(Transform parent, Vector3 worldPosition, float size, Vector3? direction = null, bool blackSmoke = true)
        {
            Register();
            Vector3 dir = direction ?? Vector3.up;
            var fx = FxPool.Spawn(blackSmoke ? "fire" : "fire_grey", worldPosition, Quaternion.FromToRotation(Vector3.up, dir), parent);
            fx.SetScale(size, Mathf.Clamp(size, 0.6f, 1.6f));
            SetDelay(fx, 0f);
            SetWind(fx);
            fx.OnStart(f =>
            {
                var src = SimpleAudio.FireLoop(f.transform, size);
                f.Released += _ => { if (src != null) Object.Destroy(src); };
            });
            return fx;
        }

        /// <summary>
        /// Smoke seeping out of a closed hull (seams, vision blocks) while a fire burns inside, or the smouldering column
        /// of a burnt-out wreck: no flames.
        /// </summary>
        public static FxEffect Seep(Transform parent, Vector3 worldPosition, bool blackSmoke, float size = 1f)
        {
            Register();
            var fx = FxPool.Spawn(blackSmoke ? "seep" : "seep_grey", worldPosition, Quaternion.identity, parent);
            fx.SetScale(size, Mathf.Clamp(size, 0.6f, 1.6f));
            SetWind(fx);
            return fx;
        }

        /// <summary>Debris burning on the ground around a wreck: a few small fires that die down after a while.</summary>
        public static void ScatterFires(Vector3 centre, int count, float radius)
        {
            for (int i = 0; i < count; i++)
            {
                var p = centre + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(radius * 0.4f, radius);
                if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 40f, GameLayers.TerrainMask, QueryTriggerInteraction.Ignore))
                    p = hit.point;
                var fire = Fire(null, p, Random.Range(0.3f, 0.55f));
                // Burning debris lands a moment after the blast, then catches.
                SetDelay(fire, Random.Range(1f, 3.5f));
                DelayStop(fire, Random.Range(25f, 50f));
            }
        }

        static void SetDelay(FxEffect fx, float seconds)
        {
            // The delay only applies from the next Play: restart the (just spawned) systems.
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.startDelay = seconds;
                ps.Play(false);
            }
            foreach (var l in fx.Lights) l.SetDelay(seconds);
        }

        static void DelayStop(FxEffect fx, float after)
        {
            if (!fx.TryGetComponent(out FxStopAfter stopper)) stopper = fx.gameObject.AddComponent<FxStopAfter>();
            stopper.Arm(after);
        }

        /// <summary>Dense grey smoke cloud laid by a vehicle's smoke grenades; drifts and thins out over ~30 s.</summary>
        public static void SmokeScreen(Vector3 position)
        {
            Register();
            var fx = FxPool.Spawn("smokescreen", position + Vector3.up * 1.5f, Quaternion.identity);
            fx.Lifetime = 36f;
            SetWind(fx, 0.6f);
            // Screening grenades build a wall a few tens of metres wide within seconds (abstract density).
            fx.OnStart(f =>
            {
                for (int i = 0; i < 4; i++)
                    SmokeVolumes.Add(position + Wind.Current * (2f + i * 3f) + Vector3.up * 3f, 9f, 1.5f, Time.time + 28f);
            });
        }

        // ---------------------------------------------------------------- templates

        static void Register()
        {
            // The pool forgets its kinds on entering play mode (statics survive without a domain reload).
            if (FxPool.Has("jet")) return;
            FxClock.Ensure();
            foreach (ExplosionKind k in System.Enum.GetValues(typeof(ExplosionKind)))
            {
                if (k == ExplosionKind.Auto) continue;
                var kind = k;
                FxPool.Register("exp_" + kind, () => BuildExplosion(kind), kind == ExplosionKind.Era ? 6 : 4);
            }
            FxPool.Register("jet", BuildJet, 6);
            FxPool.Register("fire", () => BuildFire(true), 14);
            FxPool.Register("fire_grey", () => BuildFire(false), 8);
            FxPool.Register("smokescreen", BuildSmokeScreen, 3);
            FxPool.Register("seep", () => BuildSeep(true), 8);
            FxPool.Register("seep_grey", () => BuildSeep(false), 6);
            FxPool.Register("leaves", BuildLeaves, 6);
        }

        static GameObject BuildExplosion(ExplosionKind kind)
        {
            var root = new GameObject("VFX_Explosion_" + kind);
            var t = root.transform;
            bool ammo = kind == ExplosionKind.Ammo, fuel = kind == ExplosionKind.Fuel, era = kind == ExplosionKind.Era;
            bool armor = kind == ExplosionKind.Armor, groundKind = kind == ExplosionKind.Ground;

            // 1. Flash: a white-hot burst and its light, a few frames long.
            Flash(t, ammo ? 9f : era ? 3.5f : fuel ? 6f : 5f, era ? 0.04f : 0.07f);
            Light(t, "FlashLight", FxLight.Mode.Flash, ammo ? 60f : 30f, ammo ? 40f : 25f, era ? 0.06f : 0.12f, new Color(1f, 0.85f, 0.65f));

            // 2. Fireball: lit, glowing gas cooling into soot (simulated flipbook).
            {
                // Each flipbook frame is the whole simulated fireball: a few copies (their glow adds up), not dozens.
                int count = ammo ? 4 : fuel ? 4 : era ? 1 : armor ? 2 : 3;
                float life = ammo ? 3.6f : fuel ? 2.6f : era ? 0.9f : 1.6f;
                var fb = Fireball(t, ammo ? 6.5f : fuel ? 5f : era ? 2f : 3.4f, count, life, fuel ? 0.06f : 0.2f);
                if (ammo)
                {
                    // Charges and shells go one after another: a second, rolling wave of fire.
                    var em = fb.emission;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count), new ParticleSystem.Burst(0.25f, 2), new ParticleSystem.Burst(0.6f, 2) });
                    var main = fb.main;
                    main.duration = 0.8f;
                }
                Light(t, "GlowLight", FxLight.Mode.Glow, ammo ? 14f : fuel ? 10f : 6f, ammo ? 40f : 26f, ammo ? 2.2f : fuel ? 1.1f : era ? 0.15f : 0.35f, new Color(1f, 0.55f, 0.25f));
            }

            // 3. Blast front: a ring of bent light racing out.
            ShockRing(t, ammo ? 55f : era ? 14f : 28f, ammo ? 0.16f : 0.1f);

            // 4. Fragments, molten liner metal, burning propellant grains.
            if (!fuel) Sparks(t, armor || era ? 70 : ammo ? 110 : 45, armor);
            if (ammo) Embers(t, 90);
            if (groundKind) Dirt(t);

            // 5. Smoke rolling out of the cooling fireball, then rising and drifting.
            Color soot = era ? new Color(0.55f, 0.55f, 0.53f) : fuel ? new Color(0.07f, 0.065f, 0.06f) : ammo ? new Color(0.28f, 0.27f, 0.26f) : new Color(0.18f, 0.17f, 0.16f);
            Smoke(t, "Smoke", FxAssets.SmokeHot, soot, ammo ? 6f : era ? 3f : 4.2f, ammo ? 20 : fuel ? 16 : era ? 5 : 11,
                ammo || fuel ? 16f : era ? 7f : 12f, delay: ammo ? 0.4f : fuel ? 0.25f : 0.08f, alpha: era ? 0.6f : 0.85f);
            return root;
        }

        static GameObject BuildJet()
        {
            var root = new GameObject("VFX_CookOff");
            var t = root.transform;
            var jet = Emitter(t, "Jet", FxAssets.Flame, false, 160);
            SixWay(jet, Flipbook.Flame, heat: new[] { (0f, 1.3f), (0.6f, 1f), (1f, 0.4f) });
            var main = jet.main;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 18f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f / FlameFill, 2.2f / FlameFill);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            main.startColor = new Color(1f, 1f, 1f, 1f);
            var em = jet.emission;
            // Uneven venting: the jet flares and dies back as charges burn through.
            var flare = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.45f), new Keyframe(0.3f, 1f), new Keyframe(0.5f, 0.35f),
                new Keyframe(0.65f, 0.9f), new Keyframe(0.85f, 0.4f), new Keyframe(1f, 0.1f));
            em.rateOverTime = new ParticleSystem.MinMaxCurve(45f, flare);
            var shape = jet.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // cone along the effect's up = the opening's normal
            Grow(jet, 0.8f, 2.4f);
            Drag(jet, 2.5f);
            Light(t, "JetLight", FxLight.Mode.Fire, 12f, 22f, 0f, new Color(1f, 0.62f, 0.3f)).transform.localPosition = Vector3.up * 2f;
            // Burning nitrocellulose propellant smokes white-grey.
            var smoke = Smoke(t, "Smoke", FxAssets.SmokeHot, new Color(0.62f, 0.61f, 0.6f), 3.2f, 0, 11f, alpha: 0.7f);
            var sm = smoke.main;
            sm.duration = 14f;
            var sem = smoke.emission;
            sem.rateOverTime = 5f;
            smoke.transform.localPosition = Vector3.up * 2.5f;
            Haze(t, 4f).transform.localPosition = Vector3.up * 3f;
            return root;
        }

        static GameObject BuildFire(bool blackSmoke)
        {
            var root = new GameObject("VFX_Fire");
            var t = root.transform;
            var flames = Emitter(t, "Flames", FxAssets.Flame, true, 60);
            SixWay(flames, Flipbook.Flame, heat: new[] { (0f, 1.1f), (0.5f, 1f), (1f, 0.5f) });
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.4f / FlameFill, 2.4f / FlameFill);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var em = flames.emission;
            em.rateOverTime = 14f;
            var shape = flames.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.45f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Grow(flames, 0.8f, 1.1f);
            // Hot gas rises (buoyancy) whatever the opening's direction.
            var vel = flames.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Light(t, "FireLight", FxLight.Mode.Fire, 4.5f, 13f, 0f, new Color(1f, 0.55f, 0.25f)).transform.localPosition = Vector3.up * 1.2f;

            // Burning diesel, rubber and paint: black, oily; propellant and wood: grey.
            Color c = blackSmoke ? new Color(0.11f, 0.1f, 0.095f) : new Color(0.5f, 0.49f, 0.47f);
            var smoke = Smoke(t, "Smoke", FxAssets.SmokeHot, c, 3.2f, 0, 12f, alpha: blackSmoke ? 0.9f : 0.75f, looping: true);
            var sem = smoke.emission;
            // Particle budget: a burning wreck can have several of these at once; big, see-through puffs merge into one column.
            sem.rateOverTime = 6f;
            smoke.transform.localPosition = Vector3.up * 1.4f;
            Haze(t, 2.5f).transform.localPosition = Vector3.up * 2.2f;
            var sparks = Emitter(t, "Embers", FxAssets.Embers, true, 30);
            var sp = sparks.main;
            sp.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            sp.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            sp.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            sp.startColor = new Color(1f, 0.6f, 0.25f);
            sp.gravityModifier = -0.15f;
            var spe = sparks.emission;
            spe.rateOverTime = 5f;
            var noise = sparks.noise;
            noise.enabled = true;
            noise.strength = 1.2f;
            noise.frequency = 0.6f;
            FadeOut(sparks, new Color(1f, 0.75f, 0.4f), new Color(0.9f, 0.25f, 0.05f));
            return root;
        }

        static GameObject BuildSeep(bool black)
        {
            var root = new GameObject("VFX_Seep");
            Color c = black ? new Color(0.09f, 0.085f, 0.08f) : new Color(0.55f, 0.54f, 0.52f);
            var smoke = Smoke(root.transform, "Smoke", FxAssets.SmokeHot, c, 2.2f, 0, 12f, alpha: 0.75f, looping: true);
            var em = smoke.emission;
            em.rateOverTime = 4f;
            var main = smoke.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            return root;
        }

        static GameObject BuildSmokeScreen()
        {
            var root = new GameObject("VFX_SmokeScreen");
            var smoke = Smoke(root.transform, "Smoke", FxAssets.Smoke, new Color(0.78f, 0.79f, 0.76f), 7f, 0, 24f, alpha: 1f);
            var main = smoke.main;
            main.duration = 6f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            var emission = smoke.emission;
            emission.rateOverTime = 7f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 6f;
            return root;
        }

        /// <summary>Leaves and twigs stripped from a crown by the blast, fluttering down.</summary>
        static GameObject BuildLeaves()
        {
            var root = new GameObject("VFX_Leaves");
            var ps = Emitter(root.transform, "Leaves", FxAssets.Leaves, false, 80);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.5f, 0.24f), new Color(0.62f, 0.56f, 0.3f));
            main.gravityModifier = 0.05f;
            Burst(ps, 40);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 2.5f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.4f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = sheet.numTilesY = 2;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            var coll = ps.collision;
            coll.enabled = true;
            coll.type = ParticleSystemCollisionType.World;
            coll.quality = ParticleSystemCollisionQuality.Low;
            coll.collidesWith = GameLayers.TerrainMask;
            coll.bounce = 0f;
            coll.dampen = 1f;
            Alpha(ps, 1f, 0.85f);
            return root;
        }

        /// <summary>Smoke trail of a hot fragment (pooled with the fragment).</summary>
        internal static ParticleSystem DebrisTrail(Transform parent)
        {
            var ps = Emitter(parent, "Trail", FxAssets.SmokeHot, true, 60);
            SixWay(ps, Flipbook.Smoke);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f / SmokeFill, 0.8f / SmokeFill);
            main.startColor = new Color(0.12f, 0.115f, 0.11f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.rateOverDistance = 4f;
            Grow(ps, 0.6f, 2.5f);
            Alpha(ps, 0.9f, 0f);
            return ps;
        }

        // ---------------------------------------------------------------- phases

        static void Flash(Transform root, float size, float life)
        {
            var ps = Emitter(root, "Flash", FxAssets.Flash, false, 4);
            var main = ps.main;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size);
            main.startColor = new Color(1f, 0.95f, 0.85f, 1f);
            Burst(ps, 2);
            FadeOut(ps, new Color(1f, 0.97f, 0.9f), new Color(1f, 0.6f, 0.25f));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortingFudge = -40f; // drawn over the fireball
        }

        static ParticleSystem Fireball(Transform root, float size, int count, float life, float rise)
        {
            var ps = Emitter(root, "Fireball", FxAssets.Fireball, false, 40);
            SixWay(ps, Flipbook.Explosion, heat: new[] { (0f, 1.25f), (0.08f, 1.1f), (0.25f, 0.7f), (0.45f, 0.15f), (0.6f, 0f) },
                soot: new[] { (0f, 0f), (0.3f, 0.3f), (1f, 0.6f) });
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.75f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.75f / ExplosionFill, size / ExplosionFill);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f); // the gas rises up the tile: small roll only
            main.startColor = new Color(0.32f, 0.3f, 0.28f, 1f);
            main.gravityModifier = -rise;
            Burst(ps, count);
            Shape(ps, 0.4f);
            Grow(ps, 0.85f, 1.6f);
            Drag(ps, 3f);
            Alpha(ps, 1f, 0f, fadeFrom: 0.7f);
            return ps;
        }

        static void ShockRing(Transform root, float radius, float life)
        {
            var ps = Emitter(root, "ShockRing", FxAssets.ShockRing, false, 2);
            var main = ps.main;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = radius * 2f;
            main.startColor = new Color(1f, 1f, 1f, 1f);
            Burst(ps, 1);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            // The front slows as it weakens: fast at first.
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.05f, 0f, 3f), new Keyframe(1f, 1f, 0.5f, 0f)));
            Alpha(ps, 1f, 0f, fadeFrom: 0.3f);
        }

        /// <summary>Incandescent fragments and molten metal: streaks falling under gravity, bouncing off the ground and hulls.</summary>
        static void Sparks(Transform root, int count, bool liner)
        {
            var ps = Emitter(root, "Sparks", FxAssets.Sparks, false, 160);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, liner ? 1.2f : 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(20f, 65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.55f, 0.15f));
            main.gravityModifier = 1f;
            Burst(ps, count);
            Shape(ps, 0.2f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f;
            r.lengthScale = 1.5f;
            Collide(ps, 0.3f);
            FadeOut(ps, new Color(1f, 0.85f, 0.5f), new Color(1f, 0.3f, 0.05f));
        }

        /// <summary>Burning propellant grains and hot debris: long-lived glowing embers thrown out, landing and glowing on the ground.</summary>
        static void Embers(Transform root, int count)
        {
            var ps = Emitter(root, "Embers", FxAssets.Embers, false, 120);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.4f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = 0.8f;
            Burst(ps, count);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.8f;
            Collide(ps, 0.15f);
            FadeOut(ps, new Color(1f, 0.85f, 0.5f), new Color(0.9f, 0.25f, 0.05f));
        }

        /// <summary>Ground burst: clods thrown in a cone, a brown column, and a dust skirt rolling out along the ground.</summary>
        static void Dirt(Transform root)
        {
            var clods = Emitter(root, "Clods", FxAssets.Clods, false, 90);
            var main = clods.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 17f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.15f, 0.1f), new Color(0.32f, 0.26f, 0.18f));
            main.gravityModifier = 1f;
            Burst(clods, 70);
            var shape = clods.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var sheet = clods.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = sheet.numTilesY = 2;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            var rot = clods.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            Collide(clods, 0.1f);

            // The thrown-up earth: brown, heavy, falling back as it spreads.
            var column = Smoke(root, "DirtColumn", FxAssets.Dust, new Color(0.4f, 0.32f, 0.23f), 2.6f, 9, 4.5f, alpha: 1f);
            var cm = column.main;
            cm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            cm.gravityModifier = 0.55f;
            var cs = column.shape;
            cs.shapeType = ParticleSystemShapeType.Cone;
            cs.angle = 16f;
            cs.radius = 0.4f;
            cs.rotation = new Vector3(-90f, 0f, 0f);

            var skirt = Smoke(root, "DustSkirt", FxAssets.Dust, new Color(0.55f, 0.48f, 0.38f), 2.4f, 16, 6f, alpha: 0.8f);
            var sm = skirt.main;
            sm.startSpeed = new ParticleSystem.MinMaxCurve(7f, 12f);
            sm.gravityModifier = 0f;
            var ss = skirt.shape;
            ss.shapeType = ParticleSystemShapeType.Circle;
            ss.radius = 0.6f;
            ss.rotation = new Vector3(-90f, 0f, 0f); // flat on the ground, particles fly outward
            Drag(skirt, 0.08f, 0.4f);
            Grow(skirt, 0.7f, 3f);
        }

        /// <summary>Six-way lit smoke: rolls out, grows, rises a little and drifts with the wind (set per spawn).</summary>
        static ParticleSystem Smoke(Transform root, string name, Material mat, Color color, float size, int count, float life,
            float delay = 0f, float alpha = 1f, bool looping = false)
        {
            var ps = Emitter(root, name, mat, looping, 120);
            SixWay(ps, Flipbook.Smoke);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.75f / SmokeFill, size / SmokeFill);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f); // the puff rises up the tile: small roll only
            main.startColor = new Color(color.r, color.g, color.b, alpha);
            main.gravityModifier = -0.02f;
            if (count > 0)
            {
                main.duration = delay + 0.3f;
                ps.emission.SetBursts(new[] { new ParticleSystem.Burst(delay, (short)count) });
            }
            Shape(ps, 0.6f);
            Grow(ps, 0.7f, 3.2f);
            // Drag slows the burst out of the cloud; the buoyant rise and the wind (velocity module) stay above it.
            Drag(ps, looping ? 4.5f : 3f, 0.12f);
            Alpha(ps, 1f, 0f, fadeFrom: 0.35f, fadeIn: 0.04f);
            return ps;
        }

        static ParticleSystem Haze(Transform root, float size)
        {
            var ps = Emitter(root, "Haze", FxAssets.Haze, true, 12);
            var main = ps.main;
            main.startLifetime = 1.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size);
            main.startColor = new Color(1f, 1f, 1f, 0.8f);
            var em = ps.emission;
            em.rateOverTime = 5f;
            Shape(ps, 0.4f);
            Alpha(ps, 1f, 0f, fadeFrom: 0.5f, fadeIn: 0.2f);
            return ps;
        }

        // ---------------------------------------------------------------- environment

        /// <summary>Blackened ground under a burst (the hit-mark atlas' scorch cell, projected straight down).</summary>
        static void Scorch(Vector3 p, float size, ExplosionKind kind)
        {
            if (!GroundBelow(p, 3f * size, out var hit)) return;
            float d = (kind == ExplosionKind.Ammo ? 9f : kind == ExplosionKind.Fuel ? 6f : 3.2f) * Mathf.Sqrt(Mathf.Max(0.3f, size));
            GroundMarks.Place(hit.point, hit.normal, d);
        }

        /// <summary>Strips leaves off crowns within reach of the blast (the trees themselves sway in their shader).</summary>
        static void ShakeTrees(Vector3 p, float size)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return;
            float reach = 18f * Mathf.Pow(Mathf.Max(0.1f, size), 1f / 3f);
            int shown = 0;
            foreach (var tree in TreesNear(terrain, p, reach))
            {
                if (shown++ >= 3) break;
                float dist = Vector3.Distance(tree, p);
                var fx = FxPool.Spawn("leaves", tree, Quaternion.identity);
                fx.Lifetime = 9f;
                var ps = fx.GetComponentInChildren<ParticleSystem>();
                var main = ps.main;
                main.startDelay = Shockwaves.Arrival(dist);
                var em = ps.emission;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Lerp(16f, 4f, dist / reach)) });
                // Thrown away from the burst.
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                Vector3 away = (tree - p).normalized * 3f + Wind.Current * 0.5f;
                vel.x = new ParticleSystem.MinMaxCurve(away.x * 0.5f, away.x);
                vel.y = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
                vel.z = new ParticleSystem.MinMaxCurve(away.z * 0.5f, away.z);
            }
        }

        static readonly System.Collections.Generic.List<Vector3> _treeHits = new System.Collections.Generic.List<Vector3>();

        /// <summary>Crown centres of terrain trees within <paramref name="reach"/>, nearest first.</summary>
        static System.Collections.Generic.List<Vector3> TreesNear(Terrain terrain, Vector3 p, float reach)
        {
            _treeHits.Clear();
            var data = terrain.terrainData;
            Vector3 origin = terrain.transform.position, size = data.size;
            foreach (var inst in data.treeInstances)
            {
                Vector3 w = origin + Vector3.Scale(inst.position, size);
                if ((w - p).sqrMagnitude > reach * reach) continue;
                _treeHits.Add(w + Vector3.up * Mathf.Max(3f, 9f * inst.heightScale));
            }
            _treeHits.Sort((a, b) => (a - p).sqrMagnitude.CompareTo((b - p).sqrMagnitude));
            return _treeHits;
        }

        /// <summary>Gives the smoke of a fresh effect the current wind (per system: low smoke drifts slower, log profile).</summary>
        static void SetWind(FxEffect fx, float factor = 1f)
        {
            Vector3 w = Wind.Current * factor;
            float h = Mathf.Max(2f, fx.transform.position.y - (Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(fx.transform.position) : 0f) + 4f);
            w *= Wind.HeightFactor(h);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps.name.Contains("Smoke") && !ps.name.Contains("Dust") && ps.name != "DirtColumn") continue;
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                // Smoke picks up the wind over its first seconds (curve), and moves faster higher up.
                var ramp = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(0.25f, 0.8f), new Keyframe(1f, 1.3f));
                vel.x = new ParticleSystem.MinMaxCurve(w.x, ramp);
                // Buoyant rise: a fire's plume climbs a few m/s (hot gas), a blast's cloud less, dust hardly
                // (look values, abstract), slowing as the smoke cools and mixes.
                string root = fx.name;
                float rise = ps.name.Contains("Dust") || ps.name == "DirtColumn" ? 0.2f
                    : root.Contains("Fire") || root.Contains("CookOff") || root.Contains("Seep") ? 2.6f
                    : root.Contains("Explosion") ? 1.3f : 0.5f;
                vel.y = new ParticleSystem.MinMaxCurve(rise, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.45f)));
                vel.z = new ParticleSystem.MinMaxCurve(w.z, ramp);
            }
        }

        /// <summary>Re-applies the wind to a long-burning effect (its drift follows gusts and veering).</summary>
        public static void UpdateWind(FxEffect fx) => SetWind(fx);

        // ---------------------------------------------------------------- building blocks

        enum Flipbook { Explosion, Smoke, Flame }

        /// <summary>
        /// Share of the quad the simulated gas covers (the domain is larger than the cloud so nothing is cut off):
        /// sizes in this file are visible metres, divided by this for the quad.
        /// </summary>
        const float ExplosionFill = 0.33f, SmokeFill = 0.42f, FlameFill = 0.7f;

        static readonly System.Collections.Generic.List<ParticleSystemVertexStream> SixWayStreams = new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2, ParticleSystemVertexStream.Center,
            ParticleSystemVertexStream.AnimBlend, ParticleSystemVertexStream.Custom1XY, ParticleSystemVertexStream.Tangent
        };

        /// <summary>Six-way flipbook playback: blended frames, heat (emission) and soot over the particle's life.</summary>
        static void SixWay(ParticleSystem ps, Flipbook book, (float t, float v)[] heat = null, (float t, float v)[] soot = null)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.SetActiveVertexStreams(SixWayStreams);
            r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.maxParticleSize = 4f;
            // The gas starts below the tile centre and rises through the frames: pivot the quad on where it starts
            // (opacity centroid of the first frame, Art/VFX/Flipbooks/<book>.json "pivot", tile units, +y up). The
            // renderer offsets the quad by -pivot, so the centroid's offset goes in negated.
            r.pivot = -(book switch
            {
                Flipbook.Explosion => new Vector3(0.031f, -0.333f, 0f),
                Flipbook.Smoke => new Vector3(0.002f, -0.29f, 0f),
                _ => new Vector3(-0.022f, -0.186f, 0f),
            });
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = sheet.numTilesY = 8;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            switch (book)
            {
                case Flipbook.Explosion:
                    sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                    break;
                case Flipbook.Smoke:
                    // The puff's simulation, eased: it billows quickly, then slowly spreads.
                    sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.08f, 0f, 2f), new Keyframe(1f, 0.99f, 0.3f, 0f)));
                    break;
                default:
                    // Steady flames: each particle plays a random stretch of the fire's flicker.
                    sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 63f);
                    sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.22f));
                    sheet.cycleCount = 1;
                    break;
            }
            var custom = ps.customData;
            custom.enabled = true;
            custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 2);
            custom.SetVector(ParticleSystemCustomData.Custom1, 0, Curve(heat ?? new[] { (0f, 0f), (1f, 0f) }));
            custom.SetVector(ParticleSystemCustomData.Custom1, 1, Curve(soot ?? new[] { (0f, 0f), (1f, 0f) }));
        }

        static ParticleSystem.MinMaxCurve Curve((float t, float v)[] keys)
        {
            var c = new AnimationCurve();
            float max = 0f;
            foreach (var k in keys) max = Mathf.Max(max, Mathf.Abs(k.v));
            if (max <= 0f) return new ParticleSystem.MinMaxCurve(0f);
            foreach (var k in keys) c.AddKey(k.t, k.v / max);
            return new ParticleSystem.MinMaxCurve(max, c);
        }

        static ParticleSystem Emitter(Transform parent, string name, Material material, bool looping, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = looping;
            main.duration = looping ? 1f : 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = maxParticles;
            main.playOnAwake = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static FxLight Light(Transform root, string name, FxLight.Mode mode, float intensity, float range, float duration, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<Light>();
            var l = go.AddComponent<FxLight>();
            l.Configure(mode, intensity, range, duration, color);
            return l;
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
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, from, 0f, (to - from) * 2.5f), new Keyframe(1f, to, (to - from) * 0.3f, 0f)));
        }

        /// <summary>Air drag: puffs thrown out fast slow down to drift.</summary>
        static void Drag(ParticleSystem ps, float limit, float dampen = 0.3f)
        {
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.limit = limit;
            lim.dampen = dampen;
        }

        static void Collide(ParticleSystem ps, float bounce)
        {
            var coll = ps.collision;
            coll.enabled = true;
            coll.type = ParticleSystemCollisionType.World;
            coll.quality = ParticleSystemCollisionQuality.Medium;
            coll.collidesWith = GameLayers.TerrainMask | GameLayers.VehicleHullMask | GameLayers.ArmorMask;
            coll.bounce = bounce;
            coll.dampen = 0.6f;
            coll.lifetimeLoss = 0.1f;
            coll.radiusScale = 0.5f;
        }

        static void Alpha(ParticleSystem ps, float start, float end, float fadeFrom = 0.3f, float fadeIn = 0f)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                fadeIn > 0f
                    ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(start, fadeIn), new GradientAlphaKey(start, fadeFrom), new GradientAlphaKey(end, 1f) }
                    : new[] { new GradientAlphaKey(start, 0f), new GradientAlphaKey(start, fadeFrom), new GradientAlphaKey(end, 1f) });
            col.color = g;
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
    }

    /// <summary>Stops an effect's emission after a delay in game time (a fire that burns out), then pools it.</summary>
    public sealed class FxStopAfter : MonoBehaviour
    {
        float _left = -1f;

        public void Arm(float seconds) => _left = seconds;

        void Update()
        {
            if (_left < 0f || Time.deltaTime <= 0f) return;
            _left -= Time.deltaTime;
            if (_left >= 0f) return;
            _left = -1f;
            var fx = GetComponent<FxEffect>();
            if (fx != null) fx.StopEmitting();
        }
    }
}
