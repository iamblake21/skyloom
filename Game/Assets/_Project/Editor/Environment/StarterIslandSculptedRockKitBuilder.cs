using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Integrates artist-authored Blender derivatives into an isolated prefab
    /// kit. Source cliffs and the gameplay scene are never modified.
    /// </summary>
    internal static class StarterIslandSculptedRockKitBuilder
    {
        private const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/VerticalRockKit_Sculpted";
        private const string ModelRoot = Root + "/Models";
        private const string MeshRoot = Root + "/Meshes";
        private const string MaterialRoot = Root + "/Materials";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string PreviewRoot = Root + "/Preview";
        private const string MaterialPath = MaterialRoot + "/M_VRKS_AutoGrass.mat";
        private const string DebugMaterialPath = MaterialRoot + "/M_VRKS_DebugGrassMask.mat";
        private const string ClayMaterialPath = MaterialRoot + "/M_VRKS_PreviewClay.mat";
        private const string GroundMaterialPath = MaterialRoot + "/M_VRKS_PreviewGround.mat";
        private const string PreviewScenePath = PreviewRoot + "/SCN_VRKS_Preview.unity";
        private const string TextureRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Textures";

        private readonly struct ModuleDef
        {
            public ModuleDef(string name, string model)
            {
                Name = name;
                ModelPath = ModelRoot + "/" + model;
            }

            public string Name { get; }
            public string ModelPath { get; }
            public string MeshPath => MeshRoot + "/MESH_VRKS_" + Name + ".asset";
            public string PrefabPath => PrefabRoot + "/PF_VRKS_" + Name + ".prefab";
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            public VertexKey(Vector3 position)
            {
                X = Mathf.RoundToInt(position.x * 1000f);
                Y = Mathf.RoundToInt(position.y * 1000f);
                Z = Mathf.RoundToInt(position.z * 1000f);
            }

            private int X { get; }
            private int Y { get; }
            private int Z { get; }

            public bool Equals(VertexKey other) =>
                X == other.X && Y == other.Y && Z == other.Z;

            public override bool Equals(object obj) =>
                obj is VertexKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((X * 397) ^ Y) * 397 ^ Z;
                }
            }
        }

        private static readonly ModuleDef[] Modules =
        {
            new ModuleDef("Straight_A", "SM_VRKS_Straight_A.obj"),
            new ModuleDef("Straight_B", "SM_VRKS_Straight_B.obj"),
            new ModuleDef("Corner_A", "SM_VRKS_Corner_A.obj"),
            new ModuleDef("Corner_B", "SM_VRKS_Corner_B.obj"),
            new ModuleDef("End_A", "SM_VRKS_End_A.obj"),
            new ModuleDef("End_B", "SM_VRKS_End_B.obj"),
            new ModuleDef("Ledge_A", "SM_VRKS_Ledge_A.obj"),
            new ModuleDef("Transition_A", "SM_VRKS_Transition_A.obj")
        };

        [MenuItem("CML/Art/Vertical Rock Kit/Rebuild Sculpted Gate")]
        public static void Rebuild()
        {
            EnsureFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var material = BuildMaterial();
            var debugMaterial = BuildDebugMaterial(material);
            var clayMaterial = BuildPreviewMaterial(
                ClayMaterialPath,
                "M_VRKS_PreviewClay",
                Html("#A8AAA6"));
            var groundMaterial = BuildPreviewMaterial(
                GroundMaterialPath,
                "M_VRKS_PreviewGround",
                Html("#4C5839"));
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var module in Modules)
            {
                var mesh = BuildProcessedMesh(module, LoadMesh(module));
                prefabs.Add(module.Name, SavePrefab(module, mesh, material));
            }

            AssetDatabase.SaveAssets();
            BuildPreview(prefabs, debugMaterial, clayMaterial, groundMaterial);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate(prefabs, material);
            Debug.Log(
                "SCULPTED_ROCK_KIT assets=8 singleMesh=PASS autoGrass=PASS " +
                "sceneChanges=0 status=PASS");
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Meshes");
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root, "Preview");
        }

        private static void EnsureFolder(string parent, string name)
        {
            var path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static Material BuildMaterial()
        {
            var shader = Shader.Find("CML/Environment/Vertical Rock Auto Grass");
            if (shader == null)
            {
                throw new InvalidOperationException("Auto-grass shader is unavailable.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_VRKS_AutoGrass" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_RockMap", LoadTexture("T_StarterIsland_CliffWarm.asset"));
            material.SetTexture("_RockNormalMap", LoadTexture("T_StarterIsland_CliffWarm_Normal.asset"));
            material.SetTexture("_GrassMap", LoadTexture("T_StarterIsland_GrassSun.asset"));
            material.SetTexture("_GrassNormalMap", LoadTexture("T_StarterIsland_GrassSun_Normal.asset"));
            material.SetFloat("_RockTileScale", 0.115f);
            material.SetFloat("_GrassTileScale", 0.15f);
            material.SetFloat("_TriplanarSharpness", 4.2f);
            material.SetFloat("_RockNormalStrength", 0.20f);
            material.SetFloat("_GrassNormalStrength", 0.10f);
            material.SetFloat("_GrassSlopeStart", 0.74f);
            material.SetFloat("_GrassSlopeEnd", 0.90f);
            material.SetFloat("_GrassNoiseScale", 0.24f);
            material.SetFloat("_GrassNoiseStrength", 0.07f);
            material.SetColor("_RockShadowColor", Html("#71463A"));
            material.SetColor("_RockBaseColor", Html("#B87557"));
            material.SetColor("_RockHighlightColor", Html("#DEA174"));
            material.SetColor("_GrassShadowColor", Html("#35452A"));
            material.SetColor("_GrassBaseColor", Html("#71843B"));
            material.SetColor("_GrassHighlightColor", Html("#9EAB54"));
            material.SetFloat("_PaletteStrength", 0.86f);
            material.SetFloat("_PlaneToneStrength", 0.47f);
            material.SetFloat("_SurfaceRoughness", 0.88f);
            material.SetFloat("_SpecularStrength", 0.042f);
            material.SetFloat("_MacroVariation", 0.035f);
            material.SetFloat("_AmbientStrength", 1.08f);
            material.SetFloat("_ShadowFloor", 0.32f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildDebugMaterial(Material source)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(DebugMaterialPath);
            if (material == null)
            {
                material = new Material(source) { name = "M_VRKS_DebugGrassMask" };
                AssetDatabase.CreateAsset(material, DebugMaterialPath);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            material.SetFloat("_DebugGrassMask", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildPreviewMaterial(
            string path,
            string name,
            Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP Lit shader is unavailable.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.02f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D LoadTexture(string fileName)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                TextureRoot + "/" + fileName);
            if (texture == null)
            {
                throw new InvalidOperationException("Missing terrain texture " + fileName);
            }

            return texture;
        }

        private static Mesh LoadMesh(ModuleDef module)
        {
            if (!File.Exists(Path.Combine(
                    Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                    module.ModelPath)))
            {
                throw new InvalidOperationException("Missing sculpted model " + module.ModelPath);
            }

            AssetDatabase.ImportAsset(module.ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var mesh = AssetDatabase.LoadAllAssetsAtPath(module.ModelPath)
                .OfType<Mesh>()
                .OrderByDescending(candidate => candidate.vertexCount)
                .FirstOrDefault();
            if (mesh == null)
            {
                throw new InvalidOperationException("No mesh found in " + module.ModelPath);
            }

            return mesh;
        }

        private static Mesh BuildProcessedMesh(ModuleDef module, Mesh source)
        {
            var generated = UnityEngine.Object.Instantiate(source);
            generated.name = "MESH_VRKS_" + module.Name;
            generated.SetUVs(1, BuildMacroNormals(generated));
            generated.RecalculateBounds();

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(module.MeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, module.MeshPath);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            existing.name = generated.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static List<Vector3> BuildMacroNormals(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var groupLookup = new Dictionary<VertexKey, int>();
            var vertexGroups = new int[vertices.Length];
            var groupCount = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var key = new VertexKey(vertices[i]);
                if (!groupLookup.TryGetValue(key, out var group))
                {
                    group = groupCount++;
                    groupLookup.Add(key, group);
                }

                vertexGroups[i] = group;
            }

            var normals = new Vector3[groupCount];
            var neighbours = new HashSet<int>[groupCount];
            for (var i = 0; i < groupCount; i++)
            {
                neighbours[i] = new HashSet<int>();
            }

            for (var i = 0; i < triangles.Length; i += 3)
            {
                var ia = triangles[i];
                var ib = triangles[i + 1];
                var ic = triangles[i + 2];
                var face = Vector3.Cross(
                    vertices[ib] - vertices[ia],
                    vertices[ic] - vertices[ia]);
                var ga = vertexGroups[ia];
                var gb = vertexGroups[ib];
                var gc = vertexGroups[ic];
                normals[ga] += face;
                normals[gb] += face;
                normals[gc] += face;
                Connect(neighbours, ga, gb);
                Connect(neighbours, gb, gc);
                Connect(neighbours, gc, ga);
            }

            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = normals[i].sqrMagnitude > 0.000001f
                    ? normals[i].normalized
                    : Vector3.up;
            }

            // Three low-frequency relaxation passes remove one-face slope
            // islands while leaving the render normals and rock facets intact.
            for (var pass = 0; pass < 4; pass++)
            {
                var relaxed = new Vector3[groupCount];
                for (var group = 0; group < groupCount; group++)
                {
                    var average = Vector3.zero;
                    foreach (var neighbour in neighbours[group])
                    {
                        average += normals[neighbour];
                    }

                    if (neighbours[group].Count > 0)
                    {
                        average /= neighbours[group].Count;
                        relaxed[group] = Vector3.Slerp(
                            normals[group],
                            average.normalized,
                            0.58f).normalized;
                    }
                    else
                    {
                        relaxed[group] = normals[group];
                    }
                }

                normals = relaxed;
            }

            var result = new List<Vector3>(vertices.Length);
            for (var i = 0; i < vertices.Length; i++)
            {
                result.Add(normals[vertexGroups[i]]);
            }

            return result;
        }

        private static void Connect(HashSet<int>[] neighbours, int a, int b)
        {
            if (a == b)
            {
                return;
            }

            neighbours[a].Add(b);
            neighbours[b].Add(a);
        }

        private static GameObject SavePrefab(
            ModuleDef module,
            Mesh mesh,
            Material material)
        {
            var root = new GameObject("PF_VRKS_" + module.Name);
            try
            {
                root.isStatic = true;
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                var collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, module.PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException("Could not save " + module.PrefabPath);
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildPreview(
            Dictionary<string, GameObject> prefabs,
            Material debugMaterial,
            Material clayMaterial,
            Material groundMaterial)
        {
            var anchor = new Vector3(6200f, 6200f, 6200f);
            var original = SceneManager.GetActiveScene();
            var preview = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(preview);
            try
            {
                ConfigureLighting(anchor);

                var catalog = new GameObject("SculptedKit_Catalog");
                catalog.transform.position = anchor;
                BuildCatalog(catalog.transform, prefabs, null);
                // Layer 25 is already proven by the assembly capture. Spatial
                // separation keeps the two preview groups out of each other's
                // camera while avoiding project-specific layer 24 filtering.
                SetLayer(catalog, 25);

                var clayCatalog = new GameObject("SculptedKit_ClayCatalog");
                clayCatalog.transform.position = anchor + new Vector3(50f, 0f, 0f);
                BuildCatalog(clayCatalog.transform, prefabs, clayMaterial);
                SetLayer(clayCatalog, 25);

                var assembly = new GameObject("SculptedKit_Assembly");
                assembly.transform.position = anchor + new Vector3(120f, 0f, 0f);
                BuildAssembly(assembly.transform, prefabs, groundMaterial);
                SetLayer(assembly, 25);

                var rotation = new GameObject("SculptedKit_RotationTest");
                rotation.transform.position = anchor + new Vector3(220f, 0f, 0f);
                BuildRotationTest(rotation.transform, prefabs, debugMaterial);
                SetLayer(rotation, 26);

                var catalogCamera = CreateCamera(
                    "CAM_VRKS_Catalog",
                    anchor + new Vector3(0f, 0f, 34f),
                    anchor,
                    true,
                    8.4f,
                    25);
                var clayCamera = CreateCamera(
                    "CAM_VRKS_ClayCatalog",
                    anchor + new Vector3(50f, 0f, 34f),
                    anchor + new Vector3(50f, 0f, 0f),
                    true,
                    8.4f,
                    25);
                var assemblyCamera = CreateCamera(
                    "CAM_VRKS_Assembly",
                    anchor + new Vector3(122f, 13.0f, 52f),
                    anchor + new Vector3(122f, 3.0f, 0.4f),
                    false,
                    0f,
                    25);
                assemblyCamera.fieldOfView = 42f;
                var rotationCamera = CreateCamera(
                    "CAM_VRKS_Rotation",
                    anchor + new Vector3(220f, 7.0f, 27f),
                    anchor + new Vector3(220f, 2.5f, 0f),
                    true,
                    8.3f,
                    26);

                Render(catalogCamera, @"D:\CodexTemp\StarterIslandTerrain\sculpted_rock_catalog.png");
                Render(clayCamera, @"D:\CodexTemp\StarterIslandTerrain\sculpted_rock_catalog_clay.png");
                Render(assemblyCamera, @"D:\CodexTemp\StarterIslandTerrain\sculpted_rock_assembly.png");
                Render(rotationCamera, @"D:\CodexTemp\StarterIslandTerrain\sculpted_rock_rotation.png");
                catalogCamera.enabled = false;
                clayCamera.enabled = false;
                assemblyCamera.enabled = false;
                rotationCamera.enabled = false;
                if (!EditorSceneManager.SaveScene(preview, PreviewScenePath, false))
                {
                    throw new InvalidOperationException("Could not save sculpted preview scene.");
                }
            }
            finally
            {
                if (original.IsValid() && original.isLoaded)
                {
                    SceneManager.SetActiveScene(original);
                }

                EditorSceneManager.CloseScene(preview, true);
            }
        }

        private static void ConfigureLighting(Vector3 anchor)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Html("#BED1D2");
            RenderSettings.ambientEquatorColor = Html("#A5A19A");
            RenderSettings.ambientGroundColor = Html("#4C514B");
            RenderSettings.ambientIntensity = 1.0f;
            var sunObject = new GameObject("PreviewSun");
            sunObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Html("#FFD8B7");
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sun.cullingMask = (1 << 24) | (1 << 25) | (1 << 26);
            RenderSettings.sun = sun;
            AddFill("CatalogFill", anchor + new Vector3(-5f, 8f, 12f));
            AddFill("ClayCatalogFill", anchor + new Vector3(50f, 8f, 12f));
            AddFill("AssemblyFill", anchor + new Vector3(120f, 8f, 12f));
            AddFill("RotationFill", anchor + new Vector3(220f, 8f, 12f));
        }

        private static void AddFill(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Html("#B9D2D4");
            light.intensity = 150f;
            light.range = 30f;
            light.shadows = LightShadows.None;
            light.cullingMask = (1 << 24) | (1 << 25) | (1 << 26);
        }

        private static void BuildCatalog(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            Material overrideMaterial)
        {
            var names = Modules.Select(module => module.Name).ToArray();
            for (var i = 0; i < names.Length; i++)
            {
                var row = i / 4;
                var column = i % 4;
                PlaceCatalogInstance(
                    root,
                    prefabs[names[i]],
                    (i + 1).ToString("00") + "_" + names[i],
                    new Vector3(-12.0f + column * 8.0f, 2.6f - row * 5.2f, 0f),
                    overrideMaterial);
            }
        }

        private static void PlaceCatalogInstance(
            Transform root,
            GameObject prefab,
            string name,
            Vector3 targetLocal,
            Material overrideMaterial)
        {
            var instance = Instantiate(prefab, root);
            instance.name = name;
            instance.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
            var bounds = RendererBounds(instance);
            var scale = 3.65f / Mathf.Max(bounds.size.x, bounds.size.y, 0.01f);
            instance.transform.localScale = Vector3.one * scale;
            if (overrideMaterial != null)
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.GetComponent<MeshRenderer>().sharedMaterial = overrideMaterial;
            }

            bounds = RendererBounds(instance);
            var target = root.position + targetLocal;
            instance.transform.position += target - bounds.center;
        }

        private static void BuildAssembly(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            Material groundMaterial)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "PreviewGround_NotPartOfKit";
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(0f, -0.22f, 0.8f);
            ground.transform.localScale = new Vector3(50f, 0.35f, 15f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

            var order = new[]
            {
                "End_A", "Corner_B", "Straight_A", "Ledge_A",
                "Transition_A", "Straight_B", "Corner_A", "End_B"
            };
            var yaw = new[] { -9f, 21f, -8f, 6f, -13f, 9f, -22f, 16f };
            var vertical = new[] { -0.10f, -0.30f, 0f, -0.42f, -0.18f, 0f, -0.28f, -0.12f };
            var depth = new[] { 0.10f, 0.82f, 0.18f, 1.05f, 0.46f, 0.12f, 0.92f, 0.28f };
            var cursor = -18f;
            for (var i = 0; i < order.Length; i++)
            {
                var instance = Instantiate(prefabs[order[i]], root);
                instance.name = "Assembly_" + (i + 1).ToString("00") + "_" + order[i];
                instance.transform.localRotation = Quaternion.Euler(0f, yaw[i], 0f);
                instance.transform.localPosition = new Vector3(cursor, vertical[i], depth[i]);
                var bounds = RendererBounds(instance);
                cursor += bounds.size.x * 0.58f;
            }
        }

        private static void BuildRotationTest(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            Material debugMaterial)
        {
            var rotations = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 90f, 38f),
                new Vector3(0f, 180f, 82f)
            };
            for (var i = 0; i < rotations.Length; i++)
            {
                var instance = Instantiate(prefabs["Straight_A"], root);
                instance.name = "WorldUp_Test_" + i;
                instance.GetComponent<MeshRenderer>().sharedMaterial = debugMaterial;
                instance.transform.localPosition = new Vector3(-8f + i * 8f, 3.2f, 0f);
                instance.transform.localRotation = Quaternion.Euler(rotations[i]);
                var bounds = RendererBounds(instance);
                instance.transform.position += new Vector3(0f, root.position.y - bounds.min.y, 0f);
            }
        }

        private static GameObject Instantiate(GameObject prefab, Transform parent)
        {
            var instance = PrefabUtility.InstantiatePrefab(
                prefab,
                parent.gameObject.scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Could not instantiate " + prefab.name);
            }

            instance.transform.SetParent(parent, false);
            return instance;
        }

        private static Camera CreateCamera(
            string name,
            Vector3 position,
            Vector3 target,
            bool orthographic,
            float size,
            int layer)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Html("#8FA5A9");
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 120f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.orthographic = orthographic;
            camera.orthographicSize = size;
            camera.cullingMask = 1 << layer;
            return camera;
        }

        private static void Render(Camera camera, string path)
        {
            const int width = 1920;
            const int height = 1080;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? @"D:\CodexTemp");
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static void Validate(
            Dictionary<string, GameObject> prefabs,
            Material material)
        {
            if (prefabs.Count != Modules.Length)
            {
                throw new InvalidOperationException("Sculpted kit prefab count mismatch.");
            }

            foreach (var module in Modules)
            {
                var prefab = prefabs[module.Name];
                if (prefab.transform.childCount != 0 ||
                    prefab.GetComponents<MeshRenderer>().Length != 1 ||
                    prefab.GetComponents<MeshFilter>().Length != 1 ||
                    prefab.GetComponents<MeshCollider>().Length != 1)
                {
                    throw new InvalidOperationException(
                        module.Name + " is not a one-object/one-mesh prefab.");
                }

                var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || mesh.vertexCount < 100 || mesh.triangles.Length < 300)
                {
                    throw new InvalidOperationException(module.Name + " has invalid geometry.");
                }

                var macroNormals = new List<Vector3>();
                mesh.GetUVs(1, macroNormals);
                if (macroNormals.Count != mesh.vertexCount)
                {
                    throw new InvalidOperationException(
                        module.Name + " is missing its world-up grass macro normals in UV2.");
                }

                if (prefab.GetComponent<MeshRenderer>().sharedMaterial != material)
                {
                    throw new InvalidOperationException(module.Name + " has the wrong material.");
                }
            }
        }

        private static void SetLayer(GameObject root, int layer)
        {
            root.layer = layer;
            for (var i = 0; i < root.transform.childCount; i++)
            {
                SetLayer(root.transform.GetChild(i).gameObject, layer);
            }
        }

        private static Bounds RendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static Color Html(string html)
        {
            if (!ColorUtility.TryParseHtmlString(html, out var color))
            {
                throw new InvalidOperationException("Invalid color " + html);
            }

            return color;
        }
    }
}
