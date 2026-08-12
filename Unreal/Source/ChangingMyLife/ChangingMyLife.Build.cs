using UnrealBuildTool;

public class ChangingMyLife : ModuleRules
{
    public ChangingMyLife(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[]
        {
            "Core", "CoreUObject", "Engine", "InputCore", "EnhancedInput", "CMLCore"
        });
        PrivateDependencyModuleNames.AddRange(new[]
        {
            "UMG", "Slate", "SlateCore", "Niagara", "Landscape", "NavigationSystem",
            "ProceduralMeshComponent"
        });
    }
}
