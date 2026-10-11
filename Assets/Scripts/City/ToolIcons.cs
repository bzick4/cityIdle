using System;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Цветные значки инструментов, рисуются кодом в текстуры 64x64 (сглаживание 3x3). Картинки не нужны.
    /// Координаты 0..64, начало внизу слева. Цвета временные.
    /// </summary>
    public static class ToolIcons
    {
        const int N = 64;

        static readonly Color Slate = new Color32(0x3B, 0x45, 0x52, 0xFF);
        static readonly Color Yellow = new Color32(0xF2, 0xB8, 0x4B, 0xFF);
        static readonly Color Green = new Color32(0xA9, 0xD1, 0x8E, 0xFF);
        static readonly Color Red = new Color32(0xE0, 0x45, 0x45, 0xFF);
        static readonly Color White = new Color32(0xF7, 0xF7, 0xF4, 0xFF);

        public static Texture2D Make(Tool tool)
        {
            var px = new Color[N * N];
            switch (tool)
            {
                case Tool.Road:
                    Fill(px, RBox(22, 4, 42, 60, 3), Slate);
                    foreach (float y in new[] { 10f, 28f, 46f }) Fill(px, Box(30.5f, y, 33.5f, y + 10f), Yellow);
                    break;

                case Tool.Junction:
                    Fill(px, RBox(4, 22, 60, 42, 3), Slate);
                    Fill(px, RBox(22, 4, 42, 60, 3), Slate);
                    Fill(px, Circle(32, 32, 13), Slate);
                    Fill(px, Circle(32, 32, 7), Green);
                    break;

                case Tool.Residential:
                    Fill(px, RBox(12, 8, 52, 34, 3), new Color32(0x6F, 0xA8, 0xDC, 0xFF));
                    Fill(px, Tri(6, 32, 32, 57, 58, 32), new Color32(0x3E, 0x78, 0xB2, 0xFF));
                    Fill(px, Box(27, 8, 37, 22), White);
                    Fill(px, Box(16, 20, 24, 28), White);
                    Fill(px, Box(40, 20, 48, 28), White);
                    break;

                case Tool.Commercial:
                    Fill(px, RBox(10, 8, 54, 40, 3), new Color32(0xE8, 0x83, 0x8B, 0xFF));
                    for (int i = 0; i < 6; i++)
                        Fill(px, Box(10 + i * 7.34f, 40, 10 + (i + 1) * 7.34f, 52),
                            i % 2 == 0 ? White : new Color32(0xD8, 0x50, 0x5B, 0xFF));
                    Fill(px, Box(18, 14, 46, 30), new Color32(0xF8, 0xE8, 0xEA, 0xFF));
                    break;

                case Tool.Industrial:
                    Fill(px, RBox(8, 8, 56, 34, 3), new Color32(0xF2, 0xC1, 0x4E, 0xFF));
                    for (int i = 0; i < 3; i++)
                    {
                        float x = 8 + i * 14;
                        Fill(px, Tri(x, 34, x, 48, x + 14, 34), new Color32(0xD9, 0xA0, 0x2E, 0xFF));
                    }

                    Fill(px, Box(48, 34, 56, 56), new Color32(0x7A, 0x83, 0x8C, 0xFF));
                    Fill(px, Circle(54, 59, 4), new Color(0.9f, 0.9f, 0.9f, 0.9f));
                    Fill(px, Box(14, 14, 24, 22), White);
                    Fill(px, Box(30, 14, 40, 22), White);
                    break;

                case Tool.Service:
                    Fill(px, Circle(32, 32, 28), new Color32(0xC9, 0xD1, 0xD8, 0xFF));
                    Fill(px, Circle(32, 32, 25), White);
                    Fill(px, Box(26, 14, 38, 50), Red);
                    Fill(px, Box(14, 26, 50, 38), Red);
                    break;

                case Tool.Erase:
                    Fill(px, Seg(13, 13, 51, 51, 9), Red);
                    Fill(px, Seg(13, 51, 51, 13, 9), Red);
                    break;

                case Tool.Expand:
                    Fill(px, Hex(32, 32, 29), new Color32(0x8F, 0xE0, 0xCE, 0xFF));
                    Fill(px, Box(28, 16, 36, 48), new Color32(0x2E, 0x8B, 0x77, 0xFF));
                    Fill(px, Box(16, 28, 48, 36), new Color32(0x2E, 0x8B, 0x77, 0xFF));
                    break;
            }

            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // --- растеризация ---

        static void Fill(Color[] px, Func<float, float, bool> inside, Color c)
        {
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                    if (inside(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f)) hit++;
                if (hit == 0) continue;

                float a = hit / 9f * c.a;
                Color d = px[y * N + x];
                float outA = a + d.a * (1f - a);
                px[y * N + x] = new Color(
                    (c.r * a + d.r * d.a * (1f - a)) / outA,
                    (c.g * a + d.g * d.a * (1f - a)) / outA,
                    (c.b * a + d.b * d.a * (1f - a)) / outA, outA);
            }
        }

        static Func<float, float, bool> Box(float x0, float y0, float x1, float y1)
        {
            return (x, y) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
        }

        static Func<float, float, bool> RBox(float x0, float y0, float x1, float y1, float r)
        {
            return (x, y) =>
            {
                float dx = Mathf.Max(Mathf.Max(x0 + r - x, x - (x1 - r)), 0f);
                float dy = Mathf.Max(Mathf.Max(y0 + r - y, y - (y1 - r)), 0f);
                return x >= x0 && x <= x1 && y >= y0 && y <= y1 && dx * dx + dy * dy <= r * r;
            };
        }

        static Func<float, float, bool> Circle(float cx, float cy, float r)
        {
            return (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
        }

        static Func<float, float, bool> Tri(float ax, float ay, float bx, float by, float cx, float cy)
        {
            return (x, y) =>
            {
                float d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
                float d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
                float d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0;
                bool pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            };
        }

        static Func<float, float, bool> Seg(float ax, float ay, float bx, float by, float w)
        {
            return (x, y) =>
            {
                float vx = bx - ax, vy = by - ay;
                float t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / (vx * vx + vy * vy));
                float px = ax + vx * t - x, py = ay + vy * t - y;
                return px * px + py * py <= w * w * 0.25f;
            };
        }

        static Func<float, float, bool> Hex(float cx, float cy, float r)
        {
            var ang = new float[6];
            for (int i = 0; i < 6; i++) ang[i] = Mathf.Deg2Rad * (90f + 60f * i);
            return (x, y) =>
            {
                // Правильный шестиугольник: расстояние до каждой стороны не больше апофемы.
                float apothem = r * 0.8660254f;
                for (int i = 0; i < 6; i++)
                {
                    float a = ang[i] + Mathf.Deg2Rad * 30f;
                    float d = (x - cx) * Mathf.Cos(a) + (y - cy) * Mathf.Sin(a);
                    if (d > apothem) return false;
                }

                return true;
            };
        }
    }
}
