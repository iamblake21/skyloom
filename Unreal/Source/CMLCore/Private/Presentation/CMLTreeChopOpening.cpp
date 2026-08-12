#include "Presentation/CMLTreeChopOpening.h"

FCMLTreeChopOpening FCMLTreeChop::ResolveOpening(const float SectionWidth, const int32 Stage)
{
    const float Progress = FMath::Clamp(
        (Stage - 1.0f) / FMath::Max(1.0f, HitsRequired - 1.0f), 0.0f, 1.0f);

    // A trunk too thin to measure gets a fixed notch rather than the largest
    // one: guessing small leaves an ugly scar, guessing large severs the tree.
    const bool bMeasured = SectionWidth > MeasuredSectionThreshold;

    // Sized against the trunk it is cut into. A notch scaled for a mature trunk
    // would cut a sapling clean in half.
    const float MaximumWidth = bMeasured
        ? FMath::Min(TargetMaximumWidth, SectionWidth * 0.68f)
        : UnmeasuredWidth;
    const float MaximumHeight = bMeasured
        ? FMath::Min(TargetMaximumHeight, SectionWidth * 0.98f)
        : UnmeasuredHeight;

    FCMLTreeChopOpening Opening;
    // The first blow already opens most of the way; the rest widen it. A notch
    // that started tiny would make the early blows look like they missed.
    Opening.Width = FMath::Max(
        MinimumOpeningWidth, MaximumWidth * FMath::Lerp(0.55f, 1.0f, Progress)) * FootprintScale;
    Opening.Height = FMath::Max(
        MinimumOpeningHeight, MaximumHeight * FMath::Lerp(0.52f, 1.0f, Progress)) * FootprintScale;

    // Depth is capped against the section and *not* scaled by the footprint:
    // broadening the scar is cosmetic, deepening it would fell the tree early.
    const float RequestedDepth = FMath::Lerp(0.012f, 0.030f, Progress);
    Opening.Depth = bMeasured
        ? FMath::Min(RequestedDepth, SectionWidth * 0.09f)
        : RequestedDepth;
    return Opening;
}
