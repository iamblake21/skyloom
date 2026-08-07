using System;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using UnityEngine;

namespace CML.Unity.Presentation.Machines
{
    /// <summary>
    /// The only way the interface writes to the simulation.
    ///
    /// A panel does not hold the engine and does not build commands: it asks for a move
    /// and gets an answer. That keeps the write path in one place where the tick and the
    /// sequence are computed correctly, and it means a panel cannot accidentally mutate
    /// state it is only supposed to display.
    ///
    /// The catalog is required, not optional. A state with a machine graph cannot advance
    /// without it, and every scene that shows a crate has one by definition.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TransferCommandBridge : MonoBehaviour
    {
        private SimulationEngine _engine;
        private GameCatalog _catalog;

        public bool IsAttached => _engine != null;

        public GameCatalog Catalog =>
            _catalog ?? throw new InvalidOperationException("The transfer bridge has no catalog.");

        public SimulationEngine Engine =>
            _engine ?? throw new InvalidOperationException("The transfer bridge is not attached.");

        /// <summary>
        /// Binds an engine the caller owns. Taking the engine rather than creating one
        /// keeps a scene from ending up with two authoritative states.
        /// </summary>
        public void Attach(SimulationEngine engine, GameCatalog catalog)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public InventorySimulationState Inventories => Engine.State.GetInventorySnapshot();

        public MachineSimulationState Machines => Engine.State.GetMachineSnapshot();

        /// <summary>
        /// Queues a move for the next tick. It returns what the boundary accepted, not
        /// whether the move will succeed: the rule decides that in phase 9, against the
        /// state as it will be then, and a refusal lands in the rejection record with its
        /// cause. A panel that reported success here would be guessing.
        /// </summary>
        public SimulationCommandAcceptance SubmitTransfer(
            TransferEndpoint source,
            TransferEndpoint destination,
            StableId itemId,
            NonNegativeQuantity amount)
        {
            var tick = Engine.State.Tick.Next();
            var sequence = 0UL;
            var pending = Engine.State.GetAcceptedCommandsCanonical();
            for (var index = 0; index < pending.Count; index++)
            {
                if (pending[index].TargetTick == tick)
                {
                    sequence = checked(sequence + 1UL);
                }
            }

            return Engine.EnqueueCommand(
                new SimulationCommand(
                    tick,
                    sequence,
                    SimulationCommandKinds.Transfer,
                    source.OwnerId,
                    destination.OwnerId,
                    amount.Value,
                    TransferCommandPayload.Encode(source, destination, itemId)));
        }

        /// <summary>
        /// Accoda un riordino fra due slot dello stesso inventario.
        ///
        /// Serve un comando distinto perché il trasferimento lavora fra endpoint
        /// e rifiuta per costruzione origine e destinazione coincidenti: dentro
        /// un solo inventario non aveva modo di esprimersi. Vale la stessa
        /// avvertenza del trasferimento — qui si accetta al confine, chi decide
        /// è la regola in fase 9.
        /// </summary>
        public SimulationCommandAcceptance SubmitSlotMove(
            StableId inventoryId,
            int sourceSlotIndex,
            int destinationSlotIndex,
            NonNegativeQuantity amount)
        {
            var tick = Engine.State.Tick.Next();
            var sequence = 0UL;
            var pending = Engine.State.GetAcceptedCommandsCanonical();
            for (var index = 0; index < pending.Count; index++)
            {
                if (pending[index].TargetTick == tick)
                {
                    sequence = checked(sequence + 1UL);
                }
            }

            return Engine.EnqueueCommand(
                new SimulationCommand(
                    tick,
                    sequence,
                    SimulationCommandKinds.MoveInventorySlot,
                    inventoryId,
                    StableId.None,
                    amount.Value,
                    SlotMoveCommandPayload.Encode(
                        sourceSlotIndex,
                        destinationSlotIndex)));
        }
    }
}
