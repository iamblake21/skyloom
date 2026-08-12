#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"

/**
 * The published content ids, transcribed from CML.Content.ContentIds.
 *
 * These values are hashed into the canonical state, so they are not free to
 * change: inventing a convenient number here would silently produce a different
 * world from the same actions. The high word is the kind (items 0x1…, recipes
 * 0x2…, machines 0x3…, containers 0x4…, islands 0x6…) and the low word is a
 * sequence that is only ever appended to.
 *
 * Two gaps in the item sequence are deliberate and must stay: 0x0B and 0x0C
 * belonged to a rotor and a part that no longer exist, and closing the gaps
 * would renumber every id after them.
 */
namespace CMLContentIds
{
    inline constexpr uint64 ItemKind = 0x1000000000000000ULL;
    inline constexpr uint64 RecipeKind = 0x2000000000000000ULL;
    inline constexpr uint64 MachineKind = 0x3000000000000000ULL;
    inline constexpr uint64 ContainerKind = 0x4000000000000000ULL;
    inline constexpr uint64 IslandKind = 0x6000000000000000ULL;

    // Items
    inline constexpr FCMLStableId RawIron{ItemKind, 0x01};
    inline constexpr FCMLStableId IronIngot{ItemKind, 0x02};
    inline constexpr FCMLStableId IronPlate{ItemKind, 0x03};
    inline constexpr FCMLStableId BeltStraight{ItemKind, 0x04};
    inline constexpr FCMLStableId BeltCurve{ItemKind, 0x05};
    inline constexpr FCMLStableId BeltSupport{ItemKind, 0x06};
    inline constexpr FCMLStableId BeltFunnel{ItemKind, 0x07};
    inline constexpr FCMLStableId BeltDriveUnit{ItemKind, 0x08};
    inline constexpr FCMLStableId WoodenCrateItem{ItemKind, 0x09};
    inline constexpr FCMLStableId MechanicalPressItem{ItemKind, 0x0A};
    inline constexpr FCMLStableId BeltIncline{ItemKind, 0x0D};
    /** A mirrored mesh, not the right-hand curve rotated. */
    inline constexpr FCMLStableId BeltCurveLeft{ItemKind, 0x0E};
    inline constexpr FCMLStableId CrudePickaxe{ItemKind, 0x0F};
    inline constexpr FCMLStableId Stone{ItemKind, 0x10};
    inline constexpr FCMLStableId IronPickaxe{ItemKind, 0x11};
    inline constexpr FCMLStableId WoodLog{ItemKind, 0x12};
    inline constexpr FCMLStableId WorkbenchItem{ItemKind, 0x13};
    inline constexpr FCMLStableId CrudeFurnaceItem{ItemKind, 0x14};
    inline constexpr FCMLStableId MechanicalDrillItem{ItemKind, 0x15};
    inline constexpr FCMLStableId RawCopper{ItemKind, 0x16};
    inline constexpr FCMLStableId RawTin{ItemKind, 0x17};
    inline constexpr FCMLStableId InsulatedCable{ItemKind, 0x18};
    inline constexpr FCMLStableId PlantFiber{ItemKind, 0x19};
    inline constexpr FCMLStableId Stick{ItemKind, 0x1A};

    // Recipes
    inline constexpr FCMLStableId PressIronPlate{RecipeKind, 0x01};
    inline constexpr FCMLStableId CraftCrudePickaxe{RecipeKind, 0x02};
    inline constexpr FCMLStableId CraftWoodenCrate{RecipeKind, 0x03};
    inline constexpr FCMLStableId WorkbenchIronPlate{RecipeKind, 0x04};
    inline constexpr FCMLStableId WorkbenchBeltStraight{RecipeKind, 0x05};
    inline constexpr FCMLStableId WorkbenchBeltSupport{RecipeKind, 0x06};
    inline constexpr FCMLStableId WorkbenchBeltFunnel{RecipeKind, 0x07};
    inline constexpr FCMLStableId WorkbenchMechanicalPress{RecipeKind, 0x08};
    inline constexpr FCMLStableId WorkbenchIronPickaxe{RecipeKind, 0x09};
    inline constexpr FCMLStableId SmeltIronIngot{RecipeKind, 0x0A};
    inline constexpr FCMLStableId WorkbenchCrudeFurnace{RecipeKind, 0x0B};
    inline constexpr FCMLStableId WorkbenchMechanicalDrill{RecipeKind, 0x0C};
    inline constexpr FCMLStableId DrillRawIron{RecipeKind, 0x0D};
    inline constexpr FCMLStableId DrillRawCopper{RecipeKind, 0x0E};
    inline constexpr FCMLStableId DrillRawTin{RecipeKind, 0x0F};

    // Machines
    inline constexpr FCMLStableId MechanicalPress{MachineKind, 0x01};
    inline constexpr FCMLStableId CrudeFurnace{MachineKind, 0x02};
    inline constexpr FCMLStableId MechanicalDrill{MachineKind, 0x03};

    // Containers
    inline constexpr FCMLStableId WoodenCrate{ContainerKind, 0x01};
    inline constexpr FCMLStableId PlayerInventory{ContainerKind, 0x02};
    /** The airship's hold is a container like any other; no special rules. */
    inline constexpr FCMLStableId AirshipHold{ContainerKind, 0x03};

    // Islands
    inline constexpr FCMLStableId MeadowIsland{IslandKind, 0x01};
    inline constexpr FCMLStableId HighlandIsland{IslandKind, 0x02};
}
