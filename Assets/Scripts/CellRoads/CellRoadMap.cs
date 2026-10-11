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
            if (PreviewCell.HasValue && PreviewCell.Value == cell && IsValid(cell, PreviewType)) return PreviewType;
            return TypeAt(cell);
        }

        public int Count => _masks.Count;
        public IEnumerable<KeyValuePair<Vector2Int, int>> Cells => _masks;

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
            if (t == JunctionType.Roundabout) return deg >= 3;
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

            int heading;
            if (horizontal) heading = partnerSide == Side.N ? Side.E : Side.W;
            else heading = partnerSide == Side.E ? Side.S : Side.N;

            return new HighwayLane { Active = true, Heading = heading, PartnerSide = partnerSide };
        }

        /// <summary>Какие типы можно поставить на эту клетку. Пусто, если это не развилка.</summary>
        public List<JunctionType> AvailableTypes(Vector2Int cell)
        {
            var list = new List<JunctionType>();
            int deg = Side.Count(Mask(cell));
            if (deg < 3) return list;

            list.Add(JunctionType.Plain);
            list.Add(JunctionType.Roundabout);
            if (deg == 4)
            {
                list.Add(JunctionType.Overpass);
                list.Add(JunctionType.OverpassVertical);
            }

            return list;
        }

        /// <summary>Развилки с особым типом (не обычный перекрёсток). Нужны для сохранения.</summary>
        public IEnumerable<KeyValuePair<Vector2Int, JunctionType>> ExplicitTypes => _types;

        /// <summary>Заменяет все дороги разом, одно событие Changed. Для загрузки сохранения.</summary>
        public void Load(IEnumerable<KeyValuePair<Vector2Int, int>> masks, IEnumerable<KeyValuePair<Vector2Int, JunctionType>> types)
        {
            _masks.Clear();
            _types.Clear();
            foreach (var kv in masks) _masks[kv.Key] = kv.Value;
            foreach (var kv in types)
                if (_masks.ContainsKey(kv.Key) && IsValid(kv.Key, kv.Value)) _types[kv.Key] = kv.Value;
            Changed?.Invoke();
        }

        public void SetType(Vector2Int cell, JunctionType type)
        {
            if (!_masks.ContainsKey(cell)) return;
            if (type == JunctionType.Plain) _types.Remove(cell);
            else _types[cell] = type;
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

            for (int i = 0; i < path.Count; i++)
                if (!_masks.ContainsKey(path[i])) _masks[path[i]] = 0;

            for (int i = 0; i + 1 < path.Count; i++)
            {
                int side = Side.FromDelta(path[i + 1] - path[i]);
                if (side == 0) continue;
                _masks[path[i]] |= side;
                _masks[path[i + 1]] |= Side.Opposite(side);
            }

            Changed?.Invoke();
        }

        public bool Remove(Vector2Int cell)
        {
            if (!_masks.TryGetValue(cell, out int mask)) return false;

            foreach (int side in Side.All)
            {
                if ((mask & side) == 0) continue;
                Vector2Int n = cell + Side.Vec(side);
                if (_masks.TryGetValue(n, out int nm))
                {
                    _masks[n] = nm & ~Side.Opposite(side);
                    if (_types.TryGetValue(n, out JunctionType nt) && !IsValid(n, nt)) _types.Remove(n);
                }
            }

            _masks.Remove(cell);
            _types.Remove(cell);
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
