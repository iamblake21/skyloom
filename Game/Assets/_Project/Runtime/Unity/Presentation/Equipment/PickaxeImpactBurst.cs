using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Unity.Presentation.Equipment
{
    public enum PickaxeImpactSurface : byte
    {
        Stone = 0,
        Wood = 1,
    }

    /// <summary>
    /// Procedural impact VFX made entirely from three-dimensional particle
    /// meshes. Stone throws a dense smoke puff and mineral chips; wood throws
    /// a lighter sawdust puff and long, tumbling splinters. No billboard quad
    /// is used by either material family.
    /// </summary>
    public static class PickaxeImpactBurst
    {
        public const string StoneObjectName =
            "FX_PickaxeImpact_Stone";
        public const string WoodObjectName =
            "FX_PickaxeImpact_Wood";

        private const string SmokeShaderResource =
            "ImpactSmokeMesh";
        private const string FragmentShaderResource =
            "ImpactFragmentMesh";
        private const int StoneSmokeCount = 9;
        private const int WoodSmokeCount = 5;
        private const int StoneFragmentCount = 7;
        private const int WoodSplinterCount = 9;
        private const float SurfaceOffset = 0.018f;
        private const float EffectLifetime = 1.45f;

        private static readonly Color StoneSmokeTint =
            new Color(0.86f, 0.82f, 0.74f, 0.78f);
        private static readonly Color WoodDustTint =
            new Color(0.90f, 0.59f, 0.33f, 0.58f);
        private static readonly Color StoneFragmentTint =
            new Color(0.68f, 0.64f, 0.56f, 1f);
        private static readonly Color WoodSplinterTint =
            new Color(0.78f, 0.43f, 0.20f, 1f);

        private static Mesh _smokePuffMesh;
        private static Mesh _stoneFragmentMesh;
        private static Mesh _woodSplinterMesh;
        private static Material _stoneSmokeMaterial;
        private static Material _woodDustMaterial;
        private static Material _stoneFragmentMaterial;
        private static Material _woodSplinterMaterial;

        /// <summary>
        /// The irregular puff mesh, shared with any other stylized smoke in the
        /// game. Exposed rather than copied so there is one shape to change.
        /// </summary>
        internal static Mesh SharedSmokePuffMesh()
        {
            return SmokePuffMesh();
        }

        /// <summary>The project's smoke shader, with its runtime fallbacks.</summary>
        internal static Shader SharedSmokeShader()
        {
            return Resources.Load<Shader>(SmokeShaderResource)
                ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default");
        }

        /// <summary>
        /// Applies the blend state the smoke shader expects, or a sane
        /// transparent setup when a fallback shader is in use.
        /// </summary>
        internal static void ApplySharedSmokeTransparency(Material material)
        {
            ConfigureFallbackTransparency(material);
        }

        internal static void SetSmokeFloat(
            Material material,
            string property,
            float value)
        {
            SetIfPresent(material, property, value);
        }

        internal static void SetSmokeColor(
            Material material,
            string property,
            Color value)
        {
            SetColorIfPresent(material, property, value);
        }

        public static void Play(
            RaycastHit hit,
            PickaxeImpactSurface surface)
        {
            if (hit.collider != null)
            {
                Play(hit.point, hit.normal, surface);
            }
        }

        public static void Play(
            Vector3 position,
            Vector3 surfaceNormal,
            PickaxeImpactSurface surface)
        {
            var normal = surfaceNormal.sqrMagnitude > 0.0001f
                ? surfaceNormal.normalized
                : Vector3.up;
            var host = new GameObject(ObjectName(surface));
            host.transform.SetPositionAndRotation(
                position + normal * SurfaceOffset,
                SurfaceRotation(normal));
            host.SetActive(false);

            var smoke = host.AddComponent<ParticleSystem>();
            ConfigureSmoke(smoke, surface);

            var fragmentHost = new GameObject(
                surface == PickaxeImpactSurface.Stone
                    ? "FX_StoneFragments"
                    : "FX_WoodSplinters");
            fragmentHost.transform.SetParent(host.transform, false);
            var fragments = fragmentHost.AddComponent<ParticleSystem>();
            ConfigureFragments(fragments, surface);

            host.SetActive(true);
            smoke.Play(true);
            fragments.Play(true);
            if (Application.isPlaying)
            {
                Object.Destroy(host, EffectLifetime);
            }
        }

        private static void ConfigureSmoke(
            ParticleSystem system,
            PickaxeImpactSurface surface)
        {
            ConfigureSmokeMain(system, surface);

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(
                    0f,
                    (short)(surface == PickaxeImpactSurface.Stone
                        ? StoneSmokeCount
                        : WoodSmokeCount)),
            });

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = surface == PickaxeImpactSurface.Stone
                ? 41f
                : 34f;
            shape.radius = 0.028f;
            shape.length = 0.018f;

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(0.42f);
            limit.dampen = 0.48f;

            var noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            noise.frequency = 0.62f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.22f);
            noise.damping = true;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.42f),
                    new Keyframe(0.22f, 0.96f),
                    new Keyframe(1f, 1.78f)));

            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(
                SmokeFade());

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
            rotation.y = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = SmokePuffMesh();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.enableGPUInstancing = false;
            renderer.sharedMaterial = SmokeMaterial(surface);
        }

        private static void ConfigureSmokeMain(
            ParticleSystem system,
            PickaxeImpactSurface surface)
        {
            var main = system.main;
            main.duration = 0.12f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = surface == PickaxeImpactSurface.Stone
                ? new ParticleSystem.MinMaxCurve(0.48f, 0.82f)
                : new ParticleSystem.MinMaxCurve(0.32f, 0.60f);
            main.startSpeed = surface == PickaxeImpactSurface.Stone
                ? new ParticleSystem.MinMaxCurve(0.18f, 0.46f)
                : new ParticleSystem.MinMaxCurve(0.24f, 0.58f);
            main.startSize = surface == PickaxeImpactSurface.Stone
                ? new ParticleSystem.MinMaxCurve(0.13f, 0.25f)
                : new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = surface == PickaxeImpactSurface.Stone
                ? new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.98f, 0.93f, 0.98f),
                    new Color(0.88f, 0.84f, 0.76f, 0.72f))
                : new ParticleSystem.MinMaxGradient(
                    new Color(1f, 1f, 1f, 0.96f),
                    new Color(0.72f, 0.78f, 0.76f, 0.62f));
            main.gravityModifier = surface == PickaxeImpactSurface.Stone
                ? -0.015f
                : 0.035f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = surface == PickaxeImpactSurface.Stone
                ? StoneSmokeCount
                : WoodSmokeCount;
        }

        private static void ConfigureFragments(
            ParticleSystem system,
            PickaxeImpactSurface surface)
        {
            var isStone = surface == PickaxeImpactSurface.Stone;
            var count = isStone
                ? StoneFragmentCount
                : WoodSplinterCount;
            var main = system.main;
            main.duration = 0.10f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = isStone
                ? new ParticleSystem.MinMaxCurve(0.42f, 0.76f)
                : new ParticleSystem.MinMaxCurve(0.48f, 0.92f);
            main.startSpeed = isStone
                ? new ParticleSystem.MinMaxCurve(0.52f, 1.15f)
                : new ParticleSystem.MinMaxCurve(0.68f, 1.42f);
            main.startSize = isStone
                ? new ParticleSystem.MinMaxCurve(0.055f, 0.11f)
                : new ParticleSystem.MinMaxCurve(0.075f, 0.15f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = isStone
                ? new ParticleSystem.MinMaxGradient(
                    new Color(0.84f, 0.81f, 0.74f, 1f),
                    new Color(0.99f, 0.96f, 0.88f, 1f))
                : new ParticleSystem.MinMaxGradient(
                    new Color(0.82f, 0.58f, 0.34f, 1f),
                    new Color(1f, 0.88f, 0.62f, 1f));
            main.gravityModifier = isStone ? 1.10f : 0.82f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)count),
            });

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = isStone ? 46f : 37f;
            shape.radius = 0.022f;
            shape.length = 0.012f;

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-11f, 11f);
            rotation.y = new ParticleSystem.MinMaxCurve(-14f, 14f);
            rotation.z = new ParticleSystem.MinMaxCurve(-9f, 9f);

            // Fragments stay opaque so they read as real solid pieces. A quick
            // end-of-life shrink avoids the hard pop of an alpha-blended mesh.
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.82f),
                    new Keyframe(0.12f, 1f),
                    new Keyframe(0.82f, 1f),
                    new Keyframe(1f, 0.06f)));

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = isStone
                ? StoneFragmentMesh()
                : WoodSplinterMesh();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enableGPUInstancing = false;
            renderer.sharedMaterial = FragmentMaterial(surface);
        }

        private static Material SmokeMaterial(
            PickaxeImpactSurface surface)
        {
            var cached = surface == PickaxeImpactSurface.Stone
                ? _stoneSmokeMaterial
                : _woodDustMaterial;
            if (cached != null)
            {
                return cached;
            }

            var shader = Resources.Load<Shader>(SmokeShaderResource)
                ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return null;
            }

            cached = new Material(shader)
            {
                name = surface == PickaxeImpactSurface.Stone
                    ? "FX_PickaxeImpact_StoneSmoke"
                    : "FX_PickaxeImpact_WoodDust",
            };
            var tint = surface == PickaxeImpactSurface.Stone
                ? StoneSmokeTint
                : WoodDustTint;
            SetColorIfPresent(cached, "_BaseColor", tint);
            SetColorIfPresent(cached, "_Color", tint);
            SetIfPresent(cached, "_EdgeSoftness", 0.44f);
            SetIfPresent(cached, "_NoiseScale", 9f);
            SetIfPresent(
                cached,
                "_FogInfluence",
                surface == PickaxeImpactSurface.Stone ? 0f : 0.35f);
            ConfigureFallbackTransparency(cached);
            if (surface == PickaxeImpactSurface.Stone)
            {
                _stoneSmokeMaterial = cached;
            }
            else
            {
                _woodDustMaterial = cached;
            }

            return cached;
        }

        private static Material FragmentMaterial(
            PickaxeImpactSurface surface)
        {
            var cached = surface == PickaxeImpactSurface.Stone
                ? _stoneFragmentMaterial
                : _woodSplinterMaterial;
            if (cached != null)
            {
                return cached;
            }

            var shader = Resources.Load<Shader>(
                    FragmentShaderResource)
                ?? Shader.Find(
                    "Universal Render Pipeline/Particles/Simple Lit")
                ?? Shader.Find("Universal Render Pipeline/Particles/Lit")
                ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                return null;
            }

            cached = new Material(shader)
            {
                name = surface == PickaxeImpactSurface.Stone
                    ? "FX_PickaxeImpact_StoneFragments"
                    : "FX_PickaxeImpact_WoodSplinters",
            };
            var tint = surface == PickaxeImpactSurface.Stone
                ? StoneFragmentTint
                : WoodSplinterTint;
            SetColorIfPresent(cached, "_BaseColor", tint);
            SetColorIfPresent(cached, "_Color", tint);
            SetIfPresent(cached, "_Metallic", 0f);
            SetIfPresent(cached, "_Smoothness", 0.12f);
            SetIfPresent(
                cached,
                "_FogInfluence",
                surface == PickaxeImpactSurface.Stone ? 0f : 0.35f);
            SetIfPresent(cached, "_Surface", 0f);
            SetIfPresent(cached, "_ZWrite", 1f);
            SetIfPresent(cached, "_Cull", 0f);
            SetIfPresent(
                cached,
                "_SrcBlend",
                (float)BlendMode.One);
            SetIfPresent(
                cached,
                "_DstBlend",
                (float)BlendMode.Zero);
            SetIfPresent(
                cached,
                "_SrcBlendAlpha",
                (float)BlendMode.One);
            SetIfPresent(
                cached,
                "_DstBlendAlpha",
                (float)BlendMode.Zero);
            cached.SetOverrideTag("RenderType", "Opaque");
            cached.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            cached.renderQueue = (int)RenderQueue.Geometry;
            if (surface == PickaxeImpactSurface.Stone)
            {
                _stoneFragmentMaterial = cached;
            }
            else
            {
                _woodSplinterMaterial = cached;
            }

            return cached;
        }

        private static void ConfigureFallbackTransparency(
            Material material)
        {
            if (material.shader != null &&
                material.shader.name == "CML/Effects/Impact Smoke Mesh")
            {
                return;
            }

            SetIfPresent(material, "_Surface", 1f);
            SetIfPresent(material, "_Blend", 0f);
            SetIfPresent(material, "_ZWrite", 0f);
            SetIfPresent(material, "_AlphaClip", 0f);
            SetIfPresent(material, "_Cull", 0f);
            SetIfPresent(
                material,
                "_SrcBlend",
                (float)BlendMode.SrcAlpha);
            SetIfPresent(
                material,
                "_DstBlend",
                (float)BlendMode.OneMinusSrcAlpha);
            SetIfPresent(
                material,
                "_SrcBlendAlpha",
                (float)BlendMode.One);
            SetIfPresent(
                material,
                "_DstBlendAlpha",
                (float)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent + 25;
        }

        private static Mesh SmokePuffMesh()
        {
            if (_smokePuffMesh != null)
            {
                return _smokePuffMesh;
            }

            const int latitudeSegments = 7;
            const int longitudeSegments = 12;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            vertices.Add(new Vector3(0f, 0.50f, 0f));

            for (var latitude = 1;
                 latitude < latitudeSegments;
                 latitude++)
            {
                var phi = Mathf.PI * latitude / latitudeSegments;
                var ringRadius = Mathf.Sin(phi);
                var y = Mathf.Cos(phi);
                for (var longitude = 0;
                     longitude < longitudeSegments;
                     longitude++)
                {
                    var theta = Mathf.PI * 2f * longitude /
                        longitudeSegments;
                    var direction = new Vector3(
                        Mathf.Cos(theta) * ringRadius,
                        y,
                        Mathf.Sin(theta) * ringRadius);
                    var irregularity = 1f
                        + Mathf.Sin(theta * 3f + phi * 2f) * 0.11f
                        + Mathf.Cos(theta * 5f - phi * 3f) * 0.065f;
                    vertices.Add(direction * (0.50f * irregularity));
                }
            }

            var bottomIndex = vertices.Count;
            vertices.Add(new Vector3(0f, -0.50f, 0f));
            for (var longitude = 0;
                 longitude < longitudeSegments;
                 longitude++)
            {
                var next = (longitude + 1) % longitudeSegments;
                triangles.Add(0);
                triangles.Add(1 + longitude);
                triangles.Add(1 + next);
            }

            for (var latitude = 0;
                 latitude < latitudeSegments - 2;
                 latitude++)
            {
                var currentRing = 1 + latitude * longitudeSegments;
                var nextRing = currentRing + longitudeSegments;
                for (var longitude = 0;
                     longitude < longitudeSegments;
                     longitude++)
                {
                    var next = (longitude + 1) % longitudeSegments;
                    triangles.Add(currentRing + longitude);
                    triangles.Add(nextRing + longitude);
                    triangles.Add(nextRing + next);
                    triangles.Add(currentRing + longitude);
                    triangles.Add(nextRing + next);
                    triangles.Add(currentRing + next);
                }
            }

            var finalRing = bottomIndex - longitudeSegments;
            for (var longitude = 0;
                 longitude < longitudeSegments;
                 longitude++)
            {
                var next = (longitude + 1) % longitudeSegments;
                triangles.Add(finalRing + next);
                triangles.Add(finalRing + longitude);
                triangles.Add(bottomIndex);
            }

            _smokePuffMesh = BuildMesh(
                "FX_MESH_ImpactSmokePuff",
                vertices.ToArray(),
                triangles.ToArray());
            return _smokePuffMesh;
        }

        private static Mesh StoneFragmentMesh()
        {
            if (_stoneFragmentMesh == null)
            {
                _stoneFragmentMesh = BuildMesh(
                    "FX_MESH_StoneFragment",
                    new[]
                    {
                        new Vector3(-0.42f, -0.30f, -0.28f),
                        new Vector3(0.48f, -0.22f, -0.18f),
                        new Vector3(0.05f, -0.12f, 0.46f),
                        new Vector3(-0.06f, 0.54f, -0.04f),
                    },
                    new[]
                    {
                        0, 2, 1,
                        0, 1, 3,
                        1, 2, 3,
                        2, 0, 3,
                    });
            }

            return _stoneFragmentMesh;
        }

        private static Mesh WoodSplinterMesh()
        {
            if (_woodSplinterMesh == null)
            {
                _woodSplinterMesh = BuildMesh(
                    "FX_MESH_WoodSplinter",
                    new[]
                    {
                        new Vector3(-0.075f, -0.48f, -0.025f),
                        new Vector3(0.075f, -0.48f, -0.025f),
                        new Vector3(0.060f, -0.48f, 0.025f),
                        new Vector3(-0.060f, -0.48f, 0.025f),
                        new Vector3(0.018f, 0.52f, 0f),
                    },
                    new[]
                    {
                        0, 2, 1,
                        0, 3, 2,
                        0, 1, 4,
                        1, 2, 4,
                        2, 3, 4,
                        3, 0, 4,
                    });
            }

            return _woodSplinterMesh;
        }

        private static Mesh BuildMesh(
            string name,
            Vector3[] vertices,
            int[] triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            for (var index = 0; index < triangles.Length; index += 3)
            {
                var second = triangles[index + 1];
                triangles[index + 1] = triangles[index + 2];
                triangles[index + 2] = second;
            }

            mesh.triangles = triangles;
            var colours = new Color[vertices.Length];
            for (var index = 0; index < colours.Length; index++)
            {
                colours[index] = Color.white;
            }

            mesh.colors = colours;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Gradient SmokeFade()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.58f, 0.46f),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        private static Quaternion SurfaceRotation(Vector3 normal)
        {
            var up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.98f
                ? Vector3.right
                : Vector3.up;
            return Quaternion.LookRotation(normal, up);
        }

        private static string ObjectName(PickaxeImpactSurface surface)
        {
            return surface == PickaxeImpactSurface.Stone
                ? StoneObjectName
                : WoodObjectName;
        }

        private static void SetIfPresent(
            Material material,
            string property,
            float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetColorIfPresent(
            Material material,
            string property,
            Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }
    }
}
