using CML.Content;
using CML.Foundation;

namespace CML.Simulation.Airship
{
    /// <summary>
    /// The canonical cost of making the hull airworthy again, fixed by GDD 2.3
    /// and DVS-001: four Iron Plates and two Insulated Cables, then eight
    /// seconds of work.
    ///
    /// The two components are named rather than held in a list because the
    /// installed counters are named in the canonical schema too. A third
    /// component would be a schema revision either way, so a fixed pair reads
    /// better here than a variable-length collection that pretends otherwise.
    /// </summary>
    public static class AirshipRepairBill
    {
        public const int RequiredIronPlates = 4;

        public const int RequiredInsulatedCables = 2;

        /// <summary>Eight seconds at the authoritative 20 Hz tick.</summary>
        public const int RepairDurationTicks =
            8 * AirshipSimulationConstants.TicksPerSecond;

        public static StableId IronPlateItemId => ContentIds.IronPlate;

        public static StableId InsulatedCableItemId => ContentIds.InsulatedCable;

        /// <summary>
        /// How many units of <paramref name="itemId"/> the bill asks for, or
        /// zero when the item is not part of it.
        /// </summary>
        public static int RequiredCountFor(StableId itemId)
        {
            if (itemId == ContentIds.IronPlate)
            {
                return RequiredIronPlates;
            }

            if (itemId == ContentIds.InsulatedCable)
            {
                return RequiredInsulatedCables;
            }

            return 0;
        }

        public static bool IsPartOfBill(StableId itemId)
        {
            return RequiredCountFor(itemId) > 0;
        }
    }
}
