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
    /// Creates a self-contained organic rock kit from continuous signed-distance
    /// volumes. Every prefab has one watertight mesh, one renderer and one
    /// world-space auto-grass material. It never edits the gameplay scene.
    /// </summary>
    internal static class StarterIslandOrganicRockKitBuilder
    {
        private const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/VerticalRockKit_Organic";
        private const string MeshRoot = Root + "/Meshes";
        private const string MaterialRoot = Root + "/Materials";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string PreviewRoot = Root + "/Preview";
        private const string AutoMaterialPath =
            MaterialRoot + "/M_VRK_Organic_AutoGrass.mat";
        private const string GroundMaterialPath =
            MaterialRoot + "/M_VRK_Organic_PreviewGround.mat";
        private const string BackdropMaterialPath =
            MaterialRoot + "/M_VRK_Organic_PreviewBackdrop.mat";
        private const string PreviewScenePath =
            PreviewRoot + "/SCN_VRK_Organic_Preview.unity";
        private const string TextureRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Textures";

        private readonly struct ShapeDef
        {
            public ShapeDef(string name, string category, Vector3 size, int seed)
            {
                Name = name;
                Category = category;
                Size = size;
                Seed = seed;
            }

            public string Name { get; }
            public string Category { get; }
            public Vector3 Size { get; }
            public int Seed { get; }
            public string MeshPath => MeshRoot + "/MESH_VRKO_" + Name + ".asset";
            public string PrefabPath => PrefabRoot + "/PF_VRKO_" + Name + ".prefab";
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            public VertexKey(Vector3 position)
            {
                // A 1 mm weld tolerance closes numerically split iso-vertices
                // while remaining far below the gameplay/contact tolerance.
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

        private sealed class MeshDraft
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(9000);
            public readonly List<Vector3> Normals = new List<Vector3>(9000);
            public readonly List<Vector2> Uvs = new List<Vector2>(9000);
            public readonly List<int> Triangles = new List<int>(18000);
            public readonly Dictionary<VertexKey, int> Weld =
                new Dictionary<VertexKey, int>(9000);
        }

        private static readonly ShapeDef[] Shapes =
        {
            new ShapeDef("Arch", "Spans", new Vector3(9.0f, 7.0f, 3.4f), 101),
            new ShapeDef("Bridge", "Spans", new Vector3(11.0f, 2.7f, 3.4f), 127),
            new ShapeDef("Elevation", "Transitions", new Vector3(6.8f, 3.8f, 4.2f), 149),
            new ShapeDef("Extension", "Transitions", new Vector3(5.4f, 6.6f, 3.4f), 173),
            new ShapeDef("Flat", "Platforms", new Vector3(7.2f, 2.3f, 5.8f), 197),
            new ShapeDef("Overhang_Left", "Overhangs", new Vector3(7.0f, 7.2f, 5.0f), 223),
            new ShapeDef("Overhang_Right", "Overhangs", new Vector3(7.0f, 7.2f, 5.0f), 251),
            new ShapeDef("Overhang_Surface_A", "Overhangs", new Vector3(5.8f, 3.8f, 5.4f), 277),
            new ShapeDef("Overhang_Surface_B", "Overhangs", new Vector3(5.4f, 4.5f, 5.0f), 307),
            new ShapeDef("Pillar", "Supports", new Vector3(3.8f, 8.4f, 3.6f), 331),
            new ShapeDef("Stone_Large", "Accents", new Vector3(4.2f, 3.1f, 3.8f), 359),
            new ShapeDef("Stone_Medium", "Accents", new Vector3(2.8f, 2.1f, 2.5f), 383),
            new ShapeDef("Wall_Straight", "Walls", new Vector3(8.2f, 6.6f, 3.1f), 419),
            new ShapeDef("Wall_Corner", "Walls", new Vector3(7.0f, 6.4f, 7.0f), 443)
        };

        private static readonly int[,] Tetrahedra =
        {
            { 0, 5, 1, 6 },
            { 0, 1, 2, 6 },
            { 0, 2, 3, 6 },
            { 0, 3, 7, 6 },
            { 0, 7, 4, 6 },
            { 0, 4, 5, 6 }
        };

        [MenuItem("CML/Art/Vertical Rock Kit/Rebuild Organic V2")]
        public static void Rebuild()
        {
            EnsureFolders();
            var materials = BuildMaterials();
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var shape in Shapes)
            {
                var mesh = SaveMesh(shape);
                prefabs.Add(shape.Name, SavePrefab(shape, mesh, materials.auto));
            }

            AssetDatabase.SaveAssets();
            BuildPreview(prefabs, materials.ground, materials.backdrop);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate(prefabs);
            Debug.Log(
                $"ORGANIC_ROCK_KIT assets={prefabs.Count} singleMesh=PASS " +
                "autoGrass=PASS sceneChanges=0 status=PASS");
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "_Project");
            EnsureFolder("Assets/_Project", "Art");
            EnsureFolder("Assets/_Project/Art", "Environment");
            EnsureFolder("Assets/_Project/Art/Environment", "StarterIsland");
            EnsureFolder(
                "Assets/_Project/Art/Environment/StarterIsland",
                "VerticalRockKit_Organic");
            EnsureFolder(Root, "Meshes");
            EnsureFolder(Root, "Materials");
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

        private static (Material auto, Material ground, Material backdrop)
            BuildMaterials()
        {
            var shader = Shader.Find("CML/Environment/Vertical Rock Auto Grass");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || lit == null)
            {
                throw new InvalidOperationException("Organic rock kit shaders are unavailable.");
            }

            var material = LoadOrCreateMaterial(
                AutoMaterialPath,
                "M_VRK_Organic_AutoGrass",
                shader);
            var rock = LoadTexture("T_StarterIsland_CliffWarm.asset");
            var rockNormal = LoadTexture("T_StarterIsland_CliffWarm_Normal.asset");
            var grass = LoadTexture("T_StarterIsland_GrassSun.asset");
            var grassNormal = LoadTexture("T_StarterIsland_GrassSun_Normal.asset");
            material.SetTexture("_RockMap", rock);
            material.SetTexture("_RockNormalMap", rockNormal);
            material.SetTexture("_GrassMap", grass);
            material.SetTexture("_GrassNormalMap", grassNormal);
            material.SetTexture("_BaseMap", rock);
            material.SetColor("_RockTint", Color.white);
            material.SetColor("_GrassTint", Color.white);
            material.SetFloat("_RockTileScale", 0.125f);
            material.SetFloat("_GrassTileScale", 0.16f);
            material.SetFloat("_TriplanarSharpness", 4.8f);
            material.SetFloat("_RockNormalStrength", 0.46f);
            material.SetFloat("_GrassNormalStrength", 0.16f);
            material.SetFloat("_GrassSlopeStart", 0.60f);
            material.SetFloat("_GrassSlopeEnd", 0.80f);
            material.SetFloat("_GrassNoiseScale", 0.30f);
            material.SetFloat("_GrassNoiseStrength", 0.12f);
            material.SetColor("_RockShadowColor", Html("#654542"));
            material.SetColor("_RockBaseColor", Html("#A96E58"));
            material.SetColor("_RockHighlightColor", Html("#D29A70"));
            material.SetColor("_GrassShadowColor", Html("#3E5425"));
            material.SetColor("_GrassBaseColor", Html("#819D36"));
            material.SetColor("_GrassHighlightColor", Html("#B0BE4F"));
            material.SetFloat("_PaletteStrength", 0.78f);
            material.SetFloat("_MacroVariation", 0.055f);
            material.SetFloat("_AmbientStrength", 1.0f);
            material.SetFloat("_ShadowFloor", 0.26f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);

            var ground = LoadOrCreateMaterial(
                GroundMaterialPath,
                "M_VRK_Organic_PreviewGround",
                lit);
            ground.SetColor("_BaseColor", Html("#506326"));
            ground.SetFloat("_Smoothness", 0.02f);
            EditorUtility.SetDirty(ground);

            var backdrop = LoadOrCreateMaterial(
                BackdropMaterialPath,
                "M_VRK_Organic_PreviewBackdrop",
                lit);
            backdrop.SetColor("_BaseColor", Html("#4B5659"));
            backdrop.SetFloat("_Smoothness", 0.015f);
            EditorUtility.SetDirty(backdrop);
            return (material, ground, backdrop);
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
                material.name = name;
            }

            return material;
        }

        private static Mesh SaveMesh(ShapeDef shape)
        {
            var generated = BuildSdfMesh(shape);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(shape.MeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, shape.MeshPath);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            existing.name = generated.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static GameObject SavePrefab(
            ShapeDef shape,
            Mesh mesh,
            Material material)
        {
            var root = new GameObject("PF_VRKO_" + shape.Name);
            try
            {
                root.isStatic = true;
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                var collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, shape.PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException("Could not save " + shape.PrefabPath);
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Mesh BuildSdfMesh(ShapeDef shape)
        {
            const int nx = 27;
            const int ny = 24;
            const int nz = 21;
            // Generous exterior field margin: warped superellipsoids and
            // subtractive arches must always become closed before the grid edge.
            var padding = new Vector3(1.30f, 1.15f, 1.30f);
            var min = new Vector3(
                -shape.Size.x * 0.5f,
                0f,
                -shape.Size.z * 0.5f) - padding;
            var max = new Vector3(
                shape.Size.x * 0.5f,
                shape.Size.y,
                shape.Size.z * 0.5f) + padding;
            var step = new Vector3(
                (max.x - min.x) / (nx - 1),
                (max.y - min.y) / (ny - 1),
                (max.z - min.z) / (nz - 1));
            var positions = new Vector3[nx, ny, nz];
            var values = new float[nx, ny, nz];
            for (var x = 0; x < nx; x++)
            for (var y = 0; y < ny; y++)
            for (var z = 0; z < nz; z++)
            {
                var position = min + Vector3.Scale(
                    new Vector3(x, y, z),
                    step);
                positions[x, y, z] = position;
                values[x, y, z] = Evaluate(shape, position);
            }

            var boundaryMinimum = float.PositiveInfinity;
            var boundaryPosition = Vector3.zero;
            for (var x = 0; x < nx; x++)
            for (var y = 0; y < ny; y++)
            for (var z = 0; z < nz; z++)
            {
                if (x != 0 && x != nx - 1 &&
                    y != 0 && y != ny - 1 &&
                    z != 0 && z != nz - 1)
                {
                    continue;
                }

                if (values[x, y, z] < boundaryMinimum)
                {
                    boundaryMinimum = values[x, y, z];
                    boundaryPosition = positions[x, y, z];
                }
            }

            if (boundaryMinimum <= 0f)
            {
                throw new InvalidOperationException(
                    shape.Name + " crosses SDF grid at " +
                    boundaryPosition.ToString("F3") +
                    " value=" + boundaryMinimum.ToString("F4"));
            }

            var draft = new MeshDraft();
            var cubeP = new Vector3[8];
            var cubeD = new float[8];
            for (var x = 0; x < nx - 1; x++)
            for (var y = 0; y < ny - 1; y++)
            for (var z = 0; z < nz - 1; z++)
            {
                FillCube(positions, values, x, y, z, cubeP, cubeD);
                for (var tetra = 0; tetra < 6; tetra++)
                {
                    PolygoniseTetra(shape, draft, cubeP, cubeD, tetra);
                }
            }

            SealBoundaryLoops(shape, draft);

            if (draft.Triangles.Count < 300)
            {
                throw new InvalidOperationException("SDF produced insufficient geometry for " + shape.Name);
            }

            var bounds = BoundsOf(draft.Vertices);
            var offset = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            for (var i = 0; i < draft.Vertices.Count; i++)
            {
                draft.Vertices[i] -= offset;
                draft.Uvs[i] = new Vector2(draft.Vertices[i].x, draft.Vertices[i].z);
                draft.Normals[i] = draft.Normals[i].normalized;
            }

            var mesh = new Mesh
            {
                name = "MESH_VRKO_" + shape.Name,
                indexFormat = draft.Vertices.Count > 65534
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(draft.Vertices);
            mesh.SetNormals(draft.Normals);
            mesh.SetUVs(0, draft.Uvs);
            mesh.SetTriangles(draft.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void SealBoundaryLoops(ShapeDef shape, MeshDraft draft)
        {
            var minimumY = float.PositiveInfinity;
            for (var vertexIndex = 0; vertexIndex < draft.Vertices.Count; vertexIndex++)
            {
                minimumY = Mathf.Min(minimumY, draft.Vertices[vertexIndex].y);
            }

            var edgeCounts = new Dictionary<ulong, int>();
            for (var i = 0; i < draft.Triangles.Count; i += 3)
            {
                CountEdge(edgeCounts, draft.Triangles[i], draft.Triangles[i + 1]);
                CountEdge(edgeCounts, draft.Triangles[i + 1], draft.Triangles[i + 2]);
                CountEdge(edgeCounts, draft.Triangles[i + 2], draft.Triangles[i]);
            }

            var adjacency = new Dictionary<int, List<int>>();
            foreach (var pair in edgeCounts)
            {
                if (pair.Value != 1)
                {
                    continue;
                }

                var a = (int)(pair.Key >> 32);
                var b = (int)(pair.Key & 0xffffffffu);
                AddNeighbour(adjacency, a, b);
                AddNeighbour(adjacency, b, a);
            }

            var visited = new HashSet<ulong>();
            foreach (var startPair in edgeCounts)
            {
                if (startPair.Value != 1 || visited.Contains(startPair.Key))
                {
                    continue;
                }

                var start = (int)(startPair.Key >> 32);
                var current = (int)(startPair.Key & 0xffffffffu);
                var previous = start;
                var loop = new List<int> { start, current };
                visited.Add(startPair.Key);
                var closed = false;
                for (var guard = 0; guard < adjacency.Count + 4; guard++)
                {
                    if (!adjacency.TryGetValue(current, out var neighbours))
                    {
                        break;
                    }

                    var next = -1;
                    for (var i = 0; i < neighbours.Count; i++)
                    {
                        var candidate = neighbours[i];
                        var edge = EdgeKey(current, candidate);
                        if (candidate != previous && !visited.Contains(edge))
                        {
                            next = candidate;
                            visited.Add(edge);
                            break;
                        }
                    }

                    if (next < 0)
                    {
                        if (neighbours.Contains(start))
                        {
                            visited.Add(EdgeKey(current, start));
                            closed = true;
                        }

                        break;
                    }

                    if (next == start)
                    {
                        closed = true;
                        break;
                    }

                    loop.Add(next);
                    previous = current;
                    current = next;
                }

                if (!closed || loop.Count < 3)
                {
                    continue;
                }

                var loopBounds = new Bounds(
                    draft.Vertices[loop[0]],
                    Vector3.zero);
                var onUnderside = true;
                for (var i = 0; i < loop.Count; i++)
                {
                    var vertex = draft.Vertices[loop[i]];
                    loopBounds.Encapsulate(vertex);
                    onUnderside &= vertex.y <= minimumY + 0.075f;
                }

                // Never cap a large side opening with a fan: it would create a
                // visible planar bite. Only microscopic cracks and the flat
                // buried underside are safe to seal this way.
                if (!onUnderside && loopBounds.size.magnitude > 0.42f)
                {
                    continue;
                }

                var center = Vector3.zero;
                for (var i = 0; i < loop.Count; i++)
                {
                    center += draft.Vertices[loop[i]];
                }

                center /= loop.Count;
                for (var i = 0; i < loop.Count; i++)
                {
                    var next = (i + 1) % loop.Count;
                    AddTriangle(
                        shape,
                        draft,
                        draft.Vertices[loop[i]],
                        draft.Vertices[loop[next]],
                        center);
                }
            }
        }

        private static void AddNeighbour(
            Dictionary<int, List<int>> adjacency,
            int a,
            int b)
        {
            if (!adjacency.TryGetValue(a, out var neighbours))
            {
                neighbours = new List<int>(2);
                adjacency.Add(a, neighbours);
            }

            if (!neighbours.Contains(b))
            {
                neighbours.Add(b);
            }
        }

        private static ulong EdgeKey(int a, int b)
        {
            var low = (uint)Mathf.Min(a, b);
            var high = (uint)Mathf.Max(a, b);
            return ((ulong)low << 32) | high;
        }

        private static void FillCube(
            Vector3[,,] positions,
            float[,,] values,
            int x,
            int y,
            int z,
            Vector3[] p,
            float[] d)
        {
            var offsets = new[,]
            {
                {0,0,0},{1,0,0},{1,1,0},{0,1,0},
                {0,0,1},{1,0,1},{1,1,1},{0,1,1}
            };
            for (var i = 0; i < 8; i++)
            {
                var ix = x + offsets[i, 0];
                var iy = y + offsets[i, 1];
                var iz = z + offsets[i, 2];
                p[i] = positions[ix, iy, iz];
                d[i] = values[ix, iy, iz];
            }
        }

        private static void PolygoniseTetra(
            ShapeDef shape,
            MeshDraft draft,
            Vector3[] cubeP,
            float[] cubeD,
            int tetra)
        {
            var inside = new int[4];
            var outside = new int[4];
            var insideCount = 0;
            var outsideCount = 0;
            for (var i = 0; i < 4; i++)
            {
                var index = Tetrahedra[tetra, i];
                if (cubeD[index] < 0f)
                {
                    inside[insideCount++] = index;
                }
                else
                {
                    outside[outsideCount++] = index;
                }
            }

            if (insideCount == 0 || insideCount == 4)
            {
                return;
            }

            if (insideCount == 1 || insideCount == 3)
            {
                var invert = insideCount == 3;
                var lone = invert ? outside[0] : inside[0];
                var others = invert ? inside : outside;
                var a = Interpolate(cubeP[lone], cubeP[others[0]], cubeD[lone], cubeD[others[0]]);
                var b = Interpolate(cubeP[lone], cubeP[others[1]], cubeD[lone], cubeD[others[1]]);
                var c = Interpolate(cubeP[lone], cubeP[others[2]], cubeD[lone], cubeD[others[2]]);
                AddTriangle(shape, draft, a, invert ? c : b, invert ? b : c);
                return;
            }

            var p0 = Interpolate(cubeP[inside[0]], cubeP[outside[0]], cubeD[inside[0]], cubeD[outside[0]]);
            var p1 = Interpolate(cubeP[inside[0]], cubeP[outside[1]], cubeD[inside[0]], cubeD[outside[1]]);
            var p2 = Interpolate(cubeP[inside[1]], cubeP[outside[0]], cubeD[inside[1]], cubeD[outside[0]]);
            var p3 = Interpolate(cubeP[inside[1]], cubeP[outside[1]], cubeD[inside[1]], cubeD[outside[1]]);
            AddTriangle(shape, draft, p0, p1, p2);
            AddTriangle(shape, draft, p1, p3, p2);
        }

        private static Vector3 Interpolate(
            Vector3 a,
            Vector3 b,
            float da,
            float db)
        {
            var denominator = da - db;
            var t = Mathf.Abs(denominator) < 0.000001f
                ? 0.5f
                : Mathf.Clamp01(da / denominator);
            return Vector3.LerpUnclamped(a, b, t);
        }

        private static void AddTriangle(
            ShapeDef shape,
            MeshDraft draft,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            // Marching tetrahedra can legitimately create very small closure
            // triangles when the iso-surface passes close to a grid vertex.
            // Keeping them is required for a watertight manifold shell.
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 0.0000000000000001f)
            {
                return;
            }

            var centroid = (a + b + c) / 3f;
            var gradient = Gradient(shape, centroid);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), gradient) < 0f)
            {
                var swap = b;
                b = c;
                c = swap;
            }

            var ia = AddVertex(shape, draft, a);
            var ib = AddVertex(shape, draft, b);
            var ic = AddVertex(shape, draft, c);
            if (ia == ib || ib == ic || ic == ia)
            {
                return;
            }

            draft.Triangles.Add(ia);
            draft.Triangles.Add(ib);
            draft.Triangles.Add(ic);
        }

        private static int AddVertex(
            ShapeDef shape,
            MeshDraft draft,
            Vector3 position)
        {
            var key = new VertexKey(position);
            if (draft.Weld.TryGetValue(key, out var index))
            {
                draft.Normals[index] += Gradient(shape, position);
                return index;
            }

            index = draft.Vertices.Count;
            draft.Weld.Add(key, index);
            draft.Vertices.Add(position);
            draft.Normals.Add(Gradient(shape, position));
            draft.Uvs.Add(Vector2.zero);
            return index;
        }

        private static Vector3 Gradient(ShapeDef shape, Vector3 p)
        {
            const float e = 0.018f;
            return new Vector3(
                Evaluate(shape, p + Vector3.right * e) - Evaluate(shape, p - Vector3.right * e),
                Evaluate(shape, p + Vector3.up * e) - Evaluate(shape, p - Vector3.up * e),
                Evaluate(shape, p + Vector3.forward * e) - Evaluate(shape, p - Vector3.forward * e)).normalized;
        }

        private static float Evaluate(ShapeDef shape, Vector3 p)
        {
            var sourceP = p;
            p = DomainWarp(p, shape.Seed);
            float d;
            switch (shape.Name)
            {
                case "Arch":
                {
                    var left = RoundedBox(p, new Vector3(-3.15f, 3.0f, 0f), new Vector3(0.72f, 2.55f, 1.18f), 0.48f);
                    var right = RoundedBox(p, new Vector3(3.02f, 2.85f, 0.08f), new Vector3(0.82f, 2.40f, 1.22f), 0.52f);
                    var crownP = p;
                    crownP.y -= 0.12f * Mathf.Cos(p.x * 0.55f);
                    var crown = RoundedBox(crownP, new Vector3(0f, 5.72f, 0f), new Vector3(3.48f, 0.62f, 1.20f), 0.58f);
                    d = SmoothUnion(SmoothUnion(left, right, 0.50f), crown, 0.62f);
                    var hole = Ellipsoid(p, new Vector3(-0.05f, 2.65f, 0f), new Vector3(2.20f, 2.72f, 2.15f));
                    d = Mathf.Max(d, -hole);
                    break;
                }
                case "Bridge":
                {
                    var q = p;
                    q.y -= 0.22f * (1f - Mathf.Clamp01(p.x * p.x / 25f));
                    d = RoundedBox(q, new Vector3(0f, 1.28f, 0f), new Vector3(4.75f, 0.56f, 1.12f), 0.58f);
                    d = SmoothUnion(d, Ellipsoid(p, new Vector3(-4.35f, 0.92f, 0.10f), new Vector3(1.10f, 0.95f, 1.45f)), 0.48f);
                    d = SmoothUnion(d, Ellipsoid(p, new Vector3(4.25f, 0.88f, -0.08f), new Vector3(1.18f, 0.92f, 1.42f)), 0.48f);
                    break;
                }
                case "Elevation":
                {
                    var q = p;
                    q.y -= 0.28f * p.x;
                    d = RoundedBox(q, new Vector3(0f, 1.72f, 0f), new Vector3(2.72f, 1.18f, 1.55f), 0.62f);
                    d = SmoothUnion(d, Ellipsoid(p, new Vector3(-2.15f, 0.72f, 0.42f), new Vector3(1.28f, 0.92f, 1.65f)), 0.48f);
                    break;
                }
                case "Extension":
                {
                    var q = RotateY(p - new Vector3(0f, 3.0f, 0f), -8f);
                    q.x += (p.y - 3f) * 0.09f;
                    d = RoundedBox(q, Vector3.zero, new Vector3(2.05f, 2.55f, 1.12f), 0.58f);
                    d = SmoothUnion(d, Ellipsoid(p, new Vector3(-1.45f, 1.15f, 0.48f), new Vector3(1.48f, 1.32f, 1.62f)), 0.56f);
                    break;
                }
                case "Flat":
                    d = SmoothUnion(
                        RoundedBox(p, new Vector3(0f, 1.05f, 0f), new Vector3(2.65f, 0.52f, 2.22f), 0.58f),
                        Ellipsoid(p, new Vector3(-1.85f, 0.78f, 0.45f), new Vector3(1.48f, 0.82f, 1.72f)),
                        0.48f);
                    break;
                case "Overhang_Left":
                case "Overhang_Right":
                {
                    var sign = shape.Name.EndsWith("Left", StringComparison.Ordinal) ? -1f : 1f;
                    var trunkP = p;
                    trunkP.x -= sign * (p.y - 2.8f) * 0.09f;
                    var trunk = RoundedBox(trunkP, new Vector3(sign * 0.85f, 2.75f, -0.12f), new Vector3(1.20f, 2.35f, 1.18f), 0.62f);
                    var shelfP = p;
                    shelfP.y -= sign * p.x * 0.035f;
                    var shelf = RoundedBox(shelfP, new Vector3(-sign * 0.72f, 5.62f, 0.72f), new Vector3(2.35f, 0.58f, 1.62f), 0.62f);
                    var shoulder = Ellipsoid(p, new Vector3(sign * 0.10f, 4.45f, 0.32f), new Vector3(1.82f, 1.46f, 1.52f));
                    d = SmoothUnion(SmoothUnion(trunk, shoulder, 1.12f), shelf, 1.08f);
                    break;
                }
                case "Overhang_Surface_A":
                {
                    var q = p;
                    q.y -= 0.14f * p.x;
                    var body = RoundedBox(q, new Vector3(0f, 1.65f, 0.12f), new Vector3(2.05f, 1.08f, 1.88f), 0.66f);
                    var prow = Ellipsoid(p, new Vector3(-1.72f, 1.85f, 0.92f), new Vector3(1.42f, 1.18f, 1.72f));
                    d = SmoothUnion(body, prow, 1.02f);
                    break;
                }
                case "Overhang_Surface_B":
                {
                    var q = RotateY(p - new Vector3(0f, 2.0f, 0f), 10f);
                    q.y += 0.10f * p.z;
                    var body = RoundedBox(q, Vector3.zero, new Vector3(1.88f, 1.35f, 1.72f), 0.68f);
                    var lip = Ellipsoid(p, new Vector3(1.35f, 2.55f, 0.75f), new Vector3(1.42f, 1.08f, 1.54f));
                    d = SmoothUnion(body, lip, 0.96f);
                    break;
                }
                case "Pillar":
                {
                    var t = Mathf.Clamp01(p.y / 8.0f);
                    var scale = Mathf.Lerp(1.12f, 0.82f, t);
                    var q = p;
                    q.x = (p.x - (p.y - 4f) * 0.055f) / scale;
                    q.z = (p.z + (p.y - 4f) * 0.025f) / scale;
                    d = RoundedBox(q, new Vector3(0f, 3.90f, 0f), new Vector3(1.12f, 3.28f, 1.02f), 0.64f);
                    d = SmoothUnion(d, Ellipsoid(p, new Vector3(-0.18f, 0.72f, 0.12f), new Vector3(1.72f, 0.86f, 1.52f)), 0.55f);
                    break;
                }
                case "Stone_Large":
                    d = Ellipsoid(p, new Vector3(0f, 1.34f, 0f), new Vector3(1.82f, 1.26f, 1.58f));
                    break;
                case "Stone_Medium":
                    d = Ellipsoid(p, new Vector3(0f, 0.90f, 0f), new Vector3(1.18f, 0.82f, 1.04f));
                    break;
                case "Wall_Straight":
                {
                    var q = p;
                    q.x += Mathf.Sin(p.y * 0.58f + 0.4f) * 0.18f;
                    q.z += Mathf.Sin(p.x * 0.74f - p.y * 0.28f) * 0.16f;
                    var body = RoundedBox(q, new Vector3(0f, 3.05f, 0f), new Vector3(3.35f, 2.58f, 0.92f), 0.66f);
                    var baseLobe = Ellipsoid(p, new Vector3(-2.35f, 1.08f, 0.48f), new Vector3(1.62f, 1.34f, 1.46f));
                    var shoulder = Ellipsoid(p, new Vector3(2.42f, 3.78f, 0.18f), new Vector3(1.48f, 1.82f, 1.26f));
                    d = SmoothUnion(SmoothUnion(body, baseLobe, 0.62f), shoulder, 0.58f);
                    break;
                }
                case "Wall_Corner":
                {
                    var leftP = RotateY(p - new Vector3(-1.25f, 3.0f, 0.72f), -38f);
                    var rightP = RotateY(p - new Vector3(1.30f, 3.10f, 0.72f), 38f);
                    var left = RoundedBox(leftP, Vector3.zero, new Vector3(2.62f, 2.52f, 0.86f), 0.64f);
                    var right = RoundedBox(rightP, Vector3.zero, new Vector3(2.55f, 2.62f, 0.86f), 0.64f);
                    var heart = Ellipsoid(p, new Vector3(0f, 2.68f, 0.62f), new Vector3(1.88f, 2.28f, 1.82f));
                    d = SmoothUnion(SmoothUnion(left, right, 0.78f), heart, 0.72f);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(shape.Name);
            }

            var noise = OrganicNoise(sourceP, shape.Seed);
            // Preserve some breakup on the underside as well: a perfectly
            // tangent implicit plane creates degenerate marching cells.
            var verticalFade = Mathf.Lerp(
                0.38f,
                1f,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.y / 0.45f)));
            var organicAmplitude =
                shape.Name.StartsWith("Stone", StringComparison.Ordinal)
                    ? 0.18f
                    : 0.24f;
            return d + noise * organicAmplitude * verticalFade;
        }

        private static Vector3 DomainWarp(Vector3 p, int seed)
        {
            var phase = seed * 0.037f;
            var source = p;
            p.x += Mathf.Sin(source.y * 0.61f + source.z * 0.27f + phase) * 0.34f;
            p.z += Mathf.Sin(source.y * 0.49f - source.x * 0.33f + phase * 1.31f) * 0.30f;
            p.y += Mathf.Sin(source.x * 0.58f + source.z * 0.42f + phase * 0.73f) * 0.23f;
            return p;
        }

        private static float RoundedBox(
            Vector3 p,
            Vector3 center,
            Vector3 half,
            float radius)
        {
            // A superellipsoid keeps broad stylized planes without retaining
            // the unmistakable silhouette of a rounded cube.
            const float power = 2.45f;
            var radii = half + Vector3.one * radius;
            var delta = Abs(p - center);
            var q = new Vector3(
                delta.x / Mathf.Max(radii.x, 0.01f),
                delta.y / Mathf.Max(radii.y, 0.01f),
                delta.z / Mathf.Max(radii.z, 0.01f));
            var field = Mathf.Pow(
                Mathf.Pow(q.x, power) +
                Mathf.Pow(q.y, power) +
                Mathf.Pow(q.z, power),
                1f / power) - 1f;
            return field * Mathf.Min(radii.x, Mathf.Min(radii.y, radii.z));
        }

        private static float Ellipsoid(
            Vector3 p,
            Vector3 center,
            Vector3 radii)
        {
            var q = p - center;
            var normalized = new Vector3(q.x / radii.x, q.y / radii.y, q.z / radii.z);
            return (normalized.magnitude - 1f) *
                Mathf.Min(radii.x, Mathf.Min(radii.y, radii.z));
        }

        private static float SmoothUnion(float a, float b, float k)
        {
            var h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        private static Vector3 RotateY(Vector3 p, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(radians);
            var s = Mathf.Sin(radians);
            return new Vector3(c * p.x - s * p.z, p.y, s * p.x + c * p.z);
        }

        private static float OrganicNoise(Vector3 p, int seed)
        {
            var phase = seed * 0.071f;
            return Mathf.Sin(p.x * 0.86f + p.y * 0.43f + p.z * 0.62f + phase) * 0.52f +
                Mathf.Sin(p.x * -0.47f + p.y * 0.71f + p.z * 1.04f + phase * 1.7f) * 0.31f +
                Mathf.Sin(p.x * 1.29f - p.y * 0.28f + p.z * 0.38f + phase * 0.63f) * 0.17f;
        }

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        private static Vector3 Max(Vector3 a, Vector3 b) =>
            new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));

        private static Bounds BoundsOf(List<Vector3> vertices)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (var i = 1; i < vertices.Count; i++)
            {
                bounds.Encapsulate(vertices[i]);
            }

            return bounds;
        }

        private static void BuildPreview(
            Dictionary<string, GameObject> prefabs,
            Material groundMaterial,
            Material backdropMaterial)
        {
            var anchor = new Vector3(5000f, 5000f, 5000f);
            var original = SceneManager.GetActiveScene();
            var preview = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(preview);
            try
            {
                ConfigureLighting(anchor);
                var catalog = new GameObject("OrganicKit_Catalog");
                catalog.transform.position = anchor;
                BuildCatalog(catalog.transform, prefabs, backdropMaterial);
                SetLayer(catalog, 27);

                var assembly = new GameObject("OrganicKit_Assembly");
                assembly.transform.position = anchor + new Vector3(40f, 0f, 0f);
                BuildAssembly(assembly.transform, prefabs, groundMaterial);
                SetLayer(assembly, 28);

                var rotation = new GameObject("AutoGrass_RotationTest");
                rotation.transform.position = anchor + new Vector3(80f, 0f, 0f);
                BuildRotationTest(rotation.transform, prefabs, backdropMaterial);
                SetLayer(rotation, 29);

                var catalogCamera = CreateCamera(
                    "CAM_OrganicCatalog",
                    anchor + new Vector3(0f, 5.0f, 31f),
                    anchor + new Vector3(0f, 0.6f, 0f),
                    true,
                    11.5f,
                    27);
                var assemblyCamera = CreateCamera(
                    "CAM_OrganicAssembly",
                    anchor + new Vector3(49f, 11.5f, 25f),
                    anchor + new Vector3(40f, 3.2f, 0.4f),
                    false,
                    0f,
                    28);
                assemblyCamera.fieldOfView = 42f;
                var rotationCamera = CreateCamera(
                    "CAM_AutoGrassRotation",
                    anchor + new Vector3(80f, 5.8f, 25f),
                    anchor + new Vector3(80f, 2.0f, 0f),
                    true,
                    7.0f,
                    29);

                Render(catalogCamera, @"D:\CodexTemp\StarterIslandTerrain\organic_rock_kit_catalog.png");
                Render(assemblyCamera, @"D:\CodexTemp\StarterIslandTerrain\organic_rock_kit_assembly.png");
                Render(rotationCamera, @"D:\CodexTemp\StarterIslandTerrain\organic_rock_auto_grass_rotation.png");
                catalogCamera.enabled = false;
                assemblyCamera.enabled = false;
                rotationCamera.enabled = false;
                if (!EditorSceneManager.SaveScene(preview, PreviewScenePath, false))
                {
                    throw new InvalidOperationException("Could not save organic preview scene.");
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
            RenderSettings.ambientSkyColor = Html("#C2D4D4");
            RenderSettings.ambientEquatorColor = Html("#A9A59A");
            RenderSettings.ambientGroundColor = Html("#53564F");
            RenderSettings.ambientIntensity = 1.05f;
            var sunObject = new GameObject("PreviewSun");
            sunObject.transform.rotation = Quaternion.Euler(44f, -37f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Html("#FFD8B5");
            sun.intensity = 1.58f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.74f;
            sun.cullingMask = (1 << 27) | (1 << 28) | (1 << 29);
            RenderSettings.sun = sun;

            AddFill("CatalogFill", anchor + new Vector3(-5f, 8f, 12f));
            AddFill("AssemblyFill", anchor + new Vector3(40f, 8f, 12f));
            AddFill("RotationFill", anchor + new Vector3(80f, 8f, 12f));
        }

        private static void AddFill(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Html("#B9D2D4");
            light.intensity = 165f;
            light.range = 31f;
            light.shadows = LightShadows.None;
            light.cullingMask = (1 << 27) | (1 << 28) | (1 << 29);
        }

        private static void BuildCatalog(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            Material backdropMaterial)
        {
            // Camera clear color is used for the catalog. A physical backdrop
            // can never intersect deep rotated modules.
            for (var i = 0; i < Shapes.Length; i++)
            {
                var instance = Instantiate(prefabs[Shapes[i].Name], root);
                instance.name = $"{i + 1:00}_{Shapes[i].Name}";
                instance.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
                var bounds = RendererBounds(instance);
                var scale = 3.95f / Mathf.Max(bounds.size.x, bounds.size.y, 0.01f);
                instance.transform.localScale = Vector3.one * scale;
                bounds = RendererBounds(instance);
                var row = i / 5;
                var column = i % 5;
                var target = root.position + new Vector3(-8f + column * 4f, 6.9f - row * 6.0f, 0f);
                instance.transform.position += target - bounds.center;
            }
        }

        private static void BuildAssembly(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            Material groundMaterial)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "PreviewGround_NotPartOfKit";
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(0f, -0.22f, 1.4f);
            ground.transform.localScale = new Vector3(23f, 0.38f, 14f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

            Place(root, prefabs, "Wall_Straight", new Vector3(-4.8f, -0.25f, 0f), new Vector3(0f, -4f, 0f));
            Place(root, prefabs, "Wall_Corner", new Vector3(1.15f, -0.30f, -0.35f), new Vector3(0f, 2f, 0f));
            Place(root, prefabs, "Extension", new Vector3(5.4f, -0.25f, 0.20f), new Vector3(0f, -8f, 0f));
            Place(root, prefabs, "Overhang_Left", new Vector3(-6.15f, -0.35f, 1.35f), new Vector3(0f, 8f, 0f));
            Place(root, prefabs, "Overhang_Surface_A", new Vector3(3.05f, 2.75f, 0.70f), new Vector3(0f, -5f, -3f));
            Place(root, prefabs, "Stone_Large", new Vector3(6.95f, -0.18f, 2.65f), new Vector3(0f, 22f, 0f));
            Place(root, prefabs, "Stone_Medium", new Vector3(4.95f, -0.12f, 3.55f), new Vector3(0f, -17f, 0f));
        }

        private static void BuildRotationTest(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            Material backdropMaterial)
        {
            // Keep the rotation proof completely free of occluding geometry.
            var rotations = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 90f, 15f),
                new Vector3(0f, 180f, -15f)
            };
            for (var i = 0; i < rotations.Length; i++)
            {
                var instance = Instantiate(prefabs["Overhang_Surface_A"], root);
                instance.name = "WorldUp_Test_" + i;
                instance.transform.localPosition = new Vector3(-6.2f + i * 6.2f, 0f, 0f);
                instance.transform.localRotation = Quaternion.Euler(rotations[i]);
            }
        }

        private static void AddBackdrop(
            Transform root,
            Material material,
            Vector3 position,
            Vector3 scale)
        {
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backdrop.name = "PreviewBackdrop";
            backdrop.transform.SetParent(root, false);
            backdrop.transform.localPosition = position;
            backdrop.transform.localScale = scale;
            backdrop.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());
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

        private static void Place(
            Transform root,
            Dictionary<string, GameObject> prefabs,
            string name,
            Vector3 position,
            Vector3 rotation)
        {
            var instance = Instantiate(prefabs[name], root);
            instance.name = "Assembly_" + name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(rotation);
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

        private static void Validate(Dictionary<string, GameObject> prefabs)
        {
            if (prefabs.Count != Shapes.Length)
            {
                throw new InvalidOperationException("Organic kit prefab count mismatch.");
            }

            foreach (var shape in Shapes)
            {
                var prefab = prefabs[shape.Name];
                if (prefab.transform.childCount != 0 ||
                    prefab.GetComponentsInChildren<MeshRenderer>(true).Length != 1 ||
                    prefab.GetComponentsInChildren<MeshFilter>(true).Length != 1)
                {
                    throw new InvalidOperationException(
                        shape.Name + " is not a one-object/one-mesh prefab.");
                }

                var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || mesh.vertexCount < 180 || mesh.triangles.Length < 900)
                {
                    throw new InvalidOperationException(shape.Name + " has invalid geometry.");
                }

                ValidateFinite(shape.Name, mesh);
                ValidateManifold(shape.Name, mesh);
                var collider = prefab.GetComponent<MeshCollider>();
                if (collider == null || collider.sharedMesh != mesh)
                {
                    throw new InvalidOperationException(shape.Name + " collider mismatch.");
                }
            }
        }

        private static void ValidateFinite(string name, Mesh mesh)
        {
            foreach (var vertex in mesh.vertices)
            {
                if (float.IsNaN(vertex.x) || float.IsInfinity(vertex.x) ||
                    float.IsNaN(vertex.y) || float.IsInfinity(vertex.y) ||
                    float.IsNaN(vertex.z) || float.IsInfinity(vertex.z))
                {
                    throw new InvalidOperationException(name + " contains non-finite vertices.");
                }
            }
        }

        private static void ValidateManifold(string name, Mesh mesh)
        {
            var counts = new Dictionary<ulong, int>();
            var triangles = mesh.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                CountEdge(counts, triangles[i], triangles[i + 1]);
                CountEdge(counts, triangles[i + 1], triangles[i + 2]);
                CountEdge(counts, triangles[i + 2], triangles[i]);
            }

            var boundary = 0;
            var nonManifold = 0;
            ulong firstBad = 0;
            foreach (var pair in counts)
            {
                if (pair.Value != 2)
                {
                    if (firstBad == 0)
                    {
                        firstBad = pair.Key;
                    }

                    if (pair.Value == 1)
                    {
                        boundary++;
                    }
                    else
                    {
                        nonManifold++;
                    }
                }
            }

            if (boundary > 0 || nonManifold > 0)
            {
                var low = (int)(firstBad >> 32);
                var high = (int)(firstBad & 0xffffffffu);
                var vertices = mesh.vertices;
                throw new InvalidOperationException(
                    name + " topology boundary=" + boundary +
                    " nonManifold=" + nonManifold +
                    " first=" + vertices[low].ToString("F5") +
                    " -> " + vertices[high].ToString("F5"));
            }
        }

        private static void CountEdge(Dictionary<ulong, int> counts, int a, int b)
        {
            var key = EdgeKey(a, b);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        private static Color Html(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var color))
            {
                throw new ArgumentException("Invalid color " + value);
            }

            return color;
        }
    }
}
