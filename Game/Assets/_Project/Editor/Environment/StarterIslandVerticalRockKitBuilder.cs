using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Builds a self-contained, hand-placeable vertical rock construction kit.
    /// It never opens, edits, or populates the Starter Island gameplay scene.
    /// </summary>
    internal static class StarterIslandVerticalRockKitBuilder
    {
        private const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/VerticalRockKit";
        private const string MeshesRoot = Root + "/Meshes";
        private const string MaterialsRoot = Root + "/Materials";
        private const string PrefabsRoot = Root + "/Prefabs";
        private const string PreviewRoot = Root + "/Preview";
        private const string RockMaterialPath =
            MaterialsRoot + "/M_VRK_RockWarm.mat";
        private const string GrassMaterialPath =
            MaterialsRoot + "/M_VRK_GrassCap.mat";
        private const string BackdropMaterialPath =
            MaterialsRoot + "/M_VRK_PreviewBackdrop.mat";
        private const string PreviewScenePath =
            PreviewRoot + "/SCN_VerticalRockKit_Preview.unity";

        private readonly struct PartSpec
        {
            public PartSpec(
                string name,
                Vector3 size,
                Vector3 position,
                Vector3 rotation,
                int seed,
                bool grass = false,
                float squareness = 0.58f,
                float taper = 0.22f,
                Vector2 lean = default)
            {
                Name = name;
                Size = size;
                Position = position;
                Rotation = rotation;
                Seed = seed;
                Grass = grass;
                Squareness = squareness;
                Taper = taper;
                Lean = lean;
            }

            public string Name { get; }
            public Vector3 Size { get; }
            public Vector3 Position { get; }
            public Vector3 Rotation { get; }
            public int Seed { get; }
            public bool Grass { get; }
            public float Squareness { get; }
            public float Taper { get; }
            public Vector2 Lean { get; }
        }

        private readonly struct ModuleSpec
        {
            public ModuleSpec(
                string name,
                string category,
                params PartSpec[] parts)
            {
                Name = name;
                Category = category;
                Parts = parts;
            }

            public string Name { get; }
            public string Category { get; }
            public PartSpec[] Parts { get; }
            public string PrefabPath => PrefabsRoot + "/PF_VRK_" + Name + ".prefab";
        }

        private static readonly ModuleSpec[] Modules =
        {
            new ModuleSpec(
                "Wall_Wide_A",
                "Walls",
                Rock("Body", 8.2f, 6.0f, 1.65f, 1101, 0f, 0f, 0f, 0f, 0f, 0.55f, 0.18f, 0.18f, 0.08f),
                Rock("Foot", 3.3f, 1.7f, 2.15f, 1102, -2.25f, 0f, 0.30f, 0f, -7f, 0.62f, 0.28f)),
            new ModuleSpec(
                "Wall_Wide_B",
                "Walls",
                Rock("Body", 7.4f, 5.2f, 1.85f, 1201, 0f, 0f, 0f, 0f, 2f, 0.52f, 0.20f, -0.12f, 0.11f),
                Rock("Shoulder", 2.8f, 3.3f, 2.05f, 1202, 2.45f, 0.10f, 0.24f, 0f, 9f, 0.60f, 0.24f)),
            new ModuleSpec(
                "Wall_Tall_A",
                "Walls",
                Rock("Body", 4.15f, 8.2f, 1.75f, 1301, 0f, 0f, 0f, 0f, -2f, 0.56f, 0.16f, 0.22f, -0.06f),
                Rock("Base", 3.5f, 2.1f, 2.35f, 1302, -0.20f, 0f, 0.32f, 0f, 6f, 0.62f, 0.26f)),
            new ModuleSpec(
                "Wall_Narrow",
                "Walls",
                Rock("Body", 3.0f, 5.8f, 1.55f, 1401, 0f, 0f, 0f, 0f, 1f, 0.57f, 0.18f, -0.13f, 0.05f)),
            new ModuleSpec(
                "Corner_Convex",
                "Corners",
                Rock("Left", 4.8f, 5.4f, 1.55f, 2101, -1.45f, 0f, 0.55f, 0f, 43f, 0.55f, 0.18f, 0.12f, 0.04f),
                Rock("Right", 4.8f, 5.8f, 1.55f, 2102, 1.45f, 0f, 0.55f, 0f, -43f, 0.57f, 0.18f, -0.08f, 0.04f)),
            new ModuleSpec(
                "Corner_Concave",
                "Corners",
                Rock("Left", 4.5f, 5.2f, 1.45f, 2201, -1.50f, 0f, -0.35f, 0f, -38f, 0.56f, 0.18f, 0.08f, 0.03f),
                Rock("Right", 4.5f, 5.6f, 1.45f, 2202, 1.50f, 0f, -0.35f, 0f, 38f, 0.55f, 0.18f, -0.08f, 0.03f)),
            new ModuleSpec(
                "Buttress",
                "Supports",
                Rock("Spine", 3.3f, 6.7f, 3.45f, 3101, 0f, 0f, 0.75f, -8f, 0f, 0.58f, 0.22f, 0.10f, 0.24f),
                Rock("Toe", 4.2f, 1.9f, 4.8f, 3102, 0f, 0f, 1.18f, 0f, 4f, 0.61f, 0.28f)),
            new ModuleSpec(
                "Pillar",
                "Supports",
                Rock("Column", 2.85f, 7.9f, 2.75f, 3201, 0f, 0f, 0f, 0f, -3f, 0.61f, 0.12f, 0.16f, -0.05f),
                Rock("Foot", 3.35f, 1.35f, 3.25f, 3202, 0f, 0f, 0f, 0f, 8f, 0.60f, 0.26f)),
            new ModuleSpec(
                "Ledge_Long",
                "Ledges",
                Rock("Rock", 7.6f, 1.35f, 3.8f, 4101, 0f, 0f, 1.05f, 0f, 0f, 0.57f, 0.24f),
                Grass("GrassCap", 7.2f, 0.24f, 3.45f, 4102, 0f, 1.22f, 1.05f)),
            new ModuleSpec(
                "Ledge_Short",
                "Ledges",
                Rock("Rock", 4.25f, 1.25f, 3.15f, 4201, 0f, 0f, 0.88f, 0f, -5f, 0.59f, 0.26f),
                Grass("GrassCap", 3.95f, 0.22f, 2.88f, 4202, 0f, 1.13f, 0.88f)),
            new ModuleSpec(
                "Overhang_Left",
                "Ledges",
                Rock("Wall", 4.1f, 5.4f, 1.55f, 4301, 0.75f, 0f, 0f, 0f, 2f, 0.57f, 0.17f, -0.18f, 0.04f),
                Rock("Shelf", 5.7f, 1.45f, 4.1f, 4302, -1.05f, 4.18f, 1.05f, 0f, -4f, 0.56f, 0.24f),
                Grass("GrassCap", 5.35f, 0.23f, 3.75f, 4303, -1.05f, 5.48f, 1.05f)),
            new ModuleSpec(
                "Overhang_Right",
                "Ledges",
                Rock("Wall", 4.1f, 5.4f, 1.55f, 4401, -0.75f, 0f, 0f, 0f, -2f, 0.57f, 0.17f, 0.18f, 0.04f),
                Rock("Shelf", 5.7f, 1.45f, 4.1f, 4402, 1.05f, 4.18f, 1.05f, 0f, 4f, 0.56f, 0.24f),
                Grass("GrassCap", 5.35f, 0.23f, 3.75f, 4403, 1.05f, 5.48f, 1.05f)),
            new ModuleSpec(
                "Bridge",
                "Spans",
                Rock("Span", 7.8f, 1.55f, 2.65f, 5101, 0f, 0f, 0f, 0f, 0f, 0.56f, 0.20f, 0.05f, 0.02f),
                Grass("GrassCap", 7.4f, 0.24f, 2.4f, 5102, 0f, 1.40f, 0f)),
            new ModuleSpec(
                "Arch",
                "Spans",
                Rock("LeftPier", 2.1f, 5.7f, 2.35f, 5201, -2.70f, 0f, 0f, 0f, 2f, 0.60f, 0.13f, 0.08f, 0.03f),
                Rock("RightPier", 2.1f, 5.7f, 2.35f, 5202, 2.70f, 0f, 0f, 0f, -2f, 0.60f, 0.13f, -0.08f, 0.03f),
                Rock("Crown", 7.4f, 1.85f, 2.55f, 5203, 0f, 4.75f, 0f, 0f, 0f, 0.55f, 0.20f),
                Grass("GrassCap", 7.0f, 0.24f, 2.3f, 5204, 0f, 6.45f, 0f)),
            new ModuleSpec(
                "Platform",
                "Caps",
                Rock("Rock", 6.2f, 1.35f, 5.8f, 6101, 0f, 0f, 0f, 0f, 7f, 0.58f, 0.25f),
                Grass("GrassCap", 5.85f, 0.24f, 5.45f, 6102, 0f, 1.22f, 0f)),
            new ModuleSpec(
                "Boulder_Large",
                "Accents",
                Rock("Rock", 4.4f, 3.35f, 3.8f, 7101, 0f, 0f, 0f, 0f, 13f, 0.64f, 0.30f, 0.14f, 0.04f)),
            new ModuleSpec(
                "Boulder_Medium",
                "Accents",
                Rock("Rock", 2.8f, 2.15f, 2.45f, 7201, 0f, 0f, 0f, 0f, -11f, 0.65f, 0.31f, -0.08f, 0.03f))
        };

        [MenuItem("CML/Art/Vertical Rock Kit/Rebuild Assets")]
        public static void RebuildAssets()
        {
            EnsureFolders();
            var materials = BuildMaterials();
            var prefabs = BuildPrefabs(materials.rock, materials.grass);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateAssets(prefabs);
            Debug.Log(
                $"VERTICAL_ROCK_KIT assets={prefabs.Count} " +
                "sceneChanges=0 status=PASS");
        }

        [MenuItem("CML/Art/Vertical Rock Kit/Rebuild Assets And Preview")]
        public static void RebuildAssetsAndPreview()
        {
            EnsureFolders();
            var materials = BuildMaterials();
            var prefabs = BuildPrefabs(materials.rock, materials.grass);
            AssetDatabase.SaveAssets();
            BuildAndRenderPreview(
                prefabs,
                materials.grass,
                materials.backdrop);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateAssets(prefabs);
            Debug.Log(
                $"VERTICAL_ROCK_KIT assets={prefabs.Count} " +
                $"preview={PreviewScenePath} sceneChanges=0 status=PASS");
        }

        private static PartSpec Rock(
            string name,
            float x,
            float y,
            float z,
            int seed,
            float px = 0f,
            float py = 0f,
            float pz = 0f,
            float rx = 0f,
            float ry = 0f,
            float squareness = 0.58f,
            float taper = 0.22f,
            float leanX = 0f,
            float leanZ = 0f)
        {
            return new PartSpec(
                name,
                new Vector3(x, y, z),
                new Vector3(px, py, pz),
                new Vector3(rx, ry, 0f),
                seed,
                false,
                squareness,
                taper,
                new Vector2(leanX, leanZ));
        }

        private static PartSpec Grass(
            string name,
            float x,
            float y,
            float z,
            int seed,
            float px,
            float py,
            float pz)
        {
            return new PartSpec(
                name,
                new Vector3(x, 0.13f, z),
                new Vector3(px, py, pz),
                Vector3.zero,
                seed,
                true,
                0.61f,
                0.03f,
                Vector2.zero);
        }

        private static (Material rock, Material grass, Material backdrop)
            BuildMaterials()
        {
            var rockShader = Shader.Find(
                "CML/Environment/Starter Island Cliff Rock");
            var surfaceShader = Shader.Find(
                "CML/Environment/Starter Island Stylized Surface");
            var litShader = Shader.Find(
                "Universal Render Pipeline/Lit");
            if (rockShader == null || surfaceShader == null || litShader == null)
            {
                throw new InvalidOperationException(
                    "Required Starter Island/URP shaders are unavailable.");
            }

            var rock = LoadOrCreateMaterial(
                RockMaterialPath,
                "M_VRK_RockWarm",
                rockShader);
            var cliffColor = AssetDatabase.LoadAssetAtPath<Texture2D>(
                StarterIslandTerrainSetup.TexturesRoot +
                "/T_StarterIsland_CliffWarm.asset");
            var cliffNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                StarterIslandTerrainSetup.TexturesRoot +
                "/T_StarterIsland_CliffWarm_Normal.asset");
            if (cliffColor == null || cliffNormal == null)
            {
                throw new InvalidOperationException(
                    "Starter Island cliff textures are unavailable.");
            }

            rock.SetTexture("_BaseMap", cliffColor);
            rock.SetTexture("_NormalMap", cliffNormal);
            rock.SetColor("_Tint", Color.white);
            rock.SetFloat("_TileScale", 1f / 8f);
            rock.SetFloat("_TriplanarSharpness", 4.2f);
            rock.SetFloat("_NormalStrength", 0.34f);
            rock.SetFloat("_Brightness", 1.18f);
            rock.SetFloat("_AmbientStrength", 1.02f);
            rock.SetFloat("_ShadowFloor", 0.28f);
            rock.SetFloat("_MacroVariation", 0.052f);
            rock.SetFloat("_RunoffVariation", 0.003f);
            rock.SetColor("_CliffShadowColor", Html("#74504A"));
            rock.SetColor("_CliffBaseColor", Html("#B8785F"));
            rock.SetColor("_CliffHighlightColor", Html("#D49A74"));
            rock.SetFloat("_CliffPaletteStrength", 0.82f);
            rock.SetColor("_CliffCavityColor", Html("#5A4144"));
            rock.SetFloat("_CliffCavityStrength", 0.16f);
            rock.SetFloat("_CliffReliefNormalStrength", 0.46f);
            rock.enableInstancing = true;
            EditorUtility.SetDirty(rock);

            var grass = LoadOrCreateMaterial(
                GrassMaterialPath,
                "M_VRK_GrassCap",
                surfaceShader);
            grass.SetColor("_BaseColor", Html("#A8B84A"));
            grass.SetColor("_SecondaryColor", Html("#80963B"));
            grass.SetColor("_WetColor", Html("#4A632F"));
            grass.SetFloat("_VertexBlend", 0f);
            grass.SetFloat("_AmbientStrength", 0.88f);
            grass.SetFloat("_ShadowFloor", 0.30f);
            grass.SetFloat("_ColorVariation", 0.025f);
            grass.SetFloat("_RockDetail", 0f);
            grass.SetFloat("_RockContactBlend", 0f);
            grass.enableInstancing = true;
            EditorUtility.SetDirty(grass);

            var backdrop = LoadOrCreateMaterial(
                BackdropMaterialPath,
                "M_VRK_PreviewBackdrop",
                litShader);
            backdrop.SetColor("_BaseColor", Html("#465055"));
            backdrop.SetFloat("_Metallic", 0f);
            backdrop.SetFloat("_Smoothness", 0.02f);
            EditorUtility.SetDirty(backdrop);
            return (rock, grass, backdrop);
        }

        private static Material LoadOrCreateMaterial(
            string path,
            string name,
            Shader shader)
        {
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

            return material;
        }

        private static Dictionary<string, GameObject> BuildPrefabs(
            Material rockMaterial,
            Material grassMaterial)
        {
            var result = new Dictionary<string, GameObject>(
                StringComparer.Ordinal);
            for (var moduleIndex = 0;
                 moduleIndex < Modules.Length;
                 moduleIndex++)
            {
                var spec = Modules[moduleIndex];
                var root = new GameObject("PF_VRK_" + spec.Name);
                try
                {
                    ApplyStaticFlags(root);
                    for (var partIndex = 0;
                         partIndex < spec.Parts.Length;
                         partIndex++)
                    {
                        var part = spec.Parts[partIndex];
                        var meshPath =
                            MeshesRoot + "/MESH_VRK_" + spec.Name +
                            "_" + part.Name + ".asset";
                        var mesh = BuildOrUpdateMesh(
                            meshPath,
                            "MESH_VRK_" + spec.Name + "_" + part.Name,
                            part);
                        var child = new GameObject(part.Name);
                        child.transform.SetParent(root.transform, false);
                        child.transform.localPosition = part.Position;
                        child.transform.localRotation = Quaternion.Euler(
                            part.Rotation);
                        var filter = child.AddComponent<MeshFilter>();
                        filter.sharedMesh = mesh;
                        var renderer = child.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial =
                            part.Grass ? grassMaterial : rockMaterial;
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                        renderer.motionVectorGenerationMode =
                            MotionVectorGenerationMode.Camera;
                        if (!part.Grass)
                        {
                            var collider = child.AddComponent<MeshCollider>();
                            collider.sharedMesh = mesh;
                            collider.convex = false;
                        }

                        ApplyStaticFlags(child);
                    }

                    var prefab = PrefabUtility.SaveAsPrefabAsset(
                        root,
                        spec.PrefabPath);
                    if (prefab == null)
                    {
                        throw new InvalidOperationException(
                            "Could not save vertical rock prefab " +
                            spec.PrefabPath);
                    }

                    result.Add(spec.Name, prefab);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            PruneOrphanedGeneratedMeshes();
            return result;
        }

        private static void PruneOrphanedGeneratedMeshes()
        {
            var expected = new HashSet<string>(StringComparer.Ordinal);
            for (var moduleIndex = 0;
                 moduleIndex < Modules.Length;
                 moduleIndex++)
            {
                var module = Modules[moduleIndex];
                for (var partIndex = 0;
                     partIndex < module.Parts.Length;
                     partIndex++)
                {
                    expected.Add(
                        MeshesRoot + "/MESH_VRK_" + module.Name + "_" +
                        module.Parts[partIndex].Name + ".asset");
                }
            }

            var guids = AssetDatabase.FindAssets(
                "t:Mesh MESH_VRK_",
                new[] { MeshesRoot });
            for (var index = 0; index < guids.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[index]);
                if (!expected.Contains(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        private static Mesh BuildOrUpdateMesh(
            string path,
            string name,
            PartSpec part)
        {
            var generated = BuildRockMesh(name, part);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static Mesh BuildRockMesh(string name, PartSpec part)
        {
            var horizontal =
                part.Grass ||
                part.Size.y <= Mathf.Min(part.Size.x, part.Size.z) * 0.62f;
            if (horizontal)
            {
                return BuildHorizontalSlabMesh(name, part);
            }

            var wallSlab =
                part.Size.y > 3.6f &&
                part.Size.z < part.Size.x * 0.66f;
            if (wallSlab)
            {
                return BuildVerticalSlabMesh(name, part);
            }

            return BuildPolyRockMesh(name, part);
        }

        private static Mesh BuildVerticalSlabMesh(
            string name,
            PartSpec part)
        {
            const int columns = 5;
            const int rows = 5;
            var vertices = new List<Vector3>(420);
            var uvs = new List<Vector2>(420);
            var colors = new List<Color>(420);
            var triangles = new List<int>(420);
            var front = new Vector3[columns, rows];
            var back = new Vector3[columns, rows];
            var xProfile = new[] { -0.50f, -0.27f, -0.04f, 0.23f, 0.50f };
            var yProfile = new[] { 0f, 0.22f, 0.48f, 0.74f, 1f };

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var t = yProfile[row];
                    var edge = column == 0 || column == columns - 1;
                    var topOrBottom = row == 0 || row == rows - 1;
                    var xJitter = RockNoise(
                        part.Seed + row * 37,
                        column) * part.Size.x * (edge ? 0.025f : 0.018f);
                    var yJitter = RockNoise(
                        part.Seed + column * 43,
                        row) * part.Size.y * (topOrBottom ? 0.024f : 0.016f);
                    var lean = part.Lean * (t - 0.5f);
                    var broadFold = Mathf.Sin(
                        column * 1.83f + row * 0.71f + part.Seed * 0.019f);
                    var chippedPlane = RockNoise(
                        part.Seed + 211 + row * 13,
                        column) * 0.07f;
                    var relief = (broadFold * 0.12f + chippedPlane) *
                        part.Size.z;
                    var x = xProfile[column] * part.Size.x + xJitter + lean.x;
                    var y = Mathf.Clamp(
                        t * part.Size.y + yJitter,
                        row == 0 ? -0.04f * part.Size.y : 0f,
                        row == rows - 1 ? 1.04f * part.Size.y : part.Size.y);
                    front[column, row] = new Vector3(
                        x,
                        y,
                        part.Size.z * 0.40f + relief + lean.y);
                    back[column, row] = new Vector3(
                        x * 0.97f,
                        y,
                        -part.Size.z * 0.50f);
                }
            }

            var rockColor = Color.white;
            for (var row = 0; row < rows - 1; row++)
            {
                for (var column = 0; column < columns - 1; column++)
                {
                    var a = front[column, row];
                    var b = front[column + 1, row];
                    var c = front[column + 1, row + 1];
                    var d = front[column, row + 1];
                    var ba = back[column, row];
                    var bb = back[column + 1, row];
                    var bc = back[column + 1, row + 1];
                    var bd = back[column, row + 1];
                    if ((row + column + part.Seed) % 2 == 0)
                    {
                        AddTriangle(vertices, uvs, colors, triangles, a, b, c, rockColor);
                        AddTriangle(vertices, uvs, colors, triangles, a, c, d, rockColor);
                        AddTriangle(vertices, uvs, colors, triangles, ba, bc, bb, rockColor);
                        AddTriangle(vertices, uvs, colors, triangles, ba, bd, bc, rockColor);
                    }
                    else
                    {
                        AddTriangle(vertices, uvs, colors, triangles, a, b, d, rockColor);
                        AddTriangle(vertices, uvs, colors, triangles, b, c, d, rockColor);
                        AddTriangle(vertices, uvs, colors, triangles, ba, bd, bb, rockColor);
                        AddTriangle(vertices, uvs, colors, triangles, bb, bd, bc, rockColor);
                    }
                }
            }

            for (var column = 0; column < columns - 1; column++)
            {
                AddQuad(vertices, uvs, colors, triangles,
                    back[column, 0], back[column + 1, 0],
                    front[column + 1, 0], front[column, 0], rockColor);
                AddQuad(vertices, uvs, colors, triangles,
                    front[column, rows - 1], front[column + 1, rows - 1],
                    back[column + 1, rows - 1], back[column, rows - 1], rockColor);
            }

            for (var row = 0; row < rows - 1; row++)
            {
                AddQuad(vertices, uvs, colors, triangles,
                    back[0, row], front[0, row],
                    front[0, row + 1], back[0, row + 1], rockColor);
                AddQuad(vertices, uvs, colors, triangles,
                    front[columns - 1, row], back[columns - 1, row],
                    back[columns - 1, row + 1], front[columns - 1, row + 1], rockColor);
            }

            return CreateFacetedMesh(name, vertices, uvs, colors, triangles);
        }

        private static Mesh BuildHorizontalSlabMesh(
            string name,
            PartSpec part)
        {
            var contour = BuildRockContour(part.Seed);
            var vertices = new List<Vector3>(160);
            var uvs = new List<Vector2>(160);
            var colors = new List<Color>(160);
            var triangles = new List<int>(240);
            var bottom = new Vector3[contour.Length];
            var top = new Vector3[contour.Length];
            for (var index = 0; index < contour.Length; index++)
            {
                var x = contour[index].x * part.Size.x;
                var z = contour[index].y * part.Size.z;
                var edgeVariation =
                    RockNoise(part.Seed + 31, index) * part.Size.y * 0.06f;
                bottom[index] = new Vector3(x * 0.92f, 0f, z * 0.92f);
                top[index] = new Vector3(
                    x,
                    part.Size.y + edgeVariation,
                    z);
            }

            var topCenter = new Vector3(
                part.Lean.x * 0.08f,
                part.Size.y * (part.Grass ? 1.015f : 1.04f),
                part.Lean.y * 0.08f);
            var bottomCenter = Vector3.zero;
            var rockColor = part.Grass
                ? new Color(0.72f, 0.36f, 0f, 0f)
                : Color.white;
            for (var index = 0; index < contour.Length; index++)
            {
                var next = (index + 1) % contour.Length;
                AddTriangle(
                    vertices,
                    uvs,
                    colors,
                    triangles,
                    topCenter,
                    top[next],
                    top[index],
                    rockColor);
                AddTriangle(
                    vertices,
                    uvs,
                    colors,
                    triangles,
                    bottomCenter,
                    bottom[index],
                    bottom[next],
                    rockColor);
                AddQuad(
                    vertices,
                    uvs,
                    colors,
                    triangles,
                    bottom[index],
                    top[index],
                    top[next],
                    bottom[next],
                    rockColor);
            }

            return CreateFacetedMesh(name, vertices, uvs, colors, triangles);
        }

        private static Mesh BuildPolyRockMesh(
            string name,
            PartSpec part)
        {
            const int segments = 8;
            var slender =
                part.Size.y > Mathf.Max(part.Size.x, part.Size.z) * 1.12f;
            var heights = new[] { 0f, 0.30f, 0.72f, 1f };
            var radii = slender
                ? new[] { 0.82f, 1f, 0.94f, 0.78f }
                : new[] { 0.58f, 1f, 0.91f, 0.50f };
            var rings = new Vector3[heights.Length, segments];
            for (var ring = 0; ring < heights.Length; ring++)
            {
                var t = heights[ring];
                var ringOffset = new Vector2(
                    RockNoise(part.Seed + 53, ring) * part.Size.x * 0.06f,
                    RockNoise(part.Seed + 71, ring) * part.Size.z * 0.06f);
                for (var segment = 0; segment < segments; segment++)
                {
                    var angle =
                        segment * Mathf.PI * 2f / segments +
                        RockNoise(part.Seed + ring * 19, segment) * 0.10f;
                    var radius = radii[ring] *
                        (1f + RockNoise(part.Seed + 97 + ring, segment) * 0.09f);
                    var lean = part.Lean * (t - 0.5f);
                    rings[ring, segment] = new Vector3(
                        Mathf.Cos(angle) * part.Size.x * 0.5f * radius +
                        ringOffset.x + lean.x,
                        t * part.Size.y,
                        Mathf.Sin(angle) * part.Size.z * 0.5f * radius +
                        ringOffset.y + lean.y);
                }
            }

            var vertices = new List<Vector3>(220);
            var uvs = new List<Vector2>(220);
            var colors = new List<Color>(220);
            var triangles = new List<int>(320);
            var rockColor = part.Grass
                ? new Color(0.72f, 0.36f, 0f, 0f)
                : Color.white;
            for (var ring = 0; ring < heights.Length - 1; ring++)
            {
                for (var segment = 0; segment < segments; segment++)
                {
                    var next = (segment + 1) % segments;
                    AddQuad(
                        vertices,
                        uvs,
                        colors,
                        triangles,
                        rings[ring, segment],
                        rings[ring + 1, segment],
                        rings[ring + 1, next],
                        rings[ring, next],
                        rockColor);
                }
            }

            var bottomCenter = AveragePolyRing(rings, 0, segments, 0f);
            var topCenter = AveragePolyRing(
                rings,
                heights.Length - 1,
                segments,
                part.Size.y);
            for (var segment = 0; segment < segments; segment++)
            {
                var next = (segment + 1) % segments;
                AddTriangle(
                    vertices,
                    uvs,
                    colors,
                    triangles,
                    bottomCenter,
                    rings[0, next],
                    rings[0, segment],
                    rockColor);
                AddTriangle(
                    vertices,
                    uvs,
                    colors,
                    triangles,
                    topCenter,
                    rings[heights.Length - 1, segment],
                    rings[heights.Length - 1, next],
                    rockColor);
            }

            return CreateFacetedMesh(name, vertices, uvs, colors, triangles);
        }

        private static Vector2[] BuildRockContour(int seed)
        {
            var contour = new[]
            {
                new Vector2(-0.43f, -0.47f),
                new Vector2(-0.10f, -0.50f),
                new Vector2(0.38f, -0.46f),
                new Vector2(0.49f, -0.22f),
                new Vector2(0.47f, 0.17f),
                new Vector2(0.40f, 0.46f),
                new Vector2(0.11f, 0.43f),
                new Vector2(-0.18f, 0.50f),
                new Vector2(-0.44f, 0.41f),
                new Vector2(-0.50f, 0.08f),
                new Vector2(-0.47f, -0.25f)
            };
            for (var index = 0; index < contour.Length; index++)
            {
                var jitter = RockNoise(seed, index);
                contour[index].x += jitter * 0.025f;
                contour[index].y +=
                    RockNoise(seed + 101, index) * 0.018f;
            }

            return contour;
        }

        private static float RockNoise(int seed, int index)
        {
            return Mathf.Sin(seed * 0.371f + index * 2.173f) * 0.63f +
                Mathf.Sin(seed * 0.117f - index * 1.319f) * 0.37f;
        }

        private static Vector3 AveragePolyRing(
            Vector3[,] rings,
            int ring,
            int segments,
            float height)
        {
            var sum = Vector3.zero;
            for (var segment = 0; segment < segments; segment++)
            {
                sum += rings[ring, segment];
            }

            sum /= segments;
            sum.y = height;
            return sum;
        }

        private static void AddTriangle(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Color color)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(a);
            vertices.Add(c);
            vertices.Add(b);
            uvs.Add(new Vector2(a.x, a.z));
            uvs.Add(new Vector2(b.x, b.z));
            uvs.Add(new Vector2(c.x, c.z));
            uvs.Add(new Vector2(a.x, a.z));
            uvs.Add(new Vector2(c.x, c.z));
            uvs.Add(new Vector2(b.x, b.z));
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            triangles.Add(start + 4);
            triangles.Add(start + 5);
        }

        private static void AddQuad(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Color color)
        {
            AddTriangle(vertices, uvs, colors, triangles, a, b, c, color);
            AddTriangle(vertices, uvs, colors, triangles, a, c, d, color);
        }

        private static Mesh CreateFacetedMesh(
            string name,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildLegacyRockMesh(string name, PartSpec part)
        {
            const int radialSegments = 12;
            const int verticalSegments = 6;
            var sideVertexCount =
                radialSegments * (verticalSegments + 1);
            var capVertexCount = (radialSegments + 1) * 2;
            var vertices = new Vector3[sideVertexCount + capVertexCount];
            var uvs = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[
                radialSegments * verticalSegments * 6 +
                radialSegments * 6];
            var phase = part.Seed * 0.01371f;

            for (var ring = 0; ring <= verticalSegments; ring++)
            {
                var t = ring / (float)verticalSegments;
                // Sin(PI) can be a tiny negative float. Clamp before the
                // fractional power or the final ring becomes NaN.
                var bulge = Mathf.Pow(
                    Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)),
                    0.72f);
                var effectiveTaper = part.Grass
                    ? 0f
                    : part.Size.y > 2.4f &&
                      part.Size.y > part.Size.x * 0.65f
                        ? part.Taper * 0.32f
                        : part.Taper;
                var radius =
                    1f - effectiveTaper + effectiveTaper * bulge;
                var twist =
                    Mathf.Sin(phase * 0.31f + t * 2.9f) * 0.045f;
                for (var segment = 0;
                     segment < radialSegments;
                     segment++)
                {
                    var angle =
                        segment * Mathf.PI * 2f / radialSegments + twist;
                    var cosine = Mathf.Cos(angle);
                    var sine = Mathf.Sin(angle);
                    var squareX = SignedPow(cosine, part.Squareness);
                    var squareZ = SignedPow(sine, part.Squareness);
                    var silhouette =
                        Mathf.Sin(angle * 3f + phase) * 0.060f +
                        Mathf.Sin(angle * 5f - phase * 0.73f) * 0.034f +
                        Mathf.Sin(angle * 2f + t * 4.3f + phase * 0.27f) *
                        0.026f;
                    var ringNoise =
                        Mathf.Sin(t * 5.1f + phase) * 0.025f +
                        Mathf.Sin(t * 9.3f - phase * 0.42f) * 0.012f;
                    var localRadius =
                        Mathf.Max(0.68f, radius + silhouette + ringNoise);
                    var edgeMask = Mathf.Sin(t * Mathf.PI);
                    var yNoise =
                        edgeMask * part.Size.y *
                        (Mathf.Sin(angle * 4f + phase) * 0.012f +
                         Mathf.Sin(angle * 7f - phase) * 0.005f);
                    var lean = part.Lean * (t - 0.5f);
                    var index = ring * radialSegments + segment;
                    vertices[index] = new Vector3(
                        squareX * part.Size.x * 0.5f * localRadius + lean.x,
                        t * part.Size.y + yNoise,
                        squareZ * part.Size.z * 0.5f * localRadius + lean.y);
                    uvs[index] = new Vector2(
                        segment / (float)radialSegments,
                        t);
                    colors[index] = part.Grass
                        ? new Color(0.72f, 0.36f, 0f, 0f)
                        : Color.white;
                }
            }

            var triangle = 0;
            for (var ring = 0; ring < verticalSegments; ring++)
            {
                var current = ring * radialSegments;
                var nextRing = (ring + 1) * radialSegments;
                for (var segment = 0;
                     segment < radialSegments;
                     segment++)
                {
                    var next = (segment + 1) % radialSegments;
                    var a = current + segment;
                    var b = current + next;
                    var c = nextRing + segment;
                    var d = nextRing + next;
                    if (((ring + segment + part.Seed) & 1) == 0)
                    {
                        triangles[triangle++] = a;
                        triangles[triangle++] = c;
                        triangles[triangle++] = b;
                        triangles[triangle++] = b;
                        triangles[triangle++] = c;
                        triangles[triangle++] = d;
                    }
                    else
                    {
                        triangles[triangle++] = a;
                        triangles[triangle++] = c;
                        triangles[triangle++] = d;
                        triangles[triangle++] = a;
                        triangles[triangle++] = d;
                        triangles[triangle++] = b;
                    }
                }
            }

            var bottomCenter = sideVertexCount;
            var bottomRing = bottomCenter + 1;
            var topCenter = bottomRing + radialSegments;
            var topRing = topCenter + 1;
            vertices[bottomCenter] = AverageRing(
                vertices,
                0,
                radialSegments,
                0f);
            vertices[topCenter] = AverageRing(
                vertices,
                verticalSegments * radialSegments,
                radialSegments,
                part.Size.y);
            uvs[bottomCenter] = uvs[topCenter] = new Vector2(0.5f, 0.5f);
            colors[bottomCenter] = colors[topCenter] =
                part.Grass ? new Color(0.72f, 0.36f, 0f, 0f) : Color.white;
            for (var segment = 0;
                 segment < radialSegments;
                 segment++)
            {
                var sourceBottom = segment;
                var sourceTop =
                    verticalSegments * radialSegments + segment;
                vertices[bottomRing + segment] = vertices[sourceBottom];
                vertices[topRing + segment] = vertices[sourceTop];
                uvs[bottomRing + segment] = CapUv(
                    vertices[sourceBottom],
                    part.Size);
                uvs[topRing + segment] = CapUv(
                    vertices[sourceTop],
                    part.Size);
                colors[bottomRing + segment] = colors[sourceBottom];
                colors[topRing + segment] = colors[sourceTop];
                var next = (segment + 1) % radialSegments;
                triangles[triangle++] = bottomCenter;
                triangles[triangle++] = bottomRing + next;
                triangles[triangle++] = bottomRing + segment;
                triangles[triangle++] = topCenter;
                triangles[triangle++] = topRing + segment;
                triangles[triangle++] = topRing + next;
            }

            var mesh = new Mesh
            {
                name = name,
                indexFormat = IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 AverageRing(
            Vector3[] vertices,
            int start,
            int count,
            float height)
        {
            var sum = Vector3.zero;
            for (var index = 0; index < count; index++)
            {
                sum += vertices[start + index];
            }

            sum /= count;
            sum.y = height;
            return sum;
        }

        private static Vector2 CapUv(Vector3 vertex, Vector3 size)
        {
            return new Vector2(
                0.5f + vertex.x / Mathf.Max(size.x, 0.001f),
                0.5f + vertex.z / Mathf.Max(size.z, 0.001f));
        }

        private static float SignedPow(float value, float exponent)
        {
            return Mathf.Sign(value) *
                Mathf.Pow(Mathf.Abs(value), exponent);
        }

        private static void BuildAndRenderPreview(
            Dictionary<string, GameObject> prefabs,
            Material grassMaterial,
            Material backdropMaterial)
        {
            var previewAnchor = new Vector3(2500f, 2500f, 2500f);
            var originalActiveScene = SceneManager.GetActiveScene();
            var previewScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(previewScene);
            try
            {
                ConfigurePreviewLighting(previewAnchor);
                var catalogRoot = new GameObject("Catalog_16_Modules");
                BuildCatalog(catalogRoot.transform, prefabs, backdropMaterial);
                catalogRoot.transform.position = previewAnchor;
                var assemblyRoot = new GameObject("Assembly_Example");
                BuildAssembly(
                    assemblyRoot.transform,
                    prefabs,
                    grassMaterial);
                assemblyRoot.transform.position =
                    previewAnchor + new Vector3(42f, 0f, 0f);
                SetLayerRecursively(catalogRoot, 30);
                SetLayerRecursively(assemblyRoot, 31);

                var catalogCamera = CreateCamera(
                    "CAM_KitCatalog",
                    previewAnchor + new Vector3(0f, 3.6f, 30f),
                    previewAnchor,
                    true,
                    11.4f);
                var assemblyCamera = CreateCamera(
                    "CAM_AssemblyExample",
                    previewAnchor + new Vector3(45f, 14.0f, 26f),
                    previewAnchor + new Vector3(42f, 3.1f, 0.3f),
                    false,
                    0f);
                catalogCamera.cullingMask = 1 << 30;
                assemblyCamera.cullingMask = 1 << 31;
                assemblyCamera.fieldOfView = 43f;

                RenderCamera(
                    catalogCamera,
                    @"D:\CodexTemp\StarterIslandTerrain\vertical_rock_kit_catalog.png");
                RenderCamera(
                    assemblyCamera,
                    @"D:\CodexTemp\StarterIslandTerrain\vertical_rock_kit_assembly.png");
                catalogCamera.enabled = false;
                assemblyCamera.enabled = false;
                if (!EditorSceneManager.SaveScene(
                        previewScene,
                        PreviewScenePath,
                        false))
                {
                    throw new InvalidOperationException(
                        "Could not save vertical rock kit preview scene.");
                }
            }
            finally
            {
                if (originalActiveScene.IsValid() &&
                    originalActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalActiveScene);
                }

                EditorSceneManager.CloseScene(previewScene, true);
            }
        }

        private static void ConfigurePreviewLighting(Vector3 previewAnchor)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Html("#B8CBD0");
            RenderSettings.ambientEquatorColor = Html("#9F9C94");
            RenderSettings.ambientGroundColor = Html("#4E504B");
            RenderSettings.ambientIntensity = 1.22f;

            var sunObject = new GameObject("PreviewSun");
            sunObject.transform.rotation = Quaternion.Euler(43f, -34f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Html("#FFD6AD");
            sun.intensity = 2.02f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.78f;
            sun.cullingMask = (1 << 30) | (1 << 31);
            RenderSettings.sun = sun;

            var fillObject = new GameObject("PreviewFill");
            fillObject.transform.position =
                previewAnchor + new Vector3(-8f, 8f, 10f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = Html("#A9C8D0");
            fill.intensity = 190f;
            fill.range = 34f;
            fill.shadows = LightShadows.None;
            fill.cullingMask = (1 << 30) | (1 << 31);

            var assemblyFillObject = new GameObject("PreviewAssemblyFill");
            assemblyFillObject.transform.position =
                previewAnchor + new Vector3(39f, 9f, 11f);
            var assemblyFill = assemblyFillObject.AddComponent<Light>();
            assemblyFill.type = LightType.Point;
            assemblyFill.color = Html("#B9D1D3");
            assemblyFill.intensity = 165f;
            assemblyFill.range = 32f;
            assemblyFill.shadows = LightShadows.None;
            assemblyFill.cullingMask = (1 << 30) | (1 << 31);
        }

        private static void BuildCatalog(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            Material backdropMaterial)
        {
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backdrop.name = "CatalogBackdrop";
            backdrop.transform.SetParent(root, false);
            backdrop.transform.localPosition = new Vector3(0f, 0f, -2.1f);
            backdrop.transform.localScale =
                new Vector3(41f, 23f, 0.25f);
            backdrop.GetComponent<Renderer>().sharedMaterial = backdropMaterial;
            UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());

            for (var index = 0; index < Modules.Length; index++)
            {
                var row = index / 5;
                var column = index % 5;
                var instance = PrefabUtility.InstantiatePrefab(
                    prefabs[Modules[index].Name],
                    root.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate catalog module.");
                }

                instance.name = $"{index + 1:00}_{Modules[index].Name}";
                instance.transform.SetParent(root, true);
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.Euler(0f, -17f, 0f));
                var bounds = RendererBounds(instance);
                var scale = 4.25f /
                    Mathf.Max(bounds.size.x, bounds.size.y, 0.01f);
                instance.transform.localScale = Vector3.one * scale;
                bounds = RendererBounds(instance);
                var target = new Vector3(
                    -8.0f + column * 4f,
                    7.2f - row * 4.8f,
                    0f);
                instance.transform.position +=
                    target - bounds.center;
            }
        }

        private static void BuildAssembly(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            Material grassMaterial)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "AssemblyGround";
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(0f, -0.18f, 1.1f);
            ground.transform.localScale = new Vector3(21f, 0.35f, 13f);
            ground.GetComponent<Renderer>().sharedMaterial = grassMaterial;
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

            // Overlap each module by 0.4-1.0 m. The resulting silhouette is
            // continuous from every gameplay angle and has no exposed seams.
            Place(root, prefabs, "Wall_Narrow", new Vector3(-7.65f, -0.42f, 0.30f), new Vector3(0f, 7f, 0f));
            Place(root, prefabs, "Wall_Wide_A", new Vector3(-4.35f, -0.32f, 0.02f), new Vector3(0f, -2f, 0f));
            Place(root, prefabs, "Wall_Tall_A", new Vector3(-0.55f, -0.58f, -0.48f), new Vector3(0f, 2f, 0f));
            Place(root, prefabs, "Wall_Wide_B", new Vector3(3.15f, -0.38f, -0.10f), new Vector3(0f, 4f, 0f));
            Place(root, prefabs, "Overhang_Right", new Vector3(6.15f, -0.48f, 0.12f), new Vector3(0f, -7f, 0f));
            Place(root, prefabs, "Ledge_Long", new Vector3(-4.10f, 2.45f, 0.12f), new Vector3(0f, 1f, -2f));
            Place(root, prefabs, "Ledge_Short", new Vector3(2.85f, 2.10f, 0.18f), new Vector3(0f, -4f, 1f));
            Place(root, prefabs, "Buttress", new Vector3(-6.90f, -0.48f, 1.40f), new Vector3(0f, 12f, 0f));
            Place(root, prefabs, "Boulder_Large", new Vector3(6.95f, -0.28f, 2.60f), new Vector3(0f, 18f, 0f));
            Place(root, prefabs, "Boulder_Medium", new Vector3(4.45f, -0.18f, 3.25f), new Vector3(0f, -8f, 0f));
        }

        private static void Place(
            Transform parent,
            Dictionary<string, GameObject> prefabs,
            string name,
            Vector3 position,
            Vector3 rotation)
        {
            var instance = PrefabUtility.InstantiatePrefab(
                prefabs[name],
                parent.gameObject.scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate assembly module " + name);
            }

            instance.name = "Demo_" + name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(rotation);
        }

        private static Camera CreateCamera(
            string name,
            Vector3 position,
            Vector3 target,
            bool orthographic,
            float orthographicSize)
        {
            var cameraObject = new GameObject(name);
            cameraObject.transform.position = position;
            cameraObject.transform.rotation = Quaternion.LookRotation(
                target - position,
                Vector3.up);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Html("#889DA2");
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 120f;
            camera.orthographic = orthographic;
            camera.orthographicSize = orthographicSize;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            // The preview lives more than 17 km from the gameplay scene, so
            // all prefab child layers can render while the map remains far
            // outside the camera frustum.
            camera.cullingMask = ~0;
            return camera;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (var index = 0; index < root.transform.childCount; index++)
            {
                SetLayerRecursively(
                    root.transform.GetChild(index).gameObject,
                    layer);
            }
        }

        private static void RenderCamera(Camera camera, string outputPath)
        {
            const int width = 1920;
            const int height = 1080;
            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath) ??
                @"D:\CodexTemp\StarterIslandTerrain");
            var renderTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            var capture = new Texture2D(
                width,
                height,
                TextureFormat.RGB24,
                false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                capture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                capture.Apply();
                File.WriteAllBytes(outputPath, capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(capture);
            }
        }

        private static Bounds RendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Prefab has no renderer: " + root.name);
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void ValidateAssets(
            Dictionary<string, GameObject> prefabs)
        {
            if (prefabs.Count != Modules.Length || Modules.Length < 16)
            {
                throw new InvalidOperationException(
                    "Vertical rock kit module count is invalid.");
            }

            foreach (var pair in prefabs)
            {
                var prefab = pair.Value;
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                var colliders = prefab.GetComponentsInChildren<MeshCollider>(true);
                if (filters.Length == 0 ||
                    renderers.Length != filters.Length ||
                    colliders.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Invalid rock module components: " + pair.Key);
                }

                for (var index = 0; index < filters.Length; index++)
                {
                    var mesh = filters[index].sharedMesh;
                    if (mesh == null ||
                        mesh.vertexCount < 100 ||
                        mesh.triangles.Length < 90 ||
                        !IsFinite(mesh.bounds.min) ||
                        !IsFinite(mesh.bounds.max))
                    {
                        throw new InvalidOperationException(
                            "Invalid mesh in rock module: " + pair.Key);
                    }
                }
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Color Html(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var color))
            {
                throw new InvalidOperationException(
                    "Invalid HTML color " + value);
            }

            return color;
        }

        private static void ApplyStaticFlags(GameObject target)
        {
            GameObjectUtility.SetStaticEditorFlags(
                target,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(MeshesRoot);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);
            EnsureFolder(PreviewRoot);
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
    }
}
