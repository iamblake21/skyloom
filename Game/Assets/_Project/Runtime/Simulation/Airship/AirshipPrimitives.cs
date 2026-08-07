using System;

namespace CML.Simulation.Airship
{
    /// <summary>
    /// Canonical logical position. One unit is one millimetre; floating-point values
    /// never cross the simulation boundary.
    /// </summary>
    [Serializable]
    public readonly struct AirshipVector3Millimetres : IEquatable<AirshipVector3Millimetres>
    {
        public static readonly AirshipVector3Millimetres Zero = default;

        public AirshipVector3Millimetres(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public long X { get; }

        public long Y { get; }

        public long Z { get; }

        public bool Equals(AirshipVector3Millimetres other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is AirshipVector3Millimetres other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public static AirshipVector3Millimetres operator +(
            AirshipVector3Millimetres left,
            AirshipVector3Millimetres right)
        {
            return new AirshipVector3Millimetres(
                checked(left.X + right.X),
                checked(left.Y + right.Y),
                checked(left.Z + right.Z));
        }

        public static AirshipVector3Millimetres operator -(
            AirshipVector3Millimetres left,
            AirshipVector3Millimetres right)
        {
            return new AirshipVector3Millimetres(
                checked(left.X - right.X),
                checked(left.Y - right.Y),
                checked(left.Z - right.Z));
        }

        public static bool operator ==(
            AirshipVector3Millimetres left,
            AirshipVector3Millimetres right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            AirshipVector3Millimetres left,
            AirshipVector3Millimetres right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Canonical pose. Yaw is a complete unsigned turn split into 65,536 units.
    /// Overflow is deliberate modular turn arithmetic.
    /// </summary>
    [Serializable]
    public readonly struct AirshipPoseState : IEquatable<AirshipPoseState>
    {
        public AirshipPoseState(AirshipVector3Millimetres position, ushort yawTurn)
        {
            Position = position;
            YawTurn = yawTurn;
        }

        public AirshipVector3Millimetres Position { get; }

        public ushort YawTurn { get; }

        public bool Equals(AirshipPoseState other)
        {
            return Position == other.Position && YawTurn == other.YawTurn;
        }

        public override bool Equals(object obj)
        {
            return obj is AirshipPoseState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Position.GetHashCode() * 397) ^ YawTurn.GetHashCode();
            }
        }

        public static bool operator ==(AirshipPoseState left, AirshipPoseState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AirshipPoseState left, AirshipPoseState right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Quantized pilot intent. Throttle changes the persistent airspeed, lift is
    /// held, while yaw and pitch are one-tick mouse impulses.
    /// </summary>
    [Serializable]
    public readonly struct AirshipPilotInputState : IEquatable<AirshipPilotInputState>
    {
        public static readonly AirshipPilotInputState None = default;

        public AirshipPilotInputState(
            int throttleChangePermille,
            int liftPermille,
            int yawDeltaPermille,
            int pitchDeltaPermille)
        {
            ThrottleChangePermille = RequireAxis(
                throttleChangePermille,
                nameof(throttleChangePermille));
            LiftPermille = RequireAxis(liftPermille, nameof(liftPermille));
            YawDeltaPermille = RequireAxis(
                yawDeltaPermille,
                nameof(yawDeltaPermille));
            PitchDeltaPermille = RequireAxis(
                pitchDeltaPermille,
                nameof(pitchDeltaPermille));
        }

        public short ThrottleChangePermille { get; }

        public short LiftPermille { get; }

        public short YawDeltaPermille { get; }

        public short PitchDeltaPermille { get; }

        public bool Equals(AirshipPilotInputState other)
        {
            return ThrottleChangePermille == other.ThrottleChangePermille
                && LiftPermille == other.LiftPermille
                && YawDeltaPermille == other.YawDeltaPermille
                && PitchDeltaPermille == other.PitchDeltaPermille;
        }

        public override bool Equals(object obj)
        {
            return obj is AirshipPilotInputState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ThrottleChangePermille.GetHashCode();
                hash = (hash * 397) ^ LiftPermille.GetHashCode();
                hash = (hash * 397) ^ YawDeltaPermille.GetHashCode();
                hash = (hash * 397) ^ PitchDeltaPermille.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(
            AirshipPilotInputState left,
            AirshipPilotInputState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            AirshipPilotInputState left,
            AirshipPilotInputState right)
        {
            return !left.Equals(right);
        }

        private static short RequireAxis(int value, string parameterName)
        {
            if (value < -1000 || value > 1000)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "A pilot input axis must be between -1000 and 1000.");
            }

            return (short)value;
        }
    }

    public enum AirshipFlightMode : byte
    {
        Anchored = 0,
        Flying = 1,
        Stabilizing = 2,
    }

    public enum AirshipPlayerFrameKind : byte
    {
        World = 0,
        Airship = 1,
    }

    public enum AirshipLandingRequestResult : byte
    {
        None = 0,
        Accepted = 1,
        AlreadyAnchored = 2,
        AlreadyStabilizing = 3,
        TooFast = 4,
        UnknownSurface = 5,
        SurfaceOutOfReach = 6,
    }

    /// <summary>
    /// Airworthiness of the hull. The opening of the game starts at
    /// <see cref="Damaged"/>: the transit accident is the reason the player has a
    /// first need at all. Piloting is refused until <see cref="Repaired"/>.
    /// </summary>
    public enum AirshipRepairStatus : byte
    {
        Damaged = 1,
        Repairing = 2,
        Repaired = 3,
    }

    /// <summary>
    /// Why an install was refused. Every refusal names a cause: a panel that
    /// could only say "non installato" would be the same dead end the plan
    /// forbids for machines.
    /// </summary>
    public enum AirshipRepairInstallResult : byte
    {
        None = 0,
        Installed = 1,
        AlreadyRepaired = 2,
        NotPartOfBill = 3,
        AlreadySatisfied = 4,
        MissingFromInventory = 5,
        UnknownAirship = 6,
        InvalidAmount = 7,
    }

    /// <summary>Normative flight and interaction constants for the first airship.</summary>
    public static class AirshipSimulationConstants
    {
        public const int TicksPerSecond = 20;
        public const int MaximumForwardSpeedMillimetresPerSecond = 20_000;
        public const int MaximumReverseSpeedMillimetresPerSecond = 6_000;
        public const int MaximumStrafeSpeedMillimetresPerSecond = 8_000;
        public const int MaximumVerticalSpeedMillimetresPerSecond = 6_000;
        public const int MaximumYawRateTurnUnitsPerSecond = 8_192;
        public const int AccelerationTicks = 80;
        public const int FullYawAuthoritySpeedMillimetresPerSecond = 4_000;
        public const int MaximumPitchTurnUnits = 2_731;
        public const int PitchChangeTurnUnitsPerTick = 192;
        public const int LandingSpeedThresholdMillimetresPerSecond = 4_000;
        public const int LandingDurationTicks = 120;

        public const long MaximumAbsoluteCoordinateMillimetres = 1_000_000_000L;

        public const int RampTipLocalXMillimetres = 2_030;
        public const int RampTipLocalYMillimetres = 300;
        public const int RampTipLocalZMillimetres = -160;
        public const int LandingMinimumReachMillimetres = 400;
        public const int LandingMaximumReachMillimetres = 2_500;
        public const int LandingMaximumHeightDeltaMillimetres = 350;
        public const int LandingRequiredCorridorWidthMillimetres = 800;
        public const int LandingRequiredPadDepthMillimetres = 900;
        public const int LandingSampleStepMillimetres = 50;

        public static readonly AirshipVector3Millimetres BoardingVolumeCenter =
            new AirshipVector3Millimetres(1_720, 780, -160);

        public static readonly AirshipVector3Millimetres BoardingVolumeHalfExtents =
            new AirshipVector3Millimetres(1_100, 650, 700);

        public static readonly AirshipVector3Millimetres PilotSeatCenter =
            new AirshipVector3Millimetres(0, 1_140, 3_300);

        public static readonly AirshipVector3Millimetres PilotSeatHalfExtents =
            new AirshipVector3Millimetres(700, 900, 600);

        // Feet position that places the first-person camera exactly on the
        // authored seated REF_PilotCamera at (0, 1.68, 2.46) metres.
        // The CharacterController is disabled while piloting, so the body root
        // may sit below the cockpit floor without producing a collision.
        public static readonly AirshipVector3Millimetres PilotViewBodyRootPosition =
            new AirshipVector3Millimetres(0, 30, 2_460);

        public static readonly AirshipVector3Millimetres PilotExitBodyRootPosition =
            new AirshipVector3Millimetres(550, 700, 550);
    }

    internal static class AirshipIntegerMath
    {
        public static long Abs(long value)
        {
            if (value == long.MinValue)
            {
                throw new OverflowException("The absolute value cannot be represented.");
            }

            return value < 0L ? -value : value;
        }

        public static int Abs(int value)
        {
            if (value == int.MinValue)
            {
                throw new OverflowException("The absolute value cannot be represented.");
            }

            return value < 0 ? -value : value;
        }

        public static long RoundDivideAwayFromZero(long numerator, long positiveDenominator)
        {
            if (positiveDenominator <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(positiveDenominator));
            }

            if (numerator == 0L)
            {
                return 0L;
            }

            var sign = numerator < 0L ? -1L : 1L;
            var magnitude = Abs(numerator);
            var quotient = magnitude / positiveDenominator;
            var remainder = magnitude % positiveDenominator;
            if (checked(remainder * 2L) >= positiveDenominator)
            {
                quotient = checked(quotient + 1L);
            }

            return checked(sign * quotient);
        }

        public static int IntegratePerSecond(int valuePerSecond, ref int euclideanRemainder)
        {
            var numerator = checked(valuePerSecond + euclideanRemainder);
            var quotient = numerator / AirshipSimulationConstants.TicksPerSecond;
            var remainder = numerator % AirshipSimulationConstants.TicksPerSecond;
            if (remainder < 0)
            {
                quotient = checked(quotient - 1);
                remainder += AirshipSimulationConstants.TicksPerSecond;
            }

            euclideanRemainder = remainder;
            return quotient;
        }

        public static int MoveTowards(int current, int target, int maximumDelta)
        {
            if (maximumDelta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDelta));
            }

            if (current < target)
            {
                return Math.Min(checked(current + maximumDelta), target);
            }

            if (current > target)
            {
                return Math.Max(checked(current - maximumDelta), target);
            }

            return current;
        }

        public static int ShortestTurnDelta(ushort from, ushort to)
        {
            return unchecked((short)(to - from));
        }

        public static ushort AddTurn(ushort yaw, int signedDelta)
        {
            return unchecked((ushort)(yaw + signedDelta));
        }

        public static long ClampCoordinate(long value)
        {
            if (value < -AirshipSimulationConstants.MaximumAbsoluteCoordinateMillimetres
                || value > AirshipSimulationConstants.MaximumAbsoluteCoordinateMillimetres)
            {
                throw new SimulationInvariantException(
                    "An airship coordinate exceeds the supported canonical world range.");
            }

            return value;
        }
    }
}
