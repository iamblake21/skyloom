"""Regression tests for the Unity Y-up -> Unreal Z-up terrain axis mapping."""

from __future__ import annotations

import array
import struct
import unittest

import extract_unity_terrain as terrain


class TerrainAxisMappingTests(unittest.TestCase):
    def test_heightmap_transposes_unity_zx_into_unreal_yx(self) -> None:
        # Unity storage [Z][X]: z0=(1,2), z1=(3,4).
        source = array.array("H", [1, 2, 3, 4])
        payload = terrain.build_heightmap(source, 2, 2)
        actual = list(struct.unpack("<4H", payload))
        self.assertEqual(
            actual,
            [
                terrain.UNREAL_HEIGHT_ORIGIN + 1,
                terrain.UNREAL_HEIGHT_ORIGIN + 3,
                terrain.UNREAL_HEIGHT_ORIGIN + 2,
                terrain.UNREAL_HEIGHT_ORIGIN + 4,
            ],
        )

    def test_weightmaps_transpose_but_control_texture_restores_unity_uvs(self) -> None:
        # Layer 0 values in Unity [Z][X] order. Layer 1 is the complement so
        # interpolation/normalisation remains exact.
        layer0 = (10, 20, 30, 40)
        pixels = bytearray()
        for value in layer0:
            pixels.extend((value, 255 - value, 0, 0))

        weights = terrain.build_weightmaps([(2, 2, bytes(pixels))], 2, 2, 2)
        self.assertEqual(list(weights[0]), [10, 30, 20, 40])
        self.assertEqual(list(weights[1]), [245, 225, 235, 215])

        control = terrain.build_control_tga(weights, 2)
        bgra = control[18:]
        # TGA rows/columns are back in natural texture [UnityZ][UnityX]
        # orientation. The red channel therefore reproduces the source.
        self.assertEqual([bgra[index + 2] for index in range(0, 16, 4)], list(layer0))

    def test_visibility_moves_with_the_transposed_landscape_quads(self) -> None:
        # One Unity hole at z1/x0. Unreal row is Unity X, column is Unity Z.
        source = bytes((255, 255, 0, 255))
        self.assertEqual(
            list(terrain.build_visibilitymap(source, 2, 2)),
            [0, 255, 0, 0],
        )


if __name__ == "__main__":
    unittest.main()
