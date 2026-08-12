#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"

#include "CMLIntroBootstrap.generated.h"

/**
 * Puts the opening's director into any level that has an opening to direct.
 *
 * In Unity the controller was a component on a scene object, so opening the
 * scene was enough to start it. The scene converter brought the object across
 * but not its scripts, which left the map full of scenery with nothing to move
 * it — and writing the director class alone did not fix that, because nothing
 * placed one.
 *
 * Keying off the marker actor rather than the map name is what makes this
 * survive a re-conversion: the map can be regenerated at any time and the
 * opening still starts, with no actor to re-place by hand and no name to keep
 * in step.
 */
UCLASS()
class CHANGINGMYLIFE_API UCMLIntroBootstrap : public UWorldSubsystem
{
    GENERATED_BODY()

public:
    /** The Unity GameObject that carried the cinematic controller. */
    static const TCHAR* MarkerLabel;

    virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
    virtual void OnWorldBeginPlay(UWorld& InWorld) override;

private:
    UPROPERTY() TObjectPtr<class ACMLIntroDirector> Director;
};
