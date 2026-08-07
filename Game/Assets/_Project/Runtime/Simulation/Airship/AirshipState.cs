using System;
using System.Collections.Generic;
using CML.Foundation;

namespace CML.Simulation.Airship
{
    [Serializable]
    public sealed class AirshipEntityState
    {
        public AirshipEntityState(StableId id, AirshipPoseState pose)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("An airship requires a stable id.", nameof(id));
            }

            Id = id;
            Pose = pose;
            Mode = AirshipFlightMode.Anchored;
            PilotId = StableId.None;
            AcceptedLandingSurfaceId = StableId.None;
            DockedLandingSurfaceId = StableId.None;
            HeldInput = AirshipPilotInputState.None;

            // An airship is airworthy unless a scene explicitly authors the
            // wreck of the opening. Defaulting the other way would silently
            // ground every existing technical fixture.
            RepairStatus = AirshipRepairStatus.Repaired;
            InstalledIronPlates = AirshipRepairBill.RequiredIronPlates;
            InstalledInsulatedCables = AirshipRepairBill.RequiredInsulatedCables;
            RepairTicksRemaining = 0;
        }

        public StableId Id { get; }

        public AirshipPoseState Pose { get; internal set; }

        public AirshipFlightMode Mode { get; internal set; }

        public int LandingTicksRemaining { get; internal set; }

        public int ForwardSpeedMillimetresPerSecond { get; internal set; }

        public int StrafeSpeedMillimetresPerSecond { get; internal set; }

        public int VerticalSpeedMillimetresPerSecond { get; internal set; }

        public int YawRateTurnUnitsPerSecond { get; internal set; }

        public int PitchTurnUnits { get; internal set; }

        public int ForwardIntegrationRemainder { get; internal set; }

        public int StrafeIntegrationRemainder { get; internal set; }

        public int VerticalIntegrationRemainder { get; internal set; }

        public int YawIntegrationRemainder { get; internal set; }

        public AirshipPilotInputState HeldInput { get; internal set; }

        public StableId PilotId { get; internal set; }

        public StableId AcceptedLandingSurfaceId { get; internal set; }

        public StableId DockedLandingSurfaceId { get; internal set; }

        public AirshipLandingRequestResult LastLandingRequestResult { get; internal set; }

        public AirshipRepairStatus RepairStatus { get; internal set; }

        public int InstalledIronPlates { get; internal set; }

        public int InstalledInsulatedCables { get; internal set; }

        /// <summary>Ticks left of the eight-second repair; zero unless repairing.</summary>
        public int RepairTicksRemaining { get; internal set; }

        public bool IsBillSatisfied =>
            InstalledIronPlates >= AirshipRepairBill.RequiredIronPlates
            && InstalledInsulatedCables >= AirshipRepairBill.RequiredInsulatedCables;

        public AirshipEntityState DeepClone()
        {
            return new AirshipEntityState(Id, Pose)
            {
                Mode = Mode,
                LandingTicksRemaining = LandingTicksRemaining,
                ForwardSpeedMillimetresPerSecond = ForwardSpeedMillimetresPerSecond,
                StrafeSpeedMillimetresPerSecond = StrafeSpeedMillimetresPerSecond,
                VerticalSpeedMillimetresPerSecond = VerticalSpeedMillimetresPerSecond,
                YawRateTurnUnitsPerSecond = YawRateTurnUnitsPerSecond,
                PitchTurnUnits = PitchTurnUnits,
                ForwardIntegrationRemainder = ForwardIntegrationRemainder,
                StrafeIntegrationRemainder = StrafeIntegrationRemainder,
                VerticalIntegrationRemainder = VerticalIntegrationRemainder,
                YawIntegrationRemainder = YawIntegrationRemainder,
                HeldInput = HeldInput,
                PilotId = PilotId,
                AcceptedLandingSurfaceId = AcceptedLandingSurfaceId,
                DockedLandingSurfaceId = DockedLandingSurfaceId,
                LastLandingRequestResult = LastLandingRequestResult,
                RepairStatus = RepairStatus,
                InstalledIronPlates = InstalledIronPlates,
                InstalledInsulatedCables = InstalledInsulatedCables,
                RepairTicksRemaining = RepairTicksRemaining,
            };
        }
    }

    [Serializable]
    public sealed class AirshipPlayerState
    {
        public AirshipPlayerState(StableId id, AirshipPoseState worldPose)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("A player requires a stable id.", nameof(id));
            }

            Id = id;
            FrameKind = AirshipPlayerFrameKind.World;
            FrameAirshipId = StableId.None;
            QuantizedPose = worldPose;
        }

        public StableId Id { get; }

        public AirshipPlayerFrameKind FrameKind { get; internal set; }

        public StableId FrameAirshipId { get; internal set; }

        /// <summary>World pose on foot; airship-local pose while aboard.</summary>
        public AirshipPoseState QuantizedPose { get; internal set; }

        public bool IsPiloting { get; internal set; }

        public AirshipPlayerState DeepClone()
        {
            return new AirshipPlayerState(Id, QuantizedPose)
            {
                FrameKind = FrameKind,
                FrameAirshipId = FrameAirshipId,
                IsPiloting = IsPiloting,
            };
        }
    }

    /// <summary>Canonical axis-aligned obstacle used by authoritative flight collision.</summary>
    [Serializable]
    public sealed class AirshipObstacleState
    {
        public AirshipObstacleState(
            StableId id,
            AirshipVector3Millimetres minimum,
            AirshipVector3Millimetres maximum)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("An obstacle requires a stable id.", nameof(id));
            }

            if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z)
            {
                throw new ArgumentException("Obstacle minimum must not exceed its maximum.");
            }

            Id = id;
            Minimum = minimum;
            Maximum = maximum;
        }

        public StableId Id { get; }

        public AirshipVector3Millimetres Minimum { get; }

        public AirshipVector3Millimetres Maximum { get; }

        public AirshipObstacleState DeepClone()
        {
            return new AirshipObstacleState(Id, Minimum, Maximum);
        }
    }

    /// <summary>
    /// Canonical horizontal landing rectangle. A Unity probe can discover its id,
    /// but only this logical geometry may authorize a landing.
    /// </summary>
    [Serializable]
    public sealed class AirshipLandingSurfaceState
    {
        public AirshipLandingSurfaceState(
            StableId id,
            AirshipVector3Millimetres center,
            ushort yawTurn,
            int halfWidthMillimetres,
            int halfDepthMillimetres,
            StableId supportingObstacleId)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("A landing surface requires a stable id.", nameof(id));
            }

            if (halfWidthMillimetres <= 0 || halfDepthMillimetres <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfWidthMillimetres),
                    "Landing surface extents must be positive.");
            }

            Id = id;
            Center = center;
            YawTurn = yawTurn;
            HalfWidthMillimetres = halfWidthMillimetres;
            HalfDepthMillimetres = halfDepthMillimetres;
            SupportingObstacleId = supportingObstacleId;
        }

        public StableId Id { get; }

        public AirshipVector3Millimetres Center { get; }

        public ushort YawTurn { get; }

        public int HalfWidthMillimetres { get; }

        public int HalfDepthMillimetres { get; }

        public StableId SupportingObstacleId { get; }

        public AirshipLandingSurfaceState DeepClone()
        {
            return new AirshipLandingSurfaceState(
                Id,
                Center,
                YawTurn,
                HalfWidthMillimetres,
                HalfDepthMillimetres,
                SupportingObstacleId);
        }
    }

    /// <summary>
    /// Pure AIR-owned state. It is designed to be embedded in the global
    /// SimulationState and cloned at the same transaction boundary.
    /// </summary>
    [Serializable]
    public sealed class AirshipSimulationState
    {
        private readonly SortedDictionary<StableId, AirshipEntityState> _airships =
            new SortedDictionary<StableId, AirshipEntityState>();
        private readonly SortedDictionary<StableId, AirshipPlayerState> _players =
            new SortedDictionary<StableId, AirshipPlayerState>();
        private readonly SortedDictionary<StableId, AirshipObstacleState> _obstacles =
            new SortedDictionary<StableId, AirshipObstacleState>();
        private readonly SortedDictionary<StableId, AirshipLandingSurfaceState> _landingSurfaces =
            new SortedDictionary<StableId, AirshipLandingSurfaceState>();

        internal IEnumerable<KeyValuePair<StableId, AirshipEntityState>> Airships => _airships;

        internal IEnumerable<KeyValuePair<StableId, AirshipPlayerState>> Players => _players;

        internal IEnumerable<KeyValuePair<StableId, AirshipObstacleState>> Obstacles => _obstacles;

        internal IEnumerable<KeyValuePair<StableId, AirshipLandingSurfaceState>> LandingSurfaces =>
            _landingSurfaces;

        public int AirshipCount => _airships.Count;

        public int PlayerCount => _players.Count;

        public int ObstacleCount => _obstacles.Count;

        public int LandingSurfaceCount => _landingSurfaces.Count;

        internal void AddAirship(AirshipEntityState airship)
        {
            if (airship == null)
            {
                throw new ArgumentNullException(nameof(airship));
            }

            _airships.Add(airship.Id, airship);
        }

        internal void AddPlayer(AirshipPlayerState player)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            _players.Add(player.Id, player);
        }

        internal void AddObstacle(AirshipObstacleState obstacle)
        {
            if (obstacle == null)
            {
                throw new ArgumentNullException(nameof(obstacle));
            }

            _obstacles.Add(obstacle.Id, obstacle);
        }

        internal void AddLandingSurface(AirshipLandingSurfaceState surface)
        {
            if (surface == null)
            {
                throw new ArgumentNullException(nameof(surface));
            }

            _landingSurfaces.Add(surface.Id, surface);
        }

        public bool TryGetAirship(StableId id, out AirshipEntityState airship)
        {
            if (_airships.TryGetValue(id, out var authoritative))
            {
                airship = authoritative.DeepClone();
                return true;
            }

            airship = null;
            return false;
        }

        public bool TryGetPlayer(StableId id, out AirshipPlayerState player)
        {
            if (_players.TryGetValue(id, out var authoritative))
            {
                player = authoritative.DeepClone();
                return true;
            }

            player = null;
            return false;
        }

        public bool TryGetObstacle(StableId id, out AirshipObstacleState obstacle)
        {
            if (_obstacles.TryGetValue(id, out var authoritative))
            {
                obstacle = authoritative.DeepClone();
                return true;
            }

            obstacle = null;
            return false;
        }

        public bool TryGetLandingSurface(StableId id, out AirshipLandingSurfaceState surface)
        {
            if (_landingSurfaces.TryGetValue(id, out var authoritative))
            {
                surface = authoritative.DeepClone();
                return true;
            }

            surface = null;
            return false;
        }

        internal bool TryGetAirshipMutable(StableId id, out AirshipEntityState airship)
        {
            return _airships.TryGetValue(id, out airship);
        }

        internal bool TryGetPlayerMutable(StableId id, out AirshipPlayerState player)
        {
            return _players.TryGetValue(id, out player);
        }

        internal bool TryGetLandingSurfaceMutable(
            StableId id,
            out AirshipLandingSurfaceState surface)
        {
            return _landingSurfaces.TryGetValue(id, out surface);
        }

        internal bool RemovePlayer(StableId id)
        {
            if (!_players.Remove(id))
            {
                return false;
            }

            // Player destruction is authoritative cleanup, not merely a Unity
            // presentation event. No airship may retain stale occupancy or input.
            foreach (var pair in _airships)
            {
                if (pair.Value.PilotId == id)
                {
                    pair.Value.PilotId = StableId.None;
                    pair.Value.HeldInput = AirshipPilotInputState.None;
                }
            }

            return true;
        }

        public AirshipSimulationState DeepClone()
        {
            var clone = new AirshipSimulationState();
            foreach (var pair in _airships)
            {
                clone._airships.Add(pair.Key, pair.Value.DeepClone());
            }

            foreach (var pair in _players)
            {
                clone._players.Add(pair.Key, pair.Value.DeepClone());
            }

            foreach (var pair in _obstacles)
            {
                clone._obstacles.Add(pair.Key, pair.Value.DeepClone());
            }

            foreach (var pair in _landingSurfaces)
            {
                clone._landingSurfaces.Add(pair.Key, pair.Value.DeepClone());
            }

            return clone;
        }

        internal IReadOnlyList<StableId> GetPersistentIdsCanonical()
        {
            var ids = new StableId[
                checked(
                    _airships.Count
                    + _players.Count
                    + _obstacles.Count
                    + _landingSurfaces.Count)];
            var index = 0;
            foreach (var pair in _airships)
            {
                ids[index++] = pair.Key;
            }

            foreach (var pair in _players)
            {
                ids[index++] = pair.Key;
            }

            foreach (var pair in _obstacles)
            {
                ids[index++] = pair.Key;
            }

            foreach (var pair in _landingSurfaces)
            {
                ids[index++] = pair.Key;
            }

            Array.Sort(ids);
            return ids;
        }

        public void ValidateInvariants()
        {
            var persistentOwners = new Dictionary<StableId, string>();
            foreach (var pair in _airships)
            {
                RegisterPersistentId(persistentOwners, pair.Key, "airship");
                ValidateAirship(pair.Key, pair.Value);
            }

            foreach (var pair in _players)
            {
                RegisterPersistentId(persistentOwners, pair.Key, "player");
                ValidatePlayer(pair.Key, pair.Value);
            }

            foreach (var pair in _obstacles)
            {
                RegisterPersistentId(persistentOwners, pair.Key, "obstacle");
                if (pair.Key != pair.Value.Id)
                {
                    throw new SimulationInvariantException("Obstacle dictionary key does not match its id.");
                }

                ValidatePosition(pair.Value.Minimum);
                ValidatePosition(pair.Value.Maximum);
            }

            foreach (var pair in _landingSurfaces)
            {
                RegisterPersistentId(
                    persistentOwners,
                    pair.Key,
                    "landing surface");
                if (pair.Key != pair.Value.Id)
                {
                    throw new SimulationInvariantException(
                        "Landing-surface dictionary key does not match its id.");
                }

                ValidatePosition(pair.Value.Center);
                if (!pair.Value.SupportingObstacleId.IsNone
                    && !_obstacles.ContainsKey(pair.Value.SupportingObstacleId))
                {
                    throw new SimulationInvariantException(
                        "A landing surface references an unknown supporting obstacle.");
                }
            }
        }

        private static void RegisterPersistentId(
            IDictionary<StableId, string> owners,
            StableId id,
            string ownerKind)
        {
            if (id.IsNone)
            {
                throw new SimulationInvariantException(
                    $"A {ownerKind} cannot use the reserved zero persistent ID.");
            }

            if (owners.TryGetValue(id, out var existingKind))
            {
                throw new SimulationInvariantException(
                    $"Persistent ID {id} is assigned to both {existingKind} and {ownerKind}.");
            }

            owners.Add(id, ownerKind);
        }

        /// <summary>
        /// The three repair states are mutually exclusive and each one pins the
        /// counters and the countdown. Without this, a half-written state could
        /// present itself as flyable with nothing installed.
        /// </summary>
        private static void ValidateAirshipRepair(AirshipEntityState airship)
        {
            if (!Enum.IsDefined(typeof(AirshipRepairStatus), airship.RepairStatus))
            {
                throw new SimulationInvariantException(
                    "Airship repair status is invalid.");
            }

            RequireRange(
                airship.InstalledIronPlates,
                0,
                AirshipRepairBill.RequiredIronPlates,
                "installed iron plates");
            RequireRange(
                airship.InstalledInsulatedCables,
                0,
                AirshipRepairBill.RequiredInsulatedCables,
                "installed insulated cables");
            RequireRange(
                airship.RepairTicksRemaining,
                0,
                AirshipRepairBill.RepairDurationTicks,
                "repair ticks remaining");

            switch (airship.RepairStatus)
            {
                case AirshipRepairStatus.Damaged:
                    if (airship.IsBillSatisfied)
                    {
                        throw new SimulationInvariantException(
                            "A damaged airship cannot have a satisfied repair bill.");
                    }

                    if (airship.RepairTicksRemaining != 0)
                    {
                        throw new SimulationInvariantException(
                            "A damaged airship cannot be counting down a repair.");
                    }

                    break;

                case AirshipRepairStatus.Repairing:
                    if (!airship.IsBillSatisfied)
                    {
                        throw new SimulationInvariantException(
                            "A repairing airship must have a satisfied repair bill.");
                    }

                    if (airship.RepairTicksRemaining <= 0)
                    {
                        throw new SimulationInvariantException(
                            "A repairing airship must have ticks remaining.");
                    }

                    break;

                case AirshipRepairStatus.Repaired:
                    if (!airship.IsBillSatisfied)
                    {
                        throw new SimulationInvariantException(
                            "A repaired airship must have a satisfied repair bill.");
                    }

                    if (airship.RepairTicksRemaining != 0)
                    {
                        throw new SimulationInvariantException(
                            "A repaired airship cannot have ticks remaining.");
                    }

                    break;
            }

            if (airship.RepairStatus != AirshipRepairStatus.Repaired
                && !airship.PilotId.IsNone)
            {
                throw new SimulationInvariantException(
                    "An airship that is not repaired cannot hold a pilot.");
            }
        }

        private void ValidateAirship(StableId key, AirshipEntityState airship)
        {
            if (airship == null || key != airship.Id)
            {
                throw new SimulationInvariantException("Airship dictionary entry is invalid.");
            }

            ValidatePosition(airship.Pose.Position);
            if (!Enum.IsDefined(typeof(AirshipFlightMode), airship.Mode))
            {
                throw new SimulationInvariantException("Airship flight mode is invalid.");
            }

            RequireRange(
                airship.ForwardSpeedMillimetresPerSecond,
                -AirshipSimulationConstants.MaximumReverseSpeedMillimetresPerSecond,
                AirshipSimulationConstants.MaximumForwardSpeedMillimetresPerSecond,
                "forward speed");
            RequireRange(
                airship.StrafeSpeedMillimetresPerSecond,
                -AirshipSimulationConstants.MaximumStrafeSpeedMillimetresPerSecond,
                AirshipSimulationConstants.MaximumStrafeSpeedMillimetresPerSecond,
                "strafe speed");
            RequireRange(
                airship.VerticalSpeedMillimetresPerSecond,
                -AirshipSimulationConstants.MaximumVerticalSpeedMillimetresPerSecond,
                AirshipSimulationConstants.MaximumVerticalSpeedMillimetresPerSecond,
                "vertical speed");
            RequireRange(
                airship.YawRateTurnUnitsPerSecond,
                -AirshipSimulationConstants.MaximumYawRateTurnUnitsPerSecond,
                AirshipSimulationConstants.MaximumYawRateTurnUnitsPerSecond,
                "yaw rate");
            RequireRange(
                airship.PitchTurnUnits,
                -AirshipSimulationConstants.MaximumPitchTurnUnits,
                AirshipSimulationConstants.MaximumPitchTurnUnits,
                "pitch");
            ValidateAirshipRepair(airship);
            RequireRemainder(airship.ForwardIntegrationRemainder, "forward remainder");
            RequireRemainder(airship.StrafeIntegrationRemainder, "strafe remainder");
            RequireRemainder(airship.VerticalIntegrationRemainder, "vertical remainder");
            RequireRemainder(airship.YawIntegrationRemainder, "yaw remainder");

            if (airship.Mode == AirshipFlightMode.Stabilizing)
            {
                if (airship.LandingTicksRemaining <= 0
                    || airship.LandingTicksRemaining > AirshipSimulationConstants.LandingDurationTicks)
                {
                    throw new SimulationInvariantException(
                        "A stabilizing airship requires a valid remaining duration.");
                }

                if (airship.AcceptedLandingSurfaceId.IsNone
                    || !_landingSurfaces.ContainsKey(airship.AcceptedLandingSurfaceId))
                {
                    throw new SimulationInvariantException(
                        "A stabilizing airship requires a registered accepted surface.");
                }
            }
            else if (airship.LandingTicksRemaining != 0
                || !airship.AcceptedLandingSurfaceId.IsNone)
            {
                throw new SimulationInvariantException(
                    "Only a stabilizing airship may retain landing progress.");
            }

            if (!airship.DockedLandingSurfaceId.IsNone)
            {
                if (airship.Mode != AirshipFlightMode.Anchored
                    || !_landingSurfaces.ContainsKey(airship.DockedLandingSurfaceId))
                {
                    throw new SimulationInvariantException(
                        "A docked surface must be registered and belongs only to an anchored airship.");
                }
            }

            if (airship.Mode != AirshipFlightMode.Flying && HasMotion(airship))
            {
                throw new SimulationInvariantException(
                    "An anchored or stabilizing airship cannot retain motion.");
            }

            if (!airship.PilotId.IsNone)
            {
                if (!_players.TryGetValue(airship.PilotId, out var pilot)
                    || !pilot.IsPiloting
                    || pilot.FrameKind != AirshipPlayerFrameKind.Airship
                    || pilot.FrameAirshipId != airship.Id)
                {
                    throw new SimulationInvariantException(
                        "Airship pilot occupancy does not match canonical player state.");
                }
            }
            else if (airship.HeldInput != AirshipPilotInputState.None)
            {
                throw new SimulationInvariantException(
                    "An unoccupied pilot station cannot retain held input.");
            }
        }

        private void ValidatePlayer(StableId key, AirshipPlayerState player)
        {
            if (player == null || key != player.Id)
            {
                throw new SimulationInvariantException("Player dictionary entry is invalid.");
            }

            ValidatePosition(player.QuantizedPose.Position);
            if (!Enum.IsDefined(typeof(AirshipPlayerFrameKind), player.FrameKind))
            {
                throw new SimulationInvariantException("Player frame kind is invalid.");
            }

            if (player.FrameKind == AirshipPlayerFrameKind.World)
            {
                if (!player.FrameAirshipId.IsNone || player.IsPiloting)
                {
                    throw new SimulationInvariantException(
                        "A world-frame player cannot reference or pilot an airship.");
                }
            }
            else
            {
                if (player.FrameAirshipId.IsNone
                    || !_airships.TryGetValue(player.FrameAirshipId, out var airship))
                {
                    throw new SimulationInvariantException(
                        "An airship-frame player references an unknown airship.");
                }

                if (player.IsPiloting && airship.PilotId != player.Id)
                {
                    throw new SimulationInvariantException(
                        "Piloting player does not own the airship station.");
                }
            }

        }

        private static void ValidatePosition(AirshipVector3Millimetres value)
        {
            AirshipIntegerMath.ClampCoordinate(value.X);
            AirshipIntegerMath.ClampCoordinate(value.Y);
            AirshipIntegerMath.ClampCoordinate(value.Z);
        }

        private static bool HasMotion(AirshipEntityState state)
        {
            return state.ForwardSpeedMillimetresPerSecond != 0
                || state.StrafeSpeedMillimetresPerSecond != 0
                || state.VerticalSpeedMillimetresPerSecond != 0
                || state.YawRateTurnUnitsPerSecond != 0
                || state.PitchTurnUnits != 0
                || state.ForwardIntegrationRemainder != 0
                || state.StrafeIntegrationRemainder != 0
                || state.VerticalIntegrationRemainder != 0
                || state.YawIntegrationRemainder != 0;
        }

        private static void RequireRange(int value, int minimum, int maximum, string fieldName)
        {
            if (value < minimum || value > maximum)
            {
                throw new SimulationInvariantException(
                    $"{fieldName} is outside [{minimum}, {maximum}].");
            }
        }

        private static void RequireRemainder(int value, string fieldName)
        {
            if (value < 0 || value >= AirshipSimulationConstants.TicksPerSecond)
            {
                throw new SimulationInvariantException(
                    $"{fieldName} is not a Euclidean tick remainder.");
            }
        }
    }

    /// <summary>
    /// Explicit construction boundary for a new world. Build returns a deep copy;
    /// callers never receive the mutable state subsequently owned by an engine.
    /// </summary>
    public sealed class AirshipSimulationStateBuilder
    {
        private readonly AirshipSimulationState _state = new AirshipSimulationState();
        private readonly Dictionary<StableId, string> _persistentOwners =
            new Dictionary<StableId, string>();

        public AirshipSimulationStateBuilder AddAirship(
            StableId id,
            AirshipPoseState initialPose)
        {
            var airship = new AirshipEntityState(id, initialPose);
            ReservePersistentId(id, "airship", nameof(id));
            _state.AddAirship(airship);
            return this;
        }

        /// <summary>
        /// The wreck of the opening: nothing installed, nothing flying. Kept as
        /// a separate entry point so authoring the damaged hull is a deliberate
        /// act of a scene rather than a default every fixture inherits.
        /// </summary>
        public AirshipSimulationStateBuilder AddDamagedAirship(
            StableId id,
            AirshipPoseState initialPose)
        {
            var airship = new AirshipEntityState(id, initialPose)
            {
                RepairStatus = AirshipRepairStatus.Damaged,
                InstalledIronPlates = 0,
                InstalledInsulatedCables = 0,
                RepairTicksRemaining = 0,
            };
            ReservePersistentId(id, "airship", nameof(id));
            _state.AddAirship(airship);
            return this;
        }

        public AirshipSimulationStateBuilder AddPlayer(
            StableId id,
            AirshipPoseState initialWorldPose)
        {
            var player = new AirshipPlayerState(id, initialWorldPose);
            ReservePersistentId(id, "player", nameof(id));
            _state.AddPlayer(player);
            return this;
        }

        public AirshipSimulationStateBuilder AddAboardPlayer(
            StableId id,
            StableId airshipId,
            AirshipPoseState localPose,
            bool isPiloting)
        {
            if (!_state.TryGetAirshipMutable(airshipId, out var airship))
            {
                throw new ArgumentException(
                    "The referenced airship must be added before its passenger.",
                    nameof(airshipId));
            }

            var player = new AirshipPlayerState(id, localPose)
            {
                FrameKind = AirshipPlayerFrameKind.Airship,
                FrameAirshipId = airshipId,
                IsPiloting = isPiloting,
            };
            if (isPiloting && !airship.PilotId.IsNone)
            {
                throw new InvalidOperationException(
                    "An airship cannot be initialized with two pilots.");
            }

            ReservePersistentId(id, "player", nameof(id));
            if (isPiloting)
            {
                airship.PilotId = id;
            }

            _state.AddPlayer(player);
            return this;
        }

        public AirshipSimulationStateBuilder AddObstacle(
            StableId id,
            AirshipVector3Millimetres minimum,
            AirshipVector3Millimetres maximum)
        {
            var obstacle = new AirshipObstacleState(id, minimum, maximum);
            ReservePersistentId(id, "obstacle", nameof(id));
            _state.AddObstacle(obstacle);
            return this;
        }

        public AirshipSimulationStateBuilder AddLandingSurface(
            StableId id,
            AirshipVector3Millimetres center,
            ushort yawTurn,
            int halfWidthMillimetres,
            int halfDepthMillimetres,
            StableId supportingObstacleId)
        {
            var surface = new AirshipLandingSurfaceState(
                id,
                center,
                yawTurn,
                halfWidthMillimetres,
                halfDepthMillimetres,
                supportingObstacleId);
            ReservePersistentId(id, "landing surface", nameof(id));
            _state.AddLandingSurface(surface);
            return this;
        }

        public AirshipSimulationStateBuilder DockAirship(
            StableId airshipId,
            StableId landingSurfaceId)
        {
            if (!_state.TryGetAirshipMutable(airshipId, out var airship))
            {
                throw new ArgumentException("The docked airship is unknown.", nameof(airshipId));
            }

            if (!_state.TryGetLandingSurfaceMutable(landingSurfaceId, out _))
            {
                throw new ArgumentException(
                    "The docked landing surface is unknown.",
                    nameof(landingSurfaceId));
            }

            if (airship.Mode != AirshipFlightMode.Anchored)
            {
                throw new InvalidOperationException("Only an anchored airship can be docked.");
            }

            airship.DockedLandingSurfaceId = landingSurfaceId;
            return this;
        }

        public AirshipSimulationState Build()
        {
            _state.ValidateInvariants();
            return _state.DeepClone();
        }

        private void ReservePersistentId(
            StableId id,
            string ownerKind,
            string parameterName)
        {
            if (_persistentOwners.TryGetValue(id, out var existingKind))
            {
                throw new ArgumentException(
                    $"Persistent ID {id} is already assigned to {existingKind}; "
                    + $"it cannot also identify {ownerKind}.",
                    parameterName);
            }

            _persistentOwners.Add(id, ownerKind);
        }
    }
}
