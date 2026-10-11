using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>Строит плоские меши дороги в плоскости XY. Порядок обхода треугольников по часовой стрелке (взгляд из камеры 2D).</summary>
    public static class RoadMeshBuilder
    {
        /// <summary>Лента вдоль ломаной. UV: u поперёк дороги (0..1), v вдоль неё в единицах ширины.</summary>
        public static Mesh BuildRibbon(IReadOnlyList<Vector2> points, float width)
        {
            var mesh = new Mesh { name = "RoadRibbon" };
            List<Vector2> pts = PolylineUtil.Dedup(points, 0.001f);
            int n = pts.Count;
            if (n < 2) return mesh;

            float halfWidth = width * 0.5f;
            var vertices = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var triangles = new int[(n - 1) * 6];

            float distance = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 tangent;
                if (i == 0) tangent = pts[1] - pts[0];
                else if (i == n - 1) tangent = pts[n - 1] - pts[n - 2];
                else tangent = pts[i + 1] - pts[i - 1];
                tangent.Normalize();

                var normal = new Vector2(-tangent.y, tangent.x);
                if (i > 0) distance += Vector2.Distance(pts[i - 1], pts[i]);

                Vector2 left = pts[i] + normal * halfWidth;
                Vector2 right = pts[i] - normal * halfWidth;
                vertices[i * 2] = new Vector3(left.x, left.y, 0f);
                vertices[i * 2 + 1] = new Vector3(right.x, right.y, 0f);
                uvs[i * 2] = new Vector2(0f, distance / width);
                uvs[i * 2 + 1] = new Vector2(1f, distance / width);
            }

            int t = 0;
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2;       // левая точка в начале сегмента
                int b = i * 2 + 1;   // правая точка в начале сегмента
                int c = i * 2 + 2;   // левая точка в конце сегмента
                int d = i * 2 + 3;   // правая точка в конце сегмента

                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = b;

                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = d;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Круг в узле: скругляет концы дорог и стыки.</summary>
        public static Mesh BuildDisc(Vector2 center, float radius, int segments = 16)
        {
            var mesh = new Mesh { name = "RoadNodeDisc" };
            segments = Mathf.Max(6, segments);

            var vertices = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = new Vector3(center.x, center.y, 0f);

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius, 0f);
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = next + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
