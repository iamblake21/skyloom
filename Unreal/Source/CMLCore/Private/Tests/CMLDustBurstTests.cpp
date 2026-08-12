#include "Presentation/CMLDustBurst.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLDustBurstTest,
    "CML.Core.Presentation.DustBurst",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLDustBurstTest::RunTest(const FString& Parameters)
{
    constexpr int32 Size = FCMLDustBurst::SpriteSize;
    const TArray<uint8>& Pixels = FCMLDustBurst::SpritePixels();

    auto AlphaAt = [&Pixels](const int32 X, const int32 Y)
    {
        return Pixels[(Y * Size + X) * 4 + 3];
    };

    TestEqual(TEXT("The sprite is 64 by 64 RGBA"), Pixels.Num(), Size * Size * 4);

    // Opaque in the middle, gone at the rim, and white throughout: the tint is
    // applied by the material, not baked into the sprite.
    {
        // 254, not 255: the sprite is an even number of pixels wide, so its
        // centre falls between them and no pixel sits exactly on it. Unity's
        // sprite has the same value for the same reason.
        TestEqual(TEXT("The centre is opaque"), static_cast<int32>(AlphaAt(32, 32)), 254);
        TestEqual(TEXT("The corner is empty"), static_cast<int32>(AlphaAt(0, 0)), 0);
        TestEqual(TEXT("The edge midpoint is empty"), static_cast<int32>(AlphaAt(0, 32)), 0);
        for (int32 Index = 0; Index < Pixels.Num(); Index += 4)
        {
            if (Pixels[Index] != 255 || Pixels[Index + 1] != 255 || Pixels[Index + 2] != 255)
            {
                AddError(TEXT("The sprite carries a colour; it must be white"));
                break;
            }
        }
    }

    // A hard disc reads as a bubble. The falloff has to be smooth and monotonic
    // from the centre outwards, and it must ease rather than ramp linearly —
    // that easing is what lets overlapping particles merge into a cloud.
    {
        int32 Previous = 256;
        bool bMonotonic = true;
        for (int32 X = 32; X < Size; ++X)
        {
            const int32 Alpha = AlphaAt(X, 32);
            if (Alpha > Previous)
            {
                bMonotonic = false;
                break;
            }
            Previous = Alpha;
        }
        TestTrue(TEXT("Alpha never rises going outwards"), bMonotonic);

        // Halfway out, a linear ramp would sit at 128. Smoothstep sits at 128
        // too — so the tell is nearer the ends: at three quarters, linear gives
        // ~64 and smoothstep noticeably less.
        const int32 ThreeQuarters = AlphaAt(32 + 24, 32);
        TestTrue(TEXT("The falloff eases rather than ramping"),
            ThreeQuarters < 56 && ThreeQuarters > 0);
    }

    // Dust is the pale ghost of what it came off, never the material colour.
    {
        const FLinearColor Rust(0.62f, 0.24f, 0.08f, 1.0f);
        const FLinearColor Tint = FCMLDustBurst::DustTint(Rust, FCMLDustBurst::DefaultWhiteness);

        TestTrue(TEXT("It is paler than the source"),
            Tint.R + Tint.G + Tint.B > Rust.R + Rust.G + Rust.B);
        const float SourceSpread = FMath::Max3(Rust.R, Rust.G, Rust.B)
            - FMath::Min3(Rust.R, Rust.G, Rust.B);
        const float TintSpread = FMath::Max3(Tint.R, Tint.G, Tint.B)
            - FMath::Min3(Tint.R, Tint.G, Tint.B);
        TestTrue(TEXT("It is desaturated, not full-strength paint"), TintSpread < SourceSpread);
        TestTrue(TEXT("But it still remembers where it came from"), TintSpread > 0.01f);
        TestEqual(TEXT("Dust is opaque; the sprite carries the fade"), Tint.A, 1.0f, 1e-6f);
    }

    // Full whiteness lands on the pale reference colour regardless of source,
    // which is what makes the parameter mean what it says.
    {
        const FLinearColor FromRed = FCMLDustBurst::DustTint(FLinearColor::Red, 1.0f);
        const FLinearColor FromBlue = FCMLDustBurst::DustTint(FLinearColor::Blue, 1.0f);
        TestEqual(TEXT("Red and blue meet at full whiteness"), FromRed.R, FromBlue.R, 1e-6f);
        TestEqual(TEXT("Red and blue meet at full whiteness"), FromRed.G, FromBlue.G, 1e-6f);
    }
    return true;
}
#endif
