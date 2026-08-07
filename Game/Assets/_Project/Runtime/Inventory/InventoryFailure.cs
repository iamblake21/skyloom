namespace CML.Inventory
{
    public enum InventoryFailure
    {
        None = 0,
        UnknownItem = 1,
        CapacityExceeded = 2,
        InsufficientQuantity = 3,
        InvalidDefinition = 4,
        ArithmeticOverflow = 5
    }
}
