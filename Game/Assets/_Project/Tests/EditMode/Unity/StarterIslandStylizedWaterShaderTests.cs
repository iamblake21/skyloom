using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Tests.Unity
{
    public sealed class StarterIslandStylizedWaterShaderTests
    {
        private const string ShaderName =
            "CML/Environment/Starter Island Stylized Water";
        private const string ShaderPath =
            "Assets/_Project/Art/Environment/StarterIsland/Shaders/" +
            "StarterIslandStylizedWater.shader";
        private const string MaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Materials/" +
            "M_StarterIsland_TerrainWater.mat";

        private static readonly string[] RequiredProperties =
        {
            "_ShallowColor",
            "_DeepColor",
            "_FoamColor",
            "_DepthRange",
            "_FoamDistance",
            "_WaveStrength",
            "_RefractionStrength",
            "_ReflectionStrength",
            "_Opacity",
            "_AmbientStrength",
            "_TransmissionStrength",
            "_CrestStrength",
            "_FoamIntensity"
        };

        [Test]
        public void ShaderImportsAsSupportedUrpSinglePassContract()
        {
            var shader = Shader.Find(ShaderName);

            Assert.That(shader, Is.Not.Null, $"Missing shader '{ShaderName}'.");
            Assert.That(
                AssetDatabase.GetAssetPath(shader),
                Is.EqualTo(ShaderPath),
                "Shader.Find resolved a different asset with the same name.");
            Assert.That(shader.isSupported, Is.True);
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                "The imported water shader contains compile errors.");
            Assert.That(shader.passCount, Is.EqualTo(1));

            var probe = new Material(shader);
            try
            {
                Assert.That(
                    probe.GetTag("RenderPipeline", false),
                    Is.EqualTo("UniversalPipeline"));
                Assert.That(
                    probe.GetTag("RenderType", false),
                    Is.EqualTo("Transparent"));
                Assert.That(
                    probe.renderQueue,
                    Is.EqualTo((int)RenderQueue.Transparent));
                Assert.That(probe.FindPass("ForwardWater"), Is.EqualTo(0));

                foreach (var propertyName in RequiredProperties)
                {
                    Assert.That(
                        probe.HasProperty(propertyName),
                        Is.True,
                        $"Water contract is missing '{propertyName}'.");
                }
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void AuthoredMaterialUsesShaderAndKeepsReadableWaterBalance()
        {
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            Assert.That(material, Is.Not.Null, $"Missing {MaterialPath}.");
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(ShaderName));
            Assert.That(material.passCount, Is.EqualTo(1));
            Assert.That(material.FindPass("ForwardWater"), Is.EqualTo(0));
            Assert.That(
                material.renderQueue,
                Is.EqualTo((int)RenderQueue.Transparent));

            foreach (var propertyName in RequiredProperties)
            {
                Assert.That(
                    material.HasProperty(propertyName),
                    Is.True,
                    $"Authored material cannot drive '{propertyName}'.");
            }

            Assert.That(
                material.GetFloat("_RefractionStrength"),
                Is.InRange(0f, 0.03f),
                "Strong screen-space refraction recreates the old ghosting.");
            Assert.That(
                material.GetFloat("_Opacity"),
                Is.InRange(0.75f, 0.95f));
            Assert.That(
                material.GetFloat("_TransmissionStrength"),
                Is.InRange(0.4f, 0.8f));
            Assert.That(
                material.GetFloat("_AmbientStrength"),
                Is.InRange(0.8f, 1.35f),
                "The water needs an ambient floor during the night cycle.");
            Assert.That(
                material.GetFloat("_ReflectionStrength"),
                Is.InRange(0.35f, 0.9f));
            Assert.That(
                material.GetFloat("_CrestStrength"),
                Is.InRange(0.08f, 0.35f));
            Assert.That(
                material.GetFloat("_FoamIntensity"),
                Is.InRange(0.7f, 1.3f));

            var shallow = material.GetColor("_ShallowColor");
            var deep = material.GetColor("_DeepColor");
            var foam = material.GetColor("_FoamColor");
            Assert.That(shallow.grayscale, Is.GreaterThan(deep.grayscale));
            Assert.That(foam.grayscale, Is.GreaterThan(shallow.grayscale));
            Assert.That(shallow.b, Is.GreaterThan(shallow.r));
            Assert.That(deep.b, Is.GreaterThan(deep.r));
        }

        [Test]
        public void ForwardPassOverwritesOneCompositedResultAndWritesDepth()
        {
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.That(material, Is.Not.Null);

            Assert.That(
                material.HasProperty("_SrcBlend") &&
                material.HasProperty("_DstBlend") &&
                material.HasProperty("_ZWrite") &&
                material.HasProperty("_Cull"),
                Is.True,
                "Fixed pass state must remain inspectable on the authored " +
                "material so the compositing contract cannot regress silently.");
            Assert.That(
                material.GetInt("_SrcBlend"),
                Is.EqualTo((int)BlendMode.One));
            Assert.That(
                material.GetInt("_DstBlend"),
                Is.EqualTo((int)BlendMode.Zero));
            Assert.That(material.GetInt("_ZWrite"), Is.EqualTo(1));
            Assert.That(
                material.GetInt("_Cull"),
                Is.EqualTo((int)CullMode.Back));
        }
    }
}
