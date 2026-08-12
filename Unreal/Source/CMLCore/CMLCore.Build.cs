using UnrealBuildTool;

public class CMLCore : ModuleRules
{
    public CMLCore(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject" });
        // RenderCore exposes AddShaderSourceDirectoryMapping, which publishes the
        // ported Unity shader library under the /CML/ virtual shader path.
        PrivateDependencyModuleNames.AddRange(new[] { "Json", "JsonUtilities", "RenderCore", "Projects" });
    }
}
