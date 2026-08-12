#include "Presentation/CMLTutorialGlyphs.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    using Glyphs = FCMLTutorialGlyphs;

    uint8 AlphaAt(const TArray<uint8>& Pixels, const int32 Width, const int32 X, const int32 Y)
    {
        return Pixels[(Y * Width + X) * 4 + 3];
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLTutorialGlyphsTest,
    "CML.Core.Presentation.TutorialGlyphs",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLTutorialGlyphsTest::RunTest(const FString& Parameters)
{
    const TArray<uint8>& Mouse = Glyphs::MouseGlyph();
    const TArray<uint8>& Arrow = Glyphs::ArrowGlyph();

    TestEqual(TEXT("The mouse is the authored size"),
        Mouse.Num(), Glyphs::MouseWidth * Glyphs::MouseHeight * 4);
    TestEqual(TEXT("The arrow is the authored size"),
        Arrow.Num(), Glyphs::ArrowSize * Glyphs::ArrowSize * 4);

    // Both glyphs are white with the shape in the alpha, so one tint at draw
    // time colours the whole thing.
    {
        bool bAllWhite = true;
        for (const TArray<uint8>* Pixels : {&Mouse, &Arrow})
        {
            for (int32 Index = 0; Index < Pixels->Num(); Index += 4)
            {
                if ((*Pixels)[Index] != 255 || (*Pixels)[Index + 1] != 255
                    || (*Pixels)[Index + 2] != 255)
                {
                    bAllWhite = false;
                    break;
                }
            }
        }
        TestTrue(TEXT("The glyphs carry no colour of their own"), bAllWhite);
    }

    // The mouse is an outline, not a filled pill: its rim is opaque and the
    // space beside the wheel, inside the body, is empty.
    {
        constexpr int32 W = Glyphs::MouseWidth;
        constexpr int32 H = Glyphs::MouseHeight;
        TestEqual(TEXT("The corners are empty"),
            static_cast<int32>(AlphaAt(Mouse, W, 0, 0)), 0);
        TestTrue(TEXT("The left rim is drawn"),
            AlphaAt(Mouse, W, 7, H / 2) > 200);
        TestTrue(TEXT("The body is hollow beside the wheel"),
            AlphaAt(Mouse, W, W / 2 - 16, H / 2 + 26) < 40);
        TestTrue(TEXT("The wheel is filled"),
            AlphaAt(Mouse, W, W / 2, H / 2 + 26) > 200);

        // The seam stops at the waist: present above it, gone below.
        TestTrue(TEXT("The seam runs above the waist"),
            AlphaAt(Mouse, W, W / 2, H / 2 + 44) > 150);
        TestTrue(TEXT("And stops below it"),
            AlphaAt(Mouse, W, W / 2, H / 2 - 20) < 40);
    }

    // The arrow points one way: its head is wide near the middle and closes to
    // a point at the tip, so it reads as a direction and not as a bar.
    {
        constexpr int32 S = Glyphs::ArrowSize;
        auto ColumnCoverage = [&Arrow](const int32 X)
        {
            int32 Total = 0;
            for (int32 Y = 0; Y < Glyphs::ArrowSize; ++Y)
            {
                Total += AlphaAt(Arrow, Glyphs::ArrowSize, X, Y);
            }
            return Total;
        };

        const int32 Shaft = ColumnCoverage(static_cast<int32>(S * 0.30f));
        const int32 HeadBase = ColumnCoverage(static_cast<int32>(S * 0.55f));
        const int32 Tip = ColumnCoverage(static_cast<int32>(S * 0.93f));
        TestTrue(TEXT("The head is wider than the shaft"), HeadBase > Shaft * 2);
        TestTrue(TEXT("And narrows to a point"), Tip < HeadBase / 2);
        TestTrue(TEXT("The shaft is drawn at all"), Shaft > 0);

        // Nothing spills past the tip.
        TestEqual(TEXT("Beyond the tip is empty"), ColumnCoverage(S - 1), 0);
    }

    // The card is clamped at both ends of the range of screens it may meet.
    {
        TestEqual(TEXT("1080p is the authored scale"),
            Glyphs::ScaleForScreenHeight(1080.0f), 1.0f, 1e-6f);
        TestEqual(TEXT("A tiny window stops shrinking"),
            Glyphs::ScaleForScreenHeight(200.0f), 0.6f, 1e-6f);
        TestEqual(TEXT("A huge one stops growing"),
            Glyphs::ScaleForScreenHeight(8000.0f), 2.2f, 1e-6f);
        TestTrue(TEXT("And it scales in between"),
            Glyphs::ScaleForScreenHeight(1440.0f) > 1.0f);
    }
    return true;
}
#endif
