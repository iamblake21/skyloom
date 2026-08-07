using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Geometry-only placement helpers for presentation cargo.
    /// Cargo pivots are not contact points: every item is placed from its rendered
    /// bounds so differently sized items share the same physical support plane.
    /// </summary>
    public static class FactoryCargoVisualGeometry
    {
        // Top of the authored cross-battens in MEC_Belt_Straight and
        // MEC_Belt_DriveUnit. The canvas surface is 0.600 m; visible cargo must
        // rest on the 0.014 m battens rather than intersect them.
        public const float BeltKitSupportHeightMetres = 0.614f;

        public static bool TryGetWorldProjection(
            Transform visualRoot,
            Vector3 worldAxis,
            out float minimum,
            out float maximum)
        {
            minimum = 0f;
            maximum = 0f;
            if (visualRoot == null || worldAxis.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            var axis = worldAxis.normalized;
            var renderers =
                visualRoot.GetComponentsInChildren<Renderer>(true);
            var found = false;
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var localBounds = renderer.localBounds;
                var centre = localBounds.center;
                var extents = localBounds.extents;

                for (var corner = 0; corner < 8; corner++)
                {
                    var localCorner = centre + new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);
                    var projection = Vector3.Dot(
                        renderer.transform.TransformPoint(localCorner),
                        axis);
                    if (!found)
                    {
                        minimum = projection;
                        maximum = projection;
                        found = true;
                        continue;
                    }

                    minimum = Mathf.Min(minimum, projection);
                    maximum = Mathf.Max(maximum, projection);
                }
            }

            return found;
        }

        public static bool AlignMinimumToPlane(
            Transform visualRoot,
            Vector3 planePoint,
            Vector3 planeNormal,
            out float translationMetres)
        {
            translationMetres = 0f;
            if (planeNormal.sqrMagnitude <= 0.000001f
                || !TryGetWorldProjection(
                    visualRoot,
                    planeNormal,
                    out var minimum,
                    out _))
            {
                return false;
            }

            var normal = planeNormal.normalized;
            var target = Vector3.Dot(planePoint, normal);
            translationMetres = target - minimum;
            visualRoot.position += normal * translationMetres;
            return true;
        }

        public static bool TryGetMinimumClearance(
            Transform visualRoot,
            Vector3 planePoint,
            Vector3 outwardNormal,
            out float clearanceMetres)
        {
            clearanceMetres = 0f;
            if (outwardNormal.sqrMagnitude <= 0.000001f
                || !TryGetWorldProjection(
                    visualRoot,
                    outwardNormal,
                    out var minimum,
                    out _))
            {
                return false;
            }

            var normal = outwardNormal.normalized;
            clearanceMetres =
                minimum - Vector3.Dot(planePoint, normal);
            return true;
        }
    }
}
