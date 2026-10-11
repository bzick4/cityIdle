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
            foreach (var kv in cells)
            {
                Vector2 c = new Vector2((kv.Key.x + 0.5f) * cellSize, (kv.Key.y + 0.5f) * cellSize);
                JunctionType type = typeOf != null ? typeOf(kv.Key) : JunctionType.Plain;
                HighwayLane lane = laneOf != null ? laneOf(kv.Key) : default;
                if (lane.Active) AppendHighwayLane(layers, c, lane, cellSize);
                else AppendCell(layers, c, kv.Value, type, cellSize, cellSize * widthFraction);
            }

            return layers;
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
        static void AppendHighwayLane(RoadLayers L, Vector2 c, HighwayLane lane, float cell)
        {
            float h = cell * 0.5f;
            float hw = cell * HighwayHalfWidth;
            Vector2 along = Side.Vec(lane.Heading);
            Vector2 across = new Vector2(-along.y, along.x);
            Vector2 partner = Side.Vec(lane.PartnerSide);
            float sign = Vector2.Dot(partner, across) > 0f ? 1f : -1f;

            AddFrameRect(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, along, across, -h, h, -hw, hw);

            // Разделитель между полосами лежит с обеих сторон границы клеток и вместе даёт сплошную жёлтую.
            float b0 = sign > 0f ? hw : -h;
            float b1 = sign > 0f ? h : -hw;
            AddFrameRect(L.Verts[RoadLayers.Median], L.Tris[RoadLayers.Median], c, along, across, -h, h, b0, b1);

            // Светлая кромка у внешнего края.
            float ew = cell * 0.025f;
            float eb = -sign * (hw - cell * 0.045f);
            AddFrameRect(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, along, across, -h, h,
                eb - ew * 0.5f, eb + ew * 0.5f);

            // Стрелка направления.
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

        public static void AppendCell(RoadLayers L, Vector2 c, int mask, JunctionType type, float cell, float width)
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
                AddCornerDash(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, mask, h, cell * MarkingWidth);
                return;
            }

            if (count == 4 && JunctionSpecs.IsOverpass(type))
            {
                AddOverpass(L, c, h, hw, type == JunctionType.OverpassVertical);
                return;
            }

            foreach (int side in Side.All)
            {
                if ((mask & side) == 0) continue;
                AddArm(v, t, c, side, h, hw);

                // Прерывистая осевая между направлениями. В развилках разметки нет.
                if (count <= 2)
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
        static void AddOverpass(RoadLayers L, Vector2 c, float h, float hw, bool vertical)
        {
            float lw = h * 2f * MarkingWidth;
            Vector2 along = vertical ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            Vector2 across = new Vector2(-along.y, along.x);
            int topA = vertical ? Side.N : Side.E;
            int topB = vertical ? Side.S : Side.W;
            int lowA = vertical ? Side.E : Side.N;
            int lowB = vertical ? Side.W : Side.S;

            AddArm(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, lowA, h, hw);
            AddArm(L.Verts[RoadLayers.Road], L.Tris[RoadLayers.Road], c, lowB, h, hw);
            AddDash(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, lowA, h, lw);
            AddDash(L.Verts[RoadLayers.Marking], L.Tris[RoadLayers.Marking], c, lowB, h, lw);

            // Тень лежит только на нижней дороге по бокам от полотна.
            float sh = hw + ShadowExtra * h * 2f;
            AddFrameRect(L.Verts[RoadLayers.Shadow], L.Tris[RoadLayers.Shadow], c, along, across, -hw, hw, -sh, sh);

            // Полотно, осевая и бортики, чтобы мост читался как мост.
            AddFrameRect(L.Verts[RoadLayers.Bridge], L.Tris[RoadLayers.Bridge], c, along, across, -h, h, -hw, hw);
            AddDash(L.Verts[RoadLayers.BridgeMarking], L.Tris[RoadLayers.BridgeMarking], c, topA, h, lw);
            AddDash(L.Verts[RoadLayers.BridgeMarking], L.Tris[RoadLayers.BridgeMarking], c, topB, h, lw);

            float edge = hw - lw * 0.9f;
            float ew = lw * 0.7f;
            List<Vector3> bv = L.Verts[RoadLayers.BridgeMarking];
            List<int> bt = L.Tris[RoadLayers.BridgeMarking];
            AddFrameRect(bv, bt, c, along, across, -h, h, edge - ew * 0.5f, edge + ew * 0.5f);
            AddFrameRect(bv, bt, c, along, across, -h, h, -edge - ew * 0.5f, -edge + ew * 0.5f);
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
