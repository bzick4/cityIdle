using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>Стороны клетки как биты: север, восток, юг, запад.</summary>
    public static class Side
    {
        public const int N = 1, E = 2, S = 4, W = 8;
        public static readonly int[] All = { N, E, S, W };

        public static Vector2Int Vec(int side)
        {
            switch (side)
            {
                case N: return new Vector2Int(0, 1);
                case E: return new Vector2Int(1, 0);
                case S: return new Vector2Int(0, -1);
                default: return new Vector2Int(-1, 0);
            }
        }

        public static int Opposite(int side)
        {
            switch (side)
            {
                case N: return S;
                case S: return N;
                case E: return W;
                default: return E;
            }
        }

        /// <summary>Сторона по смещению на одну клетку, 0 если клетки не соседние.</summary>
        public static int FromDelta(Vector2Int d)
        {
            if (d == new Vector2Int(0, 1)) return N;
            if (d == new Vector2Int(1, 0)) return E;
            if (d == new Vector2Int(0, -1)) return S;
            if (d == new Vector2Int(-1, 0)) return W;
            return 0;
        }

        public static int Count(int mask)
        {
            int n = 0;
            for (int i = 0; i < 4; i++)
                if ((mask & All[i]) != 0) n++;
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

        /// <summary>Строит отдельное полотно шоссе на пустых клетках или поверх обычной дороги.</summary>
        public bool SetHighwayPath(IReadOnlyList<Vector2Int> path, bool standalone = true)
        {
            if (path == null || path.Count < 2) return false;
            LockExistingHighways();
            for (int i = 0; i < path.Count; i++)
            {
                if (LaneAt(path[i]).Active) return false;
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
            {
                int heading;
                if (i + 1 < path.Count) heading = Side.FromDelta(path[i + 1] - path[i]);
                else heading = Side.FromDelta(path[i] - path[i - 1]);
                _highwayDirections[path[i]] = heading;
                _rampCells.Remove(path[i]);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Строит одностороннюю дорогу обычной ширины на пустых клетках. Она может
        /// соединяться с шоссе и пересекать его; геометрия и направление шоссе сохраняются.
        /// </summary>
        public bool SetOneWayPath(IReadOnlyList<Vector2Int> path)
        {
            if (path == null || path.Count < 2) return false;
            LockExistingHighways();

            bool hasRoadCell = false;
            var highwayEndpoints = new bool[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                HighwayLane lane = LaneAt(path[i]);
                bool highway = lane.Active && !lane.OneWay;
                highwayEndpoints[i] = highway;
                if (lane.Active && !highway) return false;
                if (highway && i > 0 && i + 1 < path.Count)
                {
                    int incoming = Side.FromDelta(path[i] - path[i - 1]);
                    int outgoing = Side.FromDelta(path[i + 1] - path[i]);
                    // incoming уже направлен от предыдущей клетки к перекрёстку.
                    // Чтобы пересечь шоссе насквозь, следующий шаг должен продолжать это направление.
                    if (outgoing != incoming) return false;
                }
                if (_masks.TryGetValue(path[i], out int existingMask) && existingMask != 0 && !highway)
                    return false;
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
            {
                if (highwayEndpoints[i]) continue; // не перезаписываем направление шоссе
                int heading = i + 1 < path.Count
                    ? Side.FromDelta(path[i + 1] - path[i])
                    : Side.FromDelta(path[i] - path[i - 1]);
                _highwayDirections[path[i]] = heading;
                _rampCells.Add(path[i]);
            }

            Changed?.Invoke();
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
                if (lane.Active && !lane.OneWay) return true;
            }
            return false;
        }

        bool HasHighwayApproach(Vector2Int cell)
        {
            HighwayLane own = LaneAt(cell);
            if (own.Active && !own.OneWay) return true;
            int mask = Mask(cell);
            foreach (int side in Side.All)
            {
                if ((mask & side) == 0) continue;
                HighwayLane approach = LaneAt(cell + Side.Vec(side));
                if (approach.Active && !approach.OneWay) return true;
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
            Changed?.Invoke();
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
            Changed?.Invoke();
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

            Changed?.Invoke();
        }

        public bool Remove(Vector2Int cell)
        {
            LockExistingHighways();
            if (!_masks.TryGetValue(cell, out int mask)) return false;
            HighwayLane currentLane = LaneAt(cell);
            if (currentLane.Active && !currentLane.OneWay) return false;

            // Не позволяем удалить соседнюю клетку, если этим разорвётся уже размеченное шоссе.
            foreach (int side in Side.All)
            {
                Vector2Int neighbor = cell + Side.Vec(side);
                HighwayLane adjacent = LaneAt(neighbor);
                int edgeFromNeighbor = Side.Opposite(side);
                if (adjacent.Active && !adjacent.OneWay && (Mask(neighbor) & edgeFromNeighbor) != 0 &&
                    (adjacent.Heading == edgeFromNeighbor ||
                     Side.Opposite(adjacent.Heading) == edgeFromNeighbor)) return false;
            }

            foreach (int side in Side.All)
            {
                if ((mask & side) == 0) continue;
                Vector2Int n = cell + Side.Vec(side);
                if (_masks.TryGetValue(n, out int nm))
                {
                    _masks[n] = nm & ~Side.Opposite(side);
                    if (_standaloneOriginalMasks.ContainsKey(n))
                        _standaloneOriginalMasks[n] &= ~Side.Opposite(side);
                    if (_highwayDirections.TryGetValue(n, out int heading) &&
                        (_masks[n] & heading) == 0)
                    {
                        _highwayDirections.Remove(n);
                        _rampCells.Remove(n);
                    }
                    if (_types.TryGetValue(n, out JunctionType nt) && !IsValid(n, nt)) _types.Remove(n);
                }
            }

            _masks.Remove(cell);
            _types.Remove(cell);
            _highwayDirections.Remove(cell);
            _rampCells.Remove(cell);
            _standaloneHighwayCells.Remove(cell);
            _ownedHighwayCells.Remove(cell);
            _standaloneOriginalMasks.Remove(cell);
            Changed?.Invoke();
            return true;
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
