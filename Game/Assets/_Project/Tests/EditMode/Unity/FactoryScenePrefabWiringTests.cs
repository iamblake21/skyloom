using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// Ogni prefab piazzabile deve essere collegato nella scena di gioco.
    ///
    /// È l'inciampo che si è ripetuto tre volte di fila — Curva, Salita, Curva
    /// sinistra. Aggiungere un modulo tocca il codice *e* la serializzazione
    /// della scena: il campo nuovo nasce vuoto e resta vuoto finché il builder
    /// non viene rieseguito. Il codice compila, i test passano, e in gioco il
    /// pezzo non si piazza perché il prefab è null.
    ///
    /// Nessuna delle prove sulla topologia poteva accorgersene: guardano la
    /// simulazione, non cosa la scena ha davvero in mano. Questa sì.
    /// </summary>
    public sealed class FactoryScenePrefabWiringTests
    {
        private const string ScenePath =
            "Assets/_Project/Scenes/92_M04B_FactoryLine_Test.unity";

        /// <summary>
        /// Campi prefab del controller di costruzione che devono essere popolati.
        /// Aggiungendo un modulo va aggiunto anche qui: è il promemoria che
        /// rigenerare la scena non è facoltativo.
        /// </summary>
        private static readonly string[] RequiredPrefabFields =
        {
            "cratePrefab",
            "funnelPrefab",
            "beltStraightPrefab",
            "beltDrivePrefab",
            "beltCurvePrefab",
            "beltInclinePrefab",
            "beltCurveLeftPrefab",
            "pressPrefab"
        };

        [Test]
        public void LaScenaDiGiocoHaOgniPrefabPiazzabileCollegato()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.That(scene.IsValid(), Is.True, $"Scena non apribile: {ScenePath}");

            var controller = FindBuildController(scene);
            Assert.That(
                controller,
                Is.Not.Null,
                "FactoryBuildController assente dalla scena.");

            var serialized = new SerializedObject(controller);
            var missing = new List<string>();
            foreach (var field in RequiredPrefabFields)
            {
                var property = serialized.FindProperty(field);
                if (property == null)
                {
                    missing.Add($"{field} (campo inesistente)");
                    continue;
                }

                if (property.objectReferenceValue == null)
                {
                    missing.Add(field);
                }
            }

            Assert.That(
                missing,
                Is.Empty,
                "Prefab non collegati nella scena: "
                + string.Join(", ", missing)
                + ". Rigenerare con CML/Factory/Build M0.4B Factory Line Test Scene.");
        }

        private static MonoBehaviour FindBuildController(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var components = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var component in components)
                {
                    if (component != null
                        && component.GetType().Name == "FactoryBuildController")
                    {
                        return component;
                    }
                }
            }

            return null;
        }
    }
}
