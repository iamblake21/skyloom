using CML.Unity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Keeps Terrain contact blending visible in Scene view as well as Play
    /// mode. It only publishes Unity's existing alphamap to the shader.
    /// </summary>
    [InitializeOnLoad]
    public static class TerrainSurfaceBlendEditorBinding
    {
        static TerrainSurfaceBlendEditorBinding()
        {
            EditorApplication.delayCall += Bind;
            EditorApplication.hierarchyChanged += Bind;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
        }

        private static void HandleSceneOpened(
            Scene scene,
            OpenSceneMode mode)
        {
            Bind();
        }

        private static void Bind()
        {
            TerrainSurfaceBlendGlobals.BindActiveTerrain();
        }
    }
}
