using System;
using CML.Foundation;

namespace CML.Simulation
{
    /// <summary>
    /// Why an accepted command changed nothing. Values are persisted in the canonical
    /// record and are append-only.
    ///
    /// A refused transfer names the specific cause rather than a generic refusal: a
    /// record that says only "rejected" tells the player nothing and tells a bug report
    /// less. The shortfall case reuses <see cref="InsufficientQuantity"/>, since the
    /// command kind in the record already says which system it came from.
    /// </summary>
    public enum CommandRejectionReason : byte
    {
        InsufficientQuantity = 1,
        QuantityOverflow = 2,
        TransferSourceMissing = 3,
        TransferDestinationMissing = 4,
        TransferSameEndpoint = 5,
        TransferUnknownItem = 6,
        TransferZeroAmount = 7,
        TransferDestinationFull = 8,
        TransferNotAdmitted = 9,
        TransferMalformed = 10,
        BuildMalformed = 11,
        BuildDefinitionMissing = 12,
        BuildSourceMissing = 13,
        BuildDestinationMissing = 14,
        BuildTopologyInvalid = 15,

        /// <summary>The salvaged cell holds nothing, or holds something unplaced.</summary>
        SalvageTargetMissing = 16,

        /// <summary>
        /// The component, or what it still contained, does not fit in the inventory.
        /// Refusing keeps salvage lossless: nothing is ever destroyed to make room.
        /// </summary>
        SalvageDestinationFull = 17,

        /// <summary>Il comando di riordino non ha un payload leggibile.</summary>
        SlotMoveMalformed = 18,

        /// <summary>L'inventario indicato non esiste.</summary>
        SlotMoveInventoryMissing = 19,

        /// <summary>Uno dei due indici non esiste in quell'inventario.</summary>
        SlotMoveSlotOutOfRange = 20,

        /// <summary>
        /// Lo slot di destinazione è pieno, o tiene un altro oggetto e si stava
        /// spostando solo una parte della pila: il resto non avrebbe dove stare.
        /// </summary>
        SlotMoveBlocked = 21,

        /// <summary>Il comando di installazione non ha un payload leggibile.</summary>
        RepairInstallMalformed = 22,

        /// <summary>L'aeronave indicata non esiste.</summary>
        RepairAirshipMissing = 23,

        /// <summary>Lo scafo è già riparato o è già in riparazione.</summary>
        RepairNotDamaged = 24,

        /// <summary>L'oggetto non appartiene alla distinta di riparazione.</summary>
        RepairComponentNotInBill = 25,

        /// <summary>Quel componente è già installato nella quantità richiesta.</summary>
        RepairComponentAlreadySatisfied = 26,

        /// <summary>L'inventario indicato non esiste o non contiene il componente.</summary>
        RepairComponentMissing = 27
    }

    [Serializable]
    public readonly struct CommandRejection : IEquatable<CommandRejection>, IComparable<CommandRejection>
    {
        public CommandRejection(
            SimulationTick tick,
            SimulationCommand command,
            CommandRejectionReason reason)
        {
            Tick = tick;
            Command = command;
            Reason = reason;
        }

        public SimulationTick Tick { get; }

        public SimulationCommand Command { get; }

        public CommandRejectionReason Reason { get; }

        public int CompareTo(CommandRejection other)
        {
            var comparison = Tick.CompareTo(other.Tick);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = Command.Sequence.CompareTo(other.Command.Sequence);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = Command.DestinationId.CompareTo(other.Command.DestinationId);
            return comparison != 0 ? comparison : Reason.CompareTo(other.Reason);
        }

        public bool Equals(CommandRejection other)
        {
            return Tick == other.Tick && Command == other.Command && Reason == other.Reason;
        }

        public override bool Equals(object obj)
        {
            return obj is CommandRejection other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Tick.GetHashCode() * 397) ^ Command.GetHashCode()) * 397 ^ (int)Reason;
            }
        }
    }
}
