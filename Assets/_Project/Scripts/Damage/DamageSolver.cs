using System;
using System.Collections.Generic;
using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>
    /// Resolves a detonation against the scene: jet raycast through armor and modules,
    /// spall cone behind penetrated armor, and blast/fragments with line-of-sight shielding.
    /// </summary>
    public static class DamageSolver
    {
        /// <summary>Start the jet slightly in front of the contact so the struck surface is included.</summary>
        const float BackOff = 0.5f;
        const int MaxHits = 64;
        /// <summary>How far past the contact point a screen still counts as the struck element, m.</summary>
        const float ContactTolerance = 0.3f;
        /// <summary>Jet damage at the end of its penetration, as a fraction of full (abstract gameplay value).</summary>
        const float MinJetDamage = 0.35f;

        static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];
        static readonly RaycastHit[] SubHits = new RaycastHit[MaxHits];
        static readonly Collider[] Overlaps = new Collider[128];
        static readonly List<PenetrationLayer> Layers = new List<PenetrationLayer>();
        static readonly List<LayerResult> Results = new List<LayerResult>();
        static readonly List<(RaycastHit hit, ArmorElement armor, VehicleModule module)> Crossed = new List<(RaycastHit, ArmorElement, VehicleModule)>();
        static readonly HashSet<VehicleModule> BlastSeen = new HashSet<VehicleModule>();
        static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        /// <summary>Surfaces a strike can mark: hull and armour colliders (the visual model has none).</summary>
        const int SurfaceMask = GameLayers.VehicleHullMask | GameLayers.ArmorMask | GameLayers.ERAMask | GameLayers.ScreenMask;

        /// <summary>Raised after every detonation, e.g. for persistent marks on the struck vehicle.</summary>
        public static event Action<StrikeReport> Detonated;

        const int JetMask = GameLayers.ArmorMask | GameLayers.ERAMask | GameLayers.ScreenMask | GameLayers.ModuleMask;
        const int SpallMask = GameLayers.ArmorMask | GameLayers.ModuleMask;
        const int ShieldMask = GameLayers.ArmorMask;

        public static StrikeReport LastReport { get; private set; }

        public static StrikeReport Detonate(WarheadData warhead, Vector3 point, Vector3 direction, System.Random random)
        {
            Vector3 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.down;
            var report = new StrikeReport(warhead, point, dir);
            // Find the struck surface first: an ERA block that fires is disabled by the jet.
            bool surface = FindSurface(point, dir, out var surfaceHit);

            if (warhead.kind == WarheadKind.ShapedCharge)
                Jet(report, warhead, point, dir, random);
            else
                ScreenFuze(report, point, dir, random);
            if (warhead.blastRadius > 0f && !report.dud)
                Blast(report, warhead, point - dir * 0.1f);

            if (surface) MarkSurface(report, warhead, surfaceHit);
            LastReport = report;
            DrawDebug(report, 15f);
            Detonated?.Invoke(report);
            return report;
        }

        static void Jet(StrikeReport report, WarheadData w, Vector3 point, Vector3 dir, System.Random random)
        {
            Vector3 origin = point - dir * BackOff;
            int n = SortedRaycast(origin, dir, w.jetMaxLength + BackOff, JetMask, Hits);

            Layers.Clear();
            Crossed.Clear();
            for (int i = 0; i < n; i++)
            {
                var hit = Hits[i];
                var armor = hit.collider.GetComponent<ArmorElement>();
                var module = armor == null ? hit.collider.GetComponent<VehicleModule>() : null;
                if (armor == null && module == null) continue;
                if (armor != null && armor.Consumed) continue;
                // A screen only matters where the warhead struck it; slats further along the jet line are no obstacle.
                if (armor != null && armor.Kind == ArmorKind.Screen && (Layers.Count > 0 || hit.distance > BackOff + ContactTolerance)) continue;

                Layers.Add(new PenetrationLayer
                {
                    kind = armor != null ? armor.Kind : ArmorKind.Module,
                    value = armor != null ? armor.Value : module.Absorb,
                    cosAngle = Vector3.Dot(-dir, hit.normal),
                    distance = hit.distance
                });
                Crossed.Add((hit, armor, module));
            }

            PenetrationSolver.Resolve(w, Layers, random.NextDouble, Results);

            Vector3 end = origin + dir * (w.jetMaxLength + BackOff);
            int lastPlate = -1;
            for (int i = 0; i < Crossed.Count; i++)
            {
                var r = Results[i];
                if (!r.reached) break;
                var (hit, armor, module) = Crossed[i];

                if (armor != null && armor.Kind == ArmorKind.Screen && r.triggered)
                {
                    report.Track(armor.Owner);
                    report.dud = true;
                    armor.Consume();
                    return;
                }

                if (armor != null)
                {
                    report.Track(armor.Owner);
                    if (report.impactElement == null) report.impactElement = armor;
                    if (armor.Kind == ArmorKind.Reactive || (armor.Kind == ArmorKind.Screen && r.triggered))
                    {
                        if (armor.Kind == ArmorKind.Reactive && !armor.Consumed)
                            report.marks.Add(new SurfaceMark { point = hit.point, normal = hit.normal, kind = MarkKind.EraBlast, surface = hit.collider.transform, owner = armor.Owner });
                        armor.Consume();
                    }
                    if (armor.Kind == ArmorKind.Plate && r.passed) lastPlate = i;
                }
                else
                {
                    // A weaker residual jet does less damage. Propellant is the exception: any jet reaching it sets it
                    // off ("penetrating hits can easily set off a chain reaction", en.wikipedia.org/wiki/T-72).
                    float residual = Mathf.Clamp01(r.penetrationIn / Mathf.Max(1f, w.penetration));
                    float damage = w.jetDamage * Mathf.Lerp(MinJetDamage, 1f, residual);
                    if (module.Type == ModuleType.AmmoRack) damage = Mathf.Max(damage, module.MaxHp);
                    report.AddHit(module, damage, HitKind.Jet);
                }

                if (!r.passed)
                {
                    end = hit.point;
                    break;
                }
            }

            report.armorPenetrated = lastPlate >= 0;
            report.traces.Add(new Trace { from = point, to = end, kind = report.armorPenetrated ? TraceKind.Jet : TraceKind.JetStopped });

            if (lastPlate >= 0)
            {
                // Spall intensity scales with how much jet energy was left after the plate.
                float residual = Mathf.Clamp01(Results[lastPlate].penetrationOut / Mathf.Max(1f, w.penetration));
                float scale = Mathf.Lerp(0.4f, 1f, residual);
                Spall(report, w, ExitPoint(Crossed[lastPlate].hit, dir) + dir * 0.02f, dir, scale, random);
            }
        }

        /// <summary>
        /// Marks where the warhead met the vehicle: a shaped charge leaves a hole if it penetrated or a crater if the
        /// plate stopped it, both inside a scorch; a fragmentation charge scorches and pits the surface. A dud
        /// (fuze crushed by a screen) leaves nothing.
        /// </summary>
        static bool FindSurface(Vector3 point, Vector3 dir, out RaycastHit hit)
        {
            int n = SortedRaycast(point - dir * BackOff, dir, BackOff + ContactTolerance, SurfaceMask, SubHits);
            hit = n > 0 ? SubHits[0] : default;
            return n > 0;
        }

        static void MarkSurface(StrikeReport report, WarheadData w, RaycastHit hit)
        {
            if (report.dud) return;
            var owner = hit.collider.GetComponentInParent<VehicleDamageModel>();
            if (owner == null) return;
            void Add(MarkKind kind) => report.marks.Add(new SurfaceMark { point = hit.point, normal = hit.normal, kind = kind, surface = hit.collider.transform, owner = owner });
            Add(MarkKind.Scorch);
            if (w.kind == WarheadKind.ShapedCharge) Add(report.armorPenetrated ? MarkKind.JetHole : MarkKind.JetCrater);
            else Add(MarkKind.Pocks);
        }

        /// <summary>Back face of the struck plate along the jet: spall leaves from there, not from the entry hole.</summary>
        static Vector3 ExitPoint(RaycastHit entry, Vector3 dir)
        {
            const float Probe = 5f;
            var back = new Ray(entry.point + dir * Probe, -dir);
            return entry.collider.Raycast(back, out var exit, Probe) ? exit.point : entry.point;
        }

        /// <summary>Fragmentation charges use the same contact fuze: a screen can crush it too.</summary>
        static void ScreenFuze(StrikeReport report, Vector3 point, Vector3 dir, System.Random random)
        {
            int n = SortedRaycast(point - dir * BackOff, dir, BackOff + ContactTolerance, GameLayers.ScreenMask, Hits);
            if (n == 0) return;
            var armor = Hits[0].collider.GetComponent<ArmorElement>();
            if (armor == null || armor.Consumed || random.NextDouble() >= armor.Value) return;
            report.Track(armor.Owner);
            report.dud = true;
            armor.Consume();
        }

        static void Spall(StrikeReport report, WarheadData w, Vector3 origin, Vector3 dir, float scale, System.Random random)
        {
            for (int ray = 0; ray < w.spallRays; ray++)
            {
                Vector3 d = RandomInCone(dir, w.spallConeAngle, random);
                int n = SortedRaycast(origin, d, w.spallRange, SpallMask, SubHits);
                Vector3 end = origin + d * w.spallRange;

                for (int i = 0; i < n; i++)
                {
                    var hit = SubHits[i];
                    var armor = hit.collider.GetComponent<ArmorElement>();
                    if (armor != null)
                    {
                        if (armor.Consumed || armor.Kind != ArmorKind.Plate) continue;
                        end = hit.point; // inner walls stop fragments
                        break;
                    }
                    var module = hit.collider.GetComponent<VehicleModule>();
                    if (module == null) continue;

                    float falloff = 1f - 0.5f * (hit.distance / w.spallRange);
                    report.AddHit(module, w.spallDamage * scale * falloff, HitKind.Spall);
                    end = hit.point;
                    break;
                }
                report.traces.Add(new Trace { from = origin, to = end, kind = TraceKind.Spall });
            }
        }

        static void Blast(StrikeReport report, WarheadData w, Vector3 point)
        {
            BlastSeen.Clear();
            int n = Physics.OverlapSphereNonAlloc(point, w.blastRadius, Overlaps, GameLayers.ModuleMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var module = Overlaps[i].GetComponent<VehicleModule>();
                if (module == null || !BlastSeen.Add(module)) continue;

                Vector3 target = Overlaps[i].ClosestPoint(point);
                float dist = Vector3.Distance(point, target);
                if (dist > w.blastRadius || IsShielded(point, target, w.fragmentPenetration)) continue;

                float falloff = 1f - dist / w.blastRadius;
                report.AddHit(module, w.blastDamage * falloff, HitKind.Fragment);
                report.traces.Add(new Trace { from = point, to = target, kind = TraceKind.Fragment });
            }
        }

        /// <summary>True if a plate between the points is too strong for fragments.</summary>
        static bool IsShielded(Vector3 from, Vector3 to, float fragmentPenetration)
        {
            Vector3 delta = to - from;
            float len = delta.magnitude;
            if (len < 1e-4f) return false;
            Vector3 dir = delta / len;
            int n = SortedRaycast(from, dir, len, ShieldMask, SubHits);
            for (int i = 0; i < n; i++)
            {
                var armor = SubHits[i].collider.GetComponent<ArmorElement>();
                if (armor == null || armor.Consumed || armor.Kind != ArmorKind.Plate) continue;
                float cos = Vector3.Dot(-dir, SubHits[i].normal);
                if (PenetrationSolver.EffectiveArmor(armor.Value, cos) > fragmentPenetration)
                    return true;
            }
            return false;
        }

        static int SortedRaycast(Vector3 origin, Vector3 dir, float length, int mask, RaycastHit[] buffer)
        {
            int n = Physics.RaycastNonAlloc(origin, dir, buffer, length, mask, QueryTriggerInteraction.Collide);
            Array.Sort(buffer, 0, n, ByDistance);
            return n;
        }

        static Vector3 RandomInCone(Vector3 axis, float angleDeg, System.Random random)
        {
            float cosMax = Mathf.Cos(angleDeg * Mathf.Deg2Rad);
            float z = Mathf.Lerp(cosMax, 1f, (float)random.NextDouble());
            float phi = (float)(random.NextDouble() * Math.PI * 2.0);
            float r = Mathf.Sqrt(1f - z * z);
            return Quaternion.LookRotation(axis) * new Vector3(r * Mathf.Cos(phi), r * Mathf.Sin(phi), z);
        }

        public static Color ColorOf(TraceKind kind) => kind switch
        {
            TraceKind.Jet => new Color(1f, 0.85f, 0.2f),
            TraceKind.JetStopped => new Color(0.3f, 0.6f, 1f),
            TraceKind.Spall => new Color(1f, 0.35f, 0.1f),
            TraceKind.Fragment => new Color(0.9f, 0.9f, 0.9f),
            _ => Color.white
        };

        static void DrawDebug(StrikeReport report, float duration)
        {
            foreach (var t in report.traces)
                Debug.DrawLine(t.from, t.to, ColorOf(t.kind), duration);
        }
    }
}
