using NUnit.Framework;
using UnityEngine;

namespace Toybox.Tests
{
    public class SmokeTests
    {
        [Test]
        public void ProjectSetupProducedPipelineAndShippableMaterial()
        {
            Assert.IsNotNull(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline, "URP asset is not assigned");
            var lit = Resources.Load<Material>("Materials/ToyLit");
            Assert.IsNotNull(lit, "Resources/Materials/ToyLit is missing - run ProjectSetup");
            Assert.AreEqual("Universal Render Pipeline/Lit", lit.shader.name);
        }

        [Test]
        public void PhysicsCanBeSteppedManuallyInEditMode()
        {
            var previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var go = new GameObject("FallingBody");
            try
            {
                go.transform.position = new Vector3(0f, 5f, 0f);
                var body = go.AddComponent<Rigidbody>();
                go.AddComponent<SphereCollider>();
                for (int i = 0; i < 30; i++) Physics.Simulate(1f / 60f);
                Assert.Less(body.position.y, 5f);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Physics.simulationMode = previousMode;
            }
        }
    }
}
