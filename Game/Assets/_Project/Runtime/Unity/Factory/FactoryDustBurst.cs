using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// The puff of dust every breakable component shares, tinted from whatever was
    /// broken.
    ///
    /// Dust rather than chips, which is a different animal from flying debris: the
    /// particles are soft billboards, they are slowed by drag instead of thrown
    /// ballistically, and they spread and fade rather than falling. Getting that
    /// reading right is mostly the velocity limiter and the alpha ramp, not the
    /// particle count.
    ///
    /// Both the sprite and the system are generated in code, so the effect carries no
    /// asset, no .meta and no scene reference: nothing to wire up, nothing that can go
    /// missing from a build.
    /// </summary>
    public static class FactoryDustBurst
    {
        private const int ParticleCount = 20;
        private const float LifetimeSeconds = 1.45f;

        private static Texture2D _dustTexture;
        private static Material _dustMaterial;

        public static void Play(Vector3 position, Color tint)
        {
            var host = new GameObject("FX_DustBurst");
            host.transform.position = position;

            // Built while deactivated so Awake never runs mid-configuration. A
            // ParticleSystem added to a live object starts playing at once, and Unity
            // refuses to have its duration set while it plays.
            host.SetActive(false);

            var system = host.AddComponent<ParticleSystem>();
            ConfigureMain(system, tint);
            ConfigureBurst(system);
            ConfigureMotion(system);
            ConfigureAppearance(system);
            ConfigureRenderer(system);

            host.SetActive(true);
            system.Play();
            Object.Destroy(host, LifetimeSeconds * 2f);
        }

        private static void ConfigureMain(ParticleSystem system, Color tint)
        {
            var main = system.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, LifetimeSeconds);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 1.25f);

            // Puffs start broad. Small particles read as sparks or grit; dust needs
            // overlapping soft blobs to look like a cloud rather than a scatter.
            main.startSize = new ParticleSystem.MinMaxCurve(0.20f, 0.48f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                DustTint(tint, 0.55f),
                DustTint(tint, 0.28f));

            // Barely any gravity. Dust hangs in the air and settles; anything heavier
            // immediately looks like falling rubble.
            main.gravityModifier = 0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = ParticleCount;
        }

        private static void ConfigureBurst(ParticleSystem system)
        {
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, ParticleCount),
            });

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.24f;
        }

        private static void ConfigureMotion(ParticleSystem system)
        {
            // Air drag is what makes this read as dust. Without the dampening the
            // particles keep their launch speed and look like thrown fragments.
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(0.55f);
            limit.dampen = 0.4f;

            // A gentle upward bias so the cloud blooms off the broken surface instead
            // of collapsing straight down.
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // X, Y and Z must use the same curve mode in Unity. X and Z are
            // constants, so keep Y constant too instead of using TwoConstants.
            velocity.y = new ParticleSystem.MinMaxCurve(0.27f);

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.1f, 1.1f);
        }

        private static void ConfigureAppearance(ParticleSystem system)
        {
            // Expanding as it thins is the other half of the dust reading: a cloud
            // spreads out while it disperses, it does not shrink away.
            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.5f));

            var colourOverLifetime = system.colorOverLifetime;
            colourOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    // A quick rise, then a long fade: the puff appears at once and
                    // lingers on the way out.
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0f, 1f),
                });
            colourOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void ConfigureRenderer(ParticleSystem system)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            var material = DustMaterial();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// One shared transparent material for every puff. Shared rather than per-burst
        /// so breaking things in a row does not leak a material each time.
        /// </summary>
        private static Material DustMaterial()
        {
            if (_dustMaterial != null)
            {
                return _dustMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Particles/Lit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return null;
            }

            _dustMaterial = new Material(shader) { name = "FX_DustPuff" };

            // URP decides transparency from these, not from the shader alone. Left at
            // the defaults the puff renders as an opaque square.
            SetIfPresent(_dustMaterial, "_Surface", 1f);
            SetIfPresent(_dustMaterial, "_Blend", 0f);
            SetIfPresent(_dustMaterial, "_ZWrite", 0f);
            SetIfPresent(_dustMaterial, "_AlphaClip", 0f);
            SetIfPresent(_dustMaterial, "_Cull", 0f);
            _dustMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _dustMaterial.DisableKeyword("_ALPHATEST_ON");
            _dustMaterial.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.Transparent;

            var texture = DustTexture();
            if (_dustMaterial.HasProperty("_BaseMap"))
            {
                _dustMaterial.SetTexture("_BaseMap", texture);
            }

            if (_dustMaterial.HasProperty("_MainTex"))
            {
                _dustMaterial.SetTexture("_MainTex", texture);
            }

            return _dustMaterial;
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        /// <summary>
        /// A soft round sprite with no edge to it. A hard disc reads as a bubble; the
        /// smoothstep falloff is what makes overlapping particles merge into a cloud.
        /// </summary>
        private static Texture2D DustTexture()
        {
            if (_dustTexture != null)
            {
                return _dustTexture;
            }

            const int size = 64;
            _dustTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FX_DustPuff",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var centre = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - centre) / centre;
                    var dy = (y - centre) / centre;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = Mathf.Clamp01(1f - distance);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    pixels[y * size + x] =
                        new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            _dustTexture.SetPixels32(pixels);
            _dustTexture.Apply(false, false);
            return _dustTexture;
        }

        /// <summary>
        /// Dust is the pale, desaturated ghost of what it came off, never the material
        /// colour itself: full-strength paint reads as confetti.
        /// </summary>
        private static Color DustTint(Color source, float whiteness)
        {
            var pale = Color.Lerp(source, new Color(0.94f, 0.92f, 0.87f), whiteness);
            var grey = pale.grayscale;
            return new Color(
                Mathf.Lerp(pale.r, grey, 0.35f),
                Mathf.Lerp(pale.g, grey, 0.35f),
                Mathf.Lerp(pale.b, grey, 0.35f),
                1f);
        }
    }
}
