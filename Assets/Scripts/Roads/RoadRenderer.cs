using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>
    /// Рисует граф дорог: один меш на ребро и один круг на узел.
    /// Слушает события графа и сам ничего в нём не меняет.
    /// </summary>
    public sealed class RoadRenderer : MonoBehaviour
    {
        [Header("Материалы (если пусто, создаются автоматически)")]
        [SerializeField] Material localMaterial;
        [SerializeField] Material highwayMaterial;

        RoadGraph _graph;
        // Список слоёв на ребро: [0] тело, [1..] разметка.
        readonly Dictionary<int, List<GameObject>> _edgeViews = new Dictionary<int, List<GameObject>>();
        readonly Dictionary<int, GameObject> _nodeViews = new Dictionary<int, GameObject>();

        Material _markingMaterial;

        void Awake()
        {
            if (localMaterial   == null) localMaterial   = MakeMaterial(RoadSpecs.BodyColor(RoadType.Local));
            if (highwayMaterial == null) highwayMaterial = MakeMaterial(RoadSpecs.BodyColor(RoadType.Highway));
            _markingMaterial = MakeMaterial(new Color(1f, 1f, 1f, 0.82f));
        }

        static Material MakeMaterial(Color color)
        {
            // Sprites/Default работает в 2D-проектах (Built-in и URP) и рисует с учётом sortingOrder.
            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = color;
            return material;
        }

        public void Bind(RoadGraph graph)
        {
            Unbind();
            _graph = graph;
            if (_graph == null) return;

            _graph.EdgeAdded += OnEdgeAdded;
            _graph.EdgeRemoved += OnEdgeRemoved;
            _graph.NodeRemoved += OnNodeRemoved;

            foreach (RoadEdge edge in _graph.Edges) OnEdgeAdded(edge);
        }

        void Unbind()
        {
            if (_graph == null) return;
            _graph.EdgeAdded -= OnEdgeAdded;
            _graph.EdgeRemoved -= OnEdgeRemoved;
            _graph.NodeRemoved -= OnNodeRemoved;
            _graph = null;
        }

        void OnDestroy()
        {
            Unbind();
        }

        Material MaterialFor(RoadType type)
        {
            return type == RoadType.Highway ? highwayMaterial : localMaterial;
        }

        GameObject CreateView(string viewName, Mesh mesh, Material material, int sortingOrder)
        {
            var go = new GameObject(viewName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = sortingOrder;
            return go;
        }

        static void DestroyView(GameObject go)
        {
            if (go == null) return;
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            Destroy(go);
        }

        void OnEdgeAdded(RoadEdge edge)
        {
            int order = RoadSpecs.SortingOrder(edge.Layer);
            var views = new List<GameObject>();

            // Тело дороги
            views.Add(CreateView("Edge" + edge.Id + "_body",
                RoadMeshBuilder.BuildRibbon(edge.Points, RoadSpecs.Width(edge.Type)),
                MaterialFor(edge.Type), order));

            // Разметка (sortingOrder тела + 1)
            if (edge.Type == RoadType.Local)
            {
                // Пунктирная осевая линия
                views.Add(CreateView("Edge" + edge.Id + "_dash",
                    RoadMeshBuilder.BuildCenterDashes(edge.Points),
                    _markingMaterial, order + 1));
            }
            else // Highway
            {
                float hw = RoadSpecs.Width(RoadType.Highway) * 0.5f; // 0.45
                float lineW = 0.028f;
                float edgeOff = hw - lineW * 0.7f;
                // Две боковые линии
                views.Add(CreateView("Edge" + edge.Id + "_eL",
                    RoadMeshBuilder.BuildOffsetRibbon(edge.Points,  edgeOff, lineW),
                    _markingMaterial, order + 1));
                views.Add(CreateView("Edge" + edge.Id + "_eR",
                    RoadMeshBuilder.BuildOffsetRibbon(edge.Points, -edgeOff, lineW),
                    _markingMaterial, order + 1));
                // Сплошная осевая
                views.Add(CreateView("Edge" + edge.Id + "_ctr",
                    RoadMeshBuilder.BuildOffsetRibbon(edge.Points, 0f, lineW),
                    _markingMaterial, order + 1));
            }

            _edgeViews[edge.Id] = views;
            RefreshNode(edge.NodeA);
            RefreshNode(edge.NodeB);
        }

        void OnEdgeRemoved(RoadEdge edge)
        {
            if (_edgeViews.TryGetValue(edge.Id, out List<GameObject> views))
            {
                foreach (GameObject v in views) DestroyView(v);
                _edgeViews.Remove(edge.Id);
            }

            RefreshNode(edge.NodeA);
            RefreshNode(edge.NodeB);
        }

        void OnNodeRemoved(RoadNode node)
        {
            if (_nodeViews.TryGetValue(node.Id, out GameObject view))
            {
                DestroyView(view);
                _nodeViews.Remove(node.Id);
            }
        }

        /// <summary>Пересоздаёт круг узла по самой широкой из подходящих дорог.</summary>
        void RefreshNode(int nodeId)
        {
            RoadNode node = _graph?.GetNode(nodeId);
            if (node == null) return;

            if (_nodeViews.TryGetValue(nodeId, out GameObject old))
            {
                DestroyView(old);
                _nodeViews.Remove(nodeId);
            }

            float widest = 0f;
            RoadType widestType = RoadType.Local;
            foreach (int edgeId in node.EdgeIds)
            {
                RoadEdge edge = _graph.GetEdge(edgeId);
                if (edge == null) continue;
                float w = RoadSpecs.Width(edge.Type);
                if (w > widest)
                {
                    widest = w;
                    widestType = edge.Type;
                }
            }

            if (widest <= 0f) return;

            Mesh disc = RoadMeshBuilder.BuildDisc(node.Position, widest * 0.5f);
            _nodeViews[nodeId] = CreateView("Node " + nodeId, disc, MaterialFor(widestType),
                RoadSpecs.SortingOrder(node.Layer) + 1);
        }
    }
}
