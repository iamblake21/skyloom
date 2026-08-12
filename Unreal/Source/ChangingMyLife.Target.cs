using UnrealBuildTool;
using System.Collections.Generic;

public class ChangingMyLifeTarget : TargetRules
{
    public ChangingMyLifeTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Game;
        DefaultBuildSettings = BuildSettingsVersion.V7;
        IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
        if (Target.Platform == UnrealTargetPlatform.Win64)
        {
            WindowsPlatform.CompilerVersion = "14.44.35207";
        }
        ExtraModuleNames.AddRange(new[] { "CMLCore", "ChangingMyLife" });
    }
}
