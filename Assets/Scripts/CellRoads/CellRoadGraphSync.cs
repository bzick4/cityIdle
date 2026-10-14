using System.Collections.Generic;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Держит RoadGraph в соответствии с дорогами по клеткам, для расчёта потоков.
    /// Узлы: концы дорог и развилки (не 2 связи). Рёбра: цепочки клеток между узлами,
    /// а ломаная идёт по центрам клеток и по дугам в угловых клетках.
    /// Граф перестраивается целиком, для сотен клеток это быстро.
    /// </summary>
    public sealed class CellRoadGraphSync
    {
        readonly CellRoadMap _map;
        readonly RoadGraph _graph;
        readonly float _cell;

        public CellRoadGraphSync(CellRoadMap map, RoadGraph graph, float cellSize)
        {
            _map = map;
            _graph = graph;
            _cell = cellSize;
            _map.Changed += Rebuild;
            Rebuild();
        }

        Vector2 Center(Vector2Int c)
        {
            return new Vector2((c.x + 0.5f) * _cell, (c.y + 0.5f) * _cell);
        }

        public void Rebuild()
        {
            var ids = new List<int>();
            foreach (RoadEdge e in _graph.Edges) ids.Add(e.Id);
            foreach (int id in ids) _graph.RemoveEdge(id);

            var nodes = new Dictionary<Vector2Int, RoadNode>();
            var visited = new HashSet<(Vector2Int, int)>();

            // Первый проход: от настоящих узлов. Второй: замкнутые кольца без узлов.
            foreach (var kv in _map.Cells)
            {
                int deg = Side.Count(kv.Value);
                if (deg == 0 || deg == 2 || IsOverpass(kv.Key)) continue;
                TraceAllFrom(kv.Key, nodes, visited);
            }

            var rest = new List<Vector2Int>();
            foreach (var kv in _map.Cells)
                if (Side.Count(kv.Value) == 2) rest.Add(kv.Key);

            foreach (Vector2Int cell in rest)
            {
                bool any = false;
                foreach (int side in Side.All)
                    if ((_map.Mask(cell) & side) != 0 && !visited.Contains((cell, side))) any = true;
                if (any) TraceAllFrom(cell, nodes, visited);
            }
        }

        /// <summary>Эстакада это не узел: дороги проходят её насквозь и не соединяются.</summary>
        bool IsOverpass(Vector2Int cell)
        {
            return Side.Count(_map.Mask(cell)) == 4 && JunctionSpecs.IsOverpass(_map.TypeAt(cell));
        }

        bool IsRoadModeBoundary(Vector2Int a, Vector2Int b)
        {
            HighwayLane x = _map.LaneAt(a);
            HighwayLane y = _map.LaneAt(b);
            return x.Active != y.Active || (x.Active && y.Active && x.OneWay != y.OneWay);
        }

        RoadNode NodeAt(Vector2Int cell, Dictionary<Vector2Int, RoadNode> nodes)
        {
            if (!nodes.TryGetValue(cell, out RoadNode n))
            {
                n = _graph.AddNode(Center(cell), RoadLayer.Ground);
                nodes[cell] = n;

                // Развилка (три и четыре дороги) задерживает машины, кольцо меньше и пропускает больше.
                if (Side.Count(_map.Mask(cell)) >= 3)
                {
                    JunctionType type = _map.TypeAt(cell);
                    n.JunctionDelay = JunctionSpecs.Delay(type);
                    n.JunctionCapacity = JunctionSpecs.Capacity(type);
                }
            }

            return n;
        }

        void TraceAllFrom(Vector2Int start, Dictionary<Vector2Int, RoadNode> nodes, HashSet<(Vector2Int, int)> visited)
        {
            int startMask = _map.Mask(start);
            foreach (int side in Side.All)
            {
                if ((startMask & side) == 0 || visited.Contains((start, side))) continue;
                Trace(start, side, nodes, visited);
            }
        }

        void Trace(Vector2Int start, int firstSide, Dictionary<Vector2Int, RoadNode> nodes,
            HashSet<(Vector2Int, int)> visited)
        {
            var pts = new List<Vector2> { Center(start) };
            var edgeCells = new List<Vector2Int> { start };
            Vector2Int cur = start;
            int dir = firstSide;
            visited.Add((cur, dir));

            int interior = 0, laneCells = 0, highwayCells = 0, fwd = 0, bwd = 0;

            int guard = _map.Count * 4 + 8;
            while (guard-- > 0)
            {
                Vector2Int next = cur + Side.Vec(dir);
                int incoming = Side.Opposite(dir);
                if (!_map.Has(next)) return;
                visited.Add((next, incoming));

                int nextMask = _map.Mask(next);
                bool overpass = IsOverpass(next);
                bool modeBoundary = !overpass && Side.Count(nextMask) == 2 &&
                                    Side.Count(_map.Mask(cur)) == 2 && IsRoadModeBoundary(cur, next);
                bool isNode = (Side.Count(nextMask) != 2 && !overpass) || next == start || modeBoundary;
                if (isNode)
                {
                    pts.Add(Center(next));
                    edgeCells.Add(next);
                    Traits traits = modeBoundary
                        ? MakeBoundaryTraits(cur, next, dir)
                        : MakeTraits(interior, laneCells, highwayCells, fwd, bwd);
                    if (IsStandaloneHighwayPath(edgeCells))
                    {
                        traits = MakeStandaloneTraits(edgeCells, dir);
                        pts = SmoothCellPath(edgeCells);
                    }
                    Finish(start, next, pts, nodes, traits);
                    return;
                }

                int exit = overpass ? Side.Opposite(incoming) : nextMask & ~incoming;
                AppendInterior(pts, next, incoming, exit);
                edgeCells.Add(next);

                interior++;
                // Направленная полоса может проходить по эстакаде. Сам узел рисуется как мост,
                // но граф сохраняет направление и односторонность продолжающегося шоссе.
                // В клетке моста собственная полоса может принадлежать другой дороге: берём полосу по оси проезда.
                HighwayLane lane = overpass ? _map.LaneAlong(next, exit) : _map.LaneAt(next);
                // В клетке моста LaneAt возвращает направление дороги, которая уже была
                // размечена в этой клетке. При поперечном проезде это направление относится
                // к другой оси и не должно задавать движение текущего ребра.
                bool laneOnThisPass = lane.Active &&
                    (SameAxis(lane.Heading, incoming) || SameAxis(lane.Heading, exit));
                if (laneOnThisPass)
                {
                    laneCells++;
                    if (!lane.OneWay) highwayCells++;
                    if (exit == lane.Heading) fwd++;
                    else bwd++;
                }

                dir = exit;
                visited.Add((next, dir));
                cur = next;
            }
        }

        static bool SameAxis(int a, int b)
        {
            return a == b || a == Side.Opposite(b);
        }

        struct Traits
        {
            public int OneWay;
            public float Speed;
            public float Capacity;
        }

        Traits MakeBoundaryTraits(Vector2Int from, Vector2Int to, int direction)
        {
            HighwayLane lane = _map.LaneAt(from);
            bool atFrom = lane.Active;
            if (!lane.Active) lane = _map.LaneAt(to);
            if (!lane.Active) return new Traits { OneWay = 0, Speed = 1f, Capacity = 1f };

            bool followsRoute;
            if (atFrom) followsRoute = lane.Heading == direction;
            else
            {
                int incoming = Side.Opposite(direction);
                int exit = _map.Mask(to) & ~incoming;
                followsRoute = lane.Heading == exit;
            }

            int oneWay = followsRoute ? 1 : -1;
            return new Traits
            {
                OneWay = oneWay,
                Speed = lane.OneWay ? 1f : HighwaySpecs.SpeedFactor,
                Capacity = lane.OneWay ? 1f : HighwaySpecs.CapacityFactor
            };
        }

        /// <summary>
        /// Шоссе по цепочке клеток. Скорость растёт пропорционально доле клеток-полос.
        /// Переходные обычные клетки на концах наследуют направление трассы, пока не встретится узел.
        /// </summary>
        static Traits MakeTraits(int interior, int laneCells, int highwayCells, int fwd, int bwd)
        {
            var t = new Traits { OneWay = 0, Speed = 1f, Capacity = 1f };
            if (interior == 0 || laneCells == 0) return t;

            float f = (float)highwayCells / interior;
            t.Speed = 1f / ((1f - f) + f / HighwaySpecs.SpeedFactor);

            t.Capacity = 1f + f * (HighwaySpecs.CapacityFactor - 1f);
            if (fwd > 0 && bwd == 0) t.OneWay = 1;
            else if (bwd > 0 && fwd == 0) t.OneWay = -1;

            return t;
        }

        static void Apply(RoadEdge e, Traits t)
        {
            if (e == null) return;
            e.OneWay = t.OneWay;
            e.SpeedFactor = t.Speed;
            e.CapacityFactor = t.Capacity;
        }

        bool IsStandaloneHighwayPath(List<Vector2Int> cells)
        {
            if (cells.Count < 2) return false;
            bool oneWay = _map.LaneAt(cells[0]).OneWay;
            for (int i = 0; i < cells.Count; i++)
            {
                HighwayLane lane = _map.LaneAt(cells[i]);
                if (!lane.Active || !lane.Standalone || lane.OneWay != oneWay) return false;
            }
            return true;
        }

        Traits MakeStandaloneTraits(List<Vector2Int> cells, int firstDirection)
        {
            HighwayLane lane = _map.LaneAt(cells[0]);
            bool followsRoute = lane.Heading == firstDirection;
            return new Traits
            {
                OneWay = followsRoute ? 1 : -1,
                Speed = lane.OneWay ? 1f : HighwaySpecs.SpeedFactor,
                Capacity = lane.OneWay ? 1f : HighwaySpecs.CapacityFactor
            };
        }

        List<Vector2> SmoothCellPath(List<Vector2Int> cells)
        {
            var centers = new List<Vector2>(cells.Count);
            for (int i = 0; i < cells.Count; i++) centers.Add(Center(cells[i]));
            var result = new List<Vector2>();
            const int steps = 10;
            for (int i = 0; i + 1 < centers.Count; i++)
            {
                Vector2 p0 = i > 0 ? centers[i - 1] : centers[i];
                Vector2 p1 = centers[i];
                Vector2 p2 = centers[i + 1];
                Vector2 p3 = i + 2 < centers.Count ? centers[i + 2] : p2;
                for (int step = 0; step < steps; step++)
                {
                    float t = step / (float)steps;
                    float t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * ((2f * p1) + (-p0 + p2) * t +
                        (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                        (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(centers[centers.Count - 1]);
            return result;
        }

        void Finish(Vector2Int a, Vector2Int b, List<Vector2> pts, Dictionary<Vector2Int, RoadNode> nodes, Traits traits)
        {
            if (a != b)
            {
                Apply(_graph.AddEdge(NodeAt(a, nodes), NodeAt(b, nodes), pts, RoadType.Local), traits);
                return;
            }

            // Петля: делим пополам вспомогательным узлом, потому что граф не допускает ребро из узла в себя.
            if (pts.Count < 4) return;
            int k = pts.Count / 2;
            RoadNode na = NodeAt(a, nodes);
            RoadNode mid = _graph.AddNode(pts[k], RoadLayer.Ground);
            Apply(_graph.AddEdge(na, mid, pts.GetRange(0, k + 1), RoadType.Local), traits);
            Apply(_graph.AddEdge(mid, na, pts.GetRange(k, pts.Count - k), RoadType.Local), traits);
        }

        void AppendInterior(List<Vector2> pts, Vector2Int cell, int incoming, int exit)
        {
            Vector2 c = Center(cell);
            if (incoming == Side.Opposite(exit))
            {
                pts.Add(c);
                return;
            }

            float h = _cell * 0.5f;
            Vector2 vin = Side.Vec(incoming);
            Vector2 vex = Side.Vec(exit);
            Vector2 k = c + (vin + vex) * h;
            Vector2 start = c + vin * h;
            Vector2 end = c + vex * h;
            float a0 = Mathf.Atan2(start.y - k.y, start.x - k.x);
            float a1 = Mathf.Atan2(end.y - k.y, end.x - k.x);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            const int n = 4;
            for (int i = 0; i <= n; i++)
            {
                float a = a0 + delta * i / n;
                pts.Add(k + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h);
            }
        }
    }
}
