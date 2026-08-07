using System;
using System.Collections.Generic;
using CML.Foundation;

namespace CML.Simulation.Airship
{
    /// <summary>
    /// AIR flight-state reducer. Player locomotion and collision are handled by
    /// Unity's CharacterController rather than duplicated in this model.
    /// </summary>
    public static class AirshipReducer
    {
        public static void ApplyPhaseOneCommands(
            AirshipSimulationState state,
            SimulationTick executingTick,
            IReadOnlyList<SimulationCommand> commands)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            var ordered = new List<SimulationCommand>(commands.Count);
            for (var index = 0; index < commands.Count; index++)
            {
                if (AirshipCommandKinds.IsAirshipCommand(commands[index].Kind))
                {
                    if (commands[index].TargetTick != executingTick)
                    {
                        throw new SimulationInvariantException(
                            "A phase-1 AIR command targets a different global tick.");
                    }

                    ordered.Add(commands[index]);
                }
            }

            ordered.Sort(SimulationCommandComparer.Instance);
            for (var index = 0; index < ordered.Count; index++)
            {
                ApplyCommand(state, ordered[index]);
            }

            state.ValidateInvariants();
        }

        public static void AdvancePhaseTwo(AirshipSimulationState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            foreach (var pair in state.Airships)
            {
                AdvanceAirship(state, pair.Value);
            }

            state.ValidateInvariants();
        }

        private static void ApplyCommand(
            AirshipSimulationState state,
            SimulationCommand command)
        {
            switch (command.Kind)
            {
                case AirshipCommandKinds.PilotInput:
                    ApplyPilotInput(state, command);
                    break;
                case AirshipCommandKinds.Takeoff:
                    AirshipCommandCodec.ValidateEmpty(command);
                    ApplyTakeoff(state, command.InitiatorId, command.DestinationId);
                    break;
                case AirshipCommandKinds.LandingRequest:
                    ApplyLandingRequest(
                        state,
                        command.InitiatorId,
                        command.DestinationId,
                        AirshipCommandCodec.DecodeLandingSurfaceId(command));
                    break;
                case AirshipCommandKinds.Board:
                    AirshipCommandCodec.ValidateEmpty(command);
                    ApplyBoard(state, command.InitiatorId, command.DestinationId);
                    break;
                case AirshipCommandKinds.Disembark:
                    AirshipCommandCodec.ValidateEmpty(command);
                    ApplyDisembark(state, command.InitiatorId, command.DestinationId);
                    break;
                case AirshipCommandKinds.PilotBegin:
                    AirshipCommandCodec.ValidateEmpty(command);
                    ApplyPilotBegin(state, command.InitiatorId, command.DestinationId);
                    break;
                case AirshipCommandKinds.PilotEnd:
                    AirshipCommandCodec.ValidateEmpty(command);
                    ApplyPilotEnd(state, command.InitiatorId, command.DestinationId);
                    break;
                case AirshipCommandKinds.PlayerDestroyed:
                    AirshipCommandCodec.ValidateForIngress(command);
                    state.RemovePlayer(command.InitiatorId);
                    break;
                case AirshipCommandKinds.RepairInstall:
                    // Deliberately inert here. Installing moves matter out of an
                    // inventory, so it belongs to the validated-transfer commit
                    // in phase 9 next to the transfer rule, not to the phase-1
                    // flight commands. It is listed so the default arm keeps
                    // meaning "nobody owns this kind".
                    break;
                default:
                    throw new SimulationInvariantException(
                        $"Unhandled AIR command kind '{command.Kind}'.");
            }
        }

        private static void ApplyPilotInput(
            AirshipSimulationState state,
            SimulationCommand command)
        {
            var input = AirshipCommandCodec.DecodePilotInput(command);
            if (!TryGetPilotedAirship(
                    state,
                    command.InitiatorId,
                    command.DestinationId,
                    out var airship)
                || airship.Mode == AirshipFlightMode.Stabilizing)
            {
                return;
            }

            // The throttle itself releases an anchored airship. There is no
            // separate takeoff button in the player-facing control contract.
            if (airship.Mode == AirshipFlightMode.Anchored)
            {
                if (input.ThrottleChangePermille == 0)
                {
                    return;
                }

                airship.Mode = AirshipFlightMode.Flying;
                airship.DockedLandingSurfaceId = StableId.None;
                airship.LastLandingRequestResult = AirshipLandingRequestResult.None;
                ResetMotion(airship);
            }

            airship.HeldInput = input;
        }

        private static void ApplyTakeoff(
            AirshipSimulationState state,
            StableId playerId,
            StableId airshipId)
        {
            if (!TryGetPilotedAirship(state, playerId, airshipId, out var airship)
                || airship.Mode != AirshipFlightMode.Anchored)
            {
                return;
            }

            airship.Mode = AirshipFlightMode.Flying;
            airship.DockedLandingSurfaceId = StableId.None;
            airship.LastLandingRequestResult = AirshipLandingRequestResult.None;
            ResetMotion(airship);
        }

        private static void ApplyLandingRequest(
            AirshipSimulationState state,
            StableId playerId,
            StableId airshipId,
            StableId surfaceId)
        {
            if (!TryGetPilotedAirship(state, playerId, airshipId, out var airship))
            {
                return;
            }

            if (airship.Mode == AirshipFlightMode.Anchored)
            {
                airship.LastLandingRequestResult = AirshipLandingRequestResult.AlreadyAnchored;
                return;
            }

            if (airship.Mode == AirshipFlightMode.Stabilizing)
            {
                airship.LastLandingRequestResult =
                    AirshipLandingRequestResult.AlreadyStabilizing;
                return;
            }

            if (!IsBelowLandingSpeed(airship))
            {
                airship.LastLandingRequestResult = AirshipLandingRequestResult.TooFast;
                return;
            }

            if (!state.TryGetLandingSurfaceMutable(surfaceId, out _))
            {
                airship.LastLandingRequestResult = AirshipLandingRequestResult.UnknownSurface;
                return;
            }

            if (!AirshipLandingValidator.IsReachable(state, airship.Pose, surfaceId))
            {
                airship.LastLandingRequestResult =
                    AirshipLandingRequestResult.SurfaceOutOfReach;
                return;
            }

            airship.Mode = AirshipFlightMode.Stabilizing;
            airship.LandingTicksRemaining = AirshipSimulationConstants.LandingDurationTicks;
            airship.AcceptedLandingSurfaceId = surfaceId;
            airship.LastLandingRequestResult = AirshipLandingRequestResult.Accepted;
            airship.HeldInput = AirshipPilotInputState.None;
            ResetMotion(airship);
        }

        private static void ApplyBoard(
            AirshipSimulationState state,
            StableId playerId,
            StableId airshipId)
        {
            if (!state.TryGetPlayerMutable(playerId, out var player)
                || player.FrameKind != AirshipPlayerFrameKind.World
                || !state.TryGetAirshipMutable(airshipId, out var airship)
                || airship.Mode != AirshipFlightMode.Anchored)
            {
                return;
            }

            var relativeWorld = player.QuantizedPose.Position - airship.Pose.Position;
            var localPosition = FixedTurnTrig.RotateWorldToLocal(
                relativeWorld,
                airship.Pose.YawTurn);
            player.FrameKind = AirshipPlayerFrameKind.Airship;
            player.FrameAirshipId = airship.Id;
            player.QuantizedPose = new AirshipPoseState(
                localPosition,
                unchecked((ushort)(
                    player.QuantizedPose.YawTurn - airship.Pose.YawTurn)));
        }

        private static void ApplyDisembark(
            AirshipSimulationState state,
            StableId playerId,
            StableId airshipId)
        {
            if (!state.TryGetPlayerMutable(playerId, out var player)
                || player.FrameKind != AirshipPlayerFrameKind.Airship
                || player.FrameAirshipId != airshipId
                || player.IsPiloting
                || !state.TryGetAirshipMutable(airshipId, out var airship)
                || airship.Mode != AirshipFlightMode.Anchored)
            {
                return;
            }

            if (airship.DockedLandingSurfaceId.IsNone
                || !AirshipLandingValidator.TryGetDisembarkPoint(
                    state,
                    airship.Pose,
                    airship.DockedLandingSurfaceId,
                    out var worldPosition))
            {
                return;
            }

            player.QuantizedPose = new AirshipPoseState(
                worldPosition,
                unchecked((ushort)(
                    player.QuantizedPose.YawTurn + airship.Pose.YawTurn)));
            player.FrameKind = AirshipPlayerFrameKind.World;
            player.FrameAirshipId = StableId.None;
        }

        private static void ApplyPilotBegin(
            AirshipSimulationState state,
            StableId playerId,
            StableId airshipId)
        {
            if (!state.TryGetPlayerMutable(playerId, out var player)
                || player.FrameKind != AirshipPlayerFrameKind.Airship
                || player.FrameAirshipId != airshipId
                || player.IsPiloting
                || !state.TryGetAirshipMutable(airshipId, out var airship)
                || !airship.PilotId.IsNone
                // The wreck of the opening is not flyable. This is the
                // authoritative gate: presentation only changes the wording of
                // the prompt.
                || airship.RepairStatus != AirshipRepairStatus.Repaired)
            {
                return;
            }

            player.QuantizedPose = new AirshipPoseState(
                AirshipSimulationConstants.PilotViewBodyRootPosition,
                0);
            player.IsPiloting = true;
            airship.PilotId = player.Id;
            airship.HeldInput = AirshipPilotInputState.None;
        }

        private static void ApplyPilotEnd(
            AirshipSimulationState state,
            StableId playerId,
            StableId airshipId)
        {
            if (!state.TryGetAirshipMutable(airshipId, out var airship)
                || airship.PilotId != playerId
                || airship.Mode == AirshipFlightMode.Stabilizing)
            {
                return;
            }

            airship.PilotId = StableId.None;
            airship.HeldInput = AirshipPilotInputState.None;
            if (state.TryGetPlayerMutable(playerId, out var player))
            {
                player.QuantizedPose = new AirshipPoseState(
                    AirshipSimulationConstants.PilotExitBodyRootPosition,
                    0);
                player.IsPiloting = false;
            }
        }

        /// <summary>
        /// The eight seconds of work between a satisfied bill and an airworthy
        /// hull. The components are already consumed at this point: the
        /// countdown cannot fail, only finish.
        /// </summary>
        private static void AdvanceRepair(AirshipEntityState airship)
        {
            if (airship.RepairStatus != AirshipRepairStatus.Repairing)
            {
                return;
            }

            airship.RepairTicksRemaining--;
            if (airship.RepairTicksRemaining <= 0)
            {
                airship.RepairTicksRemaining = 0;
                airship.RepairStatus = AirshipRepairStatus.Repaired;
            }
        }

        private static void AdvanceAirship(
            AirshipSimulationState state,
            AirshipEntityState airship)
        {
            // Before the anchored early-out: a repairing hull is anchored by
            // definition, so a countdown placed below would never tick.
            AdvanceRepair(airship);

            if (airship.Mode == AirshipFlightMode.Anchored)
            {
                airship.HeldInput = AirshipPilotInputState.None;
                ResetMotion(airship);
                return;
            }

            if (airship.Mode == AirshipFlightMode.Stabilizing)
            {
                airship.HeldInput = AirshipPilotInputState.None;
                ResetMotion(airship);
                airship.LandingTicksRemaining--;
                if (airship.LandingTicksRemaining == 0)
                {
                    airship.Mode = AirshipFlightMode.Anchored;
                    airship.DockedLandingSurfaceId =
                        airship.AcceptedLandingSurfaceId;
                    airship.AcceptedLandingSurfaceId = StableId.None;
                }

                return;
            }

            var currentPitch = airship.PitchTurnUnits;
            UpdateFlightControls(airship);
            var verticalRemainder = airship.VerticalIntegrationRemainder;
            var forwardRemainder = airship.ForwardIntegrationRemainder;
            var yawRemainder = airship.YawIntegrationRemainder;
            FixedTurnTrig.SinCos(
                unchecked((ushort)airship.PitchTurnUnits),
                out var pitchSine,
                out var pitchCosine);
            var horizontalForwardSpeed = checked((int)
                AirshipIntegerMath.RoundDivideAwayFromZero(
                    checked(
                        (long)airship.ForwardSpeedMillimetresPerSecond
                        * pitchCosine),
                    FixedTurnTrig.One));
            var pitchVerticalSpeed = checked((int)
                AirshipIntegerMath.RoundDivideAwayFromZero(
                    checked(
                        -(long)AirshipIntegerMath.Abs(
                            airship.ForwardSpeedMillimetresPerSecond)
                        * pitchSine),
                    FixedTurnTrig.One));
            var localY = AirshipIntegerMath.IntegratePerSecond(
                checked(
                    airship.VerticalSpeedMillimetresPerSecond
                    + pitchVerticalSpeed),
                ref verticalRemainder);
            var localZ = AirshipIntegerMath.IntegratePerSecond(
                horizontalForwardSpeed,
                ref forwardRemainder);
            var yawDelta = AirshipIntegerMath.IntegratePerSecond(
                airship.YawRateTurnUnitsPerSecond,
                ref yawRemainder);
            airship.StrafeIntegrationRemainder = 0;
            airship.VerticalIntegrationRemainder = verticalRemainder;
            airship.ForwardIntegrationRemainder = forwardRemainder;
            airship.YawIntegrationRemainder = yawRemainder;
            var candidateYaw = AirshipIntegerMath.AddTurn(airship.Pose.YawTurn, yawDelta);
            var worldDelta = FixedTurnTrig.RotateLocalToWorld(
                new AirshipVector3Millimetres(0, localY, localZ),
                candidateYaw);
            var candidate = new AirshipPoseState(
                airship.Pose.Position + worldDelta,
                candidateYaw);

            AirshipIntegerMath.ClampCoordinate(candidate.Position.X);
            AirshipIntegerMath.ClampCoordinate(candidate.Position.Y);
            AirshipIntegerMath.ClampCoordinate(candidate.Position.Z);
            if (!AirshipCollision.IsCandidateClear(
                    state,
                    airship.Pose,
                    currentPitch,
                    candidate,
                    airship.PitchTurnUnits))
            {
                // Translation, yaw and pitch are one atomic candidate.
                airship.PitchTurnUnits = currentPitch;
                ResetMotion(airship, false);
                return;
            }

            airship.Pose = candidate;
        }

        private static void UpdateFlightControls(AirshipEntityState airship)
        {
            var input = airship.HeldInput;
            var throttleStep = checked(
                (AirshipSimulationConstants.MaximumForwardSpeedMillimetresPerSecond
                    + AirshipSimulationConstants.AccelerationTicks - 1)
                / AirshipSimulationConstants.AccelerationTicks);
            var throttleDelta = checked(
                (throttleStep * input.ThrottleChangePermille) / 1000);
            var verticalTarget = ScaleInput(
                AirshipSimulationConstants.MaximumVerticalSpeedMillimetresPerSecond,
                input.LiftPermille);

            airship.ForwardSpeedMillimetresPerSecond = Math.Max(
                -AirshipSimulationConstants.MaximumReverseSpeedMillimetresPerSecond,
                Math.Min(
                    AirshipSimulationConstants.MaximumForwardSpeedMillimetresPerSecond,
                    checked(
                        airship.ForwardSpeedMillimetresPerSecond
                        + throttleDelta)));
            airship.StrafeSpeedMillimetresPerSecond = 0;
            airship.VerticalSpeedMillimetresPerSecond = AdvanceAxis(
                airship.VerticalSpeedMillimetresPerSecond,
                verticalTarget,
                AirshipSimulationConstants.MaximumVerticalSpeedMillimetresPerSecond,
                AirshipSimulationConstants.MaximumVerticalSpeedMillimetresPerSecond);
            var absoluteForwardSpeed = AirshipIntegerMath.Abs(
                airship.ForwardSpeedMillimetresPerSecond);
            if (absoluteForwardSpeed == 0)
            {
                airship.YawRateTurnUnitsPerSecond = 0;
                airship.YawIntegrationRemainder = 0;
                return;
            }

            var yawAuthorityPermille = Math.Min(
                1000,
                checked(
                    absoluteForwardSpeed * 1000
                    / AirshipSimulationConstants.FullYawAuthoritySpeedMillimetresPerSecond));
            airship.YawRateTurnUnitsPerSecond = checked((int)
                AirshipIntegerMath.RoundDivideAwayFromZero(
                    checked(
                        (long)AirshipSimulationConstants
                            .MaximumYawRateTurnUnitsPerSecond
                        * input.YawDeltaPermille
                        * yawAuthorityPermille),
                    1_000_000));

            var pitchDelta = checked((int)
                AirshipIntegerMath.RoundDivideAwayFromZero(
                    checked(
                        (long)AirshipSimulationConstants.PitchChangeTurnUnitsPerTick
                        * input.PitchDeltaPermille),
                    1000));
            airship.PitchTurnUnits = Math.Max(
                -AirshipSimulationConstants.MaximumPitchTurnUnits,
                Math.Min(
                    AirshipSimulationConstants.MaximumPitchTurnUnits,
                    checked(airship.PitchTurnUnits + pitchDelta)));
        }

        private static bool TryGetPilotedAirship(
            AirshipSimulationState state,
            StableId playerId,
            StableId airshipId,
            out AirshipEntityState airship)
        {
            return state.TryGetAirshipMutable(airshipId, out airship)
                && airship.PilotId == playerId
                && state.TryGetPlayerMutable(playerId, out var player)
                && player.IsPiloting
                && player.FrameAirshipId == airshipId;
        }

        private static int AdvanceAxis(
            int current,
            int target,
            int positiveMaximum,
            int negativeMaximum)
        {
            if (current != 0 && target != 0 && Math.Sign(current) != Math.Sign(target))
            {
                target = 0;
            }

            var relevantMaximum = current < 0 || (current == 0 && target < 0)
                ? negativeMaximum
                : positiveMaximum;
            var maximumDelta = checked(
                (relevantMaximum + AirshipSimulationConstants.AccelerationTicks - 1)
                / AirshipSimulationConstants.AccelerationTicks);
            return AirshipIntegerMath.MoveTowards(current, target, maximumDelta);
        }

        private static int ScaleInput(int maximum, int inputPermille)
        {
            return checked((maximum * inputPermille) / 1000);
        }

        private static bool IsBelowLandingSpeed(AirshipEntityState airship)
        {
            var threshold =
                (long)AirshipSimulationConstants.LandingSpeedThresholdMillimetresPerSecond;
            var speedSquared = checked(
                ((long)airship.ForwardSpeedMillimetresPerSecond
                    * airship.ForwardSpeedMillimetresPerSecond)
                + ((long)airship.StrafeSpeedMillimetresPerSecond
                    * airship.StrafeSpeedMillimetresPerSecond)
                + ((long)airship.VerticalSpeedMillimetresPerSecond
                    * airship.VerticalSpeedMillimetresPerSecond));
            return speedSquared < checked(threshold * threshold);
        }

        private static void ResetMotion(
            AirshipEntityState airship,
            bool resetPitch = true)
        {
            airship.ForwardSpeedMillimetresPerSecond = 0;
            airship.StrafeSpeedMillimetresPerSecond = 0;
            airship.VerticalSpeedMillimetresPerSecond = 0;
            airship.YawRateTurnUnitsPerSecond = 0;
            airship.ForwardIntegrationRemainder = 0;
            airship.StrafeIntegrationRemainder = 0;
            airship.VerticalIntegrationRemainder = 0;
            airship.YawIntegrationRemainder = 0;
            if (resetPitch)
            {
                airship.PitchTurnUnits = 0;
            }
        }

        /// <summary>
        /// Publishes the physical world's rejection of a just-committed flight
        /// candidate. Unity supplies only the last safe quantized pose; AIR remains
        /// the owner of pose and inertia after the correction.
        /// </summary>
        internal static bool ResolveWorldCollision(
            AirshipSimulationState state,
            StableId airshipId,
            AirshipPoseState lastSafePose,
            int lastSafePitchTurnUnits)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!state.TryGetAirshipMutable(airshipId, out var airship)
                || airship.Mode != AirshipFlightMode.Flying)
            {
                return false;
            }

            AirshipIntegerMath.ClampCoordinate(lastSafePose.Position.X);
            AirshipIntegerMath.ClampCoordinate(lastSafePose.Position.Y);
            AirshipIntegerMath.ClampCoordinate(lastSafePose.Position.Z);
            if (lastSafePitchTurnUnits
                    < -AirshipSimulationConstants.MaximumPitchTurnUnits
                || lastSafePitchTurnUnits
                    > AirshipSimulationConstants.MaximumPitchTurnUnits)
            {
                throw new ArgumentOutOfRangeException(nameof(lastSafePitchTurnUnits));
            }

            airship.Pose = lastSafePose;
            airship.PitchTurnUnits = lastSafePitchTurnUnits;
            ResetMotion(airship, false);
            return true;
        }
    }
}
