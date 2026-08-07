using System;
using UnityEngine;

namespace CML.Unity.Presentation.Intro
{
    /// <summary>
    /// The frozen teaching card shown when the opening asks the player to fly
    /// for themselves. Both glyphs are rasterised at runtime, so the prompt
    /// needs no imported sprite and scales with the screen.
    /// </summary>
    public sealed class IntroTutorialPrompt : IDisposable
    {
        private const int MouseWidth = 84;
        private const int MouseHeight = 128;
        private const int ArrowSize = 72;

        private Texture2D _mouse;
        private Texture2D _arrow;
        private GUIStyle _textStyle;

        public void Draw(string before, string after, float direction, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            if (alpha <= 0.002f)
            {
                return;
            }

            EnsureResources();

            var scale = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2.2f);
            _textStyle.fontSize = Mathf.RoundToInt(34f * scale);

            var iconWidth = MouseWidth * scale * 0.62f;
            var iconHeight = MouseHeight * scale * 0.62f;
            var arrowWidth = ArrowSize * scale * 0.62f;
            var gap = 14f * scale;

            var beforeSize = _textStyle.CalcSize(new GUIContent(before));
            var afterSize = _textStyle.CalcSize(new GUIContent(after));
            var totalWidth = beforeSize.x + gap + iconWidth + gap + afterSize.x;

            var centerX = Screen.width * 0.5f;
            var centerY = Screen.height * 0.5f;
            var left = centerX - totalWidth * 0.5f;

            // The icon column is taller than the text, so the row is centred on
            // the mouse body rather than on the glyph stack.
            var iconTop = centerY - iconHeight * 0.5f;
            var textTop = centerY - beforeSize.y * 0.5f;

            var previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(
                new Rect(left, textTop, beforeSize.x, beforeSize.y),
                before,
                _textStyle);

            var iconLeft = left + beforeSize.x + gap;
            GUI.DrawTexture(
                new Rect(iconLeft, iconTop, iconWidth, iconHeight),
                _mouse,
                ScaleMode.ScaleToFit);

            // The arrow sits under the mouse, as a direction the device moves.
            var arrowRect = new Rect(
                iconLeft + (iconWidth - arrowWidth) * 0.5f,
                iconTop + iconHeight + 6f * scale,
                arrowWidth,
                arrowWidth);
            var matrix = GUI.matrix;
            if (direction < 0f)
            {
                GUIUtility.ScaleAroundPivot(
                    new Vector2(-1f, 1f),
                    arrowRect.center);
            }

            GUI.DrawTexture(arrowRect, _arrow, ScaleMode.ScaleToFit);
            GUI.matrix = matrix;

            GUI.Label(
                new Rect(
                    iconLeft + iconWidth + gap,
                    textTop,
                    afterSize.x,
                    afterSize.y),
                after,
                _textStyle);
            GUI.color = previousColor;
        }

        public void Dispose()
        {
            DestroyTexture(ref _mouse);
            DestroyTexture(ref _arrow);
        }

        private void EnsureResources()
        {
            if (_textStyle == null)
            {
                _textStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    wordWrap = false,
                    richText = false
                };
                _textStyle.normal.textColor = Color.white;
            }

            if (_mouse == null)
            {
                _mouse = BuildMouseGlyph();
            }

            if (_arrow == null)
            {
                _arrow = BuildArrowGlyph();
            }
        }

        private static Texture2D BuildMouseGlyph()
        {
            var texture = CreateTexture(MouseWidth, MouseHeight, "CIN_MouseGlyph");
            var pixels = new Color32[MouseWidth * MouseHeight];

            var halfSize = new Vector2(
                MouseWidth * 0.5f - 7f,
                MouseHeight * 0.5f - 7f);
            const float Radius = 34f;
            const float Stroke = 5.5f;

            for (var y = 0; y < MouseHeight; y++)
            {
                for (var x = 0; x < MouseWidth; x++)
                {
                    var point = new Vector2(
                        x - MouseWidth * 0.5f + 0.5f,
                        y - MouseHeight * 0.5f + 0.5f);

                    // Body outline.
                    var body = Mathf.Abs(RoundedBox(point, halfSize, Radius))
                        - Stroke * 0.5f;
                    var coverage = Coverage(body);

                    // The seam between the two buttons stops at the waist.
                    if (point.y > 6f && Mathf.Abs(point.x) < halfSize.x)
                    {
                        var seam = Mathf.Abs(point.x) - Stroke * 0.35f;
                        var withinTop = 6f - point.y;
                        coverage = Mathf.Max(
                            coverage,
                            Coverage(Mathf.Max(seam, withinTop)));
                    }

                    // Scroll wheel.
                    var wheel = RoundedBox(
                        new Vector2(point.x, point.y - 26f),
                        new Vector2(3.4f, 10f),
                        3.4f);
                    coverage = Mathf.Max(coverage, Coverage(wheel));

                    pixels[y * MouseWidth + x] = ToPixel(coverage);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D BuildArrowGlyph()
        {
            var texture = CreateTexture(ArrowSize, ArrowSize, "CIN_ArrowGlyph");
            var pixels = new Color32[ArrowSize * ArrowSize];

            for (var y = 0; y < ArrowSize; y++)
            {
                for (var x = 0; x < ArrowSize; x++)
                {
                    var u = (x + 0.5f) / ArrowSize;
                    var v = (y + 0.5f) / ArrowSize - 0.5f;

                    // Shaft.
                    var shaft = Mathf.Max(
                        Mathf.Abs(v) - 0.085f,
                        Mathf.Max(0.10f - u, u - 0.58f));

                    // Head: a wedge that closes to a point at the tip.
                    var headSpan = Mathf.InverseLerp(0.50f, 0.95f, u);
                    var head = Mathf.Max(
                        Mathf.Abs(v) - Mathf.Lerp(0.30f, 0.0f, headSpan),
                        Mathf.Max(0.50f - u, u - 0.95f));

                    var coverage = Mathf.Max(
                        Coverage(shaft * ArrowSize),
                        Coverage(head * ArrowSize));
                    pixels[y * ArrowSize + x] = ToPixel(coverage);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static float RoundedBox(
            Vector2 point,
            Vector2 halfSize,
            float radius)
        {
            var q = new Vector2(
                Mathf.Abs(point.x) - halfSize.x + radius,
                Mathf.Abs(point.y) - halfSize.y + radius);
            return Mathf.Min(Mathf.Max(q.x, q.y), 0f)
                + new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                - radius;
        }

        private static float Coverage(float signedDistance)
        {
            return Mathf.Clamp01(0.5f - signedDistance);
        }

        private static Color32 ToPixel(float coverage)
        {
            var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f);
            return new Color32(255, 255, 255, alpha);
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            texture = null;
        }
    }
}
