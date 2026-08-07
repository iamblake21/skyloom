using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity-side setup for the mechanical logistics kit
    /// (ART-002).  Blender owns geometry, UVs and the dimensional contract
    /// (Tools/Art/build_belt_kit.py); this class owns importer settings, URP
    /// materials and the six reusable prefabs.
    ///
    /// Dimensional contract shared by every module:
    ///   pitch 1.00 m · frame 0.90 m · lane 0.70 m · belt surface 0.60 m
    ///   90° curve inside a 1x1 cell · in-world item scale 0.45
    /// </summary>
    public static class BeltKitAssetSetup
    {
        private const string Root = "Assets/_Project/Art/Logistics/BeltKit";
        private const string ModelsRoot = Root + "/Models";
        private const string TextureRoot = Root + "/Textures";
        private const string MaterialsRoot = Root + "/Materials";
        private const string PrefabsRoot = Root + "/Prefabs";

        private const string CanvasMaterialPath = MaterialsRoot + "/M_BeltKit_Canvas.mat";
        private const string WoodMaterialPath = MaterialsRoot + "/M_BeltKit_Wood.mat";
        private const string IronMaterialPath = MaterialsRoot + "/M_BeltKit_Iron.mat";
        private const string CreamMaterialPath = MaterialsRoot + "/M_BeltKit_Cream.mat";
        private const string ArrowMaterialPath = MaterialsRoot + "/M_BeltKit_Arrow.mat";

        private const string CanvasTexturePath = TextureRoot + "/T_BeltKit_Canvas_BaseColor.png";
        private const string WoodTexturePath = TextureRoot + "/T_BeltKit_Wood_BaseColor.png";
        private const string IronTexturePath = TextureRoot + "/T_BeltKit_Iron_BaseColor.png";
        private const string CreamTexturePath = TextureRoot + "/T_BeltKit_Cream_BaseColor.png";

        /// <summary>Module name, prefab name and the collider that gameplay will use.</summary>
        private static readonly BeltModule[] Modules =
        {
            new BeltModule(
                "MEC_Belt_Straight",
                "PF_Belt_Straight",
                new Vector3(0.90f, 0.21f, 1.00f),
                new Vector3(0f, 0.505f, 0f),
                scrollsBand: true),
            // La Curva è stata riportata dentro una cella sola, con l'arco
            // centrato sullo spigolo: prima misurava 1.37 x 1.02 e il collider
            // ne descriveva l'ingombro vecchio, spostato di mezza cella.
            new BeltModule(
                "MEC_Belt_Curve",
                "PF_Belt_Curve",
                new Vector3(0.95f, 0.22f, 0.95f),
                new Vector3(-0.028f, 0.510f, -0.028f),
                scrollsBand: true),
            new BeltModule(
                "MEC_Belt_Support",
                "PF_Belt_Support",
                new Vector3(0.97f, 0.54f, 0.20f),
                new Vector3(0f, 0.27f, 0f),
                scrollsBand: false),
            new BeltModule(
                "MEC_Belt_Funnel",
                "PF_Belt_Funnel",
                new Vector3(0.73f, 0.80f, 0.36f),
                new Vector3(0f, 0.40f, 0.18f),
                scrollsBand: false),
            new BeltModule(
                "MEC_Belt_DriveUnit",
                "PF_Belt_DriveUnit",
                new Vector3(1.00f, 0.65f, 1.10f),
                new Vector3(0f, 0.33f, -0.05f),
                scrollsBand: true),
            new BeltModule(
                "MEC_Belt_DirectionArrow",
                "PF_Belt_DirectionArrow",
                Vector3.zero,
                Vector3.zero,
                scrollsBand: false),
            // Nastro in salita: una cella in pianta, 0.30 m di dislivello. Il
            // collider fascia il piano inclinato, dal fondo del pianale a valle
            // (0.515) alla cima del coprifilo a monte (0.962), non le gambe.
            // Curva sinistra: mesh specchiata, stesse misure della destra.
            new BeltModule(
                "MEC_Belt_CurveLeft",
                "PF_Belt_CurveLeft",
                new Vector3(0.95f, 0.22f, 0.95f),
                new Vector3(0.028f, 0.510f, -0.028f),
                scrollsBand: true),
            new BeltModule(
                "MEC_Belt_Incline",
                "PF_Belt_Incline",
                new Vector3(0.96f, 0.45f, 1.00f),
                new Vector3(0f, 0.739f, 0f),
                scrollsBand: true)
        };

        [MenuItem("CML/Art/Rebuild Belt Kit")]
        public static void Run()
        {
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);

            foreach (var texture in new[]
                     {
                         CanvasTexturePath,
                         WoodTexturePath,
                         IronTexturePath,
                         CreamTexturePath
                     })
            {
                AssetDatabase.ImportAsset(texture, ImportAssetOptions.ForceUpdate);
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The URP/Lit shader is unavailable. Verify that the Universal Render Pipeline is active.");
            }

            var materials = new Dictionary<BeltMaterialRole, Material>
            {
                // The band is canvas: rough, never metallic, and tiled so the
                // weave stays physical rather than stretched.
                [BeltMaterialRole.Canvas] = UpsertMaterial(
                    CanvasMaterialPath,
                    shader,
                    material => ConfigureOpaque(material, RequireAsset<Texture2D>(CanvasTexturePath), 0.80f, 0f)),
                [BeltMaterialRole.Wood] = UpsertMaterial(
                    WoodMaterialPath,
                    shader,
                    material => ConfigureOpaque(material, RequireAsset<Texture2D>(WoodTexturePath), 0.70f, 0f)),
                // Painted iron, not bare steel: low metallic keeps it readable
                // without an HDRI reflection environment.
                [BeltMaterialRole.Iron] = UpsertMaterial(
                    IronMaterialPath,
                    shader,
                    material => ConfigureOpaque(material, RequireAsset<Texture2D>(IronTexturePath), 0.42f, 0.30f)),
                [BeltMaterialRole.Cream] = UpsertMaterial(
                    CreamMaterialPath,
                    shader,
                    material => ConfigureOpaque(material, RequireAsset<Texture2D>(CreamTexturePath), 0.62f, 0.04f)),
                [BeltMaterialRole.Arrow] = UpsertMaterial(
                    ArrowMaterialPath,
                    shader,
                    ConfigureArrow)
            };

            var report = new List<string>();
            foreach (var module in Modules)
            {
                ConfigureModelMaterialRemaps(module, materials);
                var prefab = BuildPrefab(module, materials);
                var metrics = Validate(module, prefab, materials);
                report.Add($"{module.PrefabName}:tris={metrics.TriangleCount}:renderers={metrics.RendererCount}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"BELT_KIT_UNITY_VALIDATION modules={Modules.Length} {string.Join(" ", report)}");
        }

        /// <summary>
        /// Reimports and rebuilds only the Funnel. This is the safe companion
        /// to build_belt_kit.py --only-module funnel: approved meshes,
        /// materials and prefabs belonging to the other five modules are not
        /// rewritten.
        /// </summary>
        [MenuItem("CML/Art/Rebuild Belt Kit/Funnel Only")]
        public static void RunFunnelOnly()
        {
            EnsureFolder(PrefabsRoot);
            var materials = new Dictionary<BeltMaterialRole, Material>
            {
                [BeltMaterialRole.Canvas] = RequireAsset<Material>(CanvasMaterialPath),
                [BeltMaterialRole.Wood] = RequireAsset<Material>(WoodMaterialPath),
                [BeltMaterialRole.Iron] = RequireAsset<Material>(IronMaterialPath),
                [BeltMaterialRole.Cream] = RequireAsset<Material>(CreamMaterialPath),
                [BeltMaterialRole.Arrow] = RequireAsset<Material>(ArrowMaterialPath)
            };
            // Cercato per nome, non per indice: la tabella dei moduli cresce e
            // un indice fisso finirebbe per rigenerare in silenzio il modulo
            // sbagliato, che è l'opposto di quello che questo comando promette.
            var index = Array.FindIndex(
                Modules,
                entry => entry.ModelName == "MEC_Belt_Funnel");
            if (index < 0)
            {
                throw new InvalidOperationException(
                    "MEC_Belt_Funnel non è nella tabella dei moduli.");
            }

            var module = Modules[index];
            ConfigureModelMaterialRemaps(module, materials);
            var prefab = BuildPrefab(module, materials);
            var metrics = Validate(module, prefab, materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"BELT_KIT_FUNNEL_UNITY_VALIDATION " +
                $"tris={metrics.TriangleCount}:renderers={metrics.RendererCount}");
        }

        private static GameObject BuildPrefab(
            BeltModule module,
            IReadOnlyDictionary<BeltMaterialRole, Material> materials)
        {
            var modelPath = $"{ModelsRoot}/{module.ModelName}.fbx";
            var source = RequireAsset<GameObject>(modelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate model asset: {modelPath}");
            }

            try
            {
                instance.name = module.PrefabName;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var sourceMaterials = renderer.sharedMaterials;
                    var assigned = new Material[sourceMaterials.Length];
                    for (var index = 0; index < sourceMaterials.Length; index++)
                    {
                        assigned[index] = ResolveMaterial(sourceMaterials[index], renderer, materials);
                    }

                    renderer.sharedMaterials = assigned;
                }

                // Gameplay collider: a single box per module.  The render mesh is
                // never used as a collider, exactly as for the airship.
                //
                // La misura si ricava dai renderer, non dalla tabella: erano due
                // fonti separate per lo stesso fatto e sono divergute. Quando i
                // moduli sono stati rimpiccioliti per rientrare nella cella, i
                // valori scritti a mano sono rimasti quelli vecchi — la motrice
                // ha continuato a dichiarare 1.10 di profondità con una mesh da
                // 1.00, e sporgeva di 10 cm nella cella precedente. Il controllo
                // di sovrapposizione del costruttore legge proprio questi box,
                // quindi bloccava piazzamenti legittimi.
                if (module.ColliderSize != Vector3.zero
                    && TryMeasureRenderBounds(instance, out var measured))
                {
                    var collider = instance.AddComponent<BoxCollider>();
                    collider.size = measured.size;
                    collider.center = measured.center;
                }

                // Presentation-only animation. One component per belt that owns a
                // band; it scrolls the shared material and, when explicitly asked,
                // spins the ANM_ rollers of that instance.
                if (module.ScrollsBand)
                {
                    var animator = instance.AddComponent<CML.Unity.Presentation.Logistics.BeltVisuals>();
                    animator.Configure(materials[BeltMaterialRole.Canvas]);
                    animator.SetRollersEnabled(
                        module.PrefabName == "PF_Belt_DriveUnit");
                }

                var prefabPath = $"{PrefabsRoot}/{module.PrefabName}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Could not save prefab: {prefabPath}");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ConfigureModelMaterialRemaps(
            BeltModule module,
            IReadOnlyDictionary<BeltMaterialRole, Material> materials)
        {
            var modelPath = $"{ModelsRoot}/{module.ModelName}.fbx";
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not load ModelImporter for: {modelPath}");
            }

            foreach (var pair in new[]
                     {
                         ("M_BeltKit_Canvas", BeltMaterialRole.Canvas),
                         ("M_BeltKit_Wood", BeltMaterialRole.Wood),
                         ("M_BeltKit_Iron", BeltMaterialRole.Iron),
                         ("M_BeltKit_Cream", BeltMaterialRole.Cream),
                         ("M_BeltKit_Arrow", BeltMaterialRole.Arrow)
                     })
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Item1),
                    materials[pair.Item2]);
            }

            importer.SaveAndReimport();
        }

        private static Material ResolveMaterial(
            Material source,
            Renderer renderer,
            IReadOnlyDictionary<BeltMaterialRole, Material> materials)
        {
            var name = source != null ? source.name : string.Empty;
            if (name.IndexOf("Canvas", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[BeltMaterialRole.Canvas];
            }

            if (name.IndexOf("Wood", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[BeltMaterialRole.Wood];
            }

            if (name.IndexOf("Iron", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[BeltMaterialRole.Iron];
            }

            if (name.IndexOf("Cream", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[BeltMaterialRole.Cream];
            }

            if (name.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials[BeltMaterialRole.Arrow];
            }

            throw new InvalidOperationException(
                $"Renderer '{HierarchyPath(renderer.transform)}' uses unsupported source material " +
                $"'{(string.IsNullOrEmpty(name) ? "<missing>" : name)}'.");
        }

        private static void ConfigureOpaque(Material material, Texture2D baseMap, float roughness, float metallic)
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
            material.DisableKeyword("_EMISSION");
            material.renderQueue = (int)RenderQueue.Geometry;

            SetTexture(material, "_BaseMap", baseMap);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            SetColor(material, "_BaseColor", Color.white);
            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", 1f - roughness);
            material.enableInstancing = true;
        }

        private static void ConfigureArrow(Material material)
        {
            ConfigureOpaque(material, null, 0.40f, 0f);
            var gold = new Color(0.86f, 0.68f, 0.20f, 1f);
            SetColor(material, "_BaseColor", gold);
            SetTexture(material, "_EmissionMap", null);
            SetColor(material, "_EmissionColor", gold * 2.2f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        private static Material UpsertMaterial(string path, Shader shader, Action<Material> configure)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
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

        private static BeltMetrics Validate(
            BeltModule module,
            GameObject prefab,
            IReadOnlyDictionary<BeltMaterialRole, Material> materials)
        {
            var expected = new HashSet<Material>(materials.Values);
            var rendererCount = 0;
            long triangles = 0;
            var meshes = new HashSet<Mesh>();
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                rendererCount++;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !expected.Contains(material))
                    {
                        throw new InvalidOperationException(
                            $"Renderer '{HierarchyPath(renderer.transform)}' has an invalid material assignment.");
                    }
                }
            }

            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && meshes.Add(filter.sharedMesh))
                {
                    for (var subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                    {
                        triangles += (long)filter.sharedMesh.GetIndexCount(subMesh) / 3L;
                    }
                }
            }

            if (rendererCount == 0 || triangles <= 0)
            {
                throw new InvalidOperationException($"Prefab '{module.PrefabName}' has no renderable geometry.");
            }

            // Modules that carry a band must expose their rollers, otherwise the
            // presentation layer has nothing to spin.
            if (module.ScrollsBand && FindRecursive(prefab.transform, "ANM_") == null)
            {
                throw new InvalidOperationException(
                    $"Prefab '{module.PrefabName}' is missing the ANM_ rotating parts.");
            }

            if (module.PrefabName == "PF_Belt_Funnel")
            {
                ValidateFunnelMarker(
                    prefab.transform,
                    "PORT_Belt",
                    new Vector3(0f, 0.600f, 0f),
                    Vector3.back);
                ValidateFunnelMarker(
                    prefab.transform,
                    "PORT_Inventory",
                    new Vector3(0f, 0.4375f, 0.333f),
                    Vector3.forward);
            }

            return new BeltMetrics(rendererCount, triangles);
        }

        private static void ValidateFunnelMarker(
            Transform root,
            string markerName,
            Vector3 expectedLocalPosition,
            Vector3 expectedForward)
        {
            var marker = FindExactRecursive(root, markerName);
            if (marker == null)
            {
                throw new InvalidOperationException(
                    $"Prefab 'PF_Belt_Funnel' is missing required marker '{markerName}'.");
            }

            // FBX may retain an axis-conversion parent around exported
            // empties. Compare in module-root space, not against the marker's
            // immediate parent coordinates.
            var modulePosition = root.InverseTransformPoint(marker.position);
            if (Vector3.Distance(modulePosition, expectedLocalPosition) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"Marker '{markerName}' is at {modulePosition}, expected " +
                    $"{expectedLocalPosition}.");
            }

            var moduleForward = root.InverseTransformDirection(marker.forward).normalized;
            if (Vector3.Dot(moduleForward, expectedForward) < 0.999f)
            {
                throw new InvalidOperationException(
                    $"Marker '{markerName}' faces {moduleForward}, expected " +
                    $"{expectedForward}.");
            }
        }

        private static Transform FindRecursive(Transform root, string prefix)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return transform;
                }
            }

            return null;
        }

        private static Transform FindExactRecursive(Transform root, string name)
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

        private static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                names.Push(current.name);
            }

            return string.Join("/", names);
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

        private enum BeltMaterialRole
        {
            Canvas,
            Wood,
            Iron,
            Cream,
            Arrow
        }

        /// <summary>
        /// Ingombro reso, in spazio locale della radice del modulo.
        ///
        /// È la sola fonte per il collider di gioco: la geometria la possiede
        /// Blender, e qualunque valore riscritto a mano da questa parte è
        /// destinato a restare indietro alla prima modifica.
        /// </summary>
        private static bool TryMeasureRenderBounds(GameObject instance, out Bounds local)
        {
            local = default;
            // Si misura dalla mesh, non da Renderer.bounds: su un'istanza di
            // prefab che non sta in una scena quelle bounds non sono attendibili
            // e restituiscono i valori del prefab precedente, che è esattamente
            // il modo in cui il collider della motrice era rimasto indietro.
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0)
            {
                return false;
            }

            var root = instance.transform;
            var found = false;
            var min = Vector3.zero;
            var max = Vector3.zero;
            for (var index = 0; index < filters.Length; index++)
            {
                var mesh = filters[index].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                var bounds = mesh.bounds;
                var meshTransform = filters[index].transform;
                for (var corner = 0; corner < 8; corner++)
                {
                    var localCorner = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var world = meshTransform.TransformPoint(localCorner);
                    var point = root.InverseTransformPoint(world);
                    if (!found)
                    {
                        min = point;
                        max = point;
                        found = true;
                        continue;
                    }

                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            }

            if (!found)
            {
                return false;
            }

            local = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        private readonly struct BeltModule
        {
            public BeltModule(
                string modelName,
                string prefabName,
                Vector3 colliderSize,
                Vector3 colliderCentre,
                bool scrollsBand)
            {
                ModelName = modelName;
                PrefabName = prefabName;
                ColliderSize = colliderSize;
                ColliderCentre = colliderCentre;
                ScrollsBand = scrollsBand;
            }

            public string ModelName { get; }

            public string PrefabName { get; }

            public Vector3 ColliderSize { get; }

            public Vector3 ColliderCentre { get; }

            public bool ScrollsBand { get; }
        }

        private readonly struct BeltMetrics
        {
            public BeltMetrics(int rendererCount, long triangleCount)
            {
                RendererCount = rendererCount;
                TriangleCount = triangleCount;
            }

            public int RendererCount { get; }

            public long TriangleCount { get; }
        }
    }

    /// <summary>
    /// Keeps regenerated Blender exports deterministic on every import.
    /// </summary>
    internal sealed class BeltKitAssetPostprocessor : AssetPostprocessor
    {
        private const string ModelsRoot = "Assets/_Project/Art/Logistics/BeltKit/Models/";
        private const string TextureRoot = "Assets/_Project/Art/Logistics/BeltKit/Textures/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsRoot, StringComparison.Ordinal))
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
            // The rotation of rollers, drum, pulley and flap is produced
            // procedurally by the presentation layer, so the baked clips are
            // deliberately left out of the import.
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
            importer.bakeAxisConversion = true;
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

            // Canvas, wood and iron are tiling maps: they need repeat, bilinear
            // filtering and mipmaps, unlike the airship palette atlases.
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 256;
        }
    }
}
