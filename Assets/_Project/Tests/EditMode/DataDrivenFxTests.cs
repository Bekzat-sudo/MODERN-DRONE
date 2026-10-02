using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Tests
{
    /// <summary>Effects driven by data: published scaling, substance behaviour, compartments, detonation, recipes, prefabs.</summary>
    public class DataDrivenFxTests
    {
        // ---------------------------------------------------------------- scaling from mass

        [Test]
        public void FireballScalesWithTheCubeRootOfMass()
        {
            // CCPS: D = 5.8 M^(1/3), t = 0.45 M^(1/3) below 30 t.
            Assert.AreEqual(58f, FireScaling.FireballDiameter(1000f), 0.01f);
            Assert.AreEqual(4.5f, FireScaling.FireballDuration(1000f), 0.01f);
            Assert.AreEqual(2.6f * Mathf.Pow(64000f, 1f / 6f), FireScaling.FireballDuration(64000f), 0.01f);
            // Eight times the mass, twice the diameter.
            Assert.AreEqual(2f, FireScaling.FireballDiameter(80f) / FireScaling.FireballDiameter(10f), 1e-3f);
            Assert.AreEqual(6.8f, FireScaling.DetonationDiameter(8f), 0.01f);
            // Baker / IATG 01.80: d = 3.86 Q^0.32.
            Assert.AreEqual(3.86f * Mathf.Pow(100f, 0.32f), FireScaling.PropellantFireballDiameter(100f), 0.01f);
            Assert.AreEqual(0f, FireScaling.FireballDiameter(-5f));
        }

        [Test]
        public void PoolBurningApproachesTheLargePoolRate()
        {
            // Babrauskas: small pools burn slower, large ones at m''inf.
            float small = FireScaling.BurningRate(0.055f, 2.1f, 0.2f), large = FireScaling.BurningRate(0.055f, 2.1f, 10f);
            Assert.Less(small, large);
            Assert.AreEqual(0.055f, large, 1e-4f);
            Assert.Greater(FireScaling.PoolBurnTime(100f, 0.055f, 2.1f, 1f), FireScaling.PoolBurnTime(100f, 0.055f, 2.1f, 3f));
        }

        [Test]
        public void FlamesOutOfAnOpeningAreCappedByItsVentilation()
        {
            // Heskestad: 1 MW over a 1 m source ~ 2.7 m.
            Assert.AreEqual(0.235f * Mathf.Pow(1000f, 0.4f) - 1.02f, FireScaling.FlameHeight(1000f, 1f), 1e-3f);
            // A hatch lets in air for far less than a 20 MW propellant fire: same flames for 5 or 20 MW.
            Assert.AreEqual(FireScaling.OpeningFlameHeight(5f, 0.3f), FireScaling.OpeningFlameHeight(20f, 0.3f), 1e-4f);
            // A big opening lets the fire grow.
            Assert.Greater(FireScaling.OpeningFlameHeight(20f, 3f), FireScaling.OpeningFlameHeight(20f, 0.3f));
            Assert.Greater(FireScaling.VentilationLimit(3f), FireScaling.VentilationLimit(0.3f));
        }

        [Test]
        public void FlameScaleGrowsWithHeatAndStaysInRange()
        {
            var lib = SubstanceLibrary.Instance;
            var recipe = FxLibrary.Instance.fire;
            float ring = VehicleEffects.FlameScale(lib.gunPropellant, 1f, 3f, recipe);
            float hatch = VehicleEffects.FlameScale(lib.diesel, 1f, 0.3f, recipe);
            float hole = VehicleEffects.FlameScale(lib.diesel, 1f, 0.01f, recipe);
            Assert.Greater(ring, hatch);
            Assert.Greater(hatch, hole);
            Assert.That(hole, Is.InRange(0.3f, 2.2f));
            Assert.That(ring, Is.InRange(0.3f, 2.2f));
            // Half the fire through each of two openings: smaller flames than all of it through one.
            Assert.LessOrEqual(VehicleEffects.FlameScale(lib.oil, 0.5f, 1f, recipe), VehicleEffects.FlameScale(lib.oil, 1f, 1f, recipe));
        }

        // ---------------------------------------------------------------- substances

        [Test]
        public void SubstancesBehaveByTheirProperties()
        {
            var lib = SubstanceLibrary.Instance;
            Assert.IsNotNull(lib, "Resources/SubstanceLibrary (Modern Drone/Setup/Build Substance Assets)");
            // Petrol lights easier than diesel, oil hardest; oil does not flash as a mist.
            Assert.Greater(lib.gasoline.IgniteChance(HitKind.Jet), lib.diesel.IgniteChance(HitKind.Jet));
            Assert.Greater(lib.diesel.IgniteChance(HitKind.Jet), lib.oil.IgniteChance(HitKind.Jet));
            Assert.Less(lib.gasoline.heatToIgnite, lib.diesel.heatToIgnite);
            Assert.IsFalse(lib.oil.mistFireball);
            Assert.IsTrue(lib.diesel.liquid && lib.gasoline.liquid && lib.jetFuel.liquid);
            // Petrol burns fastest (Babrauskas m''inf).
            Assert.Greater(lib.gasoline.burningRate, lib.diesel.burningRate);
            // Propellant deflagrates violently and halon cannot stop it; fuel fires can be put out.
            Assert.IsTrue(lib.gunPropellant.violent);
            Assert.IsFalse(lib.gunPropellant.suppressible);
            Assert.IsTrue(lib.diesel.suppressible);
            // Only explosive fill detonates, and only from a jet or spall.
            Assert.IsTrue(lib.highExplosiveShells.detonates);
            Assert.AreEqual(0f, lib.highExplosiveShells.DetonateChance(HitKind.Fragment));
            Assert.AreEqual(0f, lib.diesel.DetonateChance(HitKind.Jet));
            // Each class cooks off its own way.
            Assert.Greater(lib.rocketMotor.flyOffShare, 0f);
            Assert.Greater(lib.smallArms.crackleSeconds, 0f);
            Assert.Greater(lib.highExplosiveShells.chainCount, 1);
            Assert.IsNotNull(lib.canisterMissile.ventJet);
            // Smoke: fuel black, propellant and rocket motors light.
            Assert.Less(Brightness(lib.diesel.fire), 0.3f);
            Assert.Greater(Brightness(lib.gunPropellant.fire), 0.4f);
            Assert.Greater(Brightness(lib.rocketMotor.fire), 0.6f);
        }

        static float Brightness(FxRecipe r)
        {
            foreach (var l in r.layers) if (l.kind == FxLayerKind.Smoke) return l.color.grayscale;
            return -1f;
        }

        [Test]
        public void ModulesTakeTheirDefaultsFromTheLibrary()
        {
            var go = new GameObject("M");
            go.AddComponent<BoxCollider>();
            var m = go.AddComponent<VehicleModule>();
            try
            {
                m.Configure(ModuleType.FuelTank, 100f, 10f);
                Assert.AreEqual(SubstanceLibrary.Instance.diesel, m.Substance);
                Assert.Greater(m.Amount, 0f);
                m.Configure(ModuleType.AmmoRack, 100f, 10f);
                Assert.AreEqual(SubstanceClass.GunPropellant, m.Substance.kind);
                Assert.IsTrue(m.HighExplosive);
                m.SetContents(SubstanceLibrary.Instance.gasoline, 200f, fill: 0.5f);
                Assert.AreEqual(100f, m.Remaining, 1e-3f);
                Assert.IsFalse(m.HighExplosive);
                Assert.AreEqual(150f, SubstanceLibrary.Instance.gasoline.Mass(200f), 1f); // 0.75 kg/L
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------- detonation

        sealed class FixedRandom : System.Random
        {
            readonly double _v;
            public FixedRandom(double v) => _v = v;
            public override double NextDouble() => _v;
        }

        [Test]
        public void DetonationRecordsWhatWentAndHowMuch()
        {
            var root = new GameObject("Vehicle");
            try
            {
                var model = root.AddComponent<VehicleDamageModel>();
                var go = new GameObject("Rack");
                go.transform.SetParent(root.transform);
                go.AddComponent<BoxCollider>();
                var rack = go.AddComponent<VehicleModule>();
                rack.Configure(ModuleType.AmmoRack, 1e6f, 10f);
                rack.SetContents(SubstanceLibrary.Instance.gunPropellant, 22f, SubstanceLibrary.Instance.highExplosiveShells, 22f);
                model.Rebuild();
                model.Detonate(rack);
                var d = model.LastDetonation;
                Assert.AreEqual(rack, d.module);
                Assert.IsTrue(d.explosive);
                Assert.AreEqual(SubstanceLibrary.Instance.highExplosiveShells, d.substance);
                Assert.AreEqual(22f, d.amount, 1e-3f);
                Assert.AreEqual(DestructionCause.AmmoDetonation, model.Cause);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void PropellantFireBlowsTheHullWithTheChargesLeft()
        {
            var root = new GameObject("Vehicle");
            try
            {
                var model = root.AddComponent<VehicleDamageModel>();
                var go = new GameObject("Charges");
                go.transform.SetParent(root.transform);
                go.AddComponent<BoxCollider>();
                var rack = go.AddComponent<VehicleModule>();
                rack.Configure(ModuleType.AmmoRack, 1e6f, 10f);
                rack.SetContents(SubstanceLibrary.Instance.gunPropellant, 30f); // charges only, no explosive fill
                model.SetAmmoConfinement(1f); // a closed hull: the deflagration blows it
                model.SetRandom(new FixedRandom(0.5));
                model.Rebuild();
                model.Ignite(rack);
                for (int i = 0; i < 100 && model.Cause == DestructionCause.None; i++) model.Tick(0.1f);
                var d = model.LastDetonation;
                Assert.AreEqual(DestructionCause.AmmoDetonation, model.Cause);
                Assert.IsFalse(d.explosive);
                Assert.AreEqual(SubstanceClass.GunPropellant, d.substance.kind);
                Assert.That(d.amount, Is.InRange(1f, 30f));
                // The fireball the effects draw grows with what was left.
                Assert.Greater(FireScaling.PropellantFireballDiameter(d.substance.Mass(30f)), FireScaling.PropellantFireballDiameter(d.substance.Mass(5f)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---------------------------------------------------------------- compartments

        static VentPicker.Vent V(Vector3 p, float area, int compartment, VentKind kind = VentKind.Grille) =>
            new VentPicker.Vent { position = p, normal = Vector3.up, area = area, open = true, kind = kind, compartment = compartment };

        [Test]
        public void FlamesStayInTheirCompartmentUntilABreach()
        {
            var go = new GameObject("Profile");
            try
            {
                var p = go.AddComponent<VehicleFxProfile>();
                p.compartments = new[]
                {
                    new Compartment { name = "Fighting", kind = CompartmentKind.Fighting, center = new Vector3(0f, 1f, 1f), size = new Vector3(2f, 1.5f, 2f) },
                    new Compartment { name = "Engine", kind = CompartmentKind.Engine, center = new Vector3(0f, 1f, -1.3f), size = new Vector3(2f, 1.5f, 2f) },
                    new Compartment { name = "Cab", kind = CompartmentKind.Cab, center = new Vector3(0f, 1f, 5f), size = new Vector3(2f, 1.5f, 1f) },
                };
                Assert.AreEqual(0, p.CompartmentAt(new Vector3(0f, 1f, 1f)));
                Assert.AreEqual(1, p.CompartmentAt(new Vector3(0f, 1f, -2f)));
                Assert.AreEqual(-1, p.CompartmentAt(new Vector3(0f, 1f, 20f)));

                // A fire in the fighting compartment: the engine grille is the nearest big opening, but behind a bulkhead.
                var vents = new List<VentPicker.Vent>
                {
                    V(new Vector3(0f, 2f, -1.2f), 0.8f, 1),
                    V(new Vector3(0f, 2f, 1.5f), 0.3f, 0, VentKind.Hatch),
                };
                var shares = new List<(int, float)>();
                Vector3 fire = new Vector3(0f, 1f, 0.2f);
                VentPicker.Block(0, vents, p.Connected);
                VentPicker.Share(fire, vents, shares);
                Assert.AreEqual(1, shares.Count);
                Assert.AreEqual(1, shares[0].Item1);

                // Pressure breaches the bulkhead: now the grille takes flames too.
                p.BreachAround(0);
                Assert.IsTrue(p.Connected(0, 1));
                Assert.IsFalse(p.Connected(0, 2));
                VentPicker.Block(0, vents, p.Connected);
                VentPicker.Share(fire, vents, shares);
                Assert.AreEqual(2, shares.Count);

                // Connections chain: cab joined to the engine bay is reachable from the fighting compartment through it.
                p.compartments[2].connected = new[] { 1 };
                Assert.IsTrue(p.Connected(0, 2));
                // No compartment known: nothing is blocked.
                VentPicker.Block(-1, vents, p.Connected);
                Assert.IsFalse(vents[0].blocked || vents[1].blocked);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ClosedHatchesBehindABulkheadAreNotBlown()
        {
            var vents = new List<VentPicker.Vent>
            {
                new VentPicker.Vent { position = new Vector3(0f, 2f, 0.5f), area = 0.3f, open = false, kind = VentKind.Hatch, compartment = 0 },
                new VentPicker.Vent { position = new Vector3(0f, 2f, -1f), area = 0.3f, open = false, kind = VentKind.Hatch, compartment = 1 },
            };
            VentPicker.Block(0, vents, (a, b) => a == b);
            var blown = new List<int>();
            VentPicker.Blown(new Vector3(0f, 1f, 0f), vents, blown);
            CollectionAssert.AreEqual(new[] { 0 }, blown);
        }

        // ---------------------------------------------------------------- recipes and prefabs

        [Test]
        public void EveryStandardRecipeBuildsOneChildPerLayer()
        {
            var lib = FxLibrary.Instance;
            Assert.IsNotNull(lib, "Resources/FxLibrary (Modern Drone/Setup/Build Substance Assets)");
            var recipes = new HashSet<FxRecipe> { lib.ground, lib.armor, lib.air, lib.era, lib.fuel, lib.ammo, lib.fire, lib.fireGrey, lib.seep, lib.seepGrey, lib.jet, lib.smokeScreen };
            foreach (var s in new[] { SubstanceLibrary.Instance.rocketMotor, SubstanceLibrary.Instance.canisterMissile, SubstanceLibrary.Instance.smallArms })
            {
                if (s.fire != null) recipes.Add(s.fire);
                if (s.ventJet != null) recipes.Add(s.ventJet);
            }
            foreach (var r in recipes)
            {
                Assert.IsNotNull(r);
                var go = SimpleVfx.Build(r);
                try
                {
                    Assert.AreEqual(r.layers.Length, go.transform.childCount, r.name);
                    Assert.Greater(r.referenceSize, 0f, r.name);
                    if (!r.looping) Assert.Greater(r.lifetime, 0f, r.name);
                }
                finally { Object.DestroyImmediate(go); }
            }
            // The ammunition detonation keeps its phases: flash, rolling fireballs, blast ring, fragments, embers, smoke.
            var kinds = new HashSet<FxLayerKind>();
            foreach (var l in lib.ammo.layers) kinds.Add(l.kind);
            CollectionAssert.IsSubsetOf(new[] { FxLayerKind.Flash, FxLayerKind.Fireball, FxLayerKind.ShockRing, FxLayerKind.Sparks, FxLayerKind.Embers, FxLayerKind.Smoke, FxLayerKind.Light }, kinds);
            Assert.IsTrue(lib.ammo.shockwave && lib.ammo.dazzles && lib.ammo.debris > 0);
        }

        [Test]
        public void LowerEffectsQualityDropsOnlyTheExtras()
        {
            var lib = FxLibrary.Instance;
            int Drawn(FxRecipe r, int q) { int n = 0; foreach (var l in r.layers) if (l.minQuality <= q) n++; return n; }
            // The ammunition detonation thins out with each step down, but low still has its flash, fireball and smoke.
            Assert.Less(Drawn(lib.ammo, 1), Drawn(lib.ammo, 2));
            Assert.Less(Drawn(lib.ammo, 2), Drawn(lib.ammo, 3));
            foreach (var l in lib.ammo.layers)
                if (l.kind == FxLayerKind.Flash || l.kind == FxLayerKind.Smoke || l.kind == FxLayerKind.Fireball && l.delay == 0f)
                    Assert.AreEqual(1, l.minQuality, l.kind.ToString());
            // Small-arms crackle is its embers: kept on every level.
            foreach (var l in SubstanceLibrary.Instance.smallArms.fire.layers)
                if (l.kind == FxLayerKind.Embers) Assert.AreEqual(1, l.minQuality);
            Assert.AreEqual(3, new ModernDrone.Drone.ControlSettings().effectsQuality);
        }

        [Test]
        public void EveryVehiclePrefabPassesTheFxValidator()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Vehicles" });
            Assert.Greater(guids.Length, 10);
            var errors = new List<string>();
            foreach (var g in guids)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (prefab.GetComponent<VehicleDamageModel>() == null) continue;
                foreach (var issue in VehicleFxSetup.Validate(prefab))
                    if (issue.error) errors.Add($"{prefab.name}: {issue.message}");
            }
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void ThrownTrack_FindsTheBeltMaterial()
        {
            // Named belts (BMP-3, MT-LB), and the pair of belt materials the T-72 and 2S19 models carry under other names:
            // never the hull or the road wheels inside the loop.
            var expected = new Dictionary<string, string[]>
            {
                { "IFV_Placeholder", new[] { "bmp3-track__9" } },
                { "MTLB_Tractor", new[] { "mt-lbm-track_2" } },
                { "Tank_Placeholder", new[] { "a2434888-a965-4551-8959-f43d69c782d9.003", "3b8012e5-fddd-4ae8-b8bb-2ef1ebedc2ef.003" } },
                { "SPG_Placeholder", new[] { "4f275be7-5485-41ee-a5e0-df3b708a39ed", "ed7d7ea8-3ad8-42b8-b7ac-d04bb0b1992b" } },
            };
            foreach (var pair in expected)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Vehicles/{pair.Key}.prefab");
                Assert.IsNotNull(prefab, pair.Key);
                var envelope = prefab.transform.Find("Track_L");
                var mesh = envelope.GetComponent<MeshFilter>().sharedMesh;
                var box = new Bounds(prefab.transform.InverseTransformPoint(envelope.TransformPoint(mesh.bounds.center)), Vector3.Scale(mesh.bounds.size, envelope.lossyScale));
                var found = ThrownTrack.FindTrackMaterials(prefab.transform, box).ConvertAll(m => m.name);
                CollectionAssert.AreEquivalent(pair.Value, found, pair.Key);
            }
        }
    }
}
