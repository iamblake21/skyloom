#pragma once

#include "CoreMinimal.h"

#include "CMLIntroGradeState.generated.h"

/**
 * The per-frame look of a cinematic shot, ported from
 * CML.Unity.Presentation.Intro.IntroGradeState.
 *
 * Everything the director animates on the image lives in one struct, so a shot
 * only has to say how it should look — not which post-process override owns
 * which field. In Unity these drove URP volume overrides; in Unreal they drive a
 * post-process settings block. Keeping the shot description separate from the
 * binding is what lets the whole grade be checked without a renderer.
 *
 * The units are Unity's, deliberately: contrast and saturation run on its
 * -100..100 scale, not Unreal's 0..2 multipliers. Converting here would scatter
 * the same conversion through every shot; it belongs at the binding, once.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIntroGradeState
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float BloomIntensity = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float BloomThreshold = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") FLinearColor BloomTint = FLinearColor::White;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float ChromaticAberration = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float LensDistortion = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float VignetteIntensity = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") FLinearColor VignetteColor = FLinearColor::Black;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float MotionBlur = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float FilmGrain = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float PostExposure = 0.0f;

    /** Unity's -100..100 scale. */
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float Contrast = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float Saturation = 0.0f;

    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") FLinearColor ColorFilter = FLinearColor::White;
    UPROPERTY(BlueprintReadWrite, Category="CML|Cinematic") float Panini = 0.0f;

    /**
     * The neutral cruise look. Every shot starts here and pushes only the two
     * or three values that carry its intent — which is what stops each shot
     * from having to restate the whole grade and drifting from the others.
     */
    static FCMLIntroGradeState Cruise();

    /** Blends two looks. `Alpha` is clamped, so a shot cannot overshoot its own grade. */
    static FCMLIntroGradeState Blend(
        const FCMLIntroGradeState& From, const FCMLIntroGradeState& To, float Alpha);
};
