using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>Геометрия ломаных линий: упрощение, сглаживание, поиск ближайшей точки, разрезание.</summary>
    public static class PolylineUtil
    {
        public static float Length(IReadOnlyList<Vector2> p)
        {
            float len = 0f;
            for (int i = 1; i < p.Count; i++) len += Vector2.Distance(p[i - 1], p[i]);
            return len;
        }

        public static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            t = l2 < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return a + ab * t;
        }

        /// <summary>Ближайшая точка на ломаной. segIndex = номер отрезка (от точки segIndex до segIndex + 1).</summary>
        public static Vector2 ClosestPoint(IReadOnlyList<Vector2> pts, Vector2 q, out int segIndex, out float distance)
        {
            segIndex = 0;
            distance = float.MaxValue;
            Vector2 best = pts.Count > 0 ? pts[0] : q;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 c = ClosestOnSegment(q, pts[i], pts[i + 1], out _);
                float d = Vector2.Distance(q, c);
                if (d < distance)
                {
                    distance = d;
                    best = c;
                    segIndex = i;
                }
            }

            return best;
        }

        /// <summary>Алгоритм Рамера-Дугласа-Пекера: убирает лишние точки, оставляя форму линии.</summary>
        public static List<Vector2> Simplify(IReadOnlyList<Vector2> pts, float epsilon)
        {
            int n = pts.Count;
            var result = new List<Vector2>();
            if (n < 3)
            {
                for (int i = 0; i < n; i++) result.Add(pts[i]);
                return result;
            }

            var keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;

            var stackStart = new Stack<int>();
            var stackEnd = new Stack<int>();
            stackStart.Push(0);
            stackEnd.Push(n - 1);

            while (stackStart.Count > 0)
            {
                int s = stackStart.Pop();
                int e = stackEnd.Pop();

                float maxD = 0f;
                int idx = -1;
                for (int i = s + 1; i < e; i++)
                {
                    Vector2 c = ClosestOnSegment(pts[i], pts[s], pts[e], out _);
                    float d = Vector2.Distance(pts[i], c);
                    if (d > maxD)
                    {
                        maxD = d;
                        idx = i;
                    }
                }

                if (idx >= 0 && maxD > epsilon)
                {
                    keep[idx] = true;
                    stackStart.Push(s);
                    stackEnd.Push(idx);
                    stackStart.Push(idx);
                    stackEnd.Push(e);
                }
            }

            for (int i = 0; i < n; i++)
                if (keep[i]) result.Add(pts[i]);
            return result;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                           + (-p0 + p2) * t
                           + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                           + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Сглаживает ломаную кривой Катмулла-Рома и разбивает на точки с шагом около spacing.</summary>
        public static List<Vector2> SmoothCatmullRom(IReadOnlyList<Vector2> pts, float spacing)
        {
            int n = pts.Count;
            var result = new List<Vector2>();
            if (n == 0) return result;
            if (n == 1)
            {
                result.Add(pts[0]);
                return result;
            }

            spacing = Mathf.Max(0.01f, spacing);

            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = pts[Mathf.Max(i - 1, 0)];
                Vector2 p1 = pts[i];
                Vector2 p2 = pts[i + 1];
                Vector2 p3 = pts[Mathf.Min(i + 2, n - 1)];

                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / spacing));
                for (int s = 0; s < steps; s++)
                    result.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
            }

            result.Add(pts[n - 1]);
            return result;
        }

        /// <summary>Разрезает ломаную в точке на отрезке segIndex на две части. Точка входит в обе части.</summary>
        public static void SplitAt(IReadOnlyList<Vector2> pts, int segIndex, Vector2 point,
            out List<Vector2> first, out List<Vector2> second)
        {
            first = new List<Vector2>();
            for (int i = 0; i <= segIndex; i++) first.Add(pts[i]);
            first.Add(point);

            second = new List<Vector2> { point };
            for (int i = segIndex + 1; i < pts.Count; i++) second.Add(pts[i]);
        }

        /// <summary>Убирает подряд идущие точки, расстояние между которыми меньше eps.</summary>
        public static List<Vector2> Dedup(IReadOnlyList<Vector2> pts, float eps)
        {
            var result = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                if (result.Count == 0 || Vector2.Distance(result[result.Count - 1], pts[i]) > eps)
                    result.Add(pts[i]);
            }

            return result;
        }
    }
}
