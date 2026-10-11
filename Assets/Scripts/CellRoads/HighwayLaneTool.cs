using System;
using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Строит отдельное шоссе или обычную одностороннюю дорогу по сетке.
    /// Шоссе можно класть поверх обычной дороги; односторонняя дорога строится на свободных клетках.
    /// </summary>
    public sealed class HighwayLaneTool : MonoBehaviour
    {
        public Camera Cam;
        public float CellSize = 1f;
        public bool OneWayMode;
        public Func<Vector2, bool> IsPointerBlocked;
        public event Action<string> Message;

        CellRoadMap _map;
        CityModel _city;
        SectorMap _sectors;
        Economy _economy;
        public Func<Vector2Int, float> CostFactorAt;
        readonly List<Vector2Int> _path = new List<Vector2Int>();
        bool _drawing;
        bool _axisKnown;
        bool _horizontal;
        LineRenderer _preview;
        Material _previewMaterial;

        public void Bind(CellRoadMap map, CityModel city = null, SectorMap sectors = null, Economy economy = null)
        {
            _map = map;
            _city = city;
            _sectors = sectors;
            _economy = economy;
        }

        bool Allowed(Vector2Int cell)
        {
            if (_city != null && _city.HasCell(cell)) return false;
            if (_map.Has(cell)) return true;
            Vector2 center = new Vector2((cell.x + 0.5f) * CellSize, (cell.y + 0.5f) * CellSize);
            return _sectors == null || _sectors.IsOpenAt(center);
        }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            _previewMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = new Color32(0xF2, 0xB8, 0x4B, 0xD8)
            };
            var go = new GameObject("Highway direction preview");
            go.transform.SetParent(transform, false);
            _preview = go.AddComponent<LineRenderer>();
            _preview.useWorldSpace = true;
            _preview.sharedMaterial = _previewMaterial;
            _preview.positionCount = 0;
            _preview.numCornerVertices = 5;
            _preview.numCapVertices = 4;
            _preview.sortingOrder = 50;
            _preview.startColor = _preview.endColor = Color.white;
            _preview.widthMultiplier = CellSize * (OneWayMode ? 0.5f : 0.88f);
            _preview.enabled = false;
        }

        void OnDisable()
        {
            if (_drawing && _path.Count > 1) Commit();
            _drawing = false;
            _path.Clear();
            if (_preview != null) _preview.enabled = false;
        }

        void OnDestroy()
        {
            if (_previewMaterial != null) Destroy(_previewMaterial);
        }

        Vector2Int ToCell(Vector2 screen)
        {
            Vector3 p = Cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -Cam.transform.position.z));
            return new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));
        }

        void Update()
        {
            if (_map == null || Cam == null) return;
            PointerInput.Poll();

            if (PointerInput.Gesture)
            {
                _drawing = false;
                _path.Clear();
                if (_preview != null) _preview.enabled = false;
                return;
            }

            if (!_drawing && PointerInput.Down)
            {
                if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;
                Vector2Int start = ToCell(PointerInput.Position);
                if (!Allowed(start))
                {
                    Message?.Invoke(OneWayMode
                        ? "Односторонняя дорога строится на свободных клетках и соединяется с шоссе"
                        : "Шоссе строится на свободных клетках или поверх обычной дороги");
                    return;
                }

                HighwayLane startLane = _map.LaneAt(start);
                bool occupiedByOrdinaryRoad = _map.Has(start) && Side.Count(_map.Mask(start)) > 0 &&
                    !startLane.Active;
                if ((!OneWayMode && startLane.Active) ||
                    (OneWayMode && (startLane.OneWay || occupiedByOrdinaryRoad)))
                {
                    Message?.Invoke(OneWayMode
                        ? "Одностороннюю дорогу нельзя прокладывать поверх обычной дороги"
                        : "Здесь уже есть шоссе или односторонняя дорога");
                    return;
                }

                _drawing = true;
                _path.Clear();
                _path.Add(start);
                _axisKnown = false;
                UpdatePreview();
            }

            if (!_drawing) return;
            if (PointerInput.Pressed && !PointerInput.Up) Follow(ToCell(PointerInput.Position));
            if (PointerInput.Up) Commit();
        }

        void Follow(Vector2Int target)
        {
            for (int guard = 0; guard < 128; guard++)
            {
                Vector2Int tail = _path[_path.Count - 1];
                if (tail == target) return;
                int existing = _path.IndexOf(target);
                if (existing >= 0)
                {
                    _path.RemoveRange(existing + 1, _path.Count - existing - 1);
                    _axisKnown = false;
                    UpdatePreview();
                    return;
                }

                Vector2Int delta = target - tail;
                if (!_axisKnown)
                {
                    _horizontal = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
                    _axisKnown = true;
                }

                int along = _horizontal ? delta.x : delta.y;
                int across = _horizontal ? delta.y : delta.x;
                bool turn = across != 0 && (along == 0 ||
                    (Mathf.Abs(across) > Mathf.Abs(along) && Mathf.Abs(across) >= 1));
                if (turn)
                {
                    _horizontal = !_horizontal;
                    along = _horizontal ? delta.x : delta.y;
                }
                if (along == 0) return;

                Vector2Int step = _horizontal
                    ? new Vector2Int(along > 0 ? 1 : -1, 0)
                    : new Vector2Int(0, along > 0 ? 1 : -1);
                Vector2Int next = tail + step;
                int side = Side.FromDelta(next - tail);
                if (!Allowed(next) || _path.Contains(next)) return;
                HighwayLane nextLane = _map.LaneAt(next);
                if (!OneWayMode && nextLane.Active) return;
                if (OneWayMode)
                {
                    // Обычная односторонняя дорога не накладывается на уже занятую дорогу.
                    // Шоссе можно соединить или пересечь им для последующей установки моста.
                    if (nextLane.Active && nextLane.OneWay) return;
                    if (_map.Has(next) && Side.Count(_map.Mask(next)) > 0 && !nextLane.Active) return;
                }
                _path.Add(next);
                UpdatePreview();
            }
        }

        void Commit()
        {
            _drawing = false;
            float unitCost = _economy != null
                ? (OneWayMode ? _economy.RoadCostPerCell : _economy.HighwayCostPerCell)
                : 0f;
            var accepted = new List<Vector2Int>();
            float total = 0f;
            foreach (Vector2Int cell in _path)
            {
                float factor = CostFactorAt != null ? CostFactorAt(cell) : 1f;
                HighwayLane lane = _map.LaneAt(cell);
                bool highwayConnection = OneWayMode && lane.Active && !lane.OneWay;
                float cost = highwayConnection ? 0f : unitCost * factor;
                if (_economy != null && _economy.Money < total + cost) break;
                accepted.Add(cell);
                total += cost;
            }
            bool ok = accepted.Count >= 2 && (OneWayMode
                ? _map.SetOneWayPath(accepted)
                : _map.SetHighwayPath(accepted, true));
            if (ok && total > 0f && _economy != null) _economy.TrySpend(total);
            Message?.Invoke(!ok ? "Нужно провести маршрут минимум через две клетки" :
                OneWayMode ? "Односторонняя дорога построена" : "Шоссе построено · встречную полосу проложи рядом обратным ходом");
            _path.Clear();
            if (_preview != null) _preview.enabled = false;
        }

        void UpdatePreview()
        {
            if (_preview == null) return;
            _preview.positionCount = _path.Count;
            for (int i = 0; i < _path.Count; i++)
                _preview.SetPosition(i, new Vector3((_path[i].x + 0.5f) * CellSize,
                    (_path[i].y + 0.5f) * CellSize, -0.02f));
            _preview.widthMultiplier = CellSize * (OneWayMode ? 0.5f : 0.88f);
            _preview.enabled = _path.Count > 1;
        }
    }
}
