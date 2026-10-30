using System.Collections.Generic;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Рисует диагональные съезды из RampMap: одна лента на коннектор.
    /// </summary>
    public sealed class RampRenderer : MonoBehaviour
    {
        public float CellSize { get; set; } = 1f;

        RampMap _map;
        readonly List<GameObject> _views = new List<GameObject>();
        Material _roadMat;
        Material _markMat;

        const float HalfWidth = 0.25f;
        // Road body = 0, road marking = 2. Рамп ниже дорог: в зонах перекрытия дорога рисуется поверх.
        const int SortBody = -1;
        const int SortArrow = 1;

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
            Vector2 p0 = RampMap.CellCenter(r.FromCell, cs);
            Vector2 p1 = RampMap.CellCenter(r.ToCell, cs);
            Vector2 along = (p1 - p0);
            float len = along.magnitude;
            if (len < 0.01f) return;
            along /= len;
            Vector2 perp = new Vector2(-along.y, along.x);
            float hw = cs * HalfWidth;

            // Тело: ниже дорог, скрывается под ними в зоне перекрытия
            Mesh road = BuildQuad(p0, p1, along, perp, hw);
            _views.Add(CreateView("Ramp_body", road, _roadMat, SortBody));

            // Стрелка: между телом и разметкой дорог (1 < road marking 2), видна в зазоре
            Mesh arrow = BuildArrow(p0, p1, along, perp, cs);
            _views.Add(CreateView("Ramp_arrow", arrow, _markMat, SortArrow));
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
