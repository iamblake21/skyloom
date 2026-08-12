#include "Intro/CMLIntroBootstrap.h"

#include "Engine/World.h"
#include "EngineUtils.h"
#include "Intro/CMLIntroDirector.h"

DEFINE_LOG_CATEGORY_STATIC(LogCMLIntroBootstrap, Log, All);

const TCHAR* UCMLIntroBootstrap::MarkerLabel = TEXT("CIN_IntroSequence");

bool UCMLIntroBootstrap::ShouldCreateSubsystem(UObject* Outer) const
{
    const UWorld* World = Cast<UWorld>(Outer);
    return World != nullptr && World->IsGameWorld()
        && World->GetMapName().Contains(TEXT("A_01_IntroCinematic"));
}

void UCMLIntroBootstrap::OnWorldBeginPlay(UWorld& InWorld)
{
    Super::OnWorldBeginPlay(InWorld);

    // A director already in the level wins: placing one by hand has to keep
    // working, and spawning a second would run the opening twice.
    for (TActorIterator<ACMLIntroDirector> It(&InWorld); It; ++It)
    {
        Director = *It;
        return;
    }

    FActorSpawnParameters Parameters;
    Parameters.Name = TEXT("CMLIntroDirector");
    Director = InWorld.SpawnActor<ACMLIntroDirector>(
        ACMLIntroDirector::StaticClass(), FTransform::Identity, Parameters);
    if (Director == nullptr)
    {
        UE_LOG(LogCMLIntroBootstrap, Error,
            TEXT("'%s' is in this level but the director could not be spawned, "
                 "so the opening will play to a still frame."), MarkerLabel);
        return;
    }
#if WITH_EDITOR
    Director->SetActorLabel(TEXT("CIN_IntroDirector"));
#endif
    UE_LOG(LogCMLIntroBootstrap, Display,
        TEXT("Found '%s'; the opening is directed."), MarkerLabel);
}
