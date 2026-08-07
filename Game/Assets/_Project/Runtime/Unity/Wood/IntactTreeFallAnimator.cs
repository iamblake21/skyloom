using System;
using UnityEngine;

namespace CML.Unity.Wood
{
    /// <summary>
    /// Heavy tree fall with four explicit physical phases: supported release,
    /// free fall, one real rebound, and grounded settlement.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class IntactTreeFallAnimator : MonoBehaviour
    {
        private const float MaximumAngularSpeed = 0.36f;
        private const float ReleaseRampDuration = 0.85f;
        private const float ReleaseTorqueHeightFactor = 0.042f;
        private const float MinimumDistalContactFraction = 0.30f;
        private const float MinimumImpactSpeed = 0.45f;
        private const float MinimumImpactAngle = 48f;
        private const float ReboundRestitution = 0.16f;
        private const float MinimumReboundPointSpeed = 0.16f;
        private const float MaximumReboundPointSpeed = 0.30f;
        private const float MaximumReboundImpulsePerMass = 0.20f;
        private const float MinimumReboundTime = 0.12f;
        private const float MaximumReboundTime = 0.58f;
        private const float SupportGraceSteps = 2.25f;
        private const float SettlementRampDuration = 0.65f;
        private const float SettledAngularDamping = 1.25f;
        private const float SettledLinearDamping = 0.55f;
        private const float QuietAngularSpeed = 0.060f;
        private const float QuietLinearSpeed = 0.080f;
        private const float QuietTimeRequired = 0.60f;
        private const float FreeFallSupportFallbackTime = 0.28f;

        private enum FallPhase
        {
            SupportedRelease,
            JointReleasePending,
            FreeFall,
            Rebound,
            Settlement,
            Complete
        }

        private Rigidbody _body;
        private HingeJoint _hinge;
        private Collider[] _fallingColliders = Array.Empty<Collider>();
        private Collider[] _initialSupports = Array.Empty<Collider>();
        private PhysicsMaterial _fallingMaterial;
        private Vector3 _fallAxis;
        private Vector3 _uprightDirection;
        private Vector3 _initialPivot;
        private float _trunkHeight;
        private float _hingeReleaseAngle;
        private float _elapsed;
        private float _phaseStartedAt;
        private float _lastSupportAt = float.NegativeInfinity;
        private float _quietTime;
        private float _freeFallSupportTime;
        private float _initialLinearDamping;
        private float _initialAngularDamping;
        private Vector3 _preStepLinearVelocity;
        private Vector3 _preStepAngularVelocity;
        private Vector3 _preStepCentreOfMass;
        private bool _hasEstablishedGroundSupport;
        private bool _separatedAfterImpact;
        private FallPhase _phase;

        public bool IsComplete => _phase == FallPhase.Complete;

        public void Configure(
            Rigidbody body,
            HingeJoint hinge,
            Collider[] fallingColliders,
            Collider[] initialSupports,
            PhysicsMaterial fallingMaterial,
            Vector3 fallAxis,
            Vector3 uprightDirection,
            Vector3 initialPivot,
            float trunkHeight,
            float releaseAngle)
        {
            _body = body;
            _hinge = hinge;
            _fallingColliders = fallingColliders
                                ?? Array.Empty<Collider>();
            _initialSupports = initialSupports
                               ?? Array.Empty<Collider>();
            _fallingMaterial = fallingMaterial;
            _fallAxis = fallAxis.normalized;
            _uprightDirection = uprightDirection.normalized;
            _initialPivot = initialPivot;
            _trunkHeight = Mathf.Max(0.1f, trunkHeight);
            // The factory owns the single beyond-balance margin. Adding a
            // second margin here would hold the base unnaturally long and
            // create a visible drop when the joint disappears.
            _hingeReleaseAngle = releaseAngle;
            _elapsed = 0f;
            _phaseStartedAt = 0f;
            _lastSupportAt = float.NegativeInfinity;
            _hasEstablishedGroundSupport = false;
            _quietTime = 0f;
            _freeFallSupportTime = 0f;
            _initialLinearDamping = body.linearDamping;
            _initialAngularDamping = body.angularDamping;
            CapturePreSimulationVelocity();
            _separatedAfterImpact = false;
            _phase = FallPhase.SupportedRelease;
        }

        private void FixedUpdate()
        {
            if (_phase == FallPhase.Complete || _body == null)
            {
                return;
            }

            _elapsed += Time.fixedDeltaTime;
            var fallAngle = ResolveFallAngle();
            switch (_phase)
            {
                case FallPhase.SupportedRelease:
                    AdvanceSupportedRelease(fallAngle);
                    break;
                case FallPhase.JointReleasePending:
                    AdvanceJointReleasePending();
                    break;
                case FallPhase.FreeFall:
                    AdvanceFreeFall(fallAngle);
                    break;
                case FallPhase.Rebound:
                    AdvanceRebound();
                    break;
                case FallPhase.Settlement:
                    AdvanceSettlement();
                    break;
            }

            CapturePreSimulationVelocity();
        }

        private void AdvanceSupportedRelease(float fallAngle)
        {
            if (_hinge == null)
            {
                EnterFreeFall();
                return;
            }

            var gravityTorque = Vector3.Dot(
                Vector3.Cross(
                    _body.worldCenterOfMass - _initialPivot,
                    Physics.gravity * _body.mass),
                _fallAxis);
            var backwardGravityCompensation = Mathf.Max(
                0f,
                -gravityTorque);
            var ramp = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(_elapsed / ReleaseRampDuration));
            var releaseTorque = _body.mass
                                * Physics.gravity.magnitude
                                * _trunkHeight
                                * ReleaseTorqueHeightFactor
                                * ramp;
            _body.AddTorque(
                _fallAxis
                * (backwardGravityCompensation + releaseTorque),
                ForceMode.Force);
            _body.WakeUp();

            if (fallAngle < _hingeReleaseAngle)
            {
                return;
            }

            // Destroy is deferred by Unity. Keep the base support ignored
            // until the following physics step, otherwise the still-alive
            // world joint and a restored terrain contact fight each other and
            // create the visible release hitch.
            Destroy(_hinge);
            _phase = FallPhase.JointReleasePending;
            _phaseStartedAt = _elapsed;
        }

        private void AdvanceJointReleasePending()
        {
            if (_hinge != null)
            {
                return;
            }

            _hinge = null;
            EnterFreeFall();
        }

        private void EnterFreeFall()
        {
            RestoreInitialSupportCollision();
            _body.maxAngularVelocity = MaximumAngularSpeed;
            _phase = FallPhase.FreeFall;
            _phaseStartedAt = _elapsed;
            _freeFallSupportTime = 0f;
        }

        private void AdvanceFreeFall(float fallAngle)
        {
            var isSupported = fallAngle >= MinimumImpactAngle
                              && HasCurrentSupport();
            var isQuiet = isSupported
                          && _body.angularVelocity.sqrMagnitude
                          <= QuietAngularSpeed * QuietAngularSpeed
                          && _body.linearVelocity.sqrMagnitude
                          <= QuietLinearSpeed * QuietLinearSpeed;
            _freeFallSupportTime = isQuiet
                ? _freeFallSupportTime + Time.fixedDeltaTime
                : 0f;
            if (_freeFallSupportTime >= FreeFallSupportFallbackTime)
            {
                BeginSettlement();
            }
        }

        private void AdvanceRebound()
        {
            var elapsedInPhase = _elapsed - _phaseStartedAt;
            var hasSupport = HasCurrentSupport();
            if (!hasSupport && elapsedInPhase >= MinimumReboundTime)
            {
                _separatedAfterImpact = true;
            }

            var returnedAfterSeparation =
                _separatedAfterImpact && hasSupport;
            var groundedFallback =
                elapsedInPhase >= MaximumReboundTime && hasSupport;
            if (returnedAfterSeparation || groundedFallback)
            {
                BeginSettlement();
            }
        }

        private void BeginSettlement()
        {
            _phase = FallPhase.Settlement;
            _phaseStartedAt = _elapsed;
            _quietTime = 0f;
            if (_fallingMaterial != null)
            {
                _fallingMaterial.bounciness = 0f;
                _fallingMaterial.bounceCombine =
                    PhysicsMaterialCombine.Minimum;
            }
        }

        private void AdvanceSettlement()
        {
            var ramp = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(
                    (_elapsed - _phaseStartedAt)
                    / SettlementRampDuration));
            _body.angularDamping = Mathf.Lerp(
                _initialAngularDamping,
                SettledAngularDamping,
                ramp);
            _body.linearDamping = Mathf.Lerp(
                _initialLinearDamping,
                SettledLinearDamping,
                ramp);

            var isGrounded = HasCurrentSupport();
            var isQuiet = isGrounded
                          && _body.angularVelocity.sqrMagnitude
                          <= QuietAngularSpeed * QuietAngularSpeed
                          && _body.linearVelocity.sqrMagnitude
                          <= QuietLinearSpeed * QuietLinearSpeed;
            _quietTime = isQuiet
                ? _quietTime + Time.fixedDeltaTime
                : 0f;
            if (_quietTime < QuietTimeRequired)
            {
                return;
            }

            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = true;
            _phase = FallPhase.Complete;
        }

        private float ResolveFallAngle()
        {
            var currentUp = transform.rotation * _uprightDirection;
            return Mathf.Max(
                0f,
                Vector3.SignedAngle(
                    _uprightDirection,
                    currentUp,
                    _fallAxis));
        }

        private bool HasCurrentSupport()
        {
            // Unity stops sending OnCollisionStay while a Rigidbody sleeps.
            // A dynamic body under gravity can only remain asleep after a
            // real supporting contact has been established, so sleeping is
            // the durable continuation of that contact. The timestamp grace
            // bridges callback ordering while the body is still awake.
            return (_hasEstablishedGroundSupport
                    && _body != null
                    && _body.IsSleeping())
                   || Time.fixedTime - _lastSupportAt
                   <= Time.fixedDeltaTime * SupportGraceSteps;
        }

        private void OnCollisionEnter(Collision collision)
        {
            RegisterContacts(collision, canBeginRebound: true);
        }

        private void OnCollisionStay(Collision collision)
        {
            RegisterContacts(collision, canBeginRebound: false);
        }

        private void RegisterContacts(
            Collision collision,
            bool canBeginRebound)
        {
            if (_body == null)
            {
                return;
            }

            var fallAngle = ResolveFallAngle();
            for (var index = 0;
                 index < collision.contactCount;
                 index++)
            {
                var contact = collision.GetContact(index);
                if (!IsGroundSupport(contact))
                {
                    continue;
                }

                // Grounding and first impact are deliberately separate. A
                // trunk can settle on any portion of its authored compound,
                // while only a new, sufficiently distal contact may trigger
                // the single visible rebound.
                _hasEstablishedGroundSupport = true;
                _lastSupportAt = Time.fixedTime;
                if (!canBeginRebound
                    || _phase != FallPhase.FreeFall
                    || fallAngle < MinimumImpactAngle
                    || !IsDistalImpactContact(contact))
                {
                    continue;
                }

                var incomingSpeed = ResolveIncomingSpeed(contact);
                if (incomingSpeed < MinimumImpactSpeed)
                {
                    continue;
                }

                ApplyBoundedRebound(contact, incomingSpeed);
                _phase = FallPhase.Rebound;
                _phaseStartedAt = _elapsed;
                _separatedAfterImpact = false;
                return;
            }
        }

        private void ApplyBoundedRebound(
            ContactPoint contact,
            float incomingSpeed)
        {
            // The project-wide PhysX bounce threshold is higher than the
            // typical distal speed of this deliberately heavy fall. Compute
            // one restitution impulse from the contact effective mass so the
            // trunk visibly yields once without ever receiving a launch-like
            // arbitrary force.
            var normal = contact.normal.normalized;
            var otherBody = contact.otherCollider != null
                ? contact.otherCollider.attachedRigidbody
                : null;
            var otherPointVelocity = otherBody != null
                ? otherBody.GetPointVelocity(contact.point)
                : Vector3.zero;
            var currentNormalSpeed = Vector3.Dot(
                _body.GetPointVelocity(contact.point)
                - otherPointVelocity,
                normal);
            var targetSpeed = Mathf.Clamp(
                incomingSpeed * ReboundRestitution,
                MinimumReboundPointSpeed,
                MaximumReboundPointSpeed);
            var requiredSpeedChange = targetSpeed
                                      - currentNormalSpeed;
            if (requiredSpeedChange <= 0f)
            {
                return;
            }

            var lever = contact.point - _body.worldCenterOfMass;
            var angularImpulse = Vector3.Cross(lever, normal);
            var inertiaRotation =
                _body.rotation * _body.inertiaTensorRotation;
            var localAngularImpulse =
                Quaternion.Inverse(inertiaRotation)
                * angularImpulse;
            var inertia = _body.inertiaTensor;
            localAngularImpulse = new Vector3(
                localAngularImpulse.x / Mathf.Max(0.0001f, inertia.x),
                localAngularImpulse.y / Mathf.Max(0.0001f, inertia.y),
                localAngularImpulse.z / Mathf.Max(0.0001f, inertia.z));
            var angularResponse = inertiaRotation
                                  * localAngularImpulse;
            var inverseEffectiveMass = 1f / _body.mass
                                       + Vector3.Dot(
                                           normal,
                                           Vector3.Cross(
                                               angularResponse,
                                               lever));
            if (!float.IsFinite(inverseEffectiveMass)
                || inverseEffectiveMass <= 0.000001f)
            {
                return;
            }

            var impulseMagnitude = Mathf.Min(
                requiredSpeedChange / inverseEffectiveMass,
                _body.mass * MaximumReboundImpulsePerMass);
            _body.AddForceAtPosition(
                normal * impulseMagnitude,
                contact.point,
                ForceMode.Impulse);
        }

        private float ResolveIncomingSpeed(ContactPoint contact)
        {
            var currentTreePointVelocity =
                _body.GetPointVelocity(contact.point);
            var preStepTreePointVelocity =
                _preStepLinearVelocity
                + Vector3.Cross(
                    _preStepAngularVelocity,
                    contact.point - _preStepCentreOfMass);
            var otherBody = contact.otherCollider != null
                ? contact.otherCollider.attachedRigidbody
                : null;
            var otherPointVelocity = otherBody != null
                ? otherBody.GetPointVelocity(contact.point)
                : Vector3.zero;
            var currentIncoming = Mathf.Max(
                0f,
                -Vector3.Dot(
                    currentTreePointVelocity - otherPointVelocity,
                    contact.normal));
            var preStepIncoming = Mathf.Max(
                0f,
                -Vector3.Dot(
                    preStepTreePointVelocity - otherPointVelocity,
                    contact.normal));
            return Mathf.Max(currentIncoming, preStepIncoming);
        }

        private void CapturePreSimulationVelocity()
        {
            if (_body == null)
            {
                return;
            }

            _preStepLinearVelocity = _body.linearVelocity;
            _preStepAngularVelocity = _body.angularVelocity;
            _preStepCentreOfMass = _body.worldCenterOfMass;
        }

        private static bool IsGroundSupport(ContactPoint contact)
        {
            var gravity = Physics.gravity;
            var supportUp = gravity.sqrMagnitude > 0.0001f
                ? -gravity.normalized
                : Vector3.up;
            return Vector3.Dot(contact.normal, supportUp) >= 0.45f;
        }

        private bool IsDistalImpactContact(ContactPoint contact)
        {
            var currentTrunkDirection =
                transform.rotation * _uprightDirection;
            var fromBase = contact.point - transform.position;
            var alongTrunk = Mathf.Abs(Vector3.Dot(
                fromBase,
                currentTrunkDirection));
            return alongTrunk
                   >= _trunkHeight * MinimumDistalContactFraction;
        }

        private void RestoreInitialSupportCollision()
        {
            if (_initialSupports.Length == 0
                || _fallingColliders.Length == 0)
            {
                return;
            }

            for (var colliderIndex = 0;
                 colliderIndex < _fallingColliders.Length;
                 colliderIndex++)
            {
                var fallingCollider =
                    _fallingColliders[colliderIndex];
                if (fallingCollider == null)
                {
                    continue;
                }

                for (var supportIndex = 0;
                     supportIndex < _initialSupports.Length;
                     supportIndex++)
                {
                    var support = _initialSupports[supportIndex];
                    if (support != null)
                    {
                        Physics.IgnoreCollision(
                            fallingCollider,
                            support,
                            false);
                    }
                }
            }

            _initialSupports = Array.Empty<Collider>();
        }

        private void OnDestroy()
        {
            RestoreInitialSupportCollision();
            if (_fallingMaterial != null)
            {
                Destroy(_fallingMaterial);
            }
        }
    }
}
