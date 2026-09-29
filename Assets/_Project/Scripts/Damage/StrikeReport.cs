using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ModernDrone.Damage
{
    public enum TraceKind
    {
        Jet,
        JetStopped,
        Spall,
        Fragment
    }

    /// <summary>What a strike leaves on the vehicle's surface.</summary>
    public enum MarkKind
    {
        /// <summary>Jet went through: small hole with a ring of molten liner metal and radial soot.</summary>
        JetHole,
        /// <summary>Jet stopped in the plate: shallow crater, scorched paint, no through hole.</summary>
        JetCrater,
        /// <summary>Soot and burnt paint from the charge's blast.</summary>
        Scorch,
        /// <summary>Fragment pitting and chipped paint.</summary>
        Pocks,
        /// <summary>An explosive reactive armour block that went off.</summary>
        EraBlast
    }

    public struct SurfaceMark
    {
        public Vector3 point;
        public Vector3 normal;
        public MarkKind kind;
        /// <summary>Part the mark sticks to (it follows a turret or a moving vehicle).</summary>
        public Transform surface;
        public VehicleDamageModel owner;
    }

    public struct Trace
    {
        public Vector3 from;
        public Vector3 to;
        public TraceKind kind;
    }

    public struct ModuleHit
    {
        public VehicleModule module;
        public float damage;
        public ModuleState before;
        public ModuleState after;
    }

    /// <summary>Everything a detonation did: rays for the x-ray view, module damage and resulting kill states.</summary>
    public sealed class StrikeReport
    {
        public readonly WarheadData warhead;
        public readonly Vector3 point;
        public readonly Vector3 direction;
        public readonly float time;

        public readonly List<Trace> traces = new List<Trace>();
        public readonly List<ModuleHit> hits = new List<ModuleHit>();
        public readonly List<SurfaceMark> marks = new List<SurfaceMark>();
        public readonly Dictionary<VehicleDamageModel, KillState> stateBefore = new Dictionary<VehicleDamageModel, KillState>();
        /// <summary>Where each vehicle's incident log stood when this strike reached it.</summary>
        readonly Dictionary<VehicleDamageModel, int> logStart = new Dictionary<VehicleDamageModel, int>();

        /// <summary>Armor element that took the impact (first plate / block on the jet line).</summary>
        public ArmorElement impactElement;
        public bool armorPenetrated;
        /// <summary>A screen crushed the fuze: the charge never fired.</summary>
        public bool dud;

        public StrikeReport(WarheadData warhead, Vector3 point, Vector3 direction)
        {
            this.warhead = warhead;
            this.point = point;
            this.direction = direction;
            time = Time.time;
        }

        internal void Track(VehicleDamageModel vehicle)
        {
            if (vehicle == null) return;
            vehicle.RegisterHit();
            if (!stateBefore.ContainsKey(vehicle))
            {
                stateBefore[vehicle] = vehicle.State;
                logStart[vehicle] = vehicle.Log.Count;
            }
        }

        internal void AddHit(VehicleModule module, float damage, HitKind kind)
        {
            Track(module.Owner);
            var before = module.State;
            module.ApplyDamage(damage, kind);
            hits.Add(new ModuleHit { module = module, damage = damage, before = before, after = module.State });
        }

        public bool HitDecoy
        {
            get
            {
                foreach (var v in stateBefore.Keys) if (v != null && v.IsDecoy) return true;
                return false;
            }
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            if (dud) return "Несработка: решётка смяла взрыватель";
            if (stateBefore.Count == 0)
                return armorPenetrated ? "Пробитие, модули не задеты" : "Нет поражения";

            foreach (var kv in stateBefore)
            {
                var vehicle = kv.Key;
                if (vehicle.IsDecoy)
                {
                    sb.Append(vehicle.DisplayName).AppendLine(": ЛОЖНАЯ ЦЕЛЬ (надувной макет)");
                    continue;
                }
                sb.Append(vehicle.DisplayName).Append(": ").Append(DamageNames.Ru(vehicle.State));
                bool anyModuleHit = hits.Exists(h => h.module.Owner == vehicle && h.after != h.before);
                if (vehicle.State == kv.Value && vehicle.State == KillState.None && !anyModuleHit)
                    sb.Append(armorPenetrated ? " (пробитие)" : " (не пробито)");
                sb.AppendLine();
            }

            // Worst outcome per module.
            var seen = new Dictionary<VehicleModule, ModuleState>();
            foreach (var h in hits)
                if (!seen.TryGetValue(h.module, out var s) || h.after > s)
                    seen[h.module] = h.after;
            foreach (var kv in seen)
                if (kv.Value != ModuleState.Operational)
                    sb.Append("  ").Append(DamageNames.Ru(kv.Key.Type)).Append(" — ").Append(DamageNames.Ru(kv.Value)).AppendLine();

            // Fires, leaks and explosions set off by this strike (the list grows while the vehicle burns).
            foreach (var kv in logStart)
            {
                if (kv.Key == null) continue;
                var log = kv.Key.Log;
                for (int i = kv.Value; i < log.Count && i < kv.Value + 8; i++)
                    sb.Append("  • ").Append(log[i]).AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
    }
}
