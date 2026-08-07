using System;

namespace CML.Simulation
{
    internal static class SimulationCommandIngress
    {
        public static void Validate(SimulationCommand command)
        {
            if (!command.IsWellFormed)
            {
                throw new ArgumentException("A command must have a non-empty known kind.", nameof(command));
            }

            switch (command.Kind)
            {
                case SimulationCommandKinds.NoOp:
                    if (command.QuantizedValue != 0L
                        || !command.DestinationId.IsNone)
                    {
                        throw new ArgumentException(
                            "A no-op command cannot carry a destination or value.",
                            nameof(command));
                    }

                    break;

                case SimulationCommandKinds.SetQuantity:
                case SimulationCommandKinds.AddQuantity:
                case SimulationCommandKinds.RemoveQuantity:
                    if (command.DestinationId.IsNone)
                    {
                        throw new ArgumentException(
                            $"Command '{command.Kind}' requires a persistent destination.",
                            nameof(command));
                    }

                    if (command.QuantizedValue < 0L)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(command),
                            $"Command '{command.Kind}' cannot carry a negative quantity.");
                    }

                    if (command.Payload.Count != 0)
                    {
                        throw new ArgumentException(
                            $"Command '{command.Kind}' does not define a payload in the current logical schema.",
                            nameof(command));
                    }

                    break;

                case SimulationCommandKinds.Transfer:
                    if (command.InitiatorId.IsNone || command.DestinationId.IsNone)
                    {
                        throw new ArgumentException(
                            "A transfer command requires both a source and a destination owner.",
                            nameof(command));
                    }

                    if (command.QuantizedValue < 0L)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(command),
                            "A transfer command cannot carry a negative amount.");
                    }

                    // Rejecting a malformed payload at the boundary means phase 9 never
                    // meets one from live input. It still checks, because a state
                    // restored from a file has not passed through here.
                    if (!CML.Simulation.Inventories.TransferCommandPayload.TryDecode(
                            command,
                            out _,
                            out _,
                            out _,
                            out _))
                    {
                        throw new ArgumentException(
                            "A transfer command carries a payload that does not decode to two "
                            + "endpoints and an item.",
                            nameof(command));
                    }

                    break;

                case SimulationCommandKinds.MoveInventorySlot:
                    if (command.InitiatorId.IsNone || !command.DestinationId.IsNone)
                    {
                        throw new ArgumentException(
                            "A slot move names one inventory in InitiatorId and carries no "
                            + "destination owner: both slots belong to that inventory.",
                            nameof(command));
                    }

                    if (command.QuantizedValue < 0L)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(command),
                            "A slot move cannot carry a negative amount.");
                    }

                    if (!CML.Simulation.Inventories.SlotMoveCommandPayload.TryDecode(
                            command,
                            out _,
                            out _,
                            out _,
                            out _))
                    {
                        throw new ArgumentException(
                            "A slot move carries a payload that does not decode to two slot "
                            + "indices.",
                            nameof(command));
                    }

                    break;

                case SimulationCommandKinds.BuildMachineGraphElement:
                    if (command.InitiatorId.IsNone
                        || !command.DestinationId.IsNone
                        || command.QuantizedValue != 0L)
                    {
                        throw new ArgumentException(
                            "A machine build command requires one initiator and carries "
                            + "no destination or scalar value.",
                            nameof(command));
                    }

                    if (!CML.Simulation.Machines.MachineBuildCommandPayload.TryDecode(
                            command,
                            out _))
                    {
                        throw new ArgumentException(
                            "A machine build command carries an invalid fixed payload.",
                            nameof(command));
                    }

                    break;

                case SimulationCommandKinds.SalvageMachineGraphElement:
                    if (command.InitiatorId.IsNone
                        || command.DestinationId.IsNone
                        || command.QuantizedValue != 0L
                        || command.Payload.Count != 0)
                    {
                        throw new ArgumentException(
                            "A salvage command requires one initiator and one destination "
                            + "node, and carries no scalar value or payload.",
                            nameof(command));
                    }

                    break;

                default:
                    if (CML.Simulation.Airship.AirshipCommandKinds.IsAirshipCommand(command.Kind))
                    {
                        CML.Simulation.Airship.AirshipCommandCodec.ValidateForIngress(command);
                        break;
                    }

                    throw new ArgumentException(
                        $"Unknown authoritative command kind '{command.Kind}'.",
                        nameof(command));
            }
        }
    }
}
