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
    /// Builds the sparse rock kit used to break selected Terrain cliff faces.
    ///
    /// The Terrain remains both the morphology and the only collision
    /// authority. These modules are broad, shallow patches whose perimeter is
    /// deliberately buried in the heightfield; they are not boulders scaled up
    /// and they never form a second continuous wall.
    /// </summary>
    internal static class StarterIslandCliffRockKitBuilder
    {
        internal const string SceneRootName = "CliffRockKitRoot";

        private const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/CliffRockKit";
        private const string MeshesRoot = Root + "/Meshes";
        private const string MaterialsRoot = Root + "/Materials";
        private const string PrefabsRoot = Root + "/Prefabs";
        private const string MaterialPath =
            MaterialsRoot + "/M_StarterIsland_CliffRockKit.mat";

        private enum Shape
        {
            WideBulge,
            TallPillar,
            BaseMass,
            UpperLip,
            Shoulder
        }

        private readonly struct ModuleSpec
        {
            public ModuleSpec(
                string name,
                Shape shape,
                float width,
                float height,
                float depth,
                int columns,
                int rows,
                int seed)
            {
                Name = name;
                Shape = shape;
                Width = width;
                Height = height;
                Depth = depth;
                Columns = columns;
                Rows = rows;
                Seed = seed;
            }

            public string Name { get; }
            public Shape Shape { get; }
            public float Width { get; }
            public float Height { get; }
            public float Depth { get; }
            public int Columns { get; }
            public int Rows { get; }
            public int Seed { get; }

            public string MeshPath =>
                MeshesRoot + "/MESH_" + Name + ".asset";

            public string PrefabPath =>
                PrefabsRoot + "/PF_" + Name + ".prefab";
        }

        private readonly struct Placement
        {
            public Placement(
                string module,
                string terrace,
                float angleDegrees,
                float heightOffset,
                float outwardOffset,
                float pitch,
                float roll,
                Vector3 scale)
            {
                Module = module;
                Terrace = terrace;
                AngleDegrees = angleDegrees;
                HeightOffset = heightOffset;
                OutwardOffset = outwardOffset;
                Pitch = pitch;
                Roll = roll;
                Scale = scale;
            }

            public string Module { get; }
            public string Terrace { get; }
            public float AngleDegrees { get; }
            public float HeightOffset { get; }
            public float OutwardOffset { get; }
            public float Pitch { get; }
            public float Roll { get; }
            public Vector3 Scale { get; }
        }

        private static readonly ModuleSpec[] Modules =
        {
            new ModuleSpec(
                "CliffBulgeWide",
                Shape.WideBulge,
                18.0f,
                9.4f,
                4.15f,
                24,
                8,
                0x21A7),
            new ModuleSpec(
                "CliffPillarTall",
                Shape.TallPillar,
                9.0f,
                12.4f,
                3.65f,
                20,
                9,
                0x32B9),
            new ModuleSpec(
                "CliffBaseMass",
                Shape.BaseMass,
                15.5f,
                6.2f,
                4.35f,
                24,
                8,
                0x43CB),
            new ModuleSpec(
                "CliffLipWide",
                Shape.UpperLip,
                19.0f,
                4.4f,
                3.75f,
                24,
                7,
                0x54DD),
            new ModuleSpec(
                "CliffShoulder",
                Shape.Shoulder,
                13.0f,
                8.8f,
                3.95f,
                22,
                8,
                0x65EF)
        };

        // The south-facing cascade walls are the hero read, so they receive
        // overlapping masses with buried perimeters: the gaps between modules
        // become real shadow seams instead of painted stripes. Other sectors
        // keep isolated accents and the Terrain remains the continuous wall
        // and sole collision authority.
        private static readonly Placement[] Placements =
        {
            new Placement(
                "CliffShoulder",
                "NorthWestRing1",
                -108.0f,
                -0.15f,
                0.38f,
                -14.0f,
                1.5f,
                new Vector3(1.00f, 1.00f, 0.96f)),
            new Placement(
                "CliffBulgeWide",
                "NorthWestRing1",
                -94.0f,
                0.05f,
                0.34f,
                -15.0f,
                -1.5f,
                new Vector3(1.04f, 1.00f, 1.00f)),
            new Placement(
                "CliffBaseMass",
                "NorthWestRing1",
                -83.0f,
                -3.55f,
                0.58f,
                -9.0f,
                2.0f,
                new Vector3(1.00f, 1.00f, 0.98f)),
            new Placement(
                "CliffBulgeWide",
                "NorthWestRing1",
                -72.5f,
                -0.25f,
                0.30f,
                -15.5f,
                -1.0f,
                new Vector3(1.04f, 1.02f, 1.00f)),
            new Placement(
                "CliffLipWide",
                "NorthWestRing1",
                -72.5f,
                6.15f,
                0.18f,
                -10.0f,
                2.0f,
                new Vector3(1.02f, 0.94f, 1.05f)),
            new Placement(
                "CliffPillarTall",
                "NorthWestRing1",
                -62.0f,
                0.15f,
                0.24f,
                -15.5f,
                1.5f,
                new Vector3(0.94f, 1.02f, 0.94f)),
            new Placement(
                "CliffBaseMass",
                "NorthWestRing1",
                -52.5f,
                -4.65f,
                0.55f,
                -9.0f,
                -2.0f,
                new Vector3(1.00f, 1.00f, 0.96f)),

            new Placement(
                "CliffBulgeWide",
                "NorthWestRing2",
                -108.0f,
                -0.15f,
                0.34f,
                -15.0f,
                -1.0f,
                new Vector3(0.96f, 1.02f, 0.96f)),
            new Placement(
                "CliffPillarTall",
                "NorthWestRing2",
                -88.0f,
                0.10f,
                0.30f,
                -15.5f,
                1.5f,
                new Vector3(0.92f, 1.00f, 0.94f)),
            new Placement(
                "CliffShoulder",
                "NorthWestRing2",
                -70.0f,
                -0.05f,
                0.32f,
                -14.5f,
                -1.5f,
                new Vector3(1.02f, 1.00f, 0.96f)),
            new Placement(
                "CliffShoulder",
                "NorthWestRing2",
                -148.0f,
                -0.1f,
                0.28f,
                -16.0f,
                2.0f,
                new Vector3(1.05f, 1.00f, 0.96f)),
            new Placement(
                "CliffPillarTall",
                "NorthWestRing3",
                8.0f,
                0.0f,
                0.22f,
                -16.0f,
                -1.5f,
                new Vector3(0.90f, 0.88f, 0.92f)),
            new Placement(
                "CliffBaseMass",
                "NorthWestRing3",
                -92.0f,
                -2.85f,
                0.46f,
                -9.5f,
                1.0f,
                new Vector3(0.90f, 0.92f, 0.92f)),
            new Placement(
                "CliffBulgeWide",
                "NorthEastRing1",
                16.0f,
                -0.2f,
                0.32f,
                -15.5f,
                1.0f,
                new Vector3(0.96f, 1.00f, 0.96f)),
            new Placement(
                "CliffBaseMass",
                "NorthEastRing2",
                132.0f,
                -3.7f,
                0.48f,
                -10.0f,
                -2.0f,
                new Vector3(0.96f, 0.96f, 0.94f)),
            new Placement(
                "CliffShoulder",
                "NorthEastRing3",
                -12.0f,
                0.0f,
                0.25f,
                -16.0f,
                1.5f,
                new Vector3(0.96f, 0.94f, 0.94f)),
            new Placement(
                "CliffLipWide",
                "PortalCrown",
                42.0f,
                3.2f,
                0.18f,
                -10.0f,
                -1.0f,
                new Vector3(0.92f, 0.90f, 0.94f))
        };

        internal static void EnsureAndPopulate(Terrain terrain)
        {
            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            var prefabs = EnsureAssets();

            var oldRoot = GameObject.Find(SceneRootName);
            if (oldRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRoot);
            }

            var sceneRoot = new GameObject(SceneRootName);
            var placed = 0;
            for (var index = 0; index < Placements.Length; index++)
            {
                var placement = Placements[index];
                if (!prefabs.TryGetValue(
                        placement.Module,
                        out var prefab))
                {
                    throw new InvalidOperationException(
                        "Missing cliff module prefab: " +
                        placement.Module);
                }

                var terrace = FindTerrace(placement.Terrace);
                var wallPoint = FindWallMidpoint(
                    terrain,
                    terrace,
                    placement.AngleDegrees);
                var outward = new Vector3(
                    wallPoint.x - terrace.CenterX,
                    0f,
                    wallPoint.z - terrace.CenterZ).normalized;
                wallPoint += outward * placement.OutwardOffset;
                wallPoint.y += placement.HeightOffset;

                var instance = PrefabUtility.InstantiatePrefab(
                    prefab,
                    terrain.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate cliff module " +
                        placement.Module);
                }

                instance.name =
                    $"DEC_CliffRock_{placed:00}_{placement.Module}";
                instance.transform.SetParent(sceneRoot.transform, true);
                instance.transform.SetPositionAndRotation(
                    wallPoint,
                    Quaternion.LookRotation(outward, Vector3.up) *
                    Quaternion.Euler(
                        placement.Pitch,
                        0f,
                        placement.Roll));
                instance.transform.localScale = placement.Scale;
                RemovePhysics(instance);
                ApplyStaticFlags(instance);
                placed++;
            }

            ValidateSceneKit(sceneRoot, placed);
            Debug.Log(
                $"STARTER_ISLAND_CLIFF_ROCK_KIT modules={Modules.Length} " +
                $"placements={placed} colliders=0 rigidbodies=0 " +
                "terrainCoverage=sparse status=PASS");
        }

        [MenuItem("CML/Art/Rebuild Starter Island Cliff Rock Kit Assets")]
        public static void RebuildAssets()
        {
            EnsureAssets();
            Debug.Log(
                $"STARTER_ISLAND_CLIFF_ROCK_ASSETS modules={Modules.Length} " +
                "colliders=0 status=PASS");
        }

        [MenuItem("CML/Art/Render Starter Island Cliff Rock Kit Preview")]
        public static void RenderPreview()
        {
            var prefabs = EnsureAssets();
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Html("#CBD6D4");
            RenderSettings.ambientEquatorColor = Html("#B7B1A6");
            RenderSettings.ambientGroundColor = Html("#69645F");
            RenderSettings.ambientIntensity = 0.72f;

            var sunObject = new GameObject("PreviewSun");
            sunObject.transform.rotation =
                Quaternion.Euler(38f, -32f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Html("#FFD5AC");
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            RenderSettings.sun = sun;

            var panelShader =
                Shader.Find("Universal Render Pipeline/Lit");
            if (panelShader == null)
            {
                throw new InvalidOperationException(
                    "URP Lit shader is unavailable for kit preview.");
            }

            var panelMaterial = new Material(panelShader);
            panelMaterial.SetColor("_BaseColor", Html("#8C8177"));
            panelMaterial.SetFloat("_Smoothness", 0.02f);
            panelMaterial.SetFloat("_Metallic", 0f);

            var layout = new[]
            {
                new Vector2(-6.0f, 3.15f),
                new Vector2(0f, 3.15f),
                new Vector2(6.0f, 3.15f),
                new Vector2(-3.15f, -3.05f),
                new Vector2(3.15f, -3.05f)
            };
            var order = new[]
            {
                "CliffBulgeWide",
                "CliffPillarTall",
                "CliffShoulder",
                "CliffBaseMass",
                "CliffLipWide"
            };

            for (var index = 0; index < order.Length; index++)
            {
                var spec = FindModule(order[index]);
                var scale = Mathf.Min(
                    4.45f / spec.Width,
                    4.35f / spec.Height);
                var center = new Vector3(
                    layout[index].x,
                    layout[index].y,
                    0f);

                var panel =
                    GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "PreviewPanel_" + order[index];
                panel.transform.position = center;
                panel.transform.localScale =
                    new Vector3(5.25f, 5.15f, 0.42f);
                panel.GetComponent<Renderer>().sharedMaterial =
                    panelMaterial;
                UnityEngine.Object.DestroyImmediate(
                    panel.GetComponent<Collider>());

                var instance = PrefabUtility.InstantiatePrefab(
                    prefabs[order[index]],
                    SceneManager.GetActiveScene()) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate preview module " +
                        order[index]);
                }

                instance.name = "Preview_" + order[index];
                instance.transform.position =
                    center + Vector3.forward * 0.74f;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale =
                    Vector3.one * scale;
            }

            var cameraObject = new GameObject("PreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Html("#D8DEDC");
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.transform.position =
                new Vector3(0f, 0.3f, 24f);
            cameraObject.transform.rotation =
                Quaternion.LookRotation(
                    new Vector3(0f, 0.1f, 0f) -
                    cameraObject.transform.position,
                    Vector3.up);

            const int width = 1600;
            const int height = 900;
            var outputPath =
                @"D:\CodexTemp\StarterIslandTerrain\" +
                "cliff_rock_kit_preview.png";
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
                capture.ReadPixels(
                    new Rect(0f, 0f, width, height),
                    0,
                    0);
                capture.Apply();
                File.WriteAllBytes(
                    outputPath,
                    capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(capture);
                UnityEngine.Object.DestroyImmediate(panelMaterial);
            }

            Debug.Log(
                "STARTER_ISLAND_CLIFF_ROCK_PREVIEW " +
                $"path={outputPath} modules={Modules.Length} status=PASS");
        }

        private static Dictionary<string, GameObject> EnsureAssets()
        {
            EnsureFolder(Root);
            EnsureFolder(MeshesRoot);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);

            var material = BuildMaterial();
            var prefabs = new Dictionary<string, GameObject>(
                StringComparer.Ordinal);
            for (var index = 0; index < Modules.Length; index++)
            {
                var spec = Modules[index];
                var mesh = BuildOrUpdateMesh(spec);
                prefabs.Add(
                    spec.Name,
                    BuildOrUpdatePrefab(spec, mesh, material));
            }

            AssetDatabase.SaveAssets();
            return prefabs;
        }

        private static ModuleSpec FindModule(string name)
        {
            for (var index = 0; index < Modules.Length; index++)
            {
                if (string.Equals(
                        Modules[index].Name,
                        name,
                        StringComparison.Ordinal))
                {
                    return Modules[index];
                }
            }

            throw new InvalidOperationException(
                "Unknown cliff module: " + name);
        }

        private static Color Html(string value)
        {
            if (!ColorUtility.TryParseHtmlString(
                    value,
                    out var color))
            {
                throw new InvalidOperationException(
                    "Invalid HTML color: " + value);
            }

            return color;
        }

        private static Material BuildMaterial()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Cliff Rock");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Starter Island cliff rock shader is unavailable.");
            }

            var material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_CliffRockKit"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            var baseTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    StarterIslandTerrainSetup.TexturesRoot +
                    "/T_StarterIsland_CliffWarm.asset");
            var normalTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    StarterIslandTerrainSetup.TexturesRoot +
                    "/T_StarterIsland_CliffWarm_Normal.asset");
            if (baseTexture == null || normalTexture == null)
            {
                throw new InvalidOperationException(
                    "Cliff Terrain textures must exist before the rock kit.");
            }

            material.SetTexture("_BaseMap", baseTexture);
            material.SetTexture("_NormalMap", normalTexture);
            material.SetColor("_Tint", Color.white);
            material.SetFloat("_TileScale", 1f / 12f);
            material.SetFloat("_TriplanarSharpness", 5.2f);
            material.SetFloat("_NormalStrength", 0.30f);
            material.SetFloat("_Brightness", 1.04f);
            material.SetFloat("_AmbientStrength", 0.50f);
            material.SetFloat("_ShadowFloor", 0.08f);
            material.SetFloat("_MacroVariation", 0.07f);
            material.SetFloat("_RunoffVariation", 0.015f);
            material.SetColor(
                "_CliffShadowColor",
                new Color32(0x74, 0x3E, 0x36, 0xFF));
            material.SetColor(
                "_CliffBaseColor",
                new Color32(0xB9, 0x60, 0x43, 0xFF));
            material.SetColor(
                "_CliffHighlightColor",
                new Color32(0xDA, 0x8A, 0x5F, 0xFF));
            material.SetFloat("_CliffPaletteStrength", 0.82f);
            material.SetColor(
                "_CliffCavityColor",
                new Color32(0x68, 0x3A, 0x35, 0xFF));
            material.SetFloat("_CliffCavityStrength", 0.18f);
            material.SetFloat("_CliffReliefNormalStrength", 0.9f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh BuildOrUpdateMesh(ModuleSpec spec)
        {
            var generated = BuildMesh(spec);
            var existing =
                AssetDatabase.LoadAssetAtPath<Mesh>(spec.MeshPath);
            if (existing == null)
            {
                generated.name = "MESH_" + spec.Name;
                AssetDatabase.CreateAsset(generated, spec.MeshPath);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            existing.name = "MESH_" + spec.Name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static Mesh BuildMesh(ModuleSpec spec)
        {
            var segments = spec.Columns;
            var rings = spec.Rows;
            var vertexCount = 1 + segments * rings;
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var triangles = new int[
                segments * 3 +
                segments * (rings - 1) * 6];

            var phase = spec.Seed * 0.00091f;
            ShapeCentre(
                spec.Shape,
                out var centreX,
                out var centreY,
                out var centreDepthScale);
            vertices[0] = new Vector3(
                centreX * spec.Width,
                centreY * spec.Height,
                -1.18f +
                spec.Depth * centreDepthScale);
            uvs[0] = new Vector2(
                0.5f + centreX,
                0.5f + centreY);

            for (var ring = 1; ring <= rings; ring++)
            {
                var radial = ring / (float)rings;
                var envelope =
                    Mathf.Pow(
                        Mathf.Max(
                            0f,
                            1f - Mathf.Pow(radial, 1.55f)),
                        0.68f);
                for (var segment = 0;
                     segment < segments;
                     segment++)
                {
                    var angle =
                        segment * Mathf.PI * 2f / segments;
                    var directionX = Mathf.Cos(angle);
                    var directionY = Mathf.Sin(angle);
                    PolarShapeProfile(
                        spec.Shape,
                        directionX,
                        directionY,
                        radial,
                        out var horizontalScale,
                        out var verticalScale,
                        out var depthScale,
                        out var horizontalShift,
                        out var verticalShift);
                    var boundaryJitter =
                        1f +
                        Mathf.Sin(
                            angle * 3f +
                            phase) *
                        0.055f +
                        Mathf.Sin(
                            angle * 7f -
                            phase * 1.7f) *
                        0.025f;
                    var shapedRadius =
                        radial * boundaryJitter;
                    var centreBlend =
                        1f - radial;
                    var x =
                        directionX *
                        shapedRadius *
                        spec.Width *
                        0.5f *
                        horizontalScale +
                        horizontalShift *
                        spec.Width +
                        centreX *
                        spec.Width *
                        centreBlend;
                    var y =
                        directionY *
                        shapedRadius *
                        spec.Height *
                        0.5f *
                        verticalScale +
                        verticalShift *
                        spec.Height +
                        centreY *
                        spec.Height *
                        centreBlend;
                    var broad =
                        Mathf.Sin(
                            angle * 2.0f +
                            phase) *
                        Mathf.Sin(
                            radial * Mathf.PI * 1.7f -
                            phase * 0.8f);
                    var medium =
                        Mathf.Sin(
                            angle * 5.0f -
                            phase * 1.1f) *
                        Mathf.Sin(
                            radial * Mathf.PI * 3.2f +
                            phase * 0.6f);
                    var depthNoise =
                        1f +
                        broad * 0.085f +
                        medium * 0.035f;
                    var z =
                        -1.18f +
                        spec.Depth *
                        envelope *
                        depthScale *
                        depthNoise;

                    var index = 1 +
                        (ring - 1) * segments +
                        segment;
                    vertices[index] = new Vector3(x, y, z);
                    uvs[index] = new Vector2(
                        0.5f + x / spec.Width,
                        0.5f + y / spec.Height);
                }
            }

            var triangle = 0;
            for (var segment = 0;
                 segment < segments;
                 segment++)
            {
                var next = (segment + 1) % segments;
                triangles[triangle++] = 0;
                triangles[triangle++] = 1 + segment;
                triangles[triangle++] = 1 + next;
            }

            for (var ring = 1; ring < rings; ring++)
            {
                var innerStart =
                    1 + (ring - 1) * segments;
                var outerStart =
                    1 + ring * segments;
                for (var segment = 0;
                     segment < segments;
                     segment++)
                {
                    var next = (segment + 1) % segments;
                    var innerCurrent = innerStart + segment;
                    var innerNext = innerStart + next;
                    var outerCurrent = outerStart + segment;
                    var outerNext = outerStart + next;

                    if (((ring + segment + spec.Seed) & 1) == 0)
                    {
                        triangles[triangle++] = innerCurrent;
                        triangles[triangle++] = outerCurrent;
                        triangles[triangle++] = innerNext;
                        triangles[triangle++] = outerCurrent;
                        triangles[triangle++] = outerNext;
                        triangles[triangle++] = innerNext;
                    }
                    else
                    {
                        triangles[triangle++] = innerCurrent;
                        triangles[triangle++] = outerNext;
                        triangles[triangle++] = innerNext;
                        triangles[triangle++] = innerCurrent;
                        triangles[triangle++] = outerCurrent;
                        triangles[triangle++] = outerNext;
                    }
                }
            }

            var mesh = new Mesh
            {
                name = "MESH_" + spec.Name,
                indexFormat =
                    vertexCount > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ShapeCentre(
            Shape shape,
            out float horizontal,
            out float vertical,
            out float depthScale)
        {
            horizontal = 0f;
            vertical = 0f;
            depthScale = 1f;
            switch (shape)
            {
                case Shape.BaseMass:
                    vertical = -0.08f;
                    depthScale = 1.10f;
                    break;
                case Shape.UpperLip:
                    vertical = 0.08f;
                    depthScale = 1.08f;
                    break;
                case Shape.Shoulder:
                    horizontal = 0.08f;
                    depthScale = 1.04f;
                    break;
            }
        }

        private static void PolarShapeProfile(
            Shape shape,
            float directionX,
            float directionY,
            float radial,
            out float horizontalScale,
            out float verticalScale,
            out float depthScale,
            out float horizontalShift,
            out float verticalShift)
        {
            horizontalScale = 1f;
            verticalScale = 1f;
            depthScale = 1f;
            horizontalShift = 0f;
            verticalShift = 0f;

            switch (shape)
            {
                case Shape.WideBulge:
                    horizontalScale =
                        0.92f +
                        (1f - Mathf.Abs(directionY)) * 0.08f;
                    depthScale =
                        0.94f +
                        directionX * 0.06f;
                    horizontalShift =
                        (1f - radial) * 0.015f;
                    break;

                case Shape.TallPillar:
                    horizontalScale =
                        0.82f +
                        (1f - Mathf.Abs(directionY)) * 0.18f;
                    depthScale =
                        0.96f +
                        directionY * 0.04f;
                    horizontalShift =
                        directionY * (1f - radial) * 0.025f;
                    break;

                case Shape.BaseMass:
                    horizontalScale =
                        Mathf.Lerp(
                            1.10f,
                            0.84f,
                            directionY * 0.5f + 0.5f);
                    verticalScale =
                        directionY < 0f ? 0.88f : 1.00f;
                    depthScale =
                        1.06f - directionY * 0.14f;
                    horizontalShift =
                        (1f - radial) * -0.02f;
                    verticalShift = -0.025f * (1f - radial);
                    break;

                case Shape.UpperLip:
                    horizontalScale =
                        0.94f +
                        (1f - Mathf.Abs(directionY)) * 0.06f;
                    verticalScale =
                        directionY > 0f ? 0.86f : 1.00f;
                    depthScale =
                        1.02f + directionY * 0.16f;
                    horizontalShift =
                        (1f - radial) * 0.018f;
                    verticalShift = 0.02f * (1f - radial);
                    break;

                case Shape.Shoulder:
                    horizontalScale =
                        0.90f +
                        (1f - Mathf.Abs(directionY)) * 0.10f;
                    depthScale =
                        Mathf.Lerp(
                            0.78f,
                            1.16f,
                            directionX * 0.5f + 0.5f);
                    horizontalShift =
                        (1f - radial) * 0.07f;
                    break;
            }
        }

        private static GameObject BuildOrUpdatePrefab(
            ModuleSpec spec,
            Mesh mesh,
            Material material)
        {
            var authoringRoot = new GameObject(spec.Name);
            try
            {
                var filter = authoringRoot.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer =
                    authoringRoot.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode =
                    ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.Camera;
                ApplyStaticFlags(authoringRoot);

                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    authoringRoot,
                    spec.PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Could not save cliff module prefab: " +
                        spec.PrefabPath);
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(authoringRoot);
            }
        }

        private static StarterIslandTerraceField.Terrace FindTerrace(
            string name)
        {
            var terraces = StarterIslandTerraceField.Terraces;
            for (var index = 0; index < terraces.Length; index++)
            {
                if (string.Equals(
                        terraces[index].Name,
                        name,
                        StringComparison.Ordinal))
                {
                    return terraces[index];
                }
            }

            throw new InvalidOperationException(
                "Unknown Starter Island terrace: " + name);
        }

        private static Vector3 FindWallMidpoint(
            Terrain terrain,
            StarterIslandTerraceField.Terrace terrace,
            float angleDegrees)
        {
            var angle = angleDegrees * Mathf.Deg2Rad;
            var meanRadius =
                (terrace.RadiusX + terrace.RadiusZ) * 0.5f;
            var feather =
                Mathf.Max(
                    0.004f,
                    terrace.EdgeMetres / meanRadius);
            var targetDistance = 1f + feather * 0.52f;
            var low = 0.78f;
            var high = 1.28f;

            for (var iteration = 0; iteration < 24; iteration++)
            {
                var radius = (low + high) * 0.5f;
                var x =
                    terrace.CenterX +
                    Mathf.Cos(angle) *
                    terrace.RadiusX *
                    radius;
                var z =
                    terrace.CenterZ +
                    Mathf.Sin(angle) *
                    terrace.RadiusZ *
                    radius;
                var distance =
                    StarterIslandTerraceField.OutlineDistance(
                        x,
                        z,
                        terrace);
                if (distance < targetDistance)
                {
                    low = radius;
                }
                else
                {
                    high = radius;
                }
            }

            var solvedRadius = (low + high) * 0.5f;
            var point = new Vector3(
                terrace.CenterX +
                Mathf.Cos(angle) *
                terrace.RadiusX *
                solvedRadius,
                0f,
                terrace.CenterZ +
                Mathf.Sin(angle) *
                terrace.RadiusZ *
                solvedRadius);
            point.y =
                terrain.SampleHeight(point) +
                terrain.transform.position.y;
            return point;
        }

        private static void ValidateSceneKit(
            GameObject root,
            int expectedPlacements)
        {
            if (root == null || root.name != SceneRootName)
            {
                throw new InvalidOperationException(
                    "Cliff rock kit root contract is invalid.");
            }

            if (root.transform.childCount != expectedPlacements)
            {
                throw new InvalidOperationException(
                    "Cliff rock placement count mismatch.");
            }

            if (root.GetComponentsInChildren<Collider>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Cliff rock kit must not contain colliders.");
            }

            if (root.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Cliff rock kit must not contain rigidbodies.");
            }

            var filters =
                root.GetComponentsInChildren<MeshFilter>(true);
            var renderers =
                root.GetComponentsInChildren<MeshRenderer>(true);
            if (filters.Length != expectedPlacements ||
                renderers.Length != expectedPlacements)
            {
                throw new InvalidOperationException(
                    "Every cliff placement must have one mesh and renderer.");
            }

            for (var index = 0; index < filters.Length; index++)
            {
                if (filters[index].sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        "Cliff placement has no mesh.");
                }

                var scale = filters[index].transform.lossyScale;
                if (!IsPositiveFinite(scale.x) ||
                    !IsPositiveFinite(scale.y) ||
                    !IsPositiveFinite(scale.z))
                {
                    throw new InvalidOperationException(
                        "Cliff placement has an invalid scale.");
                }
            }
        }

        private static void RemovePhysics(GameObject root)
        {
            foreach (var collider in
                     root.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            foreach (var body in
                     root.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(body);
            }
        }

        private static void ApplyStaticFlags(GameObject root)
        {
            foreach (var transform in
                     root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(
                    transform.gameObject,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ReflectionProbeStatic);
            }
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(
                        current,
                        segments[index]);
                }

                current = next;
            }
        }
    }
}
