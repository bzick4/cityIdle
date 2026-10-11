using System;
using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>
    /// Осевые координаты шестиугольника (pointy-top: вершина сверху).
    /// Направления соседей по кругу: 0 = восток, 1 = северо-восток, 2 = северо-запад,
    /// 3 = запад, 4 = юго-запад, 5 = юго-восток.
    /// </summary>
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        public readonly int Q;
        public readonly int R;

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        static readonly int[] DirQ = { 1, 1, 0, -1, -1, 0 };
        static readonly int[] DirR = { 0, -1, -1, 0, 1, 1 };

        public HexCoord Neighbor(int dir)
        {
            int d = ((dir % 6) + 6) % 6;
            return new HexCoord(Q + DirQ[d], R + DirR[d]);
        }

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => unchecked(Q * 397) ^ R;
        public override string ToString() => "(" + Q + "," + R + ")";

        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
    }

    /// <summary>
    /// Математика шестиугольной сетки. size = расстояние от центра до угла в мировых единицах.
    /// Ось Y направлена вверх, значение R растёт вниз по экрану.
    /// </summary>
    public static class HexMath
    {
        const float Sqrt3 = 1.7320508f;

        public static Vector2 ToWorld(HexCoord h, float size)
        {
            return new Vector2(size * Sqrt3 * (h.Q + h.R * 0.5f), -size * 1.5f * h.R);
        }

        public static HexCoord FromWorld(Vector2 p, float size)
        {
            float yDown = -p.y;
            float q = (Sqrt3 / 3f * p.x - yDown / 3f) / size;
            float r = (2f / 3f * yDown) / size;
            return Round(q, r);
        }

        static HexCoord Round(float q, float r)
        {
            float s = -q - r;
            int rq = Mathf.RoundToInt(q);
            int rr = Mathf.RoundToInt(r);
            int rs = Mathf.RoundToInt(s);

            float dq = Mathf.Abs(rq - q);
            float dr = Mathf.Abs(rr - r);
            float ds = Mathf.Abs(rs - s);

            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;

            return new HexCoord(rq, rr);
        }

        /// <summary>Угол шестиугольника с номером 0..5.</summary>
        public static Vector2 Corner(HexCoord h, int i, float size)
        {
            float a = Mathf.Deg2Rad * (60f * i + 30f);
            Vector2 c = ToWorld(h, size);
            return new Vector2(c.x + size * Mathf.Cos(a), c.y + size * Mathf.Sin(a));
        }

        /// <summary>Середина грани в направлении dir: здесь будут порты для стыковки дорог.</summary>
        public static Vector2 EdgeMidpoint(HexCoord h, int dir, float size)
        {
            Vector2 a = ToWorld(h, size);
            Vector2 b = ToWorld(h.Neighbor(dir), size);
            return (a + b) * 0.5f;
        }
    }
}
