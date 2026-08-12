#include "Presentation/CMLTreeChopOpening.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLTreeChopOpeningTest,
    "CML.Core.Presentation.TreeChopOpening",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLTreeChopOpeningTest::RunTest(const FString& Parameters)
{
    using Chop = FCMLTreeChop;
    constexpr float MatureTrunk = 0.42f;
    constexpr float Sapling = 0.10f;

    // The notch grows with every blow, in all three directions.
    {
        FCMLTreeChopOpening Previous = Chop::ResolveOpening(MatureTrunk, 1);
        for (int32 Stage = 2; Stage <= Chop::HitsRequired; ++Stage)
        {
            const FCMLTreeChopOpening Current = Chop::ResolveOpening(MatureTrunk, Stage);
            TestTrue(TEXT("It widens with each blow"), Current.Width >= Previous.Width);
            TestTrue(TEXT("And deepens"), Current.Depth > Previous.Depth);
            Previous = Current;
        }
    }

    // The first blow already opens most of the way. A notch that started tiny
    // would make the early blows look like they missed.
    {
        const FCMLTreeChopOpening First = Chop::ResolveOpening(MatureTrunk, 1);
        const FCMLTreeChopOpening Last = Chop::ResolveOpening(MatureTrunk, Chop::HitsRequired);
        TestTrue(TEXT("The first blow is already more than half the final width"),
            First.Width > Last.Width * 0.5f);
        TestTrue(TEXT("But visibly smaller than the last"), First.Width < Last.Width);
    }

    // Sized against the trunk it is cut into: a sapling gets a smaller notch
    // than a mature trunk, or it would be cut clean in half.
    {
        const FCMLTreeChopOpening Big = Chop::ResolveOpening(MatureTrunk, Chop::HitsRequired);
        const FCMLTreeChopOpening Small = Chop::ResolveOpening(Sapling, Chop::HitsRequired);
        TestTrue(TEXT("A thin trunk gets a narrower notch"), Small.Width < Big.Width);
        TestTrue(TEXT("And a shallower one"), Small.Depth < Big.Depth);
    }

    // The depth cap is the rule that stops a few blows going straight through.
    // Even at the last stage the notch must not reach the far side.
    {
        for (const float Section : {0.06f, 0.10f, 0.18f, 0.30f, 0.42f})
        {
            const FCMLTreeChopOpening Deepest =
                Chop::ResolveOpening(Section, Chop::HitsRequired);
            TestTrue(TEXT("The notch never reaches halfway through the trunk"),
                Deepest.Depth < Section * 0.5f);
        }
    }

    // Broadening the footprint is cosmetic: it must not deepen the cut, or the
    // tree would fall early.
    {
        const FCMLTreeChopOpening Opening =
            Chop::ResolveOpening(MatureTrunk, Chop::HitsRequired);
        // The depth ceiling is the authored 0.030, unscaled by the footprint.
        TestTrue(TEXT("Depth is not multiplied by the footprint scale"),
            Opening.Depth <= 0.030f + 1e-5f);
        TestTrue(TEXT("While the width is"),
            Opening.Width > Chop::TargetMaximumWidth * 0.68f);
    }

    // Every notch is worth looking at, even the first on the thinnest trunk.
    {
        const FCMLTreeChopOpening Smallest = Chop::ResolveOpening(0.02f, 1);
        TestTrue(TEXT("There is always a visible opening"),
            Smallest.Width >= Chop::MinimumOpeningWidth
                && Smallest.Height >= Chop::MinimumOpeningHeight);
    }

    // An unmeasured trunk falls back to a fixed size rather than the largest:
    // guessing small leaves an ugly scar, guessing large severs the tree.
    {
        const FCMLTreeChopOpening Unmeasured = Chop::ResolveOpening(0.0f, Chop::HitsRequired);
        const FCMLTreeChopOpening Mature = Chop::ResolveOpening(MatureTrunk, Chop::HitsRequired);
        TestTrue(TEXT("The fallback is smaller than a mature trunk's notch"),
            Unmeasured.Width < Mature.Width);
        TestEqual(TEXT("And is exactly the authored fallback"),
            Unmeasured.Width, Chop::UnmeasuredWidth * Chop::FootprintScale, 1e-5f);
    }

    // A stage past the end does not keep growing: the tree falls instead.
    {
        const FCMLTreeChopOpening Last = Chop::ResolveOpening(MatureTrunk, Chop::HitsRequired);
        const FCMLTreeChopOpening Beyond = Chop::ResolveOpening(MatureTrunk, 99);
        TestEqual(TEXT("The notch stops growing at the felling blow"),
            Beyond.Width, Last.Width, 1e-5f);
        TestEqual(TEXT("In depth too"), Beyond.Depth, Last.Depth, 1e-5f);
    }
    return true;
}
#endif
