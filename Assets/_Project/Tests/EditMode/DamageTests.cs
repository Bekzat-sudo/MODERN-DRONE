using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class PenetrationSolverTests
    {
        WarheadData _w;
        readonly List<LayerResult> _results = new List<LayerResult>();

        [SetUp]
        public void SetUp()
        {
            _w = ScriptableObject.CreateInstance<WarheadData>();
            _w.penetration = 300f;
            _w.decayPerMeter = 0f;
            _w.jetMaxLength = 10f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_w);

        static PenetrationLayer Plate(float armor, float cos = 1f, float dist = 0f) =>
            new PenetrationLayer { kind = ArmorKind.Plate, value = armor, cosAngle = cos, distance = dist };

        [Test]
        public void ThinPlate_IsPenetrated_WithResidual()
        {
            int last = PenetrationSolver.Resolve(_w, new[] { Plate(100f) }, () => 0.5, _results);
            Assert.AreEqual(0, last);
            Assert.AreEqual(200f, _results[0].penetrationOut, 1e-3f);
        }

        [Test]
        public void ThickPlate_StopsJet()
        {
            int last = PenetrationSolver.Resolve(_w, new[] { Plate(400f), Plate(10f, 1f, 0.5f) }, () => 0.5, _results);
            Assert.AreEqual(-1, last);
            Assert.IsFalse(_results[0].passed);
            Assert.IsFalse(_results[1].reached);
        }

        [Test]
        public void SlopedPlate_HasHigherEffectiveArmor()
        {
            // 150 points at 60 degrees -> 300 effective: exactly stops a 300 jet.
            int last = PenetrationSolver.Resolve(_w, new[] { Plate(150f, 0.5f) }, () => 0.5, _results);
            Assert.AreEqual(-1, last);
            Assert.AreEqual(300f, _results[0].effectiveArmor, 1e-3f);
        }

        [Test]
        public void Reactive_HalvesSingleCharge_TandemIgnoresIt()
        {
            var layers = new[]
            {
                new PenetrationLayer { kind = ArmorKind.Reactive, value = 0.5f, cosAngle = 1f },
                Plate(200f, 1f, 0.1f)
            };
            // 300 * 0.5 = 150 < 200: the plate behind the block holds.
            PenetrationSolver.Resolve(_w, layers, () => 0.5, _results);
            Assert.IsTrue(_results[0].triggered);
            Assert.IsFalse(_results[1].passed);

            _w.tandem = true;
            Assert.AreEqual(1, PenetrationSolver.Resolve(_w, layers, () => 0.5, _results));
            Assert.IsTrue(_results[1].passed);
        }

        [Test]
        public void Screen_DisruptsOnlyWhenRollSucceeds()
        {
            var layers = new[]
            {
                new PenetrationLayer { kind = ArmorKind.Screen, value = 0.6f, cosAngle = 1f },
                Plate(100f, 1f, 0.5f)
            };
            PenetrationSolver.Resolve(_w, layers, () => 0.9, _results); // roll fails -> no effect
            Assert.IsFalse(_results[0].triggered);
            Assert.IsTrue(_results[1].passed);

            PenetrationSolver.Resolve(_w, layers, () => 0.1, _results); // fuze crushed -> dud, nothing reaches the plate
            Assert.IsTrue(_results[0].triggered);
            Assert.IsFalse(_results[1].reached);
        }

        [Test]
        public void Decay_ReducesPenetrationWithDistance()
        {
            _w.decayPerMeter = 100f;
            var layers = new[] { Plate(50f), Plate(100f, 1f, 1.5f) };
            // 300 - 50 = 250, travel 1.5 m -> 100 left, then 100 plate stops it.
            Assert.AreEqual(0, PenetrationSolver.Resolve(_w, layers, () => 0.5, _results));
        }
    }

    public class VehicleDamageModelTests
    {
        GameObject _root;
        VehicleDamageModel _model;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Vehicle");
            _model = _root.AddComponent<VehicleDamageModel>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        VehicleModule Add(ModuleType type, float hp = 100f)
        {
            var go = new GameObject(type.ToString());
            go.transform.SetParent(_root.transform);
            go.AddComponent<BoxCollider>();
            var m = go.AddComponent<VehicleModule>();
            m.Configure(type, hp, 10f);
            return m;
        }

        [Test]
        public void Helicopter_FliesOnOneEngine_ButNotWithoutTailRotor()
        {
            var left = Add(ModuleType.Engine);
            var right = Add(ModuleType.Engine);
            var tail = Add(ModuleType.TailRotor);
            _model.SetRandom(new System.Random(1));
            _model.Rebuild();

            left.ApplyDamage(1000f);
            Assert.AreEqual(KillState.None, _model.State & KillState.Mobility);
            tail.ApplyDamage(1000f);
            Assert.AreEqual(KillState.Mobility, _model.State & KillState.Mobility);

            _model.Crash();
            Assert.AreEqual(DestructionCause.Crashed, _model.Cause);
            Assert.AreNotEqual(KillState.None, _model.State & KillState.Destroyed);
        }

        [Test]
        public void EngineDestroyed_GivesMobilityKill()
        {
            var engine = Add(ModuleType.Engine);
            Add(ModuleType.Gun);
            _model.SetRandom(new System.Random(1));
            _model.Rebuild();

            engine.ApplyDamage(50f);
            Assert.AreEqual(ModuleState.Damaged, engine.State);
            Assert.AreEqual(KillState.None, _model.State & KillState.Mobility);

            engine.ApplyDamage(60f);
            Assert.AreNotEqual(KillState.None, _model.State & KillState.Mobility);
            Assert.AreEqual(KillState.None, _model.State & KillState.Firepower);
        }

        [Test]
        public void ControlStation_KillsMobilityAndFirepower()
        {
            var control = Add(ModuleType.ControlStation);
            _model.Rebuild();
            control.ApplyDamage(1000f);
            Assert.AreEqual(KillState.Mobility | KillState.Firepower, _model.State);
        }

        [Test]
        public void SensorsKill_OnlyWhenAllSensorsDestroyed()
        {
            var radar = Add(ModuleType.Radar);
            var optics = Add(ModuleType.Optics);
            _model.Rebuild();
            radar.ApplyDamage(1000f);
            Assert.AreEqual(KillState.None, _model.State & KillState.Sensors);
            optics.ApplyDamage(1000f);
            Assert.AreEqual(KillState.Sensors, _model.State & KillState.Sensors);
        }

        [Test]
        public void AmmoRackDestroyed_CanDetonateVehicle()
        {
            var ammo = Add(ModuleType.AmmoRack);
            _model.SetRandom(new FixedRandom(0.0)); // always below the detonation chance
            _model.Rebuild();
            DestructionCause cause = DestructionCause.None;
            _model.Destroyed += (_, c) => cause = c;

            ammo.ApplyDamage(1000f);
            Assert.AreEqual(DestructionCause.AmmoDetonation, cause);
            Assert.AreNotEqual(KillState.None, _model.State & KillState.Destroyed);
        }

        [Test]
        public void Fire_BurnsVehicleOutOverTime()
        {
            var fuel = Add(ModuleType.FuelTank, 50f);
            Add(ModuleType.Engine, 1000f);
            _model.SetRandom(new FixedRandom(0.0));
            _model.Rebuild();

            fuel.ApplyDamage(30f); // damaged -> fire
            Assert.IsTrue(_model.Burning);
            for (int i = 0; i < 300; i++) _model.Tick(0.1f);
            Assert.AreEqual(DestructionCause.BurnedOut, _model.Cause);
        }

        sealed class FixedRandom : System.Random
        {
            readonly double _v;
            public FixedRandom(double v) => _v = v;
            public override double NextDouble() => _v;
            protected override double Sample() => _v;
        }
    }

    /// <summary>Detonations against real colliders (edit-mode physics queries).</summary>
    public class DamageSolverTests
    {
        GameObject _root;
        WarheadData _w;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Target");
            _w = ScriptableObject.CreateInstance<WarheadData>();
            _w.penetration = 300f;
            _w.decayPerMeter = 0f;
            _w.blastRadius = 0f;
            _w.spallRays = 16;
            _w.spallConeAngle = 10f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_w);
        }

        GameObject Box(string name, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_root.transform);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            return go;
        }

        VehicleDamageModel Build(float plateArmor)
        {
            // Plate facing -z at z=0, engine module behind it.
            Box("Plate", new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 0.1f)).AddComponent<ArmorElement>().Configure(ArmorKind.Plate, plateArmor);
            var engine = Box("Engine", new Vector3(0f, 0f, 1f), new Vector3(1f, 1f, 1f));
            engine.AddComponent<VehicleModule>().Configure(ModuleType.Engine, 100f, 20f);
            var model = _root.AddComponent<VehicleDamageModel>();
            model.SetRandom(new System.Random(3));
            model.Rebuild();
            Physics.SyncTransforms();
            return model;
        }

        [Test]
        public void JetThroughThinPlate_DamagesModuleBehind()
        {
            var model = Build(100f);
            var report = DamageSolver.Detonate(_w, new Vector3(0f, 0f, -0.05f), Vector3.forward, new System.Random(1));
            Assert.IsTrue(report.armorPenetrated);
            Assert.IsTrue(report.hits.Exists(h => h.module.Type == ModuleType.Engine));
            Assert.AreEqual(ModuleState.Destroyed, model.Modules[0].State);
        }

        [Test]
        public void WeakResidualJet_DoesLessDamage_ButStillSetsOffAmmo()
        {
            // 300 penetration - 250 plate: the jet reaches the modules with 1/6 of its power left.
            Box("Plate", new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 0.1f)).AddComponent<ArmorElement>().Configure(ArmorKind.Plate, 250f);
            var engine = Box("Engine", new Vector3(0.6f, 0f, 1f), new Vector3(0.5f, 0.5f, 0.5f)).AddComponent<VehicleModule>();
            engine.Configure(ModuleType.Engine, 1000f, 0f);
            var ammo = Box("Ammo", new Vector3(-0.6f, 0f, 1f), new Vector3(0.5f, 0.5f, 0.5f)).AddComponent<VehicleModule>();
            ammo.Configure(ModuleType.AmmoRack, 1000f, 0f);
            _w.spallRays = 0;
            var model = _root.AddComponent<VehicleDamageModel>();
            model.SetRandom(new System.Random(3));
            model.Rebuild();
            Physics.SyncTransforms();

            DamageSolver.Detonate(_w, new Vector3(0.6f, 0f, -0.05f), Vector3.forward, new System.Random(1));
            Assert.Less(engine.MaxHp - engine.Hp, _w.jetDamage * 0.6f);

            DamageSolver.Detonate(_w, new Vector3(-0.6f, 0f, -0.05f), Vector3.forward, new System.Random(1));
            Assert.AreEqual(ModuleState.Destroyed, ammo.State);
        }

        [Test]
        public void ScreenDud_FiresNothing()
        {
            Box("Cage", new Vector3(0f, 0f, -0.3f), new Vector3(2f, 2f, 0.05f)).AddComponent<ArmorElement>().Configure(ArmorKind.Screen, 1f);
            var model = Build(10f);
            var report = DamageSolver.Detonate(_w, new Vector3(0f, 0f, -0.33f), Vector3.forward, new System.Random(1));
            Assert.IsTrue(report.dud);
            Assert.AreEqual(ModuleState.Operational, model.Modules[0].State);
        }

        [Test]
        public void JetAgainstThickPlate_LeavesModuleIntact()
        {
            var model = Build(500f);
            var report = DamageSolver.Detonate(_w, new Vector3(0f, 0f, -0.05f), Vector3.forward, new System.Random(1));
            Assert.IsFalse(report.armorPenetrated);
            Assert.AreEqual(ModuleState.Operational, model.Modules[0].State);
        }

        [Test]
        public void Blast_IsShieldedByArmor_ButHitsExposedModules()
        {
            _w.kind = WarheadKind.Fragmentation;
            _w.blastRadius = 5f;
            _w.blastDamage = 200f;
            _w.fragmentPenetration = 20f;
            var model = Build(100f);
            var exposed = Box("Optics", new Vector3(0f, 1.5f, -0.5f), new Vector3(0.3f, 0.3f, 0.3f));
            exposed.AddComponent<VehicleModule>().Configure(ModuleType.Optics, 40f, 5f);
            model.Rebuild();
            Physics.SyncTransforms();

            DamageSolver.Detonate(_w, new Vector3(0f, 0.5f, -1f), Vector3.forward, new System.Random(1));
            Assert.AreEqual(ModuleState.Destroyed, exposed.GetComponent<VehicleModule>().State);
            Assert.AreEqual(ModuleState.Operational, model.Modules[0].State, "engine behind the plate must be shielded");
        }

        [Test]
        public void LayerConstants_AreUsedByBuilderLayers()
        {
            Assert.AreEqual(GameLayers.Armor, ArmorElement.LayerFor(ArmorKind.Plate));
            Assert.AreEqual(GameLayers.ERA, ArmorElement.LayerFor(ArmorKind.Reactive));
            Assert.AreEqual(GameLayers.Screen, ArmorElement.LayerFor(ArmorKind.Screen));
        }
    }
}
