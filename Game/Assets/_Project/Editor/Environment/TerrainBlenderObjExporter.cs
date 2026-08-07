using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Exports a Unity Terrain as actual polygonal geometry for Blender.
    /// Unity's FBX Exporter does not tessellate Terrain components, so exporting
    /// TerrainTop directly creates an FBX container with no useful mesh.
    /// </summary>
    [InitializeOnLoad]
    public static class TerrainBlenderObjExporter
    {
        private const int BlenderResolution = 513;
        private const string OneShotMarker = "Temp/CML_ExportTerrainTopToBlender.once";

        static TerrainBlenderObjExporter()
        {
            EditorApplication.delayCall += RunOneShotExportIfRequested;
        }

        [MenuItem("CML/Environment/Blender/Export Selected Terrain to OBJ (Blender, 513)")]
        private static void ExportSelectedTerrainForBlender()
        {
            Terrain terrain = GetSelectedTerrain();
            if (terrain == null)
            {
                EditorUtility.DisplayDialog(
                    "Export Terrain to Blender",
                    "Select a GameObject containing a Unity Terrain component first.",
                    "OK");
                return;
            }

            string defaultName = SanitizeFileName(terrain.name) + "_Blender.obj";
            string path = EditorUtility.SaveFilePanel(
                "Export Terrain mesh for Blender",
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                defaultName,
                "obj");

            if (string.IsNullOrWhiteSpace(path))
                return;

            Export(terrain, path, BlenderResolution);
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("CML/Environment/Blender/Export Selected Terrain to OBJ (Blender, 513)", true)]
        private static bool ValidateExportSelectedTerrainForBlender()
        {
            return GetSelectedTerrain() != null;
        }

        private static void RunOneShotExportIfRequested()
        {
            string markerPath = Path.GetFullPath(OneShotMarker);
            if (!File.Exists(markerPath))
                return;

            Terrain terrain = GetSelectedTerrain() ?? FindTerrainTopInLoadedScenes();
            if (terrain == null)
            {
                Debug.LogError(
                    "[TerrainBlenderObjExporter] TerrainTop was not found in a loaded scene; " +
                    "the one-shot OBJ export was not performed.");
                return;
            }

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string outputPath = Path.Combine(desktop, "TerrainTop_Blender.obj");
                Export(terrain, outputPath, BlenderResolution);
                File.Delete(markerPath);
                Debug.Log(
                    $"[TerrainBlenderObjExporter] Blender-ready terrain exported successfully: {outputPath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static Terrain GetSelectedTerrain()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null ? selected.GetComponent<Terrain>() : null;
        }

        private static Terrain FindTerrainTopInLoadedScenes()
        {
            foreach (Terrain terrain in Resources.FindObjectsOfTypeAll<Terrain>())
            {
                Scene scene = terrain.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded && terrain.name == "TerrainTop")
                    return terrain;
            }

            return null;
        }

        private static void Export(Terrain terrain, string objPath, int maximumResolution)
        {
            TerrainData data = terrain.terrainData;
            if (data == null)
                throw new InvalidOperationException($"Terrain '{terrain.name}' has no TerrainData.");

            int sourceResolution = data.heightmapResolution;
            int resolution = Math.Min(maximumResolution, sourceResolution);
            float[,] heights = data.GetHeights(0, 0, sourceResolution, sourceResolution);
            Vector3 size = data.size;
            string directory = Path.GetDirectoryName(objPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("The OBJ output directory is invalid.");

            Directory.CreateDirectory(directory);
            string baseName = Path.GetFileNameWithoutExtension(objPath);
            string mtlPath = Path.Combine(directory, baseName + ".mtl");

            CultureInfo invariant = CultureInfo.InvariantCulture;
            using (var writer = new StreamWriter(objPath, false, new UTF8Encoding(false), 1 << 20))
            {
                writer.WriteLine("# Blender-ready mesh converted from Unity Terrain");
                writer.WriteLine($"# Source: {terrain.name}, heightmap: {sourceResolution}x{sourceResolution}");
                writer.WriteLine($"# Mesh: {resolution}x{resolution} vertices, {(resolution - 1) * (resolution - 1) * 2} triangles");
                writer.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");
                writer.WriteLine($"o {SanitizeObjectName(terrain.name)}");

                for (int z = 0; z < resolution; z++)
                {
                    float v = z / (float)(resolution - 1);
                    int sourceZ = Mathf.RoundToInt(v * (sourceResolution - 1));
                    for (int x = 0; x < resolution; x++)
                    {
                        float u = x / (float)(resolution - 1);
                        int sourceX = Mathf.RoundToInt(u * (sourceResolution - 1));
                        float px = u * size.x;
                        float py = heights[sourceZ, sourceX] * size.y;
                        float pz = v * size.z;
                        // Unity is Y-up and Blender is Z-up. Negating Unity Z
                        // keeps the conversion right-handed: (x, y, z) -> (x, -z, y).
                        writer.WriteLine(string.Format(invariant, "v {0:R} {1:R} {2:R}", px, -pz, py));
                    }
                }

                for (int z = 0; z < resolution; z++)
                {
                    float v = z / (float)(resolution - 1);
                    for (int x = 0; x < resolution; x++)
                    {
                        float u = x / (float)(resolution - 1);
                        writer.WriteLine(string.Format(invariant, "vt {0:R} {1:R}", u, v));
                    }
                }

                writer.WriteLine("usemtl Terrain_Surface");
                writer.WriteLine("s 1");
                for (int z = 0; z < resolution - 1; z++)
                {
                    int row = z * resolution;
                    int nextRow = (z + 1) * resolution;
                    for (int x = 0; x < resolution - 1; x++)
                    {
                        int i00 = row + x + 1;
                        int i10 = i00 + 1;
                        int i01 = nextRow + x + 1;
                        int i11 = i01 + 1;
                        writer.WriteLine($"f {i00}/{i00} {i01}/{i01} {i10}/{i10}");
                        writer.WriteLine($"f {i10}/{i10} {i01}/{i01} {i11}/{i11}");
                    }
                }
            }

            using (var writer = new StreamWriter(mtlPath, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("newmtl Terrain_Surface");
                writer.WriteLine("Kd 0.42 0.32 0.20");
                writer.WriteLine("Ks 0.0 0.0 0.0");
                writer.WriteLine("Ns 1.0");
            }

            long vertexCount = (long)resolution * resolution;
            long triangleCount = (long)(resolution - 1) * (resolution - 1) * 2;
            Debug.Log(
                $"[TerrainBlenderObjExporter] Exported '{terrain.name}' to '{objPath}' " +
                $"with {vertexCount:N0} vertices and {triangleCount:N0} triangles.");
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }

        private static string SanitizeObjectName(string value)
        {
            return value.Replace(' ', '_').Replace('/', '_').Replace('\\', '_');
        }
    }
}
