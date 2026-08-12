#include "Game/CMLGameMode.h"

#include "Player/CMLPlayerCharacter.h"
#include "UI/CMLHUD.h"

ACMLGameMode::ACMLGameMode()
{
    DefaultPawnClass = ACMLPlayerCharacter::StaticClass();
    HUDClass = ACMLHUD::StaticClass();
}
