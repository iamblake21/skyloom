using CML.Foundation;
using CML.Simulation.Machines;
using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Spatial counterpart of one authoritative graph node. Authored sockets are the
    /// physical connection contract used to create the topology; the StableId records
    /// that exact connection after placement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FactoryNodeAnchor : MonoBehaviour
    {
        [SerializeField] private ulong idHigh;
        [SerializeField] private ulong idLow;
        [SerializeField] private MachineNodeKind nodeKind;
        [SerializeField] private Transform inputSocket;
        [SerializeField] private Transform outputSocket;

        public StableId NodeId => new StableId(idHigh, idLow);

        public MachineNodeKind NodeKind => nodeKind;

        public Transform InputSocket => inputSocket != null ? inputSocket : transform;

        public Transform OutputSocket => outputSocket != null ? outputSocket : transform;

        public void Configure(
            StableId id,
            MachineNodeKind kind,
            Transform itemInputSocket,
            Transform itemOutputSocket)
        {
            idHigh = id.High;
            idLow = id.Low;
            nodeKind = kind;
            inputSocket = itemInputSocket;
            outputSocket = itemOutputSocket;
        }
    }
}
