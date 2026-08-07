using System;

namespace CML.Inventory
{
    public sealed class InventoryInvariantException : InvalidOperationException
    {
        public InventoryInvariantException(string message)
            : base(message)
        {
        }
    }
}
