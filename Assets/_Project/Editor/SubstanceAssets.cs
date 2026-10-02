using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Vehicles;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Creates the data assets behind fires and detonations: substances (Data/Substances), effect recipes (Data/Fx), sound
    /// banks (Data/Sounds) and the two libraries in Resources. Existing assets are kept as they are (hand-tuned values
    /// survive a rebuild); delete one to get its default back. The recipes reproduce the look the effects had when they
    /// were code. Fuel physics: Babrauskas pool-burning data (SFPE Handbook, "Burning rates"; gasoline/kerosene/oil rows as
    /// quoted in Rantuch et al. 2020, IJERPH 17:411 and the SFPE table, diesel's row not confirmed); densities are
    /// typical published values. Vehicle-fire heat release is abstract, ordered after Lönnermark and Ingason (SP Sweden):
    /// passenger cars 1.5-8 MW, buses ~30 MW. Explosive equivalents and propellant masses are abstract.
    /// </summary>
    public static class SubstanceAssets
    {
        const string SubDir = "Assets/_Project/Data/Substances";
        const string FxDir = "Assets/_Project/Data/Fx";
        const string SoundDir = "Assets/_Project/Data/Sounds";
        const string LibraryPath = "Assets/_Project/Resources/SubstanceLibrary.asset";
        const string FxLibraryPath = "Assets/_Project/Resources/FxLibrary.asset";

        public static SubstanceLibrary Library => CreateAll();
        public static SubstanceData Diesel => Library.diesel;
        public static SubstanceData Gasoline => Library.gasoline;
        public static SubstanceData JetFuel => Library.jetFuel;
        public static SubstanceData Oil => Library.oil;
        public static SubstanceData Propellant => Library.gunPropellant;
        public static SubstanceData Shells => Library.highExplosiveShells;
        public static SubstanceData Rockets => Library.rocketMotor;
        public static SubstanceData Missiles => Library.canisterMissile;
        public static SubstanceData SmallArms => Library.smallArms;

        static bool _built;

        [MenuItem("Modern Drone/Setup/Build Substance Assets")]
        public static void BuildMenu()
        {
            _built = false;
            CreateAll();
            Debug.Log("[ModernDrone] Substance, effect and sound assets ready.");
        }

        public static SubstanceLibrary CreateAll()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SubstanceLibrary>(LibraryPath);
            if (_built && lib != null && lib.diesel != null) return lib;
            _built = true;
            foreach (var d in new[] { SubDir, FxDir, SoundDir })
                if (!AssetDatabase.IsValidFolder(d)) AssetDatabase.CreateFolder("Assets/_Project/Data", d.Substring(d.LastIndexOf('/') + 1));

            // ---------------------------------------------------------------- sounds
            AudioClip[] Clips(params string[] names)
            {
                var list = new System.Collections.Generic.List<AudioClip>();
                foreach (var n in names)
                {
                    var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_Project/Resources/Audio/{n}.wav");
                    if (c != null) list.Add(c);
                }
                return list.ToArray();
            }
            var close = Clips("exp_close_1", "exp_close_2", "exp_close_3");
            var far = Clips("exp_far_1", "exp_far_2", "exp_far_3", "exp_far_4");
            var smallTakes = Clips("exp_small_1", "exp_small_2", "exp_small_3");
            var pops = Clips("pop_1", "pop_2");
            var blast = Get<SoundBank>(SoundDir, "Blast", b => { b.near = close; b.small = smallTakes; b.far = far; });
            var blastGround = Get<SoundBank>(SoundDir, "Blast_Ground", b => { b.near = close; b.small = smallTakes; b.far = far; b.debris = Clips("debris_dirt"); });
            var blastMetal = Get<SoundBank>(SoundDir, "Blast_Metal", b => { b.near = close; b.small = smallTakes; b.far = far; b.debris = Clips("debris_metal"); });
            var blastEra = Get<SoundBank>(SoundDir, "Blast_Era", b => { b.near = smallTakes; b.small = smallTakes; b.far = far; b.smallSize = 100f; });
            var fireLoop = Get<SoundBank>(SoundDir, "Fire", b => { b.loop = Clips("fire_large", "fire_fuel", "fire_car"); b.minDistance = 5f; });
            var flameJet = Get<SoundBank>(SoundDir, "FlameJet", b =>
            {
                b.start = Clips("flame_burst"); b.loop = Clips("flame_jet"); b.crackle = Clips("cookoff_crackle");
                b.pops = pops; b.bangs = smallTakes; b.minDistance = 10f;
            });
            var rocketMotor = Get<SoundBank>(SoundDir, "RocketMotor", b => { b.start = Clips("flame_burst"); b.loop = Clips("flame_jet"); b.minDistance = 14f; });
            // Loose rounds in a fire pop rather than fire: the case bursts, the bullet barely moves (SAAMI, "Sporting
            // ammunition and the firefighter"); crackle with frequent pops, the odd louder bang.
            var smallArmsSound = Get<SoundBank>(SoundDir, "SmallArmsCookOff", b =>
            {
                b.crackle = Clips("cookoff_crackle"); b.pops = pops; b.bangs = smallTakes; b.popInterval = 0.35f; b.bangChance = 0.04f;
            });

            // ---------------------------------------------------------------- recipes
            var exp = new FxRecipe[7];
            foreach (ExplosionKind k in System.Enum.GetValues(typeof(ExplosionKind)))
            {
                if (k == ExplosionKind.Auto) continue;
                var kind = k;
                exp[(int)k] = Get<FxRecipe>(FxDir, "Exp_" + k, r => Explosion(r, kind,
                    kind == ExplosionKind.Ground ? blastGround : kind == ExplosionKind.Ammo || kind == ExplosionKind.Armor ? blastMetal
                    : kind == ExplosionKind.Era ? blastEra : blast));
            }
            var fireBlack = Get<FxRecipe>(FxDir, "Fire_Black", r => Fire(r, new Color(0.11f, 0.1f, 0.095f, 0.9f), fireLoop));
            var fireGrey = Get<FxRecipe>(FxDir, "Fire_Grey", r => { Fire(r, new Color(0.5f, 0.49f, 0.47f, 0.75f), fireLoop); r.poolSize = 8; });
            // Petrol burns faster and brighter than diesel (Babrauskas m''inf 0.055 vs ~0.045 kg/m2 s), as sooty.
            var fireGasoline = Get<FxRecipe>(FxDir, "Fire_Gasoline", r =>
            {
                Fire(r, new Color(0.12f, 0.11f, 0.1f, 0.9f), fireLoop);
                r.layers[0].size = 2.6f; r.layers[0].rate = 18f; r.layers[0].intensity = 1.15f; r.poolSize = 6;
            });
            var fireKerosene = Get<FxRecipe>(FxDir, "Fire_Kerosene", r => { Fire(r, new Color(0.14f, 0.13f, 0.12f, 0.85f), fireLoop); r.poolSize = 6; });
            // Lubricating oil: hard to light, low flames, very thick black smoke.
            var fireOil = Get<FxRecipe>(FxDir, "Fire_Oil", r =>
            {
                Fire(r, new Color(0.08f, 0.075f, 0.07f, 0.95f), fireLoop);
                r.layers[0].size = 1.8f; r.layers[0].rate = 10f; r.layers[0].intensity = 0.9f; r.layers[2].rate = 7f; r.poolSize = 6;
            });
            // Burning rocket motors: fierce, whitish flame and dense white smoke (cook-off footage of MLRS launchers).
            var fireRocket = Get<FxRecipe>(FxDir, "Fire_Rocket", r =>
            {
                Fire(r, new Color(0.82f, 0.82f, 0.8f, 0.9f), rocketMotor);
                r.layers[0].intensity = 1.3f; r.layers[0].rate = 18f; r.layers[2].size = 3.6f; r.layers[2].rate = 9f; r.poolSize = 6;
            });
            var seepBlack = Get<FxRecipe>(FxDir, "Seep_Black", r => Seep(r, new Color(0.09f, 0.085f, 0.08f, 0.75f), 8));
            var seepGrey = Get<FxRecipe>(FxDir, "Seep_Grey", r => Seep(r, new Color(0.55f, 0.54f, 0.52f, 0.75f), 6));
            var jetPropellant = Get<FxRecipe>(FxDir, "Jet_Propellant", r => PropellantTorch(r, flameJet));
            var jetCanister = Get<FxRecipe>(FxDir, "Jet_Canister", r => Jet(r, new Color(0.8f, 0.8f, 0.78f, 0.8f), 1.3f, flameJet));
            var jetRocket = Get<FxRecipe>(FxDir, "Jet_Rocket", r => RocketExhaust(r, rocketMotor));
            var smokeScreen = Get<FxRecipe>(FxDir, "SmokeScreen", SmokeScreen);
            var crackle = Get<FxRecipe>(FxDir, "Crackle_SmallArms", Crackle);

            // ---------------------------------------------------------------- substances
            var diesel = Get<SubstanceData>(SubDir, "Diesel", s =>
            {
                Liquid(s, "Дизельное топливо", 0.84f, 0.35f, 0.08f, 0.02f, 6f, 40f, 1f, 2.2f);
                s.burningRate = 0.045f; s.kBeta = 2.1f; s.heatOfCombustion = 44.4f; s.heatReleaseMW = 4f; s.mistFraction = 0.0005f;
                s.fire = fireBlack; s.flash = exp[(int)ExplosionKind.Fuel]; s.seep = seepBlack;
            });
            var gasoline = Get<SubstanceData>(SubDir, "Gasoline", s =>
            {
                Liquid(s, "Бензин", 0.75f, 0.8f, 0.35f, 0.15f, 2f, 20f, 1.3f, 2.8f);
                s.burningRate = 0.055f; s.kBeta = 2.1f; s.heatOfCombustion = 43.7f; s.heatReleaseMW = 6f; s.mistFraction = 0.001f;
                s.fire = fireGasoline; s.flash = exp[(int)ExplosionKind.Fuel]; s.seep = seepBlack;
            });
            var jetFuel = Get<SubstanceData>(SubDir, "JetFuel", s =>
            {
                Liquid(s, "Авиакеросин", 0.8f, 0.55f, 0.2f, 0.08f, 3f, 25f, 1.2f, 2.5f);
                s.burningRate = 0.039f; s.kBeta = 3.5f; s.heatOfCombustion = 43.2f; s.heatReleaseMW = 5f; s.mistFraction = 0.0007f;
                s.fire = fireKerosene; s.flash = exp[(int)ExplosionKind.Fuel]; s.seep = seepBlack;
            });
            var oil = Get<SubstanceData>(SubDir, "Oil", s =>
            {
                Liquid(s, "Масло", 0.88f, 0.25f, 0.05f, 0.01f, 5f, 15f, 0.6f, 1.5f);
                s.mistFireball = false;
                // kBeta not found in the sources: 0.7 assumed (abstract).
                s.burningRate = 0.039f; s.kBeta = 0.7f; s.heatOfCombustion = 46f; s.heatReleaseMW = 1.5f; s.mistFraction = 0f;
                s.fire = fireOil; s.seep = seepBlack;
            });
            var propellant = Get<SubstanceData>(SubDir, "GunPropellant", s =>
            {
                s.displayName = "Метательные заряды"; s.kind = SubstanceClass.GunPropellant; s.unit = AmountUnit.Charges;
                s.massPerUnit = 6f; // propellant per charge, abstract
                s.igniteJet = 0.9f; s.igniteSpall = 0.5f; s.igniteFragment = 0.15f; s.heatToIgnite = 12f;
                s.suppressible = false; s.violent = true; s.burnSeconds = 8f; s.intensity = 3f; s.radius = 3.5f;
                s.heatOfCombustion = 4f; s.heatReleaseMW = 20f; s.mistFraction = 0f;
                s.fire = fireGrey; s.ventJet = jetPropellant; s.flash = exp[(int)ExplosionKind.Ammo]; s.seep = seepGrey; s.cookOffSound = flameJet;
            });
            var shells = Get<SubstanceData>(SubDir, "HighExplosiveShells", s =>
            {
                s.displayName = "Снаряды с ВВ"; s.kind = SubstanceClass.HighExplosive; s.unit = AmountUnit.Rounds;
                s.massPerUnit = 20f; s.detonates = true; s.detonateJet = 0.35f; s.detonateSpall = 0.05f; s.heatToDetonate = 18f;
                s.equivalentPerUnit = 4f; // abstract
                s.detonation = exp[(int)ExplosionKind.Ammo]; s.chainBang = exp[(int)ExplosionKind.Armor]; s.chainCount = 4; s.chainSpacing = 1.5f;
            });
            var rockets = Get<SubstanceData>(SubDir, "RocketMotor", s =>
            {
                s.displayName = "Ракетные двигатели (РСЗО)"; s.kind = SubstanceClass.RocketMotor; s.unit = AmountUnit.Rockets;
                s.massPerUnit = 20f; // motor propellant per rocket, abstract (a 122 mm 9M22 rocket weighs ~66 kg all up)
                s.igniteJet = 0.85f; s.igniteSpall = 0.45f; s.igniteFragment = 0.15f; s.heatToIgnite = 10f;
                s.suppressible = false; s.burnSeconds = 12f; s.intensity = 2.5f; s.radius = 4f;
                s.heatOfCombustion = 5f; s.heatReleaseMW = 15f;
                s.fire = fireRocket; s.ventJet = jetRocket; s.flash = exp[(int)ExplosionKind.Fuel]; s.seep = seepGrey;
                s.cookOffSound = rocketMotor; s.flyOffShare = 0.25f;
            });
            var missiles = Get<SubstanceData>(SubDir, "CanisterMissile", s =>
            {
                s.displayName = "Ракеты в ТПК"; s.kind = SubstanceClass.CanisterMissile; s.unit = AmountUnit.Missiles;
                s.massPerUnit = 60f; // motor propellant per missile, abstract
                s.igniteJet = 0.7f; s.igniteSpall = 0.3f; s.igniteFragment = 0.1f; s.heatToIgnite = 12f;
                s.suppressible = false; s.burnSeconds = 10f; s.intensity = 2f; s.radius = 3f;
                s.heatOfCombustion = 5f; s.heatReleaseMW = 10f;
                s.fire = fireRocket; s.ventJet = jetCanister; s.flash = exp[(int)ExplosionKind.Fuel]; s.seep = seepGrey; s.cookOffSound = flameJet;
            });
            var smallArms = Get<SubstanceData>(SubDir, "SmallArms", s =>
            {
                s.displayName = "Патроны и 30-мм выстрелы"; s.kind = SubstanceClass.SmallArms; s.unit = AmountUnit.Rounds;
                s.massPerUnit = 0.5f; // abstract mix
                s.igniteJet = 0.3f; s.igniteSpall = 0.1f; s.igniteFragment = 0.05f; s.heatToIgnite = 8f;
                s.burnSeconds = 30f; s.intensity = 0.5f; s.radius = 1.5f; s.heatReleaseMW = 0.5f;
                s.fire = crackle; s.seep = seepGrey; s.cookOffSound = smallArmsSound; s.crackleSeconds = 40f;
            });

            // ---------------------------------------------------------------- libraries
            lib = Get<SubstanceLibrary>("Assets/_Project/Resources", "SubstanceLibrary", _ => { });
            Fill(ref lib.diesel, diesel); Fill(ref lib.gasoline, gasoline); Fill(ref lib.jetFuel, jetFuel); Fill(ref lib.oil, oil); Fill(ref lib.gunPropellant, propellant);
            Fill(ref lib.highExplosiveShells, shells); Fill(ref lib.rocketMotor, rockets); Fill(ref lib.canisterMissile, missiles); Fill(ref lib.smallArms, smallArms);
            EditorUtility.SetDirty(lib);
            var fx = Get<FxLibrary>("Assets/_Project/Resources", "FxLibrary", _ => { });
            Fill(ref fx.ground, exp[(int)ExplosionKind.Ground]); Fill(ref fx.armor, exp[(int)ExplosionKind.Armor]); Fill(ref fx.air, exp[(int)ExplosionKind.Air]);
            Fill(ref fx.era, exp[(int)ExplosionKind.Era]); Fill(ref fx.fuel, exp[(int)ExplosionKind.Fuel]); Fill(ref fx.ammo, exp[(int)ExplosionKind.Ammo]);
            Fill(ref fx.fire, fireBlack); Fill(ref fx.fireGrey, fireGrey); Fill(ref fx.seep, seepBlack); Fill(ref fx.seepGrey, seepGrey); Fill(ref fx.jet, jetPropellant);
            Fill(ref fx.smokeScreen, smokeScreen);
            EditorUtility.SetDirty(fx);
            AssetDatabase.SaveAssets();
            return lib;
        }

        // Fills a reference left empty (Unity objects: no ?? on them, a missing reference is not plain null).
        static void Fill<T>(ref T field, T value) where T : Object
        {
            if (field == null) field = value;
        }

        /// <summary>
        /// Graphics tiers of a recipe's layers: low keeps flash, fireball, sparks, clods, smoke, flames, jets and lights;
        /// medium adds heat shimmer, the blast ring, embers and the rolling dust skirt; high adds the extra fireball waves.
        /// Embers stay on every tier where they are the effect itself (small-arms crackle).
        /// </summary>
        public static void AssignTiers(FxRecipe r)
        {
            bool embersOnly = r.name.StartsWith("Crackle");
            foreach (var l in r.layers)
            {
                if (l == null) continue;
                l.minQuality = l.kind switch
                {
                    FxLayerKind.Haze or FxLayerKind.ShockRing => 2,
                    FxLayerKind.Embers => embersOnly ? 1 : 2,
                    FxLayerKind.Fireball => l.delay > 0f ? 3 : 1,
                    FxLayerKind.Smoke => l.groundRing ? 2 : 1,
                    _ => 1,
                };
            }
        }

        [MenuItem("Modern Drone/Setup/Assign Effect Quality Tiers")]
        public static void AssignAllTiers()
        {
            foreach (var g in AssetDatabase.FindAssets("t:FxRecipe", new[] { FxDir }))
            {
                var r = AssetDatabase.LoadAssetAtPath<FxRecipe>(AssetDatabase.GUIDToAssetPath(g));
                AssignTiers(r);
                EditorUtility.SetDirty(r);
            }
            AssetDatabase.SaveAssets();
        }

        static T Get<T>(string dir, string name, System.Action<T> setup) where T : ScriptableObject
        {
            string path = $"{dir}/{name}.asset";
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            a.name = name;
            setup(a);
            if (a is FxRecipe recipe) AssignTiers(recipe);
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void Liquid(SubstanceData s, string name, float density, float jet, float spall, float frag, float heat, float burn, float intensity, float radius)
        {
            s.displayName = name; s.kind = SubstanceClass.LiquidFuel; s.unit = AmountUnit.Litres; s.massPerUnit = density;
            s.igniteJet = jet; s.igniteSpall = spall; s.igniteFragment = frag; s.mistFireball = true; s.liquid = true;
            s.heatToIgnite = heat; s.suppressible = true; s.burnSeconds = burn; s.intensity = intensity; s.radius = radius;
        }

        // ---------------------------------------------------------------- recipe layouts (look values, abstract)

        static FxLayer L(FxLayerKind kind, float size, int count, float lifetime, Color color) =>
            new FxLayer { kind = kind, size = size, count = count, lifetime = lifetime, color = color, radius = 0.4f };

        static FxLayer Light(FxLightMode mode, float intensity, float range, float duration, Color color, float offset = 0f) =>
            new FxLayer { kind = FxLayerKind.Light, lightMode = mode, intensity = intensity, size = range, lifetime = duration, color = color, offset = offset };

        static void Explosion(FxRecipe r, ExplosionKind k, SoundBank sound)
        {
            bool ammo = k == ExplosionKind.Ammo, fuel = k == ExplosionKind.Fuel, era = k == ExplosionKind.Era;
            bool armor = k == ExplosionKind.Armor, ground = k == ExplosionKind.Ground;
            var layers = new System.Collections.Generic.List<FxLayer>();
            // 1. Flash: a white-hot burst and its light, a few frames long.
            layers.Add(L(FxLayerKind.Flash, ammo ? 9f : era ? 3.5f : fuel ? 6f : 5f, 2, era ? 0.04f : 0.07f, new Color(1f, 0.95f, 0.85f, 1f)));
            layers.Add(Light(FxLightMode.Flash, ammo ? 60f : 30f, ammo ? 40f : 25f, era ? 0.06f : 0.12f, new Color(1f, 0.85f, 0.65f)));
            // 2. Fireball: lit, glowing gas cooling into soot. Each frame is the whole simulated fireball: a few copies.
            float fbSize = ammo ? 6.5f : fuel ? 5f : era ? 2f : 3.4f;
            FxLayer Fireball(int count, float delay) => new FxLayer
            {
                kind = FxLayerKind.Fireball, size = fbSize, count = count, delay = delay, lifetime = ammo ? 3.6f : fuel ? 2.6f : era ? 0.9f : 1.6f,
                speed = 2.5f, speedMin = 0.6f, gravity = fuel ? -0.06f : -0.2f, color = new Color(0.32f, 0.3f, 0.28f, 1f), intensity = 1f, soot = 0.6f, radius = 0.4f
            };
            layers.Add(Fireball(ammo ? 4 : fuel ? 4 : era ? 1 : armor ? 2 : 3, 0f));
            if (ammo)
            {
                // Charges and shells go one after another: a second, rolling wave of fire.
                layers.Add(Fireball(2, 0.25f));
                layers.Add(Fireball(2, 0.6f));
            }
            layers.Add(Light(FxLightMode.Glow, ammo ? 14f : fuel ? 10f : 6f, ammo ? 40f : 26f, ammo ? 2.2f : fuel ? 1.1f : era ? 0.15f : 0.35f, new Color(1f, 0.55f, 0.25f)));
            // 3. Blast front: a ring of bent light racing out.
            layers.Add(L(FxLayerKind.ShockRing, ammo ? 55f : era ? 14f : 28f, 1, ammo ? 0.16f : 0.1f, Color.white));
            // 4. Fragments, molten liner metal, burning propellant grains, earth.
            if (!fuel)
                layers.Add(new FxLayer
                {
                    kind = FxLayerKind.Sparks, count = armor || era ? 70 : ammo ? 110 : 45, lifetime = armor ? 1.2f : 0.7f, speed = 65f, speedMin = 20f,
                    size = 0.1f, gravity = 1f, radius = 0.2f, collide = true, color = Color.white
                });
            if (ammo)
                layers.Add(new FxLayer
                {
                    kind = FxLayerKind.Embers, count = 90, lifetime = 6f, speed = 16f, speedMin = 5f, size = 0.2f, gravity = 0.8f,
                    spread = 90f, radius = 0.8f, collide = true, color = Color.white
                });
            if (ground)
            {
                layers.Add(new FxLayer { kind = FxLayerKind.Clods, count = 70, lifetime = 3f, speed = 17f, speedMin = 6f, size = 0.3f, gravity = 1f, spread = 35f, radius = 0.3f, collide = true, color = Color.white });
                // The thrown-up earth: brown, heavy, falling back as it spreads; and a dust skirt rolling out.
                layers.Add(new FxLayer
                {
                    kind = FxLayerKind.Smoke, material = FxMaterial.Dust, color = new Color(0.4f, 0.32f, 0.23f, 1f), size = 2.6f, count = 9, lifetime = 4.5f,
                    speed = 11f, speedMin = 5f, gravity = 0.55f, spread = 16f, radius = 0.4f, rise = 0.2f, windDrift = true
                });
                layers.Add(new FxLayer
                {
                    kind = FxLayerKind.Smoke, material = FxMaterial.Dust, color = new Color(0.55f, 0.48f, 0.38f, 0.8f), size = 2.4f, count = 16, lifetime = 6f,
                    speed = 12f, speedMin = 7f, gravity = 0f, groundRing = true, radius = 0.6f, rise = 0.2f, windDrift = true
                });
            }
            // 5. Smoke rolling out of the cooling fireball, then rising and drifting.
            Color soot = era ? new Color(0.55f, 0.55f, 0.53f) : fuel ? new Color(0.07f, 0.065f, 0.06f) : ammo ? new Color(0.28f, 0.27f, 0.26f) : new Color(0.18f, 0.17f, 0.16f);
            soot.a = era ? 0.6f : 0.85f;
            layers.Add(new FxLayer
            {
                kind = FxLayerKind.Smoke, material = FxMaterial.SmokeHot, color = soot, size = ammo ? 6f : era ? 3f : 4.2f, count = ammo ? 20 : fuel ? 16 : era ? 5 : 11,
                lifetime = ammo || fuel ? 16f : era ? 7f : 12f, delay = ammo ? 0.4f : fuel ? 0.25f : 0.08f, speed = 2.4f, speedMin = 0.6f, gravity = -0.02f,
                radius = 0.6f, rise = 1.3f, windDrift = true
            });
            r.layers = layers.ToArray();
            r.referenceSize = fbSize;
            r.lifetime = ammo || fuel ? 22f : 16f;
            r.poolSize = era ? 6 : 4;
            r.shockwave = true;
            r.dazzles = true;
            r.shakesTrees = true;
            r.obscures = k switch
            {
                ExplosionKind.Air => FxRecipe.Obscures.AirBurst,
                ExplosionKind.Era => FxRecipe.Obscures.Era,
                ExplosionKind.Ground => FxRecipe.Obscures.GroundBurst,
                ExplosionKind.Fuel => FxRecipe.Obscures.Fuel,
                ExplosionKind.Ammo => FxRecipe.Obscures.Fuel,
                _ => FxRecipe.Obscures.VehicleBurst,
            };
            r.scorch = k == ExplosionKind.Air ? 0f : ammo ? 9f : fuel ? 6f : 3.2f;
            if (ammo) { r.debris = 6; r.debrisSpeed = 22f; r.debrisSize = 1.2f; }
            else if (armor) { r.debris = 2; r.debrisSpeed = 12f; r.debrisSize = 0.6f; }
            r.sound = sound;
        }

        /// <summary>Flames out of an opening, a smoke column, heat shimmer, rising embers.</summary>
        static void Fire(FxRecipe r, Color smoke, SoundBank sound)
        {
            r.looping = true;
            r.lifetime = 0f;
            r.poolSize = 14;
            // Calibration (abstract): the flames drawn at scale 1 stand for a 2.6 m flame height (FireScaling.OpeningFlameHeight).
            r.referenceSize = 2.6f;
            r.layers = new[]
            {
                new FxLayer { kind = FxLayerKind.Flames, size = 2.4f, rate = 14f, lifetime = 1.1f, speed = 2f, speedMin = 0.8f, spread = 25f, radius = 0.45f, rise = 2.6f, color = Color.white, intensity = 1f },
                Light(FxLightMode.Fire, 4.5f, 13f, 0f, new Color(1f, 0.55f, 0.25f), 1.2f),
                new FxLayer { kind = FxLayerKind.Smoke, material = FxMaterial.SmokeHot, color = smoke, size = 3.2f, rate = 6f, lifetime = 12f, speed = 2.4f, speedMin = 0.6f, gravity = -0.02f, radius = 0.6f, rise = 2.6f, offset = 1.4f, windDrift = true },
                new FxLayer { kind = FxLayerKind.Haze, size = 2.5f, rate = 5f, lifetime = 1.4f, speed = 2f, speedMin = 1f, radius = 0.4f, color = new Color(1f, 1f, 1f, 0.8f), offset = 2.2f },
                new FxLayer { kind = FxLayerKind.Embers, rate = 5f, lifetime = 3f, speed = 3f, speedMin = 1f, size = 0.07f, gravity = -0.15f, spread = 25f, radius = 1f, color = Color.white },
            };
            r.sound = sound;
        }

        static void Seep(FxRecipe r, Color smoke, int pool)
        {
            r.looping = true;
            r.lifetime = 0f;
            r.poolSize = pool;
            r.referenceSize = 2.2f;
            r.layers = new[]
            {
                new FxLayer { kind = FxLayerKind.Smoke, material = FxMaterial.SmokeHot, color = smoke, size = 2.2f, rate = 4f, lifetime = 12f, speed = 1.2f, speedMin = 0.4f, gravity = -0.02f, radius = 0.6f, rise = 2.6f, windDrift = true },
            };
        }

        /// <summary>Burning propellant venting through an opening: a roaring jet for seconds (set per play), smoke and shimmer.</summary>
        static void Jet(FxRecipe r, Color smoke, float heat, SoundBank sound)
        {
            r.looping = false;
            r.lifetime = 14f;
            r.poolSize = 6;
            r.referenceSize = 2.2f;
            r.layers = new[]
            {
                new FxLayer { kind = FxLayerKind.Jet, size = 2.2f, rate = 45f, duration = 10f, lifetime = 0.7f, speed = 18f, speedMin = 10f, spread = 9f, radius = 0.3f, color = Color.white, intensity = heat },
                Light(FxLightMode.Fire, 12f, 22f, 0f, new Color(1f, 0.62f, 0.3f), 2f),
                new FxLayer { kind = FxLayerKind.Smoke, material = FxMaterial.SmokeHot, color = smoke, size = 3.2f, rate = 5f, duration = 14f, lifetime = 11f, speed = 2.4f, speedMin = 0.6f, gravity = -0.02f, radius = 0.6f, rise = 2.6f, offset = 2.5f, windDrift = true },
                new FxLayer { kind = FxLayerKind.Haze, size = 4f, rate = 5f, lifetime = 1.4f, speed = 2f, speedMin = 1f, radius = 0.4f, color = new Color(1f, 1f, 1f, 0.8f), offset = 3f },
            };
            r.sound = sound;
        }

        /// <summary>
        /// Burning charges venting through a hatch: a narrow vertical torch about 6-8 m high (two to three tank heights,
        /// judged from footage such as youtu.be/gbI1EAPONWs; no measured height found, abstract) under light grey
        /// propellant smoke. Fast, thin, long-lived flame particles: the flame stays bright for about half its life, rising at
        /// up to 8 m/s (a short jet tongue slows to 2.5) it reaches ~6-7 m in that time.
        /// </summary>
        public static void PropellantTorch(FxRecipe r, SoundBank sound)
        {
            Jet(r, new Color(0.62f, 0.61f, 0.6f, 0.7f), 1f, sound);
            r.referenceSize = 1.1f;
            var torch = r.layers[0];
            torch.size = 1.1f; torch.rate = 80f; torch.lifetime = 1.6f; torch.speed = 22f; torch.speedMin = 15f;
            torch.spread = 4f; torch.radius = 0.25f; torch.intensity = 1.2f; torch.dragLimit = 8f;
            r.layers[2].offset = 6f; // the smoke leaves the top of the torch
            r.layers[3].offset = 5f;
        }

        /// <summary>Exhaust of a loose rocket: a short hot jet and a white smoke trail left in the air.</summary>
        static void RocketExhaust(FxRecipe r, SoundBank sound)
        {
            r.looping = false;
            r.lifetime = 9f;
            r.poolSize = 8;
            r.referenceSize = 1.4f;
            r.layers = new[]
            {
                new FxLayer { kind = FxLayerKind.Jet, size = 1.4f, rate = 40f, duration = 3f, lifetime = 0.35f, speed = 14f, speedMin = 8f, spread = 6f, radius = 0.15f, color = Color.white, intensity = 1.4f },
                Light(FxLightMode.Fire, 8f, 15f, 0f, new Color(1f, 0.75f, 0.45f)),
                new FxLayer { kind = FxLayerKind.Smoke, material = FxMaterial.Smoke, color = new Color(0.86f, 0.86f, 0.84f, 0.8f), size = 2.4f, rate = 30f, duration = 3.5f, lifetime = 4f, speed = 0.8f, speedMin = 0.2f, gravity = 0f, radius = 0.2f, rise = 0.3f, windDrift = true },
            };
            r.sound = sound;
        }

        static void SmokeScreen(FxRecipe r)
        {
            r.looping = false;
            r.lifetime = 70f;
            r.poolSize = 3;
            r.referenceSize = 7f;
            r.obscures = FxRecipe.Obscures.Screen;
            // Grenades burn for a while: the cloud is fed for ~25 s and drifts off over the next half minute (it screens
            // for up to 1-2 min in calm air, less in wind: SmokeVolumes.Cloud).
            r.layers = new[]
            {
                new FxLayer { kind = FxLayerKind.Smoke, material = FxMaterial.Smoke, color = new Color(0.78f, 0.79f, 0.76f, 1f), size = 7f, count = 16, rate = 3f, duration = 25f, lifetime = 24f, speed = 6f, speedMin = 2f, gravity = -0.02f, spread = 90f, radius = 6f, rise = 0.5f, windDrift = true },
            };
        }

        /// <summary>Rounds cooking off: sparks and pops spitting out, a thin grey smoke.</summary>
        static void Crackle(FxRecipe r)
        {
            r.looping = true;
            r.lifetime = 0f;
            r.poolSize = 4;
            r.referenceSize = 1f;
            r.layers = new[]
            {
                new FxLayer { kind = FxLayerKind.Embers, rate = 14f, lifetime = 0.8f, speed = 9f, speedMin = 2f, size = 0.06f, gravity = 0.6f, spread = 60f, radius = 0.5f, collide = true, color = Color.white },
                Light(FxLightMode.Fire, 2f, 8f, 0f, new Color(1f, 0.7f, 0.4f), 0.3f),
                new FxLayer { kind = FxLayerKind.Smoke, material = FxMaterial.SmokeHot, color = new Color(0.45f, 0.44f, 0.42f, 0.6f), size = 1.6f, rate = 2f, lifetime = 8f, speed = 1.2f, speedMin = 0.3f, gravity = -0.02f, radius = 0.4f, rise = 1f, windDrift = true },
            };
        }
    }
}
