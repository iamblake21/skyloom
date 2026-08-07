using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Unity.Presentation.Intro
{
    /// <summary>
    /// Everything the editor-generated opening scene hands to the director.
    /// Grouping it keeps the builder call readable and makes a missing actor a
    /// compile-time field instead of an unnamed positional argument.
    /// </summary>
    [Serializable]
    public sealed class IntroCinematicBindings
    {
        public Transform SpaceRoot;
        public Transform Airship;

        /// <summary>Yaw and pitch the player steers during the flight leg.</summary>
        public Transform AirshipHeading;

        /// <summary>Shudder and banking the director owns.</summary>
        public Transform AirshipAttitude;

        public Transform ChaseRig;
        public Camera ChaseCamera;
        public Transform CockpitShake;
        public Camera CockpitCamera;
        public Transform WarpTunnel;
        public Renderer WarpTunnelRenderer;
        public Transform Rift;
        public Renderer RiftRenderer;
        public Light RiftLight;
        public Light KeyLight;
        public Light CockpitFillLight;
        public Light[] AlertLights = Array.Empty<Light>();
        public ParticleSystem StarStreaks;
        public ParticleSystem CockpitSparks;
        public ParticleSystem RiftDebris;

        /// <summary>
        /// Rocks that stream past during the flight leg. The first one is
        /// reserved for the two teaching passes.
        /// </summary>
        public Transform[] Asteroids = Array.Empty<Transform>();
        public Volume PostProcessVolume;
        public Material SkyboxMaterial;
        public Material PortalVeilMaterial;
    }
}
