using System;
using System.Collections.Generic;
using System.IO;
using CML.Unity.Mining;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity-side setup for the modular ore-deposit kit.
    /// Blender owns geometry, UVs and module markers; Unity owns import
    /// settings, material assets, static colliders and deposit prefabs.
    /// </summary>
    public static class OreDepositAssetSetup
    {
        private const string Root = "Assets/_Project/Art/Environment/OreDeposit";
        private const string ModelsRoot = Root + "/Models";
        private const string MaterialsRoot = Root + "/Materials";
        private const string PrefabsRoot = Root + "/Prefabs";
        private const string ModulePrefabsRoot = PrefabsRoot + "/Modules";

        /// <summary>
        /// The deposit is cut from the island's own stone, so it renders with
        /// the same stylised surface shader as every painted boulder. That is
        /// what gives it the soft shading and, above all, the terrain contact
        /// blend that makes a rock sit in the grass instead of on top of it.
        /// </summary>
        private const string SurfaceShaderName =
            "CML/Environment/Starter Island Stylized Surface";

        private static readonly ModuleDefinition[] Modules =
        {
            new ModuleDefinition("L01", "ENV_Ore_L01_LandmarkWedge.fbx"),
            new ModuleDefinition("M01", "ENV_Ore_M01_TallWedge.fbx"),
            new ModuleDefinition("M02", "ENV_Ore_M02_BroadSteps.fbx"),
            new ModuleDefinition("M03", "ENV_Ore_M03_TwinSpire.fbx"),
            new ModuleDefinition("S01", "ENV_Ore_S01_LowBlade.fbx"),
            new ModuleDefinition("S02", "ENV_Ore_S02_FracturedBlock.fbx"),
            new ModuleDefinition("S03", "ENV_Ore_S03_RockPair.fbx"),
            new ModuleDefinition("S04", "ENV_Ore_S04_OreShelf.fbx"),
            new ModuleDefinition("S05", "ENV_Ore_S05_CompactNode.fbx"),
            new ModuleDefinition("G01", "ENV_Ore_G01_LongPlate.fbx"),
            new ModuleDefinition("G02", "ENV_Ore_G02_RoundPlate.fbx"),
            new ModuleDefinition("G03", "ENV_Ore_G03_SplitPlate.fbx"),
            new ModuleDefinition("R01", "ENV_Ore_R01_LargeRubble.fbx"),
            new ModuleDefinition("R02", "ENV_Ore_R02_SmallRubble.fbx")
        };

        private static readonly LayoutDefinition[] Layouts =
        {
            new LayoutDefinition(
                "A",
                1.00f,
                new[]
                {
                    P("L01", 0.00f, 2.35f, 0.0f, 1.00f),
                    P("M01", -2.35f, 1.45f, 18.0f, 0.98f),
                    P("M02", 2.20f, 1.50f, -22.0f, 1.00f),
                    P("M03", 2.70f, -0.25f, -64.0f, 0.96f),
                    P("S01", -2.75f, -0.30f, 62.0f, 0.98f),
                    P("S02", -1.95f, -1.90f, 34.0f, 0.96f),
                    P("S03", 1.95f, -1.85f, -38.0f, 1.00f),
                    P("S04", 3.05f, 0.78f, -78.0f, 0.96f),
                    P("S05", -3.05f, 0.78f, 76.0f, 1.04f),
                    P("G01", -1.45f, -0.82f, 22.0f, 0.98f),
                    P("G02", 1.32f, -0.78f, -12.0f, 1.00f),
                    P("G03", 0.05f, 0.82f, 8.0f, 1.08f),
                    P("R01", -1.25f, 0.72f, -15.0f, 0.98f),
                    P("R02", 1.28f, 0.75f, 12.0f, 1.04f),
                    P("M01", 1.38f, 2.65f, -18.0f, 0.86f),
                    P("M02", -2.80f, 0.25f, 72.0f, 0.84f),
                    P("S02", -1.40f, 2.60f, 18.0f, 0.86f),
                    P("S04", 1.15f, 2.75f, -12.0f, 0.86f),
                    P("G02", -2.70f, -1.58f, 34.0f, 0.90f),
                    P("G03", 2.70f, -1.52f, -32.0f, 0.92f),
                    P("R01", -2.80f, 2.05f, 12.0f, 0.86f),
                    P("R02", 2.75f, 2.08f, -14.0f, 0.90f)
                }),
            new LayoutDefinition(
                "B",
                0.82f,
                new[]
                {
                    P("L01", -3.20f, 2.70f, 28.0f, 1.00f),
                    P("M01", -1.15f, 1.68f, 18.0f, 0.96f),
                    P("M03", 1.10f, 0.55f, 20.0f, 1.02f),
                    P("M02", 3.25f, -0.68f, 24.0f, 1.00f),
                    P("S01", -4.20f, 0.62f, 68.0f, 0.94f),
                    P("S02", -2.15f, -0.10f, 26.0f, 1.04f),
                    P("S03", 2.25f, 1.72f, -66.0f, 0.98f),
                    P("S04", 4.22f, 1.08f, -72.0f, 1.02f),
                    P("S05", 4.12f, -2.38f, -14.0f, 0.96f),
                    P("G01", -2.70f, -1.55f, 30.0f, 1.04f),
                    P("G02", 0.08f, 2.55f, 18.0f, 0.96f),
                    P("G03", 1.48f, -1.15f, 24.0f, 1.00f),
                    P("R01", -0.72f, 0.08f, -6.0f, 1.06f),
                    P("R02", 3.12f, 0.56f, 8.0f, 0.94f),
                    P("M01", -4.25f, 3.45f, 38.0f, 0.86f),
                    P("M02", 4.35f, -3.15f, 30.0f, 0.84f),
                    P("S01", -3.75f, -2.85f, 48.0f, 0.92f),
                    P("S03", 1.40f, 2.85f, -54.0f, 0.88f),
                    P("G02", -4.45f, -1.65f, 26.0f, 0.90f),
                    P("G03", 3.40f, 2.35f, 18.0f, 0.88f),
                    P("R01", -2.55f, 2.80f, 20.0f, 0.90f),
                    P("R02", 4.32f, -0.85f, -16.0f, 0.90f)
                }),
            new LayoutDefinition(
                "C",
                0.82f,
                new[]
                {
                    P("L01", 0.10f, 3.22f, 2.0f, 1.00f),
                    P("M01", -3.22f, 0.62f, 72.0f, 0.98f),
                    P("M02", 3.18f, 0.58f, -72.0f, 1.02f),
                    P("M03", 0.02f, -3.05f, 178.0f, 1.00f),
                    P("S01", -2.40f, 2.42f, 42.0f, 0.95f),
                    P("S02", 2.38f, 2.35f, -38.0f, 1.04f),
                    P("S03", -3.22f, -1.58f, 84.0f, 1.00f),
                    P("S04", 3.18f, -1.62f, -82.0f, 0.96f),
                    P("S05", 1.25f, -3.62f, 162.0f, 1.06f),
                    P("G01", -1.72f, -2.40f, 138.0f, 0.96f),
                    P("G02", 1.70f, -2.25f, -142.0f, 1.02f),
                    P("G03", 0.02f, 1.38f, 2.0f, 1.00f),
                    P("R01", -1.58f, 1.92f, 18.0f, 1.02f),
                    P("R02", 1.65f, 1.92f, -20.0f, 0.98f),
                    P("M01", -4.05f, 2.25f, 58.0f, 0.86f),
                    P("M02", 4.05f, 2.18f, -58.0f, 0.84f),
                    P("S01", -4.15f, -0.82f, 84.0f, 0.92f),
                    P("S03", 4.12f, -0.88f, -82.0f, 0.90f),
                    P("G02", -3.05f, -3.05f, 126.0f, 0.90f),
                    P("G03", 3.08f, -3.02f, -128.0f, 0.88f),
                    P("R01", -2.05f, 3.05f, 22.0f, 0.90f),
                    P("R02", 2.12f, 3.04f, -20.0f, 0.90f)
                })
        };

        /// <summary>
        /// Surface colours of the four deposit materials. Rock repeats the
        /// Starter Island stone verbatim; each ore keeps the same shading model
        /// and only moves the three colours.
        /// </summary>
        private static readonly SurfaceRole[] OreRoles =
        {
            new SurfaceRole(
                "Rock",
                new Color(0.62352943f, 0.6392157f, 0.61960787f, 1f),
                new Color(0.84313726f, 0.7921569f, 0.73333335f, 1f),
                new Color(0.44705883f, 0.48235294f, 0.45490196f, 1f),
                isHostStone: true),
            new SurfaceRole(
                "Iron",
                new Color(0.56078434f, 0.34901962f, 0.28627452f, 1f),
                new Color(0.72156864f, 0.50588238f, 0.41176471f, 1f),
                new Color(0.38431373f, 0.23529412f, 0.19215687f, 1f)),
            new SurfaceRole(
                "Copper",
                new Color(0.74117649f, 0.45882353f, 0.27843139f, 1f),
                new Color(0.87058824f, 0.61960787f, 0.42352942f, 1f),
                new Color(0.51764709f, 0.30588236f, 0.18431373f, 1f)),
            new SurfaceRole(
                "Tin",
                new Color(0.65490198f, 0.69411767f, 0.71372551f, 1f),
                new Color(0.81960785f, 0.85098040f, 0.85490197f, 1f),
                new Color(0.43137255f, 0.48235294f, 0.50588238f, 1f))
        };

        [MenuItem("CML/Art/Rebuild Ore Deposit Kit")]
        public static void Run()
        {
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);
            EnsureFolder(ModulePrefabsRoot);

            ImportInputs();

            var shader = Shader.Find(SurfaceShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"The shader '{SurfaceShaderName}' is unavailable. It is the " +
                    "one the Starter Island rocks use and the deposit must match it.");
            }

            var materials = BuildMaterials(shader);
            ConfigureModelMaterialRemaps(materials["Rock"], materials["Iron"]);
            var modulePrefabs = BuildModulePrefabs(
                materials["Rock"],
                materials["Iron"]);

            var prefabs = new List<GameObject>();
            foreach (var role in OreRoles)
            {
                if (role.IsHostStone)
                {
                    continue;
                }

                foreach (var layout in Layouts)
                {
                    var prefab = BuildDepositPrefab(
                        layout,
                        role.Name,
                        materials["Rock"],
                        materials[role.Name],
                        modulePrefabs);
                    ValidateDepositPrefab(
                        prefab,
                        layout,
                        materials["Rock"],
                        materials[role.Name]);
                    prefabs.Add(prefab);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode && prefabs.Count > 0)
            {
                Selection.activeObject = prefabs[0];
            }

            Debug.Log(
                $"ORE_DEPOSIT_UNITY_VALIDATION modules={Modules.Length} " +
                $"modulePrefabs={modulePrefabs.Count} prefabs={prefabs.Count} " +
                $"layouts={Layouts.Length} materials={materials.Count} " +
                "terrainMeshes=0 colliders=1_per_module+1_deposit_surface status=PASS");
        }

        /// <summary>
        /// Repairs generated deposit prefab assets in place. This deliberately
        /// does not open or save a scene: existing scene instances keep their
        /// authoring and only the reusable prefab contract is brought up to
        /// date.
        /// </summary>
        [MenuItem("CML/Art/Repair Ore Deposit Prefab Contracts")]
        public static void RepairGeneratedPrefabs()
        {
            var repairedModules = RepairModulePrefabColliders();
            var repaired = 0;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsRoot });
            for (var index = 0; index < guids.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[index]);
                var prefabName = Path.GetFileNameWithoutExtension(path);
                if (!prefabName.StartsWith("PF_OreDeposit_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetOreName(prefabName, out var oreName))
                {
                    Debug.LogWarning($"Skipping ore deposit prefab with unknown role: {path}");
                    continue;
                }

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var identity = ManualMiningSourceIdentity.EnsureInfiniteDepositSurface(
                        contents,
                        "prefab.ore-deposit." + oreName.ToLowerInvariant() + "." +
                        prefabName.Substring(prefabName.LastIndexOf('_') + 1).ToLowerInvariant());
                    identity.ConfigureDepositOre("item.raw_" + oreName.ToLowerInvariant());

                    var moduleIndex = 0;
                    foreach (Transform child in contents.transform)
                    {
                        if (!child.name.StartsWith("MOD_", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        moduleIndex++;
                        ConfigureFiniteIronRockSource(
                            child.gameObject,
                            oreName,
                            prefabName.Substring(prefabName.LastIndexOf('_') + 1),
                            moduleIndex);
                    }

                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    repaired++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"ORE_DEPOSIT_PREFAB_REPAIR modules={repairedModules} " +
                $"deposits={repaired} status=PASS");
        }

        private static int RepairModulePrefabColliders()
        {
            var repaired = 0;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { ModulePrefabsRoot });
            for (var index = 0; index < guids.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[index]);
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var filters = contents.GetComponentsInChildren<MeshFilter>(true);
                    if (filters.Length != 1 || filters[0].sharedMesh == null)
                    {
                        Debug.LogWarning(
                            $"Skipping module without a single mesh filter: {path}");
                        continue;
                    }

                    var filter = filters[0];
                    var colliders = filter.GetComponents<MeshCollider>();
                    for (var colliderIndex = 1;
                         colliderIndex < colliders.Length;
                         colliderIndex++)
                    {
                        UnityEngine.Object.DestroyImmediate(colliders[colliderIndex], true);
                    }

                    var collider = colliders.Length > 0
                        ? colliders[0]
                        : filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false;
                    collider.isTrigger = false;
                    collider.enabled = true;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    repaired++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            return repaired;
        }

        private static void ImportInputs()
        {
            foreach (var module in Modules)
            {
                AssetDatabase.ImportAsset(
                    module.ModelPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        private static Dictionary<string, Material> BuildMaterials(Shader shader)
        {
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var role in OreRoles)
            {
                var materialPath = MaterialsRoot + "/M_OreDeposit_" + role.Name + ".mat";
                materials.Add(role.Name, UpsertMaterial(materialPath, shader, role));
            }

            return materials;
        }

        private static Material UpsertMaterial(
            string path,
            Shader shader,
            SurfaceRole role)
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

            // Numbers copied from M_StarterIsland_DetailRock. The host stone is
            // the island stone; an ore only shifts the three surface colours.
            SetColor(material, "_BaseColor", role.Base);
            SetColor(material, "_Color", role.Base);
            SetColor(material, "_SecondaryColor", role.Base);
            SetColor(material, "_RockTopColor", role.Top);
            SetColor(material, "_RockUnderColor", role.Under);
            SetColor(material, "_WetColor", role.Under);
            SetColor(
                material,
                "_RockContactGrassColor",
                new Color(0.28627452f, 0.41568628f, 0.20784314f, 1f));
            SetColor(
                material,
                "_RockContactDeepGrassColor",
                new Color(0.19215687f, 0.29803923f, 0.16862746f, 1f));
            SetColor(
                material,
                "_RockContactDirtColor",
                new Color(0.7176471f, 0.56078434f, 0.3764706f, 1f));
            SetColor(
                material,
                "_RockContactCliffColor",
                new Color(0.5294118f, 0.3137255f, 0.24705882f, 1f));
            SetFloat(material, "_VertexBlend", 0f);
            SetFloat(material, "_AmbientStrength", 0.92f);
            SetFloat(material, "_ShadowFloor", 0.5f);
            SetFloat(material, "_ColorVariation", 0.025f);
            SetFloat(material, "_RockDetail", 1f);
            SetFloat(material, "_RockTopStrength", 0.68f);
            SetFloat(material, "_RockUnderStrength", 0.34f);
            SetFloat(material, "_RockMacroScale", 0.48f);
            SetFloat(material, "_RockMacroStrength", 0.105f);
            SetFloat(material, "_RockGrainScale", 4.6f);
            SetFloat(material, "_RockGrainStrength", 0.045f);
            SetFloat(material, "_RockContactBlend", 0.74f);
            SetFloat(material, "_RockContactHeight", 0.24f);
            SetFloat(material, "_RockContactFeather", 0.2f);
            SetFloat(material, "_RockContactNoise", 0.14f);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.04f);
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureModelMaterialRemaps(
            Material rockMaterial,
            Material oreMaterial)
        {
            foreach (var module in Modules)
            {
                var importer = AssetImporter.GetAtPath(module.ModelPath) as ModelImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException(
                        $"Could not load ModelImporter for {module.ModelPath}.");
                }

                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(
                        typeof(Material),
                        "M_OreDeposit_Rock"),
                    rockMaterial);
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(
                        typeof(Material),
                        "M_OreDeposit_Iron"),
                    oreMaterial);
                importer.SaveAndReimport();
            }
        }

        private static GameObject BuildDepositPrefab(
            LayoutDefinition layout,
            string oreName,
            Material rockMaterial,
            Material oreMaterial,
            IReadOnlyDictionary<string, GameObject> modulePrefabs)
        {
            var prefabName = $"PF_OreDeposit_{oreName}_{layout.Name}";
            var prefabPath = PrefabsRoot + "/" + prefabName + ".prefab";
            var root = new GameObject(prefabName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            try
            {
                CreateMarker(root.transform, "REF_DepositCenter", Vector3.zero);
                CreateMarker(
                    root.transform,
                    "REF_ExtractorAnchor",
                    new Vector3(0f, 0.02f, 0f));

                for (var index = 0; index < layout.Placements.Length; index++)
                {
                    var placement = layout.Placements[index];
                    var module = FindModule(placement.ModuleId);
                    var source = modulePrefabs[module.Id];
                    var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                    if (instance == null)
                    {
                        throw new InvalidOperationException(
                            $"Could not instantiate module model {module.ModelPath}.");
                    }

                    instance.name = $"MOD_{index:00}_{placement.ModuleId}";
                    instance.transform.SetParent(root.transform, false);
                    instance.transform.localPosition = new Vector3(
                        placement.X * layout.SpatialScale,
                        0f,
                        placement.Y * layout.SpatialScale);
                    instance.transform.localRotation =
                        Quaternion.Euler(0f, -placement.RotationDegrees, 0f);
                    instance.transform.localScale = Vector3.one * placement.Scale;

                    ConfigureModuleInstance(instance, rockMaterial, oreMaterial);
                    ConfigureFiniteIronRockSource(
                        instance,
                        oreName,
                        layout.Name,
                        index + 1);
                }

                var surface = ManualMiningSourceIdentity.EnsureInfiniteDepositSurface(
                    root,
                    "prefab.ore-deposit." + oreName.ToLowerInvariant() + "." +
                    layout.Name.ToLowerInvariant());
                surface.ConfigureDepositOre("item.raw_" + oreName.ToLowerInvariant());

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Could not save prefab {prefabPath}.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureFiniteIronRockSource(
            GameObject module,
            string oreName,
            string layoutName,
            int moduleIndex)
        {
            // Manual pickaxe mining is currently authored for the iron
            // deposit. Copper and tin already expose their drill surface, but
            // their finite hand-mining rewards have not been designed yet.
            if (!string.Equals(oreName, "Iron", StringComparison.Ordinal) ||
                module.name.IndexOf("_G0", StringComparison.Ordinal) >= 0)
            {
                return;
            }

            var identity = module.GetComponent<ManualMiningSourceIdentity>();
            if (identity == null)
            {
                identity = module.AddComponent<ManualMiningSourceIdentity>();
            }

            identity.Configure(
                ManualMiningSourceKind.IronOreRock,
                "prefab.ore-deposit.iron." + layoutName.ToLowerInvariant() +
                ".rock." + moduleIndex.ToString("00"));
            identity.EnsureMiningMeshColliders();
        }

        private static Dictionary<string, GameObject> BuildModulePrefabs(
            Material rockMaterial,
            Material oreMaterial)
        {
            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (var module in Modules)
            {
                var source = RequireAsset<GameObject>(module.ModelPath);
                var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate module model {module.ModelPath}.");
                }

                try
                {
                    instance.name = module.PrefabName;
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                    ConfigureModuleInstance(instance, rockMaterial, oreMaterial);

                    var prefab = PrefabUtility.SaveAsPrefabAsset(instance, module.PrefabPath);
                    if (prefab == null)
                    {
                        throw new InvalidOperationException(
                            $"Could not save module prefab {module.PrefabPath}.");
                    }

                    ValidateModulePrefab(prefab, module, rockMaterial, oreMaterial);
                    prefabs.Add(module.Id, prefab);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            return prefabs;
        }

        private static void ValidateModulePrefab(
            GameObject prefab,
            ModuleDefinition module,
            Material rockMaterial,
            Material oreMaterial)
        {
            if (prefab.transform.localPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) > 0.001f ||
                (prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                throw new InvalidOperationException(
                    $"Module prefab {module.PrefabName} root transform is not identity.");
            }

            foreach (var markerName in new[] { "REF_Placement", "REF_Hit", "REF_Drop" })
            {
                if (FindRecursive(prefab.transform, markerName) == null)
                {
                    throw new InvalidOperationException(
                        $"Module prefab {module.PrefabName} is missing {markerName}.");
                }
            }

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            var colliders = prefab.GetComponentsInChildren<MeshCollider>(true);
            if (renderers.Length != 1 || filters.Length != 1 || colliders.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Module prefab {module.PrefabName} has invalid component counts.");
            }

            var assigned = renderers[0].sharedMaterials;
            if (assigned.Length != 2 ||
                assigned[0] != rockMaterial ||
                assigned[1] != oreMaterial ||
                colliders[0].sharedMesh != filters[0].sharedMesh ||
                colliders[0].convex)
            {
                throw new InvalidOperationException(
                    $"Module prefab {module.PrefabName} failed material/collider validation.");
            }
        }

        private static void ConfigureModuleInstance(
            GameObject moduleRoot,
            Material rockMaterial,
            Material oreMaterial)
        {
            var renderers = moduleRoot.GetComponentsInChildren<MeshRenderer>(true);
            var filters = moduleRoot.GetComponentsInChildren<MeshFilter>(true);
            if (renderers.Length != 1 || filters.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Module {moduleRoot.name} must contain exactly one renderer and one mesh.");
            }

            if (filters[0].sharedMesh == null || filters[0].sharedMesh.subMeshCount != 2)
            {
                throw new InvalidOperationException(
                    $"Module {moduleRoot.name} must contain one mesh with two submeshes.");
            }

            renderers[0].sharedMaterials = new[] { rockMaterial, oreMaterial };

            // One module is one boulder, so its single static collider is the
            // exact silhouette the pickaxe hits and destroys. Trigger and
            // enabled state are pinned as well: a disabled or trigger collider
            // silently turns a mineable rock into scenery.
            var colliders = filters[0].GetComponents<MeshCollider>();
            for (var index = 1; index < colliders.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[index], true);
            }

            var collider = colliders.Length > 0
                ? colliders[0]
                : filters[0].gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filters[0].sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            collider.enabled = true;
            SetStaticRecursively(moduleRoot);

            foreach (var markerName in new[] { "REF_Placement", "REF_Hit", "REF_Drop" })
            {
                if (FindRecursive(moduleRoot.transform, markerName) == null)
                {
                    throw new InvalidOperationException(
                        $"Module {moduleRoot.name} is missing marker {markerName}.");
                }
            }
        }

        private static void ValidateDepositPrefab(
            GameObject prefab,
            LayoutDefinition layout,
            Material rockMaterial,
            Material oreMaterial)
        {
            if (prefab.transform.localPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) > 0.001f ||
                (prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                throw new InvalidOperationException(
                    $"Prefab {prefab.name} root transform is not identity.");
            }

            if (FindRecursive(prefab.transform, "REF_DepositCenter") == null ||
                FindRecursive(prefab.transform, "REF_ExtractorAnchor") == null)
            {
                throw new InvalidOperationException(
                    $"Prefab {prefab.name} is missing its deposit markers.");
            }

            var extractionSurface = prefab.transform.Find(
                ManualMiningSourceIdentity.InfiniteDepositSurfaceName);
            var extractionCollider = extractionSurface == null
                ? null
                : extractionSurface.GetComponent<BoxCollider>();
            var extractionIdentity = extractionSurface == null
                ? null
                : extractionSurface.GetComponent<ManualMiningSourceIdentity>();
            if (extractionSurface == null ||
                extractionCollider == null ||
                !extractionCollider.enabled ||
                !extractionCollider.isTrigger ||
                extractionIdentity == null ||
                !extractionIdentity.IsMachineExtractable)
            {
                throw new InvalidOperationException(
                    $"Prefab {prefab.name} is missing its drill extraction surface contract.");
            }

            var moduleCount = 0;
            long triangleCount = 0;
            foreach (Transform child in prefab.transform)
            {
                if (!child.name.StartsWith("MOD_", StringComparison.Ordinal))
                {
                    continue;
                }

                moduleCount++;
                var renderers = child.GetComponentsInChildren<MeshRenderer>(true);
                var filters = child.GetComponentsInChildren<MeshFilter>(true);
                var colliders = child.GetComponentsInChildren<MeshCollider>(true);
                if (renderers.Length != 1 || filters.Length != 1 || colliders.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Module instance {child.name} has an invalid component count.");
                }

                var assigned = renderers[0].sharedMaterials;
                if (assigned.Length != 2 ||
                    assigned[0] != rockMaterial ||
                    assigned[1] != oreMaterial)
                {
                    throw new InvalidOperationException(
                        $"Module instance {child.name} has invalid material slots.");
                }

                if (colliders[0].sharedMesh != filters[0].sharedMesh || colliders[0].convex)
                {
                    throw new InvalidOperationException(
                        $"Module instance {child.name} has an invalid static collider.");
                }

                for (var subMesh = 0; subMesh < filters[0].sharedMesh.subMeshCount; subMesh++)
                {
                    triangleCount += filters[0].sharedMesh.GetIndexCount(subMesh) / 3L;
                }
            }

            if (moduleCount != layout.Placements.Length)
            {
                throw new InvalidOperationException(
                    $"Prefab {prefab.name} has {moduleCount} modules; " +
                    $"expected {layout.Placements.Length}.");
            }

            if (TryGetOreName(prefab.name, out var oreName) &&
                string.Equals(oreName, "Iron", StringComparison.Ordinal))
            {
                foreach (Transform child in prefab.transform)
                {
                    if (!child.name.StartsWith("MOD_", StringComparison.Ordinal) ||
                        child.name.IndexOf("_G0", StringComparison.Ordinal) >= 0)
                    {
                        continue;
                    }

                    var identity =
                        child.GetComponent<ManualMiningSourceIdentity>();
                    if (identity == null ||
                        identity.SourceKind != ManualMiningSourceKind.IronOreRock ||
                        string.IsNullOrWhiteSpace(identity.SourceId))
                    {
                        throw new InvalidOperationException(
                            $"Iron deposit rock {child.name} is missing its " +
                            "manual-mining identity.");
                    }
                }
            }

            if (triangleCount <= 0 || triangleCount > 30000)
            {
                throw new InvalidOperationException(
                    $"Prefab {prefab.name} triangle count is outside budget: {triangleCount}.");
            }

            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("Ground_NotExported", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException(
                        $"Prefab {prefab.name} unexpectedly contains terrain object {transform.name}.");
                }
            }
        }

        private static bool TryGetOreName(string prefabName, out string oreName)
        {
            oreName = string.Empty;
            var parts = prefabName.Split('_');
            if (parts.Length < 4 ||
                !string.Equals(parts[0], "PF", StringComparison.Ordinal) ||
                !string.Equals(parts[1], "OreDeposit", StringComparison.Ordinal))
            {
                return false;
            }

            for (var index = 0; index < OreRoles.Length; index++)
            {
                if (OreRoles[index].IsHostStone)
                {
                    continue;
                }

                if (string.Equals(parts[2], OreRoles[index].Name, StringComparison.Ordinal))
                {
                    oreName = parts[2];
                    return true;
                }
            }

            return false;
        }

        private static void CreateMarker(
            Transform parent,
            string name,
            Vector3 localPosition)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = localPosition;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;
        }

        private static void SetStaticRecursively(GameObject root)
        {
            // Deliberately not BatchingStatic. Static batching bakes vertices
            // into world space, and the island surface shader reads the local
            // height of the mesh to blend the rock into the terrain it stands
            // on: batched, that height becomes the world height and the grass
            // tint floods the whole boulder instead of its buried rim.
            var flags =
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, flags);
            }
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindRecursive(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static ModuleDefinition FindModule(string moduleId)
        {
            foreach (var module in Modules)
            {
                if (string.Equals(module.Id, moduleId, StringComparison.Ordinal))
                {
                    return module;
                }
            }

            throw new InvalidOperationException($"Unknown ore module {moduleId}.");
        }

        private static Placement P(
            string moduleId,
            float x,
            float y,
            float rotationDegrees,
            float scale)
        {
            return new Placement(moduleId, x, y, rotationDegrees, scale);
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    $"Required Unity asset is missing or failed to import: {path}");
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

        private readonly struct SurfaceRole
        {
            public SurfaceRole(
                string name,
                Color baseColor,
                Color top,
                Color under,
                bool isHostStone = false)
            {
                Name = name;
                Base = baseColor;
                Top = top;
                Under = under;
                IsHostStone = isHostStone;
            }

            public string Name { get; }
            public Color Base { get; }
            public Color Top { get; }
            public Color Under { get; }
            public bool IsHostStone { get; }
        }

        private readonly struct ModuleDefinition
        {
            public ModuleDefinition(string id, string fileName)
            {
                Id = id;
                ModelPath = ModelsRoot + "/" + fileName;
                var suffix = Path.GetFileNameWithoutExtension(fileName)
                    .Substring("ENV_Ore_".Length);
                PrefabName = "PF_OreModule_" + suffix;
                PrefabPath = ModulePrefabsRoot + "/" + PrefabName + ".prefab";
            }

            public string Id { get; }
            public string ModelPath { get; }
            public string PrefabName { get; }
            public string PrefabPath { get; }
        }

        private readonly struct LayoutDefinition
        {
            public LayoutDefinition(string name, float spatialScale, Placement[] placements)
            {
                Name = name;
                SpatialScale = spatialScale;
                Placements = placements;
            }

            public string Name { get; }
            public float SpatialScale { get; }
            public Placement[] Placements { get; }
        }

        private readonly struct Placement
        {
            public Placement(
                string moduleId,
                float x,
                float y,
                float rotationDegrees,
                float scale)
            {
                ModuleId = moduleId;
                X = x;
                Y = y;
                RotationDegrees = rotationDegrees;
                Scale = scale;
            }

            public string ModuleId { get; }
            public float X { get; }
            public float Y { get; }
            public float RotationDegrees { get; }
            public float Scale { get; }
        }
    }

    internal sealed class OreDepositAssetPostprocessor : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Art/Environment/OreDeposit";
        private const string ModelsRoot = Root + "/Models/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsRoot, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
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
            // Identical to the Starter Island rock kit: Unity averages the
            // normals at 120 degrees, which is what turns a nine-sided lofted
            // boulder into the soft mass the island is already made of.
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalCalculationMode =
                ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
            importer.normalSmoothingAngle = 120f;
            importer.importTangents = ModelImporterTangents.None;
            importer.generateSecondaryUV = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            // Must stay false, like the island rock kit. Baking the axis
            // conversion into the vertices leaves object-space Y pointing the
            // wrong way, and the surface shader measures the terrain contact
            // along that axis: the whole boulder ends up painted with grass.
            importer.bakeAxisConversion = false;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        }
    }
}
