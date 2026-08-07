using System;
using System.Collections.Generic;
using System.IO;
using CML.Editor.Art;
using CML.Unity.Bootstrap;
using CML.Unity.Presentation.Intro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CML.Editor.Intro
{
    /// <summary>
    /// Authors the standalone arrival scene. Keeping it generated makes the
    /// cinematic reproducible and prevents the gameplay island from carrying
    /// temporary hyperspace geometry.
    /// </summary>
    public static class IntroCinematicSceneBuilder
    {
        public const string ScenePath =
            "Assets/_Project/Scenes/01_IntroCinematic.unity";

        private const string AirshipPrefabPath =
            "Assets/_Project/Art/Vehicles/Airship/Prefabs/PF_Airship.prefab";
        private const string CinematicsRoot = "Assets/_Project/Art/Cinematics";
        private const string MaterialsFolder = CinematicsRoot + "/Materials";
        private const string MeshesFolder = CinematicsRoot + "/Meshes";
        private const string ProfilesFolder = CinematicsRoot + "/Profiles";

        private const string DeepSpaceMaterialPath =
            MaterialsFolder + "/M_CIN_DeepSpace.mat";
        private const string WarpMaterialPath =
            MaterialsFolder + "/M_CIN_WarpTunnel.mat";
        private const string RiftMaterialPath =
            MaterialsFolder + "/M_CIN_Rift.mat";
        private const string StreakMaterialPath =
            MaterialsFolder + "/M_CIN_StarStreak.mat";
        private const string VeilMaterialPath =
            MaterialsFolder + "/M_CIN_PortalVeil.mat";
        private const string AsteroidMaterialPath =
            MaterialsFolder + "/M_CIN_Asteroid.mat";
        private const string WarpMeshPath =
            MeshesFolder + "/MSH_CIN_WarpTunnel.asset";
        private const string VolumeProfilePath =
            ProfilesFolder + "/VP_CIN_Intro.asset";

        private const string GameplaySceneName =
            "91_StarterIsland_Terrain_Review";

        /// <summary>
        /// Matches the camera StarterIslandAirshipIntegration authors for the
        /// pilot seat. The intro must not invent its own framing.
        /// </summary>
        private const float GameplayCameraFieldOfView = 68f;

        private const int AsteroidCount = 9;
        private const int AsteroidRings = 14;
        private const int AsteroidSegments = 18;

        private const int TunnelRadialSegments = 96;
        private const int TunnelRings = 12;
        private const float TunnelRadius = 26f;
        private const float TunnelStart = -190f;
        private const float TunnelEnd = 260f;
        private const float RiftStartDistance = 220f;
        private const float RiftSize = 190f;

        [MenuItem("CML/Cinematics/Rebuild Intro Sequence")]
        public static void Rebuild()
        {
            BuildScene();
        }

        public static void BuildScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets");
            EnsureFolder(MaterialsFolder);
            EnsureFolder(MeshesFolder);
            EnsureFolder(ProfilesFolder);

            var deepSpace = BuildMaterial(
                DeepSpaceMaterialPath,
                "CML/Cinematics/Deep Space");
            ConfigureDeepSpace(deepSpace);
            var warp = BuildMaterial(
                WarpMaterialPath,
                "CML/Cinematics/Warp Tunnel");
            ConfigureWarpTunnel(warp);
            var rift = BuildMaterial(
                RiftMaterialPath,
                "CML/Cinematics/Rift");
            ConfigureRift(rift);
            var streak = BuildMaterial(
                StreakMaterialPath,
                "CML/Cinematics/Star Streak");
            var veil = BuildMaterial(
                VeilMaterialPath,
                "CML/Cinematics/Portal Veil");
            ConfigurePortalVeil(veil);
            var tunnelMesh = BuildTunnelMesh();
            var profile = BuildVolumeProfile();

            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            try
            {
                var root = new GameObject("CIN_IntroSequence");
                root.AddComponent<GeneratedSceneRevision>().Configure(
                    GeneratedSceneRevision.IntroSceneId,
                    GeneratedSceneRevision.CurrentIntroRevision);
                var controller = root.AddComponent<IntroCinematicController>();

                var volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100f;
                volume.weight = 1f;
                volume.sharedProfile = profile;

                var spaceRoot = new GameObject("CIN_SpaceVisuals").transform;
                spaceRoot.SetParent(root.transform, false);

                var keyLight = CreateDirectionalLight(spaceRoot);
                CreateRimLight(spaceRoot);

                // Heading is what the player steers, attitude is what the
                // director shakes. Keeping them on separate pivots means the
                // two never fight over the same rotation.
                var heading = new GameObject("CIN_AirshipHeading").transform;
                heading.SetParent(spaceRoot, false);
                var attitude = new GameObject("CIN_AirshipAttitude").transform;
                attitude.SetParent(heading, false);

                var airship = CreateVisualAirship(attitude);
                var pilotAnchor = FindDeep(airship.transform, "REF_PilotCamera");
                if (pilotAnchor == null)
                {
                    throw new InvalidOperationException(
                        "The airship prefab no longer exposes REF_PilotCamera, "
                        + "so the first person cut has no eye position.");
                }

                // Reproduces AirshipPilotViewAudit exactly: the eye sits on the
                // authored anchor but looks along the hull, not along the
                // anchor's own axes. Parenting the camera under the anchor
                // would inherit its rotation and frame a different cockpit
                // from the one the player flies.
                var cockpitShake = new GameObject("CIN_CockpitShake").transform;
                cockpitShake.SetParent(airship.transform, false);
                cockpitShake.localPosition =
                    airship.transform.InverseTransformPoint(pilotAnchor.position);
                cockpitShake.localRotation = Quaternion.identity;

                var cockpitCamera = CreateCamera(
                    cockpitShake,
                    "CIN_CockpitCamera",
                    GameplayCameraFieldOfView);
                cockpitCamera.nearClipPlane = 0.02f;
                cockpitCamera.enabled = false;

                var chaseRig = new GameObject("CIN_ChaseRig").transform;
                chaseRig.SetParent(spaceRoot, false);
                var chaseCamera = CreateCamera(chaseRig, "CIN_ChaseCamera", 61f);
                chaseCamera.tag = "MainCamera";

                var alertLights = CreateAlertLights(pilotAnchor);
                var fillLight = CreateCockpitFillLight(pilotAnchor);
                var sparks = CreateCockpitSparks(pilotAnchor, streak);

                var tunnel = CreateWarpTunnel(heading, tunnelMesh, warp);
                var riftActor = CreateRift(spaceRoot, rift, out var riftLight);
                var streaks = CreateStarStreaks(heading, streak);
                var debris = CreateRiftDebris(spaceRoot, streak);
                var asteroids = CreateAsteroids(spaceRoot);

                controller.Configure(
                    new IntroCinematicBindings
                    {
                        SpaceRoot = spaceRoot,
                        Airship = airship.transform,
                        AirshipHeading = heading,
                        AirshipAttitude = attitude,
                        ChaseRig = chaseRig,
                        ChaseCamera = chaseCamera,
                        CockpitShake = cockpitShake,
                        CockpitCamera = cockpitCamera,
                        WarpTunnel = tunnel.transform,
                        WarpTunnelRenderer = tunnel.GetComponent<Renderer>(),
                        Rift = riftActor.transform,
                        RiftRenderer = riftActor.GetComponent<Renderer>(),
                        RiftLight = riftLight,
                        KeyLight = keyLight,
                        CockpitFillLight = fillLight,
                        AlertLights = alertLights,
                        StarStreaks = streaks,
                        CockpitSparks = sparks,
                        RiftDebris = debris,
                        Asteroids = asteroids,
                        PostProcessVolume = volume,
                        SkyboxMaterial = deepSpace,
                        PortalVeilMaterial = veil
                    },
                    GameplaySceneName);

                if (TryMeasurePortalAperture(
                        out var apertureHeight,
                        out var apertureRadius))
                {
                    controller.ConfigurePortalAperture(
                        apertureHeight,
                        apertureRadius);
                }

                ConfigureSceneLighting(deepSpace);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    throw new InvalidOperationException(
                        "Could not save intro cinematic scene at " + ScenePath);
                }

                EnsureSceneIncludedInBuild();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    "CML_INTRO_CINEMATIC_READY scene=" + ScenePath
                    + " shots=9 duration=" +
                    controller.TotalDurationSeconds.ToString("F1") + "s"
                    + " alertLights=" + controller.AlertLightCount
                    + " portalAperture=" + apertureHeight.ToString("F3")
                    + "/" + apertureRadius.ToString("F3"));
            }
            finally
            {
                // Batch mode starts with no scene loaded at all, and restoring
                // an empty setup is an error rather than a no-op.
                if (previousSetup != null && previousSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        // ----------------------------------------------------------------- //
        // Actors.
        // ----------------------------------------------------------------- //
        private static GameObject CreateVisualAirship(Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AirshipPrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    "The playable airship prefab is missing: " + AirshipPrefabPath);
            }

            var airship = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (airship == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate the airship intro actor.");
            }

            airship.name = "CIN_AirshipActor";
            airship.transform.SetParent(parent, false);
            airship.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);

            // The duplicate is scenery. Nothing on it may simulate, collide or
            // claim the pilot seat while the director owns the frame.
            var behaviours = airship.GetComponentsInChildren<Behaviour>(true);
            for (var index = 0; index < behaviours.Length; index++)
            {
                behaviours[index].enabled = false;
            }

            var colliders = airship.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }

            return airship;
        }

        private static Camera CreateCamera(
            Transform parent,
            string name,
            float fieldOfView)
        {
            var cameraObject = new GameObject(name);
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.identity;

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 2400f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.depthTextureMode |= DepthTextureMode.Depth;

            // Without this the whole colour grade the director animates would
            // be computed and then thrown away.
            var urp = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            urp.antialiasingQuality = AntialiasingQuality.High;
            urp.renderShadows = true;
            urp.requiresColorOption = CameraOverrideOption.On;
            urp.requiresDepthOption = CameraOverrideOption.On;

            cameraObject.AddComponent<AudioListener>().enabled = false;
            return camera;
        }

        private static Light CreateDirectionalLight(Transform parent)
        {
            var lightObject = new GameObject("CIN_SpaceKeyLight");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(28f, -142f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.62f, 0.76f, 1f, 1f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.Soft;
            return light;
        }

        /// <summary>
        /// A hull lit by one lamp against a black sky loses its silhouette. The
        /// counter light only separates the edge; it must stay well under the
        /// key or the shot flattens out.
        /// </summary>
        private static void CreateRimLight(Transform parent)
        {
            var lightObject = new GameObject("CIN_SpaceRimLight");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(-14f, 46f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.44f, 0.34f, 0.86f, 1f);
            light.intensity = 0.85f;
            light.shadows = LightShadows.None;
        }

        private static Light[] CreateAlertLights(Transform pilotAnchor)
        {
            var placements = new[]
            {
                new Vector3(-0.95f, 0.18f, 0.35f),
                new Vector3(0.95f, 0.18f, 0.35f),
                new Vector3(0f, 0.62f, -1.15f)
            };

            var lights = new Light[placements.Length];
            for (var index = 0; index < placements.Length; index++)
            {
                var lightObject = new GameObject(
                    "CIN_AlertLight_" + index.ToString("D2"));
                lightObject.transform.SetParent(pilotAnchor, false);
                lightObject.transform.localPosition = placements[index];
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.06f, 0.03f, 1f);
                light.range = 7.5f;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                lights[index] = light;
            }

            return lights;
        }

        private static Light CreateCockpitFillLight(Transform pilotAnchor)
        {
            var lightObject = new GameObject("CIN_CockpitFill");
            lightObject.transform.SetParent(pilotAnchor, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.45f, 0.55f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.88f, 0.72f, 1f);
            light.range = 5.5f;
            light.intensity = 1.35f;
            light.shadows = LightShadows.None;
            return light;
        }

        private static ParticleSystem CreateCockpitSparks(
            Transform pilotAnchor,
            Material material)
        {
            var system = CreateParticleSystem(
                pilotAnchor,
                "CIN_CockpitSparks",
                material);
            system.transform.localPosition = new Vector3(0f, -0.32f, 1.15f);
            system.transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);

            var main = system.main;
            main.startLifetime = 0.85f;
            main.startSpeed = 4.2f;
            main.startSize = 0.035f;
            main.gravityModifier = 0.9f;
            main.maxParticles = 400;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.86f, 0.44f, 1f),
                new Color(1f, 0.42f, 0.12f, 1f));

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.12f;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 5.5f;
            renderer.velocityScale = 0.08f;
            return system;
        }

        /// <summary>
        /// Rocks for the flight leg. Index zero is the one the two teaching
        /// passes put on a collision course; the rest only give the run a sense
        /// of speed and scale.
        /// </summary>
        private static Transform[] CreateAsteroids(Transform parent)
        {
            var material = BuildAsteroidMaterial();
            var root = new GameObject("CIN_Asteroids").transform;
            root.SetParent(parent, false);

            var asteroids = new Transform[AsteroidCount];
            for (var index = 0; index < AsteroidCount; index++)
            {
                var mesh = BuildAsteroidMesh(index);
                var asteroid = new GameObject(
                    index == 0
                        ? "CIN_Asteroid_Threat"
                        : "CIN_Asteroid_" + index.ToString("D2"));
                asteroid.transform.SetParent(root, false);
                asteroid.transform.localRotation = Quaternion.Euler(
                    index * 37f,
                    index * 61f,
                    index * 23f);

                // Authored well ahead of the hull. The director re-scatters
                // them every lap, but the saved scene must never leave a rock
                // sitting inside the cockpit.
                var placement = new System.Random(4207 + index * 89);
                asteroid.transform.localPosition = new Vector3(
                    NextFloat(placement, -260f, 260f),
                    NextFloat(placement, -140f, 160f),
                    index == 0
                        ? 620f
                        : NextFloat(placement, 260f, 1250f));

                // The teaching rock is a hazard, not a wall: past roughly this
                // size it is wider than any turn can clear and the lesson stops
                // being winnable.
                var scale = index == 0
                    ? 30f
                    : Mathf.Lerp(9f, 24f, (index * 0.37f) % 1f);
                asteroid.transform.localScale = Vector3.one * scale;
                asteroid.SetActive(index != 0);

                asteroid.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = asteroid.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                asteroids[index] = asteroid.transform;
            }

            return asteroids;
        }

        private static Material BuildAsteroidMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP/Lit is unavailable for the cinematic asteroids.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(
                AsteroidMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_CIN_Asteroid" };
                AssetDatabase.CreateAsset(material, AsteroidMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            // A dark rock against dark space is a threat nobody can see coming.
            // The albedo is lifted well past real stone on purpose.
            material.SetColor("_BaseColor", new Color(0.52f, 0.46f, 0.42f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.12f);
            material.EnableKeyword("_EMISSION");
            material.SetColor(
                "_EmissionColor",
                new Color(0.09f, 0.08f, 0.10f, 1f));
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// A lumpy sphere with flat shading. Faceted rock reads as stone at
        /// speed and stays inside the game's stylised look.
        /// </summary>
        private static Mesh BuildAsteroidMesh(int variant)
        {
            var path = MeshesFolder + "/MSH_CIN_Asteroid_"
                + variant.ToString("D2") + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "MSH_CIN_Asteroid_" + variant };
                AssetDatabase.CreateAsset(mesh, path);
            }

            mesh.Clear();
            var random = new System.Random(9001 + variant * 131);
            var lumps = new Vector4[6];
            for (var index = 0; index < lumps.Length; index++)
            {
                var direction = new Vector3(
                    NextFloat(random, -1f, 1f),
                    NextFloat(random, -1f, 1f),
                    NextFloat(random, -1f, 1f)).normalized;
                lumps[index] = new Vector4(
                    direction.x,
                    direction.y,
                    direction.z,
                    NextFloat(random, -0.26f, 0.30f));
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var ring = 0; ring <= AsteroidRings; ring++)
            {
                var polar = Mathf.PI * ring / AsteroidRings;
                for (var segment = 0; segment <= AsteroidSegments; segment++)
                {
                    var azimuth = Mathf.PI * 2f * segment / AsteroidSegments;
                    var direction = new Vector3(
                        Mathf.Sin(polar) * Mathf.Cos(azimuth),
                        Mathf.Cos(polar),
                        Mathf.Sin(polar) * Mathf.Sin(azimuth));

                    var radius = 0.5f;
                    for (var lump = 0; lump < lumps.Length; lump++)
                    {
                        var axis = new Vector3(
                            lumps[lump].x,
                            lumps[lump].y,
                            lumps[lump].z);
                        var influence = Mathf.Max(
                            0f,
                            Vector3.Dot(direction, axis));
                        radius += influence * influence * lumps[lump].w * 0.5f;
                    }

                    vertices.Add(direction * Mathf.Max(radius, 0.18f));
                }
            }

            var stride = AsteroidSegments + 1;
            for (var ring = 0; ring < AsteroidRings; ring++)
            {
                for (var segment = 0; segment < AsteroidSegments; segment++)
                {
                    var current = ring * stride + segment;
                    var next = current + stride;

                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);
                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            // Flat shading: split every triangle so the facets stay hard.
            var flatVertices = new Vector3[triangles.Count];
            var flatTriangles = new int[triangles.Count];
            for (var index = 0; index < triangles.Count; index++)
            {
                flatVertices[index] = vertices[triangles[index]];
                flatTriangles[index] = index;
            }

            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = flatVertices;
            mesh.triangles = flatTriangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static float NextFloat(
            System.Random random,
            float minimum,
            float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }

        private static GameObject CreateWarpTunnel(
            Transform parent,
            Mesh mesh,
            Material material)
        {
            var tunnel = new GameObject("CIN_WarpTunnel");
            tunnel.transform.SetParent(parent, false);
            tunnel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = tunnel.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return tunnel;
        }

        private static GameObject CreateRift(
            Transform parent,
            Material material,
            out Light riftLight)
        {
            var rift = GameObject.CreatePrimitive(PrimitiveType.Quad);
            rift.name = "CIN_Rift";
            UnityEngine.Object.DestroyImmediate(rift.GetComponent<Collider>());
            rift.transform.SetParent(parent, false);
            rift.transform.localPosition = new Vector3(0f, 2f, RiftStartDistance);
            rift.transform.localRotation = Quaternion.identity;
            rift.transform.localScale = Vector3.one * RiftSize;

            var renderer = rift.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var glow = new GameObject("CIN_RiftGlow");
            glow.transform.SetParent(rift.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            riftLight = glow.AddComponent<Light>();
            riftLight.type = LightType.Point;
            riftLight.color = new Color(0.32f, 0.82f, 1f, 1f);
            riftLight.range = 460f;
            riftLight.intensity = 0f;
            riftLight.shadows = LightShadows.None;
            return rift;
        }

        private static ParticleSystem CreateStarStreaks(
            Transform parent,
            Material material)
        {
            var system = CreateParticleSystem(
                parent,
                "CIN_StarStreaks",
                material);

            // Emitted ahead of the ship and pushed backwards past the camera:
            // the parallax is what carries the sense of travel.
            system.transform.localPosition = new Vector3(0f, 0f, 150f);
            system.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var main = system.main;
            main.startLifetime = 2.2f;
            main.startSpeed = 30f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.30f);
            main.gravityModifier = 0f;
            main.maxParticles = 2400;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.62f, 0.78f, 1f, 1f),
                new Color(1f, 0.94f, 0.86f, 1f));

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 220f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(150f, 96f, 1f);

            // Hollow corridor down the travel axis. Without it the field is
            // emitted straight through the hull and the cockpit fills with
            // streaks that bury the dashboard the player is supposed to read.
            shape.boxThickness = new Vector3(0.26f, 0.30f, 0f);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 3.2f;
            renderer.velocityScale = 0f;
            renderer.cameraVelocityScale = 0f;
            return system;
        }

        private static ParticleSystem CreateRiftDebris(
            Transform parent,
            Material material)
        {
            var system = CreateParticleSystem(
                parent,
                "CIN_RiftDebris",
                material);
            system.transform.localPosition = new Vector3(0f, 0f, -40f);
            system.transform.localRotation = Quaternion.identity;

            var main = system.main;
            main.startLifetime = 3.2f;
            main.startSpeed = 18f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.62f);
            main.gravityModifier = 0f;
            main.maxParticles = 900;
            main.playOnAwake = false;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.78f, 0.62f, 1f, 1f),
                new Color(0.36f, 0.86f, 1f, 1f));

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(120f, 78f, 40f);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4.5f;
            renderer.velocityScale = 0.12f;
            return system;
        }

        private static ParticleSystem CreateParticleSystem(
            Transform parent,
            string name,
            Material material)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            var system = host.AddComponent<ParticleSystem>();

            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.useUnscaledTime = true;

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.alignment = ParticleSystemRenderSpace.World;
            return system;
        }

        private static void ConfigureSceneLighting(Material skybox)
        {
            RenderSettings.skybox = skybox;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.07f, 0.10f, 0.20f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.04f, 0.05f, 0.11f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.05f, 1f);
            RenderSettings.reflectionIntensity = 0.35f;
        }

        // ----------------------------------------------------------------- //
        // Generated assets.
        // ----------------------------------------------------------------- //
        private static Mesh BuildTunnelMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WarpMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "MSH_CIN_WarpTunnel" };
                AssetDatabase.CreateAsset(mesh, WarpMeshPath);
            }

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            var vertexCount = (TunnelRadialSegments + 1) * (TunnelRings + 1);
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var normals = new Vector3[vertexCount];

            for (var ring = 0; ring <= TunnelRings; ring++)
            {
                var v = ring / (float)TunnelRings;
                var z = Mathf.Lerp(TunnelStart, TunnelEnd, v);
                for (var segment = 0; segment <= TunnelRadialSegments; segment++)
                {
                    var u = segment / (float)TunnelRadialSegments;
                    var angle = u * Mathf.PI * 2f;
                    var index = ring * (TunnelRadialSegments + 1) + segment;
                    var direction = new Vector3(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle),
                        0f);
                    vertices[index] = direction * TunnelRadius
                        + new Vector3(0f, 0f, z);
                    normals[index] = -direction;
                    uvs[index] = new Vector2(u, v);
                }
            }

            var triangles = new int[TunnelRadialSegments * TunnelRings * 6];
            var cursor = 0;
            for (var ring = 0; ring < TunnelRings; ring++)
            {
                for (var segment = 0; segment < TunnelRadialSegments; segment++)
                {
                    var current = ring * (TunnelRadialSegments + 1) + segment;
                    var next = current + TunnelRadialSegments + 1;

                    triangles[cursor++] = current;
                    triangles[cursor++] = next;
                    triangles[cursor++] = current + 1;
                    triangles[cursor++] = current + 1;
                    triangles[cursor++] = next;
                    triangles[cursor++] = next + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static VolumeProfile BuildVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            // The director animates every one of these each frame, so the asset
            // only has to exist and expose them as overridden.
            UpsertOverride<Bloom>(profile);
            UpsertOverride<ChromaticAberration>(profile);
            UpsertOverride<LensDistortion>(profile);
            UpsertOverride<Vignette>(profile);
            UpsertOverride<MotionBlur>(profile);
            UpsertOverride<FilmGrain>(profile);
            UpsertOverride<ColorAdjustments>(profile);
            UpsertOverride<PaniniProjection>(profile);

            if (profile.TryGet<Bloom>(out var bloom))
            {
                bloom.scatter.value = 0.72f;
                bloom.highQualityFiltering.overrideState = true;
                bloom.highQualityFiltering.value = true;
            }

            if (profile.TryGet<MotionBlur>(out var motionBlur))
            {
                motionBlur.mode.value = MotionBlurMode.CameraOnly;
                motionBlur.quality.overrideState = true;
                motionBlur.quality.value = MotionBlurQuality.High;
                motionBlur.clamp.value = 0.18f;
            }

            if (profile.TryGet<FilmGrain>(out var grain))
            {
                grain.type.value = FilmGrainLookup.Thin1;
                grain.response.value = 0.72f;
            }

            if (profile.TryGet<Vignette>(out var vignette))
            {
                vignette.smoothness.value = 0.42f;
            }

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void UpsertOverride<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (profile.Has<T>())
            {
                return;
            }

            var component = profile.Add<T>(true);
            component.active = true;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        /// <summary>
        /// The sky is deep space, not weather. Most of the frame has to stay
        /// black so the star field carries the depth; the nebula is a few
        /// bright filaments along the galactic plane, not a fog that fills
        /// every pixel.
        /// </summary>
        private static void ConfigureDeepSpace(Material material)
        {
            material.SetColor("_SpaceColor", new Color(0.003f, 0.004f, 0.012f, 1f));
            material.SetColor("_NebulaColorA", new Color(0.16f, 0.07f, 0.38f, 1f));
            material.SetColor("_NebulaColorB", new Color(0.03f, 0.30f, 0.52f, 1f));
            material.SetColor("_NebulaColorC", new Color(0.78f, 0.24f, 0.44f, 1f));
            material.SetFloat("_NebulaScale", 1.05f);
            material.SetFloat("_NebulaCoverage", 0.42f);
            material.SetFloat("_NebulaContrast", 3.4f);
            material.SetFloat("_NebulaIntensity", 0.95f);

            material.SetVector("_GalaxyAxis", new Vector4(0.34f, 0.88f, -0.33f, 0f));
            material.SetFloat("_GalaxyWidth", 0.26f);
            material.SetFloat("_GalaxyIntensity", 1.7f);
            material.SetColor("_GalaxyColor", new Color(0.60f, 0.66f, 0.90f, 1f));

            material.SetFloat("_StarDensity", 0.085f);
            material.SetFloat("_StarBrightness", 8.5f);
            material.SetFloat("_StarSharpness", 3.2f);
            material.SetFloat("_TwinkleSpeed", 1.4f);

            material.SetFloat("_WarpBlend", 0f);
            material.SetVector("_WarpAxis", new Vector4(0f, 0f, 1f, 0f));
            material.SetFloat("_WarpStretch", 0.42f);
            material.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Many thin filaments, a narrow palette and only a hint of dispersion.
        /// Wide bars and saturated primaries are what make a warp tunnel look
        /// like a screensaver.
        /// </summary>
        private static void ConfigureWarpTunnel(Material material)
        {
            material.SetColor("_CoreColor", new Color(1f, 0.99f, 0.96f, 1f));
            material.SetColor("_MidColor", new Color(0.34f, 0.68f, 1f, 1f));
            material.SetColor("_EdgeColor", new Color(0.20f, 0.10f, 0.52f, 1f));
            material.SetFloat("_Intensity", 0f);
            material.SetFloat("_Speed", 3.4f);
            material.SetFloat("_StreakDensity", 240f);
            material.SetFloat("_StreakLength", 1.5f);
            material.SetFloat("_Turbulence", 0.38f);
            material.SetFloat("_Twist", 0.22f);
            material.SetFloat("_ChromaticSplit", 0.006f);
            material.SetFloat("_EndFade", 0.16f);
            material.SetFloat("_CoreGlow", 0.85f);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// A narrow, ragged tear with hot lips and a deep interior. Widening it
        /// or brightening the middle turns the varco into a lit billboard.
        /// </summary>
        private static void ConfigureRift(Material material)
        {
            material.SetColor("_CoreColor", new Color(1f, 0.97f, 0.90f, 1f));
            material.SetColor("_EnergyColor", new Color(0.32f, 0.78f, 1f, 1f));
            material.SetColor("_RimColor", new Color(0.66f, 0.24f, 1f, 1f));
            material.SetColor("_VoidColor", new Color(0.03f, 0.02f, 0.09f, 1f));
            material.SetFloat("_Openness", 0f);
            material.SetFloat("_Width", 0.19f);
            material.SetFloat("_EdgeSoftness", 0.035f);
            material.SetFloat("_EdgeTurbulence", 0.55f);
            material.SetFloat("_TurbulenceScale", 11f);
            material.SetFloat("_TurbulenceSpeed", 1.9f);
            material.SetFloat("_Refraction", 0.16f);
            material.SetFloat("_SwirlIntensity", 1.15f);
            material.SetFloat("_SwirlSpeed", 1.4f);
            material.SetFloat("_FilamentIntensity", 1.45f);
            // The climax is carried by the director's exposure ramp, not by a
            // material that is already blown out when the tear first appears.
            material.SetFloat("_Intensity", 0.62f);
            EditorUtility.SetDirty(material);
        }

        private static void ConfigurePortalVeil(Material material)
        {
            material.SetColor("_InnerColor", new Color(0.74f, 0.95f, 1f, 1f));
            material.SetColor("_OuterColor", new Color(0.16f, 0.40f, 0.94f, 1f));
            material.SetColor("_RimColor", new Color(0.96f, 0.86f, 0.52f, 1f));
            material.SetFloat("_Charge", 0f);
            material.SetFloat("_SwirlSpeed", 1.25f);
            material.SetFloat("_SwirlScale", 3.8f);
            material.SetFloat("_Refraction", 0.09f);
            material.SetFloat("_RimWidth", 0.14f);
            material.SetFloat("_Intensity", 1.6f);
            EditorUtility.SetDirty(material);
        }

        private static Material BuildMaterial(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Shader '" + shaderName + "' is unavailable. The cinematic "
                    + "shaders live in " + CinematicsRoot + "/Shaders.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // ----------------------------------------------------------------- //
        // Portal aperture measurement.
        // ----------------------------------------------------------------- //
        /// <summary>
        /// Finds the opening of the ancient arch by scanning the mesh: for each
        /// height band, the closest vertex to the vertical centre axis is the
        /// stone the camera would hit. The tallest run of bands that stays
        /// clear is the aperture the arrival has to fly through.
        /// </summary>
        private static bool TryMeasurePortalAperture(
            out float heightFraction,
            out float radiusFraction)
        {
            heightFraction = 0.42f;
            radiusFraction = 0.24f;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                StarterIslandPortalSetup.PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    "CML intro cinematic could not measure the ancient portal; "
                    + "the arrival keeps its default aperture.");
                return false;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                return false;
            }

            try
            {
                var filters = instance.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Length == 0)
                {
                    return false;
                }

                var points = new List<Vector3>();
                var bounds = new Bounds();
                var initialised = false;
                for (var index = 0; index < filters.Length; index++)
                {
                    var mesh = filters[index].sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    // The portal FBX ships with Read/Write disabled for the
                    // player. That flag does not apply to the Editor, which
                    // keeps the authoring copy of every imported mesh.
                    var local = filters[index].transform;
                    var vertices = mesh.vertices;
                    if (vertices == null || vertices.Length == 0)
                    {
                        continue;
                    }

                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        var world = local.TransformPoint(vertices[vertex]);
                        points.Add(world);
                        if (initialised)
                        {
                            bounds.Encapsulate(world);
                        }
                        else
                        {
                            bounds = new Bounds(world, Vector3.zero);
                            initialised = true;
                        }
                    }
                }

                if (!initialised || bounds.size.y <= 0.01f)
                {
                    Debug.LogWarning(
                        "CML intro cinematic could not read the ancient portal "
                        + "mesh (Read/Write is off); the arrival keeps its "
                        + "default aperture.");
                    return false;
                }

                const int BandCount = 96;
                var clearance = new float[BandCount];
                for (var band = 0; band < BandCount; band++)
                {
                    clearance[band] = float.MaxValue;
                }

                var axis = new Vector2(bounds.center.x, bounds.center.z);
                for (var index = 0; index < points.Count; index++)
                {
                    var point = points[index];
                    var normalised = Mathf.InverseLerp(
                        bounds.min.y,
                        bounds.max.y,
                        point.y);
                    var band = Mathf.Clamp(
                        Mathf.FloorToInt(normalised * BandCount),
                        0,
                        BandCount - 1);
                    var distance = Vector2.Distance(
                        new Vector2(point.x, point.z),
                        axis);
                    if (distance < clearance[band])
                    {
                        clearance[band] = distance;
                    }
                }

                // Largest disc that fits through the arch. A band only blocks a
                // radius that actually reaches it, so the limit a band imposes
                // is whichever is larger: how close its stone comes to the
                // axis, or how far away it is vertically.
                var bandHeight = bounds.size.y / BandCount;
                var bestRadius = 0f;
                var bestCentreY = bounds.center.y;
                for (var centre = 0; centre < BandCount; centre++)
                {
                    var centreY = bounds.min.y + (centre + 0.5f) * bandHeight;
                    var radius = centreY - bounds.min.y;
                    for (var band = 0; band < BandCount; band++)
                    {
                        var bandY = bounds.min.y + (band + 0.5f) * bandHeight;
                        radius = Mathf.Min(
                            radius,
                            Mathf.Max(
                                clearance[band],
                                Mathf.Abs(bandY - centreY)));
                    }

                    if (radius > bestRadius)
                    {
                        bestRadius = radius;
                        bestCentreY = centreY;
                    }
                }

                if (bestRadius <= bounds.size.y * 0.04f)
                {
                    Debug.LogWarning(
                        "CML intro cinematic found no clear span in the ancient "
                        + "portal; the arrival keeps its default aperture.");
                    return false;
                }

                heightFraction = Mathf.Clamp(
                    (bestCentreY - bounds.min.y) / bounds.size.y,
                    0.15f,
                    0.75f);

                // Stay inside the stone: the veil must never clip the jambs.
                radiusFraction = Mathf.Clamp(
                    bestRadius * 0.86f / bounds.size.y,
                    0.06f,
                    0.45f);

                Debug.Log(
                    "CML_INTRO_PORTAL_APERTURE height="
                    + (heightFraction * bounds.size.y).ToString("F2") + "m radius="
                    + (radiusFraction * bounds.size.y).ToString("F2") + "m of a "
                    + bounds.size.y.ToString("F2") + "m portal");
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        // ----------------------------------------------------------------- //
        // Helpers.
        // ----------------------------------------------------------------- //
        private static Transform FindDeep(Transform root, string exactName)
        {
            var descendants = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < descendants.Length; index++)
            {
                if (string.Equals(
                        descendants[index].name,
                        exactName,
                        StringComparison.Ordinal))
                {
                    return descendants[index];
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private static void EnsureSceneIncludedInBuild()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = 0; index < scenes.Count; index++)
            {
                if (!string.Equals(
                        scenes[index].path,
                        ScenePath,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!scenes[index].enabled)
                {
                    scenes[index] = new EditorBuildSettingsScene(ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }

                return;
            }

            var insertIndex = 0;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path.EndsWith(
                        "/00_Bootstrap.unity",
                        StringComparison.Ordinal))
                {
                    insertIndex = index + 1;
                    break;
                }
            }

            scenes.Insert(insertIndex, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
