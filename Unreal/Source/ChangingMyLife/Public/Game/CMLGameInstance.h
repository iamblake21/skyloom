#pragma once

#include "CoreMinimal.h"
#include "Engine/GameInstance.h"
#include "CMLGameInstance.generated.h"

UCLASS()
class CHANGINGMYLIFE_API UCMLGameInstance final : public UGameInstance
{
    GENERATED_BODY()

public:
    virtual void Init() override;
    virtual void Shutdown() override;

    /**
     * Set by the opening when it enters the fall, read by the island once it
     * loads. The descent happens in the gameplay world — Unity plays this half
     * of the sequence there — so the one thing that has to survive the level
     * change is the fact that it is owed.
     */
    UPROPERTY(BlueprintReadWrite, Category="CML|Intro")
    bool bIntroArrivalPending = false;
};
