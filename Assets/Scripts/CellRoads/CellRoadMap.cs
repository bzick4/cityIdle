using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>Стороны клетки как биты: N/E/S/W + диагонали NE/SE/SW/NW.</summary>
    public static class Side
    {
        public const int N = 1, E = 2, S = 4, W = 8;
        public const int NE = 16, SE = 32, SW = 64, NW = 128;
        public const int DiagonalMask = NE | SE | SW | NW;
        public static readonly int[] All = { N, E, S, W, NE, SE, SW, NW };
        public static readonly int[] Cardinal = { N, E, S, W };

        public static bool IsDiagonal(int side) => (side & DiagonalMask) != 0;

        public static Vector2Int Vec(int side)
        {
            switch (side)
            {
                case N:  return new Vector2Int(0, 1);
                case E:  return new Vector2Int(1, 0);
                case S:  return new Vector2Int(0, -1);
                case W:  return new Vector2Int(-1, 0);
                case NE: return new Vector2Int(1, 1);
                case SE: return new Vector2Int(1, -1);
                case SW: return new Vector2Int(-1, -1);
                case NW: return new Vector2Int(-1, 1);
                default: return Vector2Int.zero;
            }
        }

        public static int Opposite(int side)
        {
            switch (side)
            {
                case N:  return S;
                case S:  return N;
                case E:  return W;
                case W:  return E;
                case NE: return SW;
                case SW: return NE;
                case SE: return NW;
                case NW: return SE;
                default: return 0;
            }
        }

        /// <summary>Сторона по смещению на одну клетку (4 кардинальных + 4 диагональных), 0 если не сосед.</summary>
        public static int FromDelta(Vector2Int d)
        {
            if (d == new Vector2Int(0, 1))   return N;
            if (d == new Vector2Int(1, 0))   return E;
            if (d == new Vector2Int(0, -1))  return S;
            if (d == new Vector2Int(-1, 0))  return W;
            if (d == new Vector2Int(1, 1))   return NE;
            if (d == new Vector2Int(1, -1))  return SE;
            if (d == new Vector2Int(-1, -1)) return SW;
            if (d == new Vector2Int(-1, 1))  return NW;
            return 0;
        }

        public static int Count(int mask)
        {
            int n = 0;
            foreach (int s in All)
                if ((mask & s) != 0) n++;
            return n;
        }
    }

    /// <summary>
    /// Дороги по клеткам. Одна клетка это один участок дороги. У клетки есть маска связей со
    /// сторонами: связь создаёт нарисованный путь, поэтому параллельные соседние дороги не склеиваются.
    /// </summary>
    public sealed class CellRoadMap
    {
        readonly Dictionary<Vector2Int, int> _masks = new Dictionary<Vector2Int, int>();
        readonly Dictionary<Vector2Int, JunctionType> _types = new Dictionary<Vector2Int, JunctionType>();
        // Явные направленные участки трассы. Ключ — клетка, значение — направление движения.
        readonly Dictionary<Vector2Int, int> _highwayDirections = new Dictionary<Vector2Int, int>();
        readonly HashSet<Vector2Int> _rampCells = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> _standaloneHighwayCells = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> _ownedHighwayCells = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, int> _standaloneOriginalMasks = new Dictionary<Vector2Int, int>();

        public event Action Changed;

        bool _dry;
        void RaiseChanged()
        {
            if (!_dry) Changed?.Invoke();
        }

        /// <summary>
        /// Пробная постройка шоссе или односторонней дороги: проверяет, получится ли, и откатывает всё назад.
        /// События не вызываются, карта не меняется.
        /// </summary>
        public bool DryRunPath(IReadOnlyList<Vector2Int> path, bool oneWay)
        {
            var masks = new Dictionary<Vector2Int, int>(_masks);
            var types = new Dictionary<Vector2Int, JunctionType>(_types);
            var dirs = new Dictionary<Vector2Int, int>(_highwayDirections);
            var ramps = new HashSet<Vector2Int>(_rampCells);
            var standalone = new HashSet<Vector2Int>(_standaloneHighwayCells);
            var owned = new HashSet<Vector2Int>(_ownedHighwayCells);
            var original = new Dictionary<Vector2Int, int>(_standaloneOriginalMasks);
            bool flipped = LastJoinFlipped, fork = LastForkBuilt, reversed = LastForkReversed;
            _dry = true;
            bool ok;
            try
            {
                ok = oneWay ? SetOneWayPath(path) : SetHighwayPath(path, true);
            }
            finally
            {
                _dry = false;
                Replace(_masks, masks);
                Replace(_types, types);
                Replace(_highwayDirections, dirs);
                Replace(_rampCells, ramps);
                Replace(_standaloneHighwayCells, standalone);
                Replace(_ownedHighwayCells, owned);
                Replace(_standaloneOriginalMasks, original);
                LastJoinFlipped = flipped;
                LastForkBuilt = fork;
                LastForkReversed = reversed;
            }

            return ok;
        }

        static void Replace<K, V>(Dictionary<K, V> target, Dictionary<K, V> source)
        {
            target.Clear();
            foreach (var kv in source) target.Add(kv.Key, kv.Value);
        }

        static void Replace<K>(HashSet<K> target, HashSet<K> source)
        {
            target.Clear();
            foreach (K k in source) target.Add(k);
        }

        /// <summary>Меняется только то, что рисуется (выбор или предпросмотр). Расчёт потоков на это не реагирует.</summary>
        public event Action PreviewChanged;

        /// <summary>Выбранная развилка и тип, который на ней показан до покупки.</summary>
        public Vector2Int? PreviewCell { get; private set; }
        public JunctionType PreviewType { get; private set; }

        public void SetPreview(Vector2Int cell, JunctionType type)
        {
            PreviewCell = cell;
            PreviewType = type;
            PreviewChanged?.Invoke();
        }

        public void ClearPreview()
        {
            if (!PreviewCell.HasValue) return;
            PreviewCell = null;
            PreviewChanged?.Invoke();
        }

        /// <summary>Тип для отрисовки: на выбранной клетке показывается предпросмотр.</summary>
        public JunctionType DisplayTypeAt(Vector2Int cell)
        {
            if (PreviewCell.HasValue && IsValid(PreviewCell.Value, PreviewType))
            {
                Vector2Int selected = PreviewCell.Value;
                if (selected == cell) return PreviewType;

                JunctionType current = TypeAt(selected);
                if (JunctionSpecs.IsOverpass(current) &&
                    OverpassCompanionAt(selected, current) == cell && !JunctionSpecs.IsOverpass(PreviewType))
                    return JunctionType.Plain;

                if (JunctionSpecs.IsOverpass(PreviewType) &&
                    OverpassCompanionAt(selected, PreviewType) == cell && IsValid(cell, PreviewType))
                    return PreviewType;
            }
            return TypeAt(cell);
        }

        public int Count => _masks.Count;
        public IEnumerable<KeyValuePair<Vector2Int, int>> Cells => _masks;
        public IEnumerable<KeyValuePair<Vector2Int, int>> HighwayDirections => _highwayDirections;
        public bool HasHighwayAt(Vector2Int cell) => _highwayDirections.ContainsKey(cell);
        public bool IsRampAt(Vector2Int cell) => _rampCells.Contains(cell);
        public bool IsStandaloneHighwayAt(Vector2Int cell) => _standaloneHighwayCells.Contains(cell);
        public bool IsOwnedHighwayAt(Vector2Int cell) => _ownedHighwayCells.Contains(cell);
        public int StandaloneOriginalMaskAt(Vector2Int cell) =>
            _standaloneOriginalMasks.TryGetValue(cell, out int mask) ? mask : 0;
        public IEnumerable<Vector2Int> RampCells => _rampCells;

        public bool Has(Vector2Int cell)
        {
            return _masks.ContainsKey(cell);
        }

        public int Mask(Vector2Int cell)
        {
            _masks.TryGetValue(cell, out int m);
            return m;
        }

        /// <summary>Тип развилки клетки. Если тип больше не подходит (убрали дорогу), считается обычным.</summary>
        public JunctionType TypeAt(Vector2Int cell)
        {
            if (!_types.TryGetValue(cell, out JunctionType t)) return JunctionType.Plain;
            return IsValid(cell, t) ? t : JunctionType.Plain;
        }

        public bool IsJunction(Vector2Int cell)
        {
            return Side.Count(Mask(cell)) >= 3;
        }

        bool IsValid(Vector2Int cell, JunctionType t)
        {
            int deg = Side.Count(Mask(cell));
            if (t == JunctionType.Roundabout || t == JunctionType.HighwayRoundabout) return deg >= 3;
            if (JunctionSpecs.IsOverpass(t)) return deg == 4;
            return true;
        }

        /// <summary>
        /// Шоссе. Прямая клетка входит в шоссе, если рядом (поперёк дороги) ровно одна такая же прямая клетка,
        /// и у неё соседка тоже ровно одна. Три и больше дорог подряд шоссе не образуют.
        /// Движение правостороннее: по восточной полосе едут на север, по западной на юг,
        /// по южной на восток, по северной на запад.
        /// </summary>
        public HighwayLane LaneAt(Vector2Int cell)
        {
            if (_highwayDirections.TryGetValue(cell, out int heading) && IsDirectionConnected(cell, heading))
            {
                int left = LeftOf(heading);
                Vector2Int mate = cell + Side.Vec(left);
                bool median = _highwayDirections.TryGetValue(mate, out int mateHeading) &&
                              mateHeading == Side.Opposite(heading) && IsDirectionConnected(mate, mateHeading);
                return new HighwayLane { Active = true, Manual = true, Heading = heading,
                    PartnerSide = left, Median = median, OneWay = _rampCells.Contains(cell),
                    Standalone = _standaloneHighwayCells.Contains(cell) };
            }

            if (!HighwaySpecs.Enabled) return default;
            int m = Mask(cell);
            bool horizontal = m == (Side.E | Side.W);
            bool vertical = m == (Side.N | Side.S);
            if (!horizontal && !vertical) return default;

            int a = horizontal ? Side.N : Side.E; // две стороны поперёк дороги
            int b = horizontal ? Side.S : Side.W;

            int count = 0;
            int partnerSide = 0;
            if (Mask(cell + Side.Vec(a)) == m) { count++; partnerSide = a; }
            if (Mask(cell + Side.Vec(b)) == m) { count++; partnerSide = b; }
            if (count != 1) return default;

            Vector2Int p = cell + Side.Vec(partnerSide);
            int pc = 0;
            if (Mask(p + Side.Vec(a)) == m) pc++;
            if (Mask(p + Side.Vec(b)) == m) pc++;
            if (pc != 1) return default;

            // Концевые прямые сегменты тоже считаются шоссе. Оставляем обычную клетку только
            // перед поворотом; к узлу полотно может доходить, его геометрия расширяется отдельно.
            Vector2Int along = horizontal ? Vector2Int.right : Vector2Int.up;
            if (!IsStraightPairOrJunction(cell - along, m, partnerSide) ||
                !IsStraightPairOrJunction(cell + along, m, partnerSide)) return default;

            int inferredHeading;
            if (horizontal) inferredHeading = partnerSide == Side.N ? Side.E : Side.W;
            else inferredHeading = partnerSide == Side.E ? Side.S : Side.N;

            return new HighwayLane { Active = true, Heading = inferredHeading, PartnerSide = partnerSide, Median = true };
        }

        /// <summary>
        /// Закрепляет автоматически распознанные парные дороги как шоссе до изменения сети,
        /// чтобы добавление или удаление соседнего сегмента не меняло их тип обратно.
        /// </summary>
        void LockExistingHighways()
        {
            var cells = new List<Vector2Int>(_masks.Keys);
            foreach (Vector2Int cell in cells)
            {
                if (_highwayDirections.ContainsKey(cell)) continue;
                HighwayLane lane = LaneAt(cell);
                if (!lane.Active) continue;
                Vector2Int partner = cell + Side.Vec(lane.PartnerSide);
                if (!_masks.ContainsKey(partner)) continue;
                _highwayDirections[cell] = lane.Heading;
                _highwayDirections[partner] = Side.Opposite(lane.Heading);
            }
        }

        static int LeftOf(int heading)
        {
            switch (heading)
            {
                case Side.N: return Side.W;
                case Side.W: return Side.S;
                case Side.S: return Side.E;
                default: return Side.N;
            }
        }

        bool IsDirectionConnected(Vector2Int cell, int heading)
        {
            // На последней клетке маршрута связанная дорога находится позади направления движения.
            int connected = heading | Side.Opposite(heading);
            return _masks.TryGetValue(cell, out int mask) && (mask & connected) != 0;
        }

        static int EndpointRouteSign(HighwayLane lane, int mask)
        {
            if (!lane.Active || Side.Count(mask) != 1) return 0;
            // Если единственная связь указывает по направлению движения, это начало полосы;
            // если против направления — её конец.
            return (mask & lane.Heading) != 0 ? -1 : 1;
        }

        /// <summary>Строит отдельное полотно шоссе на пустых клетках или поверх обычной дороги.</summary>
        /// <summary>Кольцо: маршрут возвращается в свою первую клетку (как МКАД).</summary>
        public static bool IsClosedPath(IReadOnlyList<Vector2Int> path)
        {
            return path != null && path.Count >= 5 && path[0] == path[path.Count - 1];
        }

        /// <summary>Последнее соединение двух концов развернуло одну из дорог, чтобы потоки сошлись.</summary>
        public bool LastJoinFlipped { get; private set; }

        /// <summary>
        /// Цепочка клеток направленной дороги от свободного конца до другого свободного конца.
        /// Null, если на пути развилка, обрыв, смена типа дороги или кольцо: такую цепочку не разворачиваем.
        /// </summary>
        List<Vector2Int> LaneChainFrom(Vector2Int end, bool oneWay)
        {
            var chain = new List<Vector2Int>();
            Vector2Int cur = end;
            int guard = _masks.Count + 2;
            while (guard-- > 0)
            {
                HighwayLane lane = LaneAt(cur);
                if (!lane.Active || !lane.Manual || lane.OneWay != oneWay) return null;
                if (chain.Contains(cur)) return null;
                chain.Add(cur);
                int mask = Mask(cur);
                int count = Side.Count(mask);
                if (chain.Count == 1)
                {
                    if (count != 1) return null;
                }
                else if (count == 1) return chain;
                else if (count != 2) return null;

                int back = chain.Count > 1 ? Side.FromDelta(chain[chain.Count - 2] - cur) : 0;
                int forward = mask & ~back;
                if (Side.Count(forward) != 1) return null;
                cur += Side.Vec(forward);
            }
            return null;
        }

        void ReverseChain(List<Vector2Int> chain)
        {
            foreach (Vector2Int cell in chain)
            {
                if (!_highwayDirections.TryGetValue(cell, out int heading)) continue;
                int mask = Mask(cell);
                // Поворот: новый выезд это сторона, откуда раньше въезжали; на прямой и на концах это просто обратная сторона.
                _highwayDirections[cell] = Side.Count(mask) == 2 && (mask & heading) != 0 && (mask & Side.Opposite(heading)) == 0
                    ? mask & ~heading
                    : Side.Opposite(heading);
            }
        }

        /// <summary>
        /// Два готовых конца соединяют, но их потоки встречные (начало с началом или конец с концом).
        /// Разворачиваем более короткую из двух дорог целиком, чтобы потоки сошлись.
        /// </summary>
        void ReconcileEnds(IReadOnlyList<Vector2Int> path, bool oneWay)
        {
            Vector2Int a = path[0], b = path[path.Count - 1];
            HighwayLane first = LaneAt(a), last = LaneAt(b);
            if (!first.Active || !last.Active || first.OneWay != oneWay || last.OneWay != oneWay) return;
            int firstSign = EndpointRouteSign(first, Mask(a));
            int lastSign = -EndpointRouteSign(last, Mask(b));
            if (firstSign == 0 || lastSign == 0 || firstSign == lastSign) return;

            List<Vector2Int> chainA = LaneChainFrom(a, oneWay);
            List<Vector2Int> chainB = LaneChainFrom(b, oneWay);
            if (chainA == null && chainB == null) return;
            if (chainA != null && chainB != null && (chainA.Contains(b) || chainB.Contains(a))) return;
            List<Vector2Int> pick = chainA == null ? chainB : chainB == null ? chainA :
                chainA.Count < chainB.Count ? chainA : chainB;
            ReverseChain(pick);
            LastJoinFlipped = true;
        }

        static bool OnSameAxis(int a, int b)
        {
            return a == b || a == Side.Opposite(b);
        }

        /// <summary>
        /// Можно ли пройти сквозь клетку направленной дороги (шоссе или односторонней) прямой,
        /// идущей в сторону stepSide. Подходит прямая клетка без ответвлений, поперёк полосы.
        /// На такой клетке строится мост: новая дорога идёт поверх, соединения между дорогами нет.
        /// </summary>
        public bool CanCross(Vector2Int cell, int stepSide)
        {
            if (stepSide == 0) return false;
            HighwayLane lane = LaneAt(cell);
            if (!lane.Active) return false;
            int mask = Mask(cell);
            if (mask != (Side.N | Side.S) && mask != (Side.E | Side.W)) return false;
            return !OnSameAxis(lane.Heading, stepSide);
        }

        /// <summary>Клетка i пути — мост через чужую направленную полосу (путь идёт сквозь неё по прямой).</summary>
        public bool IsCrossing(IReadOnlyList<Vector2Int> path, int i)
        {
            if (path == null || i <= 0 || i + 1 >= path.Count) return false;
            int inSide = Side.FromDelta(path[i] - path[i - 1]);
            int outSide = Side.FromDelta(path[i + 1] - path[i]);
            return inSide != 0 && inSide == outSide && CanCross(path[i], inSide);
        }

        /// <summary>
        /// Полоса, по которой идёт проезд через клетку вдоль стороны side. В клетке моста собственная
        /// полоса клетки может принадлежать дороге другой оси: тогда берём полосу ближайшей клетки на этой оси.
        /// </summary>
        public HighwayLane LaneAlong(Vector2Int cell, int side)
        {
            HighwayLane own = LaneAt(cell);
            if (own.Active && OnSameAxis(own.Heading, side)) return own;
            if (Side.Count(Mask(cell)) != 4) return default(HighwayLane);
            for (int d = 0; d < 2; d++)
            {
                int dir = d == 0 ? side : Side.Opposite(side);
                Vector2Int c = cell;
                for (int k = 0; k < 8; k++)
                {
                    c += Side.Vec(dir);
                    if (!_masks.ContainsKey(c)) break;
                    HighwayLane lane = LaneAt(c);
                    if (lane.Active && OnSameAxis(lane.Heading, side)) return lane;
                    if (Side.Count(Mask(c)) != 4) break;
                }
            }

            return default(HighwayLane);
        }

        void MakeBridge(Vector2Int cell, int pathSide)
        {
            bool vertical = pathSide == Side.N || pathSide == Side.S;
            // Новая дорога идёт поверх старой: мост лежит вдоль новой дороги.
            _types[cell] = vertical ? JunctionType.OverpassVertical : JunctionType.Overpass;
        }

        public bool SetHighwayPath(IReadOnlyList<Vector2Int> path, bool standalone = true)
        {
            LastForkBuilt = false;
            var snapshot = new Dictionary<Vector2Int, int>(_highwayDirections);
            if (BuildHighwayPath(path, standalone)) return true;
            RestoreDirections(snapshot); // разворот встречной дороги не должен остаться при неудаче
            return false;
        }

        void RestoreDirections(Dictionary<Vector2Int, int> snapshot)
        {
            _highwayDirections.Clear();
            foreach (var kv in snapshot) _highwayDirections[kv.Key] = kv.Value;
            LastJoinFlipped = false;
        }

        bool BuildHighwayPath(IReadOnlyList<Vector2Int> path, bool standalone)
        {
            if (path == null || path.Count < 2) return false;
            bool closed = IsClosedPath(path);
            LockExistingHighways();
            var crossing = new bool[path.Count]; // клетки, где путь идёт мостом через чужую полосу
            for (int i = 0; i < path.Count; i++) crossing[i] = IsCrossing(path, i);
            LastJoinFlipped = false;
            if (!closed) ReconcileEnds(path, false);

            HighwayLane first = LaneAt(path[0]);
            HighwayLane last = LaneAt(path[path.Count - 1]);
            // Середина готового шоссе (две связи): к ней можно примкнуть боковым съездом, получится развилка.
            bool joinFirst = first.Active && !first.OneWay && Side.Count(Mask(path[0])) == 2;
            bool joinLast = last.Active && !last.OneWay && Side.Count(Mask(path[path.Count - 1])) == 2;
            bool extendFromFirst = first.Active && !first.OneWay && !joinFirst;
            bool extendFromLast = last.Active && !last.OneWay && !joinLast;
            if ((first.Active && !extendFromFirst && !joinFirst) || (last.Active && !extendFromLast && !joinLast)) return false;
            if (joinFirst)
            {
                int step = Side.FromDelta(path[1] - path[0]);
                if (step == 0 || (Mask(path[0]) & (step | Side.Opposite(step))) != 0) return false;
            }
            if (joinLast)
            {
                int conn = Side.FromDelta(path[path.Count - 2] - path[path.Count - 1]);
                if (conn == 0 || (Mask(path[path.Count - 1]) & (conn | Side.Opposite(conn))) != 0) return false;
            }
            int routeSign = 0;
            if (extendFromFirst)
            {
                int mask = Mask(path[0]);
                int firstSign = EndpointRouteSign(first, mask);
                int firstStep = Side.FromDelta(path[1] - path[0]);
                if (firstSign == 0 || firstStep == 0 || (mask & firstStep) != 0) return false;
                routeSign = firstSign;
            }
            if (extendFromLast)
            {
                int mask = Mask(path[path.Count - 1]);
                // Для конца маршрута знак обратный: к началу полосы (въезд) мы идём по потоку,
                // к её концу (выезд) идём против потока. Без этого два готовых конца не соединить.
                int lastSign = -EndpointRouteSign(last, mask);
                int lastConnectionSide = Side.FromDelta(path[path.Count - 2] - path[path.Count - 1]);
                if (lastSign == 0 || lastConnectionSide == 0 || (mask & lastConnectionSide) != 0) return false;
                if (routeSign != 0 && routeSign != lastSign) return false;
                routeSign = lastSign;
            }

            for (int i = 0; i < path.Count; i++)
            {
                HighwayLane lane = LaneAt(path[i]);
                bool endpoint = (i == 0 && (extendFromFirst || joinFirst)) ||
                                (i == path.Count - 1 && (extendFromLast || joinLast));
                if (lane.Active && !endpoint && !crossing[i]) return false;
                if (i + 1 < path.Count)
                {
                    int dir = Side.FromDelta(path[i + 1] - path[i]);
                    if (dir == 0) return false;
                }
            }

            // Шоссе — отдельный объект: на пустой клетке создаётся своё полотно,
            // на занятой клетке оно накладывается поверх обычной дороги.
            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int cell = path[i];
                if (crossing[i]) continue; // клетка остаётся чужой полосой, мы лишь проходим по мосту
                bool emptyCell = !_masks.ContainsKey(cell) || _masks[cell] == 0;
                if (!_masks.ContainsKey(cell)) _masks[cell] = 0;
                if (standalone)
                {
                    if (!_standaloneOriginalMasks.ContainsKey(cell))
                        _standaloneOriginalMasks[cell] = _masks[cell];
                    _standaloneHighwayCells.Add(cell);
                    if (emptyCell) _ownedHighwayCells.Add(cell);
                }
            }

            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int cell = path[i];
                if (i + 1 >= path.Count) continue;
                int side = Side.FromDelta(path[i + 1] - cell);
                _masks[cell] |= side;
                _masks[path[i + 1]] |= Side.Opposite(side);
            }

            for (int i = 0; i < path.Count; i++)
                if (crossing[i]) MakeBridge(path[i], Side.FromDelta(path[i + 1] - path[i]));

            // Готовый конец полосы, к которому мы пристроились по потоку, сохраняет своё направление:
            // это въезд (начало) другой полосы, поток дальше идёт по ней. Иначе при замыкании кольца
            // на повороте голова полосы разворачивалась бы по последнему шагу пути.
            bool keepFirstHeading = extendFromFirst && routeSign < 0;
            bool keepLastHeading = extendFromLast && routeSign > 0;
            for (int i = 0; i < path.Count; i++)
            {
                if (crossing[i]) continue; // у моста направление нижней полосы остаётся прежним
                if ((i == 0 && keepFirstHeading) || (i == path.Count - 1 && keepLastHeading)) continue;
                if (closed && i == path.Count - 1) continue; // замыкающая клетка уже получила направление как первая
                if ((i == 0 && joinFirst) || (i == path.Count - 1 && joinLast)) continue; // у шоссе, к которому примыкаем, направление своё
                int pathDirection = i + 1 < path.Count
                    ? Side.FromDelta(path[i + 1] - path[i])
                    : Side.FromDelta(path[i] - path[i - 1]);
                int heading = routeSign < 0 ? Side.Opposite(pathDirection) : pathDirection;
                _highwayDirections[path[i]] = heading;
                _rampCells.Remove(path[i]);
            }
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Строит одностороннюю дорогу обычной ширины. Её можно продолжить от свободного конца,
        /// соединить с шоссе или пересечь его; существующее направление полос сохраняется.
        /// </summary>
        public bool SetOneWayPath(IReadOnlyList<Vector2Int> path)
        {
            LastForkBuilt = false;
            LastForkReversed = false;
            if (TryBuildFork(path) || TryOverlapRegular(path)) return true;
            var snapshot = new Dictionary<Vector2Int, int>(_highwayDirections);
            if (BuildOneWayPath(path)) return true;
            RestoreDirections(snapshot);
            return false;
        }

        bool BuildOneWayPath(IReadOnlyList<Vector2Int> path)
        {
            if (path == null || path.Count < 2) return false;
            bool closed = IsClosedPath(path);
            LockExistingHighways();
            var crossing = new bool[path.Count]; // мосты через шоссе и другие односторонние дороги
            for (int i = 0; i < path.Count; i++) crossing[i] = IsCrossing(path, i);
            LastJoinFlipped = false;
            if (!closed) ReconcileEnds(path, true);

            bool hasRoadCell = false;
            var preserveDirection = new bool[path.Count]; // быстрые шоссе и обычные дороги сохраняют своё направление
            int routeSign = 0;
            HighwayLane firstLane = LaneAt(path[0]);
            HighwayLane lastLane = LaneAt(path[path.Count - 1]);
            // Свободный конец односторонней дороги продолжаем; к середине (повороту или прямой) только присоединяемся.
            bool extendFromFirst = firstLane.Active && firstLane.OneWay && Side.Count(Mask(path[0])) == 1;
            bool extendFromLast = lastLane.Active && lastLane.OneWay && Side.Count(Mask(path[path.Count - 1])) == 1;
            if (extendFromFirst)
            {
                int mask = Mask(path[0]);
                int firstSign = EndpointRouteSign(firstLane, mask);
                int firstStep = Side.FromDelta(path[1] - path[0]);
                if (firstSign == 0 || firstStep == 0 || (mask & firstStep) != 0) return false;
                routeSign = firstSign;
            }
            if (extendFromLast)
            {
                int mask = Mask(path[path.Count - 1]);
                int lastSign = -EndpointRouteSign(lastLane, mask); // см. SetHighwayPath: у конца маршрута знак обратный
                int lastConnectionSide = Side.FromDelta(path[path.Count - 2] - path[path.Count - 1]);
                if (lastSign == 0 || lastConnectionSide == 0 || (mask & lastConnectionSide) != 0) return false;
                if (routeSign != 0 && routeSign != lastSign) return false;
                routeSign = lastSign;
            }

            for (int i = 0; i < path.Count; i++)
            {
                if (i + 1 < path.Count && Side.FromDelta(path[i + 1] - path[i]) == 0)
                    return false;
                HighwayLane lane = LaneAt(path[i]);
                bool highway = lane.Active && !lane.OneWay;
                preserveDirection[i] = highway;
                if (crossing[i])
                {
                    preserveDirection[i] = true; // мост: нижняя дорога сохраняет своё направление
                    continue;
                }
                if (lane.Active && lane.OneWay)
                {
                    if (i != 0 && i != path.Count - 1) return false;
                    int laneLinks = Side.Count(Mask(path[i]));
                    if (laneLinks == 2)
                    {
                        // Въезд или съезд: присоединяемся к середине односторонней дороги с её свободной стороны.
                        int link = i == 0
                            ? Side.FromDelta(path[1] - path[0])
                            : Side.FromDelta(path[i - 1] - path[i]);
                        if (link == 0 || (Mask(path[i]) & link) != 0) return false;
                        preserveDirection[i] = true;
                        continue;
                    }
                    if (laneLinks != 1) return false;
                    continue;
                }
                // Шоссе в середине пути можно только пересечь мостом (прямо, поперёк полосы).
                if (highway && i > 0 && i + 1 < path.Count) return false;
                if (_masks.TryGetValue(path[i], out int existingMask) && existingMask != 0 && !highway)
                {
                    // Обычная двусторонняя дорога: односторонняя может её пересечь или примкнуть к ней
                    // (получится перекрёсток), но не может идти вдоль неё по тем же клеткам.
                    int inSide = i > 0 ? Side.FromDelta(path[i] - path[i - 1]) : 0;
                    int outSide = i + 1 < path.Count ? Side.FromDelta(path[i + 1] - path[i]) : 0;
                    if (i > 0 && i + 1 < path.Count && outSide != inSide) return false; // поворот прямо на дороге
                    int mine = 0;
                    if (inSide != 0) mine |= Side.Opposite(inSide);
                    if (outSide != 0) mine |= outSide;
                    if ((existingMask & mine) != 0) return false; // уже соединено: идём вдоль дороги
                    preserveDirection[i] = true;              // клетка остаётся обычной, направление не трогаем
                    continue;
                }

                if (!highway) hasRoadCell = true;

                if (i + 1 < path.Count && Side.FromDelta(path[i + 1] - path[i]) == 0)
                    return false;
            }
            if (!hasRoadCell) return false;

            for (int i = 0; i < path.Count; i++)
                if (!_masks.ContainsKey(path[i])) _masks[path[i]] = 0;

            for (int i = 0; i + 1 < path.Count; i++)
            {
                int side = Side.FromDelta(path[i + 1] - path[i]);
                _masks[path[i]] |= side;
                _masks[path[i + 1]] |= Side.Opposite(side);
            }

            for (int i = 0; i < path.Count; i++)
                if (crossing[i]) MakeBridge(path[i], Side.FromDelta(path[i + 1] - path[i]));

            if (extendFromFirst && routeSign < 0) preserveDirection[0] = true;
            if (extendFromLast && routeSign > 0) preserveDirection[path.Count - 1] = true;
            for (int i = 0; i < path.Count; i++)
            {
                if (closed && i == path.Count - 1) continue;
                if (preserveDirection[i]) continue; // не перезаписываем направление шоссе или обычной дороги
                int pathDirection = i + 1 < path.Count
                    ? Side.FromDelta(path[i + 1] - path[i])
                    : Side.FromDelta(path[i] - path[i - 1]);
                int heading = routeSign < 0 ? Side.Opposite(pathDirection) : pathDirection;
                _highwayDirections[path[i]] = heading;
                _rampCells.Add(path[i]);
            }

            RaiseChanged();
            return true;
        }

        // ---------- Развилка: две односторонние дороги из конца обычной дороги ----------

        /// <summary>Последний построенный путь был развилкой из двух односторонних дорог.</summary>
        public bool LastForkBuilt { get; private set; }

        /// <summary>Последняя развилка построена с разворотом: новая полоса пошла к корню, а не от него.</summary>
        public bool LastForkReversed { get; private set; }

        /// <summary>
        /// Корень развилки: клетка обычной дороги (конец дороги, её начало или перекрёсток),
        /// от которой отходит односторонняя дорога.
        /// </summary>
        bool IsForkRoot(Vector2Int cell)
        {
            return _masks.ContainsKey(cell) && !LaneAt(cell).Active && TypeAt(cell) == JunctionType.Plain;
        }

        /// <summary>Клетка обычной односторонней дороги без ответвлений (одна или две связи).</summary>
        bool IsChainCell(Vector2Int cell)
        {
            if (!_masks.TryGetValue(cell, out int m)) return false;
            int count = Side.Count(m);
            if (count < 1 || count > 2 || TypeAt(cell) != JunctionType.Plain) return false;
            HighwayLane lane = LaneAt(cell);
            return lane.Active && lane.OneWay && lane.Manual;
        }

        /// <summary>
        /// Односторонняя дорога, выходящая из корня root в сторону side: клетки от первой после корня и дальше.
        /// outward: движение по ней идёт от корня (иначе к нему).
        /// </summary>
        List<Vector2Int> ChainFrom(Vector2Int root, int side, out bool outward)
        {
            outward = false;
            if (side == 0 || !IsForkRoot(root) || (Mask(root) & side) == 0) return null;
            Vector2Int first = root + Side.Vec(side);
            if (!IsChainCell(first) || (Mask(first) & Side.Opposite(side)) == 0) return null;
            outward = LaneAt(first).Heading != Side.Opposite(side);
            var chain = new List<Vector2Int> { first };
            Vector2Int cur = first;
            int back = Side.Opposite(side);
            for (int guard = 0; guard < 64; guard++)
            {
                int fwd = Mask(cur) & ~back;
                if (Side.Count(fwd) != 1) break;
                Vector2Int next = cur + Side.Vec(fwd);
                if (!IsChainCell(next) || (Mask(next) & Side.Opposite(fwd)) == 0 || chain.Contains(next)) break;
                chain.Add(next);
                cur = next;
                back = Side.Opposite(fwd);
            }
            return chain;
        }

        /// <summary>
        /// Идём по односторонней дороге из клетки z в сторону firstSide до корня развилки.
        /// Возвращает клетки от z до корня включительно.
        /// </summary>
        List<Vector2Int> ChainToRoot(Vector2Int z, int firstSide, out bool outward)
        {
            outward = false;
            if (!IsChainCell(z) || (Mask(z) & firstSide) == 0) return null;
            var list = new List<Vector2Int> { z };
            Vector2Int cur = z;
            int side = firstSide;
            for (int guard = 0; guard < 64; guard++)
            {
                Vector2Int next = cur + Side.Vec(side);
                if (!_masks.ContainsKey(next)) return null;
                if (IsChainCell(next))
                {
                    if ((Mask(next) & Side.Opposite(side)) == 0 || list.Contains(next)) return null;
                    int fwd = Mask(next) & ~Side.Opposite(side);
                    list.Add(next);
                    if (Side.Count(fwd) != 1) return null;
                    cur = next;
                    side = fwd;
                    continue;
                }
                if (!IsForkRoot(next) || (Mask(next) & Side.Opposite(side)) == 0) return null;
                outward = LaneAt(cur).Heading != side;
                list.Add(next);
                return list;
            }
            return null;
        }

        /// <summary>
        /// Маршрут cells (возможно, ещё не законченный) выглядит как развилка: он идёт по готовой односторонней дороге
        /// от корня или к корню и расходится с ней по свободным клеткам. Нужен инструменту, чтобы разрешить шаг на чужую полосу.
        /// </summary>
        public bool IsForkPrefix(IReadOnlyList<Vector2Int> cells)
        {
            int n = cells.Count;
            if (n < 2) return false;

            // Маршрут начинается в корне и идёт по готовой дороге наружу.
            int s0 = Side.FromDelta(cells[1] - cells[0]);
            List<Vector2Int> chain = ChainFrom(cells[0], s0, out _);
            if (chain != null)
            {
                int d = 1;
                while (d < n && d - 1 < chain.Count && cells[d] == chain[d - 1]) d++;
                bool freeRest = d > 1;
                for (int j = d; j < n && freeRest; j++)
                    if (_masks.ContainsKey(cells[j])) freeRest = false;
                if (freeRest) return true;
            }

            // Маршрут целиком идёт по готовой дороге (две полосы в одной клетке).
            if (OverlapCells(cells) != null) return true;

            // Маршрут начинается на свободных клетках, встаёт на готовую дорогу и идёт по ней к корню.
            int a = 0;
            while (a < n && !_masks.ContainsKey(cells[a])) a++;
            if (a >= 1 && a < n)
            {
                Vector2Int z = cells[a];
                foreach (int s in Side.All)
                {
                    if ((Mask(z) & s) != 0) continue; // сторона, откуда пришли, должна быть свободной
                    if (Side.FromDelta(cells[a - 1] - z) != s) continue;
                    foreach (int t in Side.All)
                    {
                        if ((Mask(z) & t) == 0) continue;
                        List<Vector2Int> walk = ChainToRoot(z, t, out bool outward);
                        if (walk == null) continue;
                        int len = Math.Min(n - a, walk.Count);
                        bool match = true;
                        for (int j = 0; j < len; j++)
                            if (cells[a + j] != walk[j]) { match = false; break; }
                        if (match) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Вторая односторонняя дорога идёт по тем же клеткам, что и первая, от корня или к корню (обычной дороги).
        /// Общий участок превращается в обычную двухполосную дорогу, а в клетке, где пути расходятся,
        /// получается развилка: одна полоса уходит по одной дороге, другая приходит по другой.
        /// Работает с обоими концами обычной дороги.
        /// </summary>
        bool TryBuildFork(IReadOnlyList<Vector2Int> path)
        {
            if (path == null || path.Count < 3) return false;
            for (int pass = 0; pass < 2; pass++)
            {
                var o = new List<Vector2Int>(path);
                if (pass == 0) o.Reverse(); // o[0] — корень, дальше наружу; pass 0: палец шёл к корню
                int rootSide = Side.FromDelta(o[1] - o[0]);
                List<Vector2Int> chain = ChainFrom(o[0], rootSide, out bool existingOut);
                if (chain == null) continue;
                // Потоки на общем участке должны быть встречными. Если палец тянули от корня вдоль дороги,
                // которая тоже идёт от корня, новая полоса получает встречное направление.
                bool armFlowsOut = !existingOut;
                if (pass == 0 && !existingOut) continue; // палец шёл к корню, а готовая дорога тоже к корню

                int d = 1;
                while (d < o.Count && d - 1 < chain.Count && o[d] == chain[d - 1]) d++;
                if (d == 1 || d >= o.Count) continue; // нет общих клеток или нет ответвления

                bool freeArm = true;
                for (int j = d; j < o.Count; j++)
                    if (_masks.ContainsKey(o[j])) { freeArm = false; break; }
                if (!freeArm) continue;

                Vector2Int z = o[d - 1];
                int armSide = Side.FromDelta(o[d] - z);
                if (armSide == 0 || (Mask(z) & armSide) != 0) continue;

                // Общий участок, включая клетку расхождения, становится обычной двусторонней дорогой.
                for (int j = 1; j < d; j++)
                {
                    _highwayDirections.Remove(o[j]);
                    _rampCells.Remove(o[j]);
                }

                for (int j = d; j < o.Count; j++) _masks[o[j]] = 0;
                for (int j = d - 1; j + 1 < o.Count; j++)
                {
                    int side = Side.FromDelta(o[j + 1] - o[j]);
                    _masks[o[j]] |= side;
                    _masks[o[j + 1]] |= Side.Opposite(side);
                }

                for (int j = d; j < o.Count; j++)
                {
                    int toInner = Side.FromDelta(o[j - 1] - o[j]);
                    int toOuter = j + 1 < o.Count ? Side.FromDelta(o[j + 1] - o[j]) : Side.Opposite(toInner);
                    _highwayDirections[o[j]] = armFlowsOut ? toOuter : toInner;
                    _rampCells.Add(o[j]);
                }

                LastForkBuilt = true;
                LastForkReversed = pass == 1 && !armFlowsOut;
                RaiseChanged();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Клетки готовой односторонней дороги, по которым идёт маршрут целиком (от корня, к корню или вдоль дороги к корню).
        /// Null, если маршрут не лежит на такой дороге.
        /// </summary>
        List<Vector2Int> OverlapCells(IReadOnlyList<Vector2Int> path)
        {
            int n = path.Count;
            if (n < 2) return null;
            for (int pass = 0; pass < 2; pass++)
            {
                var o = new List<Vector2Int>(path);
                if (pass == 1) o.Reverse();
                int side = Side.FromDelta(o[1] - o[0]);

                // От корня наружу по готовой дороге.
                List<Vector2Int> chain = ChainFrom(o[0], side, out _);
                if (chain != null && n - 1 <= chain.Count)
                {
                    bool match = true;
                    for (int j = 1; j < n && match; j++) match = o[j] == chain[j - 1];
                    if (match) return new List<Vector2Int>(o.GetRange(1, n - 1));
                }

                // От клетки дороги к корню.
                List<Vector2Int> walk = ChainToRoot(o[0], side, out _);
                if (walk != null && n <= walk.Count)
                {
                    bool match = true;
                    for (int j = 0; j < n && match; j++) match = o[j] == walk[j];
                    if (match)
                    {
                        var cells = new List<Vector2Int>();
                        foreach (Vector2Int c in o) if (IsChainCell(c)) cells.Add(c);
                        return cells;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Вторая односторонняя дорога проложена целиком по клеткам первой от обычной дороги или к ней:
        /// две полосы в одной клетке это обычная двухполосная дорога.
        /// </summary>
        bool TryOverlapRegular(IReadOnlyList<Vector2Int> path)
        {
            List<Vector2Int> cells = OverlapCells(path);
            if (cells == null || cells.Count == 0) return false;
            foreach (Vector2Int c in cells)
            {
                _highwayDirections.Remove(c);
                _rampCells.Remove(c);
            }

            LastForkBuilt = true;
            RaiseChanged();
            return true;
        }

        bool IsStraightPairOrJunction(Vector2Int cell, int mask, int partnerSide)
        {
            if (IsStraightPair(cell, mask, partnerSide)) return true;
            return Side.Count(Mask(cell)) >= 3;
        }

        bool IsStraightPair(Vector2Int cell, int mask, int partnerSide)
        {
            if (Mask(cell) != mask) return false;
            Vector2Int partner = cell + Side.Vec(partnerSide);
            if (Mask(partner) != mask) return false;

            // Третья соседняя прямая не должна случайно стать частью того же шоссе.
            int otherSide = Side.Opposite(partnerSide);
            if (Mask(cell + Side.Vec(otherSide)) == mask) return false;
            if (Mask(partner + Side.Vec(otherSide)) == mask) return false;
            return true;
        }

        /// <summary>Какие типы можно поставить на эту клетку. Пусто, если это не развилка.</summary>
        public List<JunctionType> AvailableTypes(Vector2Int cell)
        {
            var list = new List<JunctionType>();
            int deg = Side.Count(Mask(cell));
            if (deg < 3) return list;

            list.Add(JunctionType.Plain);
            list.Add(JunctionType.Roundabout);
            if (HasHighwayApproach(cell) || TypeAt(cell) == JunctionType.HighwayRoundabout)
                list.Add(JunctionType.HighwayRoundabout);
            if (deg == 4)
            {
                list.Add(JunctionType.Overpass);
                list.Add(JunctionType.OverpassVertical);
            }

            return list;
        }

        /// <summary>
        /// На двух проезжих частях одного шоссе один мостовой выбор занимает соседние клетки по оси моста.
        /// Возвращает вторую клетку только если обе стороны действительно пересекают шоссе.
        /// </summary>
        public Vector2Int? OverpassCompanionAt(Vector2Int cell, JunctionType type)
        {
            if (!JunctionSpecs.IsOverpass(type) || !IsHighwayAcross(cell, type)) return null;
            int[] bridgeSides = type == JunctionType.OverpassVertical
                ? new[] { Side.N, Side.S }
                : new[] { Side.E, Side.W };
            foreach (int side in bridgeSides)
            {
                if ((Mask(cell) & side) == 0) continue;
                Vector2Int candidate = cell + Side.Vec(side);
                int candidateMask = Mask(candidate);
                if (Side.Count(candidateMask) != 4 ||
                    (candidateMask & Side.Opposite(side)) == 0 || !IsHighwayAcross(candidate, type)) continue;
                return candidate;
            }
            return null;
        }

        bool IsHighwayAcross(Vector2Int cell, JunctionType type)
        {
            int mask = Mask(cell);
            if (Side.Count(mask) != 4) return false;
            int[] acrossSides = type == JunctionType.OverpassVertical
                ? new[] { Side.E, Side.W }
                : new[] { Side.N, Side.S };
            foreach (int side in acrossSides)
            {
                Vector2Int approachCell = cell + Side.Vec(side);
                HighwayLane lane = LaneAt(approachCell);
                if (lane.Active) return true;
            }
            return false;
        }

        bool HasHighwayApproach(Vector2Int cell)
        {
            HighwayLane own = LaneAt(cell);
            if (own.Active) return true;
            int mask = Mask(cell);
            foreach (int side in Side.All)
            {
                if ((mask & side) == 0) continue;
                HighwayLane approach = LaneAt(cell + Side.Vec(side));
                if (approach.Active) return true;
            }
            return false;
        }

        /// <summary>Развилки с особым типом (не обычный перекрёсток). Нужны для сохранения.</summary>
        public IEnumerable<KeyValuePair<Vector2Int, JunctionType>> ExplicitTypes => _types;

        /// <summary>Заменяет все дороги разом, одно событие Changed. Для загрузки сохранения.</summary>
        public void Load(IEnumerable<KeyValuePair<Vector2Int, int>> masks, IEnumerable<KeyValuePair<Vector2Int, JunctionType>> types)
        {
            Load(masks, types, null, null);
        }

        /// <summary>Загружает дороги, типы развилок и направления шоссе одним обновлением.</summary>
        public void Load(IEnumerable<KeyValuePair<Vector2Int, int>> masks,
            IEnumerable<KeyValuePair<Vector2Int, JunctionType>> types,
            IEnumerable<KeyValuePair<Vector2Int, int>> highwayDirections)
        {
            Load(masks, types, highwayDirections, null);
        }

        public void Load(IEnumerable<KeyValuePair<Vector2Int, int>> masks,
            IEnumerable<KeyValuePair<Vector2Int, JunctionType>> types,
            IEnumerable<KeyValuePair<Vector2Int, int>> highwayDirections,
            IEnumerable<Vector2Int> rampCells)
        {
            Load(masks, types, highwayDirections, rampCells, null);
        }

        public void Load(IEnumerable<KeyValuePair<Vector2Int, int>> masks,
            IEnumerable<KeyValuePair<Vector2Int, JunctionType>> types,
            IEnumerable<KeyValuePair<Vector2Int, int>> highwayDirections,
            IEnumerable<Vector2Int> rampCells, IEnumerable<Vector2Int> standaloneHighwayCells)
        {
            Load(masks, types, highwayDirections, rampCells, standaloneHighwayCells, standaloneHighwayCells);
        }

        public void Load(IEnumerable<KeyValuePair<Vector2Int, int>> masks,
            IEnumerable<KeyValuePair<Vector2Int, JunctionType>> types,
            IEnumerable<KeyValuePair<Vector2Int, int>> highwayDirections,
            IEnumerable<Vector2Int> rampCells, IEnumerable<Vector2Int> standaloneHighwayCells,
            IEnumerable<Vector2Int> ownedHighwayCells)
        {
            Load(masks, types, highwayDirections, rampCells, standaloneHighwayCells, ownedHighwayCells, null);
        }

        public void Load(IEnumerable<KeyValuePair<Vector2Int, int>> masks,
            IEnumerable<KeyValuePair<Vector2Int, JunctionType>> types,
            IEnumerable<KeyValuePair<Vector2Int, int>> highwayDirections,
            IEnumerable<Vector2Int> rampCells, IEnumerable<Vector2Int> standaloneHighwayCells,
            IEnumerable<Vector2Int> ownedHighwayCells,
            IEnumerable<KeyValuePair<Vector2Int, int>> standaloneOriginalMasks)
        {
            _masks.Clear();
            _types.Clear();
            _highwayDirections.Clear();
            _rampCells.Clear();
            _standaloneHighwayCells.Clear();
            _ownedHighwayCells.Clear();
            _standaloneOriginalMasks.Clear();
            foreach (var kv in masks) _masks[kv.Key] = kv.Value;
            foreach (var kv in types)
                if (_masks.ContainsKey(kv.Key) && IsValid(kv.Key, kv.Value)) _types[kv.Key] = kv.Value;
            if (highwayDirections != null)
                foreach (var kv in highwayDirections)
                    if (_masks.ContainsKey(kv.Key) && Side.Count(kv.Value) == 1 && IsDirectionConnected(kv.Key, kv.Value))
                        _highwayDirections[kv.Key] = kv.Value;
            if (rampCells != null)
                foreach (Vector2Int cell in rampCells)
                    if (_highwayDirections.ContainsKey(cell)) _rampCells.Add(cell);
            if (standaloneHighwayCells != null)
                foreach (Vector2Int cell in standaloneHighwayCells)
                    if (_highwayDirections.ContainsKey(cell)) _standaloneHighwayCells.Add(cell);
            if (ownedHighwayCells != null)
                foreach (Vector2Int cell in ownedHighwayCells)
                    if (_highwayDirections.ContainsKey(cell)) _ownedHighwayCells.Add(cell);
            if (standaloneOriginalMasks != null)
                foreach (var kv in standaloneOriginalMasks)
                    if (_highwayDirections.ContainsKey(kv.Key)) _standaloneOriginalMasks[kv.Key] = kv.Value;
            PairSavedOverpasses();
            LockExistingHighways();
            RaiseChanged();
        }

        void PairSavedOverpasses()
        {
            var saved = new List<KeyValuePair<Vector2Int, JunctionType>>(_types);
            foreach (KeyValuePair<Vector2Int, JunctionType> item in saved)
            {
                if (!JunctionSpecs.IsOverpass(item.Value)) continue;
                Vector2Int? companion = OverpassCompanionAt(item.Key, item.Value);
                if (companion.HasValue && IsValid(companion.Value, item.Value))
                    _types[companion.Value] = item.Value;
            }
        }

        public void SetType(Vector2Int cell, JunctionType type)
        {
            SetTypes(new KeyValuePair<Vector2Int, JunctionType>(cell, type));
        }

        public void SetTypes(params KeyValuePair<Vector2Int, JunctionType>[] changes)
        {
            if (changes == null || changes.Length == 0) return;
            foreach (KeyValuePair<Vector2Int, JunctionType> change in changes)
            {
                if (!_masks.ContainsKey(change.Key) || !IsValid(change.Key, change.Value)) continue;
                if (change.Value == JunctionType.Plain) _types.Remove(change.Key);
                else _types[change.Key] = change.Value;
            }
            RaiseChanged();
        }

        /// <summary>Сколько клеток пути ещё не заняты дорогой (за них платим).</summary>
        public int NewCellCount(IReadOnlyList<Vector2Int> path)
        {
            int n = 0;
            for (int i = 0; i < path.Count; i++)
                if (!_masks.ContainsKey(path[i])) n++;
            return n;
        }

        public void AddPath(IReadOnlyList<Vector2Int> path)
        {
            if (path == null || path.Count == 0) return;
            LockExistingHighways();

            for (int i = 0; i < path.Count; i++)
            {
                if (!_masks.ContainsKey(path[i])) _masks[path[i]] = 0;
            }

            for (int i = 0; i + 1 < path.Count; i++)
            {
                int side = Side.FromDelta(path[i + 1] - path[i]);
                if (side == 0) continue;
                _masks[path[i]] |= side;
                _masks[path[i + 1]] |= Side.Opposite(side);
                if (_standaloneOriginalMasks.ContainsKey(path[i]))
                {
                    _standaloneOriginalMasks[path[i]] |= side;
                    _ownedHighwayCells.Remove(path[i]);
                }
                if (_standaloneOriginalMasks.ContainsKey(path[i + 1]))
                {
                    _standaloneOriginalMasks[path[i + 1]] |= Side.Opposite(side);
                    _ownedHighwayCells.Remove(path[i + 1]);
                }
            }

            RaiseChanged();
        }

        public bool Remove(Vector2Int cell)
        {
            LockExistingHighways();
            if (!_masks.TryGetValue(cell, out int mask)) return false;
            // Если шоссе было проложено поверх обычной дороги, стираем только полотно
            // шоссе и возвращаем исходные связи обычной дороги на этой клетке.
            int originalMask = 0;
            bool overlay = _standaloneHighwayCells.Contains(cell) &&
                           _standaloneOriginalMasks.TryGetValue(cell, out originalMask);
            int restoredMask = overlay ? originalMask : 0;
            int disconnectMask = mask & ~restoredMask;

            foreach (int side in Side.All)
            {
                if ((disconnectMask & side) == 0) continue;
                Vector2Int n = cell + Side.Vec(side);
                if (_masks.TryGetValue(n, out int nm))
                {
                    _masks[n] = nm & ~Side.Opposite(side);
                    if (_standaloneOriginalMasks.ContainsKey(n))
                        _standaloneOriginalMasks[n] &= ~Side.Opposite(side);
                    if (_highwayDirections.TryGetValue(n, out int heading) &&
                        (_masks[n] & heading) == 0)
                    {
                        int rest = _masks[n];
                        if (Side.Count(rest) == 1)
                        {
                            // Соседняя клетка стала концом полосы: шоссе остаётся шоссе, иначе разрыв
                            // нельзя будет соединить обратно. Направление идёт к оборванному концу.
                            _highwayDirections[n] = Side.Opposite(rest);
                        }
                        else
                        {
                            _highwayDirections.Remove(n);
                            _rampCells.Remove(n);
                        }
                    }
                    if (_types.TryGetValue(n, out JunctionType nt) && !IsValid(n, nt)) _types.Remove(n);
                    DropOrphanHighwayCell(n);
                }
            }

            if (overlay && restoredMask != 0)
                _masks[cell] = restoredMask;
            else
            {
                _masks.Remove(cell);
                _types.Remove(cell);
            }

            if (_types.TryGetValue(cell, out JunctionType type) && !IsValid(cell, type))
                _types.Remove(cell);
            _highwayDirections.Remove(cell);
            _rampCells.Remove(cell);
            _standaloneHighwayCells.Remove(cell);
            _ownedHighwayCells.Remove(cell);
            _standaloneOriginalMasks.Remove(cell);
            RaiseChanged();
            return true;
        }

        /// <summary>Клетка, созданная шоссе, без единой связи больше не нужна: убираем её совсем.</summary>
        void DropOrphanHighwayCell(Vector2Int n)
        {
            if (!_ownedHighwayCells.Contains(n) || !_masks.TryGetValue(n, out int m) || m != 0) return;
            _masks.Remove(n);
            _types.Remove(n);
            _highwayDirections.Remove(n);
            _rampCells.Remove(n);
            _standaloneHighwayCells.Remove(n);
            _ownedHighwayCells.Remove(n);
            _standaloneOriginalMasks.Remove(n);
        }

        /// <summary>Маски пути как будто он уже построен. Нужны для превью.</summary>
        public static Dictionary<Vector2Int, int> MasksOf(IReadOnlyList<Vector2Int> path)
        {
            var result = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < path.Count; i++)
                if (!result.ContainsKey(path[i])) result[path[i]] = 0;

            for (int i = 0; i + 1 < path.Count; i++)
            {
                int side = Side.FromDelta(path[i + 1] - path[i]);
                if (side == 0) continue;
                result[path[i]] |= side;
                result[path[i + 1]] |= Side.Opposite(side);
            }

            return result;
        }
    }
}
