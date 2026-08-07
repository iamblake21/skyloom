using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Factory
{
    /// <summary>
    /// Unity-side production setup for the Mechanical Era drill.
    ///
    /// Same division of ownership as the press: Blender owns geometry, UVs,
    /// hierarchy and the animation/port markers; Unity owns deterministic import
    /// settings, URP materials, compound collision and the reusable prefab.
    ///
    /// The one contract worth reading before touching this file is the output
    /// socket. Unlike the press, the drill does not meet a belt at the 0.60 m
    /// running plane: it presents the face that the Funnel clamps onto, at
    /// 0.4375 m, and the belt reaches that height through the Funnel. Every
    /// number about the socket comes from
    /// <c>Tools/Art/build_belt_kit.py::build_funnel</c> and is re-checked here
    /// because the FBX round trip and the Blender-to-Unity axis conversion are
    /// exactly where a socket silently stops fitting.
    /// </summary>
    public static class MechanicalDrillAssetSetup
    {
        private const string Root = "Assets/_Project/Art/MechanicalEra";
        private const string ModelPath = Root + "/Models/MEC_MechanicalDrill.fbx";
        private const string TextureRoot = Root + "/Textures";
        private const string MaterialRoot = Root + "/Materials";
        // Il prefab vive sotto Resources, non accanto agli altri asset d'arte.
        // La Trivella è l'unico piazzabile che non ha un'istanza già presente in
        // scena da cui ricavare un template — la Fornace ce l'ha — e assegnarla
        // a mano nell'Inspector sarebbe un passaggio manuale che qualcuno
        // prima o poi dimentica. Caricarla a runtime è la strada che il progetto
        // usa già per il viewmodel del Piccone.
        private const string PrefabRoot = "Assets/_Project/Resources/Machinery";
        private const string PrefabPath = PrefabRoot + "/PF_MechanicalDrill.prefab";

        /// <summary>Percorso per <c>Resources.Load</c>, senza cartella né estensione.</summary>
        public const string PrefabResourcePath = "Machinery/PF_MechanicalDrill";

        /// <summary>Height of the Funnel's inventory-side port, in metres.</summary>
        private const float FunnelPortHeight = 0.4375f;

        /// <summary>Depth the Funnel clamp needs behind the socket frame.</summary>
        private const float FunnelClampDepth = 0.075f;

        /// <summary>Clear width the Funnel mouth needs through the socket.</summary>
        private const float FunnelMouthWidth = 0.44f;

        /// <summary>
        /// Blender roles mapped to the Unity material name the FBX carries.
        /// Roughness matches the authored surface; metallic stays 0 for every
        /// role. The press file sets 0.34 and 0.52 on its metals, which
        /// contradicts its own Blender authoring — with no reflection probe in
        /// the scene a metallic surface renders dull, which is the reason the
        /// art direction pins it to zero.
        /// </summary>
        private static readonly SurfaceDefinition[] Surfaces =
        {
            new SurfaceDefinition("Wood", 0.62f),
            new SurfaceDefinition("Iron", 0.40f),
            new SurfaceDefinition("IronDark", 0.52f),
            new SurfaceDefinition("Bronze", 0.34f),
            new SurfaceDefinition("Shell", 0.46f),
            new SurfaceDefinition("Steel", 0.30f)
        };

        private static readonly string[] RequiredMarkers =
        {
            "ANM_DrillBit",
            "PORT_ItemOut",
            "REF_Interact",
            "REF_DepositAnchor",
            "REF_Yield"
        };

        /// <summary>Everything that spins and travels with the tool.</summary>
        private static readonly string[] RequiredBitParts =
        {
            "GEO_DrillBit_Spindle",
            "GEO_DrillBit_Collar",
            "GEO_DrillBit_Core",
            "GEO_DrillBit_Auger",
            "GEO_DrillBit_Tip",
            "GEO_DrillBit_Cutter_0",
            "GEO_DrillBit_Cutter_1",
            "GEO_DrillBit_Cutter_2"
        };

        /// <summary>
        /// Parts that must never end up under the tool pivot. If the head plate
        /// or the socket travelled with the bit, the whole machine would sink
        /// into the ground on every cycle and the Funnel would lose its seat.
        /// </summary>
        private static readonly string[] RequiredStaticParts =
        {
            "GEO_HeadPlate",
            "GEO_ThroatRing",
            "GEO_SpindleBearing",
            "GEO_SocketFrame_T",
            "GEO_SocketFrame_B",
            "GEO_SocketFrame_L",
            "GEO_SocketFrame_R",
            "GEO_SocketBack_T",
            "GEO_SocketBack_B",
            "GEO_SocketBack_L",
            "GEO_SocketBack_R",
            "GEO_OutputColumn",
            "GEO_OutputTongue"
        };

        /// <summary>
        /// The drill's drive is integrated, so it has no rotational input, no
        /// item input and no fuel port. "Flywheel" is listed because an external
        /// drive wheel was authored once and then deliberately removed: that
        /// side of the machine belongs to the socket.
        /// </summary>
        private static readonly string[] ForbiddenFragments =
        {
            "PORT_Rotational",
            "PORT_ItemIn",
            "PORT_Fuel",
            "Flywheel"
        };

        /// <summary>
        /// Compound collision. Unity Y is Blender Z and Unity Z is Blender Y.
        /// The tower is approximated with two stacked boxes rather than four
        /// leaning ones: a BoxCollider cannot lean without rotating its holder,
        /// and two boxes track the taper closely enough for a machine the player
        /// walks around rather than through.
        /// </summary>
        private static readonly ColliderDefinition[] CompoundColliders =
        {
            new ColliderDefinition(
                "COL_Base",
                new Vector3(0f, 0.10f, 0f),
                new Vector3(1.30f, 0.20f, 1.50f)),
            new ColliderDefinition(
                "COL_TowerLower",
                new Vector3(0f, 0.375f, 0f),
                new Vector3(0.86f, 0.45f, 0.86f)),
            new ColliderDefinition(
                "COL_TowerUpper",
                new Vector3(0f, 0.795f, 0f),
                new Vector3(0.56f, 0.39f, 0.56f)),
            new ColliderDefinition(
                "COL_Head",
                new Vector3(0f, 1.175f, 0f),
                new Vector3(0.62f, 0.38f, 0.62f)),
            new ColliderDefinition(
                "COL_Hopper",
                new Vector3(0f, 0.68f, 0.41f),
                new Vector3(0.62f, 0.18f, 0.52f)),
            // Si ferma sul fondo della tasca, non sulla cornice. La tasca è un
            // vuoto che l'Imbuto deve occupare: la sua morsa sporge di 7,25 cm
            // oltre la propria porta ed è progettata per entrare lì dentro.
            // Riempiendola di collisione, il piazzamento leggeva l'incastro
            // corretto come compenetrazione e lo rifiutava, lasciando passare
            // soltanto l'orientamento sbagliato — quello che non tocca niente.
            new ColliderDefinition(
                "COL_OutputSocket",
                new Vector3(0f, 0.44f, 0.5525f),
                new Vector3(0.76f, 0.62f, 0.305f))
        };

        [MenuItem("CML/Art/Rebuild Mechanical Drill")]
        public static void Run()
        {
            RequireFile(ModelPath);
            foreach (var surface in Surfaces)
            {
                RequireFile(TexturePath(surface.Role));
            }

            EnsureFolder(MaterialRoot);
            EnsureFolder(PrefabRoot);

            foreach (var surface in Surfaces)
            {
                ConfigureTexture(TexturePath(surface.Role));
            }

            ConfigureModelImporter();

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is unavailable.");
            }

            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var surface in Surfaces)
            {
                materials[MaterialName(surface.Role)] = UpsertMaterial(
                    MaterialPath(surface.Role),
                    shader,
                    RequireAsset<Texture2D>(TexturePath(surface.Role)),
                    surface.Roughness);
            }

            ConfigureMaterialRemaps(materials);
            var prefab = BuildPrefab(materials);
            ValidatePrefab(prefab, materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!Application.isBatchMode)
            {
                Selection.activeObject = prefab;
            }

            Debug.Log(
                "MECHANICAL_DRILL_UNITY_VALIDATION " +
                $"prefab={PrefabPath} materials={Surfaces.Length} " +
                $"markers={RequiredMarkers.Length} " +
                $"boxColliders={CompoundColliders.Length} status=PASS");
        }

        private static string TexturePath(string role) =>
            $"{TextureRoot}/T_MechanicalDrill_{role}_BaseColor.png";

        private static string MaterialPath(string role) =>
            $"{MaterialRoot}/M_MechanicalDrill_{role}.mat";

        private static string MaterialName(string role) => $"M_MechanicalDrill_{role}";

        private static void ConfigureModelImporter()
        {
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {ModelPath}.");
            }

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
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.SaveAndReimport();
        }

        private static void ConfigureMaterialRemaps(
            IReadOnlyDictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {ModelPath}.");
            }

            foreach (var pair in materials)
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key),
                    pair.Value);
            }

            importer.SaveAndReimport();
        }

        private static GameObject BuildPrefab(
            IReadOnlyDictionary<string, Material> materials)
        {
            var source = RequireAsset<GameObject>(ModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate {ModelPath}.");
            }

            try
            {
                instance.name = "PF_MechanicalDrill";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                SetMarkerWorldHeight(instance.transform, "PORT_ItemOut", FunnelPortHeight);

                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var assigned = renderer.sharedMaterials;
                    for (var index = 0; index < assigned.Length; index++)
                    {
                        var sourceName = assigned[index] != null
                            ? assigned[index].name
                            : string.Empty;
                        if (!materials.TryGetValue(sourceName, out var material))
                        {
                            throw new InvalidDataException(
                                $"Renderer '{HierarchyPath(renderer.transform)}' uses " +
                                $"unsupported material '{sourceName}'.");
                        }

                        assigned[index] = material;
                    }

                    renderer.sharedMaterials = assigned;
                }

                foreach (var definition in CompoundColliders)
                {
                    var holder = new GameObject(definition.Name);
                    holder.transform.SetParent(instance.transform, false);
                    var collider = holder.AddComponent<BoxCollider>();
                    collider.center = definition.Center;
                    collider.size = definition.Size;
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Could not save {PrefabPath}.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ValidatePrefab(
            GameObject prefab,
            IReadOnlyDictionary<string, Material> materials)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            foreach (var marker in RequiredMarkers)
            {
                if (FindChild(prefab.transform, marker) == null)
                {
                    throw new InvalidDataException($"Mechanical drill is missing '{marker}'.");
                }
            }

            var bit = FindChild(prefab.transform, "ANM_DrillBit");
            var bitRenderers = bit.GetComponentsInChildren<Renderer>(true);
            if (bitRenderers.Length != RequiredBitParts.Length)
            {
                throw new InvalidDataException(
                    $"ANM_DrillBit owns {bitRenderers.Length} renderers; " +
                    $"expected exactly {RequiredBitParts.Length} moving parts.");
            }

            foreach (var partName in RequiredBitParts)
            {
                var part = FindChild(prefab.transform, partName);
                if (part == null || !IsDescendantOf(part, bit))
                {
                    throw new InvalidDataException(
                        $"Movable part '{partName}' must be under ANM_DrillBit.");
                }
            }

            foreach (var partName in RequiredStaticParts)
            {
                var part = FindChild(prefab.transform, partName);
                if (part == null)
                {
                    throw new InvalidDataException(
                        $"Mechanical drill is missing static part '{partName}'.");
                }

                if (IsDescendantOf(part, bit))
                {
                    throw new InvalidDataException(
                        $"Static part '{partName}' cannot move with ANM_DrillBit.");
                }
            }

            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                foreach (var fragment in ForbiddenFragments)
                {
                    if (transform.name.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                    {
                        throw new InvalidDataException(
                            $"'{transform.name}' introduces a connection the mechanical " +
                            "drill does not have; its drive is integrated.");
                    }
                }
            }

            ValidateOutputSocket(prefab);

            var anchorLocal = prefab.transform.InverseTransformPoint(
                FindChild(prefab.transform, "REF_DepositAnchor").position);
            if (anchorLocal.magnitude > 0.002f)
            {
                throw new InvalidDataException(
                    "REF_DepositAnchor must sit on the asset origin, found " +
                    $"{anchorLocal}.");
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length < 40)
            {
                throw new InvalidDataException(
                    $"Mechanical drill has only {renderers.Length} renderers.");
            }

            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !IsApprovedMaterial(materials, material))
                    {
                        throw new InvalidDataException(
                            $"Renderer '{HierarchyPath(renderer.transform)}' is not " +
                            "using an approved Mechanical Drill material.");
                    }
                }
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            // 1.358 m di altezza: la Trivella sta sotto la linea degli occhi del
            // giocatore, che è 1.80. Se questo controllo fallisce dopo una
            // rigenerazione, la misura di riferimento è in build_mechanical_drill.py.
            AssertNear(bounds.size.x, 1.3200f, 0.035f, "width");
            AssertNear(bounds.size.y, 1.3580f, 0.035f, "height");
            AssertNear(bounds.size.z, 1.5360f, 0.035f, "depth");

            var colliders = prefab.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != CompoundColliders.Length)
            {
                throw new InvalidDataException(
                    $"Expected {CompoundColliders.Length} compound colliders, " +
                    $"found {colliders.Length}.");
            }

            foreach (var collider in colliders)
            {
                if (!(collider is BoxCollider))
                {
                    throw new InvalidDataException(
                        "Mechanical drill collision must use BoxCollider only.");
                }
            }
        }

        /// <summary>
        /// Re-proves the Funnel seat on the imported prefab.
        ///
        /// Blender already checks this on the source and on the reimported FBX,
        /// but the numbers that matter here survive an axis conversion and an
        /// importer round trip, and a socket that has quietly become a flat wall
        /// looks identical in the inspector.
        /// </summary>
        private static void ValidateOutputSocket(GameObject prefab)
        {
            var port = FindChild(prefab.transform, "PORT_ItemOut");
            var portLocal = prefab.transform.InverseTransformPoint(port.position);

            AssertNear(portLocal.y, FunnelPortHeight, 0.002f, "Funnel port height");
            if (Mathf.Abs(portLocal.x) > 0.003f)
            {
                throw new InvalidDataException(
                    $"PORT_ItemOut is {portLocal.x:F4} m off the machine centre line; " +
                    "the Funnel would meet it at an angle.");
            }

            var frameFace = float.NegativeInfinity;
            foreach (var tag in new[] { "T", "B", "L", "R" })
            {
                frameFace = Mathf.Max(
                    frameFace,
                    RequireRenderer(prefab, "GEO_SocketFrame_" + tag).bounds.max.z);
            }

            var pocketFloor = float.NegativeInfinity;
            foreach (var tag in new[] { "T", "B", "L", "R" })
            {
                pocketFloor = Mathf.Max(
                    pocketFloor,
                    RequireRenderer(prefab, "GEO_SocketBack_" + tag).bounds.max.z);
            }

            var pocketDepth = frameFace - pocketFloor;
            if (pocketDepth < FunnelClampDepth - 0.008f)
            {
                throw new InvalidDataException(
                    $"The socket pocket is only {pocketDepth:F4} m deep; the Funnel " +
                    $"clamp needs {FunnelClampDepth:F3} m to seat.");
            }

            if (Mathf.Abs(frameFace - portLocal.z) > 0.005f)
            {
                throw new InvalidDataException(
                    $"PORT_ItemOut sits at z={portLocal.z:F4} m while the socket frame " +
                    $"faces z={frameFace:F4} m; they must be the same plane.");
            }

            // Nothing may occupy the pocket: the clamp has to reach its stop.
            foreach (var partName in new[] { "GEO_OutputColumn", "GEO_SpoilHopper" })
            {
                var reach = RequireRenderer(prefab, partName).bounds.max.z;
                if (reach > pocketFloor + 0.003f)
                {
                    throw new InvalidDataException(
                        $"{partName} reaches z={reach:F4} m and intrudes into the socket " +
                        $"pocket, whose floor is at z={pocketFloor:F4} m.");
                }
            }

            var mouthWidth = Mathf.Min(
                -RequireRenderer(prefab, "GEO_SocketBack_L").bounds.max.x,
                RequireRenderer(prefab, "GEO_SocketBack_R").bounds.min.x) * 2f;
            if (mouthWidth < FunnelMouthWidth - 0.012f)
            {
                throw new InvalidDataException(
                    $"The socket mouth is {mouthWidth:F3} m wide, below the " +
                    $"{FunnelMouthWidth:F2} m the Funnel needs.");
            }

            Debug.Log(
                "MECHANICAL_DRILL_SOCKET " +
                $"portHeight={portLocal.y:F4} pocketDepth={pocketDepth:F4} " +
                $"mouthWidth={mouthWidth:F4}");
        }

        private static Renderer RequireRenderer(GameObject prefab, string name)
        {
            var transform = FindChild(prefab.transform, name);
            if (transform == null)
            {
                throw new InvalidDataException($"Mechanical drill is missing '{name}'.");
            }

            var renderer = transform.GetComponent<Renderer>();
            if (renderer == null)
            {
                throw new InvalidDataException($"'{name}' carries no Renderer.");
            }

            return renderer;
        }

        private static bool IsApprovedMaterial(
            IReadOnlyDictionary<string, Material> materials,
            Material candidate)
        {
            foreach (var pair in materials)
            {
                if (pair.Value == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetMarkerWorldHeight(
            Transform root,
            string markerName,
            float height)
        {
            var marker = FindChild(root, markerName);
            if (marker == null)
            {
                throw new InvalidDataException($"Mechanical drill is missing '{markerName}'.");
            }

            var position = marker.position;
            position.y = root.position.y + height;
            marker.position = position;
        }

        private static Material UpsertMaterial(
            string path,
            Shader shader,
            Texture2D texture,
            float roughness)
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
            else
            {
                material.shader = shader;
            }

            SetFloat(material, "_Surface", 0f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_AlphaClip", 0f);
            SetFloat(material, "_Cull", (float)CullMode.Back);
            SetFloat(material, "_ZWrite", 1f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_ReceiveShadows", 1f);
            SetTexture(material, "_BaseMap", texture);
            SetColor(material, "_BaseColor", Color.white);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 1f - roughness);
            material.SetOverrideTag("RenderType", "Opaque");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_EMISSION");
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_OCCLUSIONMAP");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureTexture(string path)
        {
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load TextureImporter for {path}.");
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool IsDescendantOf(Transform candidate, Transform ancestor)
        {
            var current = candidate.parent;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static string HierarchyPath(Transform transform)
        {
            var path = transform.name;
            var current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException($"Required asset was not found: {path}", path);
            }

            return asset;
        }

        private static void RequireFile(string assetPath)
        {
            var absolute = Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length));
            if (!File.Exists(absolute))
            {
                throw new FileNotFoundException(
                    $"Required authored file was not found: {assetPath}",
                    assetPath);
            }
        }

        private static void EnsureFolder(string folder)
        {
            var normalized = folder.Replace('\\', '/');
            var segments = normalized.Split('/');
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

        private static void AssertNear(
            float actual,
            float expected,
            float tolerance,
            string label)
        {
            if (Mathf.Abs(actual - expected) > tolerance)
            {
                throw new InvalidDataException(
                    $"Mechanical drill {label} is {actual:F4} m, " +
                    $"expected {expected:F4} ± {tolerance:F4} m.");
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private readonly struct SurfaceDefinition
        {
            public SurfaceDefinition(string role, float roughness)
            {
                Role = role;
                Roughness = roughness;
            }

            public string Role { get; }

            public float Roughness { get; }
        }

        private readonly struct ColliderDefinition
        {
            public ColliderDefinition(string name, Vector3 center, Vector3 size)
            {
                Name = name;
                Center = center;
                Size = size;
            }

            public string Name { get; }

            public Vector3 Center { get; }

            public Vector3 Size { get; }
        }
    }
}
