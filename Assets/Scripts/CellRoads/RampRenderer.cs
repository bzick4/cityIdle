using System.Collections.Generic;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Рисует диагональные съезды из RampMap: одна лента на коннектор.
    /// </summary>
    public sealed class RampRenderer : MonoBehaviour
    {
        public float CellSize = 1f;

        RampMap _map;
        readonly List<GameObject> _views = new List<GameObject>();
        Material _roadMat;
        Material _markMat;

        const float HalfWidth = 0.44f;
        const float EdgeLine = 0.025f;
        const int SortRoad = 3;
        const int SortMark = 4;

        public void Bind(RampMap map)
        {
            if (_map != null) _map.Changed -= Rebuild;
            _map = map;
            if (_map != null) _map.Changed += Rebuild;
            Rebuild();
        }

        void Awake()
        {
            _roadMat = MakeMat(new Color32(0x3B, 0x45, 0x52, 0xFF));
            _markMat = MakeMat(new Color32(0xF7, 0xF7, 0xF4, 0xFF));
        }

        void OnDestroy()
        {
            if (_map != null) _map.Changed -= Rebuild;
        }

        static Material MakeMat(Color c)
        {
            var m = new Material(Shader.Find("Sprites/Default"));
            m.color = c;
            return m;
        }

        void Rebuild()
        {
            foreach (GameObject go in _views)
                if (go != null) Destroy(go);
            _views.Clear();

            if (_map == null) return;

            foreach (RampConnector r in _map.All)
                BuildConnector(r);
        }

        void BuildConnector(RampConnector r)
        {
            float cs = CellSize;
            Vector2 p0 = RampMap.EdgePoint(r.FromCell, r.ToCell, cs);
            Vector2 p1 = RampMap.EdgePoint(r.ToCell, r.FromCell, cs);
            Vector2 along = (p1 - p0);
            float len = along.magnitude;
            if (len < 0.01f) return;
            along /= len;
            Vector2 perp = new Vector2(-along.y, along.x);
            float hw = cs * HalfWidth;

            // Тело дороги
            Mesh road = BuildQuad(p0, p1, along, perp, hw);
            _views.Add(CreateView("Ramp_body", road, _roadMat, SortRoad));

            // Боковые полосы разметки
            float ew = cs * EdgeLine;
            Mesh edgeL = BuildEdgeLine(p0, p1, along, perp, hw - ew * 1.5f, hw + ew * 0.5f);
            Mesh edgeR = BuildEdgeLine(p0, p1, along, perp, -(hw + ew * 0.5f), -(hw - ew * 1.5f));
            _views.Add(CreateView("Ramp_edgeL", edgeL, _markMat, SortMark));
            _views.Add(CreateView("Ramp_edgeR", edgeR, _markMat, SortMark));

            // Стрелка направления в центре
            Mesh arrow = BuildArrow(p0, p1, along, perp, cs);
            _views.Add(CreateView("Ramp_arrow", arrow, _markMat, SortMark));
        }

        // Прямоугольник по оси along, ширина 2*hw
        static Mesh BuildQuad(Vector2 p0, Vector2 p1, Vector2 along, Vector2 perp, float hw)
        {
            var v = new Vector3[]
            {
                p0 - perp * hw, p0 + perp * hw,
                p1 + perp * hw, p1 - perp * hw
            };
            return MakeMesh(v, new int[] { 0, 1, 2, 0, 2, 3 });
        }

        static Mesh BuildEdgeLine(Vector2 p0, Vector2 p1, Vector2 along, Vector2 perp, float inner, float outer)
        {
            var v = new Vector3[]
            {
                p0 + perp * inner, p0 + perp * outer,
                p1 + perp * outer, p1 + perp * inner
            };
            return MakeMesh(v, new int[] { 0, 1, 2, 0, 2, 3 });
        }

        static Mesh BuildArrow(Vector2 p0, Vector2 p1, Vector2 along, Vector2 perp, float cs)
        {
            Vector2 mid = (p0 + p1) * 0.5f;
            float tip  = cs * 0.12f;
            float base_ = cs * 0.09f;
            float side  = cs * 0.10f;
            var v = new Vector3[]
            {
                mid + along * tip,
                mid - along * base_ + perp * side,
                mid - along * base_ - perp * side
            };
            return MakeMesh(v, new int[] { 0, 1, 2 });
        }

        static Mesh MakeMesh(Vector3[] verts, int[] tris)
        {
            var m = new Mesh { hideFlags = HideFlags.DontSave };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        GameObject CreateView(string name, Mesh mesh, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = order;
            return go;
        }
    }
}
