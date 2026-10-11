using System;
using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.Flow
{
    /// <summary>
    /// Расчёт потоков по графу дорог.
    /// 1. Каждый район подключается к ближайшей земной дороге в радиусе AccessRadius.
    /// 2. Жилые районы создают поездки в торговые (магазины) и промышленные (работа), по гравитационной модели.
    /// 3. Поездки раскладываются по кратчайшим путям (Dijkstra), время ребра растёт по BPR: t = t0 (1 + 0.15 (v/c)^4).
    ///    Несколько итераций со средним по MSA, чтобы потоки стабилизировались.
    /// 4. Из итогового времени в пути считается довольство каждого района, оно влияет на налог.
    /// Пересчёт идёт при изменении дорог или зданий, не чаще раза в кадр.
    /// Все числа временные.
    /// </summary>
    public sealed class FlowSolver : MonoBehaviour
    {
        [Header("Дороги (единицы мира в секунду, поездок на ребро)")]
        public float LocalSpeed = 2f;
        public float HighwaySpeed = 5f;
        public float LocalCapacity = 15f;
        public float HighwayCapacity = 60f;

        [Header("Спрос")]
        public float TripsPerCell = 3f;
        [Range(0f, 1f)] public float ShopShare = 0.5f;
        [Tooltip("Чем меньше, тем сильнее жители предпочитают ближние цели, секунд")]
        public float GravityTau = 15f;

        [Header("Подключение района к дороге")]
        public float AccessRadius = 1.2f;

        [Header("Довольство: время в пути, секунд")]
        public float GoodTime = 6f;
        public float BadTime = 40f;

        [Header("Расчёт")]
        public int Iterations = 4;

        /// <summary>Загрузка v/c по id ребра. Только рёбра с ненулевым потоком.</summary>
        public readonly Dictionary<int, float> EdgeRatio = new Dictionary<int, float>();

        /// <summary>Загрузка развилок (поток / пропускная способность) по id узла. Только развилки с потоком.</summary>
        public readonly Dictionary<int, float> NodeRatio = new Dictionary<int, float>();

        /// <summary>Один участок маршрута: ребро и отрезок вдоль его ломаной (расстояния от узла A).</summary>
        public struct RouteStep
        {
            public RoadEdge Edge;
            public float From;
            public float To;
            /// <summary>Узел в конце участка, -1 если участок кончается у района.</summary>
            public int EndNode;
        }

        /// <summary>Маршрут поездок между двумя районами с их потоком. Для машин на карте.</summary>
        public sealed class Route
        {
            public float Flow;
            public int SrcDistrictId;
            public int DstDistrictId;
            /// <summary>Любая клетка района отправления и назначения: по ней район находят заново после перестройки.</summary>
            public Vector2Int SrcCell;
            public Vector2Int DstCell;
            public readonly List<RouteStep> Steps = new List<RouteStep>();
        }

        /// <summary>Маршруты туда и обратно по итоговым временам. Пересобираются при каждом расчёте.</summary>
        public readonly List<Route> Routes = new List<Route>();

        public float EdgeSpeed(RoadEdge e)
        {
            return (e.Type == RoadType.Highway ? HighwaySpeed : LocalSpeed) * e.SpeedFactor;
        }

        public float EdgeCapacity(RoadEdge e)
        {
            return (e.Type == RoadType.Highway ? HighwayCapacity : LocalCapacity) * e.CapacityFactor;
        }

        public int ConnectedDistricts { get; private set; }
        public int UnconnectedDistricts { get; private set; }

        public event Action Solved;

        /// <summary>Сеть или здания изменились, расчёт ещё не обновлён.</summary>
        public bool Dirty => _dirty;

        RoadGraph _graph;
        CityModel _model;
        bool _dirty = true;

        struct Link
        {
            public int To;
            public int Edge;       // индекс ребра для загрузки, -1 если нет
            public bool Dynamic;   // время берётся из загрузки ребра
            public float FixedTime;
        }

        /// <summary>Запомненные данные последнего расчёта: по ним ищется путь от любой точки дороги.</summary>
        sealed class RoutingState
        {
            public List<RoadEdge> Edges;
            public Dictionary<int, int> EdgeIndexById;
            public Dictionary<int, int> NodeIndex;
            public int[] NodeIdOfIndex;
            public List<Access> Accesses;
            public int RoadNodes;
            public List<Link>[] Links;
            public float[] EdgeT;
            public float[] NodeT;
            public Dictionary<int, int> DistrictNode;
        }

        RoutingState _rs;

        sealed class Access
        {
            public int DistrictIndex;
            public RoadEdge Edge;
            public float Along;
            public float Offset;
        }

        public void Bind(RoadGraph graph, CityModel model)
        {
            Unbind();
            _graph = graph;
            _model = model;
            _graph.Changed += MarkDirty;
            _model.Rebuilt += MarkDirty;
            _dirty = true;
        }

        void Unbind()
        {
            if (_graph != null) _graph.Changed -= MarkDirty;
            if (_model != null) _model.Rebuilt -= MarkDirty;
        }

        void OnDestroy()
        {
            Unbind();
        }

        void MarkDirty()
        {
            _dirty = true;
        }

        void Update()
        {
            if (!_dirty || _graph == null || _model == null) return;
            _dirty = false;
            Solve();
        }

        public float TimeScore(float seconds)
        {
            return Mathf.Clamp01(1f - (seconds - GoodTime) / Mathf.Max(0.01f, BadTime - GoodTime));
        }

        // ---------------------------------------------------------------

        public void Solve()
        {
            EdgeRatio.Clear();
            NodeRatio.Clear();

            IReadOnlyList<District> districts = _model.Districts;
            foreach (District d in districts) d.Satisfaction = CategorySpecs.Merges(d.Category) ? 0f : 1f;

            // Рёбра для маршрутизации: обычные дороги, шоссе и односторонние дороги.
            var edges = new List<RoadEdge>();
            var nodeIndex = new Dictionary<int, int>();
            foreach (RoadEdge e in _graph.Edges)
            {
                if (e.Layer == RoadLayer.Highway) continue;
                edges.Add(e);
                if (!nodeIndex.ContainsKey(e.NodeA)) nodeIndex[e.NodeA] = nodeIndex.Count;
                if (!nodeIndex.ContainsKey(e.NodeB)) nodeIndex[e.NodeB] = nodeIndex.Count;
            }

            int roadNodes = nodeIndex.Count;

            // Подключение районов.
            var accesses = new List<Access>();
            var accessOfDistrict = new Dictionary<int, Access>();
            UnconnectedDistricts = 0;
            ConnectedDistricts = 0;
            for (int i = 0; i < districts.Count; i++)
            {
                District d = districts[i];
                if (!CategorySpecs.Merges(d.Category)) continue;

                Access best = FindAccess(i, d);
                if (best == null || best.Edge.Layer == RoadLayer.Highway)
                {
                    UnconnectedDistricts++;
                    continue;
                }

                ConnectedDistricts++;
                accesses.Add(best);
                accessOfDistrict[i] = best;
            }

            int total = roadNodes + accesses.Count;
            var links = new List<Link>[total];
            for (int i = 0; i < total; i++) links[i] = new List<Link>();

            var edgeIndexById = new Dictionary<int, int>();
            var t0 = new float[edges.Count];
            var cap = new float[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                RoadEdge e = edges[i];
                edgeIndexById[e.Id] = i;
                float speed = EdgeSpeed(e);
                t0[i] = Mathf.Max(0.01f, e.Length / speed);
                cap[i] = EdgeCapacity(e);
                int a = nodeIndex[e.NodeA];
                int b = nodeIndex[e.NodeB];
                if (e.OneWay >= 0) links[a].Add(new Link { To = b, Edge = i, Dynamic = true });
                if (e.OneWay <= 0) links[b].Add(new Link { To = a, Edge = i, Dynamic = true });
            }

            var districtNode = new Dictionary<int, int>();
            for (int k = 0; k < accesses.Count; k++)
            {
                Access ac = accesses[k];
                int node = roadNodes + k;
                districtNode[ac.DistrictIndex] = node;
                int ei = edgeIndexById[ac.Edge.Id];
                float speed = EdgeSpeed(ac.Edge);
                float offT = ac.Offset / LocalSpeed;
                float toA = ac.Along / speed + offT;
                float toB = (ac.Edge.Length - ac.Along) / speed + offT;
                int a = nodeIndex[ac.Edge.NodeA];
                int b = nodeIndex[ac.Edge.NodeB];
                int ow = ac.Edge.OneWay;

                // Выезд из района к концу A возможен против хода ребра, к концу B по ходу.
                // Заезд в район: из A по ходу, из B против хода.
                if (ow <= 0) AddOne(links, node, a, ei, toA);
                if (ow >= 0) AddOne(links, node, b, ei, toB);
                if (ow >= 0) AddOne(links, a, node, ei, toA);
                if (ow <= 0) AddOne(links, b, node, ei, toB);

                // Два района на одном ребре могут ехать напрямую друг к другу.
                for (int m = 0; m < k; m++)
                {
                    Access other = accesses[m];
                    if (other.Edge.Id != ac.Edge.Id) continue;
                    float tt = Mathf.Abs(ac.Along - other.Along) / speed + offT + other.Offset / LocalSpeed;
                    // Вперёд по ребру едут от меньшего Along к большему.
                    bool newIsAhead = ac.Along >= other.Along;
                    if (ow == 0 || (ow > 0) == newIsAhead) AddOne(links, roadNodes + m, node, ei, tt);
                    if (ow == 0 || (ow > 0) != newIsAhead) AddOne(links, node, roadNodes + m, ei, tt);
                }
            }

            // Участники.
            var origins = new List<int>();
            var comList = new List<int>();
            var indList = new List<int>();
            foreach (var kv in districtNode)
            {
                District d = districts[kv.Key];
                if (d.Category == BuildingCategory.Residential) origins.Add(kv.Key);
                else if (d.Category == BuildingCategory.Commercial) comList.Add(kv.Key);
                else if (d.Category == BuildingCategory.Industrial) indList.Add(kv.Key);
            }

            // Свободное время ребра.
            var edgeT = new float[edges.Count];
            Array.Copy(t0, edgeT, edgeT.Length);

            // Развилки: задержка растёт с загрузкой так же, как у рёбер.
            var nodeD0 = new float[roadNodes];
            var nodeCap = new float[roadNodes];
            var nodeIdOfIndex = new int[roadNodes];
            foreach (var kv in nodeIndex)
            {
                nodeIdOfIndex[kv.Value] = kv.Key;
                RoadNode rn = _graph.GetNode(kv.Key);
                if (rn == null || rn.JunctionDelay <= 0f) continue;
                nodeD0[kv.Value] = rn.JunctionDelay;
                nodeCap[kv.Value] = Mathf.Max(0.01f, rn.JunctionCapacity);
            }

            var nodeT = new float[roadNodes];
            Array.Copy(nodeD0, nodeT, roadNodes);
            var nv = new float[roadNodes];
            var ny = new float[roadNodes];

            // Спрос по гравитационной модели на свободном времени.
            var od = new List<(int src, int dst, float flow)>();
            var dist = new float[total];
            var predNode = new int[total];
            var predEdge = new int[total];
            foreach (int o in origins)
            {
                Dijkstra(links, districtNode[o], edgeT, nodeT, dist, predNode, predEdge);
                float demand = districts[o].Count * TripsPerCell;
                AddDemand(od, o, comList, demand * ShopShare, districts, districtNode, dist);
                AddDemand(od, o, indList, demand * (1f - ShopShare), districts, districtNode, dist);
            }

            // Назначение потоков: MSA.
            var v = new float[edges.Count];
            var y = new float[edges.Count];
            int iterations = Mathf.Max(1, Iterations);
            for (int k = 1; k <= iterations; k++)
            {
                for (int i = 0; i < edgeT.Length; i++) edgeT[i] = Bpr(t0[i], v[i], cap[i]);
                for (int i = 0; i < roadNodes; i++) nodeT[i] = nodeD0[i] > 0f ? Bpr(nodeD0[i], nv[i], nodeCap[i]) : 0f;
                Array.Clear(y, 0, y.Length);
                Array.Clear(ny, 0, ny.Length);

                foreach (int o in origins)
                {
                    Dijkstra(links, districtNode[o], edgeT, nodeT, dist, predNode, predEdge);
                    foreach (var trip in od)
                    {
                        if (trip.src != o) continue;
                        AddPathFlow(districtNode[trip.dst], districtNode[o], predNode, predEdge, trip.flow, y, ny, roadNodes);
                    }
                }

                for (int i = 0; i < v.Length; i++) v[i] += (y[i] - v[i]) / k;
                for (int i = 0; i < nv.Length; i++) nv[i] += (ny[i] - nv[i]) / k;
            }

            // Итоговые времена.
            for (int i = 0; i < edgeT.Length; i++)
            {
                edgeT[i] = Bpr(t0[i], v[i], cap[i]);
                if (v[i] > 1e-4f) EdgeRatio[edges[i].Id] = v[i] / cap[i];
            }

            for (int i = 0; i < roadNodes; i++)
            {
                nodeT[i] = nodeD0[i] > 0f ? Bpr(nodeD0[i], nv[i], nodeCap[i]) : 0f;
                if (nodeD0[i] > 0f && nv[i] > 1e-4f) NodeRatio[nodeIdOfIndex[i]] = nv[i] / nodeCap[i];
            }

            var inFlow = new Dictionary<int, float>();
            var inTime = new Dictionary<int, float>();
            foreach (int o in origins)
            {
                Dijkstra(links, districtNode[o], edgeT, nodeT, dist, predNode, predEdge);

                float bestCom = BestTime(comList, districtNode, dist);
                float bestInd = BestTime(indList, districtNode, dist);
                float s1 = bestCom < float.MaxValue ? TimeScore(bestCom) : 0f;
                float s2 = bestInd < float.MaxValue ? TimeScore(bestInd) : 0f;
                districts[o].Satisfaction = (s1 + s2) * 0.5f;

                foreach (var trip in od)
                {
                    if (trip.src != o) continue;
                    float t = dist[districtNode[trip.dst]];
                    Add(inFlow, trip.dst, trip.flow);
                    Add(inTime, trip.dst, trip.flow * t);
                }
            }

            foreach (var kv in inFlow)
                if (kv.Value > 1e-4f)
                    districts[kv.Key].Satisfaction = TimeScore(inTime[kv.Key] / kv.Value);

            _rs = new RoutingState
            {
                Edges = edges, EdgeIndexById = edgeIndexById, NodeIndex = nodeIndex, NodeIdOfIndex = nodeIdOfIndex,
                Accesses = accesses, RoadNodes = roadNodes, Links = links, EdgeT = edgeT, NodeT = nodeT,
                DistrictNode = districtNode
            };

            BuildRoutes(districts, edges, nodeIdOfIndex, accesses, roadNodes, links, edgeT, nodeT, districtNode,
                origins, od, dist, predNode, predEdge);

            Solved?.Invoke();
        }

        // ---------------------------------------------------------------

        void BuildRoutes(IReadOnlyList<District> districts, List<RoadEdge> edges, int[] nodeIdOfIndex,
            List<Access> accesses, int roadNodes, List<Link>[] links, float[] edgeT, float[] nodeT,
            Dictionary<int, int> districtNode, List<int> origins, List<(int src, int dst, float flow)> od,
            float[] dist, int[] predNode, int[] predEdge)
        {
            Routes.Clear();

            // Туда: от жилого района к цели.
            foreach (int o in origins)
            {
                Dijkstra(links, districtNode[o], edgeT, nodeT, dist, predNode, predEdge);
                foreach (var trip in od)
                {
                    if (trip.src != o) continue;
                    Route r = MakeRoute(districtNode[o], districtNode[trip.dst], districts[o].Id, districts[trip.dst].Id,
                        trip.flow, edges, nodeIdOfIndex, accesses, roadNodes, predNode, predEdge);
                    if (r != null)
                    {
                        r.SrcCell = districts[o].Cells[0].Pos;
                        r.DstCell = districts[trip.dst].Cells[0].Pos;
                        Routes.Add(r);
                    }
                }
            }

            // Обратно: от цели домой. Односторонние полосы могут дать другой путь или не дать его вовсе.
            var targets = new HashSet<int>();
            foreach (var trip in od) targets.Add(trip.dst);
            foreach (int t in targets)
            {
                Dijkstra(links, districtNode[t], edgeT, nodeT, dist, predNode, predEdge);
                foreach (var trip in od)
                {
                    if (trip.dst != t) continue;
                    Route r = MakeRoute(districtNode[t], districtNode[trip.src], districts[t].Id, districts[trip.src].Id,
                        trip.flow, edges, nodeIdOfIndex, accesses, roadNodes, predNode, predEdge);
                    if (r != null)
                    {
                        r.SrcCell = districts[t].Cells[0].Pos;
                        r.DstCell = districts[trip.src].Cells[0].Pos;
                        Routes.Add(r);
                    }
                }
            }
        }

        static Route MakeRoute(int src, int dst, int srcId, int dstId, float flow, List<RoadEdge> edges,
            int[] nodeIdOfIndex, List<Access> accesses, int roadNodes, int[] predNode, int[] predEdge)
        {
            var hops = new List<(int from, int to, int edge)>();
            int n = dst;
            int guard = predNode.Length + 2;
            while (n != src && guard-- > 0)
            {
                int p = predNode[n];
                if (p < 0) return null;
                hops.Add((p, n, predEdge[n]));
                n = p;
            }

            if (n != src) return null;
            hops.Reverse();

            var route = new Route { Flow = flow, SrcDistrictId = srcId, DstDistrictId = dstId };
            AppendSteps(route, hops, edges, nodeIdOfIndex, accesses, roadNodes);
            return route.Steps.Count > 0 ? route : null;
        }

        static void AppendSteps(Route route, List<(int from, int to, int edge)> hops, List<RoadEdge> edges,
            int[] nodeIdOfIndex, List<Access> accesses, int roadNodes)
        {
            foreach (var hop in hops)
            {
                if (hop.edge < 0) continue;
                RoadEdge e = edges[hop.edge];
                bool fromRoad = hop.from < roadNodes;
                bool toRoad = hop.to < roadNodes;
                float a, b;

                if (fromRoad && toRoad)
                {
                    bool fromIsA = nodeIdOfIndex[hop.from] == e.NodeA;
                    a = fromIsA ? 0f : e.Length;
                    b = fromIsA ? e.Length : 0f;
                }
                else if (!fromRoad && toRoad)
                {
                    a = accesses[hop.from - roadNodes].Along;
                    b = nodeIdOfIndex[hop.to] == e.NodeA ? 0f : e.Length;
                }
                else if (fromRoad)
                {
                    a = nodeIdOfIndex[hop.from] == e.NodeA ? 0f : e.Length;
                    b = accesses[hop.to - roadNodes].Along;
                }
                else
                {
                    a = accesses[hop.from - roadNodes].Along;
                    b = accesses[hop.to - roadNodes].Along;
                }

                if (Mathf.Abs(a - b) < 0.02f) continue;
                route.Steps.Add(new RouteStep
                {
                    Edge = e,
                    From = a,
                    To = b,
                    EndNode = toRoad ? nodeIdOfIndex[hop.to] : -1
                });
            }
        }

        /// <summary>
        /// Путь от точки на дороге до района с учётом направления взгляда машины.
        /// Для машин, у которых под колёсами пересобрали сеть. Возвращает null, если дороги рядом нет или пути нет.
        /// </summary>
        public Route RouteFrom(Vector2 pos, Vector2 heading, Vector2Int destCell, float radius)
        {
            if (_dirty || _rs == null || _graph == null || _model == null) return null;

            District dd = _model.Get(destCell)?.District;
            if (dd == null) return null;
            int di = -1;
            IReadOnlyList<District> districts = _model.Districts;
            for (int i = 0; i < districts.Count; i++)
                if (ReferenceEquals(districts[i], dd)) { di = i; break; }
            if (di < 0 || !_rs.DistrictNode.TryGetValue(di, out int dstNode)) return null;

            if (!_graph.FindEdgeNear(pos, radius, RoadLayer.Ground, out RoadEdge edge, out int seg, out Vector2 closest))
                return null;
            if (!_rs.EdgeIndexById.ContainsKey(edge.Id)) return null;

            float along = 0f;
            for (int i = 0; i < seg && i + 1 < edge.Points.Count; i++)
                along += Vector2.Distance(edge.Points[i], edge.Points[i + 1]);
            along = Mathf.Clamp(along + Vector2.Distance(edge.Points[seg], closest), 0f, edge.Length);

            Vector2 tangent = seg + 1 < edge.Points.Count
                ? (edge.Points[seg + 1] - edge.Points[seg]).normalized
                : Vector2.right;
            bool towardB = Vector2.Dot(tangent, heading) >= 0f;
            float speed = EdgeSpeed(edge);

            var dist = new float[_rs.Links.Length];
            var pn = new int[_rs.Links.Length];
            var pe = new int[_rs.Links.Length];

            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool allowA = attempt == 1 || !towardB;
                bool allowB = attempt == 1 || towardB;
                var seeds = new List<(int node, float d)>();
                if (allowA && edge.OneWay <= 0) seeds.Add((_rs.NodeIndex[edge.NodeA], along / speed));
                if (allowB && edge.OneWay >= 0) seeds.Add((_rs.NodeIndex[edge.NodeB], (edge.Length - along) / speed));
                if (seeds.Count == 0) continue;

                Dijkstra(_rs.Links, -1, _rs.EdgeT, _rs.NodeT, dist, pn, pe, seeds);
                if (dist[dstNode] >= float.MaxValue) continue;

                var hops = new List<(int from, int to, int edge)>();
                int n = dstNode;
                int guard = pn.Length + 2;
                while (pn[n] >= 0 && guard-- > 0)
                {
                    hops.Add((pn[n], n, pe[n]));
                    n = pn[n];
                }

                if (n >= _rs.RoadNodes) continue;
                hops.Reverse();

                var route = new Route { DstCell = destCell };
                int seedId = _rs.NodeIdOfIndex[n];
                route.Steps.Add(new RouteStep
                {
                    Edge = edge,
                    From = along,
                    To = seedId == edge.NodeA ? 0f : edge.Length,
                    EndNode = seedId
                });
                if (Mathf.Abs(route.Steps[0].From - route.Steps[0].To) < 0.02f) route.Steps.Clear();

                AppendSteps(route, hops, _rs.Edges, _rs.NodeIdOfIndex, _rs.Accesses, _rs.RoadNodes);
                if (route.Steps.Count > 0) return route;
            }

            return null;
        }

        static void Add(Dictionary<int, float> map, int key, float value)
        {
            map.TryGetValue(key, out float cur);
            map[key] = cur + value;
        }

        static float Bpr(float t0, float v, float c)
        {
            float x = v / Mathf.Max(0.01f, c);
            float x2 = x * x;
            return t0 * (1f + 0.15f * x2 * x2);
        }

        static void AddOne(List<Link>[] links, int from, int to, int edge, float time)
        {
            links[from].Add(new Link { To = to, Edge = edge, Dynamic = false, FixedTime = time });
        }

        Access FindAccess(int index, District d)
        {
            Access best = null;
            float bestDist = float.MaxValue;
            foreach (BuildingCell c in d.Cells)
            {
                Vector2 center = new Vector2(c.Pos.x + 0.5f, c.Pos.y + 0.5f) * CellSizeScale;
                if (!_graph.FindEdgeNear(center, AccessRadius, RoadLayer.Ground,
                        out RoadEdge edge, out int seg, out Vector2 closest)) continue;

                float dist = Vector2.Distance(center, closest);
                if (dist >= bestDist) continue;

                float along = 0f;
                for (int i = 0; i < seg && i + 1 < edge.Points.Count; i++)
                    along += Vector2.Distance(edge.Points[i], edge.Points[i + 1]);
                along += Vector2.Distance(edge.Points[seg], closest);

                bestDist = dist;
                best = new Access
                {
                    DistrictIndex = index,
                    Edge = edge,
                    Along = Mathf.Clamp(along, 0f, edge.Length),
                    Offset = dist
                };
            }

            return best;
        }

        /// <summary>Размер клетки сетки зданий. Задаёт CitySystem.</summary>
        [HideInInspector] public float CellSizeScale = 1f;

        void AddDemand(List<(int src, int dst, float flow)> od, int origin, List<int> dests, float demand,
            IReadOnlyList<District> districts, Dictionary<int, int> districtNode, float[] dist)
        {
            if (dests.Count == 0 || demand <= 0f) return;

            float sum = 0f;
            var w = new float[dests.Count];
            for (int i = 0; i < dests.Count; i++)
            {
                float t = dist[districtNode[dests[i]]];
                if (float.IsInfinity(t) || t >= float.MaxValue) continue;
                w[i] = districts[dests[i]].Count * Mathf.Exp(-t / Mathf.Max(0.01f, GravityTau));
                sum += w[i];
            }

            if (sum <= 0f) return;
            for (int i = 0; i < dests.Count; i++)
                if (w[i] > 0f) od.Add((origin, dests[i], demand * w[i] / sum));
        }

        static float BestTime(List<int> dests, Dictionary<int, int> districtNode, float[] dist)
        {
            float best = float.MaxValue;
            foreach (int d in dests)
            {
                float t = dist[districtNode[d]];
                if (t < best) best = t;
            }

            return best;
        }

        static void AddPathFlow(int node, int source, int[] predNode, int[] predEdge, float flow, float[] y,
            float[] ny, int roadNodes)
        {
            int guard = predNode.Length + 2;
            while (node != source && guard-- > 0)
            {
                if (node < roadNodes) ny[node] += flow;
                int pn = predNode[node];
                if (pn < 0) return;
                int e = predEdge[node];
                if (e >= 0) y[e] += flow;
                node = pn;
            }
        }

        // Dijkstra на SortedSet. Сети маленькие, для телефона потом заменим на Job + Burst.
        static void Dijkstra(List<Link>[] links, int source, float[] edgeT, float[] nodeT, float[] dist,
            int[] predNode, int[] predEdge, List<(int node, float d)> seeds = null)
        {
            for (int i = 0; i < dist.Length; i++)
            {
                dist[i] = float.MaxValue;
                predNode[i] = -1;
                predEdge[i] = -1;
            }

            var set = new SortedSet<(float d, int n)>();
            if (seeds != null)
            {
                foreach (var sd in seeds)
                {
                    if (sd.d >= dist[sd.node]) continue;
                    dist[sd.node] = sd.d;
                    set.Add((sd.d, sd.node));
                }
            }
            else
            {
                dist[source] = 0f;
                set.Add((0f, source));
            }

            while (set.Count > 0)
            {
                (float d, int n) cur = set.Min;
                set.Remove(cur);
                if (cur.d > dist[cur.n]) continue;

                List<Link> list = links[cur.n];
                float pass = cur.n < nodeT.Length ? nodeT[cur.n] : 0f; // задержка на развилке при выезде из неё
                for (int i = 0; i < list.Count; i++)
                {
                    Link l = list[i];
                    float t = l.Dynamic ? edgeT[l.Edge] : l.FixedTime;
                    float nd = cur.d + t + pass;
                    if (nd >= dist[l.To]) continue;

                    dist[l.To] = nd;
                    predNode[l.To] = cur.n;
                    predEdge[l.To] = l.Edge;
                    set.Add((nd, l.To));
                }
            }
        }
    }
}
