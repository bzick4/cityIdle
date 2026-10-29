using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>Геометрия дорог по слоям. Дорога, остров кольца, тень эстакады, полотно эстакады.</summary>
    public sealed class RoadLayers
    {
        public const int Road = 0, Marking = 1, Island = 2, Shadow = 3, Bridge = 4, BridgeMarking = 5, Selection = 6, Median = 7;
        public const int Count = 8;

        public readonly List<Vector3>[] Verts = new List<Vector3>[Count];
        public readonly List<int>[] Tris = new List<int>[Count];

        public RoadLayers()
        {
            for (int i = 0; i < Count; i++)
            {
                Verts[i] = new List<Vector3>();
                Tris[i] = new List<int>();
            }
        }

        public Mesh ToMesh(int layer)
        {
            var mesh = new Mesh { name = "RoadLayer" + layer };
            mesh.SetVertices(Verts[layer]);
            mesh.SetTriangles(Tris[layer], 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Меш дороги по клеткам. Прямая это полоса, угол это четверть кольца вокруг угла клетки,
    /// поэтому угловая клетка закруглена. Развилки: перекрёсток со скруглёнными внутренними углами,
    /// кольцо с островом, эстакада с тенью.
    /// </summary>
    public static class RoadCellMesher
    {
        /// <summary>Радиус скругления внутренних углов перекрёстка в долях клетки.</summary>
        const float FilletFraction = 0.18f;
        const float RoundaboutOuter = 0.46f;
        const float RoundaboutIsland = 0.20f;
        const float HighwayRoundaboutOuter = 0.78f;
        const float HighwayRoundaboutIsland = 0.37f;
        const float ShadowExtra = 0.07f;

        /// <summary>Половина ширины полосы шоссе в долях клетки. Между двумя полосами остаётся полоса разделителя.</summary>
        const float HighwayHalfWidth = 0.44f;

        /// <summary>Толщина разделительной полосы в долях клетки.</summary>
        const float MarkingWidth = 0.045f;

        /// <summary>
        /// Смещение центра полосы движения от оси дороги в долях ширины дороги. Машины будут ездить
        /// по этим линиям, по одной полосе в каждую сторону.
        /// </summary>
        public const float LaneOffset = 0.25f;

        public static RoadLayers Build(IEnumerable<KeyValuePair<Vector2Int, int>> cells,
            Func<Vector2Int, JunctionType> typeOf, Func<Vector2Int, HighwayLane> laneOf, float cellSize,
            float widthFraction)
        {
            var layers = new RoadLayers();
            var entries = new List<KeyValuePair<Vector2Int, int>>();
            foreach (var item in cells) entries.Add(item);
            var allMasks = new Dictionary<Vector2Int, int>();
            foreach (var item in entries) allMasks[item.Key] = item.Value;
            foreach (var kv in entries)
            {
                Vector2 c = new Vector2((kv.Key.x + 0.5f) * cellSize, (kv.Key.y + 0.5f) * cellSize);
                JunctionType type = typeOf != null ? typeOf(kv.Key) : JunctionType.Plain;
                HighwayLane lane = laneOf != null ? laneOf(kv.Key) : default;
                // Самостоятельные быстрые трассы собираем цельной сглаженной лентой ниже.
                // Узлы оставляем здесь как основание для стыка с городской дорогой.
                if (lane.Active && lane.Standalone && Side.Count(kv.Value) <= 2)
                    continue;
                // На развилках оставляем общую геометрию узла. Полосы подходят к узлу,
                // но широкое полотно не перекрывает его целиком.
                if (lane.Active && Side.Count(kv.Value) == 1 && lane.Manual && !lane.OneWay)
                {
                    AppendHighwayEnd(layers, c, kv.Value, lane, cellSize,
                        ShouldDrawStraightArrow(kv.Key, lane, laneOf));
                }
                else if (lane.Active && Side.Count(kv.Value) == 2)
                {
                    bool corner = kv.Value == (Side.N | Side.E) || kv.Value == (Side.E | Side.S) ||
                                  kv.Value == (Side.S | Side.W) || kv.Value == (Side.W | Side.N);
                    if (lane.OneWay)
                    {
                        bool arrow = !corner && ShouldDrawStraightArrow(kv.Key, lane, laneOf);
                        bool turnArrow = corner && IsTurnArrow(kv.Key, kv.Value, lane, laneOf);
                        AppendCell(layers, c, kv.Value, type, cellSize, cellSize * widthFraction,
                            arrow || turnArrow);
                        if (arrow) AppendRampArrow(layers, c, lane, cellSize);
                        else if (turnArrow) AppendCornerArrow(layers, c, kv.Value, lane, cellSize, 0.12f, 0.09f);
                    }
                    else if (corner)
                        AppendHighwayCorner(layers, c, kv.Value, lane, cellSize,
                            IsTurnArrow(kv.Key, kv.Value, lane, laneOf));
                    else AppendHighwayLane(layers, c, lane, cellSize,
                        ShouldDrawStraightArrow(kv.Key, lane, laneOf));
                }
                else
                {
                    bool overpass = Side.Count(kv.Value) == 4 && JunctionSpecs.IsOverpass(type);
                    if (overpass)
                    {
                        bool bridgeVertical = type == JunctionType.OverpassVertical;
                        HighwayLane bridgeLane = LaneOnAxis(kv.Key, bridgeVertical, lane, laneOf, allMasks);
                        HighwayLane underLane = LaneOnAxis(kv.Key, !bridgeVertical, lane, laneOf, allMasks);
                        bool bridgeArrow = bridgeLane.Active && ShouldDrawStraightArrow(kv.Key, bridgeLane, laneOf);
                        bool underArrow = underLane.Active && ShouldDrawStraightArrow(kv.Key, underLane, laneOf);
                        AppendCell(layers, c, kv.Value, type, cellSize, cellSize * widthFraction,
                            false, bridgeLane, underLane, bridgeArrow, underArrow);
                    }
                    else AppendCell(layers, c, kv.Value, type, cellSize, cellSize * widthFraction);
                    // Обычная геометрия узла оставляет щель там, где широкая полоса подходит
                    // к развилке. Продлеваем полотно через узел по оси скоростной дороги.
                    if (Side.Count(kv.Value) >= 3 && type == JunctionType.Plain)
                        AppendHighwayNodeWidening(layers, c, kv.Key, kv.Value, laneOf, cellSize);
                }
            }

            if (laneOf != null) AppendStandaloneHighways(layers, entries, laneOf, cellSize);

            return layers;
        }

        static void AppendStandaloneHighways(RoadLayers layers,
            IEnumerable<KeyValuePair<Vector2Int, int>> cells,
            Func<Vector2Int, HighwayLane> laneOf, float cellSize)
        {
            var all = new List<Vector2Int>();
            var masks = new Dictionary<Vector2Int, int>();
            foreach (var item in cells)
            {
                masks[item.Key] = item.Value;
                HighwayLane lane = laneOf(item.Key);
                if (lane.Active && lane.Standalone) all.Add(item.Key);
            }

            var visited = new HashSet<Vector2Int>();
            foreach (Vector2Int cell in all)
            {
                if (visited.Contains(cell) || HasStandalonePredecessor(cell, laneOf, masks)) continue;
                AppendStandaloneRoute(layers, TraceStandaloneRoute(cell, laneOf, masks, visited), laneOf, masks, cellSize);
            }
            // Обрабатываем замкнутые контуры, у которых нет начала.
            foreach (Vector2Int cell in all)
            {
                if (visited.Contains(cell)) continue;
                List<Vector2Int> loop = TraceStandaloneRoute(cell, laneOf, masks, visited);
                CloseLoop(loop, laneOf, masks);
                AppendStandaloneRoute(layers, loop, laneOf, masks, cellSize);
            }
        }

        /// <summary>
        /// Замкнутое кольцо: маршрут возвращается в свою первую клетку. Без этого между последней и первой
        /// клеткой оставался разрыв с круглыми торцами. Начинаем кольцо с прямой клетки, чтобы шов не лёг на поворот,
        /// и повторяем первую клетку в конце.
        /// </summary>
        static void CloseLoop(List<Vector2Int> route, Func<Vector2Int, HighwayLane> laneOf,
            Dictionary<Vector2Int, int> masks)
        {
            if (route == null || route.Count < 4) return;
            Vector2Int last = route[route.Count - 1];
            HighwayLane lastLane = laneOf(last);
            if (!lastLane.Active || last + Side.Vec(lastLane.Heading) != route[0]) return;
            if (!masks.TryGetValue(last, out int lastMask) || (lastMask & lastLane.Heading) == 0 ||
                !masks.TryGetValue(route[0], out int firstMask) || (firstMask & Side.Opposite(lastLane.Heading)) == 0) return;

            for (int k = 0; k < route.Count; k++)
            {
                int m = masks[route[k]];
                if (m != (Side.N | Side.S) && m != (Side.E | Side.W)) continue;
                if (k > 0)
                {
                    route.AddRange(route.GetRange(0, k));
                    route.RemoveRange(0, k);
                }
                break;
            }

            route.Add(route[0]);
        }

        static bool HasStandalonePredecessor(Vector2Int cell, Func<Vector2Int, HighwayLane> laneOf,
            Dictionary<Vector2Int, int> masks)
        {
            foreach (int side in Side.All)
            {
                Vector2Int neighbor = cell + Side.Vec(side);
                HighwayLane prior = laneOf(neighbor);
                // Сосед лежит со стороны side: его связь с нами это Opposite(side), наша связь с ним это side.
                if (!prior.Active || !prior.Standalone || prior.Heading != Side.Opposite(side) ||
                    !masks.TryGetValue(neighbor, out int priorMask) || (priorMask & Side.Opposite(side)) == 0 ||
                    !masks.TryGetValue(cell, out int cellMask) || (cellMask & side) == 0) continue;
                return true;
            }
            return false;
        }

        static List<Vector2Int> TraceStandaloneRoute(Vector2Int start,
            Func<Vector2Int, HighwayLane> laneOf, Dictionary<Vector2Int, int> masks,
            HashSet<Vector2Int> visited)
        {
            var route = new List<Vector2Int>();
            Vector2Int current = start;
            bool oneWayRoute = laneOf(start).OneWay;
            int guard = 4096;
            while (guard-- > 0 && !visited.Contains(current))
            {
                HighwayLane lane = laneOf(current);
                if (!lane.Active || !lane.Standalone || lane.OneWay != oneWayRoute) break;
                route.Add(current);
                visited.Add(current);
                Vector2Int next = current + Side.Vec(lane.Heading);
                int link = lane.Heading;
                if (!masks.TryGetValue(current, out int currentMask) || (currentMask & link) == 0 ||
                    !masks.TryGetValue(next, out int nextMask) || (nextMask & Side.Opposite(link)) == 0) break;
                // Мост через чужую полосу: маршрут доходит до центра клетки моста и там обрывается.
                // Сам мост рисует AppendOverpass, а за ним маршрут начинается заново.
                if (IsBridgeCellFor(next, link, laneOf, masks))
                {
                    route.Add(next);
                    break;
                }
                HighwayLane nextLane = laneOf(next);
                if (!nextLane.Active || !nextLane.Standalone || nextLane.OneWay != oneWayRoute) break;
                current = next;
            }

            // Маршрут, начавшийся сразу за мостом, стартует с центра клетки моста, чтобы не оставлять щель.
            if (route.Count > 0)
            {
                HighwayLane first = laneOf(route[0]);
                Vector2Int before = route[0] - Side.Vec(first.Heading);
                if (IsBridgeCellFor(before, first.Heading, laneOf, masks) &&
                    masks.TryGetValue(route[0], out int firstMask) && (firstMask & Side.Opposite(first.Heading)) != 0)
                    route.Insert(0, before);
            }

            return route;
        }

        static void AppendStandaloneRoute(RoadLayers layers, List<Vector2Int> route,
            Func<Vector2Int, HighwayLane> laneOf, Dictionary<Vector2Int, int> masks, float cellSize)
        {
            if (route == null || route.Count < 2) return;

            // Полоса и признак развилки по клеткам маршрута. Концевая клетка моста принадлежит чужой дороге,
            // поэтому для неё берём полосу соседней клетки маршрута и не считаем её развилкой.
            int last = route.Count - 1;
            var routeLane = new HighwayLane[route.Count];
            var routeJunction = new bool[route.Count];
            for (int k = 0; k < route.Count; k++)
            {
                routeLane[k] = laneOf(route[k]);
                routeJunction[k] = masks.TryGetValue(route[k], out int rm) && Side.Count(rm) > 2;
            }

            if (IsBridgeCellFor(route[0], Side.FromDelta(route[1] - route[0]), laneOf, masks))
            {
                routeLane[0] = routeLane[1];
                routeJunction[0] = false;
            }

            if (IsBridgeCellFor(route[last], Side.FromDelta(route[last] - route[last - 1]), laneOf, masks))
            {
                routeLane[last] = routeLane[last - 1];
                routeJunction[last] = false;
            }

            var points = new List<Vector2>(route.Count);
            for (int i = 0; i < route.Count; i++)
                points.Add(new Vector2((route[i].x + 0.5f) * cellSize, (route[i].y + 0.5f) * cellSize));

            var curve = new List<Vector2>();
            var curveCells = new List<int>();
            const int steps = 10;
            const int cornerSteps = 8;
            // Скругляем каждый поворот отдельно. Catmull-Rom на коротких S-образных
            // маршрутах перелетал через соседние клетки и ломал края дорожной ленты.
            curve.Add(points[0]);
            curveCells.Add(0);
            Vector2 cursor = points[0];
            float cornerRadius = cellSize * 0.48f;
            for (int i = 1; i + 1 < points.Count; i++)
            {
                Vector2 incoming = (points[i] - points[i - 1]).normalized;
                Vector2 outgoing = (points[i + 1] - points[i]).normalized;
                if (Vector2.Dot(incoming, outgoing) > 0.99f) continue;

                float radius = Mathf.Min(cornerRadius,
                    Mathf.Min(Vector2.Distance(points[i - 1], points[i]),
                        Vector2.Distance(points[i], points[i + 1])) * 0.48f);
                Vector2 approach = points[i] - incoming * radius;
                Vector2 exit = points[i] + outgoing * radius;
                AppendCurveLine(curve, curveCells, cursor, approach, i - 1, cellSize, steps);

                // Точная круговая дуга сохраняет внутренний радиус больше нуля даже у широкого
                // полотна. Квадратичная кривая в этом месте давала меньший радиус и пересекала края.
                Vector2 center = points[i] - incoming * radius + outgoing * radius;
                Vector2 startRadius = approach - center;
                Vector2 endRadius = exit - center;
                float startAngle = Mathf.Atan2(startRadius.y, startRadius.x) * Mathf.Rad2Deg;
                float deltaAngle = Mathf.DeltaAngle(startAngle,
                    Mathf.Atan2(endRadius.y, endRadius.x) * Mathf.Rad2Deg);
                for (int step = 1; step <= cornerSteps; step++)
                {
                    float angle = (startAngle + deltaAngle * step / cornerSteps) * Mathf.Deg2Rad;
                    curve.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                    curveCells.Add(i);
                }
                cursor = exit;
            }
            AppendCurveLine(curve, curveCells, cursor, points[points.Count - 1],
                route.Count - 1, cellSize, steps);

            bool oneWay = routeLane[0].OneWay;
            float halfWidth = oneWay ? cellSize * 0.25f : cellSize * HighwayHalfWidth;
            var normals = new Vector2[curve.Count];
            for (int i = 0; i < curve.Count; i++)
            {
                Vector2 tangent = curve[Mathf.Min(i + 1, curve.Count - 1)] - curve[Mathf.Max(i - 1, 0)];
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector2.right;
                tangent.Normalize();
                normals[i] = new Vector2(-tangent.y, tangent.x);
            }

            AddRibbon(layers.Verts[RoadLayers.Road], layers.Tris[RoadLayers.Road],
                curve, normals, null, 0f, null, halfWidth * 2f);

            var edgeOffset = new float[curve.Count];
            var edgeWidth = new float[curve.Count];
            var medianOffset = new float[curve.Count];
            var medianWidth = new float[curve.Count];
            var farEdgeOffset = new float[curve.Count];
            var farEdgeWidth = new float[curve.Count];
            bool hasMedian = false;
            for (int i = 0; i < curve.Count; i++)
            {
                HighwayLane lane = routeLane[curveCells[i]];
                bool junction = routeJunction[curveCells[i]];
                float side = Vector2.Dot(Side.Vec(lane.PartnerSide), normals[i]) >= 0f ? 1f : -1f;
                if (lane.Median && !oneWay)
                {
                    hasMedian = true;
                    edgeOffset[i] = -side * (halfWidth - cellSize * 0.045f);
                    edgeWidth[i] = junction ? 0f : cellSize * 0.025f;
                    medianOffset[i] = side * (halfWidth + cellSize * 0.045f);
                    medianWidth[i] = junction ? 0f : cellSize * 0.045f;
                    farEdgeWidth[i] = 0f;
                }
                else
                {
                    edgeOffset[i] = halfWidth - cellSize * 0.045f;
                    edgeWidth[i] = junction ? 0f : cellSize * 0.025f;
                    medianOffset[i] = 0f;
                    medianWidth[i] = 0f;
                    farEdgeOffset[i] = -(halfWidth - cellSize * 0.045f);
                    farEdgeWidth[i] = junction ? 0f : cellSize * 0.025f;
                }
            }
            AddRibbon(layers.Verts[RoadLayers.Marking], layers.Tris[RoadLayers.Marking],
                curve, normals, edgeOffset, 0f, edgeWidth, 0f);
            AddRibbon(layers.Verts[RoadLayers.Marking], layers.Tris[RoadLayers.Marking],
                curve, normals, farEdgeOffset, 0f, farEdgeWidth, 0f);
            if (hasMedian)
                AddRibbon(layers.Verts[RoadLayers.Median], layers.Tris[RoadLayers.Median],
                    curve, normals, medianOffset, 0f, medianWidth, 0f);

            AddDisc(layers.Verts[RoadLayers.Road], layers.Tris[RoadLayers.Road], curve[0], halfWidth);
            AddDisc(layers.Verts[RoadLayers.Road], layers.Tris[RoadLayers.Road], curve[curve.Count - 1], halfWidth);
            int arrowStep = Mathf.Max(1, Mathf.RoundToInt(5f * steps));
            int arrowStart = route.Count <= 5 ? (curve.Count - 1) / 2 : Mathf.RoundToInt(3f * steps);
            for (int i = Mathf.Max(1, arrowStart); i < curve.Count - 1; i += arrowStep)
            {
                if (routeJunction[curveCells[i]]) continue;
                AddHighwayArrow(layers.Verts[RoadLayers.Marking], layers.Tris[RoadLayers.Marking],
                    curve[i], (curve[i + 1] - curve[i - 1]).normalized, cellSize);
            }
        }

        static void AppendCurveLine(List<Vector2> curve, List<int> curveCells, Vector2 from,
            Vector2 to, int routeCell, float cellSize, int stepsPerCell)
        {
            float distance = Vector2.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.RoundToInt(distance / cellSize * stepsPerCell));
            for (int step = 1; step <= steps; step++)
            {
                curve.Add(Vector2.Lerp(from, to, step / (float)steps));
                curveCells.Add(routeCell);
            }
        }

        static void AddRibbon(List<Vector3> vertices, List<int> triangles, List<Vector2> points,
            Vector2[] normals, float[] offsets, float defaultOffset, float[] widths, float defaultWidth)
        {
            for (int i = 1; i < points.Count; i++)
            {
                float oa = offsets != null ? offsets[i - 1] : defaultOffset;
                float ob = offsets != null ? offsets[i] : defaultOffset;
                float wa = widths != null ? widths[i - 1] : defaultWidth;
                float wb = widths != null ? widths[i] : defaultWidth;
                Vector2 leftA = points[i - 1] + normals[i - 1] * (oa - wa * 0.5f);
                Vector2 rightA = points[i - 1] + normals[i - 1] * (oa + wa * 0.5f);
                Vector2 leftB = points[i] + normals[i] * (ob - wb * 0.5f);
                Vector2 rightB = points[i] + normals[i] * (ob + wb * 0.5f);
                AddQuad(vertices, triangles, leftA, rightA, rightB, leftB);
            }
        }

        /// <summary>
        /// Полоса по оси клетки моста. Если собственная полоса клетки идёт по другой оси, ищем полосу вдоль
        /// этой оси у соседей, проходя насквозь соседние клетки мостов (мост через двойное шоссе).
        /// </summary>
        static HighwayLane LaneOnAxis(Vector2Int cell, bool vertical, HighwayLane current,
            Func<Vector2Int, HighwayLane> laneOf, Dictionary<Vector2Int, int> masks)
        {
            if (laneOf == null) return default;
            if (current.Active && IsOnAxis(current.Heading, vertical)) return current;
            int[] sides = vertical ? new[] { Side.N, Side.S } : new[] { Side.E, Side.W };
            foreach (int side in sides)
            {
                Vector2Int c = cell;
                for (int k = 0; k < 8; k++)
                {
                    c += Side.Vec(side);
                    HighwayLane candidate = laneOf(c);
                    if (candidate.Active && IsOnAxis(candidate.Heading, vertical)) return candidate;
                    if (masks == null || !masks.TryGetValue(c, out int m) || Side.Count(m) != 4) break;
                }
            }
            return default;
        }

        /// <summary>
        /// Клетка чужого моста для маршрута, идущего вдоль стороны link: на ней сходятся четыре дороги,
        /// а её собственная полоса идёт по другой оси.
        /// </summary>
        static bool IsBridgeCellFor(Vector2Int cell, int link, Func<Vector2Int, HighwayLane> laneOf,
            Dictionary<Vector2Int, int> masks)
        {
            if (!masks.TryGetValue(cell, out int m) || Side.Count(m) != 4) return false;
            HighwayLane lane = laneOf(cell);
            return lane.Active && lane.Heading != link && lane.Heading != Side.Opposite(link);
        }

        static bool IsOnAxis(int direction, bool vertical)
        {
            return vertical ? direction == Side.N || direction == Side.S : direction == Side.E || direction == Side.W;
        }

        /// <summary>Рамка вокруг выбранной клетки.</summary>
        public static void AppendSelection(RoadLayers L, Vector2Int cell, float cellSize)
        {
            List<Vector3> v = L.Verts[RoadLayers.Selection];
            List<int> t = L.Tris[RoadLayers.Selection];
            float x0 = cell.x * cellSize;
            float y0 = cell.y * cellSize;
            float x1 = x0 + cellSize;
            float y1 = y0 + cellSize;
            float w = cellSize * 0.06f;
            AddQuad(v, t, new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x0 + w, y1), new Vector2(x0 + w, y0));
            AddQuad(v, t, new Vector2(x1 - w, y0), new Vector2(x1 - w, y1), new Vector2(x1, y1), new Vector2(x1, y0));
            AddQuad(v, t, new Vector2(x0, y0), new Vector2(x0, y0 + w), new Vector2(x1, y0 + w), new Vector2(x1, y0));
            AddQuad(v, t, new Vector2(x0, y1 - w), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y1 - w));
        }

        /// <summary>Полоса шоссе: широкая, односторонняя, жёлтая разделительная со стороны второй полосы, стрелка направления.</summary>
        static bool IsArrowStart(Vector2Int cell, HighwayLane lane, Func<Vector2Int, HighwayLane> laneOf)
        {
            if (laneOf == null) return true;
            Vector2Int previous = cell - Side.Vec(lane.Heading);
            HighwayLane prior = laneOf(previous);
            if (!prior.Active || prior.Heading != lane.Heading || prior.OneWay != lane.OneWay) return true;
            return false;
        }

        static bool ShouldDrawStraightArrow(Vector2Int cell, HighwayLane lane,
            Func<Vector2Int, HighwayLane> laneOf)
        {
            if (IsArrowStart(cell, lane, laneOf)) return true;
            int axis = lane.Heading == Side.E || lane.Heading == Side.W ? cell.x : cell.y;
            return (axis & 1) == 0;
        }

        static bool IsTurnArrow(Vector2Int cell, int mask, HighwayLane lane,
            Func<Vector2Int, HighwayLane> laneOf)
        {
            if (laneOf == null) return true;
            int incomingSide = mask & ~lane.Heading;
            if (Side.Count(incomingSide) != 1) return false;
            HighwayLane prior = laneOf(cell + Side.Vec(incomingSide));
            return !prior.Active || prior.Heading != lane.Heading || prior.OneWay != lane.OneWay;
        }

        static void AppendHighwayNodeWidening(RoadLayers L, Vector2 c, Vector2Int cell, int mask,
            Func<Vector2Int, HighwayLane> laneOf, float size)
        {
            if (laneOf == null) return;
            float h = size * 0.5f;
            float hw = size * HighwayHalfWidth;
            if ((mask & (Side.E | Side.W)) == (Side.E | Side.W) &&
                (laneOf(cell + Vector2Int.right).Active || laneOf(cell + Vector2Int.left).Active))
                AddFrameRect(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c,
                    Vector2.right, Vector2.up, -h, h, -hw, hw);
            if ((mask & (Side.N | Side.S)) == (Side.N | Side.S) &&
                (laneOf(cell + Vector2Int.up).Active || laneOf(cell + Vector2Int.down).Active))
                AddFrameRect(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c,
                    Vector2.up, Vector2.left, -h, h, -hw, hw);
        }

        static void AppendHighwayLane(RoadLayers L, Vector2 c, HighwayLane lane, float cell, bool drawArrow)
        {
            float h = cell * 0.5f;
            float hw = cell * HighwayHalfWidth;
            Vector2 along = Side.Vec(lane.Heading);
            Vector2 across = new Vector2(-along.y, along.x);
            Vector2 partner = Side.Vec(lane.PartnerSide);
            float sign = Vector2.Dot(partner, across) > 0f ? 1f : -1f;

            AddFrameRect(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, along, across, -h, h, -hw, hw);

            // Разделитель между полосами лежит с обеих сторон границы клеток и вместе даёт сплошную жёлтую.
            if (lane.Median)
            {
                float b0 = sign > 0f ? hw : -h;
                float b1 = sign > 0f ? h : -hw;
                AddFrameRect(L.Verts[RoadLayers.Median], L.Tris[RoadLayers.Median], c, along, across, -h, h, b0, b1);
            }

            // Светлая кромка у внешнего края.
            float ew = cell * 0.025f;
            float eb = -sign * (hw - cell * 0.045f);
            AddFrameRect(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, along, across, -h, h,
                eb - ew * 0.5f, eb + ew * 0.5f);

            if (!drawArrow) return;

            // Одна стрелка в начале каждого прямого участка, чтобы разметка не превращалась в частокол.
            List<Vector3> v = L.Verts[RoadLayers.Marking];
            List<int> t = L.Tris[RoadLayers.Marking];
            Vector2 tip = c + along * (cell * 0.16f);
            Vector2 baseCenter = c - along * (cell * 0.10f);
            int i = v.Count;
            v.Add(tip);
            v.Add(baseCenter + across * (cell * 0.12f));
            v.Add(baseCenter - across * (cell * 0.12f));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        static void AppendHighwayEnd(RoadLayers L, Vector2 c, int mask, HighwayLane lane, float cell,
            bool drawArrow)
        {
            int side = mask;
            Vector2 roadDirection = Side.Vec(side);
            Vector2 along = Side.Vec(lane.Heading);
            Vector2 across = new Vector2(-along.y, along.x);
            float h = cell * 0.5f;
            float hw = cell * HighwayHalfWidth;
            float end = Vector2.Dot(roadDirection, along);
            float a0 = end > 0f ? 0f : -h;
            float a1 = end > 0f ? h : 0f;
            AddFrameRect(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, along, across,
                a0, a1, -hw, hw);
            if (lane.Median)
            {
                float sign = Vector2.Dot(Side.Vec(lane.PartnerSide), across) > 0f ? 1f : -1f;
                float b0 = sign > 0f ? hw : -h;
                float b1 = sign > 0f ? h : -hw;
                AddFrameRect(L.Verts[RoadLayers.Median], L.Tris[RoadLayers.Median], c, along, across,
                    a0, a1, b0, b1);
            }
            AddHighwayEdge(L, c, lane, cell, side, h, hw);
            if (drawArrow) AddHighwayArrow(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, along, cell);
        }

        /// <summary>Плавный четверть-круг для поворота полосы. Геометрия совпадает с дугой обычной дороги.</summary>
        static void AppendHighwayCorner(RoadLayers L, Vector2 c, int mask, HighwayLane lane, float cell,
            bool drawArrow)
        {
            float h = cell * 0.5f;
            float hw = cell * HighwayHalfWidth;
            float sx = (mask & Side.W) != 0 ? -1f : 1f;
            float sy = (mask & Side.S) != 0 ? -1f : 1f;
            Vector2 k = c + new Vector2(sx * h, sy * h);
            float a0 = Mathf.Atan2(-sy, 0f);
            float a1 = Mathf.Atan2(0f, -sx);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            // Полотно шоссе.
            AddCornerRing(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, mask, h, hw);

            const int segments = 10;
            if (lane.Median)
                AddArcBand(L.Verts[RoadLayers.Median], L.Tris[RoadLayers.Median], k, h, cell * 0.035f,
                    a0, delta, segments, 0.08f, 0.92f);
            AddArcBand(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], k, h + hw - cell * 0.035f,
                cell * 0.025f, a0, delta, segments, 0f, 1f);

            if (drawArrow) AppendCornerArrow(L, c, mask, lane, cell, 0.16f, 0.12f);
        }

        static void AddArcBand(List<Vector3> v, List<int> t, Vector2 center, float radius, float width,
            float a0, float delta, int segments, float start, float end)
        {
            Vector2 prevA = default, prevB = default;
            for (int i = 0; i <= segments; i++)
            {
                float f = Mathf.Lerp(start, end, i / (float)segments);
                float a = a0 + delta * f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 p0 = center + dir * (radius - width * 0.5f);
                Vector2 p1 = center + dir * (radius + width * 0.5f);
                if (i > 0) AddQuad(v, t, prevA, prevB, p1, p0);
                prevA = p0;
                prevB = p1;
            }
        }

        static void AppendCornerArrow(RoadLayers L, Vector2 c, int mask, HighwayLane lane, float cell,
            float length, float width)
        {
            float h = cell * 0.5f;
            float sx = (mask & Side.W) != 0 ? -1f : 1f;
            float sy = (mask & Side.S) != 0 ? -1f : 1f;
            Vector2 k = c + new Vector2(sx * h, sy * h);
            float a0 = Mathf.Atan2(-sy, 0f);
            float a1 = Mathf.Atan2(0f, -sx);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float am = a0 + delta * 0.58f;
            Vector2 radial = new Vector2(Mathf.Cos(am), Mathf.Sin(am));
            Vector2 tangent = new Vector2(-radial.y, radial.x) * Mathf.Sign(delta);
            if (Vector2.Dot(tangent, Side.Vec(lane.Heading)) < 0f) tangent = -tangent;
            Vector2 across = new Vector2(-tangent.y, tangent.x);
            Vector2 point = k + radial * h;
            List<Vector3> v = L.Verts[RoadLayers.Marking];
            List<int> t = L.Tris[RoadLayers.Marking];
            int i = v.Count;
            v.Add(point + tangent * (cell * length * 0.62f));
            v.Add(point - tangent * (cell * length * 0.38f) + across * (cell * width * 0.5f));
            v.Add(point - tangent * (cell * length * 0.38f) - across * (cell * width * 0.5f));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        static void AppendRampArrow(RoadLayers L, Vector2 c, HighwayLane lane, float cell)
        {
            Vector2 forward = Side.Vec(lane.Heading);
            Vector2 p = c;
            Vector2 across = new Vector2(-forward.y, forward.x);
            List<Vector3> v = L.Verts[RoadLayers.Marking];
            List<int> t = L.Tris[RoadLayers.Marking];
            int i = v.Count;
            v.Add(p + forward * (cell * 0.12f));
            v.Add(p - forward * (cell * 0.09f) + across * (cell * 0.10f));
            v.Add(p - forward * (cell * 0.09f) - across * (cell * 0.10f));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public static void AppendCell(RoadLayers L, Vector2 c, int mask, JunctionType type, float cell, float width,
            bool suppressCenterMarkings = false, HighwayLane bridgeLane = default,
            HighwayLane underLane = default, bool drawBridgeArrow = false, bool drawUnderArrow = false)
        {
            float h = cell * 0.5f;
            float hw = width * 0.5f;
            int count = Side.Count(mask);
            List<Vector3> v = L.Verts[RoadLayers.Road];
            List<int> t = L.Tris[RoadLayers.Road];

            if (count == 0)
            {
                AddDisc(v, t, c, hw);
                return;
            }

            bool corner = count == 2 && (mask == (Side.N | Side.E) || mask == (Side.E | Side.S) ||
                                         mask == (Side.S | Side.W) || mask == (Side.W | Side.N));
            if (corner)
            {
                AddCornerRing(v, t, c, mask, h, hw);
                if (!suppressCenterMarkings)
                    AddCornerDash(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, mask, h, cell * MarkingWidth);
                return;
            }

            if (type == JunctionType.HighwayRoundabout && count >= 3)
            {
                AddHighwayRoundabout(L, c, cell);
                return;
            }

            if (count == 4 && JunctionSpecs.IsOverpass(type))
            {
                AddOverpass(L, c, h, hw, type == JunctionType.OverpassVertical, cell,
                    bridgeLane, underLane, drawBridgeArrow, drawUnderArrow);
                return;
            }

            foreach (int side in Side.All)
            {
                if ((mask & side) == 0) continue;
                AddArm(v, t, c, side, h, hw);

                // Прерывистая осевая между направлениями. В развилках разметки нет.
                if (count <= 2 && !suppressCenterMarkings)
                    AddDash(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, side, h, cell * MarkingWidth);
            }

            if (count == 1)
            {
                AddDisc(v, t, c, hw);
                return;
            }

            if (count < 3) return;

            if (type == JunctionType.Roundabout)
            {
                AddDisc(v, t, c, cell * RoundaboutOuter);
                AddDisc(L.Verts[RoadLayers.Island], L.Tris[RoadLayers.Island], c, cell * RoundaboutIsland);
                return;
            }

            // Обычный перекрёсток: центр и скруглённые внутренние углы между соседними рукавами.
            AddQuad(v, t, c + new Vector2(-hw, -hw), c + new Vector2(-hw, hw), c + new Vector2(hw, hw),
                c + new Vector2(hw, -hw));

            float r = cell * FilletFraction;
            bool n = (mask & Side.N) != 0, e = (mask & Side.E) != 0, s = (mask & Side.S) != 0, w = (mask & Side.W) != 0;
            if (n && e) AddFillet(v, t, c, 1f, 1f, hw, r);
            if (e && s) AddFillet(v, t, c, 1f, -1f, hw, r);
            if (s && w) AddFillet(v, t, c, -1f, -1f, hw, r);
            if (w && n) AddFillet(v, t, c, -1f, 1f, hw, r);
        }

        /// <summary>Штрих осевой: на каждом рукаве от 20% до 80% его длины. Две клетки подряд дают ровный пунктир.</summary>
        static void AddDash(List<Vector3> v, List<int> t, Vector2 c, int side, float h, float lineWidth)
        {
            Vector2 d = Side.Vec(side);
            Vector2 p = new Vector2(-d.y, d.x) * (lineWidth * 0.5f);
            Vector2 a = c + d * (h * 0.2f);
            Vector2 b = c + d * (h * 0.8f);
            AddQuad(v, t, a - p, a + p, b + p, b - p);
        }

        /// <summary>Штрих осевой в угловой клетке: середина дуги.</summary>
        static void AddCornerDash(List<Vector3> v, List<int> t, Vector2 c, int mask, float h, float lineWidth)
        {
            float sx = (mask & Side.W) != 0 ? -1f : 1f;
            float sy = (mask & Side.S) != 0 ? -1f : 1f;
            Vector2 k = c + new Vector2(sx * h, sy * h);
            float a0 = Mathf.Atan2(-sy, 0f);
            float a1 = Mathf.Atan2(0f, -sx);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            const int segments = 4;
            float rIn = h - lineWidth * 0.5f;
            float rOut = h + lineWidth * 0.5f;
            Vector2 prevIn = default, prevOut = default;
            for (int i = 0; i <= segments; i++)
            {
                float a = a0 + delta * (0.18f + 0.64f * i / segments);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 pin = k + dir * rIn;
                Vector2 pout = k + dir * rOut;
                if (i > 0) AddQuad(v, t, prevIn, prevOut, pout, pin);
                prevIn = pin;
                prevOut = pout;
            }
        }

        static void AddArm(List<Vector3> v, List<int> t, Vector2 c, int side, float h, float hw)
        {
            Vector2 d = Side.Vec(side);
            Vector2 p = new Vector2(-d.y, d.x); // поперёк рукава
            Vector2 a = c - p * hw;
            Vector2 b = c + p * hw;
            Vector2 e = d * h;
            AddQuad(v, t, a, b, b + e, a + e);
        }

        /// <summary>
        /// Мост. Верхняя дорога идёт по оси along (запад-восток или север-юг), нижняя поперёк.
        /// На нижней дороге по бокам от полотна лежит тень, на верхней бортики и осевая.
        /// </summary>
        static void AddOverpass(RoadLayers L, Vector2 c, float h, float hw, bool vertical, float cell,
            HighwayLane bridgeLane, HighwayLane underLane, bool drawBridgeArrow, bool drawUnderArrow)
        {
            float lw = h * 2f * MarkingWidth;
            Vector2 along = vertical ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            Vector2 across = new Vector2(-along.y, along.x);
            int topA = vertical ? Side.N : Side.E;
            int topB = vertical ? Side.S : Side.W;
            int lowA = vertical ? Side.E : Side.N;
            int lowB = vertical ? Side.W : Side.S;
            bool fastBridge = bridgeLane.Active && !bridgeLane.OneWay;
            bool fastUnder = underLane.Active && !underLane.OneWay;
            float bridgeWidth = fastBridge ? cell * HighwayHalfWidth : hw;
            float underWidth = fastUnder ? cell * HighwayHalfWidth : hw;

            AddArm(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, lowA, h, underWidth);
            AddArm(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, lowB, h, underWidth);
            if (fastUnder)
            {
                AddHighwayEdge(L, c, underLane, cell, lowA, h, underWidth);
                AddHighwayEdge(L, c, underLane, cell, lowB, h, underWidth);
                if (drawUnderArrow) AddHighwayArrow(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c,
                    Side.Vec(underLane.Heading), cell);
            }
            else
            {
                AddDash(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, lowA, h, lw);
                AddDash(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, lowB, h, lw);
                if (underLane.Active && underLane.OneWay && drawUnderArrow)
                    AddHighwayArrow(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c,
                        Side.Vec(underLane.Heading), cell);
            }

            // Тень лежит только на нижней дороге по бокам от полотна.
            float sh = bridgeWidth + ShadowExtra * cell;
            AddFrameRect(L.Verts[RoadLayers.Shadow], L.Tris[RoadLayers.Shadow], c, along, across,
                -bridgeWidth, bridgeWidth, -sh, sh);

            // Полотно, осевая и бортики, чтобы мост читался как мост.
            AddFrameRect(L.Verts[RoadLayers.Bridge], L.Tris[RoadLayers.Bridge], c, along, across,
                -h, h, -bridgeWidth, bridgeWidth);
            if (!fastBridge)
            {
                AddDash(L.Verts[RoadLayers.BridgeMarking], L.Tris[RoadLayers.BridgeMarking], c, topA, h, lw);
                AddDash(L.Verts[RoadLayers.BridgeMarking], L.Tris[RoadLayers.BridgeMarking], c, topB, h, lw);
            }

            float edge = bridgeWidth - lw * 0.9f;
            float ew = lw * 0.7f;
            List<Vector3> bv = L.Verts[RoadLayers.BridgeMarking];
            List<int> bt = L.Tris[RoadLayers.BridgeMarking];
            AddFrameRect(bv, bt, c, along, across, -h, h, edge - ew * 0.5f, edge + ew * 0.5f);
            AddFrameRect(bv, bt, c, along, across, -h, h, -edge - ew * 0.5f, -edge + ew * 0.5f);

            if (fastBridge)
            {
                Vector2 routeAlong = Side.Vec(bridgeLane.Heading);
                Vector2 routeAcross = new Vector2(-routeAlong.y, routeAlong.x);
                Vector2 partner = Side.Vec(bridgeLane.PartnerSide);
                float sign = Vector2.Dot(partner, routeAcross) > 0f ? 1f : -1f;
                if (bridgeLane.Median)
                {
                    float b0 = sign > 0f ? bridgeWidth : -h;
                    float b1 = sign > 0f ? h : -bridgeWidth;
                    AddFrameRect(L.Verts[RoadLayers.Median], L.Tris[RoadLayers.Median], c,
                        routeAlong, routeAcross, -h, h, b0, b1);
                }
                if (drawBridgeArrow)
                    AddHighwayArrow(bv, bt, c, routeAlong, cell);
            }
            else if (bridgeLane.Active && bridgeLane.OneWay && drawBridgeArrow)
            {
                AddHighwayArrow(bv, bt, c, Side.Vec(bridgeLane.Heading), cell);
            }
        }

        static void AddHighwayRoundabout(RoadLayers L, Vector2 c, float cell)
        {
            float outer = cell * HighwayRoundaboutOuter;
            float island = cell * HighwayRoundaboutIsland;
            AddDisc(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, outer);
            AddDisc(L.Verts[RoadLayers.Island], L.Tris[RoadLayers.Island], c, island);

            // Кромки подчёркивают крупный радиус и визуально отделяют кольцо от подъездов.
            const int segments = 40;
            float line = cell * 0.022f;
            AddArcBand(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c,
                outer - line * 1.2f, line, 0f, Mathf.PI * 2f, segments, 0f, 1f);
            AddArcBand(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c,
                island + line * 1.2f, line, 0f, Mathf.PI * 2f, segments, 0f, 1f);

            // Четыре небольшие стрелки показывают круговое движение, не загромождая полотно.
            for (int i = 0; i < 4; i++)
            {
                float a = -Mathf.PI * 0.5f + i * Mathf.PI * 0.5f;
                Vector2 radial = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 tangent = new Vector2(-radial.y, radial.x);
                Vector2 p = c + radial * (outer + island) * 0.5f;
                Vector2 across = new Vector2(-tangent.y, tangent.x);
                List<Vector3> v = L.Verts[RoadLayers.Marking];
                List<int> t = L.Tris[RoadLayers.Marking];
                int start = v.Count;
                v.Add(p + tangent * (cell * 0.10f));
                v.Add(p - tangent * (cell * 0.07f) + across * (cell * 0.07f));
                v.Add(p - tangent * (cell * 0.07f) - across * (cell * 0.07f));
                t.Add(start); t.Add(start + 1); t.Add(start + 2);
            }
        }

        static void AddHighwayEdge(RoadLayers L, Vector2 c, HighwayLane lane, float cell, int side, float h,
            float width)
        {
            Vector2 along = Side.Vec(lane.Heading);
            Vector2 across = new Vector2(-along.y, along.x);
            float sign = Vector2.Dot(Side.Vec(lane.PartnerSide), across) > 0f ? 1f : -1f;
            float edge = -sign * (width - cell * 0.045f);
            float ew = cell * 0.025f;
            float end = Vector2.Dot(Side.Vec(side), along);
            float a0 = end > 0f ? 0f : -h;
            float a1 = end > 0f ? h : 0f;
            AddFrameRect(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, along, across,
                a0, a1, edge - ew * 0.5f, edge + ew * 0.5f);
        }

        static void AddHighwayArrow(List<Vector3> v, List<int> t, Vector2 c, Vector2 along, float cell)
        {
            Vector2 across = new Vector2(-along.y, along.x);
            int i = v.Count;
            v.Add(c + along * (cell * 0.16f));
            v.Add(c - along * (cell * 0.10f) + across * (cell * 0.12f));
            v.Add(c - along * (cell * 0.10f) - across * (cell * 0.12f));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        /// <summary>Прямоугольник в локальной системе клетки: a0..a1 вдоль оси along, b0..b1 поперёк.</summary>
        static void AddFrameRect(List<Vector3> v, List<int> t, Vector2 c, Vector2 along, Vector2 across,
            float a0, float a1, float b0, float b1)
        {
            AddQuad(v, t, c + along * a0 + across * b0, c + along * a0 + across * b1,
                c + along * a1 + across * b1, c + along * a1 + across * b0);
        }

        /// <summary>Скругление внутреннего угла между двумя рукавами. sx, sy: в какой четверти от центра лежит угол.</summary>
        static void AddFillet(List<Vector3> v, List<int> t, Vector2 c, float sx, float sy, float hw, float r)
        {
            Vector2 p = c + new Vector2(sx * hw, sy * hw);
            Vector2 o = p + new Vector2(sx * r, sy * r);
            float a0 = Mathf.Atan2(-sy, 0f);
            float a1 = Mathf.Atan2(0f, -sx);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            const int segments = 5;
            int start = v.Count;
            v.Add(p);
            for (int i = 0; i <= segments; i++)
            {
                float a = a0 + delta * i / segments;
                v.Add(o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }

            for (int i = 0; i < segments; i++)
            {
                t.Add(start);
                t.Add(start + 1 + i);
                t.Add(start + 2 + i);
            }
        }

        static void AddCornerRing(List<Vector3> v, List<int> t, Vector2 c, int mask, float h, float hw)
        {
            float sx = (mask & Side.W) != 0 ? -1f : 1f;
            float sy = (mask & Side.S) != 0 ? -1f : 1f;
            Vector2 k = c + new Vector2(sx * h, sy * h); // угол клетки, вокруг него дуга

            float a0 = Mathf.Atan2(-sy, 0f);
            float a1 = Mathf.Atan2(0f, -sx);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            const int segments = 8;
            float rIn = h - hw;
            float rOut = h + hw;
            Vector2 prevIn = default, prevOut = default;
            for (int i = 0; i <= segments; i++)
            {
                float a = a0 + delta * i / segments;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 pin = k + dir * rIn;
                Vector2 pout = k + dir * rOut;
                if (i > 0) AddQuad(v, t, prevIn, prevOut, pout, pin);
                prevIn = pin;
                prevOut = pout;
            }
        }

        static void AddQuad(List<Vector3> v, List<int> t, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int i = v.Count;
            v.Add(a);
            v.Add(b);
            v.Add(c);
            v.Add(d);
            // Порядок обхода может быть любым: Sprites/Default рисует обе стороны.
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        static void AddDisc(List<Vector3> v, List<int> t, Vector2 c, float r)
        {
            const int segments = 20;
            int start = v.Count;
            v.Add(c);
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                v.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }

            for (int i = 0; i < segments; i++)
            {
                t.Add(start);
                t.Add(start + 1 + i);
                t.Add(start + 1 + (i + 1) % segments);
            }
        }
    }
}
