using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>
    /// Превращает нарисованную линию в ребро графа. Концы дороги притягиваются
    /// к ближайшему узлу, а если рядом проходит дорога, она режется и на ней появляется узел.
    /// Пересечения посередине линии пока не обрабатываются (это работа шага с развилками).
    /// </summary>
    public static class RoadBuilder
    {
        struct Endpoint
        {
            public int NodeId;     // >= 0: существующий узел
            public RoadEdge Edge;  // не null: точка лежит на ребре, его нужно разрезать
            public int Segment;
            public Vector2 Position;
        }

        static Endpoint Plan(RoadGraph graph, Vector2 point, RoadLayer layer, float snapRadius)
        {
            RoadNode node = graph.FindNodeNear(point, snapRadius, layer);
            if (node != null)
                return new Endpoint { NodeId = node.Id, Position = node.Position };

            if (graph.FindEdgeNear(point, snapRadius, layer, out RoadEdge edge, out int seg, out Vector2 closest))
                return new Endpoint { NodeId = -1, Edge = edge, Segment = seg, Position = closest };

            return new Endpoint { NodeId = -1, Position = point };
        }

        static RoadNode Commit(RoadGraph graph, Endpoint ep, RoadLayer layer)
        {
            if (ep.NodeId >= 0) return graph.GetNode(ep.NodeId);
            if (ep.Edge != null) return graph.SplitEdge(ep.Edge, ep.Segment, ep.Position);
            return graph.AddNode(ep.Position, layer);
        }

        /// <summary>Возвращает созданное ребро или null, если дорога слишком короткая или замкнулась на себя.</summary>
        public static RoadEdge Build(RoadGraph graph, IReadOnlyList<Vector2> points, RoadType type,
            float snapRadius, float minLength)
        {
            if (graph == null || points == null || points.Count < 2) return null;

            RoadLayer layer = RoadSpecs.LayerFor(type);
            Vector2 rawStart = points[0];
            Vector2 rawEnd = points[points.Count - 1];

            // Сначала проверяем, ничего не меняя в графе.
            Endpoint startPlan = Plan(graph, rawStart, layer, snapRadius);
            Endpoint endPlan = Plan(graph, rawEnd, layer, snapRadius);

            if (Vector2.Distance(startPlan.Position, endPlan.Position) < 0.01f) return null;
            if (startPlan.NodeId >= 0 && startPlan.NodeId == endPlan.NodeId) return null;

            var pts = new List<Vector2>(points);
            pts[0] = startPlan.Position;
            pts[pts.Count - 1] = endPlan.Position;
            pts = PolylineUtil.Dedup(pts, 0.001f);
            if (pts.Count < 2 || PolylineUtil.Length(pts) < minLength) return null;

            // Теперь меняем граф. Конец ищем заново: начало могло разрезать то же ребро.
            RoadNode startNode = Commit(graph, startPlan, layer);
            RoadNode endNode = Commit(graph, Plan(graph, rawEnd, layer, snapRadius), layer);
            if (startNode == null || endNode == null || startNode.Id == endNode.Id) return null;

            pts[0] = startNode.Position;
            pts[pts.Count - 1] = endNode.Position;
            return graph.AddEdge(startNode, endNode, pts, type);
        }
    }
}
