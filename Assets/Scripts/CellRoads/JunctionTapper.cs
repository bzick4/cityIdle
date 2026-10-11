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
    /// Платим разницу цены: перекрёсток бесплатно, кольцо 50, мост 100; со моста на кольцо и между мостами доплаты нет.
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
                case JunctionType.Overpass:
                case JunctionType.OverpassVertical: return _economy.OverpassCost;
                default: return 0f;
            }
        }

        /// <summary>Сколько заплатим за переход с текущего типа на указанный: только разница цены.</summary>
        public float CostFor(JunctionType target)
        {
            if (!Selected.HasValue) return 0f;
            return Mathf.Max(0f, PriceOf(target) - PriceOf(Current));
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
            Selected = null;
            _map.ClearPreview();
            _map.SetType(cell, type);
            Message?.Invoke(JunctionSpecs.Title(type));
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
