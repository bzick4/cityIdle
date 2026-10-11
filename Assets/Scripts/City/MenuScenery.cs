using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Процедурные плоские текстуры для меню: небо на закате, солнце, три слоя гор с елями,
    /// скошенная кнопка, иконки и дуга логотипа. Всё считается один раз при открытии меню.
    /// </summary>
    public static class MenuScenery
    {
        static Texture2D Make(int w, int h, Color32[] px)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Color C(int rgb, float a)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        }

        // ---------- небо ----------

        static readonly float[] SkyAt = { 0f, 0.30f, 0.55f, 0.70f, 0.82f, 1f };

        static readonly Color[] SkyCol =
        {
            new Color(0.106f, 0.145f, 0.251f, 0.92f),
            new Color(0.169f, 0.227f, 0.369f, 0.70f),
            new Color(0.416f, 0.353f, 0.478f, 0.45f),
            new Color(0.910f, 0.588f, 0.361f, 0.55f),
            new Color(0.941f, 0.690f, 0.439f, 0.30f),
            new Color(0.102f, 0.122f, 0.200f, 0.90f)
        };

        /// <summary>Вертикальный градиент поверх города: сверху тёмно-синий, у горизонта оранжевый.</summary>
        public static Texture2D Sky()
        {
            const int h = 128;
            var px = new Color32[2 * h];
            for (int y = 0; y < h; y++)
            {
                float f = 1f - y / (h - 1f);          // 0 вверху экрана
                int i = 0;
                while (i < SkyAt.Length - 2 && f > SkyAt[i + 1]) i++;
                float t = Mathf.InverseLerp(SkyAt[i], SkyAt[i + 1], f);
                Color c = Color.Lerp(SkyCol[i], SkyCol[i + 1], t);
                px[y * 2] = c;
                px[y * 2 + 1] = c;
            }

            return Make(2, h, px);
        }

        // ---------- солнце ----------

        public static Texture2D Sun()
        {
            const int n = 128;
            var px = new Color32[n * n];
            Color sun = C(0xFFD678, 1f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - 63.5f) / 63.5f, dy = (y - 63.5f) / 63.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float disc = Mathf.Clamp01((0.28f - d) / 0.02f + 0.5f);
                float g = d < 1f ? 0.5f * Mathf.Pow(1f - Mathf.Max(0f, d - 0.28f) / 0.72f, 2f) : 0f;
                Color glow = C(0xFFAA5A, g);
                px[y * n + x] = Color.Lerp(glow, sun, disc);
            }

            return Make(n, n, px);
        }

        // ---------- горы и ели ----------

        static float Ridge(float u)
        {
            return 1f - Mathf.Abs(Mathf.Sin(u * Mathf.PI));
        }

        static int[] Layer(Color32[] px, int w, int h, Color32 col, float baseF, float amp,
            float f1, float p1, float f2, float p2, float edge, bool haze)
        {
            var warm = new Color32(0xD9, 0x8A, 0x6A, 255);
            var tops = new int[w];
            for (int x = 0; x < w; x++)
            {
                float x01 = x / (w - 1f);
                float n = Ridge(x01 * f1 + p1) * 0.6f + Ridge(x01 * f2 + p2) * 0.4f;
                float e1 = Mathf.Max(0f, 1f - x01 / 0.2f);
                float e2 = Mathf.Max(0f, 1f - (1f - x01) / 0.2f);
                float hf = baseF + amp * n + edge * (e1 * e1 + e2 * e2);
                int top = Mathf.Clamp(Mathf.RoundToInt(hf * h), 1, h - 1);
                tops[x] = top;
                for (int y = 0; y < top; y++)
                {
                    Color32 c = col;
                    if (haze)
                    {
                        float t = (top - y) / 36f;
                        if (t < 1f) c = Color32.Lerp(col, warm, (1f - t) * 0.35f);
                    }

                    px[y * w + x] = c;
                }
            }

            return tops;
        }

        static void Pine(Color32[] px, int w, int h, int cx, int baseY, int ph, Color32 col)
        {
            for (int k = 0; k < 5; k++)
            {
                int y0 = baseY + (int)(ph * k * 0.17f);
                int th = (int)(ph * 0.40f);
                float hw = ph * 0.24f * (1f - k * 0.16f);
                for (int dy = 0; dy < th; dy++)
                {
                    int y = y0 + dy;
                    if (y < 0 || y >= h) continue;
                    float hy = hw * (1f - dy / (float)th);
                    int xa = Mathf.Max(0, Mathf.RoundToInt(cx - hy));
                    int xb = Mathf.Min(w - 1, Mathf.RoundToInt(cx + hy));
                    for (int x = xa; x <= xb; x++) px[y * w + x] = col;
                }
            }
        }

        /// <summary>Три слоя гор (дальний светлее, с тёплой дымкой) и тёмные ели по краям. Выше гор прозрачно.</summary>
        public static Texture2D Ridges()
        {
            const int w = 512, h = 256;
            var px = new Color32[w * h];
            var far = new Color32(0x5B, 0x5F, 0x8C, 255);
            var mid = new Color32(0x3B, 0x40, 0x66, 255);
            var nearC = new Color32(0x1B, 0x1F, 0x33, 255);

            Layer(px, w, h, far, 0.55f, 0.20f, 1.3f, 0.2f, 3.1f, 0.7f, 0f, true);
            Layer(px, w, h, mid, 0.38f, 0.16f, 1.9f, 0.5f, 4.3f, 0.1f, 0f, true);
            int[] near = Layer(px, w, h, nearC, 0.20f, 0.10f, 2.6f, 0.9f, 6f, 0.3f, 0.20f, false);

            var rng = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                int x = rng.Next(6, 95);
                Pine(px, w, h, x, near[x] - 3, rng.Next(55, 105), nearC);
                x = rng.Next(420, 506);
                Pine(px, w, h, x, near[x] - 3, rng.Next(55, 105), nearC);
            }

            for (int i = 0; i < 8; i++)
            {
                int x = rng.Next(100, 410);
                Pine(px, w, h, x, near[x] - 3, rng.Next(22, 38), nearC);
            }

            return Make(w, h, px);
        }

        // ---------- кнопка ----------

        /// <summary>Белая плашка со скошенными углами. Растягивается через границы GUIStyle.</summary>
        public static Texture2D Chamfer()
        {
            const int n = 64;
            const float c = 14f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Min(
                    Mathf.Min(x + y - c, (n - 1 - x) + y - c),
                    Mathf.Min(x + (n - 1 - y) - c, (n - 1 - x) + (n - 1 - y) - c));
                float a = Mathf.Clamp01(d / 1.414f + 0.5f);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }

            return Make(n, n, px);
        }

        // ---------- иконки ----------

        public const int IconHexPlus = 0;
        public const int IconFolder = 1;
        public const int IconHex = 2;

        /// <summary>Белые иконки 48×48, цвет задаётся через GUI.color.</summary>
        public static Texture2D Icon(int kind)
        {
            const int n = 48;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px1 = (x + 0.5f) / n * 2f - 1f;
                float py1 = (y + 0.5f) / n * 2f - 1f;
                float ax = Mathf.Abs(px1), ay = Mathf.Abs(py1);
                bool on = false;
                if (kind == IconHexPlus || kind == IconHex)
                {
                    float d = Mathf.Max(ax, 0.5f * ax + 0.866f * ay);
                    on = d <= 0.9f && d >= 0.70f;
                    if (kind == IconHexPlus)
                        on |= (ax < 0.42f && ay < 0.09f) || (ay < 0.42f && ax < 0.09f);
                }
                else
                {
                    bool body = px1 > -0.82f && px1 < 0.82f && py1 > -0.55f && py1 < 0.45f;
                    bool tab = px1 > -0.82f && px1 < -0.1f && py1 >= 0.45f && py1 < 0.68f;
                    on = body || tab;
                }

                px[y * n + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }

            return Make(n, n, px);
        }

        // ---------- дуга логотипа ----------

        public static Texture2D Arc()
        {
            const int w = 256, h = 96;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float ex = (x + 0.5f - w * 0.5f) / (w * 0.5f);
                float ey = (y + 0.5f) / h;
                float e = Mathf.Sqrt(ex * ex + ey * ey);
                float a = 1f - Mathf.Clamp01((Mathf.Abs(e - 0.96f) - 0.018f) / 0.02f);
                px[y * w + x] = new Color(1f, 1f, 1f, a);
            }

            return Make(w, h, px);
        }
    }
}
