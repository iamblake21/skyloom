using System;
using System.Collections.Generic;
using UnityEngine;

namespace CML.Unity.Wood
{
    /// <summary>
    /// Converts one authored standing tree into a heavy physical body. The
    /// exact non-convex collider remains the raycast authority while standing;
    /// an authored compound following only the main trunk owns the fall.
    /// </summary>
    internal static class TreeFellingFactory
    {
        private const float FallingMass = 680f;
        private const string FallingColliderRootName =
            "WOOD_FallingColliders_V4";
        private const float MaximumAngularSpeed = 0.36f;
        private const float MinimumReleaseAngle = 4f;
        private const float MaximumReleaseAngle = 14f;
        private const float ReleaseBeyondBalance = 2.25f;

        public static bool TryCreatePhysicalFall(
            FellableTreeIdentity tree,
            out IntactTreeFallAnimator fallingTree)
        {
            fallingTree = null;
            if (tree == null
                || !tree.IsReadyForFelling
                || !tree.TryResolveTrunkRenderer(out var trunkRenderer))
            {
                return false;
            }

            if (!TryResolveAuthoredColliders(
                    tree,
                    trunkRenderer,
                    out var standingCollider,
                    out var fallingColliders)
                || standingCollider.sharedMesh == null)
            {
                Debug.LogError(
                    $"{tree.name} is missing its exact standing collider or " +
                    "authored falling-trunk compound. Rebuild the CloudTall " +
                    "prefabs.",
                    tree);
                return false;
            }

            var treeUp = tree.transform.up.sqrMagnitude > 0.0001f
                ? tree.transform.up.normalized
                : Vector3.up;
            var fallDirection = ResolveFallDirection(tree, treeUp);
            var fallAxis = Vector3.Cross(treeUp, fallDirection);
            if (fallAxis.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            fallAxis.Normalize();
            if (!TryResolveLeadingBasePivot(
                    trunkRenderer,
                    standingCollider.sharedMesh,
                    treeUp,
                    fallDirection,
                    out var pivotPosition,
                    out var trunkHeight))
            {
                return false;
            }

            var releaseAngle = ResolveReleaseAngle(
                trunkRenderer.bounds.center,
                pivotPosition,
                treeUp,
                fallDirection);
            var fallingRoot = new GameObject(
                $"WOOD_Falling_{tree.StableTreeId}");
            fallingRoot.layer = tree.gameObject.layer;
            fallingRoot.transform.SetPositionAndRotation(
                pivotPosition,
                Quaternion.identity);

            SetDynamicRecursively(tree.gameObject);
            tree.transform.SetParent(
                fallingRoot.transform,
                worldPositionStays: true);
            var fallingMaterial = PreparePhysicalColliders(
                tree.gameObject,
                standingCollider,
                fallingColliders);

            // There is deliberately no initial transform rotation here. Any
            // visible movement must come from the continuous physical release.
            var body = fallingRoot.AddComponent<Rigidbody>();
            body.mass = FallingMass;
            body.useGravity = true;
            body.isKinematic = false;
            body.linearDamping = 0.06f;
            body.angularDamping = 0.06f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = MaximumAngularSpeed;
            body.maxDepenetrationVelocity = 1.10f;
            body.solverIterations = 16;
            body.solverVelocityIterations = 12;
            body.centerOfMass =
                fallingRoot.transform.InverseTransformPoint(
                    trunkRenderer.bounds.center);
            body.ResetInertiaTensor();

            var hinge = fallingRoot.AddComponent<HingeJoint>();
            hinge.autoConfigureConnectedAnchor = false;
            hinge.connectedBody = null;
            hinge.anchor = Vector3.zero;
            hinge.connectedAnchor = pivotPosition;
            hinge.axis =
                fallingRoot.transform.InverseTransformDirection(fallAxis);
            hinge.enableCollision = false;
            hinge.enablePreprocessing = true;
            hinge.useMotor = false;
            hinge.useSpring = false;
            hinge.useLimits = true;
            var limits = hinge.limits;
            // The tree cannot rock backwards before the release torque wins.
            limits.min = 0f;
            // This is only a safety rail. The hinge is removed shortly after
            // the centre of mass crosses the base edge.
            limits.max = 175f;
            limits.bounciness = 0f;
            limits.contactDistance = 0.25f;
            hinge.limits = limits;

            var initialSupports = ResolveInitialSupports(
                pivotPosition,
                treeUp,
                tree.transform,
                trunkHeight);
            SetSupportCollisionsIgnored(
                fallingColliders,
                initialSupports,
                ignored: true);

            fallingTree =
                fallingRoot.AddComponent<IntactTreeFallAnimator>();
            fallingTree.Configure(
                body,
                hinge,
                fallingColliders,
                initialSupports,
                fallingMaterial,
                fallAxis,
                treeUp,
                pivotPosition,
                trunkHeight,
                releaseAngle);
            body.WakeUp();
            Physics.SyncTransforms();
            return true;
        }

        private static bool TryResolveAuthoredColliders(
            FellableTreeIdentity tree,
            MeshRenderer trunkRenderer,
            out MeshCollider standingCollider,
            out Collider[] fallingColliders)
        {
            standingCollider = null;
            fallingColliders = Array.Empty<Collider>();
            var colliders =
                trunkRenderer.GetComponents<MeshCollider>();
            for (var index = 0; index < colliders.Length; index++)
            {
                var candidate = colliders[index];
                if (candidate == null)
                {
                    continue;
                }

                if (!candidate.convex)
                {
                    standingCollider ??= candidate;
                }
            }

            var fallingRoot = tree.transform.Find(
                FallingColliderRootName);
            if (fallingRoot != null)
            {
                fallingColliders =
                    fallingRoot.GetComponentsInChildren<Collider>(
                        includeInactive: true);
            }

            return standingCollider != null
                   && standingCollider.sharedMesh != null
                   && fallingColliders.Length > 0;
        }

        private static Vector3 ResolveFallDirection(
            FellableTreeIdentity tree,
            Vector3 treeUp)
        {
            var direction = Vector3.ProjectOnPlane(
                tree.FinalStrikeDirection,
                treeUp);
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.ProjectOnPlane(
                    -tree.FinalHitNormal,
                    treeUp);
            }

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.ProjectOnPlane(
                    tree.transform.forward,
                    treeUp);
            }

            return direction.normalized;
        }

        private static bool TryResolveLeadingBasePivot(
            MeshRenderer trunkRenderer,
            Mesh trunkMesh,
            Vector3 treeUp,
            Vector3 fallDirection,
            out Vector3 pivot,
            out float trunkHeight)
        {
            pivot = default;
            trunkHeight = 0f;
            var vertices = trunkMesh.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return false;
            }

            var trunkTransform = trunkRenderer.transform;
            var minimumHeight = float.PositiveInfinity;
            var maximumHeight = float.NegativeInfinity;
            for (var index = 0; index < vertices.Length; index++)
            {
                var world = trunkTransform.TransformPoint(vertices[index]);
                var height = Vector3.Dot(world, treeUp);
                minimumHeight = Mathf.Min(minimumHeight, height);
                maximumHeight = Mathf.Max(maximumHeight, height);
            }

            trunkHeight = maximumHeight - minimumHeight;
            if (!float.IsFinite(trunkHeight) || trunkHeight <= 0.001f)
            {
                return false;
            }

            var sliceTop = minimumHeight
                           + Mathf.Max(0.08f, trunkHeight * 0.06f);
            var lateral = Vector3.Cross(
                treeUp,
                fallDirection).normalized;
            var leading = float.NegativeInfinity;
            var lateralSum = 0f;
            var selected = 0;
            for (var index = 0; index < vertices.Length; index++)
            {
                var world = trunkTransform.TransformPoint(vertices[index]);
                if (Vector3.Dot(world, treeUp) > sliceTop)
                {
                    continue;
                }

                leading = Mathf.Max(
                    leading,
                    Vector3.Dot(world, fallDirection));
                lateralSum += Vector3.Dot(world, lateral);
                selected++;
            }

            if (selected == 0 || !float.IsFinite(leading))
            {
                return false;
            }

            pivot = treeUp * minimumHeight
                    + fallDirection * leading
                    + lateral * (lateralSum / selected);
            return true;
        }

        private static float ResolveReleaseAngle(
            Vector3 approximateCentreOfMass,
            Vector3 pivot,
            Vector3 treeUp,
            Vector3 fallDirection)
        {
            var lever = approximateCentreOfMass - pivot;
            var height = Mathf.Max(
                0.01f,
                Vector3.Dot(lever, treeUp));
            var behindPivot = Mathf.Max(
                0f,
                -Vector3.Dot(lever, fallDirection));
            var balanceAngle = Mathf.Atan2(
                behindPivot,
                height) * Mathf.Rad2Deg;
            return Mathf.Clamp(
                balanceAngle + ReleaseBeyondBalance,
                MinimumReleaseAngle,
                MaximumReleaseAngle);
        }

        private static Collider[] ResolveInitialSupports(
            Vector3 pivot,
            Vector3 treeUp,
            Transform treeRoot,
            float trunkHeight)
        {
            var supports = new HashSet<Collider>();
            var radius = Mathf.Clamp(
                trunkHeight * 0.055f,
                0.35f,
                0.70f);
            var overlaps = Physics.OverlapSphere(
                pivot + treeUp * (radius * 0.20f),
                radius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < overlaps.Length; index++)
            {
                AddExternalSupport(
                    overlaps[index],
                    treeRoot,
                    supports);
            }

            var hits = Physics.RaycastAll(
                pivot + treeUp * 0.75f,
                -treeUp,
                2f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < hits.Length; index++)
            {
                AddExternalSupport(
                    hits[index].collider,
                    treeRoot,
                    supports);
            }

            var result = new Collider[supports.Count];
            supports.CopyTo(result);
            return result;
        }

        private static void AddExternalSupport(
            Collider candidate,
            Transform treeRoot,
            ISet<Collider> supports)
        {
            if (candidate == null
                || candidate.isTrigger
                || candidate.transform == treeRoot
                || candidate.transform.IsChildOf(treeRoot))
            {
                return;
            }

            supports.Add(candidate);
        }

        private static void SetSupportCollisionsIgnored(
            IReadOnlyList<Collider> fallingColliders,
            IReadOnlyList<Collider> supports,
            bool ignored)
        {
            for (var colliderIndex = 0;
                 colliderIndex < fallingColliders.Count;
                 colliderIndex++)
            {
                var fallingCollider = fallingColliders[colliderIndex];
                if (fallingCollider == null)
                {
                    continue;
                }

                for (var supportIndex = 0;
                     supportIndex < supports.Count;
                     supportIndex++)
                {
                    var support = supports[supportIndex];
                    if (support != null)
                    {
                        Physics.IgnoreCollision(
                            fallingCollider,
                            support,
                            ignored);
                    }
                }
            }
        }

        private static PhysicsMaterial PreparePhysicalColliders(
            GameObject tree,
            MeshCollider standingCollider,
            IReadOnlyList<Collider> fallingColliders)
        {
            var fallingSet = new HashSet<Collider>();
            for (var index = 0;
                 index < fallingColliders.Count;
                 index++)
            {
                if (fallingColliders[index] != null)
                {
                    fallingSet.Add(fallingColliders[index]);
                }
            }

            var colliders = tree.GetComponentsInChildren<Collider>(
                includeInactive: true);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider != null)
                {
                    collider.enabled = fallingSet.Contains(collider);
                }
            }

            standingCollider.enabled = false;
            var material = new PhysicsMaterial(
                "PM_WOOD_FallingTrunk")
            {
                dynamicFriction = 0.38f,
                staticFriction = 0.52f,
                // Restitution is applied once from the actual impact contact
                // by IntactTreeFallAnimator. Keeping material bounce at zero
                // avoids project-wide bounce-threshold differences and a
                // second, solver-dependent kick.
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            foreach (var fallingCollider in fallingSet)
            {
                fallingCollider.gameObject.layer = tree.layer;
                fallingCollider.isTrigger = false;
                fallingCollider.enabled = true;
                fallingCollider.sharedMaterial = material;
            }

            return material;
        }

        private static void SetDynamicRecursively(GameObject root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(
                includeInactive: true);
            for (var index = 0; index < transforms.Length; index++)
            {
                transforms[index].gameObject.isStatic = false;
            }
        }
    }
}
