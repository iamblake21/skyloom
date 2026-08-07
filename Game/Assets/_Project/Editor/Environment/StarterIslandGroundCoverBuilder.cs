using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Builds Starter Island grass and flowers as deterministic, culled mesh
    /// chunks. Unlike Terrain detail prototypes, this baker preserves every
    /// transform between an authored FBX mesh and its prefab root. This is
    /// important for foliage authored in Blender because the FBX axis
    /// conversion often lives on an intermediate transform.
    /// </summary>
    public static class StarterIslandGroundCoverBuilder
    {
        private const string FoliageRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Foliage";
        private const string TerrainRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain";
        private const string MeshAssetRoot =
            TerrainRoot + "/Data/GroundCoverChunked";
        private const string MaterialPath =
            TerrainRoot + "/Materials/M_StarterIsland_GroundCover.mat";
        private const string AtlasMaterialPath =
            FoliageRoot + "/Materials/M_StarterIsland_FoliageAtlas.mat";
        private const string ShaderName =
            "CML/Environment/Starter Island Ground Cover";
        private const string GeneratedRootName = "GroundCover_Chunked";

        private const float ChunkSize = 64f;
        private const float CellSize = 3.15f;
        private const float TerrainLift = 0.035f;
        private const float MaximumSlope = 27f;
        private const int ScatterSeed = 0x51A17E;

        private static readonly SourceDefinition[] SourceDefinitions =
        {
            new SourceDefinition(
                "Grass_A",
                FoliageRoot + "/Prefabs/PF_Grass_Clump_A.prefab",
                0.38f,
                0.62f,
                0.00f),
            new SourceDefinition(
                "Grass_B",
                FoliageRoot + "/Prefabs/PF_Grass_Clump_B.prefab",
                0.34f,
                0.55f,
                0.00f),
            new SourceDefinition(
                "Flower_White",
                FoliageRoot + "/Prefabs/PF_Flower_White_A.prefab",
                0.28f,
                0.42f,
                1.00f),
            new SourceDefinition(
                "Flower_Orange",
                FoliageRoot + "/Prefabs/PF_Flower_Orange_B.prefab",
                0.28f,
                0.42f,
                1.00f)
        };

        /// <summary>
        /// Rebuilds all ground cover below <paramref name="parent"/>.
        /// Terrain details are deliberately not modified here; the caller can
        /// clear legacy detail prototypes before or after this method.
        /// </summary>
        public static void Build(Transform parent, Terrain terrain)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (terrain == null || terrain.terrainData == null)
            {
                throw new ArgumentException(
                    "Ground cover requires a valid Unity Terrain.",
                    nameof(terrain));
            }

            DestroyPreviousRoot(parent);
            RecreateMeshAssetFolder();

            var material = BuildMaterial();
            var sources = new SourceGeometry[SourceDefinitions.Length];
            for (var index = 0; index < sources.Length; index++)
            {
                sources[index] = ReadSource(SourceDefinitions[index]);
            }

            var rootObject = new GameObject(GeneratedRootName);
            rootObject.transform.SetParent(parent, false);
            var chunks = Scatter(terrain, parent, sources);
            var orderedKeys = new List<Vector2Int>(chunks.Keys);
            orderedKeys.Sort(
                (left, right) =>
                {
                    var zComparison = left.y.CompareTo(right.y);
                    return zComparison != 0
                        ? zComparison
                        : left.x.CompareTo(right.x);
                });

            var instanceCount = 0;
            var chunkCount = 0;
            foreach (var key in orderedKeys)
            {
                var chunk = chunks[key];
                if (chunk.Indices.Count == 0)
                {
                    continue;
                }

                BuildChunkObject(
                    rootObject.transform,
                    key,
                    chunk,
                    material);
                instanceCount += chunk.InstanceCount;
                chunkCount++;
            }

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(rootObject);
            Debug.Log(
                $"STARTER_ISLAND_GROUND_COVER chunks={chunkCount} " +
                $"instances={instanceCount} status=PASS");
        }

        private static Dictionary<Vector2Int, ChunkData> Scatter(
            Terrain terrain,
            Transform outputParent,
            IReadOnlyList<SourceGeometry> sources)
        {
            var result = new Dictionary<Vector2Int, ChunkData>();
            var data = terrain.terrainData;
            var terrainOrigin = terrain.transform.position;
            var size = data.size;
            var columns = Mathf.CeilToInt(size.x / CellSize);
            var rows = Mathf.CeilToInt(size.z / CellSize);
            var alphamaps = data.GetAlphamaps(
                0,
                0,
                data.alphamapWidth,
                data.alphamapHeight);
            var layerCount = alphamaps.GetLength(2);

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var cellHash = Hash(column, row, ScatterSeed);
                    var jitterX =
                        (Hash01(cellHash ^ 0x4A39B70D) - 0.5f) *
                        CellSize *
                        0.78f;
                    var jitterZ =
                        (Hash01(cellHash ^ 0x12FAD5C9) - 0.5f) *
                        CellSize *
                        0.78f;
                    var localX = Mathf.Clamp(
                        (column + 0.5f) * CellSize + jitterX,
                        0.10f,
                        size.x - 0.10f);
                    var localZ = Mathf.Clamp(
                        (row + 0.5f) * CellSize + jitterZ,
                        0.10f,
                        size.z - 0.10f);
                    var normalizedX = localX / size.x;
                    var normalizedZ = localZ / size.z;
                    var slope = data.GetSteepness(
                        normalizedX,
                        normalizedZ);
                    if (slope > MaximumSlope)
                    {
                        continue;
                    }

                    SampleSurfaceWeights(
                        alphamaps,
                        normalizedX,
                        normalizedZ,
                        layerCount,
                        out var grassWeight,
                        out var pathWeight,
                        out var cliffWeight);
                    var pathBorder =
                        Mathf.SmoothStep(
                            0f,
                            1f,
                            Mathf.InverseLerp(
                                0.08f,
                                0.42f,
                                pathWeight)) *
                        (1f -
                         Mathf.SmoothStep(
                             0f,
                             1f,
                             Mathf.InverseLerp(
                                 0.56f,
                                 0.78f,
                                 pathWeight)));
                    if (grassWeight < 0.32f ||
                        pathWeight > 0.52f ||
                        cliffWeight > 0.20f)
                    {
                        continue;
                    }

                    var worldX = terrainOrigin.x + localX;
                    var worldZ = terrainOrigin.z + localZ;
                    if (!StarterIslandTerrainSetup
                            .IsGroundCoverClearOfWater(
                                new Vector2(worldX, worldZ)))
                    {
                        continue;
                    }

                    var terrainY =
                        terrain.SampleHeight(
                            new Vector3(worldX, 0f, worldZ)) +
                        terrainOrigin.y;
                    if (terrainY <= terrainOrigin.y + 4f)
                    {
                        continue;
                    }

                    var patchNoise = Mathf.PerlinNoise(
                        (worldX + 431f) * 0.023f,
                        (worldZ + 317f) * 0.023f);
                    var fineNoise = Mathf.PerlinNoise(
                        (worldX + 73f) * 0.079f,
                        (worldZ + 97f) * 0.079f);
                    var density =
                        Mathf.Lerp(0.22f, 0.92f, patchNoise) *
                        Mathf.Lerp(0.62f, 1.0f, fineNoise) *
                        Mathf.InverseLerp(
                            MaximumSlope,
                            2f,
                            slope);
                    density = Mathf.Lerp(
                        density,
                        Mathf.Max(density, 0.78f),
                        pathBorder);
                    if (Hash01(cellHash ^ 0x0B5297A4) > density)
                    {
                        continue;
                    }

                    var normal = data.GetInterpolatedNormal(
                        normalizedX,
                        normalizedZ);
                    normal = terrain.transform.TransformDirection(normal)
                        .normalized;
                    var yaw = Hash01(cellHash ^ 0x68E31DA4) * 360f;
                    var groundAlignment = Quaternion.Slerp(
                        Quaternion.identity,
                        Quaternion.FromToRotation(Vector3.up, normal),
                        0.16f);
                    var rotation =
                        groundAlignment *
                        Quaternion.Euler(0f, yaw, 0f);

                    var flowerRoll = Hash01(cellHash ^ 0x1B56C4E9);
                    var sourceIndex = SelectSource(
                        flowerRoll,
                        patchNoise,
                        normalizedX,
                        normalizedZ,
                        cellHash);
                    var source = sources[sourceIndex];
                    var definition = SourceDefinitions[sourceIndex];
                    var targetHeight = Mathf.Lerp(
                        definition.MinimumHeight,
                        definition.MaximumHeight,
                        Hash01(cellHash ^ 0x7F4A7C15));
                    targetHeight *= Mathf.Lerp(
                        1f,
                        0.58f,
                        pathBorder);
                    var uniformScale =
                        targetHeight /
                        Mathf.Max(0.001f, source.Height);
                    var position = new Vector3(
                        worldX,
                        terrainY + TerrainLift,
                        worldZ);
                    var chunkKey = new Vector2Int(
                        Mathf.FloorToInt(localX / ChunkSize),
                        Mathf.FloorToInt(localZ / ChunkSize));
                    if (!result.TryGetValue(chunkKey, out var chunk))
                    {
                        chunk = new ChunkData();
                        result.Add(chunkKey, chunk);
                    }

                    var phase =
                        Hash01(cellHash ^ 0x2C9277B5);
                    var variation =
                        Hash01(cellHash ^ 0x5D588B65);
                    AppendInstance(
                        chunk,
                        source,
                        outputParent.worldToLocalMatrix *
                        Matrix4x4.TRS(
                            position,
                            rotation,
                            Vector3.one * uniformScale),
                        phase,
                        variation,
                        definition.FlowerFactor);
                }
            }

            return result;
        }

        private static int SelectSource(
            float flowerRoll,
            float patchNoise,
            float normalizedX,
            float normalizedZ,
            uint hash)
        {
            var flowerChance =
                Mathf.Lerp(0.012f, 0.048f, patchNoise);
            if (flowerRoll < flowerChance)
            {
                var autumnBias =
                    Mathf.Clamp01((normalizedX - 0.48f) * 2.2f) *
                    Mathf.Clamp01((normalizedZ - 0.30f) * 1.8f);
                return Hash01(hash ^ 0x41C64E6D) <
                       Mathf.Lerp(0.28f, 0.78f, autumnBias)
                    ? 3
                    : 2;
            }

            return Hash01(hash ^ 0x3039) < 0.58f ? 0 : 1;
        }

        private static void SampleSurfaceWeights(
            float[,,] alphamaps,
            float normalizedX,
            float normalizedZ,
            int layerCount,
            out float grassWeight,
            out float pathWeight,
            out float cliffWeight)
        {
            var width = alphamaps.GetLength(1);
            var height = alphamaps.GetLength(0);
            var x = Mathf.Clamp(
                Mathf.RoundToInt(normalizedX * (width - 1)),
                0,
                width - 1);
            var z = Mathf.Clamp(
                Mathf.RoundToInt(normalizedZ * (height - 1)),
                0,
                height - 1);
            grassWeight =
                (layerCount > 0 ? alphamaps[z, x, 0] : 1f) +
                (layerCount > 1 ? alphamaps[z, x, 1] : 0f);
            pathWeight =
                layerCount > 2 ? alphamaps[z, x, 2] : 0f;
            cliffWeight =
                layerCount > 3 ? alphamaps[z, x, 3] : 0f;
        }

        private static void AppendInstance(
            ChunkData chunk,
            SourceGeometry source,
            Matrix4x4 sourceToParent,
            float phase,
            float variation,
            float flowerFactor)
        {
            var baseVertex = chunk.Vertices.Count;
            var normalMatrix = sourceToParent.inverse.transpose;
            for (var index = 0; index < source.Vertices.Length; index++)
            {
                var sourceVertex = source.Vertices[index];
                sourceVertex.y -= source.MinimumY;
                chunk.Vertices.Add(
                    sourceToParent.MultiplyPoint3x4(sourceVertex));
                chunk.Normals.Add(
                    normalMatrix.MultiplyVector(source.Normals[index])
                        .normalized);
                chunk.Uvs.Add(source.Uvs[index]);
                var heightWeight = Mathf.Clamp01(
                    (source.Vertices[index].y - source.MinimumY) /
                    Mathf.Max(0.001f, source.Height));
                chunk.Colors.Add(
                    new Color(
                        heightWeight,
                        phase,
                        variation,
                        flowerFactor));
            }

            var flipWinding = sourceToParent.determinant < 0f;
            for (var index = 0; index < source.Indices.Length; index += 3)
            {
                var a = source.Indices[index] + baseVertex;
                var b = source.Indices[index + 1] + baseVertex;
                var c = source.Indices[index + 2] + baseVertex;
                chunk.Indices.Add(a);
                chunk.Indices.Add(flipWinding ? c : b);
                chunk.Indices.Add(flipWinding ? b : c);
            }

            chunk.InstanceCount++;
        }

        private static SourceGeometry ReadSource(
            SourceDefinition definition)
        {
            var source =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    definition.PrefabPath);
            if (source == null)
            {
                throw new FileNotFoundException(
                    $"Ground-cover prefab is missing: " +
                    $"{definition.PrefabPath}");
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            var sourceRootInverse = source.transform.worldToLocalMatrix;
            foreach (var filter in
                     source.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null ||
                    string.Equals(
                        mesh.name,
                        "Cube",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sourceToRoot =
                    sourceRootInverse *
                    filter.transform.localToWorldMatrix;
                AppendSourceMesh(
                    mesh,
                    sourceToRoot,
                    vertices,
                    normals,
                    uvs,
                    indices);
            }

            if (vertices.Count == 0 || indices.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No renderable triangle mesh was found below " +
                    $"{definition.PrefabPath}.");
            }

            var minimumY = float.PositiveInfinity;
            var maximumY = float.NegativeInfinity;
            for (var index = 0; index < vertices.Count; index++)
            {
                minimumY = Mathf.Min(minimumY, vertices[index].y);
                maximumY = Mathf.Max(maximumY, vertices[index].y);
            }

            if (maximumY - minimumY < 0.05f)
            {
                throw new InvalidOperationException(
                    $"{definition.PrefabPath} remains flat after prefab " +
                    "transform baking. Check the authored prefab hierarchy.");
            }

            return new SourceGeometry(
                vertices.ToArray(),
                normals.ToArray(),
                uvs.ToArray(),
                indices.ToArray(),
                minimumY,
                maximumY);
        }

        private static void AppendSourceMesh(
            Mesh mesh,
            Matrix4x4 sourceToRoot,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> indices)
        {
            using (var meshDataArray =
                   Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var meshData = meshDataArray[0];
                var vertexCount = meshData.vertexCount;
                var positions =
                    new NativeArray<Vector3>(
                        vertexCount,
                        Allocator.Temp);
                var sourceNormals =
                    new NativeArray<Vector3>(
                        vertexCount,
                        Allocator.Temp);
                var sourceUvs =
                    new NativeArray<Vector2>(
                        vertexCount,
                        Allocator.Temp);
                try
                {
                    meshData.GetVertices(positions);
                    var hasNormals = meshData.HasVertexAttribute(
                        VertexAttribute.Normal);
                    if (hasNormals)
                    {
                        meshData.GetNormals(sourceNormals);
                    }

                    var hasUvs = meshData.HasVertexAttribute(
                        VertexAttribute.TexCoord0);
                    if (hasUvs)
                    {
                        meshData.GetUVs(0, sourceUvs);
                    }

                    var normalMatrix = sourceToRoot.inverse.transpose;
                    var baseVertex = vertices.Count;
                    for (var vertexIndex = 0;
                         vertexIndex < vertexCount;
                         vertexIndex++)
                    {
                        vertices.Add(
                            sourceToRoot.MultiplyPoint3x4(
                                positions[vertexIndex]));
                        normals.Add(
                            hasNormals
                                ? normalMatrix.MultiplyVector(
                                        sourceNormals[vertexIndex])
                                    .normalized
                                : Vector3.up);
                        uvs.Add(
                            hasUvs
                                ? sourceUvs[vertexIndex]
                                : Vector2.zero);
                    }

                    var flipWinding = sourceToRoot.determinant < 0f;
                    for (var subMeshIndex = 0;
                         subMeshIndex < meshData.subMeshCount;
                         subMeshIndex++)
                    {
                        var subMesh =
                            meshData.GetSubMesh(subMeshIndex);
                        if (subMesh.topology !=
                            MeshTopology.Triangles)
                        {
                            continue;
                        }

                        var sourceIndices =
                            new NativeArray<int>(
                                subMesh.indexCount,
                                Allocator.Temp);
                        try
                        {
                            meshData.GetIndices(
                                sourceIndices,
                                subMeshIndex,
                                true);
                            for (var index = 0;
                                 index < sourceIndices.Length;
                                 index += 3)
                            {
                                var a =
                                    sourceIndices[index] +
                                    baseVertex;
                                var b =
                                    sourceIndices[index + 1] +
                                    baseVertex;
                                var c =
                                    sourceIndices[index + 2] +
                                    baseVertex;
                                indices.Add(a);
                                indices.Add(
                                    flipWinding ? c : b);
                                indices.Add(
                                    flipWinding ? b : c);
                            }
                        }
                        finally
                        {
                            sourceIndices.Dispose();
                        }
                    }
                }
                finally
                {
                    positions.Dispose();
                    sourceNormals.Dispose();
                    sourceUvs.Dispose();
                }
            }
        }

        private static Material BuildMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new FileNotFoundException(
                    $"Shader '{ShaderName}' has not been imported.");
            }

            EnsureFolder(Path.GetDirectoryName(MaterialPath)
                ?.Replace('\\', '/'));
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_GroundCover"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            var atlas =
                AssetDatabase.LoadAssetAtPath<Material>(
                    AtlasMaterialPath);
            if (atlas != null)
            {
                CopyTexture(atlas, material, "_BaseMap");
                CopyTexture(atlas, material, "_MaskMap");
            }

            SetColor(material, "_BaseColor", "#E7E9D8");
            SetColor(material, "_RootTint", "#4F7139");
            SetColor(material, "_TipTint", "#789446");
            SetFloat(material, "_WindStrength", 0.22f);
            SetFloat(material, "_WindSpeed", 1.15f);
            SetFloat(material, "_GustScale", 0.020f);
            SetFloat(material, "_AmbientStrength", 0.64f);
            SetFloat(material, "_ShadowFloor", 0.18f);
            material.enableInstancing = true;
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildChunkObject(
            Transform root,
            Vector2Int key,
            ChunkData chunk,
            Material material)
        {
            var mesh = new Mesh
            {
                name =
                    $"MD_GroundCover_{key.x:D2}_{key.y:D2}",
                indexFormat =
                    chunk.Vertices.Count > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
            };
            mesh.SetVertices(chunk.Vertices);
            mesh.SetNormals(chunk.Normals);
            mesh.SetUVs(0, chunk.Uvs);
            mesh.SetColors(chunk.Colors);
            mesh.SetTriangles(chunk.Indices, 0, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            var assetPath =
                $"{MeshAssetRoot}/{mesh.name}.asset";
            AssetDatabase.CreateAsset(mesh, assetPath);

            var chunkObject = new GameObject(
                $"GroundCover_{key.x:D2}_{key.y:D2}");
            chunkObject.transform.SetParent(root, false);
            chunkObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = chunkObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.BlendProbes;
            renderer.allowOcclusionWhenDynamic = true;
        }

        private static void DestroyPreviousRoot(Transform parent)
        {
            var previous = parent.Find(GeneratedRootName);
            if (previous != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    previous.gameObject);
            }
        }

        private static void RecreateMeshAssetFolder()
        {
            if (AssetDatabase.IsValidFolder(MeshAssetRoot))
            {
                AssetDatabase.DeleteAsset(MeshAssetRoot);
            }

            EnsureFolder(MeshAssetRoot);
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)
                ?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"Cannot create asset folder '{path}'.");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(
                parent,
                Path.GetFileName(path));
        }

        private static void CopyTexture(
            Material source,
            Material destination,
            string propertyName)
        {
            if (!source.HasProperty(propertyName) ||
                !destination.HasProperty(propertyName))
            {
                return;
            }

            destination.SetTexture(
                propertyName,
                source.GetTexture(propertyName));
        }

        private static void SetFloat(
            Material material,
            string propertyName,
            float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetColor(
            Material material,
            string propertyName,
            string html)
        {
            if (material.HasProperty(propertyName) &&
                ColorUtility.TryParseHtmlString(html, out var color))
            {
                material.SetColor(propertyName, color);
            }
        }

        private static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                var value =
                    (uint)x * 0x8DA6B343u ^
                    (uint)z * 0xD8163841u ^
                    (uint)seed;
                value ^= value >> 13;
                value *= 0x85EBCA6Bu;
                value ^= value >> 16;
                return value;
            }
        }

        private static float Hash01(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777215f;
            }
        }

        private sealed class ChunkData
        {
            public readonly List<Vector3> Vertices =
                new List<Vector3>();
            public readonly List<Vector3> Normals =
                new List<Vector3>();
            public readonly List<Vector2> Uvs =
                new List<Vector2>();
            public readonly List<Color> Colors =
                new List<Color>();
            public readonly List<int> Indices =
                new List<int>();
            public int InstanceCount;
        }

        private sealed class SourceGeometry
        {
            public SourceGeometry(
                Vector3[] vertices,
                Vector3[] normals,
                Vector2[] uvs,
                int[] indices,
                float minimumY,
                float maximumY)
            {
                Vertices = vertices;
                Normals = normals;
                Uvs = uvs;
                Indices = indices;
                MinimumY = minimumY;
                Height = maximumY - minimumY;
            }

            public Vector3[] Vertices { get; }
            public Vector3[] Normals { get; }
            public Vector2[] Uvs { get; }
            public int[] Indices { get; }
            public float MinimumY { get; }
            public float Height { get; }
        }

        private readonly struct SourceDefinition
        {
            public SourceDefinition(
                string name,
                string prefabPath,
                float minimumHeight,
                float maximumHeight,
                float flowerFactor)
            {
                Name = name;
                PrefabPath = prefabPath;
                MinimumHeight = minimumHeight;
                MaximumHeight = maximumHeight;
                FlowerFactor = flowerFactor;
            }

            public string Name { get; }
            public string PrefabPath { get; }
            public float MinimumHeight { get; }
            public float MaximumHeight { get; }
            public float FlowerFactor { get; }
        }
    }
}
