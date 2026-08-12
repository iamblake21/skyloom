using UnrealBuildTool;

public class ChangingMyLifeEditor : ModuleRules
{
    public ChangingMyLifeEditor(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PrivateDependencyModuleNames.AddRange(new[]
        {
            "Core", "CoreUObject", "Engine", "UnrealEd", "AssetTools", "EditorSubsystem",
            "Json", "JsonUtilities", "Projects", "CMLCore", "ChangingMyLife",
            // ALandscape::Import is editor-only C++ with no scripting exposure, so
            // the Unity terrain conversion needs to reach it from here.
            // Foliage is not used directly, but LandscapeEdit.h includes
            // InstancedFoliageActor.h, which lives in that module.
            "Landscape", "LandscapeEditor", "Foliage", "RenderCore"
        });
    }
}
