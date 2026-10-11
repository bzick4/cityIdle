using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Roads
{
    public sealed class RoadNode
    {
        public readonly int Id;
        public readonly RoadLayer Layer;
        public Vector2 Position;
        public readonly List<int> EdgeIds = new List<int>();

        /// <summary>Задержка на развилке в секундах при свободной дороге. 0: развилки нет, задержки нет.</summary>
        public float JunctionDelay;

        /// <summary>Пропускная способность развилки: сколько поездок через неё до перегруза.</summary>
        public float JunctionCapacity;

        public RoadNode(int id, Vector2 position, RoadLayer layer)
        {
            Id = id;
            Position = position;
            Layer = layer;
        }
    }

    public sealed class RoadEdge
    {
        public readonly int Id;
        public readonly int NodeA;
        public readonly int NodeB;
        public readonly RoadType Type;
        public readonly RoadLayer Layer;

        /// <summary>Сглаженная ломаная от узла A до узла B.</summary>
        public readonly List<Vector2> Points;

        public readonly float Length;
        public readonly Vector2 BoundsMin;
        public readonly Vector2 BoundsMax;

        /// <summary>0: в обе стороны, 1: только от узла A к узлу B, -1: только от B к A.</summary>
        public int OneWay;

        /// <summary>Во сколько раз быстрее и пропускает больше обычной дороги. Для шоссе больше 1.</summary>
        public float SpeedFactor = 1f;
        public float CapacityFactor = 1f;

        public RoadEdge(int id, int nodeA, int nodeB, List<Vector2> points, RoadType type)
        {
            Id = id;
            NodeA = nodeA;
            NodeB = nodeB;
            Points = points;
            Type = type;
            Layer = RoadSpecs.LayerFor(type);
            Length = PolylineUtil.Length(points);

            Vector2 min = points[0];
            Vector2 max = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }

            BoundsMin = min;
            BoundsMax = max;
        }
    }

    /// <summary>
    /// Граф дорог: узлы и рёбра. Всё остальное (отрисовка, расчёт потоков, развилки)
    /// подписывается на события графа и не меняет его напрямую.
    /// </summary>
    public sealed class RoadGraph
    {
        readonly Dictionary<int, RoadNode> _nodes = new Dictionary<int, RoadNode>();
        readonly Dictionary<int, RoadEdge> _edges = new Dictionary<int, RoadEdge>();
        int _nextNodeId = 1;
        int _nextEdgeId = 1;

        public event Action<RoadNode> NodeAdded;
        public event Action<RoadNode> NodeRemoved;
        public event Action<RoadEdge> EdgeAdded;
        public event Action<RoadEdge> EdgeRemoved;

        /// <summary>Вызывается после любого изменения сети. Сюда подключится расчёт потоков.</summary>
        public event Action Changed;

        public IEnumerable<RoadNode> Nodes => _nodes.Values;
        public IEnumerable<RoadEdge> Edges => _edges.Values;
        public int NodeCount => _nodes.Count;
        public int EdgeCount => _edges.Count;

        public RoadNode GetNode(int id)
        {
            return _nodes.TryGetValue(id, out RoadNode n) ? n : null;
        }

        public RoadEdge GetEdge(int id)
        {
            return _edges.TryGetValue(id, out RoadEdge e) ? e : null;
        }

        public RoadNode AddNode(Vector2 position, RoadLayer layer)
        {
            var node = new RoadNode(_nextNodeId++, position, layer);
            _nodes.Add(node.Id, node);
            NodeAdded?.Invoke(node);
            return node;
        }

        /// <summary>Точки должны начинаться в позиции узла a и заканчиваться в позиции узла b.</summary>
        public RoadEdge AddEdge(RoadNode a, RoadNode b, List<Vector2> points, RoadType type)
        {
            if (a == null || b == null || a.Id == b.Id || points == null || points.Count < 2)
                return null;

            var edge = new RoadEdge(_nextEdgeId++, a.Id, b.Id, points, type);
            _edges.Add(edge.Id, edge);
            a.EdgeIds.Add(edge.Id);
            b.EdgeIds.Add(edge.Id);

            EdgeAdded?.Invoke(edge);
            Changed?.Invoke();
            return edge;
        }

        /// <summary>Удаляет ребро и узлы, которые остались без рёбер.</summary>
        public void RemoveEdge(int edgeId)
        {
            if (!_edges.TryGetValue(edgeId, out RoadEdge edge)) return;

            _edges.Remove(edgeId);
            RoadNode a = GetNode(edge.NodeA);
            RoadNode b = GetNode(edge.NodeB);
            a?.EdgeIds.Remove(edgeId);
            b?.EdgeIds.Remove(edgeId);

            EdgeRemoved?.Invoke(edge);

            if (a != null && a.EdgeIds.Count == 0) RemoveNode(a);
            if (b != null && b.EdgeIds.Count == 0) RemoveNode(b);

            Changed?.Invoke();
        }

        void RemoveNode(RoadNode node)
        {
            _nodes.Remove(node.Id);
            NodeRemoved?.Invoke(node);
        }

        public RoadNode FindNodeNear(Vector2 position, float radius, RoadLayer layer)
        {
            RoadNode best = null;
            float bestDist = radius;
            foreach (RoadNode n in _nodes.Values)
            {
                if (n.Layer != layer) continue;
                float d = Vector2.Distance(n.Position, position);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = n;
                }
            }

            return best;
        }

        /// <summary>Ищет ближайшее ребро того же слоя в пределах радиуса.</summary>
        public bool FindEdgeNear(Vector2 position, float radius, RoadLayer layer,
            out RoadEdge edge, out int segmentIndex, out Vector2 closestPoint)
        {
            edge = null;
            segmentIndex = 0;
            closestPoint = position;
            float bestDist = radius;

            foreach (RoadEdge e in _edges.Values)
            {
                if (e.Layer != layer) continue;

                // Быстрая проверка по габариту ребра.
                if (position.x < e.BoundsMin.x - radius || position.x > e.BoundsMax.x + radius ||
                    position.y < e.BoundsMin.y - radius || position.y > e.BoundsMax.y + radius)
                    continue;

                Vector2 c = PolylineUtil.ClosestPoint(e.Points, position, out int seg, out float d);
                if (d <= bestDist)
                {
                    bestDist = d;
                    edge = e;
                    segmentIndex = seg;
                    closestPoint = c;
                }
            }

            return edge != null;
        }

        /// <summary>Режет ребро в точке на отрезке segmentIndex, создаёт там узел и возвращает его.</summary>
        public RoadNode SplitEdge(RoadEdge edge, int segmentIndex, Vector2 point)
        {
            RoadNode a = GetNode(edge.NodeA);
            RoadNode b = GetNode(edge.NodeB);
            if (a == null || b == null) return null;

            PolylineUtil.SplitAt(edge.Points, segmentIndex, point,
                out List<Vector2> firstPart, out List<Vector2> secondPart);

            RoadNode middle = AddNode(point, edge.Layer);
            RoadType type = edge.Type;

            // Сначала добавляем новые рёбра, чтобы узлы a и b не удалились как пустые.
            AddEdge(a, middle, firstPart, type);
            AddEdge(middle, b, secondPart, type);
            RemoveEdge(edge.Id);
            return middle;
        }
    }
}
