using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    public enum VentKind
    {
        /// <summary>A crew hatch: closed until pressure blows it open or it is torn off.</summary>
        Hatch,
        /// <summary>The turret ring: a 2 m hole once the turret is gone.</summary>
        TurretRing,
        /// <summary>Engine-deck louvres and grilles: always open to the engine bay.</summary>
        Grille,
        /// <summary>A hole punched by a shaped-charge jet.</summary>
        Hole,
        /// <summary>Seams and gaps of a closed hull: only smoke seeps out.</summary>
        Seam,
        /// <summary>Open cab or cargo bed of a soft-skinned vehicle.</summary>
        Open
    }

    /// <summary>
    /// An opening a fire inside the vehicle can burn out of. Flames are drawn here, outside the hull, never at the
    /// burning module itself (inside the armour). Position = centre of the opening, up = its outward normal.
    /// </summary>
    public sealed class FireVent : MonoBehaviour
    {
        [SerializeField] VentKind kind = VentKind.Hatch;
        [Tooltip("Area of the opening, m^2.")]
        [SerializeField] float area = 0.3f;
        [SerializeField] Hatch hatch;
        [Tooltip("Dark plate inside the opening that glows while the vehicle burns.")]
        [SerializeField] Renderer glow;

        bool _forcedOpen;

        public VentKind Kind => kind;
        public float Area => area;
        public Hatch Hatch => hatch;
        public Renderer Glow => glow;

        public bool Open => _forcedOpen || kind switch
        {
            VentKind.Hatch => hatch == null || hatch.IsOpen,
            VentKind.TurretRing => false,
            VentKind.Seam => false,
            _ => true,
        };

        public void Configure(VentKind ventKind, float areaM2, Hatch lid = null, Renderer glowPlate = null)
        {
            kind = ventKind;
            area = areaM2;
            hatch = lid;
            glow = glowPlate;
        }

        /// <summary>Opens a vent that is shut by default (the turret ring when the turret is thrown off).</summary>
        public void ForceOpen() => _forcedOpen = true;

        public VentPicker.Vent Info => new VentPicker.Vent
        {
            position = transform.position, normal = transform.up, area = area, open = Open, kind = kind
        };
    }

    /// <summary>
    /// Where a fire inside a vehicle shows: flames leave through open hatches, the turret ring, engine grilles and holes
    /// punched by the jet, shared by size and nearness (a big opening close to the fire takes most of it); with every
    /// opening shut only smoke seeps from the seams. Burning propellant raises the pressure in a closed hull until hatches
    /// blow open, and the flame then roars out of them for seconds (T-72 / T-90 cook-off footage: flame jets from the
    /// commander's and gunner's hatches; en.wikipedia.org/wiki/Cooking_off). Plain math, unit-tested.
    /// </summary>
    public static class VentPicker
    {
        public struct Vent
        {
            public Vector3 position, normal;
            public float area;
            public bool open;
            public VentKind kind;
        }

        /// <summary>Hatches within this distance of a propellant fire are blown open by its pressure (abstract).</summary>
        public const float PressureReach = 3.5f;

        /// <summary>Weight of an opening for a fire at distance <paramref name="distance"/>: area over distance squared.</summary>
        public static float Weight(float area, float distance) => area / ((distance + 0.5f) * (distance + 0.5f));

        /// <summary>
        /// Up to <paramref name="max"/> open vents with their share of the flames (shares sum to 1), largest first;
        /// empty when every opening is shut.
        /// </summary>
        public static void Share(Vector3 source, IReadOnlyList<Vent> vents, List<(int index, float share)> result, int max = 3)
        {
            result.Clear();
            float total = 0f;
            for (int i = 0; i < vents.Count; i++)
            {
                if (!vents[i].open || vents[i].kind == VentKind.Seam) continue;
                float w = Weight(vents[i].area, Vector3.Distance(source, vents[i].position));
                result.Add((i, w));
                total += w;
            }
            result.Sort((a, b) => b.share.CompareTo(a.share));
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
            // Drop openings that would get only a token flame.
            total = 0f;
            foreach (var r in result) total += r.share;
            for (int i = result.Count - 1; i > 0; i--)
                if (result[i].share / total < 0.08f) { total -= result[i].share; result.RemoveAt(i); }
            for (int i = 0; i < result.Count; i++) result[i] = (result[i].index, result[i].share / total);
        }

        /// <summary>The nearest open vent for a fire at <paramref name="source"/> (-1 if all are shut).</summary>
        public static int Nearest(Vector3 source, IReadOnlyList<Vent> vents)
        {
            var list = new List<(int, float)>();
            Share(source, vents, list, 1);
            return list.Count > 0 ? list[0].Item1 : -1;
        }

        /// <summary>Where smoke seeps out when every opening is shut: the seam (or any vent) nearest the fire.</summary>
        public static int Seep(Vector3 source, IReadOnlyList<Vent> vents)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < vents.Count; i++)
            {
                float d = Vector3.Distance(source, vents[i].position) * (vents[i].kind == VentKind.Seam ? 1f : 1.5f);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Closed hatches a violent (propellant) fire at <paramref name="source"/> blows open.</summary>
        public static void Blown(Vector3 source, IReadOnlyList<Vent> vents, List<int> result, float reach = PressureReach)
        {
            result.Clear();
            for (int i = 0; i < vents.Count; i++)
                if (vents[i].kind == VentKind.Hatch && !vents[i].open && Vector3.Distance(source, vents[i].position) <= reach)
                    result.Add(i);
        }
    }
}
