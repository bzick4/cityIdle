using System.Collections.Generic;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.Flow
{
    /// <summary>
    /// Тепловая карта нагрузки: цветное свечение под дорогой, зелёное при свободной дороге,
    /// жёлтое около предела и красное при перегрузе. Включается кнопкой «Нагрузка».
    /// </summary>
    public sealed class FlowHeatmap : MonoBehaviour
    {
        [SerializeField] float _haloScale = 2.4f;
        [Tooltip("Радиус пятна на развилке")]
        [SerializeField] float _junctionRadius = 0.7f;
        [SerializeField] Color _low = new Color32(0x6B, 0xCB, 0x77, 0xB0);
        [SerializeField] Color _mid = new Color32(0xFF, 0xD9, 0x3D, 0xC0);
        [SerializeField] Color _high = new Color32(0xFF, 0x4D, 0x4D, 0xD0);

        FlowSolver _solver;
        RoadGraph _graph;
        Material _material;
        readonly List<GameObject> _views = new List<GameObject>();
        bool _visible;

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                Redraw();
            }
        }

        public void Bind(FlowSolver solver, RoadGraph graph)
        {
            if (_solver != null) _solver.Solved -= Redraw;
            _solver = solver;
            _graph = graph;
            _solver.Solved += Redraw;
            _material = new Material(Shader.Find("Sprites/Default"));
        }

        void OnDestroy()
        {
            if (_solver != null) _solver.Solved -= Redraw;
        }

        Color ColorFor(float ratio)
        {
            if (ratio <= 0.8f) return Color.Lerp(_low, _mid, Mathf.Clamp01(ratio / 0.8f));
            return Color.Lerp(_mid, _high, Mathf.Clamp01((ratio - 0.8f) / 0.7f));
        }

        void Clear()
        {
            foreach (GameObject go in _views)
            {
                if (go == null) continue;
                var f = go.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
                Destroy(go);
            }

            _views.Clear();
        }

        void Redraw()
        {
            Clear();
            if (!_visible || _solver == null || _graph == null) return;

            foreach (var kv in _solver.EdgeRatio)
            {
                RoadEdge edge = _graph.GetEdge(kv.Key);
                if (edge == null) continue;

                Mesh mesh = RoadMeshBuilder.BuildRibbon(edge.Points, RoadSpecs.Width(edge.Type) * _haloScale);
                Color c = ColorFor(kv.Value);
                var colors = new Color[mesh.vertexCount];
                for (int i = 0; i < colors.Length; i++) colors[i] = c;
                mesh.colors = colors;

                var go = new GameObject("Heat " + edge.Id);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.sortingOrder = RoadSpecs.SortingOrder(edge.Layer) - 1;
                _views.Add(go);
            }

            // Развилки: пятно под перекрёстком окрашено по его загрузке.
            foreach (var kv in _solver.NodeRatio)
            {
                RoadNode node = _graph.GetNode(kv.Key);
                if (node == null) continue;

                Mesh disc = RoadMeshBuilder.BuildDisc(node.Position, _junctionRadius, 20);
                Color c = ColorFor(kv.Value);
                var colors = new Color[disc.vertexCount];
                for (int i = 0; i < colors.Length; i++) colors[i] = c;
                disc.colors = colors;

                var go = new GameObject("Heat node " + node.Id);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = disc;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.sortingOrder = RoadSpecs.SortingOrder(RoadLayer.Ground) - 1;
                _views.Add(go);
            }
        }
    }
}
