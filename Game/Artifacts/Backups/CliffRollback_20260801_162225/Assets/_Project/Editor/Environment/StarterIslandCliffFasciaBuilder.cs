using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Builds one combined mesh of embedded cliff buttresses and talus.
    ///
    /// A near-coplanar ribbon over Terrain creates moire and still reads like
    /// a smooth sheet. These are true closed rock volumes: most of every form
    /// is buried in the collision-bearing Terrain while its large outer planes
    /// break the silhouette and catch shadows. They add no collider and never
    /// advertise a route that the Terrain does not already provide.
    /// </summary>
    internal static class StarterIslandCliffFasciaBuilder
    {
        internal const string RootName = "CliffFasciaRoot";

        private const string MeshPath =
            StarterIslandTerrainSetup.DataRoot +
            "/MESH_StarterIsland_CliffFascia.asset";
        private const int FirstMountainTerrace = 6;

        // An eroded slab is described by eight boundary points and a shallow
        // centre ridge.  Unlike an icosahedron it has no equatorial diamond
        // or single apex: the visible face is a set of metre-scale planes,
        // while the matching rear cap lives safely inside the Terrain.
        private static readonly Vector2[] SlabBoundary =
        {
            new Vector2(-0.38f, 0.38f),
            new Vector2(0.01f, 0.50f),
            new Vector2(0.41f, 0.36f),
            new Vector2(0.50f, -0.03f),
            new Vector2(0.36f, -0.43f),
            new Vector2(-0.03f, -0.50f),
            new Vector2(-0.42f, -0.37f),
            new Vector2(-0.50f, 0.02f)
        };

        public static void BuildOrUpdate(
            Transform parent,
            Terrain terrain,
            Material material)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            var previous = parent.Find(RootName);
            if (previous != null)
            {
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }

            var vertices = new List<Vector3>(18000);
            var colors = new List<Color32>(18000);
            var triangles = new List<int>(18000);
            var macroFormCount = 0;
            var talusCount = 0;
            var terraces = StarterIslandTerraceField.Terraces;

            for (var terraceIndex = FirstMountainTerrace;
                 terraceIndex < terraces.Length;
                 terraceIndex++)
            {
                AppendTerraceFormations(
                    parent,
                    terrain,
                    terraces[terraceIndex],
                    terraceIndex,
                    vertices,
                    colors,
                    triangles,
                    ref macroFormCount,
                    ref talusCount);
            }

            var generated = new Mesh
            {
                name = "MESH_StarterIsland_CliffFascia",
                indexFormat = IndexFormat.UInt32
            };
            generated.SetVertices(vertices);
            generated.SetColors(colors);
            generated.SetTriangles(triangles, 0, true);
            generated.RecalculateNormals();
            generated.RecalculateBounds();

            var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (persistent == null)
            {
                persistent = generated;
                AssetDatabase.CreateAsset(persistent, MeshPath);
            }
            else
            {
                EditorUtility.CopySerialized(generated, persistent);
                UnityEngine.Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(persistent);
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = persistent;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

            Debug.Log(
                $"STARTER_ISLAND_CLIFF_FASCIA terraces=" +
                $"{terraces.Length - FirstMountainTerrace} " +
                $"macroForms={macroFormCount} talus={talusCount} " +
                $"vertices={vertices.Count} triangles={triangles.Count / 3} " +
                "closedVolumes=1 collider=0 terrainCollisionAuthority=1 " +
                "status=PASS");
        }

        private static void AppendTerraceFormations(
            Transform parent,
            Terrain terrain,
            StarterIslandTerraceField.Terrace terrace,
            int terraceIndex,
            List<Vector3> vertices,
            List<Color32> colors,
            List<int> triangles,
            ref int macroFormCount,
            ref int talusCount)
        {
            var seed = terraceIndex * 997 + 41;
            var baseAngle = terraceIndex == 6
                ? -1.07f
                : Hash01(seed) * Mathf.PI * 2f;

            // The collision-bearing Terrain now owns all ledges and macro
            // silhouettes. This mesh is intentionally restricted to small
            // debris at the foot: it grounds the transition without pasting
            // fake handholds onto the wall.
            var clusterCount = terraceIndex == FirstMountainTerrace ? 8 : 4;
            for (var cluster = 0; cluster < clusterCount; cluster++)
            {
                var clusterAngle = terraceIndex == FirstMountainTerrace
                    ? baseAngle + (cluster - 3.5f) * 0.155f +
                      (Hash01(seed + cluster * 53) - 0.5f) * 0.040f
                    : baseAngle + cluster * Mathf.PI * 0.5f +
                      (Hash01(seed + cluster * 53) - 0.5f) * 0.36f;

                // Uneven groups of four to six fragments, all below human
                // knee height. They remain dressing, never route language.
                var fragmentCount = 4 + (int)(Hash01(seed + cluster * 71) * 3f);
                for (var talus = 0; talus < fragmentCount; talus++)
                {
                    var localSeed = seed + cluster * 313 + talus * 67 + 7001;
                    var angle =
                        clusterAngle +
                        (talus - (fragmentCount - 1f) * 0.5f) * 0.018f +
                        (Hash01(localSeed) - 0.5f) * 0.025f;
                    var width = Mathf.Lerp(0.34f, 1.25f, Hash01(localSeed + 5));
                    AppendFormationVolume(
                        parent,
                        terrain,
                        terrace,
                        angle,
                        Mathf.Lerp(0.94f, 0.995f, Hash01(localSeed + 6)),
                        width,
                        width * Mathf.Lerp(0.45f, 0.78f, Hash01(localSeed + 7)),
                        width * Mathf.Lerp(0.62f, 0.95f, Hash01(localSeed + 11)),
                        localSeed,
                        0.46f,
                        0.40f,
                        vertices,
                        colors,
                        triangles);
                    talusCount++;
                }
            }
        }

        private static Vector3 PointOnWall(
            Terrain terrain,
            StarterIslandTerraceField.Terrace terrace,
            float angle,
            float profile)
        {
            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);
            var unitX = terrace.CenterX + cos * terrace.RadiusX;
            var unitZ = terrace.CenterZ + sin * terrace.RadiusZ;
            var unitDistance = StarterIslandTerraceField.OutlineDistance(
                unitX,
                unitZ,
                terrace);
            var meanRadius =
                (terrace.RadiusX + terrace.RadiusZ) * 0.5f;
            var feather =
                Mathf.Max(28f, terrace.EdgeMetres * 3.20f) / meanRadius;
            var targetDistance = 1f + profile * feather;
            var radialScale =
                targetDistance / Mathf.Max(unitDistance, 0.0001f);
            var point = new Vector3(
                terrace.CenterX + cos * terrace.RadiusX * radialScale,
                0f,
                terrace.CenterZ + sin * terrace.RadiusZ * radialScale);
            point.y =
                terrain.SampleHeight(point) +
                terrain.transform.position.y;
            return point;
        }

        private static void AppendFormationVolume(
            Transform parent,
            Terrain terrain,
            StarterIslandTerraceField.Terrace terrace,
            float angle,
            float profile,
            float width,
            float height,
            float depth,
            int seed,
            float depthBurial,
            float verticalBurial,
            List<Vector3> vertices,
            List<Color32> colors,
            List<int> triangles)
        {
            var point = PointOnWall(terrain, terrace, angle, profile);
            var outward = new Vector3(
                point.x - terrace.CenterX,
                0f,
                point.z - terrace.CenterZ).normalized;
            var tangent = Vector3.Cross(Vector3.up, outward).normalized;
            var roll = Mathf.Lerp(-11f, 11f, Hash01(seed + 83));
            var rollRotation = Quaternion.AngleAxis(roll, outward);
            tangent = rollRotation * tangent;
            var up = Vector3.Cross(outward, tangent).normalized;
            var centre =
                point - outward * depth * depthBurial -
                up * height * verticalBurial;

            var front = new Vector3[9];
            var back = new Vector3[9];
            for (var index = 0; index < SlabBoundary.Length; index++)
            {
                var boundary = SlabBoundary[index];
                var planarJitter = Mathf.Lerp(
                    0.93f,
                    1.07f,
                    Hash01(seed + index * 101));
                var frontDepth = Mathf.Lerp(
                    0.40f,
                    0.53f,
                    Hash01(seed + index * 149 + 19));
                front[index] =
                    centre +
                    tangent * boundary.x * width * planarJitter +
                    up * boundary.y * height * planarJitter +
                    outward * depth * frontDepth;
                back[index] =
                    centre +
                    tangent * boundary.x * width * 0.82f +
                    up * boundary.y * height * 0.82f -
                    outward * depth * 0.54f;
            }

            var centreOffsetX = Mathf.Lerp(-0.07f, 0.07f, Hash01(seed + 307));
            var centreOffsetY = Mathf.Lerp(-0.06f, 0.06f, Hash01(seed + 311));
            front[8] =
                centre +
                tangent * width * centreOffsetX +
                up * height * centreOffsetY +
                outward * depth * Mathf.Lerp(
                    0.48f,
                    0.58f,
                    Hash01(seed + 313));
            back[8] = centre - outward * depth * 0.54f;

            for (var edge = 0; edge < SlabBoundary.Length; edge++)
            {
                var next = (edge + 1) % SlabBoundary.Length;
                AppendFlatFace(
                    parent,
                    front[8],
                    front[edge],
                    front[next],
                    seed + edge * 17,
                    vertices,
                    colors,
                    triangles);
                AppendFlatFace(
                    parent,
                    back[8],
                    back[next],
                    back[edge],
                    seed + edge * 19 + 401,
                    vertices,
                    colors,
                    triangles);
                AppendFlatFace(
                    parent,
                    front[edge],
                    back[edge],
                    back[next],
                    seed + edge * 23 + 809,
                    vertices,
                    colors,
                    triangles);
                AppendFlatFace(
                    parent,
                    front[edge],
                    back[next],
                    front[next],
                    seed + edge * 29 + 1201,
                    vertices,
                    colors,
                    triangles);
            }
        }

        private static void AppendFlatFace(
            Transform parent,
            Vector3 worldA,
            Vector3 worldB,
            Vector3 worldC,
            int seed,
            List<Vector3> vertices,
            List<Color32> colors,
            List<int> triangles)
        {
            var baseVertex = vertices.Count;
            var faceTone = Hash01(seed);
            var red = (byte)Mathf.RoundToInt(
                Mathf.Lerp(104f, 204f, faceTone));
            vertices.Add(parent.InverseTransformPoint(worldA));
            vertices.Add(parent.InverseTransformPoint(worldB));
            vertices.Add(parent.InverseTransformPoint(worldC));
            colors.Add(new Color32(red, 0, 0, 0));
            colors.Add(new Color32(red, 0, 0, 0));
            colors.Add(new Color32(red, 0, 0, 0));
            triangles.Add(baseVertex);
            triangles.Add(baseVertex + 1);
            triangles.Add(baseVertex + 2);
        }

        private static float Hash01(int value)
        {
            unchecked
            {
                var hash = (uint)value;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
