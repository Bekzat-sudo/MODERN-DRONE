using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    public enum CompartmentKind
    {
        /// <summary>Crew and ammunition under armour (fighting compartment, turret basket).</summary>
        Fighting,
        /// <summary>Engine and transmission bay: grilles over it are always open.</summary>
        Engine,
        /// <summary>Driver's or troop compartment.</summary>
        Crew,
        /// <summary>Cab of a soft-skinned vehicle.</summary>
        Cab,
        /// <summary>Cargo bed, launcher pack, equipment body.</summary>
        Cargo,
        /// <summary>Fuel tank outside the hull (open to the air).</summary>
        Fuel,
        /// <summary>Anything open to the air (gun platform, open bed).</summary>
        Open
    }

    /// <summary>A volume inside the vehicle, in the profile's local space; modules and openings belong to compartments.</summary>
    [Serializable]
    public sealed class Compartment
    {
        public string name = "Fighting";
        public CompartmentKind kind;
        public Vector3 center;
        public Vector3 size = Vector3.one;
        [Tooltip("Compartments it opens into (no bulkhead in between).")]
        public int[] connected = new int[0];

        public Bounds Bounds => new Bounds(center, size);
    }

    /// <summary>
    /// How a vehicle burns and blows up, described once on its prefab root and independent of the model's geometry and
    /// node names: its compartments (local volumes), the turret and rotor (references, not names), and through its
    /// children the openings (<see cref="FireVent"/>, each linked to a compartment) and hatches (<see cref="Hatch"/>, whose
    /// lids are references to model parts). A fire burns out of the openings of its compartment and of those connected
    /// to it; a deflagration or detonation breaches the bulkheads around it. Built by the vehicle builders (Editor
    /// VehicleKit), checked by <see cref="VehicleFxSetup.Validate"/>, edited with its inspector and gizmos.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleFxProfile : MonoBehaviour
    {
        [Tooltip("Thrown off by an ammunition detonation (its hatches and openings go with it).")]
        public Transform turret;
        [Tooltip("Main rotor (helicopters).")]
        public Transform rotor;
        [Tooltip("Upward kick of the turret toss at a T-72-sized detonation, m/s (abstract).")]
        public float turretTossSpeed = 9f;
        public Compartment[] compartments = new Compartment[0];

        readonly HashSet<int> _breaches = new HashSet<int>();
        readonly Queue<int> _queue = new Queue<int>();
        readonly HashSet<int> _seen = new HashSet<int>();

        /// <summary>Compartment containing <paramref name="world"/>, else the nearest within 0.5 m (-1 if none).</summary>
        public int CompartmentAt(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world);
            int best = -1;
            float bestD = 0.25f;
            for (int i = 0; i < compartments.Length; i++)
            {
                var b = compartments[i].Bounds;
                if (b.Contains(p)) return i;
                float d = b.SqrDistance(p);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Whether fire and pressure pass between two compartments (directly, through others, or through breaches).</summary>
        public bool Connected(int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return true;
            _queue.Clear();
            _seen.Clear();
            _queue.Enqueue(a);
            _seen.Add(a);
            while (_queue.Count > 0)
            {
                int c = _queue.Dequeue();
                for (int n = 0; n < compartments.Length; n++)
                {
                    if (_seen.Contains(n) || !Adjacent(c, n)) continue;
                    if (n == b) return true;
                    _seen.Add(n);
                    _queue.Enqueue(n);
                }
            }
            return false;
        }

        bool Adjacent(int a, int b)
        {
            if (_breaches.Contains(Pair(a, b))) return true;
            if (a < compartments.Length && Array.IndexOf(compartments[a].connected, b) >= 0) return true;
            return b < compartments.Length && Array.IndexOf(compartments[b].connected, a) >= 0;
        }

        static int Pair(int a, int b) => Mathf.Min(a, b) * 1024 + Mathf.Max(a, b);

        /// <summary>Opens the bulkhead between two compartments (pressure, a detonation).</summary>
        public void Breach(int a, int b)
        {
            if (a >= 0 && b >= 0 && a != b) _breaches.Add(Pair(a, b));
        }

        /// <summary>Breaches every bulkhead of <paramref name="c"/> towards compartments touching it (within <paramref name="gap"/> m).</summary>
        public void BreachAround(int c, float gap = 0.4f)
        {
            if (c < 0 || c >= compartments.Length) return;
            var b = compartments[c].Bounds;
            b.Expand(gap * 2f);
            for (int i = 0; i < compartments.Length; i++)
                if (i != c && b.Intersects(compartments[i].Bounds)) Breach(c, i);
        }

        /// <summary>Forgets runtime breaches (pooled or reset vehicles).</summary>
        public void ClearBreaches() => _breaches.Clear();

        static readonly Color[] KindColors =
        {
            new Color(1f, 0.35f, 0.2f), new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.8f, 1f), new Color(0.4f, 0.9f, 0.5f),
            new Color(0.75f, 0.55f, 1f), new Color(1f, 0.5f, 0.8f), new Color(0.8f, 0.8f, 0.8f)
        };

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int i = 0; i < compartments.Length; i++)
            {
                var c = compartments[i];
                var col = KindColors[(int)c.kind % KindColors.Length];
                Gizmos.color = new Color(col.r, col.g, col.b, 0.08f);
                Gizmos.DrawCube(c.center, c.size);
                Gizmos.color = col;
                Gizmos.DrawWireCube(c.center, c.size);
                foreach (int n in c.connected)
                    if (n >= 0 && n < compartments.Length) Gizmos.DrawLine(c.center, compartments[n].center);
            }
            Gizmos.matrix = Matrix4x4.identity;
            if (turret != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(turret.position, 0.3f);
            }
        }
    }
}
