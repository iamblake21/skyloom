using System;
using System.Collections.Generic;
using CML.Simulation.Machines;
using UnityEngine;

namespace CML.Unity.Presentation.Logistics
{
    /// <summary>
    /// Presentation-only animation for a belt module. The authored wooden
    /// battens move with the belt, the canvas scrolls and, optionally, the
    /// rollers spin. Gameplay state remains in the authoritative simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeltVisuals : MonoBehaviour
    {
        private const string RotatingPrefix = "ANM_";
        private const string BattenToken = "_Batten_";
        private const string LegacyDirectionMarkerRoot = "VIS_BeltDirection";
        private const float MinimumPathLength = 0.01f;
        private static readonly int BaseMapStId =
            Shader.PropertyToID("_BaseMap_ST");

        [SerializeField]
        [Tooltip("Shared canvas material whose UV offset represents the running band.")]
        private Material _bandMaterial;

        [SerializeField]
        [Tooltip("Metres of band travel per second at full speed.")]
        private float _metresPerSecond = 0.52f;

        [SerializeField]
        [Tooltip("Metres of band per texture repeat, from the Blender UV scale.")]
        private float _metresPerRepeat = 0.4545f;

        [SerializeField]
        [Tooltip("Spin this instance's rollers. Off by default: it costs per belt.")]
        private bool _spinRollers;

        [SerializeField]
        [Tooltip("Roller radius in metres, used to convert band speed into spin.")]
        private float _rollerRadius = 0.072f;

        private Transform[] _rotatingParts = Array.Empty<Transform>();
        private Transform[] _battens = Array.Empty<Transform>();
        private Vector3[] _battenReferencePositions = Array.Empty<Vector3>();
        private Quaternion[] _battenReferenceRotations =
            Array.Empty<Quaternion>();
        private float[] _battenPathDistances = Array.Empty<float>();
        private Vector3[] _lineOffsets = Array.Empty<Vector3>();
        private Vector3[] _arcReferenceRadials = Array.Empty<Vector3>();
        private BandRendererBinding[] _bandRenderers =
            Array.Empty<BandRendererBinding>();
        private MaterialPropertyBlock _bandProperties;

        private BattenPathKind _battenPathKind;
        private Vector3 _lineStart;
        private Vector3 _lineDirection = Vector3.forward;

        private Vector2 _arcCentre;
        private float _arcRadius;
        private float _arcStartAngle;
        private float _arcDirection = 1f;

        private float _battenPathLength;
        private float _offset;
        private Vector2 _bandTextureScale = Vector2.one;
        private Vector2 _bandTextureOffset;
        private float _directionSign = 1f;

        /// <summary>Normalised drive, 0 stopped and 1 full speed. Presentation only.</summary>
        public float Throttle { get; set; } = 1f;

        /// <summary>Called by the editor asset setup so the prefab ships wired up.</summary>
        public void Configure(Material bandMaterial)
        {
            _bandMaterial = bandMaterial;
        }

        /// <summary>
        /// Enables or disables the authored roller animation and immediately
        /// refreshes the cached parts. The belt kit uses this for the drive
        /// unit so existing scene instances do not need to be rebuilt.
        /// </summary>
        public void SetRollersEnabled(bool enabled)
        {
            _spinRollers = enabled;
            CacheRotatingParts();
        }

        /// <summary>
        /// Mirrors the authoritative belt-line result without owning it.
        /// Placement yaw chooses the belt axis; the Belt Drive defines travel direction.
        /// </summary>
        public void SetTravelDirection(BeltTravelDirection direction)
        {
            switch (direction)
            {
                case BeltTravelDirection.Forward:
                    _directionSign = 1f;
                    Throttle = 1f;
                    break;
                case BeltTravelDirection.Reverse:
                    _directionSign = -1f;
                    Throttle = 1f;
                    break;
                default:
                    _directionSign = 1f;
                    Throttle = 0f;
                    break;
            }
        }

        public void SetPowerRatio(float normalized)
        {
            Throttle = Mathf.Clamp01(normalized);
        }

        private void Awake()
        {
            // Older drive-unit prefab revisions serialized rollers as disabled.
            // The drive drum and side pulley are part of the authored ANM_ rig,
            // so recover the intended presentation behaviour even for those
            // already-placed instances.
            if (!_spinRollers && HasDriveRollerParts())
            {
                _spinRollers = true;
            }

            CacheRotatingParts();
            CacheBandRenderers();
            CacheBattens();
        }

        private void Start()
        {
            // Removes direction arrows generated by the previous presentation pass.
            // The belt's authored battens now communicate movement by themselves.
            var legacyMarkers = transform.Find(LegacyDirectionMarkerRoot);
            if (legacyMarkers != null)
            {
                Destroy(legacyMarkers.gameObject);
            }
        }

        private void OnValidate()
        {
            _metresPerRepeat = Mathf.Max(0.01f, _metresPerRepeat);
            _rollerRadius = Mathf.Max(0.001f, _rollerRadius);
        }

        private void CacheRotatingParts()
        {
            if (!_spinRollers)
            {
                _rotatingParts = Array.Empty<Transform>();
                return;
            }

            var found = new List<Transform>();
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform
                    && child.name.StartsWith(RotatingPrefix, StringComparison.Ordinal)
                    && IsRollerPart(child.name))
                {
                    found.Add(child);
                }
            }

            _rotatingParts = found.ToArray();
        }

        private bool HasDriveRollerParts()
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform && IsDriveRollerPart(child.name))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRollerPart(string name)
        {
            return !name.EndsWith("_Mesh", StringComparison.Ordinal)
                && (name.StartsWith("ANM_Roller", StringComparison.Ordinal)
                    || IsDriveRollerPart(name));
        }

        private static bool IsDriveRollerPart(string name)
        {
            return !name.EndsWith("_Mesh", StringComparison.Ordinal)
                && (name.StartsWith("ANM_DriveDrum", StringComparison.Ordinal)
                    || name.StartsWith("ANM_DrivePulley", StringComparison.Ordinal));
        }

        private void CacheBandRenderers()
        {
            if (_bandMaterial == null)
            {
                _bandRenderers = Array.Empty<BandRendererBinding>();
                return;
            }

            var found = new List<BandRendererBinding>();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    if (materials[materialIndex] == _bandMaterial)
                    {
                        found.Add(
                            new BandRendererBinding(renderer, materialIndex));
                    }
                }
            }

            _bandRenderers = found.ToArray();
            _bandProperties = new MaterialPropertyBlock();
            _bandTextureScale = _bandMaterial.GetTextureScale("_BaseMap");
            _bandTextureOffset = _bandMaterial.GetTextureOffset("_BaseMap");
        }

        private void CacheBattens()
        {
            var found = new List<Transform>();
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform
                    && child.name.IndexOf(BattenToken, StringComparison.Ordinal) >= 0)
                {
                    found.Add(child);
                }
            }

            found.Sort((left, right) =>
                string.CompareOrdinal(left.name, right.name));
            _battens = found.ToArray();

            if (_battens.Length < 2)
            {
                _battens = Array.Empty<Transform>();
                return;
            }

            _battenReferencePositions = new Vector3[_battens.Length];
            _battenReferenceRotations = new Quaternion[_battens.Length];
            _battenPathDistances = new float[_battens.Length];
            for (var index = 0; index < _battens.Length; index++)
            {
                // Imported FBX files may retain an axis-conversion parent.
                // Keep the whole path in module-root space rather than assuming
                // that every batten is an immediate child of this component.
                _battenReferencePositions[index] =
                    transform.InverseTransformPoint(_battens[index].position);
                _battenReferenceRotations[index] =
                    Quaternion.Inverse(transform.rotation)
                    * _battens[index].rotation;
            }

            if (TryConfigureArcPath())
            {
                return;
            }

            ConfigureLinePath();
        }

        private bool TryConfigureArcPath()
        {
            if (_battens.Length < 3)
            {
                return false;
            }

            var xRange = CoordinateRange(0);
            var zRange = CoordinateRange(2);
            if (xRange < 0.05f || zRange < 0.05f)
            {
                return false;
            }

            var first = ToPlane(_battenReferencePositions[0]);
            var middle = ToPlane(
                _battenReferencePositions[_battenReferencePositions.Length / 2]);
            var last = ToPlane(
                _battenReferencePositions[_battenReferencePositions.Length - 1]);
            if (!TryFindCircle(first, middle, last, out _arcCentre, out _arcRadius)
                || _arcRadius < MinimumPathLength)
            {
                return false;
            }

            var angles = new float[_battens.Length];
            angles[0] = AngleAroundCentre(first);
            for (var index = 1; index < angles.Length; index++)
            {
                var raw = AngleAroundCentre(
                    ToPlane(_battenReferencePositions[index]));
                angles[index] = angles[index - 1]
                    + Mathf.DeltaAngle(
                        angles[index - 1] * Mathf.Rad2Deg,
                        raw * Mathf.Rad2Deg)
                    * Mathf.Deg2Rad;
            }

            var averageStep =
                (angles[angles.Length - 1] - angles[0]) / (angles.Length - 1);
            if (Mathf.Abs(averageStep) < 0.001f)
            {
                return false;
            }

            _battenPathKind = BattenPathKind.Arc;
            _arcDirection = Mathf.Sign(averageStep);
            _arcStartAngle = angles[0] - averageStep * 0.5f;
            _battenPathLength =
                Mathf.Abs(averageStep) * _battens.Length * _arcRadius;
            _arcReferenceRadials = new Vector3[_battens.Length];

            for (var index = 0; index < _battens.Length; index++)
            {
                _battenPathDistances[index] =
                    Mathf.Repeat(
                        (angles[index] - _arcStartAngle)
                        * _arcDirection
                        * _arcRadius,
                        _battenPathLength);
                _arcReferenceRadials[index] = ArcRadial(angles[index]);
            }

            return true;
        }

        private void ConfigureLinePath()
        {
            var first = _battenReferencePositions[0];
            var last =
                _battenReferencePositions[_battenReferencePositions.Length - 1];
            var firstToLast = last - first;
            var averageStep = firstToLast.magnitude / (_battens.Length - 1);
            if (averageStep < MinimumPathLength)
            {
                _battens = Array.Empty<Transform>();
                _battenPathKind = BattenPathKind.None;
                return;
            }

            _battenPathKind = BattenPathKind.Line;
            _lineDirection = firstToLast.normalized;
            _lineStart = first - _lineDirection * (averageStep * 0.5f);
            _battenPathLength = averageStep * _battens.Length;
            _lineOffsets = new Vector3[_battens.Length];
            for (var index = 0; index < _battens.Length; index++)
            {
                var distance = Vector3.Dot(
                    _battenReferencePositions[index] - _lineStart,
                    _lineDirection);
                _battenPathDistances[index] = Mathf.Repeat(
                    distance,
                    _battenPathLength);
                _lineOffsets[index] =
                    _battenReferencePositions[index]
                    - (_lineStart + _lineDirection * distance);
            }
        }

        private void Update()
        {
            var travel = _metresPerSecond * Mathf.Clamp01(Throttle) * Time.deltaTime;
            if (Mathf.Approximately(travel, 0f))
            {
                return;
            }

            AdvanceBand(travel * _directionSign);
            AdvanceBattens(travel * _directionSign);
            SpinRollers(travel * _directionSign);
        }

        private void AdvanceBand(float signedTravel)
        {
            if (_bandRenderers.Length == 0)
            {
                return;
            }

            _offset = Mathf.Repeat(
                _offset + signedTravel / _metresPerRepeat,
                1f);
            var textureTransform = new Vector4(
                _bandTextureScale.x,
                _bandTextureScale.y,
                _bandTextureOffset.x,
                Mathf.Repeat(_bandTextureOffset.y + _offset, 1f));

            for (var index = 0; index < _bandRenderers.Length; index++)
            {
                var binding = _bandRenderers[index];
                binding.Renderer.GetPropertyBlock(
                    _bandProperties,
                    binding.MaterialIndex);
                _bandProperties.SetVector(BaseMapStId, textureTransform);
                binding.Renderer.SetPropertyBlock(
                    _bandProperties,
                    binding.MaterialIndex);
            }
        }

        private void AdvanceBattens(float signedTravel)
        {
            if (_battens.Length == 0 || _battenPathLength < MinimumPathLength)
            {
                return;
            }

            for (var index = 0; index < _battens.Length; index++)
            {
                _battenPathDistances[index] = Mathf.Repeat(
                    _battenPathDistances[index] + signedTravel,
                    _battenPathLength);

                if (_battenPathKind == BattenPathKind.Arc)
                {
                    PositionBattenOnArc(index);
                }
                else if (_battenPathKind == BattenPathKind.Line)
                {
                    PositionBattenOnLine(index);
                }
            }
        }

        private void PositionBattenOnLine(int index)
        {
            var position =
                _lineStart
                + _lineDirection * _battenPathDistances[index]
                + _lineOffsets[index];
            SetBattenRootPose(
                index,
                position,
                _battenReferenceRotations[index]);
        }

        private void PositionBattenOnArc(int index)
        {
            var distance = _battenPathDistances[index];
            var angle =
                _arcStartAngle + _arcDirection * distance / _arcRadius;
            var radial = ArcRadial(angle);
            var position = _battenReferencePositions[index];
            position.x = _arcCentre.x + radial.x * _arcRadius;
            position.z = _arcCentre.y + radial.z * _arcRadius;

            // Rotate by the actual radial delta. Interpolating between the
            // first and last authored rotations is subtly wrong because the
            // first and last battens sit half a spacing inside the path ends.
            // It also produces a visible snap when a batten wraps around.
            var rotation = Quaternion.FromToRotation(
                    _arcReferenceRadials[index],
                    radial)
                * _battenReferenceRotations[index];
            SetBattenRootPose(index, position, rotation);
        }

        private void SetBattenRootPose(
            int index,
            Vector3 rootPosition,
            Quaternion rootRotation)
        {
            _battens[index].SetPositionAndRotation(
                transform.TransformPoint(rootPosition),
                transform.rotation * rootRotation);
        }

        private static Vector3 ArcRadial(float angle)
        {
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        private void SpinRollers(float signedTravel)
        {
            if (_rotatingParts.Length == 0)
            {
                return;
            }

            var degrees =
                signedTravel / (2f * Mathf.PI * _rollerRadius) * 360f;
            for (var index = 0; index < _rotatingParts.Length; index++)
            {
                _rotatingParts[index].Rotate(degrees, 0f, 0f, Space.Self);
            }
        }

        private float CoordinateRange(int axis)
        {
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            for (var index = 0; index < _battenReferencePositions.Length; index++)
            {
                var value = Coordinate(_battenReferencePositions[index], axis);
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
            }

            return maximum - minimum;
        }

        private float AngleAroundCentre(Vector2 point)
        {
            return Mathf.Atan2(
                point.y - _arcCentre.y,
                point.x - _arcCentre.x);
        }

        private static Vector2 ToPlane(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        private static bool TryFindCircle(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            out Vector2 centre,
            out float radius)
        {
            var determinant =
                2f * (a.x * (b.y - c.y)
                    + b.x * (c.y - a.y)
                    + c.x * (a.y - b.y));
            if (Mathf.Abs(determinant) < 0.0001f)
            {
                centre = default;
                radius = 0f;
                return false;
            }

            var aSquared = a.sqrMagnitude;
            var bSquared = b.sqrMagnitude;
            var cSquared = c.sqrMagnitude;
            centre = new Vector2(
                (aSquared * (b.y - c.y)
                    + bSquared * (c.y - a.y)
                    + cSquared * (a.y - b.y)) / determinant,
                (aSquared * (c.x - b.x)
                    + bSquared * (a.x - c.x)
                    + cSquared * (b.x - a.x)) / determinant);
            radius = Vector2.Distance(centre, a);
            return true;
        }

        private static float Coordinate(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        private enum BattenPathKind
        {
            None,
            Line,
            Arc
        }

        private readonly struct BandRendererBinding
        {
            public BandRendererBinding(Renderer renderer, int materialIndex)
            {
                Renderer = renderer;
                MaterialIndex = materialIndex;
            }

            public Renderer Renderer { get; }

            public int MaterialIndex { get; }
        }
    }
}
