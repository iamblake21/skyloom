using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CML.Editor.Wood
{
    /// <summary>
    /// Creates the compact low-poly log used by the inventory render pipeline.
    /// The bark reuses the production tree material, keeping the item visually
    /// tied to the trees that will eventually produce it.
    /// </summary>
    [InitializeOnLoad]
    public static class WoodHarvestAssetSetup
    {
        public const string LogPrefabPath =
            "Assets/_Project/Art/ManualEra/Wood/Prefabs/" +
            "PF_Item_WoodLog.prefab";
        public const string LogModelPath =
            "Assets/_Project/Art/ManualEra/Wood/Models/" +
            "MESH_Item_WoodLog.fbx";

        private const int AssetRevision = 3;
        private const string RevisionSessionKey =
            "CML.Wood.LogAssetRevision";
        private const string Root =
            "Assets/_Project/Art/ManualEra/Wood";
        private const string MeshPath =
            Root + "/Models/Generated/MESH_Item_WoodLog.asset";
        private const string CutMaterialPath =
            Root + "/Materials/M_Item_WoodLog_Cut.mat";
        private const string BarkMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/V4/" +
            "Trees/Materials/M_ENV_Tree_CloudTall_Summer_Bark.mat";

        static WoodHarvestAssetSetup()
        {
            if (SessionState.GetInt(RevisionSessionKey, 0) >= AssetRevision)
            {
                return;
            }

            EditorApplication.delayCall += EnsureCurrentRevision;
        }

        [MenuItem("CML/Wood/Rebuild Wood Log Item")]
        public static void ForceRebuildLogItemAssets()
        {
            EnsureLogItemAssets(forceRebuild: true);
        }

        public static bool EnsureLogItemAssets()
        {
            return EnsureLogItemAssets(forceRebuild: false);
        }

        private static void EnsureCurrentRevision()
        {
            try
            {
                EnsureLogItemAssets(forceRebuild: false);
                SessionState.SetInt(RevisionSessionKey, AssetRevision);
            }
            catch (Exception exception)
            {
                SessionState.EraseInt(RevisionSessionKey);
                Debug.LogException(exception);
            }
        }

        private static bool EnsureLogItemAssets(bool forceRebuild)
        {
            EnsureFolder("Assets/_Project/Art/ManualEra/Wood");
            EnsureFolder(Root + "/Models");
            EnsureFolder(Root + "/Models/Generated");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Prefabs");

            var importedMesh = AssetDatabase
                .LoadAllAssetsAtPath(LogModelPath)
                .OfType<Mesh>()
                .OrderByDescending(candidate => candidate.vertexCount)
                .FirstOrDefault();
            var mesh = importedMesh
                ?? AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            var meshNeedsRebuild =
                importedMesh == null &&
                (forceRebuild ||
                 mesh == null ||
                 mesh.vertexCount < 180 ||
                 mesh.subMeshCount != 2);
            if (meshNeedsRebuild)
            {
                var rebuiltMesh = BuildLogMesh();
                if (mesh == null)
                {
                    mesh = rebuiltMesh;
                    AssetDatabase.CreateAsset(mesh, MeshPath);
                }
                else
                {
                    EditorUtility.CopySerialized(rebuiltMesh, mesh);
                    UnityEngine.Object.DestroyImmediate(rebuiltMesh);
                    EditorUtility.SetDirty(mesh);
                }
            }

            var cutMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    CutMaterialPath);
            if (cutMaterial == null)
            {
                var shader =
                    Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
                cutMaterial = new Material(shader)
                {
                    name = "M_Item_WoodLog_Cut"
                };
                SetMaterialColor(
                    cutMaterial,
                    new Color(0.68f, 0.43f, 0.20f, 1f));
                if (cutMaterial.HasProperty("_Smoothness"))
                {
                    cutMaterial.SetFloat("_Smoothness", 0.18f);
                }

                AssetDatabase.CreateAsset(
                    cutMaterial,
                    CutMaterialPath);
            }

            var barkMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    BarkMaterialPath);
            if (barkMaterial == null)
            {
                Debug.LogError(
                    $"Missing production bark material: " +
                    $"{BarkMaterialPath}");
                return false;
            }

            var prefabExists =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    LogPrefabPath) != null;
            var root = prefabExists
                ? PrefabUtility.LoadPrefabContents(LogPrefabPath)
                : new GameObject("PF_Item_WoodLog");
            try
            {
                root.name = "PF_Item_WoodLog";
                var filter = root.GetComponent<MeshFilter>()
                    ?? root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = root.GetComponent<MeshRenderer>()
                    ?? root.AddComponent<MeshRenderer>();
                renderer.sharedMaterials =
                    new[] { barkMaterial, cutMaterial };

                var collider = root.GetComponent<MeshCollider>()
                    ?? root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    LogPrefabPath);
            }
            finally
            {
                if (prefabExists)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                MeshPath,
                ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                LogPrefabPath,
                ImportAssetOptions.ForceUpdate);
            return meshNeedsRebuild ||
                   !prefabExists ||
                   PrefabRequiresRepair(mesh, barkMaterial, cutMaterial);
        }

        private static bool PrefabRequiresRepair(
            Mesh expectedMesh,
            Material expectedBark,
            Material expectedCut)
        {
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(LogPrefabPath);
            if (prefab == null)
            {
                return true;
            }

            var filter = prefab.GetComponent<MeshFilter>();
            var renderer = prefab.GetComponent<MeshRenderer>();
            var collider = prefab.GetComponent<MeshCollider>();
            if (filter == null ||
                filter.sharedMesh != expectedMesh ||
                renderer == null ||
                collider == null ||
                collider.sharedMesh != expectedMesh ||
                !collider.convex)
            {
                return true;
            }

            var materials = renderer.sharedMaterials;
            return materials.Length != 2 ||
                   materials[0] != expectedBark ||
                   materials[1] != expectedCut;
        }

        private static Mesh BuildLogMesh()
        {
            const int sides = 12;
            const float halfLength = 0.58f;
            const float radius = 0.205f;
            var ringPositions = new[]
            {
                -halfLength,
                -0.31f,
                -0.05f,
                0.26f,
                halfLength
            };
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var barkTriangles = new List<int>();
            var cutTriangles = new List<int>();

            for (var ring = 0;
                 ring < ringPositions.Length - 1;
                 ring++)
            {
                for (var side = 0; side < sides; side++)
                {
                    var next = (side + 1) % sides;
                    var angleA = side * Mathf.PI * 2f / sides;
                    var angleB = next * Mathf.PI * 2f / sides;
                    var radialA = new Vector3(
                        0f,
                        Mathf.Cos(angleA),
                        Mathf.Sin(angleA));
                    var radialB = new Vector3(
                        0f,
                        Mathf.Cos(angleB),
                        Mathf.Sin(angleB));
                    var normal = (radialA + radialB).normalized;
                    var first = vertices.Count;
                    vertices.Add(LogRingPoint(
                        ringPositions[ring],
                        ring,
                        side,
                        sides,
                        radius));
                    vertices.Add(LogRingPoint(
                        ringPositions[ring + 1],
                        ring + 1,
                        side,
                        sides,
                        radius));
                    vertices.Add(LogRingPoint(
                        ringPositions[ring + 1],
                        ring + 1,
                        next,
                        sides,
                        radius));
                    vertices.Add(LogRingPoint(
                        ringPositions[ring],
                        ring,
                        next,
                        sides,
                        radius));
                    for (var index = 0; index < 4; index++)
                    {
                        normals.Add(normal);
                    }

                    var u0 = side / (float)sides;
                    var u1 = (side + 1f) / sides;
                    var v0 = ring / (float)(ringPositions.Length - 1);
                    var v1 =
                        (ring + 1f) / (ringPositions.Length - 1);
                    uvs.Add(new Vector2(u0, v0));
                    uvs.Add(new Vector2(u0, v1));
                    uvs.Add(new Vector2(u1, v1));
                    uvs.Add(new Vector2(u1, v0));
                    barkTriangles.Add(first);
                    barkTriangles.Add(first + 1);
                    barkTriangles.Add(first + 2);
                    barkTriangles.Add(first);
                    barkTriangles.Add(first + 2);
                    barkTriangles.Add(first + 3);
                }
            }

            AddCap(
                -halfLength,
                -Vector3.right,
                sides,
                radius,
                vertices,
                normals,
                uvs,
                cutTriangles,
                reverse: true);
            AddCap(
                halfLength,
                Vector3.right,
                sides,
                radius,
                vertices,
                normals,
                uvs,
                cutTriangles,
                reverse: false);
            var mesh = new Mesh
            {
                name = "MESH_Item_WoodLog_v2"
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(barkTriangles, 0);
            mesh.SetTriangles(cutTriangles, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 LogRingPoint(
            float x,
            int ring,
            int side,
            int sides,
            float radius)
        {
            var angle = side * Mathf.PI * 2f / sides;
            var sideVariation =
                Mathf.Sin(side * 1.73f + ring * 0.61f) * 0.018f;
            var ringVariation =
                Mathf.Sin(ring * 2.19f + side * 0.37f) * 0.012f;
            var localRadius = radius + sideVariation + ringVariation;
            var centerY = Mathf.Sin(ring * 1.31f) * 0.012f;
            var centerZ = Mathf.Cos(ring * 1.57f) * 0.01f;
            return new Vector3(
                x,
                centerY + Mathf.Cos(angle) * localRadius,
                centerZ + Mathf.Sin(angle) * localRadius);
        }

        private static void AddBranchNub(
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Vector2> uvs,
            ICollection<int> barkTriangles,
            ICollection<int> cutTriangles)
        {
            const int sides = 8;
            var start = new Vector3(0.16f, 0.235f, -0.055f);
            var end = new Vector3(0.24f, 0.41f, -0.025f);
            var axis = (end - start).normalized;
            var tangent = Vector3.Cross(axis, Vector3.forward).normalized;
            var bitangent = Vector3.Cross(axis, tangent).normalized;

            for (var side = 0; side < sides; side++)
            {
                var next = (side + 1) % sides;
                var angleA = side * Mathf.PI * 2f / sides;
                var angleB = next * Mathf.PI * 2f / sides;
                var radialA =
                    tangent * Mathf.Cos(angleA) +
                    bitangent * Mathf.Sin(angleA);
                var radialB =
                    tangent * Mathf.Cos(angleB) +
                    bitangent * Mathf.Sin(angleB);
                var first = vertices.Count;
                vertices.Add(start + radialA * 0.09f);
                vertices.Add(end + radialA * 0.062f);
                vertices.Add(end + radialB * 0.062f);
                vertices.Add(start + radialB * 0.09f);
                var normal = (radialA + radialB).normalized;
                for (var index = 0; index < 4; index++)
                {
                    normals.Add(normal);
                }

                var u0 = side / (float)sides;
                var u1 = (side + 1f) / sides;
                uvs.Add(new Vector2(u0, 0f));
                uvs.Add(new Vector2(u0, 1f));
                uvs.Add(new Vector2(u1, 1f));
                uvs.Add(new Vector2(u1, 0f));
                barkTriangles.Add(first);
                barkTriangles.Add(first + 1);
                barkTriangles.Add(first + 2);
                barkTriangles.Add(first);
                barkTriangles.Add(first + 2);
                barkTriangles.Add(first + 3);
            }

            var capCenter = vertices.Count;
            vertices.Add(end);
            normals.Add(axis);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (var side = 0; side < sides; side++)
            {
                var angle = side * Mathf.PI * 2f / sides;
                var radial =
                    tangent * Mathf.Cos(angle) +
                    bitangent * Mathf.Sin(angle);
                vertices.Add(end + radial * 0.062f);
                normals.Add(axis);
                uvs.Add(new Vector2(
                    0.5f + Mathf.Cos(angle) * 0.5f,
                    0.5f + Mathf.Sin(angle) * 0.5f));
            }

            for (var side = 0; side < sides; side++)
            {
                cutTriangles.Add(capCenter);
                cutTriangles.Add(capCenter + 1 + side);
                cutTriangles.Add(
                    capCenter + 1 + (side + 1) % sides);
            }
        }

        private static void AddCap(
            float x,
            Vector3 normal,
            int sides,
            float radius,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Vector2> uvs,
            ICollection<int> triangles,
            bool reverse)
        {
            var baseIndex = vertices.Count;
            vertices.Add(new Vector3(x, 0f, 0f));
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (var side = 0; side < sides; side++)
            {
                var angle = side * Mathf.PI * 2f / sides;
                var y = Mathf.Cos(angle) * radius;
                var z = Mathf.Sin(angle) * radius;
                vertices.Add(new Vector3(x, y, z));
                normals.Add(normal);
                uvs.Add(new Vector2(
                    0.5f + y / (radius * 2f),
                    0.5f + z / (radius * 2f)));
            }

            for (var side = 0; side < sides; side++)
            {
                var current = baseIndex + 1 + side;
                var next = baseIndex + 1 + (side + 1) % sides;
                triangles.Add(baseIndex);
                triangles.Add(reverse ? next : current);
                triangles.Add(reverse ? current : next);
            }
        }

        private static void SetMaterialColor(
            Material material,
            Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var separator = path.LastIndexOf('/');
            var parent = path.Substring(0, separator);
            var name = path.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    internal sealed class WoodLogModelPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            for (var index = 0; index < importedAssets.Length; index++)
            {
                if (!string.Equals(
                        importedAssets[index],
                        WoodHarvestAssetSetup.LogModelPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                EditorApplication.delayCall +=
                    WoodHarvestAssetSetup.ForceRebuildLogItemAssets;
                return;
            }
        }
    }
}
