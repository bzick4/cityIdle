using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Покрытие служб. Для каждой клетки хранит, какие службы её накрывают (битовая маска по ServiceKind),
    /// и считает прибавку к налогу района как среднее по его клеткам. Пересчёт при любом изменении города.
    /// </summary>
    public sealed class ServiceCoverage
    {
        readonly CityModel _model;
        readonly Dictionary<Vector2Int, int> _mask = new Dictionary<Vector2Int, int>();
        readonly Dictionary<District, float> _districtBonus = new Dictionary<District, float>();

        public event Action Changed;

        public IReadOnlyDictionary<Vector2Int, int> Mask => _mask;

        public ServiceCoverage(CityModel model)
        {
            _model = model;
            _model.Rebuilt += Recompute;
            Recompute();
        }

        public void Dispose()
        {
            _model.Rebuilt -= Recompute;
        }

        public int MaskAt(Vector2Int cell)
        {
            _mask.TryGetValue(cell, out int m);
            return m;
        }

        /// <summary>Прибавка к налогу района, доля: 0.15 значит +15%.</summary>
        public float BonusOf(District d)
        {
            _districtBonus.TryGetValue(d, out float b);
            return b;
        }

        public static float CellBonus(int mask, BuildingCategory target)
        {
            float sum = 0f;
            foreach (ServiceKind k in Enum.GetValues(typeof(ServiceKind)))
                if ((mask & (1 << (int)k)) != 0) sum += ServiceSpecs.Bonus(k, target);
            return sum;
        }

        void Recompute()
        {
            _mask.Clear();
            _districtBonus.Clear();

            foreach (BuildingCell c in _model.Cells)
            {
                if (c.Category != BuildingCategory.Service) continue;
                float r = ServiceSpecs.Radius(c.Kind);
                int ri = Mathf.CeilToInt(r);
                for (int dx = -ri; dx <= ri; dx++)
                for (int dy = -ri; dy <= ri; dy++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    var p = new Vector2Int(c.Pos.x + dx, c.Pos.y + dy);
                    _mask.TryGetValue(p, out int m);
                    _mask[p] = m | (1 << (int)c.Kind);
                }
            }

            foreach (District d in _model.Districts)
            {
                if (!CategorySpecs.Merges(d.Category) || d.Count == 0) continue;
                float sum = 0f;
                foreach (BuildingCell c in d.Cells) sum += CellBonus(MaskAt(c.Pos), c.Category);
                _districtBonus[d] = sum / d.Count;
            }

            Changed?.Invoke();
        }
    }
}
