#include "CMLCoreModule.h"

#include "Interfaces/IPluginManager.h"
#include "Misc/Paths.h"
#include "Modules/ModuleManager.h"
#include "ShaderCore.h"

IMPLEMENT_MODULE(FCMLCoreModule, CMLCore)

void FCMLCoreModule::StartupModule()
{
    // The Unity shaders were ported to real HLSL rather than reassembled out of
    // material nodes, so the ported library has to be reachable from material
    // Custom expressions. Mapping it here (PreDefault loading phase) guarantees
    // the path exists before the first material using it is compiled.
    const FString ShaderDirectory = FPaths::Combine(FPaths::ProjectDir(), TEXT("Shaders"));
    if (!AllShaderSourceDirectoryMappings().Contains(TEXT("/CML")))
    {
        AddShaderSourceDirectoryMapping(TEXT("/CML"), ShaderDirectory);
    }
}

void FCMLCoreModule::ShutdownModule()
{
}
