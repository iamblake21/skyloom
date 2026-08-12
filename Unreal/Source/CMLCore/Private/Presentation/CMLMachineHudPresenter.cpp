#include "Presentation/CMLMachineHudPresenter.h"

namespace
{
    /** The separator between the parts of the status line. */
    const TCHAR* StatusSeparator = TEXT(" · ");

    FString Humanize(const FString& Key)
    {
        const FString Trimmed = Key.TrimStartAndEnd();
        if (Trimmed.IsEmpty())
        {
            return TEXT("Sconosciuto");
        }
        int32 Separator = INDEX_NONE;
        FString Value = Trimmed.FindLastChar(TEXT('.'), Separator)
            ? Trimmed.RightChop(Separator + 1)
            : Trimmed;
        Value = Value.Replace(TEXT("_"), TEXT(" "));
        if (Value.IsEmpty())
        {
            return TEXT("Sconosciuto");
        }
        return FString::Chr(FChar::ToUpper(Value[0])) + Value.RightChop(1);
    }

    FString DisplayNameForDefinition(const FString& DefinitionKey)
    {
        static const TMap<FString, FString> Names = {
            {TEXT("machine.mechanical_press"), TEXT("Pressa meccanica")},
            {TEXT("machine.crude_furnace"), TEXT("Fornace rudimentale")},
            {TEXT("machine.mechanical_drill"), TEXT("Estrattore meccanico")},
            {TEXT("item.belt_funnel"), TEXT("Imbuto")},
            {TEXT("container.wooden_crate"), TEXT("Cassa di legno")},
            {TEXT("container.player_inventory"), TEXT("Inventario")},
            {TEXT("item.belt_drive_unit"), TEXT("Nastro motore")},
            {TEXT("item.belt_straight"), TEXT("Nastro trasportatore")},
        };
        if (const FString* Name = Names.Find(DefinitionKey))
        {
            return *Name;
        }
        return Humanize(DefinitionKey);
    }

    FString DisplayNameForRecipe(const FString& RecipeKey)
    {
        if (RecipeKey.IsEmpty())
        {
            return FString();
        }
        static const TMap<FString, FString> Names = {
            {TEXT("recipe.press_iron_plate"), TEXT("Piastra di ferro")},
            {TEXT("recipe.smelt_iron_ingot"), TEXT("Lingotto di ferro")},
            // A drill's active recipe is not a process but the ore of the deposit
            // it stands on: the HUD has to read as "this is what it is pulling
            // out", not as the name of a job.
            {TEXT("recipe.drill_raw_iron"), TEXT("Ferro grezzo")},
            {TEXT("recipe.drill_raw_copper"), TEXT("Rame grezzo")},
            {TEXT("recipe.drill_raw_tin"), TEXT("Stagno grezzo")},
        };
        if (const FString* Name = Names.Find(RecipeKey))
        {
            return *Name;
        }
        return Humanize(RecipeKey);
    }

    FString JoinStatus(const FString& First, const FString& Second)
    {
        if (First.IsEmpty())
        {
            return Second;
        }
        return Second.IsEmpty() ? First : First + StatusSeparator + Second;
    }

    /**
     * What is missing and how much of it. "Manca materiale" alone tells the
     * player to go looking; naming the item and the amount tells them what to
     * fetch.
     */
    FString ShortfallText(
        const FCMLMachineNodeReport& Report, const FCMLGameCatalog& Catalog)
    {
        FString Text;
        for (const FCMLMachineShortfallReport& Shortfall : Report.Shortfalls)
        {
            if (!Text.IsEmpty())
            {
                Text += StatusSeparator;
            }
            FCMLItemDefinition Item;
            const FString Name = Catalog.TryGetItem(Shortfall.ItemId, Item)
                ? FCMLInventoryHudPresenter::ProjectSlot(0, Shortfall.ItemId, 1, Item).DisplayName
                : Humanize(Shortfall.ItemKey);
            Text += FString::Printf(TEXT("%lld × %s"), Shortfall.Missing(), *Name);
        }
        return Text;
    }

    FCMLMachinePortPresentation ProjectPort(
        const FCMLMachinePortReport& Port, const FCMLGameCatalog& Catalog)
    {
        FCMLMachinePortPresentation Presentation;
        Presentation.Kind = Port.Kind;
        Presentation.Title = FCMLMachineHudPresenter::PortTitle(Port.Kind);
        Presentation.TotalQuantity = Port.TotalQuantity;
        Presentation.Slots.Reserve(Port.Slots.Num());
        for (int32 Index = 0; Index < Port.Slots.Num(); ++Index)
        {
            const FCMLMachineSlotReport& Slot = Port.Slots[Index];
            FCMLItemDefinition Item;
            if (Slot.IsEmpty() || !Catalog.TryGetItem(Slot.ItemId, Item))
            {
                Presentation.Slots.Add(FCMLInventoryHudPresenter::EmptySlot(Index));
                continue;
            }
            Presentation.Slots.Add(FCMLInventoryHudPresenter::ProjectSlot(
                Index, Slot.ItemId, Slot.Quantity, Item));
        }
        return Presentation;
    }
}

FString FCMLMachineHudPresenter::CauseText(const ECMLMachineActivity Activity)
{
    switch (Activity)
    {
        case ECMLMachineActivity::Running:      return TEXT("In lavorazione");
        case ECMLMachineActivity::Idle:         return TEXT("Deposito");
        case ECMLMachineActivity::NoRecipe:     return TEXT("Nessuna ricetta impostata");
        case ECMLMachineActivity::MissingInput: return TEXT("Manca materiale in ingresso");
        case ECMLMachineActivity::MissingFuel:  return TEXT("Manca combustibile");
        case ECMLMachineActivity::OutputFull:   return TEXT("Uscita piena");
        default:
            checkf(false, TEXT("Every activity needs Italian text; this one has none."));
            return FString();
    }
}

FString FCMLMachineHudPresenter::PortTitle(const ECMLMachinePortKind Kind)
{
    switch (Kind)
    {
        case ECMLMachinePortKind::Input:   return TEXT("INGRESSO");
        case ECMLMachinePortKind::Output:  return TEXT("USCITA");
        case ECMLMachinePortKind::Storage: return TEXT("CONTENUTO");
        case ECMLMachinePortKind::Fuel:    return TEXT("COMBUSTIBILE");
        default:
            checkf(false, TEXT("Every port kind needs a title; this one has none."));
            return FString();
    }
}

FString FCMLMachineHudPresenter::BeltLineText(const FCMLMachineNodeReport& Report)
{
    switch (Report.BeltLineStatus)
    {
        case ECMLBeltLineStatus::NotApplicable:
            return FString();
        case ECMLBeltLineStatus::MissingDrive:
            return TEXT("Nastro motore mancante");
        case ECMLBeltLineStatus::Operational:
            return FString::Printf(TEXT("%lld/%lld elementi"),
                Report.BeltLineUsedCapacity, Report.BeltLineAvailableCapacity);
        case ECMLBeltLineStatus::Overloaded:
            return FString::Printf(TEXT("Sovraccarico linea - %lld/%lld elementi"),
                Report.BeltLineUsedCapacity, Report.BeltLineAvailableCapacity);
        case ECMLBeltLineStatus::DirectionConflict:
            return TEXT("Conflitto di direzione");
        default:
            checkf(false, TEXT("Every belt-line status needs presentation text."));
            return FString();
    }
}

FCMLMachineUiSnapshot FCMLMachineHudPresenter::Project(
    const FCMLMachineNodeReport& Report,
    const FCMLGameCatalog& Catalog)
{
    FCMLMachineUiSnapshot Snapshot;
    Snapshot.NodeId = Report.NodeId;
    Snapshot.Kind = Report.Kind;
    Snapshot.DefinitionKey = Report.DefinitionKey;
    Snapshot.Title = DisplayNameForDefinition(Report.DefinitionKey);
    Snapshot.RecipeName = DisplayNameForRecipe(Report.RecipeKey);
    Snapshot.CauseText = CauseText(Report.Activity);
    Snapshot.ShortfallText =
        JoinStatus(ShortfallText(Report, Catalog), BeltLineText(Report));
    Snapshot.Activity = Report.Activity;
    Snapshot.bIsBlocked = Report.IsBlocked();
    Snapshot.ProgressPermille = Report.ProgressPermille;
    Snapshot.CompletedCycles = Report.CompletedCycles;

    Snapshot.Ports.Reserve(Report.Ports.Num());
    for (const FCMLMachinePortReport& Port : Report.Ports)
    {
        Snapshot.Ports.Add(ProjectPort(Port, Catalog));
    }
    return Snapshot;
}
