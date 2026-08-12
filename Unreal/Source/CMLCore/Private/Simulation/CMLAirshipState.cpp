#include "Simulation/CMLAirshipState.h"

namespace
{
    template <typename ElementType, typename ProjectionType>
    bool AreIdsUnique(const TArray<ElementType>& Elements, ProjectionType Projection)
    {
        TSet<FCMLStableId> Seen;
        Seen.Reserve(Elements.Num());
        for (const ElementType& Element : Elements)
        {
            bool bAlreadyPresent = false;
            Seen.Add(Projection(Element), &bAlreadyPresent);
            if (bAlreadyPresent)
            {
                return false;
            }
        }
        return true;
    }
}

void FCMLAirshipSimulationState::Sort()
{
    Airships.Sort([](const FCMLAirshipEntityState& A, const FCMLAirshipEntityState& B)
    {
        return A.Id < B.Id;
    });
    Players.Sort([](const FCMLAirshipPlayerState& A, const FCMLAirshipPlayerState& B)
    {
        return A.Id < B.Id;
    });
    Obstacles.Sort([](const FCMLAirshipObstacle& A, const FCMLAirshipObstacle& B)
    {
        return A.Id < B.Id;
    });
    LandingSurfaces.Sort([](const FCMLAirshipLandingSurface& A, const FCMLAirshipLandingSurface& B)
    {
        return A.Id < B.Id;
    });
}

bool FCMLAirshipSimulationState::HasUniqueIds() const
{
    return AreIdsUnique(Airships, [](const FCMLAirshipEntityState& V) { return V.Id; })
        && AreIdsUnique(Players, [](const FCMLAirshipPlayerState& V) { return V.Id; })
        && AreIdsUnique(Obstacles, [](const FCMLAirshipObstacle& V) { return V.Id; })
        && AreIdsUnique(LandingSurfaces, [](const FCMLAirshipLandingSurface& V) { return V.Id; });
}
