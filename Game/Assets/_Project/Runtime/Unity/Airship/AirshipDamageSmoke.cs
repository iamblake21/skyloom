using System.Collections.Generic;
using CML.Simulation.Airship;
using CML.Unity.Presentation.Equipment;
using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// The visible reason the player has a first problem: the engines sputter
    /// smoke until the hull is airworthy again. Read-only on the authoritative
    /// repair state.
    ///
    /// Built at runtime on the existing instance, so making the wreck visible
    /// never means regenerating or saving the scene.
    ///
    /// It deliberately reuses the mesh, shader and blend state of
    /// <see cref="PickaxeImpactBurst"/>. The game's smoke is irregular
    /// three-dimensional puffs, never a billboard quad with a texture, and a
    /// second visual language for the same substance would read as foreign.
    ///
    /// What makes it say "broken" rather than "running" is the rhythm: a steady
    /// column reads as a working engine, so a constant trickle carries uneven
    /// coughs on top of it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirshipDamageSmoke : MonoBehaviour
    {
        private const string EmitterName = "VFX_AirshipDamageSmoke";
        private const string RotorGroupName = "ANM_PropellerRotor";

        // Same family as the stone impact puff but far darker. The pale tint of
        // the impact dust disappears against a bright sky, which is most of what
        // is behind an airship.
        private static readonly Color SmokeTint =
            new Color(0.30f, 0.28f, 0.27f, 0.92f);
        private static readonly Color BirthColour =
            new Color(0.20f, 0.19f, 0.18f, 1f);
        private static readonly Color DriftColour =
            new Color(0.44f, 0.42f, 0.40f, 0.85f);

        private readonly List<ParticleSystem> _emitters =
            new List<ParticleSystem>();

        private AirshipSimulationBridge _bridge;
        private Transform _hullRoot;
        private bool _needsBuild;
        private Material _material;
        private bool _emitting;

        public bool IsEmitting => _emitting;

        public int EmitterCount => _emitters.Count;

        public void Configure(AirshipSimulationBridge bridge, Transform hullRoot)
        {
            _bridge = bridge;
            _hullRoot = hullRoot != null ? hullRoot : transform;

            // The emitters are built on the first LateUpdate, not here.
            // Configure runs from a composition root's Awake, while the scene is
            // still loading; adding a ParticleSystem there produced modules with
            // no system behind them.
            _needsBuild = true;
        }

        /// <summary>
        /// Matches emission to the committed hull state. Idempotent, so the
        /// caller can poll it without restarting the systems every frame.
        /// </summary>
        public void Refresh()
        {
            if (_emitters.Count == 0)
            {
                return;
            }

            var shouldEmit = _bridge != null && _bridge.IsAwaitingRepair;
            if (shouldEmit == _emitting)
            {
                return;
            }

            _emitting = shouldEmit;
            for (var index = 0; index < _emitters.Count; index++)
            {
                var emitter = _emitters[index];
                if (emitter == null)
                {
                    continue;
                }

                if (shouldEmit)
                {
                    emitter.Play(true);
                    continue;
                }

                // Stop emitting but let the puffs already in the air finish: the
                // smoke should thin out as the repair lands, not cut on a frame.
                emitter.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void LateUpdate()
        {
            if (_needsBuild)
            {
                _needsBuild = false;
                BuildEmitters(_hullRoot != null ? _hullRoot : transform);
            }

            Refresh();
        }

        private void BuildEmitters(Transform hullRoot)
        {
            if (_emitters.Count > 0)
            {
                return;
            }

            var points = ResolveEmissionPoints(hullRoot);
            for (var index = 0; index < points.Count; index++)
            {
                var emitter = BuildEmitter(hullRoot, points[index], index);
                if (emitter != null)
                {
                    _emitters.Add(emitter);
                }
            }

            _emitting = false;
        }

        private ParticleSystem BuildEmitter(
            Transform hullRoot,
            Vector3 worldPoint,
            int index)
        {
            var objectName = EmitterName + "_" + index.ToString();
            var existing = hullRoot.Find(objectName);
            var emitter = existing != null
                ? existing.gameObject
                : new GameObject(objectName);
            if (existing == null)
            {
                // Parented to the hull, never to the rotor itself: the rotor
                // spins, and an emitter riding it would sweep the smoke around
                // with it.
                emitter.transform.SetParent(hullRoot, false);
                emitter.transform.position = worldPoint;
                emitter.transform.localRotation = Quaternion.identity;
            }

            // Unity-aware null check, not `??`. The null-coalescing operator
            // compares real references and so accepts a destroyed component,
            // whose modules then have no system behind them.
            var system = emitter.GetComponent<ParticleSystem>();
            if (system == null)
            {
                system = emitter.AddComponent<ParticleSystem>();
            }

            if (system == null)
            {
                return null;
            }

            // Stop before configuring, not after. A freshly added ParticleSystem
            // has playOnAwake set and is already running, and Unity refuses to
            // let the duration change on a playing system.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ConfigureParticles(system);
            ConfigureRenderer(system.GetComponent<ParticleSystemRenderer>());
            return system;
        }

        /// <summary>
        /// One point per propeller rotor. Positions come from renderer bounds
        /// rather than transforms: the exported parts share the vehicle origin
        /// as their pivot, so using transform positions put the smoke on the
        /// ground under the ship.
        /// </summary>
        private static List<Vector3> ResolveEmissionPoints(Transform hullRoot)
        {
            var points = new List<Vector3>();
            var rotorGroup = FindDescendantNamed(hullRoot, RotorGroupName);
            if (rotorGroup != null)
            {
                for (var index = 0; index < rotorGroup.childCount; index++)
                {
                    if (TryRendererCentre(rotorGroup.GetChild(index), out var centre))
                    {
                        points.Add(centre);
                    }
                }

                if (points.Count == 0
                    && TryRendererCentre(rotorGroup, out var groupCentre))
                {
                    points.Add(groupCentre);
                }
            }

            if (points.Count > 0)
            {
                return points;
            }

            // No rotors in this model: fall back to the engine pods, then to a
            // point above the hull.
            var port = FindDescendantContaining(hullRoot, "Nacelle_Port");
            var starboard = FindDescendantContaining(hullRoot, "Nacelle_Starboard");
            if (port != null && TryRendererCentre(port, out var portCentre))
            {
                points.Add(portCentre);
            }

            if (starboard != null
                && TryRendererCentre(starboard, out var starboardCentre))
            {
                points.Add(starboardCentre);
            }

            if (points.Count == 0)
            {
                points.Add(hullRoot.TransformPoint(ResolveLocalVent(hullRoot)));
            }

            return points;
        }

        private static bool TryRendererCentre(Transform root, out Vector3 centre)
        {
            centre = Vector3.zero;
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            centre = bounds.center;
            return true;
        }

        private static Transform FindDescendantNamed(Transform root, string name)
        {
            var children = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < children.Length; index++)
            {
                if (string.Equals(
                        children[index].name,
                        name,
                        System.StringComparison.Ordinal))
                {
                    return children[index];
                }
            }

            return null;
        }

        private static Transform FindDescendantContaining(
            Transform root,
            string fragment)
        {
            var children = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < children.Length; index++)
            {
                if (children[index].name.IndexOf(
                        fragment,
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return children[index];
                }
            }

            return null;
        }

        /// <summary>
        /// Last-resort vent above the hull, derived from the renderers so it
        /// still follows the real model.
        /// </summary>
        private static Vector3 ResolveLocalVent(Transform hullRoot)
        {
            var renderers = hullRoot.GetComponentsInChildren<MeshRenderer>(false);
            if (renderers.Length == 0)
            {
                return new Vector3(0f, 1.2f, -1.0f);
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            var top = new Vector3(
                bounds.center.x,
                bounds.max.y,
                Mathf.Lerp(bounds.center.z, bounds.min.z, 0.55f));
            return hullRoot.InverseTransformPoint(top);
        }

        private static void ConfigureParticles(ParticleSystem particles)
        {
            var main = particles.main;
            main.duration = 2.4f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);

            // Sized against an airship, not against a pickaxe strike. The first
            // pass borrowed the impact numbers and vanished at this scale.
            main.startSize = new ParticleSystem.MinMaxCurve(0.75f, 1.35f);

            // Full 3D spin, exactly as the impact puffs do. A lumpy sphere that
            // only rolls on Z reads as a flat sticker.
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                BirthColour,
                DriftColour);
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;

            // A constant trickle so there is always something to notice, plus
            // uneven coughs on top. Bursts alone left gaps long enough that the
            // effect was easy to miss entirely; a flat rate alone would read as
            // an engine running normally.
            //
            // Halved from the single-emitter version: there are two of these now.
            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 7f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.00f, (short)4, (short)6, 1, 0.10f),
                new ParticleSystem.Burst(0.85f, (short)3, (short)4, 1, 0.10f),
                new ParticleSystem.Burst(1.70f, (short)5, (short)7, 1, 0.08f),
            });

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.22f;
            shape.length = 0.08f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // Leaves the vent with a kick, then loses it: the same dampened
            // behaviour the impact smoke uses, at a larger scale.
            var limit = particles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(1.9f);
            limit.dampen = 0.24f;

            var noise = particles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            noise.frequency = 0.48f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.24f);
            noise.damping = true;

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.50f),
                    new Keyframe(0.22f, 1.15f),
                    new Keyframe(1f, 2.60f)));

            var colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(SmokeFade());

            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
            rotation.y = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var collision = particles.collision;
            collision.enabled = false;
            var lights = particles.lights;
            lights.enabled = false;
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
                    new GradientAlphaKey(1f, 0.10f),
                    new GradientAlphaKey(0.55f, 0.48f),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        private void ConfigureRenderer(ParticleSystemRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            // Mesh, not billboard. This is the whole reason the effect belongs
            // to the same world as the rest of the game's VFX.
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = PickaxeImpactBurst.SharedSmokePuffMesh();
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.enableGPUInstancing = false;
            renderer.sharedMaterial = EnsureMaterial();
        }

        private Material EnsureMaterial()
        {
            if (_material != null)
            {
                return _material;
            }

            var shader = PickaxeImpactBurst.SharedSmokeShader();
            if (shader == null)
            {
                return null;
            }

            _material = new Material(shader)
            {
                name = "FX_AirshipDamageSmoke",
                hideFlags = HideFlags.HideAndDontSave,
            };
            PickaxeImpactBurst.SetSmokeColor(_material, "_BaseColor", SmokeTint);
            PickaxeImpactBurst.SetSmokeColor(_material, "_Color", SmokeTint);
            PickaxeImpactBurst.SetSmokeFloat(_material, "_EdgeSoftness", 0.44f);
            PickaxeImpactBurst.SetSmokeFloat(_material, "_NoiseScale", 9f);
            PickaxeImpactBurst.SetSmokeFloat(_material, "_FogInfluence", 0.35f);
            PickaxeImpactBurst.ApplySharedSmokeTransparency(_material);
            return _material;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
