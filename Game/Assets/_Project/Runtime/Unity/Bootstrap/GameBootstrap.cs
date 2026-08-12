using System;
using System.Collections;
using CML.Unity.Airship;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Unity.Bootstrap
{
    /// <summary>
    /// Composition root for engine adapters. It deliberately contains no gameplay rules.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private const string BootstrapSceneName = "00_Bootstrap";
        private const string IntroSceneName = "01_IntroCinematic";
        private const string TechnicalSceneName = "90_Technical";
        private const string GameplaySceneName = "91_StarterIsland_Terrain_Review";
        private const string TechnicalReadyObjectName = "AIR_TechnicalReady";
        private const string SmokeArgument = "--cml-smoke-test";
        private const float SmokeTimeoutSeconds = 15f;
        private const int SceneLoadFailureExitCode = 2;
        private const int TechnicalReadyTimeoutExitCode = 3;

        private static GameBootstrap instance;
        private static bool sceneLoadStarted;

        private bool isPrimaryInstance;
        private bool shouldLoadEntryScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
            sceneLoadStarted = false;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                enabled = false;
                Destroy(gameObject);
                return;
            }

            instance = this;
            isPrimaryInstance = true;
            shouldLoadEntryScene = gameObject.scene.name == BootstrapSceneName;
            DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            if (!isPrimaryInstance)
            {
                yield break;
            }

            var isSmokeTest = Array.IndexOf(
                System.Environment.GetCommandLineArgs(),
                SmokeArgument) >= 0;
            var smokeDeadline = isSmokeTest
                ? Time.realtimeSinceStartup + SmokeTimeoutSeconds
                : float.PositiveInfinity;

            var entryScene = isSmokeTest
                ? TechnicalSceneName
                : IntroSceneName;
            if (shouldLoadEntryScene)
            {
                if (!TryBeginSceneLoad(
                        entryScene,
                        out var loadOperation,
                        out var loadFailure))
                {
                    FailStartup(loadFailure, SceneLoadFailureExitCode);
                    yield break;
                }

                while (loadOperation != null && !loadOperation.isDone)
                {
                    if (isSmokeTest && Time.realtimeSinceStartup >= smokeDeadline)
                    {
                        FailStartup(
                            $"Timed out while loading scene '{entryScene}'.",
                            SceneLoadFailureExitCode);
                        yield break;
                    }

                    yield return null;
                }
            }

            if (!isSmokeTest)
            {
                yield break;
            }

            while (!TechnicalSceneContainsReadyObject())
            {
                if (Time.realtimeSinceStartup >= smokeDeadline)
                {
                    FailStartup(
                        $"Timed out waiting for GameObject '{TechnicalReadyObjectName}' " +
                        $"in scene '{TechnicalSceneName}'.",
                        TechnicalReadyTimeoutExitCode);
                    yield break;
                }

                yield return null;
            }

            Debug.Log("CML_AIR_TECHNICAL_READY");
            Debug.Log("CML_SMOKE_TEST_READY");
            Application.Quit(0);
        }

        private static bool TryBeginSceneLoad(
            string sceneName,
            out AsyncOperation loadOperation,
            out string failure)
        {
            loadOperation = null;
            failure = null;

            var requestedScene = SceneManager.GetSceneByName(sceneName);
            if (requestedScene.IsValid() && requestedScene.isLoaded)
            {
                sceneLoadStarted = true;
                return true;
            }

            if (sceneLoadStarted)
            {
                failure = $"Scene '{sceneName}' load was already requested but is not loaded.";
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                failure = $"Scene '{sceneName}' is not available in the build settings.";
                return false;
            }

            sceneLoadStarted = true;

            try
            {
                loadOperation = SceneManager.LoadSceneAsync(
                    sceneName,
                    LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                failure =
                    $"Unable to load scene '{sceneName}': " +
                    exception.GetType().Name + ": " + exception.Message;
                return false;
            }

            if (loadOperation == null)
            {
                failure = $"Unity returned no load operation for scene '{sceneName}'.";
                return false;
            }

            return true;
        }

        private static bool TechnicalSceneContainsReadyObject()
        {
            var technicalScene = SceneManager.GetSceneByName(TechnicalSceneName);
            if (!technicalScene.IsValid() || !technicalScene.isLoaded)
            {
                return false;
            }

            var roots = technicalScene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                if (ContainsReadyScenario(roots[index].transform, TechnicalReadyObjectName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsReadyScenario(Transform root, string exactName)
        {
            if (root.name == exactName)
            {
                var scenario = root.GetComponent<AirshipTechnicalScenario>();
                if (scenario != null && scenario.IsReady)
                {
                    return true;
                }
            }

            for (var index = 0; index < root.childCount; index++)
            {
                if (ContainsReadyScenario(root.GetChild(index), exactName))
                {
                    return true;
                }
            }

            return false;
        }

        private static void FailStartup(string message, int exitCode)
        {
            Debug.LogError($"CML_BOOTSTRAP_FAILURE: {message}");
            Application.Quit(exitCode);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
