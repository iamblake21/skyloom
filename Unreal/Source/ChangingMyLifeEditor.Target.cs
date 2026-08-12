using UnrealBuildTool;
using System.Collections.Generic;

public class ChangingMyLifeEditorTarget : TargetRules
{
    public ChangingMyLifeEditorTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Editor;
        DefaultBuildSettings = BuildSettingsVersion.V7;
        IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
        if (Target.Platform == UnrealTargetPlatform.Win64)
        {
            WindowsPlatform.CompilerVersion = "14.44.35207";
        }
        ExtraModuleNames.AddRange(new[] { "CMLCore", "ChangingMyLife", "ChangingMyLifeEditor" });
    }
}
