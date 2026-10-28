using System;
using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Рисование дороги пальцем по клеткам. Дорога идёт строго по прямой вдоль оси, как линия в Paint.
    /// Если увести палец в сторону, дорога поворачивает под 90 градусов, а угол рисуется закруглением.
    /// Если вернуть палец назад по уже нарисованному, лишняя часть исчезает.
    /// </summary>
    public sealed class CellRoadDrawer : MonoBehaviour, IRoadStroke
    {
        public Camera Cam;
        public float CellSize = 1f;
        [Range(0.2f, 0.9f)] public float WidthFraction = 0.5f;

        [Tooltip("Насколько дальше в сторону нужно увести палец, чтобы дорога повернула, в клетках")]
        public int TurnThreshold = 2;

        public Func<Vector2, bool> IsPointerBlocked;

        /// <summary>Множитель цены клетки дороги (мост на море дороже).</summary>
        public Func<Vector2Int, float> CostFactorAt;

        /// <summary>Клетка занята чем-то кроме зданий (например, портом): дорогу не провести.</summary>
        public Func<Vector2Int, bool> IsOccupied;

        /// <summary>Идёт ли сейчас штрих и какие клетки в нём. Для эффектов рисования.</summary>
        public bool Drawing => _drawing;
        public bool Pending => _pending;
        public string Title => "Дорога";
        public string Problem => null;
        public string LengthLabel => _path.Count + " кл.";
        public IReadOnlyList<Vector2Int> Path => _path;
        public float Money => _economy != null ? _economy.Money : 0f;
        public float StrokeCost => PathCost();
        public float StrokeWidth => WidthFraction;
        Camera IRoadStroke.Cam => Cam;
        bool _pending;

        public void Confirm()
        {
            if (!_pending) return;
            _pending = false;
            Commit();
        }

        public void Cancel()
        {
            _pending = false;
            _drawing = false;
            _path.Clear();
            ClearPreview();
        }

        /// <summary>Дорога построена: принятые клетки (по порядку) и потраченная сумма.</summary>
        public event Action<IReadOnlyList<Vector2Int>, float> Committed;

        /// <summary>Сколько стоят новые клетки текущего штриха.</summary>
        public float PathCost()
        {
            float total = 0f;
            foreach (Vector2Int cell in _path)
                if (!_map.Has(cell)) total += _economy.RoadCostPerCell * (CostFactorAt != null ? CostFactorAt(cell) : 1f);
            return total;
        }

        CellRoadMap _map;
        CityModel _city;
        SectorMap _sectors;
        Economy _economy;

        readonly List<Vector2Int> _path = new List<Vector2Int>();
        bool _drawing;
        bool _dirKnown;
        Vector2Int _dir;
        bool _previewDirty;

        GameObject _preview;
        Material _previewMaterial;

        public void Bind(CellRoadMap map, CityModel city, SectorMap sectors, Economy economy)
        {
            _map = map;
            _city = city;
            _sectors = sectors;
            _economy = economy;
        }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            _previewMaterial = new Material(Shader.Find("Sprites/Default"));
            Color c = RoadSpecs.BodyColor(RoadType.Local);
            c.a = 0.6f;
            _previewMaterial.color = c;
        }

        void OnDisable()
        {
            Cancel();
        }

        public Vector2Int ToCell(Vector2 world)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x / CellSize), Mathf.FloorToInt(world.y / CellSize));
        }

        Vector2 CellCenter(Vector2Int c)
        {
            return new Vector2((c.x + 0.5f) * CellSize, (c.y + 0.5f) * CellSize);
        }

        bool Allowed(Vector2Int cell)
        {
            if (_city.HasCell(cell)) return false;
            if (IsOccupied != null && IsOccupied(cell)) return false;
            return _sectors.IsOpenAt(CellCenter(cell));
        }

        void Update()
        {
            if (_map == null || Cam == null) return;

            PointerInput.Poll();

            // Второй палец на экране: это жест камеры, штрих отменяем.
            if (PointerInput.Gesture)
            {
                if (_drawing)
                {
                    _drawing = false;
                    _path.Clear();
                    ClearPreview();
                }

                return;
            }

            if (!_drawing)
            {
                if (!PointerInput.Down) return;
                if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;

                Vector2Int start = ToCell(PointerWorld());
                if (!Allowed(start)) return;

                _drawing = true;
                _dirKnown = false;
                _dir = Vector2Int.zero;
                _path.Clear();
                _path.Add(start);
                _previewDirty = true;
            }

            if (!PointerInput.Pressed || PointerInput.Up)
            {
                EndStroke();
                return;
            }

            Follow(ToCell(PointerWorld()));
            if (_previewDirty) RedrawPreview();
        }

        Vector2 PointerWorld()
        {
            Vector3 p = Cam.ScreenToWorldPoint(new Vector3(PointerInput.Position.x, PointerInput.Position.y,
                -Cam.transform.position.z));
            return new Vector2(p.x, p.y);
        }

        /// <summary>Округляет смещение до ближайшего из 8 направлений (как ферзь в шахматах).</summary>
        static Vector2Int Snap8(Vector2Int delta)
        {
            if (delta.x == 0 && delta.y == 0) return Vector2Int.zero;
            float angle = Mathf.Atan2(delta.y, delta.x);
            float snapped = Mathf.Round(angle / (Mathf.PI * 0.25f)) * (Mathf.PI * 0.25f);
            return new Vector2Int(Mathf.RoundToInt(Mathf.Cos(snapped)), Mathf.RoundToInt(Mathf.Sin(snapped)));
        }

        /// <summary>Двигает конец дороги к клетке под пальцем в одном из 8 направлений.</summary>
        void Follow(Vector2Int target)
        {
            for (int guard = 0; guard < 256; guard++)
            {
                Vector2Int tail = _path[_path.Count - 1];
                if (tail == target) return;

                // Откат назад по пути.
                int idx = _path.IndexOf(target);
                if (idx >= 0)
                {
                    _path.RemoveRange(idx + 1, _path.Count - idx - 1);
                    _dirKnown = false;
                    if (_path.Count >= 2)
                    {
                        _dir = Snap8(_path[_path.Count - 1] - _path[_path.Count - 2]);
                        _dirKnown = true;
                    }
                    _previewDirty = true;
                    return;
                }

                Vector2Int raw = target - tail;
                Vector2Int snap = Snap8(raw);

                if (!_dirKnown)
                {
                    _dir = snap;
                    _dirKnown = true;
                }
                else
                {
                    // Переключаем направление, если отклонение сильное (TurnThreshold клеток в сторону).
                    int along = raw.x * _dir.x + raw.y * _dir.y;
                    int acrossX = raw.x - _dir.x * along;
                    int acrossY = raw.y - _dir.y * along;
                    int acrossMag = Mathf.Abs(acrossX) + Mathf.Abs(acrossY);
                    if (acrossMag >= TurnThreshold && acrossMag > Mathf.Abs(along))
                    {
                        _dir = snap;
                    }
                }

                Vector2Int next = tail + _dir;

                if (!Allowed(next)) return;
                if (_path.Contains(next)) return;

                _path.Add(next);
                _previewDirty = true;
            }
        }

        /// <summary>Палец отпущен: строить пока не строим, ждём подтверждения.</summary>
        void EndStroke()
        {
            _drawing = false;
            if (_path.Count == 0 || PathCost() <= 0f)
            {
                Cancel();
                return;
            }

            _pending = true;
        }

        void Commit()
        {
            _drawing = false;
            ClearPreview();

            if (_path.Count == 0) return;

            // Платим за новые клетки. Не хватает денег: строим начало пути.
            float cost = _economy.RoadCostPerCell;
            var accepted = new List<Vector2Int>();
            float total = 0f;
            foreach (Vector2Int cell in _path)
            {
                float c = _map.Has(cell) ? 0f : cost * (CostFactorAt != null ? CostFactorAt(cell) : 1f);
                if (_economy.Money < total + c) break;
                total += c;
                accepted.Add(cell);
            }

            _path.Clear();
            if (accepted.Count == 0) return;

            if (total > 0f) _economy.TrySpend(total);
            _map.AddPath(accepted);
            Committed?.Invoke(accepted, total);
        }

        void RedrawPreview()
        {
            _previewDirty = false;
            ClearPreview();

            Dictionary<Vector2Int, int> masks = CellRoadMap.MasksOf(_path);
            _preview = new GameObject("RoadPreview");
            _preview.transform.SetParent(transform, false);
            _preview.AddComponent<MeshFilter>().sharedMesh =
                RoadCellMesher.Build(masks, null, null, CellSize, WidthFraction).ToMesh(RoadLayers.Road);
            var r = _preview.AddComponent<MeshRenderer>();
            r.sharedMaterial = _previewMaterial;
            r.sortingOrder = 16;
        }

        void ClearPreview()
        {
            if (_preview == null) return;
            var f = _preview.GetComponent<MeshFilter>();
            if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
            Destroy(_preview);
            _preview = null;
        }
    }
}
