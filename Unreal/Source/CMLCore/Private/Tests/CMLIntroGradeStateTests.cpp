#include "Presentation/CMLIntroGradeState.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLIntroGradeStateTest,
    "CML.Core.Presentation.IntroGradeState",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLIntroGradeStateTest::RunTest(const FString& Parameters)
{
    const FCMLIntroGradeState Cruise = FCMLIntroGradeState::Cruise();

    // The cruise look is the baseline every shot starts from, so its values are
    // pinned rather than left to drift.
    {
        TestEqual(TEXT("Bloom intensity"), Cruise.BloomIntensity, 1.15f, 1e-6f);
        TestEqual(TEXT("Bloom threshold"), Cruise.BloomThreshold, 0.82f, 1e-6f);
        TestEqual(TEXT("Vignette"), Cruise.VignetteIntensity, 0.28f, 1e-6f);
        TestEqual(TEXT("Contrast on Unity's scale"), Cruise.Contrast, 12.0f, 1e-6f);
        TestEqual(TEXT("Saturation on Unity's scale"), Cruise.Saturation, 6.0f, 1e-6f);
        TestTrue(TEXT("The bloom is cool, not white"),
            Cruise.BloomTint.B > Cruise.BloomTint.R);
        TestTrue(TEXT("Nothing is graded away by default"),
            Cruise.ColorFilter.Equals(FLinearColor::White, 1e-6f));
        TestEqual(TEXT("No lens distortion at rest"), Cruise.LensDistortion, 0.0f, 1e-6f);
        TestEqual(TEXT("No exposure push at rest"), Cruise.PostExposure, 0.0f, 1e-6f);
    }

    // A shot pushes only the values that carry its intent; the rest come from
    // cruise, which is what stops shots drifting apart.
    {
        FCMLIntroGradeState Storm = Cruise;
        Storm.Saturation = -40.0f;
        Storm.VignetteIntensity = 0.62f;

        const FCMLIntroGradeState Half = FCMLIntroGradeState::Blend(Cruise, Storm, 0.5f);
        TestEqual(TEXT("Saturation blends halfway"), Half.Saturation, -17.0f, 1e-5f);
        TestEqual(TEXT("So does the vignette"), Half.VignetteIntensity, 0.45f, 1e-5f);
        TestEqual(TEXT("Untouched values stay put"), Half.BloomIntensity, 1.15f, 1e-6f);
        TestTrue(TEXT("And so do untouched colours"),
            Half.BloomTint.Equals(Cruise.BloomTint, 1e-6f));
    }

    // The ends are exact, and a shot that runs past its own transition holds the
    // target rather than pushing the grade beyond it.
    {
        FCMLIntroGradeState Target = Cruise;
        Target.PostExposure = 2.0f;
        Target.ColorFilter = FLinearColor(1.0f, 0.4f, 0.2f, 1.0f);

        const FCMLIntroGradeState AtStart = FCMLIntroGradeState::Blend(Cruise, Target, 0.0f);
        TestEqual(TEXT("Alpha zero is the source"), AtStart.PostExposure, 0.0f, 1e-6f);

        const FCMLIntroGradeState AtEnd = FCMLIntroGradeState::Blend(Cruise, Target, 1.0f);
        TestEqual(TEXT("Alpha one is the target"), AtEnd.PostExposure, 2.0f, 1e-6f);
        TestTrue(TEXT("Colours reach the target exactly"),
            AtEnd.ColorFilter.Equals(Target.ColorFilter, 1e-6f));

        const FCMLIntroGradeState Overshot = FCMLIntroGradeState::Blend(Cruise, Target, 3.5f);
        TestEqual(TEXT("Past the end it holds the target"),
            Overshot.PostExposure, 2.0f, 1e-6f);
        const FCMLIntroGradeState Undershot =
            FCMLIntroGradeState::Blend(Cruise, Target, -2.0f);
        TestEqual(TEXT("Before the start it holds the source"),
            Undershot.PostExposure, 0.0f, 1e-6f);
    }
    return true;
}
#endif
