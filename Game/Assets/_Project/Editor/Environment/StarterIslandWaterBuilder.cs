using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Builds the Starter Island water as independent authored sections.
    ///
    /// Pools, creeks and falls deliberately use separate meshes. A large
    /// height change can therefore never turn one creek quad into a stretched
    /// rectangular waterfall.
    /// </summary>
    public static class StarterIslandWaterBuilder
    {
        public const string RootName = "ENV_StarterIsland_WaterSystem";

        private const string DataRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Data";
        private const int RibbonCrossSectionVertices = 5;

        /// <summary>
        /// Impronta orizzontale di una pozza, con la stessa formula di bordo
        /// organico usata da <see cref="BuildPoolMesh"/>. Serve a tagliare i
        /// nastri sul bordo del disco: i tracciati sono autorizzati a partire e
        /// finire dentro le pozze, e due superfici trasparenti sovrapposte con
        /// ZWrite disattivato si miscelano due volte, il che produceva le
        /// chiazze slavate e le giunzioni sporche. La cascata superiore
        /// arrivava al 51% del raggio della pozza intermedia e il ruscello
        /// basso entrava per il 96% del raggio del lago grande.
        /// </summary>
        private readonly struct PoolFootprint
        {
            public PoolFootprint(
                Vector3 center,
                Vector2 radii,
                float rotationRadians)
            {
                Center = center;
                Radii = radii;
                RotationRadians = rotationRadians;
            }

            public Vector3 Center { get; }
            public Vector2 Radii { get; }
            public float RotationRadians { get; }

            /// <summary>
            /// Distanza normalizzata dal centro: minore di uno significa
            /// dentro il disco.
            /// </summary>
            public float NormalizedDistance(Vector3 point)
            {
                var deltaX = point.x - Center.x;
                var deltaZ = point.z - Center.z;
                var cos = Mathf.Cos(RotationRadians);
                var sin = Mathf.Sin(RotationRadians);
                var localX = deltaX * cos + deltaZ * sin;
                var localZ = -deltaX * sin + deltaZ * cos;
                var unitX = localX / Radii.x;
                var unitZ = localZ / Radii.y;
                var radius = Mathf.Sqrt(unitX * unitX + unitZ * unitZ);
                if (radius <= 0.000001f)
                {
                    return 0f;
                }

                var angle = Mathf.Atan2(unitZ, unitX);
                var organicOutline =
                    1f +
                    Mathf.Sin(angle * 3f + 0.47f) * 0.045f +
                    Mathf.Sin(angle * 7f - 1.18f) * 0.024f +
                    Mathf.Sin(angle * 11f + 0.26f) * 0.012f;
                return radius / organicOutline;
            }
        }

        private static readonly PoolFootprint SourcePool =
            new PoolFootprint(
                new Vector3(-205f, 82.00f, 145f),
                new Vector2(10.0f, 7.2f),
                0.10f);

        /// <summary>
        /// Le quattro pozze stanno sui quattro ripiani della scala, alla quota
        /// esatta del ripiano che le ospita: corona 82, terzo anello 71,
        /// secondo 62, primo 50. Devono restare allineate a
        /// <c>WaterBasins</c> in <see cref="StarterIslandTerrainSetup"/>, che
        /// scava le conche corrispondenti.
        /// </summary>
        private static readonly PoolFootprint ThirdPool =
            new PoolFootprint(
                new Vector3(-198f, 71.00f, 106f),
                new Vector2(9.5f, 6.8f),
                0.14f);

        private static readonly PoolFootprint IntermediatePool =
            new PoolFootprint(
                new Vector3(-194f, 62.00f, 80f),
                new Vector2(10.5f, 7.4f),
                -0.08f);

        private static readonly PoolFootprint LowerPool =
            new PoolFootprint(
                new Vector3(-188f, 50.00f, 50f),
                new Vector2(11.5f, 8.5f),
                0.06f);

        private static readonly PoolFootprint MainPond =
            new PoolFootprint(
                new Vector3(-178f, 26.50f, -72f),
                new Vector2(40f, 27f),
                -0.045f);

        private static readonly PoolFootprint[] Pools =
        {
            SourcePool,
            ThirdPool,
            IntermediatePool,
            LowerPool,
            MainPond
        };

        /// <summary>
        /// Rebuilds the complete deterministic source-to-pond water system.
        /// All generated vertices are expressed in the supplied parent's local
        /// space. No Collider is created.
        /// </summary>
        public static GameObject Build(Transform parent, Material material)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            EnsureFolder(DataRoot);
            var previous = FindDirectChild(parent, RootName);
            if (previous != null)
            {
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // Catena allineata alla scala dei ripiani: ogni pozza sta sul suo
            // ripiano alla quota del ripiano, e ogni salto cade dalla parete
            // che separa due ripiani. Il salto finale da 50 a 30 e' la cascata
            // principale, venti metri su una parete vera.
            CreateSection(
                root.transform,
                "ENV_Water_Pool_Source",
                BuildPoolMesh("MD_Water_Pool_Source", SourcePool, 48, 5),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Creek_Crown",
                BuildRibbonMesh(
                    "MD_Water_Creek_Crown",
                    new RibbonDefinition(
                        new Vector3(-204.4f, 81.96f, 138.0f),
                        new Vector3(-206.8f, 81.90f, 131.0f),
                        new Vector3(-202.4f, 81.86f, 123.0f),
                        new Vector3(-201.0f, 81.82f, 116.5f),
                        26,
                        2.30f,
                        2.05f,
                        0.035f,
                        0.31f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Waterfall_Crown",
                BuildRibbonMesh(
                    "MD_Water_Waterfall_Crown",
                    new RibbonDefinition(
                        new Vector3(-201.0f, 81.80f, 116.2f),
                        new Vector3(-200.6f, 80.20f, 114.2f),
                        new Vector3(-199.4f, 72.60f, 112.6f),
                        new Vector3(-199.0f, 71.05f, 110.8f),
                        22,
                        2.05f,
                        2.55f,
                        0.058f,
                        1.17f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Pool_Third",
                BuildPoolMesh("MD_Water_Pool_Third", ThirdPool, 48, 5),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Creek_Third",
                BuildRibbonMesh(
                    "MD_Water_Creek_Third",
                    new RibbonDefinition(
                        new Vector3(-197.6f, 70.96f, 100.0f),
                        new Vector3(-199.2f, 70.92f, 96.5f),
                        new Vector3(-195.4f, 70.88f, 93.0f),
                        new Vector3(-196.0f, 70.84f, 90.4f),
                        18,
                        2.25f,
                        2.05f,
                        0.035f,
                        0.73f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Waterfall_Third",
                BuildRibbonMesh(
                    "MD_Water_Waterfall_Third",
                    new RibbonDefinition(
                        new Vector3(-196.0f, 70.82f, 90.1f),
                        new Vector3(-195.8f, 69.40f, 88.4f),
                        new Vector3(-195.2f, 63.40f, 87.0f),
                        new Vector3(-195.0f, 62.05f, 85.2f),
                        20,
                        2.05f,
                        2.50f,
                        0.055f,
                        2.05f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Pool_Intermediate",
                BuildPoolMesh(
                    "MD_Water_Pool_Intermediate",
                    IntermediatePool,
                    48,
                    5),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Creek_Middle",
                BuildRibbonMesh(
                    "MD_Water_Creek_Middle",
                    new RibbonDefinition(
                        new Vector3(-193.4f, 61.96f, 73.4f),
                        new Vector3(-195.0f, 61.92f, 70.0f),
                        new Vector3(-190.8f, 61.88f, 66.4f),
                        new Vector3(-192.0f, 61.84f, 63.4f),
                        18,
                        2.35f,
                        2.10f,
                        0.035f,
                        1.41f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Waterfall_Middle",
                BuildRibbonMesh(
                    "MD_Water_Waterfall_Middle",
                    new RibbonDefinition(
                        new Vector3(-192.0f, 61.82f, 63.1f),
                        new Vector3(-191.6f, 60.20f, 61.4f),
                        new Vector3(-190.6f, 51.60f, 60.0f),
                        new Vector3(-190.0f, 50.05f, 58.2f),
                        22,
                        2.10f,
                        2.60f,
                        0.058f,
                        1.91f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Pool_Lower",
                BuildPoolMesh("MD_Water_Pool_Lower", LowerPool, 48, 5),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Creek_Shelf",
                BuildRibbonMesh(
                    "MD_Water_Creek_Shelf",
                    new RibbonDefinition(
                        new Vector3(-187.2f, 49.96f, 42.0f),
                        new Vector3(-189.0f, 49.92f, 37.0f),
                        new Vector3(-184.4f, 49.88f, 32.0f),
                        new Vector3(-186.0f, 49.84f, 27.4f),
                        20,
                        2.45f,
                        2.15f,
                        0.035f,
                        2.47f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Waterfall_Main",
                BuildRibbonMesh(
                    "MD_Water_Waterfall_Main",
                    new RibbonDefinition(
                        new Vector3(-186.0f, 49.82f, 27.1f),
                        new Vector3(-185.4f, 47.40f, 25.0f),
                        new Vector3(-183.8f, 32.60f, 22.6f),
                        new Vector3(-183.0f, 30.10f, 20.0f),
                        34,
                        2.15f,
                        2.95f,
                        0.062f,
                        3.05f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Creek_Lower",
                BuildRibbonMesh(
                    "MD_Water_Creek_Lower",
                    new RibbonDefinition(
                        new Vector3(-183.0f, 29.96f, 19.6f),
                        new Vector3(-188.0f, 29.40f, -2.0f),
                        new Vector3(-172.0f, 27.80f, -24.0f),
                        new Vector3(-178.4f, 26.58f, -45.0f),
                        48,
                        2.65f,
                        3.05f,
                        0.038f,
                        4.11f)),
                material);

            CreateSection(
                root.transform,
                "ENV_Water_Pond_Main",
                BuildPoolMesh("MD_Water_Pond_Main", MainPond, 72, 9),
                material);

            var colliders =
                root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != 0)
            {
                throw new InvalidOperationException(
                    "Starter Island water must remain visual-only.");
            }

            EditorUtility.SetDirty(root);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "STARTER_ISLAND_WATER_BUILD sections=14 pools=5 " +
                "creeks=5 waterfalls=4 crossSectionVertices=5 " +
                "colliders=0 deterministic=1 status=PASS");
            return root;
        }

        private static Mesh BuildPoolMesh(
            string assetName,
            PoolFootprint pool,
            int angularSections,
            int radialSections)
        {
            var center = pool.Center;
            var radii = pool.Radii;
            var rotationRadians = pool.RotationRadians;
            if (angularSections < 12 || radialSections < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(angularSections),
                    "A water pool needs at least 12 angular and 2 radial " +
                    "sections.");
            }

            var vertices = new List<Vector3>(
                1 + angularSections * radialSections);
            var normals = new List<Vector3>(
                1 + angularSections * radialSections);
            var uv = new List<Vector2>(
                1 + angularSections * radialSections);
            var colors = new List<Color>(
                1 + angularSections * radialSections);
            var triangles = new List<int>(
                angularSections *
                (1 + (radialSections - 1) * 2) *
                3);

            vertices.Add(center);
            normals.Add(Vector3.up);
            uv.Add(new Vector2(0.5f, 0.5f));
            colors.Add(new Color(0f, 1f, 0f, 1f));

            var cosRotation = Mathf.Cos(rotationRadians);
            var sinRotation = Mathf.Sin(rotationRadians);
            for (var radial = 1; radial <= radialSections; radial++)
            {
                var radialT = radial / (float)radialSections;
                var outlineBlend = radialT * radialT;
                for (var angular = 0;
                     angular < angularSections;
                     angular++)
                {
                    var angle =
                        angular * Mathf.PI * 2f / angularSections;
                    var organicOutline =
                        1f +
                        Mathf.Sin(angle * 3f + 0.47f) * 0.045f +
                        Mathf.Sin(angle * 7f - 1.18f) * 0.024f +
                        Mathf.Sin(angle * 11f + 0.26f) * 0.012f;
                    var radialScale =
                        radialT *
                        Mathf.Lerp(1f, organicOutline, outlineBlend);
                    var localX =
                        Mathf.Cos(angle) * radii.x * radialScale;
                    var localZ =
                        Mathf.Sin(angle) * radii.y * radialScale;
                    var rotatedX =
                        localX * cosRotation - localZ * sinRotation;
                    var rotatedZ =
                        localX * sinRotation + localZ * cosRotation;
                    vertices.Add(
                        center +
                        new Vector3(rotatedX, 0f, rotatedZ));
                    normals.Add(Vector3.up);
                    uv.Add(
                        new Vector2(
                            0.5f + localX / (radii.x * 2f),
                            0.5f + localZ / (radii.y * 2f)));
                    colors.Add(
                        new Color(
                            radialT,
                            1f - radialT,
                            0f,
                            1f));
                }
            }

            for (var angular = 0;
                 angular < angularSections;
                 angular++)
            {
                var next = (angular + 1) % angularSections;
                triangles.Add(0);
                triangles.Add(1 + next);
                triangles.Add(1 + angular);
            }

            for (var radial = 0;
                 radial < radialSections - 1;
                 radial++)
            {
                var innerStart = 1 + radial * angularSections;
                var outerStart =
                    innerStart + angularSections;
                for (var angular = 0;
                     angular < angularSections;
                     angular++)
                {
                    var next = (angular + 1) % angularSections;
                    var innerCurrent = innerStart + angular;
                    var innerNext = innerStart + next;
                    var outerCurrent = outerStart + angular;
                    var outerNext = outerStart + next;
                    triangles.Add(innerCurrent);
                    triangles.Add(innerNext);
                    triangles.Add(outerCurrent);
                    triangles.Add(innerNext);
                    triangles.Add(outerNext);
                    triangles.Add(outerCurrent);
                }
            }

            return WriteMeshAsset(
                assetName,
                vertices,
                normals,
                uv,
                colors,
                triangles,
                false);
        }

        /// <summary>
        /// Restituisce l'intervallo del parametro della curva che resta fuori
        /// da ogni disco d'acqua, cioè la porzione di nastro effettivamente
        /// visibile. Il campionamento fitto individua i due attraversamenti e
        /// una ricerca binaria li porta esattamente sul bordo, restituendo
        /// sempre il campione esterno: così il nastro tocca la pozza senza
        /// entrarci e non si crea nemmeno un vuoto.
        /// </summary>
        private static Vector2 ComputeRibbonTrim(RibbonDefinition definition)
        {
            const int probes = 512;
            var firstOutside = -1;
            var lastOutside = -1;
            for (var index = 0; index <= probes; index++)
            {
                var point = CubicBezier(
                    definition.Start,
                    definition.ControlA,
                    definition.ControlB,
                    definition.End,
                    index / (float)probes);
                if (IsInsideAnyPool(point))
                {
                    continue;
                }

                if (firstOutside < 0)
                {
                    firstOutside = index;
                }

                lastOutside = index;
            }

            if (firstOutside < 0)
            {
                throw new InvalidOperationException(
                    "A water ribbon lies entirely inside a pool footprint " +
                    "and would be invisible.");
            }

            var trimStart = firstOutside == 0
                ? 0f
                : RefinePoolBoundary(
                    definition,
                    (firstOutside - 1) / (float)probes,
                    firstOutside / (float)probes);
            var trimEnd = lastOutside == probes
                ? 1f
                : RefinePoolBoundary(
                    definition,
                    (lastOutside + 1) / (float)probes,
                    lastOutside / (float)probes);
            return new Vector2(trimStart, trimEnd);
        }

        private static float RefinePoolBoundary(
            RibbonDefinition definition,
            float insideT,
            float outsideT)
        {
            for (var iteration = 0; iteration < 24; iteration++)
            {
                var middle = (insideT + outsideT) * 0.5f;
                var point = CubicBezier(
                    definition.Start,
                    definition.ControlA,
                    definition.ControlB,
                    definition.End,
                    middle);
                if (IsInsideAnyPool(point))
                {
                    insideT = middle;
                }
                else
                {
                    outsideT = middle;
                }
            }

            return outsideT;
        }

        /// <summary>
        /// Un punto è dentro l'acqua di una pozza solo se sta dentro il bordo
        /// del disco **e** si trova alla quota del pelo dell'acqua.
        ///
        /// La sola condizione orizzontale è sbagliata per le cascate: scendendo
        /// da 72 a 62 metri il bordo del disco viene attraversato quando la
        /// cascata è ancora diversi metri più in alto della superficie, e
        /// tagliarla lì la lasciava sospesa a mezz'aria senza toccare nulla.
        /// Con il vincolo di quota il salto prosegue fino a tuffarsi
        /// nell'acqua, mentre un ruscello che scorre già a livello viene
        /// tagliato subito sul bordo, come deve essere.
        /// </summary>
        private static bool IsInsideAnyPool(Vector3 point)
        {
            const float surfaceTolerance = 0.30f;
            for (var index = 0; index < Pools.Length; index++)
            {
                var pool = Pools[index];
                if (pool.NormalizedDistance(point) < 1f &&
                    point.y <= pool.Center.y + surfaceTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static Mesh BuildRibbonMesh(
            string assetName,
            RibbonDefinition definition)
        {
            if (definition.LongitudinalSections < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(definition),
                    "A water ribbon needs at least two longitudinal sections.");
            }

            var vertexCount =
                definition.LongitudinalSections *
                RibbonCrossSectionVertices;
            var vertices = new List<Vector3>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var uv = new List<Vector2>(vertexCount);
            var colors = new List<Color>(vertexCount);
            var triangles = new List<int>(
                (definition.LongitudinalSections - 1) *
                (RibbonCrossSectionVertices - 1) *
                6);

            // I tracciati sono autorizzati a partire e finire dentro le pozze,
            // per garantire la continuità del percorso. Il nastro visibile va
            // però tagliato sul bordo del disco, altrimenti due superfici
            // trasparenti si sovrappongono e si miscelano due volte.
            var trim = ComputeRibbonTrim(definition);
            var travelled = 0f;
            var previousCenter = definition.Start;
            for (var longitudinal = 0;
                 longitudinal < definition.LongitudinalSections;
                 longitudinal++)
            {
                // u percorre il nastro visibile, t percorre la curva completa:
                // le larghezze autorizzate valgono quindi agli estremi
                // visibili e non a quelli tagliati.
                var u =
                    longitudinal /
                    (float)(definition.LongitudinalSections - 1);
                var t = Mathf.Lerp(trim.x, trim.y, u);
                var center = CubicBezier(
                    definition.Start,
                    definition.ControlA,
                    definition.ControlB,
                    definition.End,
                    t);
                if (longitudinal > 0)
                {
                    travelled += Vector3.Distance(
                        previousCenter,
                        center);
                }

                previousCenter = center;
                var tangent = CubicBezierDerivative(
                    definition.Start,
                    definition.ControlA,
                    definition.ControlB,
                    definition.End,
                    t);
                if (tangent.sqrMagnitude <= 0.000001f)
                {
                    tangent = definition.End - definition.Start;
                }

                tangent.Normalize();
                var horizontalTangent =
                    new Vector3(tangent.x, 0f, tangent.z);
                if (horizontalTangent.sqrMagnitude <= 0.000001f)
                {
                    horizontalTangent = Vector3.forward;
                }

                horizontalTangent.Normalize();
                var lateral =
                    new Vector3(
                        -horizontalTangent.z,
                        0f,
                        horizontalTangent.x);
                var surfaceNormal =
                    Vector3.Cross(lateral, tangent).normalized;
                if (surfaceNormal.y < 0f)
                {
                    surfaceNormal = -surfaceNormal;
                }

                var widthVariation =
                    1f +
                    Mathf.Sin(
                        u * Mathf.PI * 4.2f +
                        definition.Phase) *
                    0.035f +
                    Mathf.Sin(
                        u * Mathf.PI * 9.4f -
                        definition.Phase * 0.7f) *
                    0.018f;
                var halfWidth =
                    Mathf.Lerp(
                        definition.StartHalfWidth,
                        definition.EndHalfWidth,
                        SmootherStep(u)) *
                    widthVariation;
                var surfaceClearance =
                    definition.ProfileHeight > 0.045f
                        ? 0.18f
                        : 0.10f;
                for (var across = 0;
                     across < RibbonCrossSectionVertices;
                     across++)
                {
                    var acrossT =
                        across /
                        (float)(RibbonCrossSectionVertices - 1);
                    var lateralUnit = acrossT * 2f - 1f;
                    var edge = Mathf.Abs(lateralUnit);
                    var crown =
                        (1f - edge * edge) *
                        definition.ProfileHeight;
                    var fineRipple =
                        Mathf.Sin(
                            u * Mathf.PI * 8f +
                            lateralUnit * 2.1f +
                            definition.Phase) *
                        definition.ProfileHeight *
                        0.18f;
                    vertices.Add(
                        center +
                        lateral * (lateralUnit * halfWidth) +
                        surfaceNormal *
                        (surfaceClearance + crown + fineRipple));
                    normals.Add(surfaceNormal);
                    uv.Add(
                        new Vector2(
                            acrossT,
                            travelled * 0.12f));
                    colors.Add(
                        new Color(
                            u,
                            1f - edge,
                            definition.ProfileHeight > 0.045f
                                ? 1f
                                : 0f,
                            1f));
                }
            }

            for (var longitudinal = 0;
                 longitudinal < definition.LongitudinalSections - 1;
                 longitudinal++)
            {
                var row =
                    longitudinal * RibbonCrossSectionVertices;
                var nextRow =
                    row + RibbonCrossSectionVertices;
                for (var across = 0;
                     across < RibbonCrossSectionVertices - 1;
                     across++)
                {
                    var current = row + across;
                    var currentRight = current + 1;
                    var next = nextRow + across;
                    var nextRight = next + 1;
                    triangles.Add(current);
                    triangles.Add(currentRight);
                    triangles.Add(next);
                    triangles.Add(currentRight);
                    triangles.Add(nextRight);
                    triangles.Add(next);
                }
            }

            return WriteMeshAsset(
                assetName,
                vertices,
                normals,
                uv,
                colors,
                triangles,
                true);
        }

        private static Mesh WriteMeshAsset(
            string assetName,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uv,
            List<Color> colors,
            List<int> triangles,
            bool recalculateNormals)
        {
            var path = $"{DataRoot}/{assetName}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                var occupied =
                    AssetDatabase.LoadMainAssetAtPath(path);
                if (occupied != null)
                {
                    throw new InvalidOperationException(
                        $"Water mesh path is occupied by " +
                        $"{occupied.GetType().Name}: {path}");
                }

                mesh = new Mesh
                {
                    name = assetName
                };
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                mesh.Clear(false);
                mesh.name = assetName;
            }

            mesh.indexFormat =
                vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            if (recalculateNormals)
            {
                mesh.RecalculateNormals();
            }
            else
            {
                mesh.SetNormals(normals);
            }

            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        private static void CreateSection(
            Transform parent,
            string name,
            Mesh mesh,
            Material material)
        {
            var section = new GameObject(name);
            section.transform.SetParent(parent, false);
            section.transform.localPosition = Vector3.zero;
            section.transform.localRotation = Quaternion.identity;
            section.transform.localScale = Vector3.one;
            section.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = section.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.BlendProbes;
        }

        private static Vector3 CubicBezier(
            Vector3 start,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 end,
            float t)
        {
            var inverse = 1f - t;
            return inverse * inverse * inverse * start +
                   3f * inverse * inverse * t * controlA +
                   3f * inverse * t * t * controlB +
                   t * t * t * end;
        }

        private static Vector3 CubicBezierDerivative(
            Vector3 start,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 end,
            float t)
        {
            var inverse = 1f - t;
            return 3f * inverse * inverse * (controlA - start) +
                   6f * inverse * t * (controlB - controlA) +
                   3f * t * t * (end - controlB);
        }

        private static float SmootherStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value *
                   (value * (value * 6f - 15f) + 10f);
        }

        private static Transform FindDirectChild(
            Transform parent,
            string name)
        {
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (string.Equals(
                        child.name,
                        name,
                        StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var slash = folder.LastIndexOf('/');
            if (slash <= 0)
            {
                throw new InvalidOperationException(
                    $"Invalid Unity asset folder: {folder}");
            }

            var parent = folder.Substring(0, slash);
            var child = folder.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }

        private readonly struct RibbonDefinition
        {
            public RibbonDefinition(
                Vector3 start,
                Vector3 controlA,
                Vector3 controlB,
                Vector3 end,
                int longitudinalSections,
                float startHalfWidth,
                float endHalfWidth,
                float profileHeight,
                float phase)
            {
                Start = start;
                ControlA = controlA;
                ControlB = controlB;
                End = end;
                LongitudinalSections = longitudinalSections;
                StartHalfWidth = startHalfWidth;
                EndHalfWidth = endHalfWidth;
                ProfileHeight = profileHeight;
                Phase = phase;
            }

            public Vector3 Start { get; }
            public Vector3 ControlA { get; }
            public Vector3 ControlB { get; }
            public Vector3 End { get; }
            public int LongitudinalSections { get; }
            public float StartHalfWidth { get; }
            public float EndHalfWidth { get; }
            public float ProfileHeight { get; }
            public float Phase { get; }
        }
    }
}
