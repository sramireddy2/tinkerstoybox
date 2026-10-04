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
            // ART_BIBLE 3.8: each of the game's five shaders ships through a template material under
            // Resources; the plain look keeps URP Lit on a sixth.
            AssertTemplate("ToyLit", "Toybox/ToyLit");
            AssertTemplate("RoomLit", "Toybox/RoomLit");
            AssertTemplate("Sticker", "Toybox/Sticker");
            AssertTemplate("Flat", "Toybox/Flat");
            AssertTemplate("MacroBand", "Toybox/MacroBand");
            AssertTemplate("PlainLit", "Universal Render Pipeline/Lit");
        }

        static void AssertTemplate(string name, string shader)
        {
            var material = Resources.Load<Material>("Materials/" + name);
            Assert.IsNotNull(material, "Resources/Materials/" + name + " is missing - run ProjectSetup");
            Assert.IsNotNull(material.shader, name + " has no shader");
            Assert.AreEqual(shader, material.shader.name, "Resources/Materials/" + name);
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
