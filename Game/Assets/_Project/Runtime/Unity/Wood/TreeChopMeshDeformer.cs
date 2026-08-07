using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Unity.Wood
{
    /// <summary>
    /// Removes a millimetre-scale voxel field from the authored trunk and
    /// reconstructs the affected surface in the same mesh. The conformal
    /// boundary stays on the original bark while the duplicated inner surface
    /// receives an explicit wood/cambium material id. Rendering and physics
    /// always share the same runtime mesh; no decal or wound renderer exists.
    /// </summary>
    internal static class TreeChopVoxelCarver
    {
        private const float TargetMaximumWidth = 0.200f;
        private const float TargetMaximumHeight = 0.280f;
        private const float UnmeasuredWidth = 0.120f;
        private const float UnmeasuredHeight = 0.170f;
        private const float MinimumOpeningWidth = 0.060f;
        private const float MinimumOpeningHeight = 0.085f;
        // Keep the same stage progression and physical depth, but make every
        // visible opening twenty percent broader than the original pass.
        private const float OpeningFootprintScale = 1.20f;
        private const float OpeningTiltDegrees = 8f;
        // The surface pitch follows the opening so a large scar keeps the same
        // relative silhouette resolution as a small one inside one budget.
        private const float SurfacePitchPerWidth = 0.024f;
        private const float MinimumSurfacePitchWorld = 0.00125f;
        private const float MaximumSurfacePitchWorld = 0.0045f;
        private const float DepthVoxelPitchWorld = 0.00075f;
        // Authored CloudTall foliage has no wound stream; Unity supplies a
        // normal default TEXCOORD1 value for that missing channel. Runtime
        // wound vertices therefore carry an out-of-band signature so the
        // shader cannot confuse authored/default foliage data with a cut.
        private const float ChopDataSignature = 16f;
        private const int MaximumRefinementPasses = 8;
        private const int MaximumRuntimeTriangles = 24000;
        private static readonly float[] SectorRadii =
        {
            0.92f,
            0.98f,
            1.01f,
            1.00f,
            0.96f,
            0.91f,
            1.00f,
            1.02f,
            0.99f,
            1.00f,
            0.97f,
            1.06f,
            0.94f,
            1.07f,
            0.99f,
            0.96f
        };

        /// <summary>
        /// Per-impact telemetry. It walks the whole runtime mesh and formats
        /// several strings, which is a real cost inside the one frame an
        /// impact is allowed. Editor tooling and tests keep it; the running
        /// game never pays for it.
        /// </summary>
        private static bool Diagnostics => !Application.isPlaying;

        /// <summary>
        /// Carves the impact where the player actually struck. An impact that
        /// lands inside an existing opening deepens that opening; anywhere
        /// else on the trunk opens a new one. Every opening is rebuilt from
        /// the authored mesh on each impact, so earlier scars survive.
        /// </summary>
        public static bool Apply(
            FellableTreeIdentity tree,
            MeshCollider surfaceCollider,
            Vector3 point,
            Vector3 normal,
            int hitNumber)
        {
            if (!TryResolveTarget(
                    tree,
                    surfaceCollider,
                    out var filter,
                    out var owner))
            {
                return false;
            }

            BuildOpeningBasis(
                tree,
                normal,
                out var safeNormal,
                out var right,
                out var up);

            var opening = owner.FindOpening(point, safeNormal);
            if (opening >= 0)
            {
                owner.BeginDeepenOpening(opening);
            }
            else
            {
                owner.BeginNewOpening(
                    point,
                    safeNormal,
                    right,
                    up,
                    MeasureLocalSectionWidth(
                        surfaceCollider,
                        point,
                        safeNormal,
                        right));
            }

            if (!TryRebuild(tree, owner, filter, hitNumber))
            {
                owner.CancelPendingImpact();
                return false;
            }

            owner.CommitPendingImpact();
            return true;
        }

        /// <summary>
        /// Reverts the last committed impact, removing the opening it created
        /// or returning the deepened one to its previous stage.
        /// </summary>
        public static bool Undo(
            FellableTreeIdentity tree,
            MeshCollider surfaceCollider,
            int hitNumber)
        {
            if (!TryResolveTarget(
                    tree,
                    surfaceCollider,
                    out var filter,
                    out var owner) ||
                !owner.TryUndoLastImpact())
            {
                return false;
            }

            if (owner.OpeningCount == 0)
            {
                owner.RestoreAuthoredMesh();
                return true;
            }

            return TryRebuild(tree, owner, filter, hitNumber);
        }

        private static bool TryResolveTarget(
            FellableTreeIdentity tree,
            MeshCollider surfaceCollider,
            out MeshFilter filter,
            out TreeChopRuntimeMeshOwner owner)
        {
            filter = null;
            owner = null;
            if (tree == null || surfaceCollider == null)
            {
                return false;
            }

            var trunkObject = surfaceCollider.gameObject;
            filter = trunkObject.GetComponent<MeshFilter>();
            var renderer = trunkObject.GetComponent<MeshRenderer>();
            if (filter == null ||
                renderer == null ||
                filter.sharedMesh == null ||
                !filter.sharedMesh.isReadable)
            {
                Debug.LogError(
                    "Tree chopping requires the readable authored trunk " +
                    "mesh and its exact MeshCollider.",
                    tree);
                return false;
            }

            owner = trunkObject.GetComponent<
                TreeChopRuntimeMeshOwner>();
            if (owner == null)
            {
                owner = trunkObject.AddComponent<
                    TreeChopRuntimeMeshOwner>();
                owner.Initialize(filter, surfaceCollider);
            }

            return owner.OriginalMesh != null &&
                owner.OriginalMesh.isReadable;
        }

        private static void BuildOpeningBasis(
            FellableTreeIdentity tree,
            Vector3 normal,
            out Vector3 safeNormal,
            out Vector3 right,
            out Vector3 up)
        {
            safeNormal = normal.sqrMagnitude > 0.0001f
                ? normal.normalized
                : Vector3.forward;
            var baseUp = Vector3.ProjectOnPlane(
                tree.transform.up,
                safeNormal);
            if (baseUp.sqrMagnitude < 0.0001f)
            {
                baseUp = Vector3.ProjectOnPlane(
                    Vector3.up,
                    safeNormal);
            }

            if (baseUp.sqrMagnitude < 0.0001f)
            {
                baseUp = Vector3.ProjectOnPlane(
                    Vector3.right,
                    safeNormal);
            }

            baseUp.Normalize();
            var baseRight =
                Vector3.Cross(baseUp, safeNormal).normalized;
            var tilt = Quaternion.AngleAxis(
                OpeningTiltDegrees,
                safeNormal);
            right = (tilt * baseRight).normalized;
            up = (tilt * baseUp).normalized;
        }

        private static bool TryRebuild(
            FellableTreeIdentity tree,
            TreeChopRuntimeMeshOwner owner,
            MeshFilter filter,
            int hitNumber)
        {
            var watch = Diagnostics
                ? System.Diagnostics.Stopwatch.StartNew()
                : null;
            var openingCount = owner.OpeningCount;
            var frames = new GougeFrame[openingCount];
            for (var index = 0; index < openingCount; index++)
            {
                owner.GetOpening(
                    index,
                    out var center,
                    out var normal,
                    out var right,
                    out var up,
                    out var sectionWidth,
                    out var stage);
                ResolveOpeningSize(
                    sectionWidth,
                    stage,
                    out var width,
                    out var height,
                    out var depth);
                frames[index] = new GougeFrame(
                    filter.transform,
                    center,
                    normal,
                    right,
                    up,
                    width,
                    height,
                    depth);
            }

            var pitches = new float[openingCount];
            for (var index = 0; index < openingCount; index++)
            {
                pitches[index] = ResolveSurfacePitch(frames[index]);
            }

            var data = new MeshBuildData(owner.OriginalMesh);
            for (var pass = 0; pass < MaximumRefinementPasses; pass++)
            {
                if (!data.RefineNear(frames, pitches))
                {
                    break;
                }
            }

            var carvedTriangles = 0;
            var maximumDepthWorld = 0f;
            for (var index = 0; index < openingCount; index++)
            {
                var carve = data.CarveVoxelColumns(
                    frames[index],
                    DepthVoxelPitchWorld);
                carvedTriangles += carve.CarvedTriangles;
                maximumDepthWorld = Mathf.Max(
                    maximumDepthWorld,
                    carve.MaximumDepthWorld);
            }

            if (maximumDepthWorld <= 0.003f || carvedTriangles <= 0)
            {
                Debug.LogError(
                    "Tree voxel subtraction produced no physical surface; " +
                    "the impact is not committed.",
                    tree);
                return false;
            }

            var runtimeMesh = data.CreateMesh(
                $"MESH_WOOD_VoxelCarvedTrunk_{hitNumber:00}");
            owner.Replace(runtimeMesh);
            var primary = SelectPrimaryFrame(frames);

            if (Diagnostics)
            {
                Debug.Log(
                    "TREE_CHOP_VOXEL " +
                    $"tree={tree.name} hit={hitNumber} " +
                    $"openings={openingCount} " +
                    $"width={primary.WidthWorld:F4} " +
                    $"height={primary.HeightWorld:F4} " +
                    $"depth={primary.DepthWorld:F4} " +
                    $"surfacePitch={ResolveSurfacePitch(primary):F5} " +
                    $"depthVoxel={DepthVoxelPitchWorld:F5} " +
                    $"carvedTriangles={carvedTriangles} " +
                    $"vertices={runtimeMesh.vertexCount} " +
                    $"triangles={data.TriangleCount} " +
                    $"maxDepth={maximumDepthWorld:F4} " +
                    $"milliseconds={watch.Elapsed.TotalMilliseconds:F1}",
                    tree);
            }

            return true;
        }

        /// <summary>
        /// The opening an impact belongs to grows with its stage. The trunk
        /// section keeps a thin trunk from being cut in half by a scar sized
        /// for a mature one.
        /// </summary>
        internal static void ResolveOpeningSize(
            float sectionWidth,
            int stage,
            out float width,
            out float height,
            out float depth)
        {
            var progress = Mathf.Clamp01(
                (stage - 1f) /
                (FellableTreeIdentity.HitsRequired - 1f));
            var measured = sectionWidth > 0.015f;
            var maximumWidth = measured
                ? Mathf.Min(
                    TargetMaximumWidth,
                    sectionWidth * 0.68f)
                : UnmeasuredWidth;
            var maximumHeight = measured
                ? Mathf.Min(
                    TargetMaximumHeight,
                    sectionWidth * 0.98f)
                : UnmeasuredHeight;
            width = Mathf.Max(
                MinimumOpeningWidth,
                maximumWidth * Mathf.Lerp(0.55f, 1f, progress)) *
                OpeningFootprintScale;
            height = Mathf.Max(
                MinimumOpeningHeight,
                maximumHeight * Mathf.Lerp(0.52f, 1f, progress)) *
                OpeningFootprintScale;
            var requestedDepth = Mathf.Lerp(0.012f, 0.030f, progress);
            depth = measured
                ? Mathf.Min(requestedDepth, sectionWidth * 0.09f)
                : requestedDepth;
        }

        private static float ResolveSurfacePitch(GougeFrame frame)
        {
            return Mathf.Clamp(
                frame.WidthWorld * SurfacePitchPerWidth,
                MinimumSurfacePitchWorld,
                MaximumSurfacePitchWorld);
        }

        private static GougeFrame SelectPrimaryFrame(GougeFrame[] frames)
        {
            var primary = frames[0];
            for (var index = 1; index < frames.Length; index++)
            {
                if (frames[index].WidthWorld * frames[index].HeightWorld >
                    primary.WidthWorld * primary.HeightWorld)
                {
                    primary = frames[index];
                }
            }

            return primary;
        }

        private static float MeasureLocalSectionWidth(
            Collider collider,
            Vector3 point,
            Vector3 normal,
            Vector3 tangent)
        {
            // The probe has to reach past a mature trunk, otherwise every
            // section saturates at the same value and the opening cannot
            // grow with the tree it is cut into.
            const float step = 0.0025f;
            const float maximumSide = 0.16f;
            return MeasureContiguousSide(
                       collider,
                       point,
                       normal,
                       tangent,
                       step,
                       maximumSide) +
                   MeasureContiguousSide(
                       collider,
                       point,
                       normal,
                       -tangent,
                       step,
                       maximumSide);
        }

        private static float GetNormalizedRadius(float nx, float ny)
        {
            // A deterministic, vertically oriented torn outline. The small
            // lateral drift prevents bilateral symmetry; the sector table
            // creates irregular bark bites without random detached voxels.
            var shiftedX = nx -
                Mathf.Sin(ny * 5.7f + 0.35f) * 0.042f -
                Mathf.Sin(ny * 11.3f - 0.4f) * 0.018f;
            var bottomCompression = 1f +
                SmoothThreshold(0.55f, 1f, -ny) * 0.12f;
            var shapedY = ny * bottomCompression;
            var angle = Mathf.Atan2(shapedY, shiftedX);
            var sectorFloat = Mathf.Clamp(
                (angle + Mathf.PI) *
                (SectorRadii.Length / (Mathf.PI * 2f)),
                0f,
                SectorRadii.Length - 0.0001f);
            var sector = Mathf.FloorToInt(sectorFloat);
            var next = (sector + 1) % SectorRadii.Length;
            var radiusLimit = Mathf.Lerp(
                SectorRadii[sector],
                SectorRadii[next],
                sectorFloat - sector);
            return Mathf.Sqrt(
                    shiftedX * shiftedX + shapedY * shapedY) /
                radiusLimit;
        }

        private static float MeasureContiguousSide(
            Collider collider,
            Vector3 point,
            Vector3 normal,
            Vector3 direction,
            float step,
            float maximumSide)
        {
            const float rayStart = 0.075f;
            var lastHitOffset = 0f;
            for (var offset = step;
                 offset <= maximumSide + 0.0001f;
                 offset += step)
            {
                var nominal = point + direction * offset;
                var ray = new Ray(
                    nominal + normal * rayStart,
                    -normal);
                if (!collider.Raycast(
                        ray,
                        out var hit,
                        rayStart * 2.35f))
                {
                    break;
                }

                var normalSeparation = Mathf.Abs(Vector3.Dot(
                    hit.point - point,
                    normal));
                if (normalSeparation > 0.065f)
                {
                    break;
                }

                lastHitOffset = offset;
            }

            return lastHitOffset;
        }

        private readonly struct GougeFrame
        {
            public GougeFrame(
                Transform transform,
                Vector3 center,
                Vector3 normal,
                Vector3 right,
                Vector3 up,
                float width,
                float height,
                float depth)
            {
                Transform = transform;
                // Refinement and carving measure thousands of world lengths.
                // Going through the Transform for each one costs a managed to
                // native call; the cached matrix keeps that math in managed
                // code with identical results.
                LocalToWorld = transform.localToWorldMatrix;
                WidthWorld = width;
                HeightWorld = height;
                DepthWorld = depth;
                CenterOS = transform.InverseTransformPoint(center);
                NormalOS = transform
                    .InverseTransformDirection(normal).normalized;
                RightOS = transform
                    .InverseTransformDirection(right).normalized;
                UpOS = transform
                    .InverseTransformDirection(up).normalized;
                HalfWidthOS = LocalDistance(
                    transform,
                    center,
                    right,
                    width * 0.5f);
                HalfHeightOS = LocalDistance(
                    transform,
                    center,
                    up,
                    height * 0.5f);
                DepthOS = LocalDistance(
                    transform,
                    center,
                    normal,
                    depth);
                // The shell has to reach behind the rim of the opening or a
                // wide scar is clipped into an oval by the trunk curvature.
                var frontShellWorld = Mathf.Max(
                    depth + 0.004f,
                    Mathf.Max(0.012f, width * 0.21f));
                FrontShellOS = LocalDistance(
                    transform,
                    center,
                    normal,
                    frontShellWorld);
                FrontAllowanceOS = LocalDistance(
                    transform,
                    center,
                    normal,
                    0.004f);
                FullDepthWorld = LocalToWorld
                    .MultiplyVector(NormalOS * DepthOS).magnitude;
            }

            public Transform Transform { get; }
            public Matrix4x4 LocalToWorld { get; }
            public float FullDepthWorld { get; }
            public float WidthWorld { get; }
            public float HeightWorld { get; }
            public float DepthWorld { get; }
            public Vector3 CenterOS { get; }
            public Vector3 NormalOS { get; }
            public Vector3 RightOS { get; }
            public Vector3 UpOS { get; }
            public float HalfWidthOS { get; }
            public float HalfHeightOS { get; }
            public float DepthOS { get; }
            public float FrontShellOS { get; }
            public float FrontAllowanceOS { get; }
        }

        private readonly struct VoxelCarveResult
        {
            public VoxelCarveResult(
                int carvedTriangles,
                float maximumDepthWorld)
            {
                CarvedTriangles = carvedTriangles;
                MaximumDepthWorld = maximumDepthWorld;
            }

            public int CarvedTriangles { get; }
            public float MaximumDepthWorld { get; }
        }

        private sealed class MeshBuildData
        {
            private readonly Bounds _authoredBounds;
            private readonly List<Vector3> _normals;
            private readonly List<Vector2> _uv;
            private readonly List<Color32> _colors;
            private readonly List<Vector4> _chopData;
            private readonly List<float> _deformation;
            private List<int>[] _subMeshTriangles;

            public MeshBuildData(Mesh source)
            {
                // Keep the authored centre stable. MeshRenderer uses its
                // bounds when resolving per-object probe data; recalculating
                // them after the first cut can otherwise make the whole trunk
                // change illumination. The symmetric margin only protects the
                // sub-millimetre raised bark tongues from frustum clipping.
                var authoredBounds = source.bounds;
                authoredBounds.Expand(0.004f);
                _authoredBounds = authoredBounds;
                Vertices = new List<Vector3>(source.vertices);
                var sourceNormals = source.normals;
                _normals = sourceNormals.Length == source.vertexCount
                    ? new List<Vector3>(sourceNormals)
                    : CreateDefaultNormals(source.vertexCount);
                var sourceUv = source.uv;
                _uv = sourceUv.Length == source.vertexCount
                    ? new List<Vector2>(sourceUv)
                    : CreateDefaultUv(source.vertexCount);
                var sourceColors = source.colors32;
                _colors = sourceColors.Length == source.vertexCount
                    ? new List<Color32>(sourceColors)
                    : CreateDefaultColors(source.vertexCount);
                _chopData = CreateDefaultChopData(source.vertexCount);
                _deformation = new List<float>(source.vertexCount);
                for (var index = 0; index < source.vertexCount; index++)
                {
                    _deformation.Add(0f);
                }

                _subMeshTriangles = new List<int>[source.subMeshCount];
                for (var subMesh = 0;
                     subMesh < source.subMeshCount;
                     subMesh++)
                {
                    _subMeshTriangles[subMesh] = new List<int>(
                        source.GetTriangles(subMesh));
                }
            }

            public List<Vector3> Vertices { get; }

            public int TriangleCount
            {
                get
                {
                    var total = 0;
                    for (var subMesh = 0;
                         subMesh < _subMeshTriangles.Length;
                         subMesh++)
                    {
                        total += _subMeshTriangles[subMesh].Count / 3;
                    }

                    return total;
                }
            }

            /// <summary>
            /// Marks and splits the edges every opening still needs in one
            /// sweep. Sweeping once per opening would rescan the whole trunk
            /// as many times as the tree has scars.
            /// </summary>
            public bool RefineNear(
                GougeFrame[] frames,
                float[] maximumEdgeWorld)
            {
                var splitEdges = new HashSet<ulong>();
                for (var subMesh = 0;
                     subMesh < _subMeshTriangles.Length;
                     subMesh++)
                {
                    var triangles = _subMeshTriangles[subMesh];
                    for (var index = 0;
                         index < triangles.Count;
                         index += 3)
                    {
                        for (var opening = 0;
                             opening < frames.Length;
                             opening++)
                        {
                            var frame = frames[opening];
                            var pitch = maximumEdgeWorld[opening];
                            MarkContainingTriangle(
                                triangles[index],
                                triangles[index + 1],
                                triangles[index + 2],
                                frame,
                                pitch,
                                splitEdges);
                            MarkEdge(
                                triangles[index],
                                triangles[index + 1],
                                frame,
                                pitch,
                                splitEdges);
                            MarkEdge(
                                triangles[index + 1],
                                triangles[index + 2],
                                frame,
                                pitch,
                                splitEdges);
                            MarkEdge(
                                triangles[index + 2],
                                triangles[index],
                                frame,
                                pitch,
                                splitEdges);
                        }
                    }
                }

                if (splitEdges.Count == 0)
                {
                    return false;
                }

                var candidateTriangleCount = 0;
                for (var subMesh = 0;
                     subMesh < _subMeshTriangles.Length;
                     subMesh++)
                {
                    var triangles = _subMeshTriangles[subMesh];
                    for (var index = 0;
                         index < triangles.Count;
                         index += 3)
                    {
                        var a = triangles[index];
                        var b = triangles[index + 1];
                        var c = triangles[index + 2];
                        var splitCount =
                            (splitEdges.Contains(EdgeKey(a, b)) ? 1 : 0) +
                            (splitEdges.Contains(EdgeKey(b, c)) ? 1 : 0) +
                            (splitEdges.Contains(EdgeKey(c, a)) ? 1 : 0);
                        candidateTriangleCount += 1 + splitCount;
                    }
                }

                if (candidateTriangleCount > MaximumRuntimeTriangles)
                {
                    Debug.LogWarning(
                        "Tree voxel refinement stopped before exceeding " +
                        $"the {MaximumRuntimeTriangles} triangle budget.");
                    return false;
                }

                var midpoints = new Dictionary<ulong, int>();
                var refined = new List<int>[_subMeshTriangles.Length];
                for (var subMesh = 0;
                     subMesh < _subMeshTriangles.Length;
                     subMesh++)
                {
                    var source = _subMeshTriangles[subMesh];
                    var destination = new List<int>(source.Count * 2);
                    for (var index = 0;
                         index < source.Count;
                         index += 3)
                    {
                        RefineTriangle(
                            source[index],
                            source[index + 1],
                            source[index + 2],
                            splitEdges,
                            midpoints,
                            destination);
                    }

                    refined[subMesh] = destination;
                }

                _subMeshTriangles = refined;
                return true;
            }

            /// <summary>
            /// Treats the refined front shell as columns of a local voxel
            /// field. Triangles selected by the field are replaced with a
            /// separate inner surface. Boundary vertices are duplicated at
            /// exactly the same positions, producing a hard bark/wood seam
            /// without a crack while kept bark vertices retain authored UVs,
            /// colours and normals byte-for-byte.
            /// </summary>
            public VoxelCarveResult CarveVoxelColumns(
                GougeFrame frame,
                float voxelPitchWorld)
            {
                var sourceVertexCount = Vertices.Count;
                var cutBySubMesh = new bool[_subMeshTriangles.Length][];
                var usedByCut = new bool[sourceVertexCount];
                var usedByKept = new bool[sourceVertexCount];
                var carvedTriangles = 0;

                for (var subMesh = 0;
                     subMesh < _subMeshTriangles.Length;
                     subMesh++)
                {
                    var triangles = _subMeshTriangles[subMesh];
                    var flags = new bool[triangles.Count / 3];
                    for (var index = 0;
                         index < triangles.Count;
                         index += 3)
                    {
                        var a = triangles[index];
                        var b = triangles[index + 1];
                        var c = triangles[index + 2];
                        var cut = IsVoxelCutTriangle(a, b, c, frame);
                        flags[index / 3] = cut;
                        var usage = cut ? usedByCut : usedByKept;
                        usage[a] = true;
                        usage[b] = true;
                        usage[c] = true;
                        if (cut)
                        {
                            carvedTriangles++;
                        }
                    }

                    cutBySubMesh[subMesh] = flags;
                }

                var boundary = new bool[sourceVertexCount];
                for (var index = 0; index < sourceVertexCount; index++)
                {
                    boundary[index] = usedByCut[index] && usedByKept[index];
                }

                var duplicateBySource = new Dictionary<int, int>();
                var rebuilt = new List<int>[_subMeshTriangles.Length];
                var maximumDepthWorld = 0f;
                for (var subMesh = 0;
                     subMesh < _subMeshTriangles.Length;
                     subMesh++)
                {
                    var source = _subMeshTriangles[subMesh];
                    var flags = cutBySubMesh[subMesh];
                    var destination = new List<int>(source.Count);
                    for (var index = 0;
                         index < source.Count;
                         index += 3)
                    {
                        if (!flags[index / 3])
                        {
                            AddTriangle(
                                destination,
                                source[index],
                                source[index + 1],
                                source[index + 2]);
                            continue;
                        }

                        var a = GetOrCreateVoxelVertex(
                            source[index],
                            boundary[source[index]],
                            frame,
                            voxelPitchWorld,
                            duplicateBySource,
                            ref maximumDepthWorld);
                        var b = GetOrCreateVoxelVertex(
                            source[index + 1],
                            boundary[source[index + 1]],
                            frame,
                            voxelPitchWorld,
                            duplicateBySource,
                            ref maximumDepthWorld);
                        var c = GetOrCreateVoxelVertex(
                            source[index + 2],
                            boundary[source[index + 2]],
                            frame,
                            voxelPitchWorld,
                            duplicateBySource,
                            ref maximumDepthWorld);
                        AddTriangle(destination, a, b, c);
                    }

                    rebuilt[subMesh] = destination;
                }

                _subMeshTriangles = rebuilt;
                if (!Diagnostics)
                {
                    return new VoxelCarveResult(
                        carvedTriangles,
                        maximumDepthWorld);
                }

                var materialVertices = 0;
                var minimumNx = float.PositiveInfinity;
                var maximumNx = float.NegativeInfinity;
                var minimumNy = float.PositiveInfinity;
                var maximumNy = float.NegativeInfinity;
                var radiusBins = new int[5];
                var materialBins = new int[5];
                for (var index = sourceVertexCount;
                     index < Vertices.Count;
                     index++)
                {
                    var data = _chopData[index];
                    var materialId =
                        data.w >= ChopDataSignature + 0.5f
                            ? data.w - ChopDataSignature
                            : 0f;
                    var radius = GetNormalizedRadius(data.x, data.y);
                    var bin = Mathf.Clamp(
                        Mathf.FloorToInt(radius * radiusBins.Length),
                        0,
                        radiusBins.Length - 1);
                    radiusBins[bin]++;
                    if (materialId < 0.5f)
                    {
                        materialBins[0]++;
                        continue;
                    }

                    materialVertices++;
                    if (materialId < 1.5f)
                    {
                        materialBins[1]++;
                    }
                    else if (materialId < 2.9f)
                    {
                        materialBins[2]++;
                    }
                    else if (materialId < 3.5f)
                    {
                        materialBins[3]++;
                    }
                    else
                    {
                        materialBins[4]++;
                    }
                    minimumNx = Mathf.Min(minimumNx, data.x);
                    maximumNx = Mathf.Max(maximumNx, data.x);
                    minimumNy = Mathf.Min(minimumNy, data.y);
                    maximumNy = Mathf.Max(maximumNy, data.y);
                }

                Debug.Log(
                    "TREE_CHOP_VOXEL_FIELD " +
                    $"materialVertices={materialVertices} " +
                    $"nx=[{minimumNx:F3},{maximumNx:F3}] " +
                    $"ny=[{minimumNy:F3},{maximumNy:F3}] " +
                    $"radiusBins=[{string.Join(",", radiusBins)}] " +
                    $"materials=[{string.Join(",", materialBins)}]");
                return new VoxelCarveResult(
                    carvedTriangles,
                    maximumDepthWorld);
            }

            private bool IsVoxelCutTriangle(
                int a,
                int b,
                int c,
                GougeFrame frame)
            {
                var center = (Vertices[a] + Vertices[b] + Vertices[c]) /
                    3f;
                var delta = center - frame.CenterOS;
                var shell = Vector3.Dot(delta, frame.NormalOS);
                if (shell < -frame.FrontShellOS ||
                    shell > frame.FrontAllowanceOS)
                {
                    return false;
                }

                var nx = Vector3.Dot(delta, frame.RightOS) /
                    frame.HalfWidthOS;
                var ny = Vector3.Dot(delta, frame.UpOS) /
                    frame.HalfHeightOS;
                var minimumRadius = GetNormalizedRadius(nx, ny);
                minimumRadius = Mathf.Min(
                    minimumRadius,
                    GetVertexRadius(a, frame));
                minimumRadius = Mathf.Min(
                    minimumRadius,
                    GetVertexRadius(b, frame));
                minimumRadius = Mathf.Min(
                    minimumRadius,
                    GetVertexRadius(c, frame));
                return minimumRadius < 1.01f;
            }

            private float GetVertexRadius(int index, GougeFrame frame)
            {
                var delta = Vertices[index] - frame.CenterOS;
                var nx = Vector3.Dot(delta, frame.RightOS) /
                    frame.HalfWidthOS;
                var ny = Vector3.Dot(delta, frame.UpOS) /
                    frame.HalfHeightOS;
                return GetNormalizedRadius(nx, ny);
            }

            private int GetOrCreateVoxelVertex(
                int sourceIndex,
                bool isBoundary,
                GougeFrame frame,
                float voxelPitchWorld,
                IDictionary<int, int> duplicateBySource,
                ref float maximumDepthWorld)
            {
                if (duplicateBySource.TryGetValue(
                        sourceIndex,
                        out var existing))
                {
                    return existing;
                }

                var source = Vertices[sourceIndex];
                var delta = source - frame.CenterOS;
                var nx = Vector3.Dot(delta, frame.RightOS) /
                    frame.HalfWidthOS;
                var ny = Vector3.Dot(delta, frame.UpOS) /
                    frame.HalfHeightOS;
                var radius = GetNormalizedRadius(nx, ny);
                var edge = Mathf.Clamp01(1f - radius);
                // Triangle adjacency can cross a UV seam or a hard-normal
                // split well inside the opening. Only the geometric collar is
                // a true weld boundary; interior duplicates must still be
                // removed by the voxel field.
                var fullDepthWorld = frame.FullDepthWorld;

                var flap = GetAttachedBarkFlap(
                    nx,
                    ny,
                    edge,
                    out var flapLiftWorld);
                var barkBreakRadius = GetBarkBreakRadius(nx, ny);
                var materialId = 0f;
                var signedOffsetWorld = 0f;
                if (!isBoundary && flap > 0.12f)
                {
                    // A bark tongue remains on the authored shell. It is
                    // never pushed outside the trunk: the cavity behind it
                    // provides the physical separation without opening a
                    // background-visible seam.
                    signedOffsetWorld = -flapLiftWorld * flap;
                    materialId = 4f;
                }
                else if (!isBoundary)
                {
                    var layerFactor = GetVoxelLayerDepth(
                        nx,
                        ny,
                        out var woodMaterialId);
                    var edgeFalloff = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.025f, 0.30f, edge));
                    var depthWorld = fullDepthWorld *
                        layerFactor * edgeFalloff;
                    var angle = Mathf.Atan2(ny, nx);
                    var rimPhase = Mathf.Sin(
                        angle * 5.5f + ny * 4.2f - nx * 2.1f);
                    if (radius > barkBreakRadius)
                    {
                        // The entire outer collar is exactly the authored
                        // birch surface: no displacement, changed normal or
                        // tint is allowed to form a patch-shaped halo.
                        depthWorld = 0f;
                        materialId = 0f;
                    }
                    else if (IsBrokenGroove(nx, ny))
                    {
                        depthWorld += voxelPitchWorld * 0.75f;
                        materialId = 3f;
                    }
                    else
                    {
                        materialId = radius > barkBreakRadius - 0.10f &&
                            rimPhase > 0.25f
                            ? 1f
                            : woodMaterialId;
                        if (materialId > 1.5f)
                        {
                            depthWorld = Mathf.Max(
                                0.0012f,
                                depthWorld);
                        }
                    }

                    depthWorld = Mathf.Min(fullDepthWorld, depthWorld);
                    // The volume is represented by millimetre voxel columns;
                    // the conformal triangulation hides the grid while keeping
                    // deterministic, genuinely stepped chisel planes.
                    depthWorld = Mathf.Round(
                        depthWorld / voxelPitchWorld) * voxelPitchWorld;
                    signedOffsetWorld = depthWorld;
                    maximumDepthWorld = Mathf.Max(
                        maximumDepthWorld,
                        depthWorld);
                }
                else
                {
                    // A topological cut/kept boundary is immutable. Keeping
                    // its authored material attributes as well as its exact
                    // position makes the two triangle sets visually weld.
                    // A connected flap may still retain/lighten that same
                    // authored bark sample; it never changes the geometry.
                    materialId = flap > 0.08f ? 4f : 0f;
                }

                var offsetOS = fullDepthWorld > 0.000001f
                    ? frame.DepthOS *
                      (signedOffsetWorld / fullDepthWorld)
                    : 0f;
                var carved = source - frame.NormalOS * offsetOS;
                var newIndex = Vertices.Count;
                Vertices.Add(carved);
                _normals.Add(_normals[sourceIndex]);
                _uv.Add(_uv[sourceIndex]);
                _colors.Add(_colors[sourceIndex]);
                _chopData.Add(new Vector4(
                    nx,
                    ny,
                    materialId > 3.5f
                        ? -flap
                        : fullDepthWorld > 0.000001f
                        ? Mathf.Max(0f, signedOffsetWorld) /
                          fullDepthWorld
                        : 0f,
                    materialId > 0f
                        ? ChopDataSignature + materialId
                        : 0f));
                _deformation.Add(isBoundary || materialId < 0.5f
                    ? 0f
                    : materialId > 3.5f
                        ? flap * 0.35f
                        : SmoothThreshold(0.08f, 0.32f, edge));
                duplicateBySource.Add(sourceIndex, newIndex);
                return newIndex;
            }

            private static float GetVoxelLayerDepth(
                float nx,
                float ny,
                out float materialId)
            {
                // Four diagonal chisel scales overlap like short axe chips.
                // Each remains local and covers less than a third of the
                // opening; no region converges on a radial centre.
                var radius = GetNormalizedRadius(nx, ny);
                var interior = Mathf.Clamp01(
                    Mathf.InverseLerp(0.84f, 0.05f, radius));
                var baseDepth = Mathf.Lerp(
                    0.16f,
                    0.24f,
                    Mathf.Pow(interior, 1.25f));
                var depth = baseDepth;
                materialId = 2.2f;
                ApplyChiselScale(
                    ChiselScaleMask(
                        nx, ny, -0.165f, 0.417f,
                        0.362f, 0.116f, 18f),
                    baseDepth, 0.273f, 2.0f,
                    ref depth, ref materialId);
                ApplyChiselScale(
                    ChiselScaleMask(
                        nx, ny, 0.132f, 0.139f,
                        0.428f, 0.139f, 22f),
                    baseDepth, 0.491f, 2.2f,
                    ref depth, ref materialId);
                ApplyChiselScale(
                    ChiselScaleMask(
                        nx, ny, -0.099f, -0.185f,
                        0.329f, 0.104f, 26f),
                    baseDepth, 0.709f, 2.4f,
                    ref depth, ref materialId);
                ApplyChiselScale(
                    ChiselScaleMask(
                        nx, ny, 0.198f, -0.509f,
                        0.264f, 0.093f, 20f),
                    baseDepth, 0.964f, 2.6f,
                    ref depth, ref materialId);
                var surfaceBreak =
                    Mathf.Sin(nx * 9.3f - ny * 5.2f + 0.6f) *
                    SmoothThreshold(0.18f, 0.70f, interior) *
                    0.012f;
                return Mathf.Clamp(
                    depth + surfaceBreak,
                    0.27f,
                    0.96f);
            }

            private static void ApplyChiselScale(
                float mask,
                float baseDepth,
                float targetDepth,
                float candidateMaterial,
                ref float depth,
                ref float materialId)
            {
                var candidateDepth = Mathf.Lerp(
                    baseDepth,
                    targetDepth,
                    mask);
                if (candidateDepth <= depth)
                {
                    return;
                }

                depth = candidateDepth;
                materialId = candidateMaterial;
            }

            private static float ChiselScaleMask(
                float nx,
                float ny,
                float centerX,
                float centerY,
                float halfLength,
                float halfWidth,
                float angleDegrees)
            {
                var radians = angleDegrees * Mathf.Deg2Rad;
                var cosine = Mathf.Cos(radians);
                var sine = Mathf.Sin(radians);
                var dx = nx - centerX;
                var dy = ny - centerY;
                var along = dx * cosine + dy * sine;
                var across = -dx * sine + dy * cosine;
                across += Mathf.Sin(
                    along * 17f + centerY * 9f) * 0.018f;
                var distance = Mathf.Max(
                    Mathf.Abs(along) / halfLength,
                    Mathf.Abs(across) / halfWidth);
                return 1f - SmoothThreshold(0.70f, 1f, distance);
            }

            private static float GetBarkBreakRadius(float nx, float ny)
            {
                var angle = Mathf.Atan2(ny, nx);
                return 0.73f +
                    Mathf.Sin(angle * 3f + 0.5f) * 0.025f +
                    Mathf.Sin(angle * 7f - ny * 1.7f) * 0.018f;
            }

            private static float GetAttachedBarkFlap(
                float nx,
                float ny,
                float edge,
                out float liftWorld)
            {
                // Three short tongues remain connected to the outer shell.
                // They sit at the original bark position; the surrounding
                // 2-5 mm cavity makes them physically stand proud without
                // ever floating outside the trunk or opening a seam.
                var top = ConnectedFlap(
                    ny,
                    nx,
                    0.44f,
                    0.02f,
                    0.08f);
                var upperRight = ConnectedFlap(
                    (nx + ny) * 0.7071068f,
                    (nx - ny) * 0.7071068f,
                    0.49f,
                    0.02f,
                    0.075f);
                var lowerLeft = ConnectedFlap(
                    (-nx - ny) * 0.7071068f,
                    (-nx + ny) * 0.7071068f,
                    0.56f,
                    -0.01f,
                    0.065f);
                var rimWeight = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.15f, 0.55f, edge));
                var flap = top;
                liftWorld = 0.0012f;
                if (upperRight > flap)
                {
                    flap = upperRight;
                    liftWorld = 0.0009f;
                }

                if (lowerLeft > flap)
                {
                    flap = lowerLeft;
                    liftWorld = 0.0006f;
                }

                return flap * rimWeight;
            }

            private static float ConnectedFlap(
                float radial,
                float across,
                float radialStart,
                float acrossCenter,
                float halfWidth)
            {
                var root = SmoothThreshold(
                    radialStart,
                    radialStart + 0.08f,
                    radial);
                var tip = 1f - SmoothThreshold(
                    0.72f,
                    0.80f,
                    radial);
                var lateral = 1f - SmoothThreshold(
                    halfWidth * 0.70f,
                    halfWidth,
                    Mathf.Abs(across - acrossCenter));
                return root * tip * lateral;
            }

            private static bool IsBrokenGroove(float nx, float ny)
            {
                var upper = ny > 0.18f &&
                    ny < 0.40f &&
                    Mathf.Abs(nx + 0.28f - ny * 0.25f) < 0.028f;
                var middle = ny > -0.12f &&
                    ny < 0.02f &&
                    Mathf.Abs(nx - 0.24f - ny * 0.10f) < 0.026f;
                var lower = ny > -0.48f &&
                    ny < -0.28f &&
                    Mathf.Abs(nx + 0.12f + ny * 0.30f) < 0.028f;
                return upper || middle || lower;
            }

            public Mesh CreateMesh(string meshName)
            {
                var mesh = new Mesh
                {
                    name = meshName,
                    indexFormat = Vertices.Count > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, _uv);
                mesh.SetUVs(1, _chopData);
                mesh.SetColors(_colors);
                mesh.subMeshCount = _subMeshTriangles.Length;
                for (var subMesh = 0;
                     subMesh < _subMeshTriangles.Length;
                     subMesh++)
                {
                    mesh.SetTriangles(
                        _subMeshTriangles[subMesh],
                        subMesh,
                        false);
                }

                mesh.bounds = _authoredBounds;
                mesh.RecalculateNormals();
                var geometricNormals = mesh.normals;
                for (var index = 0;
                     index < geometricNormals.Length;
                     index++)
                {
                    var blend = Mathf.SmoothStep(
                        0f,
                        1f,
                        _deformation[index]);
                    geometricNormals[index] = Vector3.Slerp(
                        _normals[index],
                        geometricNormals[index],
                        blend).normalized;
                }

                mesh.SetNormals(geometricNormals);
                return mesh;
            }

            private void MarkEdge(
                int a,
                int b,
                GougeFrame frame,
                float maximumEdgeWorld,
                ISet<ulong> splitEdges)
            {
                var vertexA = Vertices[a];
                var vertexB = Vertices[b];
                var worldLength = frame.LocalToWorld
                    .MultiplyVector(vertexB - vertexA).magnitude;

                var aDelta = vertexA - frame.CenterOS;
                var bDelta = vertexB - frame.CenterOS;
                var a2 = new Vector2(
                    Vector3.Dot(aDelta, frame.RightOS) /
                        frame.HalfWidthOS,
                    Vector3.Dot(aDelta, frame.UpOS) /
                        frame.HalfHeightOS);
                var b2 = new Vector2(
                    Vector3.Dot(bDelta, frame.RightOS) /
                        frame.HalfWidthOS,
                    Vector3.Dot(bDelta, frame.UpOS) /
                        frame.HalfHeightOS);
                var direction = b2 - a2;
                var denominator = direction.sqrMagnitude;
                var t = denominator > 0.000001f
                    ? Mathf.Clamp01(-Vector2.Dot(a2, direction) /
                        denominator)
                    : 0f;
                var nearest = Vector2.Lerp(a2, b2, t);
                var radius = GetNormalizedRadius(nearest.x, nearest.y);
                var boundaryBand = 1f - SmoothThreshold(
                    0.055f,
                    0.14f,
                    Mathf.Abs(radius - 1f));
                var interiorPitch = maximumEdgeWorld * 3.5f;
                var exteriorPitch = maximumEdgeWorld * 5.0f;
                var targetPitch = radius < 1f
                    ? Mathf.Lerp(interiorPitch, maximumEdgeWorld, boundaryBand)
                    : Mathf.Lerp(exteriorPitch, maximumEdgeWorld, boundaryBand);
                if (worldLength <= targetPitch)
                {
                    return;
                }

                if (nearest.sqrMagnitude > 1.45f)
                {
                    return;
                }

                var shellA = Vector3.Dot(
                    aDelta,
                    frame.NormalOS);
                var shellB = Vector3.Dot(
                    bDelta,
                    frame.NormalOS);
                var nearestShell = Mathf.Lerp(shellA, shellB, t);
                if (nearestShell < -frame.FrontShellOS ||
                    nearestShell > frame.FrontAllowanceOS)
                {
                    return;
                }

                splitEdges.Add(EdgeKey(a, b));
            }

            private void MarkContainingTriangle(
                int a,
                int b,
                int c,
                GougeFrame frame,
                float maximumEdgeWorld,
                ISet<ulong> splitEdges)
            {
                var aDelta = Vertices[a] - frame.CenterOS;
                var bDelta = Vertices[b] - frame.CenterOS;
                var cDelta = Vertices[c] - frame.CenterOS;
                var a2 = new Vector2(
                    Vector3.Dot(aDelta, frame.RightOS) /
                        frame.HalfWidthOS,
                    Vector3.Dot(aDelta, frame.UpOS) /
                        frame.HalfHeightOS);
                var b2 = new Vector2(
                    Vector3.Dot(bDelta, frame.RightOS) /
                        frame.HalfWidthOS,
                    Vector3.Dot(bDelta, frame.UpOS) /
                        frame.HalfHeightOS);
                var c2 = new Vector2(
                    Vector3.Dot(cDelta, frame.RightOS) /
                        frame.HalfWidthOS,
                    Vector3.Dot(cDelta, frame.UpOS) /
                        frame.HalfHeightOS);
                if (!TryBarycentricAtOrigin(
                        a2,
                        b2,
                        c2,
                        out var weights))
                {
                    return;
                }

                var shell =
                    Vector3.Dot(aDelta, frame.NormalOS) * weights.x +
                    Vector3.Dot(bDelta, frame.NormalOS) * weights.y +
                    Vector3.Dot(cDelta, frame.NormalOS) * weights.z;
                if (shell < -frame.FrontShellOS ||
                    shell > frame.FrontAllowanceOS)
                {
                    return;
                }

                MarkByWorldLength(
                    a,
                    b,
                    frame,
                    maximumEdgeWorld * 3.2f,
                    splitEdges);
                MarkByWorldLength(
                    b,
                    c,
                    frame,
                    maximumEdgeWorld * 3.2f,
                    splitEdges);
                MarkByWorldLength(
                    c,
                    a,
                    frame,
                    maximumEdgeWorld * 3.2f,
                    splitEdges);
            }

            private void MarkByWorldLength(
                int a,
                int b,
                GougeFrame frame,
                float maximumEdgeWorld,
                ISet<ulong> splitEdges)
            {
                var length = frame.LocalToWorld.MultiplyVector(
                    Vertices[b] - Vertices[a]).magnitude;
                if (length > maximumEdgeWorld)
                {
                    splitEdges.Add(EdgeKey(a, b));
                }
            }

            private static bool TryBarycentricAtOrigin(
                Vector2 a,
                Vector2 b,
                Vector2 c,
                out Vector3 weights)
            {
                var denominator =
                    (b.y - c.y) * (a.x - c.x) +
                    (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(denominator) < 0.000001f)
                {
                    weights = default;
                    return false;
                }

                var weightA =
                    (b.x * c.y - c.x * b.y) /
                    denominator;
                var weightB =
                    (c.x * a.y - a.x * c.y) /
                    denominator;
                var weightC = 1f - weightA - weightB;
                const float tolerance = -0.0001f;
                weights = new Vector3(weightA, weightB, weightC);
                return weightA >= tolerance &&
                    weightB >= tolerance &&
                    weightC >= tolerance;
            }

            private void RefineTriangle(
                int a,
                int b,
                int c,
                ISet<ulong> splitEdges,
                IDictionary<ulong, int> midpoints,
                ICollection<int> destination)
            {
                var splitAb = splitEdges.Contains(EdgeKey(a, b));
                var splitBc = splitEdges.Contains(EdgeKey(b, c));
                var splitCa = splitEdges.Contains(EdgeKey(c, a));
                var splitCount =
                    (splitAb ? 1 : 0) +
                    (splitBc ? 1 : 0) +
                    (splitCa ? 1 : 0);
                if (splitCount == 0)
                {
                    AddTriangle(destination, a, b, c);
                    return;
                }

                var ab = splitAb ? Midpoint(a, b, midpoints) : -1;
                var bc = splitBc ? Midpoint(b, c, midpoints) : -1;
                var ca = splitCa ? Midpoint(c, a, midpoints) : -1;
                if (splitCount == 1)
                {
                    if (splitAb)
                    {
                        AddTriangle(destination, a, ab, c);
                        AddTriangle(destination, ab, b, c);
                    }
                    else if (splitBc)
                    {
                        AddTriangle(destination, a, b, bc);
                        AddTriangle(destination, a, bc, c);
                    }
                    else
                    {
                        AddTriangle(destination, a, b, ca);
                        AddTriangle(destination, b, c, ca);
                    }

                    return;
                }

                if (splitCount == 2)
                {
                    if (splitAb && splitBc)
                    {
                        AddTriangle(destination, b, bc, ab);
                        AddTriangle(destination, a, ab, c);
                        AddTriangle(destination, ab, bc, c);
                    }
                    else if (splitAb && splitCa)
                    {
                        AddTriangle(destination, a, ab, ca);
                        AddTriangle(destination, b, c, ca);
                        AddTriangle(destination, b, ca, ab);
                    }
                    else
                    {
                        AddTriangle(destination, c, ca, bc);
                        AddTriangle(destination, a, b, bc);
                        AddTriangle(destination, a, bc, ca);
                    }

                    return;
                }

                AddTriangle(destination, a, ab, ca);
                AddTriangle(destination, ab, b, bc);
                AddTriangle(destination, ca, bc, c);
                AddTriangle(destination, ab, bc, ca);
            }

            private int Midpoint(
                int a,
                int b,
                IDictionary<ulong, int> cache)
            {
                var key = EdgeKey(a, b);
                if (cache.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                var index = Vertices.Count;
                Vertices.Add((Vertices[a] + Vertices[b]) * 0.5f);
                _normals.Add(
                    (_normals[a] + _normals[b]).normalized);
                _uv.Add((_uv[a] + _uv[b]) * 0.5f);
                _colors.Add(Color32.Lerp(
                    _colors[a],
                    _colors[b],
                    0.5f));
                _chopData.Add(Vector4.Lerp(
                    _chopData[a],
                    _chopData[b],
                    0.5f));
                _deformation.Add(
                    (_deformation[a] + _deformation[b]) * 0.5f);
                cache.Add(key, index);
                return index;
            }

            private static ulong EdgeKey(int a, int b)
            {
                var minimum = (uint)Mathf.Min(a, b);
                var maximum = (uint)Mathf.Max(a, b);
                return ((ulong)minimum << 32) | maximum;
            }

            private static void AddTriangle(
                ICollection<int> destination,
                int a,
                int b,
                int c)
            {
                destination.Add(a);
                destination.Add(b);
                destination.Add(c);
            }

            private static List<Vector3> CreateDefaultNormals(int count)
            {
                var normals = new List<Vector3>(count);
                for (var index = 0; index < count; index++)
                {
                    normals.Add(Vector3.up);
                }

                return normals;
            }

            private static List<Vector2> CreateDefaultUv(int count)
            {
                var uv = new List<Vector2>(count);
                for (var index = 0; index < count; index++)
                {
                    uv.Add(Vector2.zero);
                }

                return uv;
            }

            private static List<Color32> CreateDefaultColors(int count)
            {
                var colors = new List<Color32>(count);
                for (var index = 0; index < count; index++)
                {
                    colors.Add(new Color32(255, 255, 255, 255));
                }

                return colors;
            }

            private static List<Vector4> CreateDefaultChopData(int count)
            {
                var values = new List<Vector4>(count);
                for (var index = 0; index < count; index++)
                {
                    values.Add(Vector4.zero);
                }

                return values;
            }
        }

        private static float LocalDistance(
            Transform transform,
            Vector3 worldPoint,
            Vector3 worldDirection,
            float worldDistance)
        {
            return Vector3.Distance(
                transform.InverseTransformPoint(worldPoint),
                transform.InverseTransformPoint(
                    worldPoint +
                    worldDirection.normalized * worldDistance));
        }

        private static float SmoothThreshold(
            float edge0,
            float edge1,
            float value)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(edge0, edge1, value));
        }
    }

    [DisallowMultipleComponent]
    internal sealed class TreeChopRuntimeMeshOwner : MonoBehaviour
    {
        // A later impact deepens an existing opening only when it lands inside
        // that opening; anywhere else on the trunk starts its own scar.
        private const float SameOpeningRadius = 1.25f;
        private const float SameOpeningNormalSlack = 0.045f;
        // The lateral gate is authoritative. This only rejects an impact that
        // arrived on the opposite face of a thin trunk.
        private const float SameOpeningFacing = -0.2f;

        private readonly List<Opening> _openings = new List<Opening>();
        private MeshFilter _filter;
        private MeshCollider _collider;
        private Mesh _runtimeMesh;
        private int _pendingOpening = -1;
        private bool _pendingOpeningIsNew;
        private int _lastOpening = -1;
        private bool _lastOpeningWasNew;

        public Mesh OriginalMesh { get; private set; }
        public int OpeningCount => _openings.Count;

        public void Initialize(
            MeshFilter filter,
            MeshCollider collider)
        {
            _filter = filter;
            _collider = collider;
            OriginalMesh = filter != null
                ? filter.sharedMesh
                : null;
        }

        /// <summary>
        /// Returns the opening an impact belongs to, or -1 when the impact
        /// landed on intact bark and has to start its own opening. The frame
        /// of an existing opening stays where it was authored so repeated
        /// impacts deepen one stable scar instead of drifting.
        /// </summary>
        public int FindOpening(Vector3 point, Vector3 normal)
        {
            var best = -1;
            var bestDistance = float.PositiveInfinity;
            for (var index = 0; index < _openings.Count; index++)
            {
                var opening = _openings[index];
                var openingNormal = transform
                    .TransformDirection(opening.NormalOS).normalized;
                if (Vector3.Dot(openingNormal, normal) <
                    SameOpeningFacing)
                {
                    continue;
                }

                TreeChopVoxelCarver.ResolveOpeningSize(
                    opening.SectionWidth,
                    opening.Stage,
                    out var width,
                    out var height,
                    out var depth);
                var delta = point -
                    transform.TransformPoint(opening.CenterOS);
                if (Mathf.Abs(Vector3.Dot(delta, openingNormal)) >
                    depth + SameOpeningNormalSlack)
                {
                    continue;
                }

                var right = transform
                    .TransformDirection(opening.RightOS).normalized;
                var up = transform
                    .TransformDirection(opening.UpOS).normalized;
                var lateral = Vector3.Dot(delta, right) / (width * 0.5f);
                var vertical = Vector3.Dot(delta, up) / (height * 0.5f);
                var distance = Mathf.Sqrt(
                    lateral * lateral + vertical * vertical);
                if (distance > SameOpeningRadius ||
                    distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = index;
            }

            return best;
        }

        public void BeginNewOpening(
            Vector3 center,
            Vector3 normal,
            Vector3 right,
            Vector3 up,
            float sectionWidth)
        {
            _openings.Add(new Opening
            {
                CenterOS = transform.InverseTransformPoint(center),
                NormalOS = transform
                    .InverseTransformDirection(normal).normalized,
                RightOS = transform
                    .InverseTransformDirection(right).normalized,
                UpOS = transform
                    .InverseTransformDirection(up).normalized,
                SectionWidth = sectionWidth,
                Stage = 1
            });
            _pendingOpening = _openings.Count - 1;
            _pendingOpeningIsNew = true;
        }

        public void BeginDeepenOpening(int index)
        {
            var opening = _openings[index];
            opening.Stage = Mathf.Min(
                opening.Stage + 1,
                FellableTreeIdentity.HitsRequired);
            _openings[index] = opening;
            _pendingOpening = index;
            _pendingOpeningIsNew = false;
        }

        public void CommitPendingImpact()
        {
            _lastOpening = _pendingOpening;
            _lastOpeningWasNew = _pendingOpeningIsNew;
            _pendingOpening = -1;
        }

        public void CancelPendingImpact()
        {
            if (_pendingOpening < 0)
            {
                return;
            }

            RevertImpact(_pendingOpening, _pendingOpeningIsNew);
            _pendingOpening = -1;
        }

        public bool TryUndoLastImpact()
        {
            if (_lastOpening < 0)
            {
                return false;
            }

            RevertImpact(_lastOpening, _lastOpeningWasNew);
            _lastOpening = -1;
            return true;
        }

        public void GetOpening(
            int index,
            out Vector3 center,
            out Vector3 normal,
            out Vector3 right,
            out Vector3 up,
            out float sectionWidth,
            out int stage)
        {
            var opening = _openings[index];
            center = transform.TransformPoint(opening.CenterOS);
            normal = transform
                .TransformDirection(opening.NormalOS).normalized;
            right = transform
                .TransformDirection(opening.RightOS).normalized;
            up = transform
                .TransformDirection(opening.UpOS).normalized;
            sectionWidth = opening.SectionWidth;
            stage = opening.Stage;
        }

        public void RestoreAuthoredMesh()
        {
            if (_runtimeMesh == null)
            {
                return;
            }

            var previous = _runtimeMesh;
            _runtimeMesh = null;
            _filter.sharedMesh = OriginalMesh;
            _collider.sharedMesh = OriginalMesh;
            DestroyOwned(previous);
        }

        private void RevertImpact(int index, bool wasNew)
        {
            if (index < 0 || index >= _openings.Count)
            {
                return;
            }

            if (wasNew)
            {
                _openings.RemoveAt(index);
                return;
            }

            var opening = _openings[index];
            opening.Stage = Mathf.Max(1, opening.Stage - 1);
            _openings[index] = opening;
        }

        public void Replace(Mesh mesh)
        {
            var previous = _runtimeMesh;
            _runtimeMesh = mesh;
            _filter.sharedMesh = mesh;
            // Cooking dominates the cost of an impact. The carved mesh is
            // generated, not authored: it needs no cleaning or welding pass,
            // and nothing simulates against it, so only the query midphase
            // has to be built.
            _collider.cookingOptions =
                MeshColliderCookingOptions.UseFastMidphase;
            _collider.sharedMesh = mesh;
            if (previous != null)
            {
                DestroyOwned(previous);
            }
        }

        private void OnDestroy()
        {
            if (_filter != null &&
                _filter.sharedMesh == _runtimeMesh)
            {
                _filter.sharedMesh = OriginalMesh;
            }

            if (_collider != null &&
                _collider.sharedMesh == _runtimeMesh)
            {
                _collider.sharedMesh = OriginalMesh;
            }

            if (_runtimeMesh != null)
            {
                DestroyOwned(_runtimeMesh);
                _runtimeMesh = null;
            }
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private struct Opening
        {
            public Vector3 CenterOS;
            public Vector3 NormalOS;
            public Vector3 RightOS;
            public Vector3 UpOS;
            public float SectionWidth;
            public int Stage;
        }
    }
}
