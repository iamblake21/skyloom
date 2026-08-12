using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace CML.Unity.Editor.Environment
{
    [InitializeOnLoad]
    internal static class CMLDayNightEditModeTestRunOnce
    {
        private const string MarkerPath =
            "Temp/CML_RunDayNightEditMode.once";
        private const string ResultPath =
            "Logs/cml-day-night-editmode-tests.xml";
        private const string TestAssembly = "CML.Tests.Unity.EditMode";
        private static readonly string[] TestFixtureFilters =
        {
            @"^CML\.Tests\.Unity\.SolarpunkDayNightProfileTests(?:\.|$)",
            @"^CML\.Tests\.Unity\.SolarpunkAmbientProbeTests(?:\.|$)",
            @"^CML\.Tests\.Unity\.StarterIslandStylizedWaterShaderTests(?:\.|$)"
        };

        private static TestRunnerApi runner;
        private static ResultCallbacks callbacks;

        static CMLDayNightEditModeTestRunOnce()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        private static void RunIfRequested()
        {
            var markerPath = ProjectPath(MarkerPath);
            if (!File.Exists(markerPath))
            {
                return;
            }

            // Unity Test Framework always executes SaveModifiedSceneTask,
            // including synchronous EditMode runs. Refuse the run instead of
            // ever prompting to serialize an in-progress composition.
            if (HasDirtyLoadedScenes())
            {
                File.Delete(markerPath);
                Debug.LogError(
                    "CML_EDITMODE_TEST_RUN " +
                    "status=BLOCKED reason=dirty-loaded-scene");
                return;
            }

            // Consume the request before starting so a domain reload cannot
            // accidentally launch the same run twice.
            File.Delete(markerPath);

            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            callbacks = new ResultCallbacks();
            runner.RegisterCallbacks(callbacks);

            var settings = new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { TestAssembly },
                // groupNames is the Test Framework API's regex-capable full-name
                // filter. Anchoring both fixture names prevents unrelated tests in
                // the shared EditMode assembly from entering this one-shot run.
                groupNames = TestFixtureFilters
            })
            {
                // Exclude frame-based UnityTests; the dirty-scene guard above
                // handles the Test Framework's independent save lifecycle.
                runSynchronously = true
            };

            Debug.Log(
                "CML_EDITMODE_TEST_RUN " +
                $"status=STARTED assembly={TestAssembly} " +
                $"fixtures={string.Join(",", TestFixtureFilters)}");
            runner.Execute(settings);
        }

        private static bool HasDirtyLoadedScenes()
        {
            for (var index = 0; index < EditorSceneManager.sceneCount; index++)
            {
                var scene = EditorSceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class ResultCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var resultPath = ProjectPath(ResultPath);
                TestRunnerApi.SaveResultToFile(result, resultPath);
                Debug.Log(
                    "CML_EDITMODE_TEST_RUN " +
                    $"status={result.TestStatus} " +
                    $"passed={result.PassCount} " +
                    $"failed={result.FailCount} " +
                    $"skipped={result.SkipCount} " +
                    $"inconclusive={result.InconclusiveCount} " +
                    $"duration={result.Duration:F3}s " +
                    $"output={resultPath}");

                runner.UnregisterCallbacks(callbacks);
                Object.DestroyImmediate(runner);
                runner = null;
                callbacks = null;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren || result.FailCount == 0)
                {
                    return;
                }

                Debug.LogError(
                    "CML_EDITMODE_TEST_FAILURE " +
                    $"test={result.FullName} " +
                    $"state={result.ResultState} " +
                    $"message={result.Message}\n{result.StackTrace}");
            }
        }

        private static string ProjectPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                relativePath));
        }
    }
}
