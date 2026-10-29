using System;
using CityGame.CellRoads;
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

                case Tool.Highway:
                    Fill(px, RBox(10, 4, 54, 60, 5), Slate);
                    Fill(px, Box(30, 5, 34, 59), Yellow);
                    foreach (float y in new[] { 12f, 30f, 48f })
                    {
                        Fill(px, Tri(21, y, 16, y - 6, 26, y - 6), White);
                        Fill(px, Tri(43, y, 38, y + 6, 48, y + 6), White);
                    }
                    break;

                case Tool.OneWay:
                    Fill(px, RBox(19, 4, 45, 60, 5), Slate);
                    foreach (float y in new[] { 8f, 52f }) Fill(px, Box(30, y, 34, y + 6f), Yellow);
                    Fill(px, Tri(32, 43, 22, 30, 42, 30), White);
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

                case Tool.Canal:
                {
                    Color sand = new Color32(0xD5, 0xC2, 0x91, 0xFF);
                    Color water = new Color32(0x6F, 0xB4, 0xE8, 0xFF);
                    Fill(px, RBox(4, 12, 60, 52, 6), sand);
                    Fill(px, Box(4, 22, 60, 42), water);
                    Fill(px, Seg(12, 32, 22, 32, 3), White);
                    Fill(px, Seg(36, 36, 48, 36, 3), White);
                    break;
                }

                case Tool.Port:
                {
                    Color water = new Color32(0xA9, 0xD6, 0xF2, 0xFF);
                    Fill(px, Circle(32, 32, 29), water);
                    Fill(px, RBox(12, 14, 52, 26, 3), new Color32(0xB9, 0x8B, 0x5E, 0xFF));
                    Fill(px, Box(14, 26, 28, 36), Red);
                    Fill(px, Box(30, 26, 44, 34), Yellow);
                    Fill(px, Box(44, 26, 49, 54), Slate);
                    Fill(px, Box(28, 49, 49, 54), Slate);
                    Fill(px, Box(28, 40, 31, 50), Slate);
                    break;
                }

                case Tool.Bus:
                {
                    Color body = new Color32(0x2E, 0x8B, 0x77, 0xFF);
                    Fill(px, Circle(32, 32, 29), new Color32(0xE3, 0xE0, 0xD8, 0xFF));
                    Fill(px, RBox(8, 18, 56, 44, 5), body);
                    Fill(px, Box(8, 30, 56, 33), Color.Lerp(body, White, 0.35f));
                    foreach (float x in new[] { 12f, 22f, 32f, 42f })
                        Fill(px, RBox(x, 34, x + 7, 41, 1.5f), new Color32(0xDC, 0xEE, 0xF6, 0xFF));
                    Fill(px, Circle(19, 18, 5), Slate);
                    Fill(px, Circle(45, 18, 5), Slate);
                    Fill(px, Circle(19, 18, 2), White);
                    Fill(px, Circle(45, 18, 2), White);
                    break;
                }

                case Tool.Ramp:
                    // Диагональная лента: толстая полоса + стрелка
                    Fill(px, Seg(10, 10, 54, 54, 10), Slate);
                    Fill(px, Tri(54, 40, 54, 54, 40, 54), Yellow);
                    Fill(px, Tri(36, 52, 28, 44, 44, 44), White);
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

        /// <summary>Мэр-наставник: лаконичный портрет в той же круглой плашке, что значки служб.</summary>
        public static Texture2D MakeBack()
        {
            var px = new Color[N * N];
            Fill(px, Seg(42, 16, 18, 32, 8), Slate);
            Fill(px, Seg(18, 32, 42, 48, 8), Slate);
            return Texture(px, "BackIcon");
        }

        public static Texture2D MakeMayor()
        {
            var px = new Color[N * N];
            Fill(px, Circle(32, 32, 30), new Color32(0xD9, 0xE0, 0xE5, 0xFF));
            Fill(px, Circle(32, 32, 26), Slate);
            Fill(px, Circle(32, 32, 21), new Color32(0xA9, 0xD1, 0x8E, 0xFF));
            // Плечи и лицо.
            Fill(px, RBox(14, 8, 50, 28, 8), new Color32(0xF7, 0xF7, 0xF4, 0xFF));
            Fill(px, Circle(32, 34, 12), new Color32(0xF0, 0xC9, 0xA5, 0xFF));
            // Кепка мэра и золотой значок.
            Fill(px, RBox(18, 42, 46, 48, 3), Yellow);
            Fill(px, Tri(32, 58, 18, 45, 46, 45), Slate);
            Fill(px, RBox(16, 43, 48, 47, 2), Slate);
            Fill(px, Circle(32, 45, 3), Yellow);
            // Галстук и пуговицы.
            Fill(px, Tri(32, 28, 27, 18, 32, 12), Yellow);
            Fill(px, Tri(32, 28, 37, 18, 32, 12), Yellow);
            return Texture(px, "MayorIcon");
        }

        public static Texture2D MakeService(ServiceKind kind)
        {
            var px = new Color[N * N];
            Color tint = kind == ServiceKind.Ambulance ? new Color32(0xE7, 0x79, 0x79, 0xFF) :
                         kind == ServiceKind.Police ? new Color32(0x73, 0x9A, 0xD2, 0xFF) :
                         kind == ServiceKind.PowerPlant ? new Color32(0xE9, 0xB2, 0x2F, 0xFF) :
                         kind == ServiceKind.WaterTower ? new Color32(0x4F, 0xA3, 0xE0, 0xFF) :
                         new Color32(0xEF, 0xA0, 0x57, 0xFF);
            Fill(px, Circle(32, 32, 29), tint);
            Fill(px, Circle(32, 32, 24), White);
            if (kind == ServiceKind.Ambulance)
            {
                Fill(px, RBox(28, 13, 36, 51, 2), tint);
                Fill(px, RBox(13, 28, 51, 36, 2), tint);
            }
            else if (kind == ServiceKind.PowerPlant)
            {
                // Молния.
                Fill(px, Tri(36, 8, 18, 36, 31, 36), tint);
                Fill(px, Tri(28, 56, 46, 28, 33, 28), tint);
                Fill(px, Box(27, 28, 38, 38), tint);
            }
            else if (kind == ServiceKind.WaterTower)
            {
                // Капля.
                Fill(px, Tri(32, 8, 18, 34, 46, 34), tint);
                Fill(px, Circle(32, 40, 13), tint);
                Fill(px, Circle(27, 42, 3), White);
            }
            else if (kind == ServiceKind.Police)
            {
                Fill(px, Tri(32, 12, 14, 25, 20, 48), tint);
                Fill(px, Tri(32, 12, 20, 48, 44, 48), tint);
                Fill(px, Tri(32, 12, 44, 48, 50, 25), tint);
                Fill(px, Box(29, 22, 35, 40), White);
                Fill(px, Circle(32, 17, 3), White);
            }
            else
            {
                Fill(px, Tri(32, 10, 18, 34, 27, 32), tint);
                Fill(px, Tri(32, 10, 27, 32, 39, 31), tint);
                Fill(px, Tri(32, 10, 39, 31, 48, 34), tint);
                Fill(px, Circle(32, 24, 7), White);
                Fill(px, Circle(32, 24, 4), tint);
            }

            return Texture(px, "ServiceIcon" + kind);
        }

        public static Texture2D MakeJunction(JunctionType type)
        {
            var px = new Color[N * N];
            Fill(px, Circle(32, 32, 29), new Color32(0xD9, 0xE0, 0xE5, 0xFF));
            Fill(px, Circle(32, 32, 25), White);
            Color road = Slate;
            if (JunctionSpecs.IsOverpass(type))
            {
                bool vertical = type == JunctionType.OverpassVertical;
                Fill(px, vertical ? RBox(5, 25, 59, 39, 5) : RBox(25, 5, 39, 59, 5),
                    new Color32(0x8B, 0x9A, 0xA7, 0xFF));
                Fill(px, vertical ? RBox(25, 5, 39, 59, 5) : RBox(5, 25, 59, 39, 5), road);
                Fill(px, vertical ? Box(30, 8, 34, 56) : Box(8, 30, 56, 34), Yellow);
            }
            else if (type == JunctionType.Roundabout || type == JunctionType.HighwayRoundabout)
            {
                bool highway = type == JunctionType.HighwayRoundabout;
                Fill(px, Circle(32, 32, highway ? 28 : 24), road);
                Fill(px, Circle(32, 32, highway ? 11 : 10), new Color32(0xA9, 0xD1, 0x8E, 0xFF));
                Fill(px, Circle(32, 32, 5), Yellow);
                if (highway)
                {
                    Fill(px, Box(2, 30, 11, 34), Yellow);
                    Fill(px, Box(53, 30, 62, 34), Yellow);
                }
            }
            else
            {
                Fill(px, RBox(5, 25, 59, 39, 5), road);
                Fill(px, RBox(25, 5, 39, 59, 5), road);
                Fill(px, Circle(32, 32, 5), Yellow);
            }
            return Texture(px, "JunctionIcon" + type);
        }

        static Texture2D Texture(Color[] pixels, string title)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = title,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels(pixels);
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
