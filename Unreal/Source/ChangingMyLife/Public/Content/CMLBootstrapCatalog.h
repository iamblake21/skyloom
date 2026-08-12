#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"

/**
 * The published bootstrap content used by the Unity game, transcribed without
 * changing ids, keys, stack sizes, recipes, capacities or revision.
 */
class CHANGINGMYLIFE_API FCMLBootstrapCatalog
{
public:
    static FCMLGameCatalog Create();
};
