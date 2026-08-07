using System;
using UnityEngine;

namespace CML.Unity.Presentation
{
    public static class WorldInteractionInput
    {
        private static int _consumedFrame = -1;

        public static bool WasConsumedThisFrame =>
            _consumedFrame == Time.frameCount;

        public static void ConsumeCurrentFrame()
        {
            _consumedFrame = Time.frameCount;
        }
    }

    /// <summary>
    /// The sole presentation contract for an object that can consume the player's
    /// interaction edge. Targets own their action and wording; targeting, input and
    /// prompt presentation remain centralized in one interactor.
    /// </summary>
    public interface IWorldInteractionTarget
    {
        bool IsInteractionAvailable { get; }

        string InteractionPrompt { get; }

        bool OwnsInteractionCollider(Collider collider);

        bool TryGetInteractionBounds(out Bounds bounds);

        bool TryInteract();
    }

    public static class WorldInteractionBounds
    {
        public static bool TryCalculate(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
            {
                return false;
            }

            var hasBounds = false;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null
                    || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy
                    || renderer is ParticleSystemRenderer
                    || renderer is TrailRenderer
                    || renderer is LineRenderer)
                {
                    continue;
                }

                Encapsulate(renderer.bounds, ref bounds, ref hasBounds);
            }

            if (hasBounds)
            {
                return true;
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null
                    || !collider.enabled
                    || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Encapsulate(collider.bounds, ref bounds, ref hasBounds);
            }

            return hasBounds;
        }

        private static void Encapsulate(
            Bounds candidate,
            ref Bounds bounds,
            ref bool hasBounds)
        {
            if (!hasBounds)
            {
                bounds = candidate;
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(candidate);
        }
    }

    /// <summary>
    /// One transparent world-space prompt shared by every interactable. TextMesh is
    /// intentionally used instead of a scene Canvas so the prompt never introduces
    /// another HUD document or a prefab/scene dependency.
    /// </summary>
    public sealed class WorldInteractionPrompt : IDisposable
    {
        private const float SurfaceOffset = 0.045f;
        private const float VerticalOffset = 0.12f;

        private readonly GameObject _root;
        private readonly TextMesh _text;
        private readonly TextMesh _shadow;

        public WorldInteractionPrompt()
        {
            _root = new GameObject("INTERACT_WorldPrompt")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _shadow = CreateText("Shadow", new Color(0.02f, 0.025f, 0.02f, 0.72f));
            _text = CreateText("Text", new Color(0.97f, 0.98f, 0.94f, 0.98f));
            _shadow.transform.localPosition = new Vector3(
                0.0027f,
                -0.0027f,
                0.0013f);
            _text.transform.localPosition = Vector3.zero;
            _root.SetActive(false);
        }

        public void Show(string message, Bounds targetBounds, Camera camera)
        {
            if (camera == null || string.IsNullOrWhiteSpace(message))
            {
                Hide();
                return;
            }

            var cameraPosition = camera.transform.position;
            var surface = targetBounds.ClosestPoint(cameraPosition);
            var towardCamera = cameraPosition - surface;
            if (towardCamera.sqrMagnitude < 0.0001f)
            {
                towardCamera = cameraPosition - targetBounds.center;
            }

            if (towardCamera.sqrMagnitude < 0.0001f)
            {
                towardCamera = -camera.transform.forward;
            }

            towardCamera.Normalize();
            _root.transform.position = surface
                + towardCamera * SurfaceOffset
                + Vector3.up * VerticalOffset;
            _root.transform.rotation = Quaternion.LookRotation(
                _root.transform.position - cameraPosition,
                camera.transform.up);

            var normalized = message.Trim().ToUpperInvariant();
            _text.text = normalized;
            _shadow.text = normalized;
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
            }
        }

        public void Hide()
        {
            if (_root != null && _root.activeSelf)
            {
                _root.SetActive(false);
            }
        }

        public void Dispose()
        {
            if (_root == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_root);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        private TextMesh CreateText(string objectName, Color colour)
        {
            var child = new GameObject(objectName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            child.transform.SetParent(_root.transform, false);
            var text = child.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = 0.0133f;
            text.fontStyle = FontStyle.Bold;
            text.richText = false;
            text.color = colour;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            return text;
        }
    }
}
