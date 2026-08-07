using System;
using System.Collections.Generic;
using System.IO;
using CML.Simulation.Airship;
using CML.Unity.Airship;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity-side setup for the production airship asset.
    /// Blender remains the source of geometry and UVs; this class owns only
    /// importer settings, URP materials and the reusable prefab.
    /// </summary>
    public static class AirshipAssetSetup
    {
        private const string Root = "Assets/_Project/Art/Vehicles/Airship";
        private const string ModelPath = Root + "/Models/AIR_Airship.fbx";
        private const string TextureRoot = Root + "/Textures";
        private const string MaterialsRoot = Root + "/Materials";
        private const string PrefabsRoot = Root + "/Prefabs";
        private const string PrefabPath = PrefabsRoot + "/PF_Airship.prefab";
        private const int ExpectedPrecisePlayerCollisionMeshCount = 55;

        private const string OpaqueMaterialPath = MaterialsRoot + "/M_Airship_OpaqueAtlas.mat";
        private const string WoodMaterialPath = MaterialsRoot + "/M_Airship_Wood.mat";
        private const string GlassMaterialPath = MaterialsRoot + "/M_Airship_Glass.mat";
        private const string EmissionMaterialPath = MaterialsRoot + "/M_Airship_EmissionAtlas.mat";

        private const string BaseTexturePath = TextureRoot + "/T_Airship_BaseColor.png";
        private const string MaskTexturePath = TextureRoot + "/T_Airship_Mask.png";
        private const string WoodBaseTexturePath = TextureRoot + "/T_Airship_Wood_BaseColor.png";
        private const string WoodMaskTexturePath = TextureRoot + "/T_Airship_Wood_Mask.png";
        private const string EmissionTexturePath = TextureRoot + "/T_Airship_Emission.png";
        private const string DetailTexturePath = TextureRoot + "/T_Airship_Detail.png";

        private static readonly string[] RequiredTransforms =
        {
            "GEO_Static",
            "ANM_Moving",
            "ANM_BoardingRamp",
            "ANM_AccessDoor",
            "ANM_PropellerRotor",
            "REF_PilotCamera",
            "REF_PilotSeat",
            "REF_PilotControls",
            "REF_PilotExit",
            "REF_InteriorRespawn",
            "REF_RampTip",
            "COL_VEH_Envelope",
            "COL_VEH_Gondola",
            "COL_VEH_PropellerClearance",
            "COL_WALK_Floor",
            "COLMESH_PLAYER_RearBulkhead",
            "COLMESH_PLAYER_CockpitCeilingLiner",
            "COLMESH_PLAYER_EntryOppositeWallLiner",
            "COLMESH_PLAYER_CockpitSideLiner_Port",
            "COLMESH_PLAYER_CockpitSideLiner_Starboard",
            "COLMESH_PLAYER_PilotConsole",
            "COLMESH_PLAYER_PilotSeatBase",
            "COLMESH_PLAYER_PilotSeatBack",
            "TRG_Interior",
            "TRG_PilotSeat",
            "TRG_BoardingArea",
            "PORT_CargoInput",
            "PORT_CargoOutput",
            "PORT_ChargeInput",
            "SLOT_RepairPlate_04",
            "SLOT_RepairCable_02"
        };

        [MenuItem("CML/Art/Rebuild Airship Asset")]
        public static void Run()
        {
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);

            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(BaseTexturePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(MaskTexturePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(WoodBaseTexturePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(WoodMaskTexturePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(EmissionTexturePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(DetailTexturePath, ImportAssetOptions.ForceUpdate);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The URP/Lit shader is unavailable. Verify that the Universal Render Pipeline is active.");
            }

            var baseTexture = RequireAsset<Texture2D>(BaseTexturePath);
            var maskTexture = RequireAsset<Texture2D>(MaskTexturePath);
            var woodBaseTexture = RequireAsset<Texture2D>(WoodBaseTexturePath);
            var woodMaskTexture = RequireAsset<Texture2D>(WoodMaskTexturePath);
            var emissionTexture = RequireAsset<Texture2D>(EmissionTexturePath);
            var detailTexture = RequireAsset<Texture2D>(DetailTexturePath);

            var materials = new Dictionary<AirshipMaterialRole, Material>
            {
                [AirshipMaterialRole.Opaque] = UpsertMaterial(
                    OpaqueMaterialPath,
                    shader,
                    material => ConfigureMaskedOpaque(
                        material,
                        baseTexture,
                        maskTexture,
                        detailTexture,
                        1f)),
                [AirshipMaterialRole.Wood] = UpsertMaterial(
                    WoodMaterialPath,
                    shader,
                    material => ConfigureMaskedOpaque(
                        material,
                        woodBaseTexture,
                        woodMaskTexture,
                        detailTexture,
                        1f)),
                [AirshipMaterialRole.Glass] = UpsertMaterial(
                    GlassMaterialPath,
                    shader,
                    ConfigureGlass),
                [AirshipMaterialRole.Emission] = UpsertMaterial(
                    EmissionMaterialPath,
                    shader,
                    material => ConfigureEmission(material, emissionTexture))
            };

            ConfigureModelMaterialRemaps(materials);
            var prefab = BuildPrefab(materials);
            ValidatePrefab(prefab, materials);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;

            var metrics = CollectMetrics(prefab);
            Debug.Log(
                $"AIRSHIP_UNITY_VALIDATION prefab={PrefabPath} " +
                $"renderers={metrics.RendererCount} meshes={metrics.MeshCount} " +
                $"triangles={metrics.TriangleCount} cargoSlots={metrics.CargoSlotCount} " +
                $"bounds=({metrics.Bounds.size.x:F3},{metrics.Bounds.size.y:F3},{metrics.Bounds.size.z:F3})");
        }

        private static GameObject BuildPrefab(IReadOnlyDictionary<AirshipMaterialRole, Material> materials)
        {
            var source = RequireAsset<GameObject>(ModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate model asset: {ModelPath}");
            }

            try
            {
                instance.name = "PF_Airship";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var sourceMaterials = renderer.sharedMaterials;
                    var assignedMaterials = new Material[sourceMaterials.Length];
                    var containsGlass = false;
                    for (var index = 0; index < sourceMaterials.Length; index++)
                    {
                        assignedMaterials[index] = ResolveMaterial(sourceMaterials[index], renderer, materials);
                        containsGlass |= assignedMaterials[index] == materials[AirshipMaterialRole.Glass];
                    }

                    renderer.sharedMaterials = assignedMaterials;
                    if (containsGlass)
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }
                }

                ConfigureGameplayRig(instance);

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Could not save prefab: {PrefabPath}");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ConfigureModelMaterialRemaps(
            IReadOnlyDictionary<AirshipMaterialRole, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not load ModelImporter for: {ModelPath}");
            }

            // The Blender generator already exports the authored Unity axes.
            // Baking FBX axis conversion a second time mirrors longitudinal
            // markers, placing the cockpit at -Z while gameplay uses +Z.
            importer.bakeAxisConversion = false;
            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Airship_OpaqueAtlas"),
                materials[AirshipMaterialRole.Opaque]);
            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Airship_Wood"),
                materials[AirshipMaterialRole.Wood]);
            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Airship_Glass"),
                materials[AirshipMaterialRole.Glass]);
            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Airship_EmissionAtlas"),
                materials[AirshipMaterialRole.Emission]);
            importer.SaveAndReimport();
        }

        private static void ConfigureGameplayRig(GameObject instance)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(instance);
            var root = instance.transform;

            // The source asset is authored in the clean, closed presentation
            // pose used by the reference sheet.  The gameplay prefab keeps
            // the starboard door physically open so the player can actually
            // walk from the boarding step into the compact cockpit.
            var accessDoor = RequireTransform(root, "ANM_AccessDoor");
            accessDoor.localRotation = ComputeOpenAccessDoorRotation(
                root,
                accessDoor,
                accessDoor.localRotation);

            foreach (var rigidbody in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(rigidbody);
            }
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            var collisionBody = instance.AddComponent<Rigidbody>();
            collisionBody.isKinematic = true;
            collisionBody.useGravity = false;
            collisionBody.interpolation = RigidbodyInterpolation.Interpolate;
            collisionBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;

            ConfigurePlayerCollisionMeshes(root);

            var interiorTrigger = RequireTransform(root, "TRG_Interior");
            interiorTrigger.rotation = root.rotation;
            ConfigureBoxCollider(
                interiorTrigger,
                center: Vector3.zero,
                size: new Vector3(2.80f, 2.20f, 3.50f),
                isTrigger: true,
                enabled: true);
            var pilotTriggerOwner = RequireTransform(root, "TRG_PilotSeat");
            pilotTriggerOwner.rotation = root.rotation;
            var pilotTrigger = ConfigureBoxCollider(
                pilotTriggerOwner,
                center: Vector3.zero,
                size: new Vector3(1.40f, 1.80f, 1.20f),
                isTrigger: true,
                enabled: true);
            var boardingTriggerOwner = RequireTransform(root, "TRG_BoardingArea");
            boardingTriggerOwner.rotation = root.rotation;
            var boardingTrigger = ConfigureBoxCollider(
                boardingTriggerOwner,
                center: Vector3.zero,
                size: new Vector3(2.20f, 1.30f, 1.40f),
                isTrigger: true,
                enabled: true);

            var passengerSpace = UpsertChild(root, "SYS_PassengerSpace");
            var rampTip = RequireTransform(root, "REF_RampTip");
            var legacyProbeOrigin = rampTip.Find("SYS_DisembarkProbeOrigin");
            if (legacyProbeOrigin != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    legacyProbeOrigin.gameObject);
            }

            var disembarkProbeOrigin =
                UpsertChild(root, "SYS_DisembarkProbeOrigin");
            disembarkProbeOrigin.localPosition = new Vector3(
                AirshipSimulationConstants.RampTipLocalXMillimetres / 1000f,
                AirshipSimulationConstants.RampTipLocalYMillimetres / 1000f,
                AirshipSimulationConstants.RampTipLocalZMillimetres / 1000f);
            disembarkProbeOrigin.localRotation =
                Quaternion.LookRotation(Vector3.right, Vector3.up);

            var landingProbe = GetOrAddComponent<AirshipLandingSurfaceProbe>(instance);
            landingProbe.Configure(
                root,
                disembarkProbeOrigin,
                ~0,
                minimumReach: 0.40f,
                maximumReach: 2.50f);

            var motor = GetOrAddComponent<AirshipMotor>(instance);
            motor.Configure(root, collisionBody);

            var frame = GetOrAddComponent<AirshipFrame>(instance);
            frame.Configure(
                passengerSpace,
                RequireTransform(root, "REF_PilotCamera"),
                motor);

            var bridge = GetOrAddComponent<AirshipSimulationBridge>(instance);
            bridge.Configure(
                motor,
                frame,
                null,
                landingProbe,
                automaticAdvance: false);

            var boardingVolume =
                GetOrAddComponent<AirshipBoardingVolume>(boardingTrigger.gameObject);
            boardingVolume.Configure(frame, bridge, disembarkProbeOrigin);

            var pilotStation =
                GetOrAddComponent<AirshipPilotStation>(pilotTrigger.gameObject);
            pilotStation.Configure(
                frame,
                bridge,
                null,
                RequireTransform(root, "REF_PilotControls"),
                interactionDistance: 1.50f);
        }

        private static Material ResolveMaterial(
            Material source,
            Renderer renderer,
            IReadOnlyDictionary<AirshipMaterialRole, Material> materials)
        {
            var sourceName = source != null ? source.name : string.Empty;
            if (sourceName.IndexOf("OpaqueAtlas", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[AirshipMaterialRole.Opaque];
            }

            if (sourceName.IndexOf("Wood", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[AirshipMaterialRole.Wood];
            }

            if (sourceName.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[AirshipMaterialRole.Glass];
            }

            if (sourceName.IndexOf("EmissionAtlas", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[AirshipMaterialRole.Emission];
            }

            throw new InvalidOperationException(
                $"Renderer '{HierarchyPath(renderer.transform)}' uses unsupported source material " +
                $"'{(string.IsNullOrEmpty(sourceName) ? "<missing>" : sourceName)}'.");
        }

        private static void ConfigureMaskedOpaque(
            Material material,
            Texture2D baseTexture,
            Texture2D maskTexture,
            Texture2D detailTexture,
            float smoothnessMultiplier)
        {
            ConfigureOpaqueSurface(material);
            SetTexture(material, "_BaseMap", baseTexture);
            SetColor(material, "_BaseColor", Color.white);
            SetTexture(material, "_MetallicGlossMap", maskTexture);
            SetFloat(material, "_Metallic", 1f);
            SetFloat(material, "_Smoothness", smoothnessMultiplier);
            SetFloat(material, "_SmoothnessTextureChannel", 0f);
            SetTexture(material, "_DetailAlbedoMap", detailTexture);
            SetFloat(material, "_DetailAlbedoMapScale", 1f);
            SetFloat(material, "_UVSec", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_DETAIL_MULX2");
            material.DisableKeyword("_EMISSION");
            material.enableInstancing = true;
        }

        private static void ConfigureGlass(Material material)
        {
            SetColor(material, "_BaseColor", new Color(0.035f, 0.24f, 0.52f, 0.54f));
            SetFloat(material, "_Surface", 1f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_AlphaClip", 0f);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.72f);
            SetFloat(material, "_Cull", (float)CullMode.Back);
            SetFloat(material, "_ZWrite", 0f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloat(material, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, "_ReceiveShadows", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_EMISSION");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
        }

        private static void ConfigureEmission(Material material, Texture2D emissionTexture)
        {
            ConfigureOpaqueSurface(material);
            SetTexture(material, "_BaseMap", emissionTexture);
            SetColor(material, "_BaseColor", Color.white);
            SetTexture(material, "_EmissionMap", emissionTexture);
            SetColor(material, "_EmissionColor", Color.white * 4f);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.35f);
            material.EnableKeyword("_EMISSION");
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.enableInstancing = true;
        }

        private static void ConfigureOpaqueSurface(Material material)
        {
            SetFloat(material, "_Surface", 0f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_AlphaClip", 0f);
            SetFloat(material, "_Cull", (float)CullMode.Back);
            SetFloat(material, "_ZWrite", 1f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_ReceiveShadows", 1f);
            material.SetOverrideTag("RenderType", "Opaque");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            material.renderQueue = (int)RenderQueue.Geometry;
        }

        private static Material UpsertMaterial(
            string path,
            Shader shader,
            Action<Material> configure)
        {
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

            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ValidatePrefab(
            GameObject prefab,
            IReadOnlyDictionary<AirshipMaterialRole, Material> materials)
        {
            foreach (var transformName in RequiredTransforms)
            {
                if (FindRecursive(prefab.transform, transformName) == null)
                {
                    throw new InvalidOperationException(
                        $"Airship prefab is missing required transform '{transformName}'.");
                }
            }

            var expectedMaterials = new HashSet<Material>(materials.Values);
            var usedMaterials = new HashSet<Material>();
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !expectedMaterials.Contains(material))
                    {
                        throw new InvalidOperationException(
                            $"Renderer '{HierarchyPath(renderer.transform)}' has an invalid material assignment.");
                    }

                    usedMaterials.Add(material);
                }
            }

            foreach (var requiredRole in new[]
                     {
                         AirshipMaterialRole.Opaque,
                         AirshipMaterialRole.Glass,
                         AirshipMaterialRole.Emission
                     })
            {
                if (!usedMaterials.Contains(materials[requiredRole]))
                {
                    throw new InvalidOperationException(
                        $"Airship prefab does not use required material role '{requiredRole}'.");
                }
            }

            ValidateGameplayRig(prefab);

            var metrics = CollectMetrics(prefab);
            if (metrics.TriangleCount <= 0 || metrics.TriangleCount > 35000)
            {
                throw new InvalidOperationException(
                    $"Airship triangle count is outside the production budget: {metrics.TriangleCount}.");
            }

            if (metrics.CargoSlotCount != 0)
            {
                throw new InvalidOperationException(
                    $"The compact cockpit airship must not expose legacy cargo slots; found {metrics.CargoSlotCount}.");
            }

            var size = metrics.Bounds.size;
            if (!Approximately(size.x, 5.786f, 0.08f) ||
                !Approximately(size.y, 3.282f, 0.08f) ||
                !Approximately(size.z, 9.693f, 0.10f))
            {
                LogAxisDiagnostics(prefab, metrics.Bounds);
                throw new InvalidOperationException(
                    $"Unexpected Unity bounds ({size.x:F3}, {size.y:F3}, {size.z:F3}). " +
                    "Expected approximately (5.786, 3.282, 9.693) metres.");
            }
        }

        private static void ValidateGameplayRig(GameObject prefab)
        {
            var root = prefab.transform;
            var collisionBody = prefab.GetComponent<Rigidbody>();
            if (collisionBody == null
                || !collisionBody.isKinematic
                || collisionBody.useGravity
                || collisionBody.interpolation != RigidbodyInterpolation.Interpolate
                || collisionBody.collisionDetectionMode
                    != CollisionDetectionMode.ContinuousSpeculative)
            {
                throw new InvalidOperationException(
                    "The moving airship requires one interpolation-enabled "
                    + "kinematic Rigidbody for its authored collision meshes.");
            }

            var motor = RequireSingleComponent<AirshipMotor>(prefab);
            var frame = RequireSingleComponent<AirshipFrame>(prefab);
            var bridge = RequireSingleComponent<AirshipSimulationBridge>(prefab);
            var probe = RequireSingleComponent<AirshipLandingSurfaceProbe>(prefab);
            if (motor.VehicleRoot != root
                || motor.CollisionBody != collisionBody
                || bridge.Motor != motor
                || bridge.LandingProbe != probe
                || bridge.Passenger != null)
            {
                throw new InvalidOperationException(
                    "Airship presentation bridge references are incomplete or inconsistent.");
            }

            if (frame.Motor != motor
                || frame.PassengerSpace != RequireTransform(root, "SYS_PassengerSpace")
                || frame.PilotCameraAnchor
                    != RequireTransform(root, "REF_PilotCamera"))
            {
                throw new InvalidOperationException(
                    "Airship passenger frame is not connected to the authoritative motor.");
            }

            var markerNames = new[]
            {
                "COL_VEH_Envelope",
                "COL_VEH_Gondola",
                "COL_VEH_PropellerClearance",
                "COL_WALK_Floor"
            };
            for (var index = 0; index < markerNames.Length; index++)
            {
                if (RequireTransform(root, markerNames[index])
                    .GetComponent<Collider>() != null)
                {
                    throw new InvalidOperationException(
                        $"Legacy primitive marker '{markerNames[index]}' must not "
                        + "own a physical collider.");
                }
            }

            var allColliders = root.GetComponentsInChildren<Collider>(true);
            var triggerCount = 0;
            for (var index = 0; index < allColliders.Length; index++)
            {
                var collider = allColliders[index];
                if (collider.isTrigger)
                {
                    triggerCount++;
                    if (!(collider is BoxCollider) || !collider.enabled)
                    {
                        throw new InvalidOperationException(
                            $"Trigger '{collider.name}' must remain an enabled BoxCollider.");
                    }

                    continue;
                }

                if (!(collider is MeshCollider))
                {
                    throw new InvalidOperationException(
                        $"Physical collider '{collider.name}' is a "
                        + $"{collider.GetType().Name}; AIR-COL-001 permits only "
                        + "exact MeshColliders.");
                }
            }

            if (triggerCount != 3)
            {
                throw new InvalidOperationException(
                    $"Expected three logical interaction triggers; found {triggerCount}.");
            }

            var accessDoor = RequireTransform(root, "ANM_AccessDoor");
            var sourceRoot = RequireAsset<GameObject>(ModelPath).transform;
            var sourceDoor = RequireTransform(sourceRoot, "ANM_AccessDoor");
            var expectedOpenDoorRotation = ComputeOpenAccessDoorRotation(
                sourceRoot,
                sourceDoor,
                sourceDoor.localRotation);
            if (Quaternion.Angle(
                    accessDoor.localRotation,
                    expectedOpenDoorRotation) > 0.1f)
            {
                throw new InvalidOperationException(
                    "The gameplay airship prefab must keep its starboard access door open.");
            }

            var accessDoorController =
                RequireSingleComponent<AirshipAccessDoor>(prefab);
            if (bridge.AccessDoor != accessDoorController
                || accessDoorController.VehicleRoot != root
                || accessDoorController.DoorRoot != accessDoor)
            {
                throw new InvalidOperationException(
                    "The access-door controller must own the authored hinge and "
                    + "share the airship vehicle root.");
            }

            var playerColliders = root.GetComponentsInChildren<MeshCollider>(true);
            var playerColliderCount = 0;
            var requiredCookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation
                | MeshColliderCookingOptions.EnableMeshCleaning
                | MeshColliderCookingOptions.WeldColocatedVertices
                | MeshColliderCookingOptions.UseFastMidphase;
            for (var index = 0; index < playerColliders.Length; index++)
            {
                if (!playerColliders[index].name.StartsWith(
                        "COLMESH_PLAYER_",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                playerColliderCount++;
                if (!playerColliders[index].enabled
                    || playerColliders[index].isTrigger
                    || playerColliders[index].convex
                    || playerColliders[index].sharedMesh == null
                    || playerColliders[index].GetComponent<MeshFilter>() == null
                    || playerColliders[index].sharedMesh
                        != playerColliders[index].GetComponent<MeshFilter>().sharedMesh
                    || playerColliders[index].cookingOptions
                        != requiredCookingOptions
                    || playerColliders[index].GetComponent<Renderer>() != null)
                {
                    throw new InvalidOperationException(
                        $"Player collider '{playerColliders[index].name}' "
                        + "must be an enabled, renderer-free, cleaned non-convex "
                        + "collision volume derived from its authored surface.");
                }
            }

            if (playerColliderCount != ExpectedPrecisePlayerCollisionMeshCount)
            {
                throw new InvalidOperationException(
                    $"Expected {ExpectedPrecisePlayerCollisionMeshCount} exact "
                    + $"airship collision meshes; found {playerColliderCount}.");
            }

            var probeOrigin = RequireTransform(root, "SYS_DisembarkProbeOrigin");
            var boarding = RequireTransform(root, "TRG_BoardingArea")
                .GetComponent<AirshipBoardingVolume>();
            var station = RequireTransform(root, "TRG_PilotSeat")
                .GetComponent<AirshipPilotStation>();
            if (boarding == null || boarding.Frame != frame
                || boarding.OutboardDirectionReference != probeOrigin
                || boarding.SimulationBridge != bridge
                || station == null || station.Frame != frame
                || station.SimulationBridge != bridge
                || station.InteractionPoint != RequireTransform(root, "REF_PilotControls"))
            {
                throw new InvalidOperationException(
                    "Boarding threshold or physical pilot station is not wired to the airship frame.");
            }

            if (probe.GangwayOrigin != probeOrigin
                || probeOrigin.parent != root
                || Vector3.Distance(
                    probeOrigin.localPosition,
                    new Vector3(
                        AirshipSimulationConstants.RampTipLocalXMillimetres / 1000f,
                        AirshipSimulationConstants.RampTipLocalYMillimetres / 1000f,
                        AirshipSimulationConstants.RampTipLocalZMillimetres / 1000f))
                    > 0.0001f
                || Vector3.Dot(probeOrigin.forward, root.right) < 0.999f)
            {
                throw new InvalidOperationException(
                    "The landing probe must match the canonical ramp tip and point outboard.");
            }
        }

        private static void LogAxisDiagnostics(GameObject root, Bounds totalBounds)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var bounds = renderer.bounds;
                if (Mathf.Abs(bounds.min.y - totalBounds.min.y) < 0.02f ||
                    Mathf.Abs(bounds.max.y - totalBounds.max.y) < 0.02f ||
                    Mathf.Abs(bounds.min.z - totalBounds.min.z) < 0.02f ||
                    Mathf.Abs(bounds.max.z - totalBounds.max.z) < 0.02f)
                {
                    Debug.LogWarning(
                        $"AIRSHIP_AXIS_DIAGNOSTIC renderer={HierarchyPath(renderer.transform)} " +
                        $"boundsCenter=({bounds.center.x:F3},{bounds.center.y:F3},{bounds.center.z:F3}) " +
                        $"boundsSize=({bounds.size.x:F3},{bounds.size.y:F3},{bounds.size.z:F3}) " +
                        $"localEuler=({renderer.transform.localEulerAngles.x:F3}," +
                        $"{renderer.transform.localEulerAngles.y:F3},{renderer.transform.localEulerAngles.z:F3})");
                }
            }

            foreach (var transformName in new[]
                     {
                         "AIR_Airship",
                         "GEO_Envelope",
                         "ANM_BoardingRamp",
                         "GEO_BoardingRamp",
                         "ANM_PropellerRotor"
                     })
            {
                var transform = FindRecursive(root.transform, transformName);
                if (transform != null)
                {
                    Debug.LogWarning(
                        $"AIRSHIP_TRANSFORM_DIAGNOSTIC name={transformName} " +
                        $"localPosition=({transform.localPosition.x:F3},{transform.localPosition.y:F3}," +
                        $"{transform.localPosition.z:F3}) localEuler=({transform.localEulerAngles.x:F3}," +
                        $"{transform.localEulerAngles.y:F3},{transform.localEulerAngles.z:F3}) " +
                        $"localScale=({transform.localScale.x:F3},{transform.localScale.y:F3}," +
                        $"{transform.localScale.z:F3})");
                }
            }
        }

        private static AirshipMetrics CollectMetrics(GameObject root)
        {
            var bounds = new Bounds();
            var hasBounds = false;
            var rendererCount = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                rendererCount++;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            var meshes = new HashSet<Mesh>();
            long triangleCount = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && meshes.Add(filter.sharedMesh))
                {
                    for (var subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                    {
                        triangleCount += (long)filter.sharedMesh.GetIndexCount(subMesh) / 3L;
                    }
                }
            }

            var cargoSlotCount = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith("GEO_CargoSlot_", StringComparison.Ordinal))
                {
                    cargoSlotCount++;
                }
            }

            return new AirshipMetrics(rendererCount, meshes.Count, triangleCount, cargoSlotCount, bounds);
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name)
                {
                    return transform;
                }
            }

            return null;
        }

        private static Transform RequireTransform(Transform root, string name)
        {
            var result = FindRecursive(root, name);
            if (result == null)
            {
                throw new InvalidOperationException(
                    $"Airship hierarchy is missing required transform '{name}'.");
            }

            return result;
        }

        private static Transform UpsertChild(Transform parent, string name)
        {
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (child.name == name)
                {
                    child.localPosition = Vector3.zero;
                    child.localRotation = Quaternion.identity;
                    child.localScale = Vector3.one;
                    return child;
                }
            }

            var gameObject = new GameObject(name);
            var transform = gameObject.transform;
            transform.SetParent(parent, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            return transform;
        }

        private static T GetOrAddComponent<T>(GameObject gameObject)
            where T : Component
        {
            var components = gameObject.GetComponents<T>();
            if (components.Length > 1)
            {
                throw new InvalidOperationException(
                    $"'{HierarchyPath(gameObject.transform)}' has duplicate {typeof(T).Name} components.");
            }

            return components.Length == 1 ? components[0] : gameObject.AddComponent<T>();
        }

        private static T RequireSingleComponent<T>(GameObject gameObject)
            where T : Component
        {
            var components = gameObject.GetComponents<T>();
            if (components.Length != 1)
            {
                throw new InvalidOperationException(
                    $"'{HierarchyPath(gameObject.transform)}' requires exactly one {typeof(T).Name}; "
                    + $"found {components.Length}.");
            }

            return components[0];
        }

        private static BoxCollider ConfigureBoxCollider(
            Transform owner,
            Vector3 center,
            Vector3 size,
            bool isTrigger,
            bool enabled)
        {
            var collider = GetOrAddComponent<BoxCollider>(owner.gameObject);
            collider.center = center;
            collider.size = size;
            collider.isTrigger = isTrigger;
            collider.enabled = enabled;
            return collider;
        }

        private static void ConfigurePlayerCollisionMeshes(Transform root)
        {
            var collisionOwners = root.GetComponentsInChildren<Transform>(true);
            var configuredCount = 0;
            for (var index = 0; index < collisionOwners.Length; index++)
            {
                var owner = collisionOwners[index];
                if (!owner.name.StartsWith(
                        "COLMESH_PLAYER_",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var filter = owner.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        $"Collision proxy '{owner.name}' has no mesh.");
                }

                foreach (var collider in owner.GetComponents<Collider>())
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                var meshCollider = owner.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
                meshCollider.enabled = true;
                meshCollider.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation
                    | MeshColliderCookingOptions.EnableMeshCleaning
                    | MeshColliderCookingOptions.WeldColocatedVertices
                    | MeshColliderCookingOptions.UseFastMidphase;

                foreach (var renderer in owner.GetComponents<Renderer>())
                {
                    UnityEngine.Object.DestroyImmediate(renderer);
                }

                configuredCount++;
            }

            if (configuredCount != ExpectedPrecisePlayerCollisionMeshCount)
            {
                throw new InvalidOperationException(
                    $"Expected {ExpectedPrecisePlayerCollisionMeshCount} authored "
                    + $"precise collision meshes; configured {configuredCount}.");
            }
        }

        private static CapsuleCollider ConfigureCapsuleCollider(
            Transform owner,
            float radius,
            float height,
            int direction,
            bool enabled)
        {
            var collider = GetOrAddComponent<CapsuleCollider>(owner.gameObject);
            collider.center = Vector3.zero;
            collider.radius = radius;
            collider.height = height;
            collider.direction = direction;
            collider.isTrigger = false;
            collider.enabled = enabled;
            return collider;
        }

        private static SphereCollider ConfigureSphereCollider(
            Transform owner,
            float radius,
            bool enabled)
        {
            var collider = GetOrAddComponent<SphereCollider>(owner.gameObject);
            collider.center = Vector3.zero;
            collider.radius = radius;
            collider.isTrigger = false;
            collider.enabled = enabled;
            return collider;
        }

        private static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                names.Push(current.name);
            }

            return string.Join("/", names);
        }

        private static bool Approximately(float actual, float expected, float tolerance)
        {
            return Mathf.Abs(actual - expected) <= tolerance;
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException($"Required Unity asset is missing or failed to import: {path}");
            }

            return asset;
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

        private static void SetTexture(Material material, string property, Texture value)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, value);
            }
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static Quaternion ComputeOpenAccessDoorRotation(
            Transform root,
            Transform accessDoor,
            Quaternion closedDoorRotation)
        {
            var localDoorHingeAxis = accessDoor.InverseTransformDirection(
                root.up).normalized;
            return closedDoorRotation
                * Quaternion.AngleAxis(100f, localDoorHingeAxis);
        }

        private enum AirshipMaterialRole
        {
            Opaque,
            Wood,
            Glass,
            Emission
        }

        private readonly struct AirshipMetrics
        {
            public AirshipMetrics(
                int rendererCount,
                int meshCount,
                long triangleCount,
                int cargoSlotCount,
                Bounds bounds)
            {
                RendererCount = rendererCount;
                MeshCount = meshCount;
                TriangleCount = triangleCount;
                CargoSlotCount = cargoSlotCount;
                Bounds = bounds;
            }

            public int RendererCount { get; }
            public int MeshCount { get; }
            public long TriangleCount { get; }
            public int CargoSlotCount { get; }
            public Bounds Bounds { get; }
        }
    }

    /// <summary>
    /// Keeps regenerated Blender exports deterministic on every Unity import.
    /// </summary>
    internal sealed class AirshipAssetPostprocessor : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Art/Vehicles/Airship";
        private const string ModelPath = Root + "/Models/AIR_Airship.fbx";
        private const string TextureRoot = Root + "/Textures/";

        private void OnPreprocessModel()
        {
            if (!string.Equals(assetPath, ModelPath, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importConstraints = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.bakeAxisConversion = false;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureRoot, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            var isMask = fileName.EndsWith("_Mask", StringComparison.Ordinal);
            var isWood = fileName.IndexOf("_Wood_", StringComparison.Ordinal) >= 0;

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = !isMask;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = isWood ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.filterMode = isWood ? FilterMode.Bilinear : FilterMode.Point;
            importer.mipmapEnabled = isWood;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
        }
    }
}
