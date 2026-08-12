#include "Modules/ModuleManager.h"

class FChangingMyLifeEditorModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override {}
    virtual void ShutdownModule() override {}
};

IMPLEMENT_MODULE(FChangingMyLifeEditorModule, ChangingMyLifeEditor)
