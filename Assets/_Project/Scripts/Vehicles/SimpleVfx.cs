using System.Collections.Generic;
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
    /// Plays effect recipes (<see cref="FxRecipe"/> assets: layers of flash, fireball, blast ring, sparks, embers, clods,
    /// smoke, flames, jets, haze and lights, plus what the effect does to the world and its sound) as pooled particle
    /// effects. Smoke and fire are lit six-way flipbooks baked from Mantaflow gas simulations
    /// (Tools/Blender/vfx_bake.py; shader ModernDrone/Particles/SixWay): the sun, the sky and every nearby light shade
    /// them, the ground and hulls cut them softly. Effects age in game time, so a world frozen by the killcam holds them at
    /// their first instant. The standard recipes (<see cref="FxLibrary"/>) reproduce the phases seen on strike footage and
    /// in fireball studies: a flash brightest in the first milliseconds with the visible fireball gone in ~100 ms for small
    /// charges (arxiv.org 2010.13537, Beirut study), a refraction ring as the blast front leaves
    /// (en.wikipedia.org/wiki/Blast_wave), sparks of liner metal and fragments, dirt and a brown dust skirt for ground
    /// bursts, then a column of soot that rises and drifts with the wind (TNT-type charges are oxygen-poor and smoke
    /// black: en.wikipedia.org/wiki/Oxygen_balance). Ammunition detonations fill the view with fire for 1.5-2 s
    /// (T-80BVM / T-72 catastrophic-kill footage), burning propellant jets out of every opening for seconds with
    /// white-grey smoke, fuel fires burn with black smoke (en.wikipedia.org/wiki/Cooking_off). Sizes and counts in the
    /// recipes are look values (abstract); events scale them from published relations (<see cref="FireScaling"/>).
    /// </summary>
    public static class SimpleVfx
    {
        // ---------------------------------------------------------------- recipe player

        /// <summary>
        /// Plays <paramref name="recipe"/> at the pose (up = the direction it faces: a jet's axis, an opening's normal),
        /// optionally parented. <paramref name="scale"/> resizes it (1 = as authored), <paramref name="rateScale"/> thins or
        /// thickens continuous emission, <paramref name="seconds"/> &gt; 0 sets how long its continuous layers emit.
        /// </summary>
        public static FxEffect Play(FxRecipe recipe, Vector3 position, Quaternion rotation, Transform parent = null, float scale = 1f,
            float rateScale = 1f, float seconds = 0f)
        {
            if (recipe == null) return null;
            string key = Register(recipe);
            var fx = FxPool.Spawn(key, position, rotation, parent);
            fx.SetScale(scale, rateScale);
            fx.Lifetime = recipe.looping ? 0f : recipe.lifetime + Mathf.Max(0f, seconds);
            if (_delayed.Remove(fx)) Delay(fx, 0f);
            if (seconds > 0f) SetDurations(fx, recipe, seconds);
            ApplyQuality(fx, recipe);
            SetWind(fx);
            bool sustained = recipe.looping || seconds > 0f;
            var bank = recipe.sound;
            if (bank != null)
            {
                if (sustained) fx.OnStart(f => SimpleAudio.Sustain(bank, f.transform, seconds, scale));
                else SimpleAudio.Blast(bank, position, scale);
            }
            if (!sustained) fx.OnStart(f => World(recipe, position, scale));
            return fx;
        }

        /// <summary>What a one-shot does to the world when it goes off: blast wave, obscuring cloud, flash, scorch, debris, leaves.</summary>
        static void World(FxRecipe r, Vector3 p, float scale)
        {
            if (r.shockwave) Shockwaves.Emit(p, scale);
            // Its smoke and dust hide what is behind it for a while, by kind (SmokeVolumes.Cloud).
            if (r.obscures == FxRecipe.Obscures.Screen)
            {
                // Screening grenades build a wall of ~10 m clouds within 1-2 s along the wind.
                for (int i = 0; i < 4; i++)
                    SmokeVolumes.Add(SmokeVolumes.Kind.Screen, p + Wind.Current * (2f + i * 3f) + Vector3.up * 1.5f, scale);
            }
            else if (r.obscures != FxRecipe.Obscures.None)
                SmokeVolumes.Add(r.ObscurationKind, p + Vector3.up * scale, scale);
            // The flash whites out thermal sights looking at it.
            if (r.dazzles) SmokeVolumes.AddFlash(p, scale);
            if (r.scorch > 0f && GroundBelow(p, 1.5f * scale, out var hit))
                GroundMarks.Place(hit.point, hit.normal, r.scorch * Mathf.Sqrt(Mathf.Max(0.3f, scale)));
            if (r.debris > 0 && scale >= 0.8f) FxDebris.Throw(p, r.debris, r.debrisSpeed, r.debrisSize);
            if (r.shakesTrees) ShakeTrees(p, scale);
        }

        // ---------------------------------------------------------------- standard effects

        public static void Explosion(Vector3 position, float size, ExplosionKind kind = ExplosionKind.Auto)
        {
            if (kind == ExplosionKind.Auto) kind = Classify(position);
            var lib = FxLibrary.Instance;
            if (lib != null) Play(lib.Explosion(kind), position, Quaternion.identity, null, size);
        }

        /// <summary>Ground, vehicle or air, from what is right around the burst.</summary>
        public static ExplosionKind Classify(Vector3 p)
        {
            if (Physics.CheckSphere(p, 0.9f, GameLayers.VehicleHullMask | GameLayers.ArmorMask | GameLayers.ERAMask | GameLayers.ScreenMask, QueryTriggerInteraction.Collide))
                return ExplosionKind.Armor;
            return GroundBelow(p, 1.8f, out _) ? ExplosionKind.Ground : ExplosionKind.Air;
        }

        static bool GroundBelow(Vector3 p, float within, out RaycastHit hit) =>
            Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out hit, within + 0.3f, GameLayers.TerrainMask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// A flame jet (<paramref name="recipe"/>, default: burning propellant) roaring along <paramref name="direction"/>
        /// for <paramref name="seconds"/>, flaring and dying back as charges burn through.
        /// </summary>
        public static FxEffect CookOff(Transform parent, Vector3 worldPosition, float seconds, Vector3? direction = null, float strength = 1f,
            FxRecipe recipe = null)
        {
            if (recipe == null) recipe = FxLibrary.Instance != null ? FxLibrary.Instance.jet : null;
            Vector3 dir = direction ?? Vector3.up;
            return Play(recipe, worldPosition, Quaternion.FromToRotation(Vector3.up, dir), parent, Mathf.Clamp(strength, 0.4f, 2f), 1f,
                Mathf.Max(0.5f, seconds));
        }

        /// <summary>
        /// Looping flames with a smoke column (<paramref name="recipe"/>, default: black or grey), parented so it follows the
        /// vehicle. <paramref name="direction"/> is the opening the fire burns out of (flames lean out of it, then rise).
        /// </summary>
        public static FxEffect Fire(Transform parent, Vector3 worldPosition, float size, Vector3? direction = null, bool blackSmoke = true,
            FxRecipe recipe = null)
        {
            var lib = FxLibrary.Instance;
            if (recipe == null && lib != null) recipe = blackSmoke ? lib.fire : lib.fireGrey;
            Vector3 dir = direction ?? Vector3.up;
            return Play(recipe, worldPosition, Quaternion.FromToRotation(Vector3.up, dir), parent, size, Mathf.Clamp(size, 0.6f, 1.6f));
        }

        /// <summary>
        /// Smoke seeping out of a closed hull (seams, vision blocks) while a fire burns inside, or the smouldering column
        /// of a burnt-out wreck: no flames.
        /// </summary>
        public static FxEffect Seep(Transform parent, Vector3 worldPosition, bool blackSmoke, float size = 1f, FxRecipe recipe = null)
        {
            var lib = FxLibrary.Instance;
            if (recipe == null && lib != null) recipe = blackSmoke ? lib.seep : lib.seepGrey;
            return Play(recipe, worldPosition, Quaternion.identity, parent, size, Mathf.Clamp(size, 0.6f, 1.6f));
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
                if (fire == null) return;
                // Burning debris lands a moment after the blast, then catches.
                Delay(fire, Random.Range(1f, 3.5f));
                StopAfter(fire, Random.Range(25f, 50f));
            }
        }

        /// <summary>Dense grey smoke cloud laid by a vehicle's smoke grenades; drifts and thins out over about a minute.</summary>
        public static void SmokeScreen(Vector3 position)
        {
            var lib = FxLibrary.Instance;
            var fx = Play(lib != null ? lib.smokeScreen : null, position + Vector3.up * 1.5f, Quaternion.identity);
            if (fx != null) SetWind(fx, 0.6f);
        }

        /// <summary>Stops an effect's emission after <paramref name="after"/> s of game time (a fire that burns out).</summary>
        public static void StopAfter(FxEffect fx, float after)
        {
            if (fx == null) return;
            if (!fx.TryGetComponent(out FxStopAfter stopper)) stopper = fx.gameObject.AddComponent<FxStopAfter>();
            stopper.Arm(after);
        }

        static readonly HashSet<FxEffect> _delayed = new HashSet<FxEffect>();

        /// <summary>Holds the whole effect back <paramref name="seconds"/> (on top of each layer's own delay).</summary>
        public static void Delay(FxEffect fx, float seconds)
        {
            if (fx == null) return;
            if (seconds > 0f) _delayed.Add(fx);
            // The delay only applies from the next Play: restart the (just spawned) systems.
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var layer = LayerOf(fx, ps);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.startDelay = seconds + (layer != null && layer.count == 0 ? layer.delay : 0f);
                ps.Play(false);
            }
            foreach (var l in fx.Lights)
            {
                var layer = LayerOf(fx, l);
                l.SetDelay(seconds + (layer != null ? layer.delay : 0f));
            }
        }

        /// <summary>Continuous layers emit for <paramref name="seconds"/> (the longer ones keep their margin over the shortest).</summary>
        static void SetDurations(FxEffect fx, FxRecipe recipe, float seconds)
        {
            float shortest = float.MaxValue;
            foreach (var l in recipe.layers) if (l != null && l.duration > 0f) shortest = Mathf.Min(shortest, l.duration);
            if (shortest == float.MaxValue) return;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var layer = LayerOf(fx, ps);
                if (layer == null || layer.duration <= 0f) continue;
                // Duration can only change on a stopped system: stop, set, restart.
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.duration = Mathf.Max(0.5f, layer.duration - shortest + seconds);
                ps.Play(false);
            }
        }

        static void ApplyQuality(FxEffect fx, FxRecipe recipe)
        {
            int q = FxRecipe.Quality;
            bool any = false;
            foreach (var l in recipe.layers) if (l != null && l.minQuality > q) { any = true; break; }
            if (!any) return;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var layer = LayerOf(fx, ps);
                if (layer != null && layer.minQuality > q) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (var l in fx.Lights)
            {
                var layer = LayerOf(fx, l);
                if (layer != null && layer.minQuality > q) l.Level = 0f;
            }
        }

        // ---------------------------------------------------------------- templates

        static readonly Dictionary<FxRecipe, string> _keys = new Dictionary<FxRecipe, string>();
        static readonly Dictionary<string, FxRecipe> _recipes = new Dictionary<string, FxRecipe>();
        static int _nextKey;

        /// <summary>Pool key of a recipe (registered with the pool on first use; the template is built from its layers).</summary>
        static string Register(FxRecipe recipe)
        {
            if (!_keys.TryGetValue(recipe, out var key))
            {
                key = "fx" + (++_nextKey) + "_" + recipe.name;
                _keys[recipe] = key;
                _recipes[key] = recipe;
            }
            // The pool forgets its kinds on entering play mode (statics survive without a domain reload).
            if (!FxPool.Has(key))
            {
                FxClock.Ensure();
                FxPool.Register(key, () => Build(recipe), recipe.poolSize);
            }
            return key;
        }

        /// <summary>The recipe layer a child of a pooled recipe effect was built from (null for other effects).</summary>
        static FxLayer LayerOf(FxEffect fx, Component part)
        {
            if (fx.Key == null || !_recipes.TryGetValue(fx.Key, out var recipe)) return null;
            string n = part.name;
            int cut = n.LastIndexOf('_');
            if (cut < 0 || !int.TryParse(n.Substring(cut + 1), out int i) || i < 0 || i >= recipe.layers.Length) return null;
            return recipe.layers[i];
        }

        /// <summary>Builds the particle systems and lights of a recipe, one child per layer, named "Kind_index".</summary>
        public static GameObject Build(FxRecipe recipe)
        {
            var root = new GameObject("VFX_" + recipe.name);
            var t = root.transform;
            for (int i = 0; i < recipe.layers.Length; i++)
            {
                var l = recipe.layers[i];
                if (l == null) continue;
                string name = l.kind + "_" + i;
                Component c = l.kind switch
                {
                    FxLayerKind.Flash => Flash(t, name, l),
                    FxLayerKind.Fireball => Fireball(t, name, l),
                    FxLayerKind.ShockRing => ShockRing(t, name, l),
                    FxLayerKind.Sparks => Sparks(t, name, l),
                    FxLayerKind.Embers => Embers(t, name, l, recipe.looping),
                    FxLayerKind.Clods => Clods(t, name, l),
                    FxLayerKind.Smoke => Smoke(t, name, l, recipe.looping),
                    FxLayerKind.Flames => Flames(t, name, l, recipe.looping),
                    FxLayerKind.Jet => Jet(t, name, l, recipe.looping),
                    FxLayerKind.Haze => Haze(t, name, l),
                    _ => Light(t, name, (FxLight.Mode)(int)l.lightMode, l.intensity, l.size, l.lifetime, l.color, l.delay),
                };
                if (c != null && l.offset != 0f) c.transform.localPosition = Vector3.up * l.offset;
            }
            return root;
        }

        static Material Mat(FxLayer l, Material fallback) => l.material switch
        {
            FxMaterial.Fireball => FxAssets.Fireball,
            FxMaterial.Smoke => FxAssets.Smoke,
            FxMaterial.SmokeHot => FxAssets.SmokeHot,
            FxMaterial.Dust => FxAssets.Dust,
            FxMaterial.Flame => FxAssets.Flame,
            FxMaterial.Flash => FxAssets.Flash,
            FxMaterial.Sparks => FxAssets.Sparks,
            FxMaterial.Embers => FxAssets.Embers,
            FxMaterial.Clods => FxAssets.Clods,
            _ => fallback,
        };

        static float SpeedMin(FxLayer l, float share) => l.speedMin > 0f ? l.speedMin : l.speed * share;

        /// <summary>Continuous layers loop in looping recipes unless they have a set duration.</summary>
        static bool Loops(FxLayer l, bool looping) => looping && l.rate > 0f && l.duration <= 0f;

        static void Continuous(ParticleSystem ps, FxLayer l)
        {
            if (l.rate <= 0f) return;
            var em = ps.emission;
            em.rateOverTime = l.rate;
            var main = ps.main;
            if (l.duration > 0f) main.duration = l.duration;
            if (l.count == 0 && l.delay > 0f) main.startDelay = l.delay;
        }

        // ---------------------------------------------------------------- layers

        /// <summary>White-hot burst sprite.</summary>
        static ParticleSystem Flash(Transform root, string name, FxLayer l)
        {
            var ps = Emitter(root, name, Mat(l, FxAssets.Flash), false, 4);
            var main = ps.main;
            main.startLifetime = l.lifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.8f, l.size);
            main.startColor = l.color;
            Burst(ps, Mathf.Max(1, l.count), l.delay);
            FadeOut(ps, new Color(1f, 0.97f, 0.9f), new Color(1f, 0.6f, 0.25f));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortingFudge = -40f; // drawn over the fireball
            return ps;
        }

        /// <summary>Lit, glowing gas cooling into soot (simulated flipbook): each frame is the whole fireball, so a few copies.</summary>
        static ParticleSystem Fireball(Transform root, string name, FxLayer l)
        {
            var ps = Emitter(root, name, Mat(l, FxAssets.Fireball), false, 40);
            float k = l.intensity, s = l.soot;
            SixWay(ps, Flipbook.Explosion, heat: new[] { (0f, 1.25f * k), (0.08f, 1.1f * k), (0.25f, 0.7f * k), (0.45f, 0.15f * k), (0.6f, 0f) },
                soot: new[] { (0f, 0f), (0.3f, 0.5f * s), (1f, s) });
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(l.lifetime * 0.75f, l.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin(l, 0.24f), l.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.75f / ExplosionFill, l.size / ExplosionFill);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f); // the gas rises up the tile: small roll only
            main.startColor = l.color;
            main.gravityModifier = l.gravity;
            Burst(ps, l.count, l.delay);
            Shape(ps, l.radius);
            Grow(ps, 0.85f, 1.6f);
            Drag(ps, 3f);
            Alpha(ps, 1f, 0f, fadeFrom: 0.7f);
            return ps;
        }

        /// <summary>Ring of bent light racing out with the blast front (size = radius it reaches).</summary>
        static ParticleSystem ShockRing(Transform root, string name, FxLayer l)
        {
            var ps = Emitter(root, name, FxAssets.ShockRing, false, 2);
            var main = ps.main;
            main.startLifetime = l.lifetime;
            main.startSpeed = 0f;
            main.startSize = l.size * 2f;
            main.startColor = l.color;
            Burst(ps, Mathf.Max(1, l.count), l.delay);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            // The front slows as it weakens: fast at first.
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.05f, 0f, 3f), new Keyframe(1f, 1f, 0.5f, 0f)));
            Alpha(ps, 1f, 0f, fadeFrom: 0.3f);
            return ps;
        }

        /// <summary>Incandescent fragments and molten metal: streaks falling under gravity, bouncing off the ground and hulls.</summary>
        static ParticleSystem Sparks(Transform root, string name, FxLayer l)
        {
            var ps = Emitter(root, name, Mat(l, FxAssets.Sparks), false, 160);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Min(0.2f, l.lifetime), l.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin(l, 0.3f), l.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.4f, l.size);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.55f, 0.15f));
            main.gravityModifier = l.gravity;
            Burst(ps, l.count, l.delay);
            Shape(ps, l.radius);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f;
            r.lengthScale = 1.5f;
            if (l.collide) Collide(ps, 0.3f);
            FadeOut(ps, new Color(1f, 0.85f, 0.5f), new Color(1f, 0.3f, 0.05f));
            return ps;
        }

        /// <summary>Glowing grains: burning propellant and hot debris thrown out (burst), or rising from a fire (continuous).</summary>
        static ParticleSystem Embers(Transform root, string name, FxLayer l, bool looping)
        {
            var ps = Emitter(root, name, Mat(l, FxAssets.Embers), Loops(l, looping), 120);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(l.lifetime * 0.4f, l.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin(l, 0.33f), l.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.4f, l.size);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.4f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = l.gravity;
            if (l.count > 0) Burst(ps, l.count, l.delay);
            Continuous(ps, l);
            Spread(ps, l);
            if (l.rate > 0f)
            {
                // Carried about by the fire's turbulence.
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 1.2f;
                noise.frequency = 0.6f;
            }
            if (l.collide) Collide(ps, 0.15f);
            FadeOut(ps, new Color(1f, 0.85f, 0.5f), new Color(0.9f, 0.25f, 0.05f));
            return ps;
        }

        /// <summary>Earth clods thrown in a cone by a ground burst, tumbling and bouncing.</summary>
        static ParticleSystem Clods(Transform root, string name, FxLayer l)
        {
            var ps = Emitter(root, name, Mat(l, FxAssets.Clods), false, 90);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(l.lifetime * 0.5f, l.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin(l, 0.35f), l.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.27f, l.size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.15f, 0.1f), new Color(0.32f, 0.26f, 0.18f));
            main.gravityModifier = l.gravity;
            Burst(ps, l.count, l.delay);
            Spread(ps, l);
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = sheet.numTilesY = 2;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            if (l.collide) Collide(ps, 0.1f);
            return ps;
        }

        /// <summary>Six-way lit smoke or dust: rolls out, grows, rises and drifts with the wind (set per spawn).</summary>
        static ParticleSystem Smoke(Transform root, string name, FxLayer l, bool looping)
        {
            bool loops = Loops(l, looping);
            var ps = Emitter(root, name, Mat(l, FxAssets.SmokeHot), loops, 120);
            SixWay(ps, Flipbook.Smoke);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(l.lifetime * 0.7f, l.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin(l, 0.25f), l.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.75f / SmokeFill, l.size / SmokeFill);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f); // the puff rises up the tile: small roll only
            main.startColor = l.color;
            main.gravityModifier = l.gravity;
            if (l.count > 0) Burst(ps, l.count, l.delay);
            Continuous(ps, l);
            if (l.groundRing)
            {
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = l.radius;
                shape.rotation = new Vector3(-90f, 0f, 0f); // flat on the ground, puffs roll outward
                Drag(ps, 0.08f, 0.4f);
                Grow(ps, 0.7f, 3f);
            }
            else
            {
                Spread(ps, l);
                Grow(ps, 0.7f, 3.2f);
                // Drag slows the burst out of the cloud; the buoyant rise and the wind (velocity module) stay above it.
                Drag(ps, loops ? 4.5f : 3f, 0.12f);
            }
            Alpha(ps, 1f, 0f, fadeFrom: 0.35f, fadeIn: 0.04f);
            return ps;
        }

        /// <summary>Flame tongues leaning out of the effect's up axis, then rising (hot gas is buoyant whatever the opening's direction).</summary>
        static ParticleSystem Flames(Transform root, string name, FxLayer l, bool looping)
        {
            var ps = Emitter(root, name, Mat(l, FxAssets.Flame), Loops(l, looping), 60);
            float k = l.intensity;
            SixWay(ps, Flipbook.Flame, heat: new[] { (0f, 1.1f * k), (0.5f, 1f * k), (1f, 0.5f * k) });
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(l.lifetime * 0.55f, l.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin(l, 0.4f), l.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.58f / FlameFill, l.size / FlameFill);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            main.startColor = l.color;
            if (l.count > 0) Burst(ps, l.count, l.delay);
            Continuous(ps, l);
            Spread(ps, l);
            Grow(ps, 0.8f, 1.1f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(l.rise * 0.46f, l.rise);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            return ps;
        }

        // Uneven venting: the jet flares and dies back as charges burn through.
        static readonly AnimationCurve Flare = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.45f), new Keyframe(0.3f, 1f),
            new Keyframe(0.5f, 0.35f), new Keyframe(0.65f, 0.9f), new Keyframe(0.85f, 0.4f), new Keyframe(1f, 0.1f));

        /// <summary>Roaring flame jet along the effect's up axis.</summary>
        static ParticleSystem Jet(Transform root, string name, FxLayer l, bool looping)
        {
            var ps = Emitter(root, name, Mat(l, FxAssets.Flame), Loops(l, looping), 160);
            float k = l.intensity;
            SixWay(ps, Flipbook.Flame, heat: new[] { (0f, 1.3f * k), (0.6f, 1f * k), (1f, 0.4f * k) });
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(l.lifetime * 0.5f, l.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin(l, 0.55f), l.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.55f / FlameFill, l.size / FlameFill);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            main.startColor = l.color;
            if (l.count > 0) Burst(ps, l.count, l.delay);
            Continuous(ps, l);
            var em = ps.emission;
            if (l.duration > 0f) em.rateOverTime = new ParticleSystem.MinMaxCurve(l.rate, Flare);
            Spread(ps, l);
            Grow(ps, 0.8f, 2.4f);
            Drag(ps, 2.5f);
            return ps;
        }

        /// <summary>Heat shimmer over a fire.</summary>
        static ParticleSystem Haze(Transform root, string name, FxLayer l)
        {
            var ps = Emitter(root, name, FxAssets.Haze, l.duration <= 0f, 12);
            var main = ps.main;
            main.startLifetime = l.lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin(l, 0.5f), l.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(l.size * 0.8f, l.size);
            main.startColor = l.color;
            Continuous(ps, l);
            Shape(ps, l.radius);
            Alpha(ps, 1f, 0f, fadeFrom: 0.5f, fadeIn: 0.2f);
            return ps;
        }

        /// <summary>Emitter shape from the layer: sphere (spread 0), cone along up, or hemisphere (spread 90 and more).</summary>
        static void Spread(ParticleSystem ps, FxLayer l)
        {
            var shape = ps.shape;
            if (l.spread >= 90f)
            {
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = l.radius;
                shape.rotation = new Vector3(-90f, 0f, 0f);
            }
            else if (l.spread > 0f)
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = l.spread;
                shape.radius = l.radius;
                shape.rotation = new Vector3(-90f, 0f, 0f); // cone along the effect's up = the opening's normal
            }
            else Shape(ps, l.radius);
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

        // ---------------------------------------------------------------- environment

        /// <summary>Strips leaves off crowns within reach of the blast (the trees themselves sway in their shader).</summary>
        static void ShakeTrees(Vector3 p, float size)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return;
            if (!FxPool.Has("leaves")) FxPool.Register("leaves", BuildLeaves, 6);
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

        static readonly List<Vector3> _treeHits = new List<Vector3>();
        static TerrainData _treeData;
        static Vector3[] _crowns = new Vector3[0];

        /// <summary>Crown centres of terrain trees within <paramref name="reach"/>, nearest first (positions cached per terrain).</summary>
        static List<Vector3> TreesNear(Terrain terrain, Vector3 p, float reach)
        {
            var data = terrain.terrainData;
            if (_treeData != data || _crowns.Length != data.treeInstanceCount)
            {
                _treeData = data;
                var trees = data.treeInstances;
                Vector3 origin = terrain.transform.position, size = data.size;
                _crowns = new Vector3[trees.Length];
                for (int i = 0; i < trees.Length; i++)
                    _crowns[i] = origin + Vector3.Scale(trees[i].position, size) + Vector3.up * Mathf.Max(3f, 9f * trees[i].heightScale);
            }
            _treeHits.Clear();
            float r2 = reach * reach;
            foreach (var c in _crowns)
                if ((c - p).sqrMagnitude <= r2 + 81f) _treeHits.Add(c);
            _treeHits.Sort((a, b) => (a - p).sqrMagnitude.CompareTo((b - p).sqrMagnitude));
            return _treeHits;
        }

        // Smoke picks up the wind over its first seconds and moves faster higher up; rising slows as it cools and mixes.
        static readonly AnimationCurve WindRamp = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(0.25f, 0.8f), new Keyframe(1f, 1.3f));
        static readonly AnimationCurve RiseRamp = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.45f));
        // Per pooled effect: its drifting systems and their buoyant rise (worked out once, the effect is reused).
        static readonly Dictionary<FxEffect, (ParticleSystem ps, float rise)[]> _drifting = new Dictionary<FxEffect, (ParticleSystem, float)[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCaches()
        {
            _drifting.Clear();
            _delayed.Clear();
            _treeData = null;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene _)
        {
            _drifting.Clear();
            _delayed.Clear();
        }

        static (ParticleSystem ps, float rise)[] Drifting(FxEffect fx)
        {
            if (_drifting.TryGetValue(fx, out var list) && (list.Length == 0 || list[0].ps != null)) return list;
            var found = new List<(ParticleSystem, float)>();
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var layer = LayerOf(fx, ps);
                if (layer != null && layer.kind == FxLayerKind.Smoke && layer.windDrift) found.Add((ps, layer.rise));
            }
            return _drifting[fx] = found.ToArray();
        }

        /// <summary>Gives the smoke of an effect the current wind (low smoke drifts slower: log profile).</summary>
        static void SetWind(FxEffect fx, float factor = 1f)
        {
            Vector3 w = Wind.Current * factor;
            Vector3 p = fx.transform.position;
            float h = Mathf.Max(2f, p.y - (Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(p) : 0f) + 4f);
            w *= Wind.HeightFactor(h);
            foreach (var (ps, rise) in Drifting(fx))
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(w.x, WindRamp);
                vel.y = new ParticleSystem.MinMaxCurve(rise, RiseRamp);
                vel.z = new ParticleSystem.MinMaxCurve(w.z, WindRamp);
            }
        }

        /// <summary>Re-applies the wind to a long-burning effect (its drift follows gusts and veering).</summary>
        public static void UpdateWind(FxEffect fx) => SetWind(fx);

        // ---------------------------------------------------------------- building blocks

        enum Flipbook { Explosion, Smoke, Flame }

        /// <summary>
        /// Share of the quad the simulated gas covers (the domain is larger than the cloud so nothing is cut off):
        /// sizes in recipes are visible metres, divided by this for the quad.
        /// </summary>
        const float ExplosionFill = 0.33f, SmokeFill = 0.42f, FlameFill = 0.7f;

        static readonly List<ParticleSystemVertexStream> SixWayStreams = new List<ParticleSystemVertexStream>
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

        static FxLight Light(Transform root, string name, FxLight.Mode mode, float intensity, float range, float duration, Color color, float delay = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<Light>();
            var l = go.AddComponent<FxLight>();
            l.Configure(mode, intensity, range, duration, color, delay);
            return l;
        }

        static void Burst(ParticleSystem ps, int count, float delay = 0f)
        {
            if (count <= 0) return;
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(delay, (short)count) });
            var main = ps.main;
            if (!main.loop) main.duration = Mathf.Max(main.duration, delay + 0.3f);
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

        // Pooled: the next user of this effect must not inherit the timer.
        void OnDisable() => _left = -1f;

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
