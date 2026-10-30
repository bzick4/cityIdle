using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.CellRoads;
using CityGame.Roads;
using CityGame.Water;
using UnityEngine;

namespace CityGame.Sectors
{
    /// <summary>
    /// Правила биомов поверх карты: где можно строить, сколько стоит, какие клетки с деревьями и бонус леса.
    /// Деревья растут на части свободных клеток стандартного биома. Здание или дорога на клетке убирает дерево.
    /// Бонус +10% к налогу клеток сектора есть, пока осталось не меньше 30% от изначального числа деревьев.
    /// </summary>
    public sealed class Biomes
    {
        public readonly BiomeMap Map;
        readonly SectorMap _sectors;
        readonly CityModel _model;
        readonly CellRoadMap _roads;
        readonly float _cell;
        readonly Dictionary<HexCoord, Vector2Int> _stats = new Dictionary<HexCoord, Vector2Int>();

        /// <summary>Каналы. Задаются после создания через BindCanals.</summary>
        public CanalMap Canals { get; private set; }

        /// <summary>Клетка занята штучной постройкой (автостанция): дерево там не растёт.</summary>
        public System.Func<Vector2Int, bool> Occupied;

        /// <summary>Пересчитать деревья после изменения штучных построек.</summary>
        public void Refresh()
        {
            Recompute();
        }

        /// <summary>Деревья изменились: пора перерисовать.</summary>
        public event System.Action Changed;

        public Biomes(BiomeMap map, SectorMap sectors, CityModel model, CellRoadMap roads, float cellSize)
        {
            Map = map;
            _sectors = sectors;
            _model = model;
            _roads = roads;
            _cell = cellSize;
            _sectors.Changed += Recompute;
            _model.Rebuilt += Recompute;
            _roads.Changed += Recompute;
            Recompute();
        }

        public void Dispose()
        {
            _sectors.Changed -= Recompute;
            _model.Rebuilt -= Recompute;
            _roads.Changed -= Recompute;
            if (Canals != null) Canals.Changed -= Recompute;
        }

        Vector2 CenterOf(Vector2Int c)
        {
            return new Vector2((c.x + 0.5f) * _cell, (c.y + 0.5f) * _cell);
        }

        public Biome BiomeAt(Vector2Int cell)
        {
            return Map.BiomeOf(_sectors.SectorAt(CenterOf(cell)));
        }

        /// <summary>Тут растёт дерево по природе клетки (не учитывая застройку).</summary>
        public bool NaturalTree(Vector2Int cell)
        {
            if (BiomeAt(cell) != Biome.Standard) return false;
            return (BiomeMap.Mix(cell.x, cell.y, Map.Seed + 5) & 0xFFFF) / 65535f < BiomeSpecs.TreeDensity;
        }

        public void BindCanals(CanalMap canals)
        {
            if (Canals != null) Canals.Changed -= Recompute;
            Canals = canals;
            if (Canals != null) Canals.Changed += Recompute;
            Recompute();
        }

        bool IsCanal(Vector2Int cell)
        {
            return (Canals != null && Canals.Has(cell)) || (Occupied != null && Occupied(cell));
        }

        public bool HasTree(Vector2Int cell)
        {
            return NaturalTree(cell) && !_model.HasCell(cell) && !_roads.Has(cell) && !IsCanal(cell);
        }

        public bool CanBuild(Vector2Int cell)
        {
            return BiomeSpecs.CanBuildBuildings(BiomeAt(cell));
        }

        public bool IsSea(Vector2Int cell)
        {
            return BiomeAt(cell) == Biome.Sea;
        }

        /// <summary>Вода: море или канал. Здесь дорога становится мостом, а порт можно поставить.</summary>
        public bool IsWater(Vector2Int cell)
        {
            return (Canals != null && Canals.Has(cell)) || IsSea(cell);
        }

        /// <summary>Множитель цены клетки дороги: на море это мост.</summary>
        public float RoadCostFactor(Vector2Int cell)
        {
            return Canals != null && Canals.Has(cell) ? BiomeSpecs.SeaCostFactor : BiomeSpecs.RoadCostFactor(BiomeAt(cell));
        }

        public float CostFactor(Vector2Int cell)
        {
            return BiomeSpecs.BuildCostFactor(BiomeAt(cell));
        }

        /// <summary>Деревья сектора: x всего, y осталось.</summary>
        public Vector2Int TreesOf(HexCoord h)
        {
            _stats.TryGetValue(h, out Vector2Int v);
            return v;
        }

        public bool ForestBonusActive(HexCoord h)
        {
            Vector2Int t = TreesOf(h);
            return t.x > 0 && t.y >= BiomeSpecs.TreeThreshold * t.x;
        }

        public float TaxBonusAt(Vector2Int cell)
        {
            HexCoord h = _sectors.SectorAt(CenterOf(cell));
            if (Map.BiomeOf(h) != Biome.Standard) return 0f;
            return ForestBonusActive(h) ? BiomeSpecs.TreeBonus : 0f;
        }

        void Recompute()
        {
            _stats.Clear();
            foreach (HexCoord h in _sectors.Open)
            {
                if (Map.BiomeOf(h) != Biome.Standard) continue;
                Vector2 c = HexMath.ToWorld(h, _sectors.HexSize);
                float rx = _sectors.HexSize * 0.8660254f + _cell;
                float ry = _sectors.HexSize + _cell;
                int x0 = Mathf.FloorToInt((c.x - rx) / _cell), x1 = Mathf.CeilToInt((c.x + rx) / _cell);
                int y0 = Mathf.FloorToInt((c.y - ry) / _cell), y1 = Mathf.CeilToInt((c.y + ry) / _cell);

                int total = 0, left = 0;
                for (int ix = x0; ix <= x1; ix++)
                for (int iy = y0; iy <= y1; iy++)
                {
                    var cell = new Vector2Int(ix, iy);
                    if (_sectors.SectorAt(CenterOf(cell)) != h || !NaturalTree(cell)) continue;
                    total++;
                    if (!_model.HasCell(cell) && !_roads.Has(cell) && !IsCanal(cell)) left++;
                }

                _stats[h] = new Vector2Int(total, left);
            }

            Changed?.Invoke();
        }
    }
}
