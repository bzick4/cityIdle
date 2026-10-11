using System;
using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Режим «Развилка». Тап по клетке, где сходятся три или четыре дороги, выбирает её: клетка получает рамку,
    /// а интерфейс показывает варианты. Выбранный вариант виден на карте сразу, но деньги списываются
    /// только по кнопке «Применить». «Отмена» или другой инструмент возвращают всё как было.
    /// Платим разницу цены: перекрёсток бесплатно, малое кольцо 50, кольцо шоссе 140, мост 100.
    /// </summary>
    public sealed class JunctionTapper : MonoBehaviour
    {
        public Camera Cam;
        public float CellSize = 1f;
        public Func<Vector2, bool> IsPointerBlocked;

        /// <summary>Показать сообщение игроку.</summary>
        public event Action<string> Message;

        CellRoadMap _map;
        Economy _economy;

        public Vector2Int? Selected { get; private set; }
        public JunctionType Pending { get; private set; }
        public JunctionType Current => Selected.HasValue ? _map.TypeAt(Selected.Value) : JunctionType.Plain;
        public bool HasSelection => Selected.HasValue;

        public void Bind(CellRoadMap map, Economy economy)
        {
            _map = map;
            _economy = economy;
        }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
        }

        void OnDisable()
        {
            Cancel();
        }

        public float PriceOf(JunctionType t)
        {
            switch (t)
            {
                case JunctionType.Roundabout: return _economy.RoundaboutCost;
                case JunctionType.HighwayRoundabout: return _economy.HighwayRoundaboutCost;
                case JunctionType.Overpass:
                case JunctionType.OverpassVertical: return _economy.OverpassCost;
                default: return 0f;
            }
        }

        /// <summary>Сколько заплатим за переход с текущего типа на указанный: только разница цены.</summary>
        public float CostFor(JunctionType target)
        {
            if (!Selected.HasValue) return 0f;
            Vector2Int cell = Selected.Value;
            Vector2Int? currentCompanion = JunctionSpecs.IsOverpass(Current)
                ? _map.OverpassCompanionAt(cell, Current)
                : null;
            Vector2Int? targetCompanion = JunctionSpecs.IsOverpass(target)
                ? _map.OverpassCompanionAt(cell, target)
                : null;

            float currentPrice = PriceOf(Current);
            Vector2Int? pricedCompanion = currentCompanion ?? targetCompanion;
            if (pricedCompanion.HasValue)
                currentPrice += PriceOf(_map.TypeAt(pricedCompanion.Value));

            float targetPrice = PriceOf(target);
            if (targetCompanion.HasValue) targetPrice += PriceOf(target);
            return Mathf.Max(0f, targetPrice - currentPrice);
        }

        public List<JunctionType> Available()
        {
            return Selected.HasValue ? _map.AvailableTypes(Selected.Value) : new List<JunctionType>();
        }

        public void Choose(JunctionType type)
        {
            if (!Selected.HasValue) return;
            Pending = type;
            _map.SetPreview(Selected.Value, type);
        }

        public void Apply()
        {
            if (!Selected.HasValue || Pending == Current) return;

            float cost = CostFor(Pending);
            if (cost > 0f && !_economy.TrySpend(cost))
            {
                Message?.Invoke("Не хватает денег");
                return;
            }

            Vector2Int cell = Selected.Value;
            JunctionType type = Pending;
            var changes = new List<KeyValuePair<Vector2Int, JunctionType>>();
            changes.Add(new KeyValuePair<Vector2Int, JunctionType>(cell, type));
            Vector2Int? companion = JunctionSpecs.IsOverpass(type)
                ? _map.OverpassCompanionAt(cell, type)
                : JunctionSpecs.IsOverpass(Current) ? _map.OverpassCompanionAt(cell, Current) : null;
            if (companion.HasValue)
            {
                JunctionType companionType = JunctionSpecs.IsOverpass(type) ? type : JunctionType.Plain;
                changes.Add(new KeyValuePair<Vector2Int, JunctionType>(companion.Value, companionType));
            }
            Selected = null;
            _map.ClearPreview();
            _map.SetTypes(changes.ToArray());
            Message?.Invoke(companion.HasValue && JunctionSpecs.IsOverpass(type)
                ? "Эстакада через обе полосы шоссе"
                : JunctionSpecs.Title(type));
        }

        public void Cancel()
        {
            Selected = null;
            if (_map != null) _map.ClearPreview();
        }

        void Select(Vector2Int cell)
        {
            Selected = cell;
            Pending = _map.TypeAt(cell);
            _map.SetPreview(cell, Pending);
        }

        void Update()
        {
            if (_map == null || Cam == null) return;

            // Дорогу под выбранной клеткой снесли или она перестала быть развилкой.
            if (Selected.HasValue && !_map.IsJunction(Selected.Value)) Cancel();

            PointerInput.Poll();
            if (PointerInput.Gesture || !PointerInput.Down) return;
            if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;

            Vector3 w = Cam.ScreenToWorldPoint(new Vector3(PointerInput.Position.x, PointerInput.Position.y,
                -Cam.transform.position.z));
            var cell = new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));

            if (_map.IsJunction(cell))
            {
                Select(cell);
            }
            else
            {
                Cancel();
                Message?.Invoke("Здесь нет развилки");
            }
        }
    }
}
