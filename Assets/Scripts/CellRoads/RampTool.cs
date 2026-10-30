using System;
using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Два тапа: первый выбирает исходную клетку, второй — целевую.
    /// После второго тапа показывается диалог подтверждения (IRoadStroke.Pending = true).
    /// </summary>
    public sealed class RampTool : MonoBehaviour, IRoadStroke
    {
        public float CellSize { get; set; } = 1f;
        public Func<Vector2, bool> IsPointerBlocked;

        public RampMap Ramps { get; private set; }

        Economy _economy;
        Camera _cam;
        bool _hasFrom;
        Vector2Int _fromCell;
        Vector2Int _toCell;
        bool _pending;

        static readonly List<Vector2Int> EmptyPath = new List<Vector2Int>();
        List<Vector2Int> _path = new List<Vector2Int>();

        public string Title => "Съезд";
        public bool Drawing => _hasFrom && !_pending;
        public bool Pending => _pending;
        public IReadOnlyList<Vector2Int> Path => _path;
        public float StrokeCost => CalcCost();
        public float Money => _economy != null ? _economy.Money : 0f;
        public float StrokeWidth => 0.44f;
        public Camera Cam => _cam;
        public string Problem => null;
        public string LengthLabel => _hasFrom && _pending ? Dist().ToString("F0") + " кл" : null;

        public event Action<IReadOnlyList<Vector2Int>, float> Committed;

        public void Bind(RampMap ramps, Economy economy)
        {
            Ramps = ramps;
            _economy = economy;
        }

        void Awake()
        {
            if (_cam == null) _cam = Camera.main;
        }

        void OnEnable()
        {
            _cam = Camera.main;
            Reset();
        }

        void OnDisable()
        {
            Reset();
        }

        void Update()
        {
            PointerInput.Poll();
            if (!PointerInput.Down || PointerInput.Gesture) return;
            if (_pending) return;
            if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;

            Vector2 world = _cam.ScreenToWorldPoint(PointerInput.Position);

            var cell = new Vector2Int(
                Mathf.FloorToInt(world.x / CellSize),
                Mathf.FloorToInt(world.y / CellSize));

            if (!_hasFrom)
            {
                _fromCell = cell;
                _hasFrom = true;
                _path = new List<Vector2Int> { _fromCell };
            }
            else if (cell != _fromCell)
            {
                _toCell = cell;
                _path = new List<Vector2Int> { _fromCell, _toCell };
                _pending = true;
            }
        }

        public void Confirm()
        {
            if (!_pending) return;
            float cost = CalcCost();
            if (_economy != null && !_economy.TrySpend(cost)) { Reset(); return; }
            Ramps.Add(_fromCell, _toCell);
            Committed?.Invoke(_path, cost);
            Reset();
        }

        public void Cancel()
        {
            Reset();
        }

        void Reset()
        {
            _hasFrom = false;
            _pending = false;
            _path = EmptyPath;
        }

        float Dist()
        {
            if (!_hasFrom) return 0f;
            Vector2 fc = RampMap.CellCenter(_fromCell, CellSize);
            Vector2 tc = RampMap.CellCenter(_toCell, CellSize);
            return Vector2.Distance(fc, tc) / CellSize;
        }

        float CalcCost()
        {
            if (!_hasFrom || !_pending) return 0f;
            return Mathf.Max(1f, Dist()) * (_economy != null ? _economy.RoadCostPerCell : 5f);
        }
    }
}
