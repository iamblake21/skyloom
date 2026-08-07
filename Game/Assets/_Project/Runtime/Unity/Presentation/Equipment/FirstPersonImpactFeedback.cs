using System.Collections.Generic;
using CML.Unity.Mining;
using CML.Unity.Wood;
using UnityEngine;

namespace CML.Unity.Presentation.Equipment
{
    /// <summary>
    /// Presentation-only recoil applied to the world object struck by the
    /// pickaxe. The camera never moves, and ordinary geometry or misses never
    /// reach this component because only resource-specific events play it.
    /// </summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FirstPersonImpactFeedback : MonoBehaviour
    {
        private const string LegacyCameraPivotName =
            "FEEL_ImpactCameraPivot";
        private const string TreeShaderName =
            "CML/Environment/Starter Island CloudTall Tree";
        private static readonly int HitOffsetId =
            Shader.PropertyToID("_CMLHitOffsetWS");

        [Header("Stone")]
        [SerializeField, Min(0.01f)] private float stoneDuration = 0.14f;
        [SerializeField, Min(0f)] private float stoneTravel = 0.028f;
        [SerializeField, Min(0f)] private float stoneRotation = 0.8f;
        [SerializeField, Min(0.1f)] private float stoneFrequency = 28f;

        [Header("Wood")]
        [SerializeField, Min(0.01f)] private float woodDuration = 0.18f;
        [SerializeField, Min(0f)] private float woodTravel = 0.014f;
        [SerializeField, Min(0f)] private float woodRotation = 0.28f;
        [SerializeField, Min(0.1f)] private float woodFrequency = 20f;

        [SerializeField] private FirstPersonEquipmentMotion impactSource;

        private Transform _activeTarget;
        private Transform _visualPivot;
        private readonly List<RendererState> _originalRenderers =
            new List<RendererState>();
        private readonly List<Material> _temporaryMaterials =
            new List<Material>();
        private Vector3 _localImpactDirection = Vector3.forward;
        private Vector3 _localSideDirection = Vector3.right;
        private float _activeDuration;
        private float _activeTravel;
        private float _activeRotation;
        private float _activeFrequency;
        private float _elapsed = float.PositiveInfinity;
        private float _direction = 1f;
        private RaycastHit _pendingHit;
        private bool _hasPendingHit;
        private bool _useRendererOffset;
        private bool _legacyTreeBlocksCleared;

        public void Configure(FirstPersonEquipmentMotion source)
        {
            RemoveLegacyCameraPivot();
            ClearLegacyTreePropertyBlocks();
            if (impactSource == source)
            {
                return;
            }

            Unsubscribe();
            impactSource = source;
            Subscribe();
        }

        private void LateUpdate()
        {
            _hasPendingHit = false;
            if (_activeTarget == null || !CanPresentImpact())
            {
                StopAndReset();
                return;
            }

            if (_originalRenderers.Count == 0
                && !PrepareVisualFeedback())
            {
                StopAndReset();
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= _activeDuration)
            {
                StopAndReset();
                return;
            }

            ApplyTargetPose();
        }

        private void OnDisable()
        {
            StopAndReset();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            StopAndReset();
        }

        private void HandlePhysicalImpact(RaycastHit hit)
        {
            _pendingHit = hit;
            _hasPendingHit = hit.collider != null;
        }

        private void HandleStoneImpact(ManualMiningSourceIdentity source)
        {
            if (source == null || !_hasPendingHit)
            {
                return;
            }

            // The infinite deposit surface is only the gameplay extraction
            // trigger. It must not turn a floor hit into a visual shake of
            // the whole deposit, which belongs to finite rock impacts.
            if (source.SourceKind ==
                ManualMiningSourceKind.IronDepositSurface)
            {
                _hasPendingHit = false;
                return;
            }

            Play(source.transform, _pendingHit, PickaxeImpactSurface.Stone);
            _hasPendingHit = false;
        }

        private void HandleWoodImpact(
            FellableTreeIdentity tree,
            RaycastHit hit)
        {
            if (tree == null)
            {
                return;
            }

            Play(tree.transform, hit, PickaxeImpactSurface.Wood);
            _hasPendingHit = false;
        }

        private void Play(
            Transform target,
            RaycastHit hit,
            PickaxeImpactSurface surface)
        {
            StopAndReset();
            if (target == null || hit.collider == null)
            {
                return;
            }

            _activeTarget = target;

            var worldDirection = hit.point - transform.position;
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                worldDirection = -hit.normal;
            }

            worldDirection.Normalize();
            var parent = target.parent;
            _localImpactDirection = parent != null
                ? parent.InverseTransformDirection(worldDirection).normalized
                : worldDirection;
            var worldSide = Vector3.Cross(Vector3.up, worldDirection);
            if (worldSide.sqrMagnitude < 0.0001f)
            {
                worldSide = Vector3.right;
            }

            worldSide.Normalize();
            _localSideDirection = parent != null
                ? parent.InverseTransformDirection(worldSide).normalized
                : worldSide;

            var stone = surface == PickaxeImpactSurface.Stone;
            _activeDuration = stone ? stoneDuration : woodDuration;
            _activeTravel = stone ? stoneTravel : woodTravel;
            _activeRotation = stone ? stoneRotation : woodRotation;
            _activeFrequency = stone ? stoneFrequency : woodFrequency;
            // Both rock and tree materials support the same vertex-space
            // offset. Keeping the original renderer in place is important:
            // replacing it with a moved proxy makes Unity drop the baked
            // lightmap association for the duration of the hit.
            _useRendererOffset = true;
            _direction = -_direction;
            _elapsed = 0f;
        }

        private void ApplyTargetPose()
        {
            var progress = Mathf.Clamp01(_elapsed / _activeDuration);
            var remaining = 1f - progress;
            var envelope = remaining * remaining;
            var phase = _elapsed * _activeFrequency * Mathf.PI * 2f;
            var recoil = Mathf.Sin(phase);
            var side = Mathf.Sin(phase * 0.67f + 0.8f) * _direction;

            var positionOffset =
                (_localImpactDirection * recoil
                 + _localSideDirection * side * 0.24f)
                * (_activeTravel * envelope);
            var rotationOffset = new Vector3(
                -recoil * _activeRotation,
                side * _activeRotation * 0.45f,
                -side * _activeRotation * 0.65f) * envelope;

            if (_useRendererOffset)
            {
                ApplyRendererOffset(
                    _activeTarget.TransformVector(positionOffset));
                return;
            }

            _visualPivot.localPosition = positionOffset;
            _visualPivot.localRotation = Quaternion.Euler(rotationOffset);
        }

        private bool CanPresentImpact()
        {
            return impactSource != null
                && impactSource.MotionRoot != null
                && impactSource.MotionRoot.gameObject.activeInHierarchy;
        }

        private void Subscribe()
        {
            if (impactSource == null)
            {
                return;
            }

            impactSource.PickaxeImpactHit += HandlePhysicalImpact;
            impactSource.PickaxeMiningSourceHit += HandleStoneImpact;
            impactSource.PickaxeTreeHit += HandleWoodImpact;
        }

        private void Unsubscribe()
        {
            if (impactSource == null)
            {
                return;
            }

            impactSource.PickaxeImpactHit -= HandlePhysicalImpact;
            impactSource.PickaxeMiningSourceHit -= HandleStoneImpact;
            impactSource.PickaxeTreeHit -= HandleWoodImpact;
        }

        private void StopAndReset()
        {
            for (var index = 0;
                 index < _originalRenderers.Count;
                 index++)
            {
                var state = _originalRenderers[index];
                if (state.Renderer != null)
                {
                    state.Renderer.enabled = state.WasEnabled;
                    if (state.OriginalMaterials != null)
                    {
                        state.Renderer.sharedMaterials =
                            state.OriginalMaterials;
                    }
                }
            }

            _originalRenderers.Clear();
            for (var index = 0;
                 index < _temporaryMaterials.Count;
                 index++)
            {
                DestroyRuntimeObject(_temporaryMaterials[index]);
            }

            _temporaryMaterials.Clear();
            if (_visualPivot != null)
            {
                _visualPivot.gameObject.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(_visualPivot.gameObject);
                }
                else
                {
                    DestroyImmediate(_visualPivot.gameObject);
                }
            }

            _visualPivot = null;
            _activeTarget = null;
            _elapsed = float.PositiveInfinity;
            _useRendererOffset = false;
        }

        private bool PrepareVisualFeedback()
        {
            return _useRendererOffset
                ? PrepareRendererOffsets()
                : CreateVisualProxy();
        }

        private bool PrepareRendererOffsets()
        {
            var renderers =
                _activeTarget.GetComponentsInChildren<MeshRenderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null
                    || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var originalMaterials = renderer.sharedMaterials;
                var impactMaterials =
                    new Material[originalMaterials.Length];
                for (var materialIndex = 0;
                     materialIndex < originalMaterials.Length;
                     materialIndex++)
                {
                    var original = originalMaterials[materialIndex];
                    if (original == null)
                    {
                        continue;
                    }

                    var impactMaterial = new Material(original)
                    {
                        name = $"{original.name}_HitFeedback",
                        hideFlags = HideFlags.DontSave,
                    };
                    impactMaterials[materialIndex] = impactMaterial;
                    _temporaryMaterials.Add(impactMaterial);
                }

                renderer.sharedMaterials = impactMaterials;
                _originalRenderers.Add(new RendererState(
                    renderer,
                    renderer.enabled,
                    originalMaterials,
                    impactMaterials));
            }

            return _originalRenderers.Count > 0;
        }

        private void ApplyRendererOffset(Vector3 worldOffset)
        {
            for (var index = 0;
                 index < _originalRenderers.Count;
                 index++)
            {
                var state = _originalRenderers[index];
                if (state.Renderer == null)
                {
                    continue;
                }

                var materials = state.ImpactMaterials;
                if (materials == null)
                {
                    continue;
                }

                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material != null && material.HasProperty(HitOffsetId))
                    {
                        material.SetVector(HitOffsetId, worldOffset);
                    }
                }
            }
        }

        private bool CreateVisualProxy()
        {
            var renderers =
                _activeTarget.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            var pivotObject = new GameObject("FEEL_HitObjectVisual")
            {
                hideFlags = HideFlags.DontSave,
                layer = _activeTarget.gameObject.layer,
            };
            _visualPivot = pivotObject.transform;
            _visualPivot.SetParent(_activeTarget, false);

            for (var index = 0; index < renderers.Length; index++)
            {
                var sourceRenderer = renderers[index];
                var sourceFilter = sourceRenderer != null
                    ? sourceRenderer.GetComponent<MeshFilter>()
                    : null;
                if (sourceRenderer == null
                    || !sourceRenderer.enabled
                    || !sourceRenderer.gameObject.activeInHierarchy
                    || sourceFilter == null
                    || sourceFilter.sharedMesh == null)
                {
                    continue;
                }

                var proxyObject = new GameObject(
                    $"FEEL_HitVisual_{sourceRenderer.name}")
                {
                    hideFlags = HideFlags.DontSave,
                    layer = sourceRenderer.gameObject.layer,
                };
                var proxyTransform = proxyObject.transform;
                proxyTransform.SetPositionAndRotation(
                    sourceRenderer.transform.position,
                    sourceRenderer.transform.rotation);
                proxyTransform.localScale =
                    sourceRenderer.transform.lossyScale;
                proxyTransform.SetParent(_visualPivot, true);

                var proxyFilter = proxyObject.AddComponent<MeshFilter>();
                proxyFilter.sharedMesh = sourceFilter.sharedMesh;
                var proxyRenderer =
                    proxyObject.AddComponent<MeshRenderer>();
                proxyRenderer.sharedMaterials =
                    sourceRenderer.sharedMaterials;
                proxyRenderer.shadowCastingMode =
                    sourceRenderer.shadowCastingMode;
                proxyRenderer.receiveShadows =
                    sourceRenderer.receiveShadows;
                proxyRenderer.lightProbeUsage =
                    sourceRenderer.lightProbeUsage;
                proxyRenderer.reflectionProbeUsage =
                    sourceRenderer.reflectionProbeUsage;
                proxyRenderer.probeAnchor = sourceRenderer.probeAnchor;
                proxyRenderer.renderingLayerMask =
                    sourceRenderer.renderingLayerMask;
                proxyRenderer.motionVectorGenerationMode =
                    sourceRenderer.motionVectorGenerationMode;
                proxyRenderer.allowOcclusionWhenDynamic =
                    sourceRenderer.allowOcclusionWhenDynamic;
                proxyRenderer.lightmapIndex =
                    sourceRenderer.lightmapIndex;
                proxyRenderer.lightmapScaleOffset =
                    sourceRenderer.lightmapScaleOffset;
                proxyRenderer.sortingLayerID =
                    sourceRenderer.sortingLayerID;
                proxyRenderer.sortingOrder =
                    sourceRenderer.sortingOrder;

                var properties = new MaterialPropertyBlock();
                sourceRenderer.GetPropertyBlock(properties);
                proxyRenderer.SetPropertyBlock(properties);

                _originalRenderers.Add(new RendererState(
                    sourceRenderer,
                    sourceRenderer.enabled,
                    null,
                    null));
                sourceRenderer.enabled = false;
            }

            if (_originalRenderers.Count > 0)
            {
                return true;
            }

            _visualPivot.gameObject.SetActive(false);
            Destroy(_visualPivot.gameObject);
            _visualPivot = null;
            return false;
        }

        private void RemoveLegacyCameraPivot()
        {
            var pivot = transform.parent;
            if (pivot == null || pivot.name != LegacyCameraPivotName)
            {
                return;
            }

            var parent = pivot.parent;
            var localPosition = pivot.localPosition;
            var localRotation = pivot.localRotation;
            var localScale = pivot.localScale;
            transform.SetParent(parent, false);
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            transform.localScale = localScale;

            if (Application.isPlaying)
            {
                Destroy(pivot.gameObject);
            }
            else
            {
                DestroyImmediate(pivot.gameObject);
            }
        }

        private void ClearLegacyTreePropertyBlocks()
        {
            if (_legacyTreeBlocksCleared)
            {
                return;
            }

            _legacyTreeBlocksCleared = true;
            var trees = Object.FindObjectsByType<FellableTreeIdentity>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var treeIndex = 0;
                 treeIndex < trees.Length;
                 treeIndex++)
            {
                var renderers = trees[treeIndex]
                    .GetComponentsInChildren<MeshRenderer>(true);
                for (var rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer == null
                        || !renderer.HasPropertyBlock()
                        || !UsesTreeShader(renderer))
                    {
                        continue;
                    }

                    renderer.SetPropertyBlock(null);
                }
            }
        }

        private static bool UsesTreeShader(MeshRenderer renderer)
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var shader = materials[index] != null
                    ? materials[index].shader
                    : null;
                if (shader != null && shader.name == TreeShaderName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void DestroyRuntimeObject(Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private readonly struct RendererState
        {
            public RendererState(
                MeshRenderer renderer,
                bool wasEnabled,
                Material[] originalMaterials,
                Material[] impactMaterials)
            {
                Renderer = renderer;
                WasEnabled = wasEnabled;
                OriginalMaterials = originalMaterials;
                ImpactMaterials = impactMaterials;
            }

            public MeshRenderer Renderer { get; }
            public bool WasEnabled { get; }
            public Material[] OriginalMaterials { get; }
            public Material[] ImpactMaterials { get; }
        }
    }
}
