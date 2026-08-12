#include "Presentation/CMLIntroGradeState.h"

FCMLIntroGradeState FCMLIntroGradeState::Cruise()
{
    FCMLIntroGradeState State;
    State.BloomIntensity = 1.15f;
    State.BloomThreshold = 0.82f;
    // A cool bloom: the airship's cruise is lit from a pale sky.
    State.BloomTint = FLinearColor(0.78f, 0.88f, 1.0f, 1.0f);
    State.ChromaticAberration = 0.08f;
    State.LensDistortion = 0.0f;
    State.VignetteIntensity = 0.28f;
    State.VignetteColor = FLinearColor::Black;
    State.MotionBlur = 0.12f;
    State.FilmGrain = 0.18f;
    State.PostExposure = 0.0f;
    State.Contrast = 12.0f;
    State.Saturation = 6.0f;
    State.ColorFilter = FLinearColor::White;
    State.Panini = 0.0f;
    return State;
}

FCMLIntroGradeState FCMLIntroGradeState::Blend(
    const FCMLIntroGradeState& From, const FCMLIntroGradeState& To, const float Alpha)
{
    // Clamped, so a shot that runs past its own transition holds the target look
    // instead of continuing to push the grade beyond it.
    const float T = FMath::Clamp(Alpha, 0.0f, 1.0f);

    FCMLIntroGradeState State;
    State.BloomIntensity = FMath::Lerp(From.BloomIntensity, To.BloomIntensity, T);
    State.BloomThreshold = FMath::Lerp(From.BloomThreshold, To.BloomThreshold, T);
    State.BloomTint = FMath::Lerp(From.BloomTint, To.BloomTint, T);
    State.ChromaticAberration =
        FMath::Lerp(From.ChromaticAberration, To.ChromaticAberration, T);
    State.LensDistortion = FMath::Lerp(From.LensDistortion, To.LensDistortion, T);
    State.VignetteIntensity = FMath::Lerp(From.VignetteIntensity, To.VignetteIntensity, T);
    State.VignetteColor = FMath::Lerp(From.VignetteColor, To.VignetteColor, T);
    State.MotionBlur = FMath::Lerp(From.MotionBlur, To.MotionBlur, T);
    State.FilmGrain = FMath::Lerp(From.FilmGrain, To.FilmGrain, T);
    State.PostExposure = FMath::Lerp(From.PostExposure, To.PostExposure, T);
    State.Contrast = FMath::Lerp(From.Contrast, To.Contrast, T);
    State.Saturation = FMath::Lerp(From.Saturation, To.Saturation, T);
    State.ColorFilter = FMath::Lerp(From.ColorFilter, To.ColorFilter, T);
    State.Panini = FMath::Lerp(From.Panini, To.Panini, T);
    return State;
}
