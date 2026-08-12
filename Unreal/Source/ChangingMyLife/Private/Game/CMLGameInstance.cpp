#include "Game/CMLGameInstance.h"

DEFINE_LOG_CATEGORY_STATIC(LogCMLGameInstance, Log, All);

void UCMLGameInstance::Init()
{
    Super::Init();
    UE_LOG(LogCMLGameInstance, Display, TEXT("Changing My Life Unreal runtime initialized."));
}

void UCMLGameInstance::Shutdown()
{
    UE_LOG(LogCMLGameInstance, Display, TEXT("Changing My Life Unreal runtime shutting down."));
    Super::Shutdown();
}
