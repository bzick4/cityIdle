using System;
using System.Collections.Generic;
using CityGame;
using CityGame.CellRoads;
using CityGame.Roads;
using CityGame.Sectors;
using CityGame.Transit;
using CityGame.Water;
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
        public PortMap Ports;
        public BusMap Buses;
        public CanalMap Canals;
        public CellRoads.RampMap Ramps;

        CityModel _model;
        Economy _economy;
        CellRoadMap _roads;
        bool _painting;

        /// <summary>Здание, ожидающее подтверждения удаления.</summary>
        public Vector2Int? PendingEraseCell { get; private set; }

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

        public bool BlockedByRoad(Vector2Int cell) => _roads != null && _roads.Has(cell);

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
        }

        void OnDisable()
        {
            _painting = false;
        }

        public void ConfirmEraseBuilding()
        {
            if (!PendingEraseCell.HasValue) return;
            Vector2Int cell = PendingEraseCell.Value;
            PendingEraseCell = null;
            _model?.Remove(cell);
        }

        public void CancelEraseBuilding()
        {
            PendingEraseCell = null;
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

            Vector2 w = GroundRay.ToGround(PointerInput.Position, Cam);
            Apply(ToCell(w));
        }

        void Apply(Vector2Int cell)
        {
            if (EraseMode)
            {
                if (Ports != null && Ports.Has(cell))
                {
                    Ports.Remove(cell);
                    return;
                }

                if (Buses != null && Buses.Has(cell))
                {
                    Buses.Remove(cell);
                    return;
                }

                if (_model.Get(cell) != null)
                {
                    PendingEraseCell = cell;
                    _painting = false;
                    return;
                }
                if (_roads.Has(cell))
                {
                    if (!_roads.Remove(cell)) Message?.Invoke("Не удалось удалить этот участок дороги");
                    return;
                }

                if (Canals != null && Canals.Has(cell)) Canals.Remove(cell);
                if (Ramps != null && Ramps.HasCell(cell)) Ramps.RemoveAt(cell);
                return;
            }

            if (_model.HasCell(cell)) return;
            if (CanPlaceAt != null && !CanPlaceAt(CellCenter(cell))) return;
            if (BlockedByRoad(cell)) return;
            if (Ports != null && Ports.Has(cell)) return;
            if (Buses != null && Buses.Has(cell)) return;
            if (Canals != null && Canals.Has(cell))
            {
                Message?.Invoke("На канале можно строить только порт");
                return;
            }

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

        /// <summary>Проверка без размещения — для госта карточки.</summary>
        public PlaceResult CheckPlacement(Vector2Int cell, BuildingCategory cat, ServiceKind kind)
        {
            if (_model.HasCell(cell)) return PlaceResult.Occupied;
            if (CanPlaceAt != null && !CanPlaceAt(CellCenter(cell))) return PlaceResult.OutOfSector;
            if (BlockedByRoad(cell)) return PlaceResult.Occupied;
            if (Ports != null && Ports.Has(cell)) return PlaceResult.Occupied;
            if (Buses != null && Buses.Has(cell)) return PlaceResult.Occupied;
            if (Canals != null && Canals.Has(cell)) return PlaceResult.Occupied;
            if (_biomes != null && !_biomes.CanBuild(cell)) return PlaceResult.NoBuild;
            float cost = _economy.CostAt(cat, kind, cell);
            if (_economy.Money < cost) return PlaceResult.NoMoney;
            return PlaceResult.Ok;
        }

        /// <summary>Разовое размещение с карточки — без активации режима рисования.</summary>
        public PlaceResult TryPlaceAt(Vector2Int cell, BuildingCategory cat, ServiceKind kind)
        {
            if (_model.HasCell(cell)) return PlaceResult.Occupied;
            if (CanPlaceAt != null && !CanPlaceAt(CellCenter(cell))) return PlaceResult.OutOfSector;
            if (BlockedByRoad(cell)) return PlaceResult.Occupied;
            if (Ports != null && Ports.Has(cell)) return PlaceResult.Occupied;
            if (Buses != null && Buses.Has(cell)) return PlaceResult.Occupied;
            if (Canals != null && Canals.Has(cell)) return PlaceResult.Occupied;
            if (_biomes != null && !_biomes.CanBuild(cell)) return PlaceResult.NoBuild;
            float cost = _economy.CostAt(cat, kind, cell);
            if (_economy.Money < cost) return PlaceResult.NoMoney;
            if (_model.Place(cell, cat, kind)) { _economy.TrySpend(cost); return PlaceResult.Ok; }
            return PlaceResult.Occupied;
        }
    }

    public enum PlaceResult { Ok, Occupied, OutOfSector, NoBuild, NoMoney }
}
