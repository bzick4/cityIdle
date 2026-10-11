using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Buildings
{
    public sealed class BuildingCell
    {
        public readonly Vector2Int Pos;
        public readonly BuildingCategory Category;
        public readonly ServiceKind Kind;
        public District District;

        public BuildingCell(Vector2Int pos, BuildingCategory category, ServiceKind kind = ServiceKind.Ambulance)
        {
            Pos = pos;
            Category = category;
            Kind = kind;
        }
    }

    /// <summary>Район: связная группа ячеек одного типа. Служба всегда район из одной ячейки.</summary>
    public sealed class District
    {
        public int Id;
        public BuildingCategory Category;
        public ServiceKind Kind;
        public readonly List<BuildingCell> Cells = new List<BuildingCell>();
        /// <summary>Довольство 0..1, считает FlowSolver. Для служб и ещё не посчитанных районов 1.</summary>
        public float Satisfaction = 1f;
        public int Count => Cells.Count;
        public string Title => Category == BuildingCategory.Service ? ServiceSpecs.Title(Kind) : CategorySpecs.Title(Category);
        public int Multiplier => CategorySpecs.Merges(Category) ? IncomeMath.Multiplier(Count) : Count;
    }

    /// <summary>
    /// Здания на глобальной квадратной сетке. После каждого изменения районы пересобираются
    /// заливкой по четырём соседям. Для сотен ячеек этого достаточно.
    /// </summary>
    public sealed class CityModel
    {
        readonly Dictionary<Vector2Int, BuildingCell> _cells = new Dictionary<Vector2Int, BuildingCell>();
        readonly List<District> _districts = new List<District>();
        int _nextDistrictId = 1;

        static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        public IReadOnlyList<District> Districts => _districts;
        public IEnumerable<BuildingCell> Cells => _cells.Values;
        public int CellCount => _cells.Count;

        public event Action<BuildingCell> CellAdded;
        public event Action<BuildingCell> CellRemoved;
        /// <summary>Районы пересобраны (после любого изменения).</summary>
        public event Action Rebuilt;

        public bool HasCell(Vector2Int pos)
        {
            return _cells.ContainsKey(pos);
        }

        public BuildingCell Get(Vector2Int pos)
        {
            _cells.TryGetValue(pos, out BuildingCell c);
            return c;
        }

        public bool Place(Vector2Int pos, BuildingCategory category, ServiceKind kind = ServiceKind.Ambulance)
        {
            if (_cells.ContainsKey(pos)) return false;
            var cell = new BuildingCell(pos, category, kind);
            _cells[pos] = cell;
            RebuildDistricts();
            CellAdded?.Invoke(cell);
            Rebuilt?.Invoke();
            return true;
        }

        public bool Remove(Vector2Int pos)
        {
            if (!_cells.TryGetValue(pos, out BuildingCell cell)) return false;
            _cells.Remove(pos);
            RebuildDistricts();
            CellRemoved?.Invoke(cell);
            Rebuilt?.Invoke();
            return true;
        }

        void RebuildDistricts()
        {
            _districts.Clear();
            _nextDistrictId = 1;
            foreach (BuildingCell c in _cells.Values) c.District = null;

            var stack = new Stack<BuildingCell>();
            foreach (BuildingCell start in _cells.Values)
            {
                if (start.District != null) continue;

                var d = new District { Id = _nextDistrictId++, Category = start.Category, Kind = start.Kind };
                _districts.Add(d);
                start.District = d;
                d.Cells.Add(start);

                if (!CategorySpecs.Merges(start.Category)) continue;

                stack.Push(start);
                while (stack.Count > 0)
                {
                    BuildingCell cur = stack.Pop();
                    foreach (Vector2Int dir in Dirs)
                    {
                        if (!_cells.TryGetValue(cur.Pos + dir, out BuildingCell n)) continue;
                        if (n.District != null || n.Category != start.Category) continue;
                        n.District = d;
                        d.Cells.Add(n);
                        stack.Push(n);
                    }
                }
            }
        }
    }
}
