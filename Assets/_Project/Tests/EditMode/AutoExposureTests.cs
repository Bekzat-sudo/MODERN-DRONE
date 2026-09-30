using ModernDrone.Video;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class AutoExposureTests
    {
        [Test]
        public void TargetExposure_BrighterSceneDarkens_AndIsClamped()
        {
            Assert.AreEqual(0.3f, AutoExposure.TargetExposure(0.2f, 0.2f, 0.3f, -1.5f, 0.7f), 1e-4f);
            Assert.AreEqual(0.3f - 1f, AutoExposure.TargetExposure(0.4f, 0.2f, 0.3f, -1.5f, 0.7f), 1e-4f);
            Assert.AreEqual(0.3f + 0.7f, AutoExposure.TargetExposure(0.001f, 0.2f, 0.3f, -1.5f, 0.7f), 1e-4f);
        }

        [Test]
        public void LogAverage_IsGeometricMean_AndCentreWeighted()
        {
            const int w = 8, h = 4;
            using var flat = Pixels(w, h, (x, y) => 0.5f);
            Assert.AreEqual(0.5f, AutoExposure.CentreWeightedLogAverage(flat, w, h), 1e-3f);

            // A bright centre outweighs an equally large bright border.
            using var centre = Pixels(w, h, (x, y) => x >= 2 && x < 6 && y >= 1 && y < 3 ? 4f : 0.25f);
            using var border = Pixels(w, h, (x, y) => x >= 2 && x < 6 && y >= 1 && y < 3 ? 0.25f : 4f);
            Assert.Greater(AutoExposure.CentreWeightedLogAverage(centre, w, h), AutoExposure.CentreWeightedLogAverage(border, w, h));
        }

        static NativeArray<ushort> Pixels(int w, int h, System.Func<int, int, float> lum)
        {
            var a = new NativeArray<ushort>(w * h * 4, Allocator.Temp);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                ushort v = Mathf.FloatToHalf(lum(x, y));
                int i = (y * w + x) * 4;
                a[i] = a[i + 1] = a[i + 2] = v;
                a[i + 3] = Mathf.FloatToHalf(1f);
            }
            return a;
        }
    }
}
