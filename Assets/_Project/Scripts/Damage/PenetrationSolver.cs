using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>One element crossed by the jet, in order of distance from the impact point.</summary>
    public struct PenetrationLayer
    {
        public ArmorKind kind;
        /// <summary>Plate / module: armor points. Reactive: fraction of penetration removed (0..1). Screen: disrupt chance (0..1).</summary>
        public float value;
        /// <summary>Cosine between jet direction and surface normal (1 = head-on).</summary>
        public float cosAngle;
        /// <summary>Distance from the first contact, m.</summary>
        public float distance;
    }

    public struct LayerResult
    {
        public float penetrationIn;
        public float penetrationOut;
        public float effectiveArmor;
        /// <summary>The jet reached this layer.</summary>
        public bool reached;
        /// <summary>The jet continued past this layer.</summary>
        public bool passed;
        /// <summary>Reactive block fired or screen disrupted the charge.</summary>
        public bool triggered;
    }

    /// <summary>Pure penetration rules for a shaped-charge jet crossing ordered layers.</summary>
    public static class PenetrationSolver
    {
        /// <summary>Angles beyond ~80 degrees are capped so grazing hits do not produce infinite armor.</summary>
        public const float MinCos = 0.17f;

        public static float EffectiveArmor(float armor, float cosAngle) => armor / Mathf.Max(Mathf.Abs(cosAngle), MinCos);

        /// <returns>Index of the last layer the jet passed, or -1.</returns>
        public static int Resolve(WarheadData w, IReadOnlyList<PenetrationLayer> layers, Func<double> random, List<LayerResult> results)
        {
            results.Clear();
            float pen = w.penetration;
            float firstDistance = layers.Count > 0 ? layers[0].distance : 0f;
            int lastPassed = -1;
            bool stopped = false;

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                var r = new LayerResult();

                float travelled = layer.distance - firstDistance;
                if (!stopped)
                {
                    pen -= w.decayPerMeter * travelled;
                    firstDistance = layer.distance;
                    if (pen <= 0f || layer.distance > w.jetMaxLength + layers[0].distance)
                        stopped = true;
                }

                if (stopped)
                {
                    results.Add(r);
                    continue;
                }

                r.reached = true;
                r.penetrationIn = pen;

                switch (layer.kind)
                {
                    case ArmorKind.Screen:
                        if (random() < layer.value)
                        {
                            pen *= w.screenDisruptedFactor;
                            r.triggered = true;
                        }
                        break;

                    case ArmorKind.Reactive:
                        r.triggered = true;
                        if (!w.tandem)
                            pen *= 1f - Mathf.Clamp01(layer.value);
                        break;

                    case ArmorKind.Plate:
                    case ArmorKind.Module:
                        r.effectiveArmor = EffectiveArmor(layer.value, layer.cosAngle);
                        pen -= r.effectiveArmor;
                        break;
                }

                if (pen > 0f)
                {
                    r.passed = true;
                    lastPassed = i;
                }
                else
                {
                    stopped = true;
                    pen = 0f;
                }
                r.penetrationOut = pen;
                results.Add(r);
            }
            return lastPassed;
        }
    }
}
