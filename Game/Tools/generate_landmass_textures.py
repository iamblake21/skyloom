#!/usr/bin/env python3
"""Generate the original CML landmass texture set.

The generator is deterministic and clean-room: it builds periodic Fourier fields
and analytic, vertically stretched erosion forms.  It intentionally avoids
photographic samples and high-frequency point noise so the result remains
stylised, readable and free of image-generation artefacts.
"""

from __future__ import annotations

import gc
import math
from pathlib import Path

import numpy as np
from PIL import Image


SEED = 0xC11FF5
GAME_ROOT = Path(__file__).resolve().parents[1]
WORKSPACE_ROOT = GAME_ROOT.parent
OUTPUT_DIR = (
    GAME_ROOT
    / "Assets"
    / "_Project"
    / "Art"
    / "Environment"
    / "StarterIsland"
    / "Landmass"
    / "Textures"
)
PREVIEW_PATH = WORKSPACE_ROOT / "Artifacts" / "landmass_texture_preview.png"


def _standardize(field: np.ndarray) -> np.ndarray:
    field = field.astype(np.float32, copy=False)
    field -= np.mean(field, dtype=np.float64)
    deviation = float(np.std(field, dtype=np.float64))
    if deviation < 1.0e-7:
        raise RuntimeError("Degenerate spectral field")
    field /= deviation
    return field


def spectral_field(
    size: int,
    seed: int,
    x_cutoff: float,
    y_cutoff: float,
    x_highpass: float = 0.0,
    rolloff: float = 4.0,
) -> np.ndarray:
    """Return a smooth, exactly periodic anisotropic random field.

    X frequencies are deliberately allowed to be much higher than Y frequencies,
    producing long vertical geological strokes instead of isotropic speckle.
    """

    rng = np.random.default_rng(seed)
    noise = rng.standard_normal((size, size)).astype(np.float32)
    spectrum = np.fft.rfft2(noise)
    del noise

    frequency_y = np.abs(np.fft.fftfreq(size) * size).astype(np.float32)[:, None]
    frequency_x = (np.fft.rfftfreq(size) * size).astype(np.float32)[None, :]
    falloff = np.exp(
        -0.5
        * (
            np.power(frequency_x / x_cutoff, rolloff)
            + np.power(frequency_y / y_cutoff, rolloff)
        )
    )
    if x_highpass > 0.0:
        falloff *= 1.0 - np.exp(
            -0.5 * np.power(frequency_x / x_highpass, rolloff)
        )
    falloff[0, 0] = 0.0
    spectrum *= falloff
    del falloff, frequency_x, frequency_y

    field = np.fft.irfft2(spectrum, s=(size, size)).astype(np.float32)
    del spectrum
    return _standardize(field)


def periodic_vertical_creases(size: int, seed: int, count: int = 11) -> np.ndarray:
    """Create a handful of broad, eroded vertical clefts with soft shoulders."""

    rng = np.random.default_rng(seed)
    u = np.arange(size, dtype=np.float32)[None, :] / float(size)
    v = np.arange(size, dtype=np.float32)[:, None] / float(size)
    result = np.zeros((size, size), dtype=np.float32)

    for _ in range(count):
        centre = float(rng.uniform(0.0, 1.0))
        width = float(rng.uniform(0.012, 0.042))
        amplitude = float(rng.uniform(0.35, 0.90))
        waviness = float(rng.uniform(0.005, 0.018))
        vertical_frequency = int(rng.integers(1, 4))
        phase = float(rng.uniform(0.0, math.tau))

        wandering_centre = centre + waviness * np.sin(
            math.tau * vertical_frequency * v + phase
        )
        distance = np.abs(u - wandering_centre)
        distance = np.minimum(distance, 1.0 - distance)
        core = np.exp(-np.square(distance / width))
        shoulder = np.exp(-np.square(distance / (width * 2.4)))
        vertical_modulation = 0.82 + 0.18 * np.sin(
            math.tau * (vertical_frequency + 1) * v + phase * 0.71
        )
        result += amplitude * (0.36 * shoulder - core) * vertical_modulation

    return _standardize(result)


def periodic_vertical_streaks(size: int, seed: int, count: int = 52) -> np.ndarray:
    """Build narrow, broken erosion strokes without directional baked lighting."""

    rng = np.random.default_rng(seed)
    u = np.arange(size, dtype=np.float32)[None, :] / float(size)
    v = np.arange(size, dtype=np.float32)[:, None] / float(size)
    result = np.zeros((size, size), dtype=np.float32)

    for _ in range(count):
        centre = float(rng.uniform(0.0, 1.0))
        width = float(rng.uniform(0.0012, 0.0065))
        amplitude = float(rng.uniform(0.18, 0.58))
        waviness = float(rng.uniform(0.0015, 0.0100))
        vertical_frequency = int(rng.integers(1, 6))
        phase = float(rng.uniform(0.0, math.tau))

        wandering_centre = centre + waviness * (
            0.72 * np.sin(math.tau * vertical_frequency * v + phase)
            + 0.28
            * np.sin(
                math.tau * (vertical_frequency + 2) * v + phase * 1.71
            )
        )
        distance = np.abs(u - wandering_centre)
        distance = np.minimum(distance, 1.0 - distance)
        core = np.exp(-np.square(distance / width))
        shoulder = np.exp(-np.square(distance / (width * 2.8)))
        broken = 0.62 + 0.38 * smoothstep01(
            0.5
            + 0.5
            * np.sin(
                math.tau * (vertical_frequency + 1) * v + phase * 0.63
            )
        )
        polarity = -1.0 if rng.random() < 0.72 else 1.0
        result += amplitude * polarity * (core - 0.30 * shoulder) * broken

    return _standardize(result)


def robust_unit(field: np.ndarray, low: float = 1.0, high: float = 99.0) -> np.ndarray:
    lo, hi = np.percentile(field, (low, high))
    return np.clip((field - lo) / max(float(hi - lo), 1.0e-6), 0.0, 1.0).astype(
        np.float32
    )


def smoothstep01(value: np.ndarray) -> np.ndarray:
    value = np.clip(value, 0.0, 1.0)
    return value * value * (3.0 - 2.0 * value)


def save_rgb(path: Path, pixels: np.ndarray) -> None:
    encoded = np.clip(np.rint(pixels * 255.0), 0.0, 255.0).astype(np.uint8)
    Image.fromarray(encoded, mode="RGB").save(path, optimize=True)


def generate_cliff(size: int = 2048) -> tuple[Path, Path]:
    # The source-study spectrum has two distinct scales: very broad colour masses
    # and a restrained layer of fine erosion.  Keeping those bands separate avoids
    # the evenly spaced, airbrushed ribbons produced by one mid-frequency field.
    macro = spectral_field(size, SEED + 101, 4.2, 1.8)
    broad_ridges = spectral_field(
        size, SEED + 102, 9.0, 3.0, x_highpass=0.6
    )
    vertical_ridges = spectral_field(
        size,
        SEED + 103,
        24.0,
        5.5,
        x_highpass=2.0,
        rolloff=3.0,
    )
    creases = periodic_vertical_creases(size, SEED + 104, count=9)
    narrow_streaks = periodic_vertical_streaks(size, SEED + 110)
    albedo_meso_vertical = spectral_field(
        size,
        SEED + 111,
        110.0,
        13.0,
        x_highpass=18.0,
        rolloff=2.5,
    )
    albedo_fine_vertical = spectral_field(
        size,
        SEED + 112,
        330.0,
        34.0,
        x_highpass=82.0,
        rolloff=2.1,
    )
    meso_erosion = spectral_field(
        size,
        SEED + 105,
        80.0,
        35.0,
        x_highpass=14.0,
        rolloff=2.3,
    )
    fine_erosion = spectral_field(
        size,
        SEED + 106,
        220.0,
        140.0,
        x_highpass=55.0,
        rolloff=2.0,
    )
    micro_erosion = spectral_field(
        size,
        SEED + 107,
        500.0,
        320.0,
        x_highpass=180.0,
        rolloff=2.0,
    )

    # Broad derivatives shape albedo without turning high-frequency normal detail
    # into baked highlights.  The complete height field is reserved for normals.
    broad_height = (
        0.42 * macro
        + 0.22 * broad_ridges
        + 0.14 * np.tanh(vertical_ridges * 0.70)
        + 0.12 * creases
    ).astype(np.float32)
    broad_height = _standardize(broad_height)

    broad_derivative_x = 0.5 * (
        np.roll(broad_height, -1, axis=1)
        - np.roll(broad_height, 1, axis=1)
    )
    broad_derivative_y = 0.5 * (
        np.roll(broad_height, -1, axis=0)
        - np.roll(broad_height, 1, axis=0)
    )
    relief_light = np.tanh(
        (-broad_derivative_x * 46.0) + (broad_derivative_y * 10.0)
    )

    # Albedo stays deliberately restrained: the mesh and normal map provide most
    # of the sculpting.  Stretching this signal to full black/white would bake
    # harsh fake lighting into the surface and make repeated modules conspicuous.
    tone = np.clip(
        0.50
        + 0.210 * np.tanh(macro * 0.65)
        + 0.070 * np.tanh(broad_ridges * 0.80)
        + 0.052 * np.tanh(vertical_ridges * 0.72)
        + 0.050 * relief_light
        + 0.040 * np.tanh(creases * 0.75)
        + 0.070 * np.tanh(narrow_streaks * 0.82)
        + 0.054 * np.tanh(albedo_meso_vertical * 0.88)
        + 0.030 * np.tanh(albedo_fine_vertical * 0.82),
        0.10,
        0.90,
    )
    tone += 0.050 * np.tanh(meso_erosion * 0.85)
    tone += 0.034 * np.tanh(fine_erosion * 0.80)
    tone += 0.010 * np.tanh(micro_erosion * 0.70)
    tone = np.clip(tone, 0.10, 0.90)

    shadow = np.array([0.540, 0.245, 0.150], dtype=np.float32)
    mid = np.array([0.745, 0.435, 0.305], dtype=np.float32)
    sunlit = np.array([0.920, 0.640, 0.470], dtype=np.float32)
    lower_t = smoothstep01(np.minimum(tone * 2.0, 1.0))[..., None]
    upper_t = smoothstep01(np.maximum(tone * 2.0 - 1.0, 0.0))[..., None]
    albedo = shadow + (mid - shadow) * lower_t
    albedo = albedo + (sunlit - mid) * upper_t

    # Extremely broad warm/cool drift keeps large repeated pieces from looking flat.
    warmth = (0.5 + 0.5 * np.tanh(macro * 0.52))[..., None]
    albedo *= np.array([0.965, 0.985, 1.015], dtype=np.float32) + warmth * np.array(
        [0.055, 0.006, -0.045], dtype=np.float32
    )
    albedo = np.clip(albedo, 0.0, 1.0)

    albedo_path = OUTPUT_DIR / "T_CML_LandmassCliff_Albedo.png"
    save_rgb(albedo_path, albedo)
    del albedo, lower_t, upper_t, tone, relief_light, warmth
    gc.collect()

    # The source normal is made of long vertical erosion strokes, not an
    # isotropic granular surface.  The previous clean-room normal gave the
    # high-frequency fields too much weight and amplified the vertical image
    # derivative; under a directional light that became the diagonal
    # cross-hatch visible on every Terrain wall.  Keep the broad sculpt and
    # retain only a restrained amount of fine erosion for the normal.
    normal_meso_vertical = spectral_field(
        size,
        SEED + 108,
        92.0,
        10.0,
        x_highpass=14.0,
        rolloff=2.6,
    )
    normal_fine_vertical = spectral_field(
        size,
        SEED + 109,
        260.0,
        22.0,
        x_highpass=68.0,
        rolloff=2.2,
    )
    height = (
        0.44 * macro
        + 0.25 * broad_ridges
        + 0.17 * np.tanh(vertical_ridges * 0.70)
        + 0.11 * creases
        + 0.026 * normal_meso_vertical
        + 0.007 * normal_fine_vertical
    ).astype(np.float32)
    height = _standardize(height)
    derivative_x = 0.5 * (
        np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)
    )
    derivative_y = 0.5 * (
        np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)
    )

    # Shape gradient magnitude rather than applying one blanket multiplier.  This
    # preserves calm planes while retaining the sparse, steep erosion cuts that
    # make the material react clearly to light.  The constants were calibrated
    # against clean-room distribution measurements, not source pixels.
    gradient_x = derivative_x
    gradient_y = derivative_y * 0.34
    gradient_magnitude = np.sqrt(
        gradient_x * gradient_x + gradient_y * gradient_y
    )
    median_gradient = max(float(np.median(gradient_magnitude)), 1.0e-7)
    shaped_gradient = (
        np.power(
            np.maximum(gradient_magnitude / median_gradient, 1.0e-8),
            1.52,
        )
        # M_Island_Landmass applies a 1.3 normal-strength multiplier.  The
        # texture therefore targets roughly 12 degrees before the material,
        # rather than incorrectly matching the fully-strengthened source and
        # then multiplying it by 1.3 a second time.
        * math.tan(math.radians(12.2))
    )
    inverse_gradient = 1.0 / np.maximum(gradient_magnitude, 1.0e-8)
    normal_x = -gradient_x * inverse_gradient * shaped_gradient
    normal_y = -gradient_y * inverse_gradient * shaped_gradient
    normal_z = np.ones_like(normal_x, dtype=np.float32)
    inverse_length = 1.0 / np.sqrt(
        normal_x * normal_x + normal_y * normal_y + normal_z * normal_z
    )
    normal = np.stack(
        (
            normal_x * inverse_length * 0.5 + 0.5,
            normal_y * inverse_length * 0.5 + 0.5,
            normal_z * inverse_length * 0.5 + 0.5,
        ),
        axis=-1,
    )
    normal_path = OUTPUT_DIR / "T_CML_LandmassCliff_Normal.png"
    save_rgb(normal_path, normal)

    del macro, vertical_ridges, broad_ridges, creases, narrow_streaks
    del albedo_meso_vertical, albedo_fine_vertical
    del meso_erosion, fine_erosion, micro_erosion
    del normal_meso_vertical, normal_fine_vertical
    del broad_height, broad_derivative_x, broad_derivative_y, height
    del derivative_x, derivative_y, gradient_x, gradient_y
    del gradient_magnitude, shaped_gradient, inverse_gradient
    del normal_x, normal_y, normal_z, inverse_length, normal
    gc.collect()
    return albedo_path, normal_path


def generate_grass_variation(size: int = 1024) -> Path:
    broad = spectral_field(size, SEED + 20, 3.6, 3.2)
    secondary = spectral_field(size, SEED + 21, 6.5, 5.4)
    directional = spectral_field(size, SEED + 22, 5.2, 2.4)
    combined = (
        0.72 * broad
        + 0.20 * np.tanh(secondary * 0.72)
        + 0.08 * directional
    )
    mask = smoothstep01(robust_unit(combined, 1.0, 99.0))
    # Match the measured clean-room distribution while retaining entirely new
    # spatial content.  Quantile remapping compresses the excessive contrast that
    # previously made the grass edge read like a fluorescent coverage cutout.
    source_percentiles = np.array(
        [0.0, 1.0, 5.0, 10.0, 25.0, 50.0, 75.0, 90.0, 95.0, 99.0, 100.0],
        dtype=np.float32,
    )
    target_values = np.array(
        [52.0, 66.0, 77.0, 91.0, 115.0, 149.0, 187.0, 210.0, 229.0, 252.0, 255.0],
        dtype=np.float32,
    ) / 255.0
    input_values = np.percentile(mask, source_percentiles)
    mask = np.interp(mask, input_values, target_values).astype(np.float32)
    encoded = np.clip(np.rint(mask * 255.0), 0.0, 255.0).astype(np.uint8)
    path = OUTPUT_DIR / "T_CML_LandmassGrassVariation.png"
    Image.fromarray(encoded, mode="L").save(path, optimize=True)
    return path


def seam_metrics(path: Path) -> dict[str, float]:
    pixels = np.asarray(Image.open(path), dtype=np.float32) / 255.0
    if pixels.ndim == 2:
        pixels = pixels[..., None]
    seam_x = float(np.mean(np.abs(pixels[:, 0] - pixels[:, -1])))
    seam_y = float(np.mean(np.abs(pixels[0] - pixels[-1])))
    interior_x = float(np.mean(np.abs(pixels[:, 1:] - pixels[:, :-1])))
    interior_y = float(np.mean(np.abs(pixels[1:] - pixels[:-1])))
    return {
        "seam_x": seam_x,
        "seam_y": seam_y,
        "ratio_x": seam_x / max(interior_x, 1.0e-8),
        "ratio_y": seam_y / max(interior_y, 1.0e-8),
    }


def make_preview(paths: tuple[Path, Path, Path]) -> None:
    panels = []
    for path in paths:
        image = Image.open(path).convert("RGB")
        image.thumbnail((512, 512), Image.Resampling.LANCZOS)
        panels.append(image)
    PREVIEW_PATH.parent.mkdir(parents=True, exist_ok=True)
    preview = Image.new("RGB", (512 * len(panels), 512), (24, 24, 24))
    for index, panel in enumerate(panels):
        preview.paste(panel, (index * 512, 0))
    preview.save(PREVIEW_PATH, optimize=True)


def describe(path: Path) -> None:
    pixels = np.asarray(Image.open(path), dtype=np.float64) / 255.0
    metrics = seam_metrics(path)
    if pixels.ndim == 3:
        values = pixels * 2.0 - 1.0 if "_Normal" in path.stem else pixels
        mean = np.mean(values, axis=(0, 1))
        deviation = np.std(values, axis=(0, 1))
        minimum = np.min(values, axis=(0, 1))
        maximum = np.max(values, axis=(0, 1))
        print(
            f"{path.name}: shape={pixels.shape}, mean={mean}, std={deviation}, "
            f"min={minimum}, max={maximum}, "
            f"seam-ratio=({metrics['ratio_x']:.3f}, {metrics['ratio_y']:.3f})"
        )
    else:
        print(
            f"{path.name}: shape={pixels.shape}, min={pixels.min():.4f}, "
            f"mean={pixels.mean():.4f}, max={pixels.max():.4f}, "
            f"std={pixels.std():.4f}, "
            f"seam-ratio=({metrics['ratio_x']:.3f}, {metrics['ratio_y']:.3f})"
        )
    if metrics["ratio_x"] > 2.5 or metrics["ratio_y"] > 2.5:
        raise RuntimeError(f"Unexpected tiling seam in {path.name}: {metrics}")


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    albedo_path, normal_path = generate_cliff()
    grass_path = generate_grass_variation()
    paths = (albedo_path, normal_path, grass_path)
    make_preview(paths)
    for path in paths:
        describe(path)
    print(f"Preview: {PREVIEW_PATH}")


if __name__ == "__main__":
    main()
