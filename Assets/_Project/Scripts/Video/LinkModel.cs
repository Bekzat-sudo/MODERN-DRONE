using System;
using UnityEngine;

namespace ModernDrone.Video
{
    public enum LinkType
    {
        /// <summary>Analog 5.8 GHz video + separate RC control link: range, line of sight and EW matter.</summary>
        Radio,
        /// <summary>Fiber-optic spool: immune to EW and terrain, limited by cable length.</summary>
        Fiber,
        /// <summary>
        /// Satellite terminal (Starlink, as on the Hornet: thepage.ua 2026): digital video and control with no range
        /// limit and out of reach of the frontline FPV jammers; Starlink's 25-60 ms latency (starlink.com).
        /// </summary>
        Satellite
    }

    [Flags]
    public enum JamBands
    {
        None = 0,
        Video = 1 << 0,
        Control = 1 << 1,
        Both = Video | Control
    }

    /// <summary>Radio link parameters (gameplay values).</summary>
    [Serializable]
    public struct RadioParams
    {
        [Tooltip("Distance where video starts to degrade / is lost, m (clear line of sight).")]
        public float videoRangeStart, videoRangeEnd;
        [Tooltip("Distance where the control link starts to degrade / is lost, m.")]
        public float controlRangeStart, controlRangeEnd;
        [Tooltip("Multiplier per terrain obstruction between operator antenna and drone.")]
        [Range(0f, 1f)] public float terrainBlock;
        [Tooltip("Multiplier per tree / structure obstruction.")]
        [Range(0f, 1f)] public float foliageBlock;
        [Tooltip("Floor for the combined obstruction multiplier.")]
        [Range(0f, 1f)] public float minObstruction;

        public static RadioParams Default => new RadioParams
        {
            videoRangeStart = 1200f,
            videoRangeEnd = 2400f,
            controlRangeStart = 3000f,
            controlRangeEnd = 6000f,
            terrainBlock = 0.3f,
            foliageBlock = 0.8f,
            minObstruction = 0.08f
        };
    }

    /// <summary>One jammer's effect at the drone position.</summary>
    public struct JamSample
    {
        public float strength;        // 0..1 jammer power
        public float radius;          // m
        public float distanceToDrone; // m
        public JamBands bands;
    }

    public struct LinkQuality
    {
        /// <summary>Video quality 0 (no picture) .. 1 (clean).</summary>
        public float video;
        /// <summary>Control link quality 0..1 (shown as LQ %).</summary>
        public float control;
    }

    /// <summary>Pure link-budget rules; no scene access.</summary>
    public static class LinkModel
    {
        /// <summary>Jam contribution of one jammer: full inside 30% of its radius, fading to zero at the edge.</summary>
        public static float JamLevel(in JamSample j)
        {
            if (j.radius <= 0f) return 0f;
            float proximity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f * j.radius, j.radius, j.distanceToDrone));
            return Mathf.Clamp01(j.strength) * proximity;
        }

        /// <summary>Multiplier from obstructions on the operator -> drone line.</summary>
        public static float Obstruction(in RadioParams p, int terrainHits, int foliageHits)
        {
            float m = Mathf.Pow(p.terrainBlock, terrainHits) * Mathf.Pow(p.foliageBlock, foliageHits);
            return terrainHits + foliageHits == 0 ? 1f : Mathf.Max(p.minObstruction, m);
        }

        /// <param name="distance">Operator antenna to drone, m.</param>
        /// <param name="jammers">Jammers affecting the drone.</param>
        public static LinkQuality Radio(in RadioParams p, float distance, int terrainHits, int foliageHits, ReadOnlySpan<JamSample> jammers)
        {
            float obstruction = Obstruction(p, terrainHits, foliageHits);
            float video = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.videoRangeStart, p.videoRangeEnd, distance))) * obstruction;
            // The control link is narrowband and tolerates obstruction better than analog video.
            float control = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.controlRangeStart, p.controlRangeEnd, distance)))
                            * Mathf.Sqrt(obstruction);

            float jamVideo = 0f, jamControl = 0f;
            foreach (var j in jammers)
            {
                float level = JamLevel(j);
                if ((j.bands & JamBands.Video) != 0) jamVideo = 1f - (1f - jamVideo) * (1f - level);
                if ((j.bands & JamBands.Control) != 0) jamControl = 1f - (1f - jamControl) * (1f - level);
            }

            // A weaker received signal (far from the operator) is easier to jam.
            float farFactor = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(0f, 600f, distance));
            video *= 1f - Mathf.Clamp01(jamVideo * farFactor);
            control *= 1f - Mathf.Clamp01(jamControl * farFactor);

            return new LinkQuality { video = Mathf.Clamp01(video), control = Mathf.Clamp01(control) };
        }

        public static LinkQuality Fiber(float cableUsed, float spoolLength) =>
            cableUsed < spoolLength ? new LinkQuality { video = 1f, control = 1f } : default;

        /// <summary>Control latency over a satellite link, s (Starlink 25-60 ms).</summary>
        public const float SatelliteLatency = 0.05f;

        /// <summary>Added control latency from link quality, s (a clean link adds almost nothing).</summary>
        public static float ControlLatency(float controlQuality) =>
            Mathf.Lerp(0.18f, 0.012f, Mathf.SmoothStep(0f, 1f, controlQuality));
    }
}
