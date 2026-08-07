using UnityEngine;

namespace CML.Unity.Presentation.Airship
{
    /// <summary>
    /// Renders a copy of the hull into a texture the repair panel can show, and
    /// lets the player orbit, pan and zoom it.
    ///
    /// A copy, staged far below the world, rather than a second camera pointed at
    /// the real airship: the real one is lit by the scene, sits in front of
    /// whatever terrain happens to be behind it, and moves. The copy is inert,
    /// isolated and safe to spin.
    /// </summary>
    public sealed class AirshipPreviewRenderer : MonoBehaviour
    {
        // Matches the stage's aspect in the panel. A square target inside a wide
        // element gets letterboxed by scale-to-fit, which is what made the hull
        // look small with empty bands either side.
        private const int TextureWidth = 900;
        private const int TextureHeight = 640;
        private const float StageDepth = -6000f;
        private const float MinimumZoom = 0.55f;
        private const float MaximumZoom = 2.4f;

        private Transform _stage;
        private Transform _pivot;
        private Camera _camera;
        private RenderTexture _texture;
        private float _yaw = 35f;
        private float _pitch = 14f;
        private float _zoom = 1f;
        private float _framingDistance = 12f;
        private Vector2 _pan;

        public RenderTexture Texture => _texture;

        public bool IsReady => _camera != null && _texture != null;

        /// <summary>
        /// Builds the stage from a source hull. Safe to call again: the previous
        /// stage is discarded first.
        /// </summary>
        public void Build(Transform hullSource)
        {
            Dispose();
            if (hullSource == null)
            {
                return;
            }

            _stage = new GameObject("AIR_PreviewStage")
            {
                hideFlags = HideFlags.HideAndDontSave,
            }.transform;
            _stage.position = new Vector3(0f, StageDepth, 0f);

            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(_stage, false);

            var copy = Instantiate(hullSource.gameObject, _pivot);
            copy.name = "HullCopy";
            StripToVisuals(copy);

            // Recentre on the visual bounds, not on the transform: the exported
            // parts share the vehicle origin as their pivot, so the transform
            // says nothing about where the geometry actually is.
            if (TryGetBounds(copy.transform, out var bounds))
            {
                copy.transform.position -= bounds.center - _pivot.position;
                // Tight framing: the hull should fill the stage, not sit in the
                // middle of it. extents.magnitude is already the corner-to-centre
                // diagonal, so anything above ~1.2 leaves a wide empty margin.
                _framingDistance = Mathf.Max(bounds.extents.magnitude * 1.15f, 1f);
            }

            _texture = new RenderTexture(TextureWidth, TextureHeight, 24)
            {
                name = "RT_AirshipPreview",
                antiAliasing = 2,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _texture.Create();

            var cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(_stage, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.targetTexture = _texture;

            // Transparent clear so the panel's own glass shows through the
            // render instead of a grey box sitting on top of it.
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.fieldOfView = 40f;
            _camera.aspect = TextureWidth / (float)TextureHeight;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 500f;
            _camera.enabled = false;

            ApplyCamera();
        }

        public void Orbit(Vector2 delta)
        {
            _yaw += delta.x * 0.45f;
            _pitch = Mathf.Clamp(_pitch - (delta.y * 0.35f), -75f, 75f);
            ApplyCamera();
        }

        public void Pan(Vector2 delta)
        {
            _pan += new Vector2(-delta.x, delta.y) * 0.012f * _framingDistance;
            ApplyCamera();
        }

        public void Zoom(float delta)
        {
            _zoom = Mathf.Clamp(_zoom - (delta * 0.08f), MinimumZoom, MaximumZoom);
            ApplyCamera();
        }

        /// <summary>Renders one frame. Called only while the panel is open.</summary>
        public void RenderNow()
        {
            if (_camera != null)
            {
                _camera.Render();
            }
        }

        private void ApplyCamera()
        {
            if (_camera == null)
            {
                return;
            }

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var focus = _pivot.position
                + (rotation * new Vector3(_pan.x, _pan.y, 0f));
            _camera.transform.position =
                focus + (rotation * new Vector3(0f, 0f, -_framingDistance * _zoom));
            _camera.transform.rotation = rotation;
        }

        /// <summary>
        /// Keeps renderers and drops everything that would misbehave off-world:
        /// colliders, rigidbodies, particles, lights, cameras and every script.
        /// </summary>
        private static void StripToVisuals(GameObject copy)
        {
            var behaviours = copy.GetComponentsInChildren<MonoBehaviour>(true);
            for (var index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] != null)
                {
                    DestroyImmediate(behaviours[index]);
                }
            }

            DestroyAll(copy.GetComponentsInChildren<Collider>(true));
            DestroyAll(copy.GetComponentsInChildren<Rigidbody>(true));
            DestroyAll(copy.GetComponentsInChildren<ParticleSystem>(true));
            DestroyAll(copy.GetComponentsInChildren<Light>(true));
            DestroyAll(copy.GetComponentsInChildren<Camera>(true));
        }

        private static void DestroyAll(Component[] components)
        {
            for (var index = 0; index < components.Length; index++)
            {
                if (components[index] != null)
                {
                    DestroyImmediate(components[index]);
                }
            }
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
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

        private void Dispose()
        {
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
                _texture = null;
            }

            if (_stage != null)
            {
                Destroy(_stage.gameObject);
                _stage = null;
            }

            _camera = null;
            _pivot = null;
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
