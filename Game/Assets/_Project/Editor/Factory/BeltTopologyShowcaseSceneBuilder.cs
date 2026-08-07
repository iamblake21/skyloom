using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Factory
{
    /// <summary>
    /// Scena vetrina, parallela a 92_M04B: mostra montate le stesse
    /// configurazioni che i test verificano, così l'aggancio si guarda invece di
    /// descriverlo.
    ///
    /// Non tocca 92_M04B né alcun prefab: instanzia soltanto. Le pose sono le
    /// medesime dei test — `BeltCurveAndInclineTopologyTests` per il moto e
    /// `BeltCurvePlacementTests` per l'aggancio — quindi se qui si vede storto,
    /// lì deve fallire qualcosa.
    /// </summary>
    public static class BeltTopologyShowcaseSceneBuilder
    {
        private const string ScenePath =
            "Assets/_Project/Scenes/93_BeltTopology_Showcase.unity";

        private const string PrefabRoot =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/";

        private const string StraightPath = PrefabRoot + "PF_Belt_Straight.prefab";
        private const string CurvePath = PrefabRoot + "PF_Belt_Curve.prefab";
        private const string InclinePath = PrefabRoot + "PF_Belt_Incline.prefab";

        private const string PressPath =
            "Assets/_Project/Art/MechanicalEra/Prefabs/PF_MechanicalPress.prefab";

        private const float Cell = 1.0f;
        private const float InclineRise = 0.30f;

        [MenuItem("CML/Factory/Build Belt Topology Showcase Scene")]
        public static void BuildScene()
        {
            var straight = RequirePrefab(StraightPath);
            var curve = RequirePrefab(CurvePath);
            var incline = RequirePrefab(InclinePath);
            var press = RequirePrefab(PressPath);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            CreateGround(scene);
            CreateLighting(scene);

            // --- A: rettilineo, Curva a destra, rettilineo che ne raccoglie
            // l'uscita laterale. Yaw 0 = +Z, yaw 1 = +X.
            var a = new Vector3(0f, 0f, 0f);
            Place(scene, straight, "A1_Straight", a, 0);
            Place(scene, straight, "A2_Straight", a + Forward(0), 0);
            Place(scene, curve, "A3_CurveRight", a + Forward(0) * 2f, 0);
            Place(scene, straight, "A4_AfterCurve", a + Forward(0) * 2f + Forward(1), 1);
            Place(
                scene,
                straight,
                "A5_AfterCurve",
                a + Forward(0) * 2f + Forward(1) * 2f,
                1);
            CreateLabel(scene, a + new Vector3(0f, 1.6f, -1.2f), "A - CURVA A DESTRA");

            // --- B: la stessa Curva girata di mezzo giro svolta a sinistra.
            var b = new Vector3(8f, 0f, 0f);
            Place(scene, straight, "B1_Straight", b, 0);
            Place(scene, straight, "B2_Straight", b + Forward(0), 0);
            Place(scene, curve, "B3_CurveLeft", b + Forward(0) * 2f, 2);
            Place(scene, straight, "B4_AfterCurve", b + Forward(0) * 2f + Forward(3), 3);
            Place(
                scene,
                straight,
                "B5_AfterCurve",
                b + Forward(0) * 2f + Forward(3) * 2f,
                3);
            CreateLabel(scene, b + new Vector3(0f, 1.6f, -1.2f), "B - CURVA A SINISTRA");

            // --- C: dalla Pressa alla Curva, il caso che si compone per primo.
            var c = new Vector3(-8f, 0f, 0f);
            Place(scene, press, "C1_Press", c, 0);
            Place(scene, curve, "C2_Curve", c + Forward(0), 0);
            Place(scene, straight, "C3_AfterCurve", c + Forward(0) + Forward(1), 1);
            CreateLabel(scene, c + new Vector3(0f, 2.8f, -1.2f), "C - PRESSA VERSO CURVA");

            // --- D: due Salite in fila, poi un rettilineo in quota.
            var d = new Vector3(16f, 0f, 0f);
            Place(scene, straight, "D1_Straight", d, 0);
            Place(scene, incline, "D2_Incline", d + Forward(0), 0);
            Place(
                scene,
                incline,
                "D3_Incline",
                d + Forward(0) * 2f + Vector3.up * InclineRise,
                0);
            Place(
                scene,
                straight,
                "D4_Top",
                d + Forward(0) * 3f + Vector3.up * (InclineRise * 2f),
                0);
            Place(
                scene,
                curve,
                "D5_CurveInQuota",
                d + Forward(0) * 4f + Vector3.up * (InclineRise * 2f),
                0);
            Place(
                scene,
                straight,
                "D6_AfterCurve",
                d + Forward(0) * 4f + Forward(1) + Vector3.up * (InclineRise * 2f),
                1);
            CreateLabel(scene, d + new Vector3(0f, 1.6f, -1.2f), "D - SALITA E CURVA IN QUOTA");

            VerifyPairsTouch(
                scene,
                ("A2_Straight", "A3_CurveRight"),
                ("A3_CurveRight", "A4_AfterCurve"),
                ("B2_Straight", "B3_CurveLeft"),
                ("B3_CurveLeft", "B4_AfterCurve"),
                ("C1_Press", "C2_Curve"),
                ("C2_Curve", "C3_AfterCurve"),
                ("D1_Straight", "D2_Incline"),
                ("D2_Incline", "D3_Incline"),
                ("D3_Incline", "D4_Top"),
                ("D4_Top", "D5_CurveInQuota"),
                ("D5_CurveInQuota", "D6_AfterCurve"));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"BELT_TOPOLOGY_SHOWCASE_WRITTEN {ScenePath}");
        }

        /// <summary>
        /// Misura, per ogni coppia consecutiva, quanto distano gli ingombri resi.
        ///
        /// Serve a non giudicare l'aggancio guardando uno screenshot: se due
        /// moduli non si toccano la scena fallisce qui, con il numero in chiaro.
        /// La soglia tiene conto del rientro che i moduli hanno per stare dentro
        /// la cella — i cuscinetti si fermano a filo del riquadro, non oltre.
        /// </summary>
        private static void VerifyPairsTouch(
            Scene scene,
            params (string From, string To)[] pairs)
        {
            const float maximumGap = 0.16f;
            var byName = new Dictionary<string, GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                byName[root.name] = root;
            }

            var failures = new List<string>();
            foreach (var (from, to) in pairs)
            {
                if (!byName.TryGetValue(from, out var a)
                    || !byName.TryGetValue(to, out var b))
                {
                    failures.Add($"{from} -> {to}: modulo assente dalla scena");
                    continue;
                }

                if (!TryRenderBounds(a, out var boundsA)
                    || !TryRenderBounds(b, out var boundsB))
                {
                    failures.Add($"{from} -> {to}: nessun renderer da misurare");
                    continue;
                }

                var gap = Gap(boundsA, boundsB);
                var verdict = gap <= maximumGap ? "OK" : "STACCATI";
                Debug.Log($"SHOWCASE_GAP {from} -> {to} = {gap * 100f:F1} cm {verdict}");
                if (gap > maximumGap)
                {
                    failures.Add($"{from} -> {to}: {gap * 100f:F1} cm");
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Moduli non agganciati nella scena vetrina: "
                    + string.Join("; ", failures));
            }

            Debug.Log($"SHOWCASE_ALL_PAIRS_TOUCH {pairs.Length}");
        }

        /// <summary>Distanza fra due ingombri: zero se si compenetrano.</summary>
        private static float Gap(Bounds left, Bounds right)
        {
            var dx = Mathf.Max(
                0f,
                Mathf.Max(left.min.x - right.max.x, right.min.x - left.max.x));
            var dy = Mathf.Max(
                0f,
                Mathf.Max(left.min.y - right.max.y, right.min.y - left.max.y));
            var dz = Mathf.Max(
                0f,
                Mathf.Max(left.min.z - right.max.z, right.min.z - left.max.z));
            return new Vector3(dx, dy, dz).magnitude;
        }

        private static bool TryRenderBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return true;
        }

        /// <summary>Versore della cella nella direzione di un quarto di giro.</summary>
        private static Vector3 Forward(int yawQuarterTurns)
        {
            switch (yawQuarterTurns & 3)
            {
                case 0: return new Vector3(0f, 0f, Cell);
                case 1: return new Vector3(Cell, 0f, 0f);
                case 2: return new Vector3(0f, 0f, -Cell);
                default: return new Vector3(-Cell, 0f, 0f);
            }
        }

        private static void Place(
            Scene scene,
            GameObject prefab,
            string name,
            Vector3 position,
            int yawQuarterTurns)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = name;
            instance.transform.SetPositionAndRotation(
                position,
                Quaternion.Euler(0f, yawQuarterTurns * 90f, 0f));
        }

        private static void CreateGround(Scene scene)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.name = "Showcase_Ground";
            ground.transform.position = new Vector3(4f, -0.25f, 3f);
            ground.transform.localScale = new Vector3(40f, 0.5f, 20f);
        }

        private static void CreateLighting(Scene scene)
        {
            var keyObject = new GameObject("Showcase_Key");
            SceneManager.MoveGameObjectToScene(keyObject, scene);
            keyObject.transform.rotation = Quaternion.Euler(52f, 38f, 0f);
            var key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.94f, 0.84f);
            key.intensity = 1.35f;
            key.shadows = LightShadows.Soft;

            var cameraObject = new GameObject("Showcase_Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(4f, 9f, -9f),
                Quaternion.Euler(38f, 0f, 0f));
            cameraObject.AddComponent<Camera>();
        }

        private static void CreateLabel(Scene scene, Vector3 position, string text)
        {
            var label = new GameObject($"LABEL_{text}");
            SceneManager.MoveGameObjectToScene(label, scene);
            label.transform.position = position;
            var mesh = label.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 48;
            mesh.characterSize = 0.08f;
            mesh.color = new Color(0.10f, 0.12f, 0.10f);
        }

        private static GameObject RequirePrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Prefab mancante: {path}");
            }

            return prefab;
        }
    }
}
