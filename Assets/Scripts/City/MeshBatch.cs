using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Набор плоских фигур с цветом в вершинах, один меш. Порядок добавления это порядок отрисовки внутри меша.
    /// Материал Sprites/Default с белым цветом берёт цвет и прозрачность из вершин.
    /// </summary>
    public sealed class MeshBatch
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>();

        public void Clear()
        {
            V.Clear();
            C.Clear();
            T.Clear();
        }

        public int Add(Vector2 p, Color c)
        {
            V.Add(new Vector3(p.x, p.y, 0f));
            C.Add(c);
            return V.Count - 1;
        }

        public void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int i = Add(a, col);
            Add(b, col);
            Add(c, col);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        /// <summary>Четырёхугольник по обходу a, b, c, d, у каждой вершины свой цвет.</summary>
        public void QuadGrad(Vector2 a, Color ca, Vector2 b, Color cb, Vector2 c, Color cc, Vector2 d, Color cd)
        {
            int i = Add(a, ca);
            Add(b, cb);
            Add(c, cc);
            Add(d, cd);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
            T.Add(i); T.Add(i + 2); T.Add(i + 3);
        }

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
        {
            QuadGrad(a, col, b, col, c, col, d, col);
        }

        public void Rect(Vector2 min, Vector2 max, Color col)
        {
            Quad(new Vector2(min.x, min.y), new Vector2(min.x, max.y), new Vector2(max.x, max.y),
                new Vector2(max.x, min.y), col);
        }

        /// <summary>Прямоугольник в системе координат (центр, ось u, ось v): a0..a1 вдоль u, b0..b1 вдоль v.</summary>
        public void Local(Vector2 c, Vector2 u, Vector2 v, float a0, float a1, float b0, float b1, Color col)
        {
            Quad(c + u * a0 + v * b0, c + u * a0 + v * b1, c + u * a1 + v * b1, c + u * a1 + v * b0, col);
        }

        public void Disc(Vector2 center, float radius, int segments, Color col)
        {
            int c0 = Add(center, col);
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, col);
            }

            for (int i = 0; i < segments; i++)
            {
                T.Add(c0); T.Add(c0 + 1 + i); T.Add(c0 + 2 + i);
            }
        }

        /// <summary>Кольцо между радиусами r0 и r1.</summary>
        public void Ring(Vector2 center, float r0, float r1, int segments, Color col)
        {
            int first = V.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Add(center + d * r0, col);
                Add(center + d * r1, col);
            }

            for (int i = 0; i < segments; i++)
            {
                int k = first + i * 2;
                T.Add(k); T.Add(k + 1); T.Add(k + 3);
                T.Add(k); T.Add(k + 3); T.Add(k + 2);
            }
        }

        /// <summary>Выпуклый многоугольник веером от первой вершины.</summary>
        public void Polygon(IList<Vector2> pts, Color col)
        {
            if (pts.Count < 3) return;
            int i0 = Add(pts[0], col);
            for (int i = 1; i < pts.Count; i++) Add(pts[i], col);
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                T.Add(i0); T.Add(i0 + i); T.Add(i0 + i + 1);
            }
        }

        public Mesh ToMesh(Mesh reuse = null)
        {
            Mesh m = reuse != null ? reuse : new Mesh { name = "Batch" };
            m.Clear();
            m.SetVertices(V);
            m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
