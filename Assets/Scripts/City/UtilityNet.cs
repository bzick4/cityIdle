using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Электричество и вода. Каждая клетка здания (жильё, торговля, промышленность) должна попасть в радиус
    /// электростанции и водонапорной башни, у которых ещё есть мощность. Мощность станции ограничена числом клеток.
    /// Первые FreeCells клеток города подключены бесплатно. Район платит налог пропорционально подключённым клеткам.
    /// </summary>
    public sealed class UtilityNet
    {
        public const int FreeCells = 3;

        readonly CityModel _model;
        readonly HashSet<Vector2Int> _noPower = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> _noWater = new HashSet<Vector2Int>();
        readonly Dictionary<District, float> _served = new Dictionary<District, float>();

        public int PowerCapacity { get; private set; }
        public int WaterCapacity { get; private set; }
        public int PowerUsed { get; private set; }
        public int WaterUsed { get; private set; }

        public event Action Changed;

        public UtilityNet(CityModel model)
        {
            _model = model;
            _model.Rebuilt += Recompute;
            Recompute();
        }

        public bool LacksPower(Vector2Int cell) { return _noPower.Contains(cell); }
        public bool LacksWater(Vector2Int cell) { return _noWater.Contains(cell); }

        /// <summary>Доля клеток района, у которых есть и ток, и вода.</summary>
        public float ServedFraction(District d)
        {
            return _served.TryGetValue(d, out float f) ? f : 1f;
        }

        void Recompute()
        {
            _noPower.Clear();
            _noWater.Clear();
            _served.Clear();

            var consumers = new List<BuildingCell>();
            foreach (BuildingCell c in _model.Cells)
                if (c.Category != BuildingCategory.Service) consumers.Add(c);

            PowerCapacity = WaterCapacity = 0;
            PowerUsed = WaterUsed = 0;
            Assign(consumers, ServiceKind.PowerPlant, _noPower, out int pCap, out int pUsed);
            Assign(consumers, ServiceKind.WaterTower, _noWater, out int wCap, out int wUsed);
            PowerCapacity = pCap; PowerUsed = pUsed;
            WaterCapacity = wCap; WaterUsed = wUsed;

            foreach (District d in _model.Districts)
            {
                if (!CategorySpecs.Merges(d.Category) || d.Count == 0) continue;
                int ok = 0;
                foreach (BuildingCell c in d.Cells)
                    if (!_noPower.Contains(c.Pos) && !_noWater.Contains(c.Pos)) ok++;
                _served[d] = (float)ok / d.Count;
            }

            Changed?.Invoke();
        }

        /// <summary>Раздаёт мощность станций вида kind. Клетки без подключения попадают в lacking.</summary>
        void Assign(List<BuildingCell> consumers, ServiceKind kind, HashSet<Vector2Int> lacking, out int capacity, out int used)
        {
            capacity = 0;
            used = 0;
            var covered = new HashSet<Vector2Int>();
            for (int i = 0; i < consumers.Count && i < FreeCells; i++) covered.Add(consumers[i].Pos);

            float r = ServiceSpecs.Radius(kind);
            int cap = ServiceSpecs.Capacity(kind);
            foreach (BuildingCell plant in _model.Cells)
            {
                if (plant.Category != BuildingCategory.Service || plant.Kind != kind) continue;
                capacity += cap;
                var near = new List<BuildingCell>();
                foreach (BuildingCell c in consumers)
                {
                    if (covered.Contains(c.Pos)) continue;
                    Vector2Int d = c.Pos - plant.Pos;
                    if (d.x * d.x + d.y * d.y <= r * r) near.Add(c);
                }

                near.Sort((a, b) =>
                {
                    Vector2Int da = a.Pos - plant.Pos, db = b.Pos - plant.Pos;
                    return (da.x * da.x + da.y * da.y).CompareTo(db.x * db.x + db.y * db.y);
                });
                for (int i = 0; i < near.Count && i < cap; i++)
                {
                    covered.Add(near[i].Pos);
                    used++;
                }
            }

            foreach (BuildingCell c in consumers)
                if (!covered.Contains(c.Pos)) lacking.Add(c.Pos);
        }
    }
}
