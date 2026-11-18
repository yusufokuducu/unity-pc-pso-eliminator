using NUnit.Framework;
using UnityEngine;

namespace YusufOkuducu.PcPsoEliminator.Tests
{
    public sealed class PsoEliminatorTests
    {
        [Test]
        public void RuntimeHitchDetector_FlagsSevereFrameSpike()
        {
            var go = new GameObject("TestOrchestrator");
            try
            {
                var orchestrator = go.AddComponent<AsyncPsoWarmupOrchestrator>();

                // Feed 30 steady 8.3ms frames (120 FPS)
                for (int i = 0; i < 30; i++)
                {
                    bool hitched = orchestrator.DetectRuntimePsoHitch(8.3f, i);
                    Assert.IsFalse(hitched);
                }

                // Feed a 45ms PSO compilation stutter frame
                bool spikeDetected = orchestrator.DetectRuntimePsoHitch(45.0f, 31);
                Assert.IsTrue(spikeDetected);
                Assert.AreEqual(1, orchestrator.DetectedHitches.Count);
                Assert.Greater(orchestrator.DetectedHitches[0].spikeRatio, 4.0f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
