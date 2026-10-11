using System;
using System.Collections.Generic;
using CityGame.CellRoads;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Ставит здания тапом или проводом пальца (красит ячейки). Режим Erase убирает ячейки.
    /// Сетка глобальная квадратная, потом её заменят участками внутри шестиугольных секторов.
    /// </summary>
    public sealed class BuildingPlacer : MonoBehaviour
    {
        public Camera Cam;
        public float CellSize = 1f;

        [Tooltip("Что ставим сейчас")]
        public BuildingCategory Category = BuildingCategory.Residential;
        public ServiceKind Kind = ServiceKind.Ambulance;
        public bool EraseMode;

        public Func<Vector2, bool> IsPointerBlocked;

        /// <summary>Правила секторов: можно ли строить в этой точке мира.</summary>
        public Func<Vector2, bool> CanPlaceAt;
        public event Action<Vector2Int> PlaceFailedNoMoney;
        public event Action<string> Message;
        Biomes _biomes;

        CityModel _model;
        Economy _economy;
        CellRoadMap _roads;
        bool _painting;

        public void Bind(CityModel model, Economy economy, CellRoadMap roads, Biomes biomes = null)
        {
            _biomes = biomes;
            _model = model;
            _economy = economy;
            _roads = roads;
        }

        public Vector2Int ToCell(Vector2 world)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x / CellSize), Mathf.FloorToInt(world.y / CellSize));
        }

        public Vector2 CellCenter(Vector2Int cell)
        {
            return new Vector2((cell.x + 0.5f) * CellSize, (cell.y + 0.5f) * CellSize);
        }

        /// <summary>Клетка занята дорогой. Одна клетка это одна постройка, дорога или здание.</summary>
        public bool BlockedByRoad(Vector2Int cell)
        {
            return _roads != null && _roads.Has(cell);
        }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
        }

        void OnDisable()
        {
            _painting = false;
        }

        void Update()
        {
            if (_model == null || Cam == null) return;

            PointerInput.Poll();

            if (PointerInput.Gesture)
            {
                _painting = false;
                return;
            }

            if (!_painting)
            {
                if (!PointerInput.Down) return;
                if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;
                _painting = true;
            }

            if (!PointerInput.Pressed || PointerInput.Up)
            {
                _painting = false;
                return;
            }

            Vector3 w = Cam.ScreenToWorldPoint(new Vector3(PointerInput.Position.x, PointerInput.Position.y,
                -Cam.transform.position.z));
            Apply(ToCell(new Vector2(w.x, w.y)));
        }

        void Apply(Vector2Int cell)
        {
            if (EraseMode)
            {
                _model.Remove(cell);
                _roads.Remove(cell);
                return;
            }

            if (_model.HasCell(cell)) return;
            if (CanPlaceAt != null && !CanPlaceAt(CellCenter(cell))) return;
            if (BlockedByRoad(cell)) return;

            if (_biomes != null && !_biomes.CanBuild(cell))
            {
                Message?.Invoke("В горах можно строить только дороги");
                return;
            }

            float cost = _economy.CostAt(Category, Kind, cell);
            if (_economy.Money < cost)
            {
                PlaceFailedNoMoney?.Invoke(cell);
                Message?.Invoke("Не хватает денег: " + cost.ToString("0"));
                return;
            }

            if (_model.Place(cell, Category, Kind)) _economy.TrySpend(cost);
        }
    }
}
