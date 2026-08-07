using CML.Unity.Presentation.Equipment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CML.Editor.Equipment
{
    [CustomEditor(typeof(PickaxeHandsSetupAuthoring))]
    internal sealed class PickaxeHandsSetupAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var authoring = (PickaxeHandsSetupAuthoring)target;

            EditorGUILayout.HelpBox(
                "Sposta PICKAXE con i normali gizmo. " +
                "Quando il risultato ti piace, premi SALVA POSA NEL GIOCO. " +
                "Il profilo viene usato automaticamente in tutte le scene.",
                MessageType.Info);

            DrawDefaultInspector();
            EditorGUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(!authoring.IsComplete))
            {
                var previousColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
                if (GUILayout.Button(
                        "SALVA POSA NEL GIOCO",
                        GUILayout.Height(42f)))
                {
                    SavePose(authoring);
                }

                GUI.backgroundColor = previousColor;

                if (GUILayout.Button(
                        "Ricarica l'ultima posa salvata",
                        GUILayout.Height(28f)))
                {
                    ReloadPose(authoring);
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "Profilo condiviso",
                "Assets/_Project/Resources/Equipment/" +
                "FirstPersonEquipmentPose.asset");
        }

        private static void SavePose(
            PickaxeHandsSetupAuthoring authoring)
        {
            Undo.RecordObject(
                authoring.SharedPose,
                "Save first-person pickaxe pose");
            authoring.SharedPose.Capture(authoring.Pickaxe);
            EditorUtility.SetDirty(authoring.SharedPose);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
            EditorSceneManager.SaveScene(authoring.gameObject.scene);

            Debug.Log(
                "PICKAXE_VIEW_POSE_SAVED: the setup scene and shared pose " +
                "were saved; the pose now applies to every gameplay scene.",
                authoring);
        }

        private static void ReloadPose(
            PickaxeHandsSetupAuthoring authoring)
        {
            Undo.RecordObject(
                authoring.Pickaxe,
                "Reload first-person pickaxe pose");

            authoring.SharedPose.Pickaxe.ApplyTo(authoring.Pickaxe);
            SceneView.RepaintAll();
        }
    }
}
