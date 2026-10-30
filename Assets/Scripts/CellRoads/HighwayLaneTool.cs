using System;
using CityGame;
using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.CellRoads
{
    /// <summary>
    /// Строит отдельное шоссе или обычную одностороннюю дорогу по сетке.
    /// Оба типа можно продолжать от своего свободного конца; шоссе также кладётся поверх обычной дороги.
    /// </summary>
    public sealed class HighwayLaneTool : MonoBehaviour, IRoadStroke
    {
        public Camera Cam;
        public float CellSize = 1f;
        [Tooltip("Смещение пальца в сторону для поворота маршрута, в клетках")]
        public int TurnThreshold = 2;
        public bool OneWayMode;
        public Func<Vector2, bool> IsPointerBlocked;
        public event Action<string> Message;

        CellRoadMap _map;
        CityModel _city;
        SectorMap _sectors;
        Economy _economy;
        public Func<Vector2Int, float> CostFactorAt;

        /// <summary>Клетка занята чем-то кроме зданий (например, портом): дорогу не провести.</summary>
        public Func<Vector2Int, bool> IsOccupied;
        readonly List<Vector2Int> _path = new List<Vector2Int>();
        bool _drawing;
        bool _axisKnown;
        bool _horizontal;
        LineRenderer _preview;
        Material _previewMaterial;
        bool _pending;
        bool _trimmed;
        string _problem;
        public string Problem => _problem;
        public string LengthLabel => _path.Count + " кл.";

        public bool Drawing => _drawing;
        public bool Pending => _pending;
        public string Title => OneWayMode ? "Односторонняя дорога" : "Шоссе";
        public IReadOnlyList<Vector2Int> Path => _path;
        public float Money => _economy != null ? _economy.Money : 0f;
        public float StrokeWidth => OneWayMode ? 0.5f : 0.88f;
        Camera IRoadStroke.Cam => Cam;
        public float StrokeCost => Evaluate(null, false);
        public event Action<IReadOnlyList<Vector2Int>, float> Committed;

        public void Confirm()
        {
            if (!_pending || _problem != null) return;
            _pending = false;
            Commit();
        }

        public void Cancel()
        {
            _problem = null;
            _pending = false;
            _drawing = false;
            _path.Clear();
            if (_preview != null) _preview.enabled = false;
        }

        /// <summary>Цена маршрута по клеткам. Если respectMoney, считает только то, на что хватает денег.</summary>
        float Evaluate(List<Vector2Int> accepted, bool respectMoney)
        {
            float unitCost = _economy != null
                ? (OneWayMode ? _economy.RoadCostPerCell : _economy.HighwayCostPerCell)
                : 0f;
            float total = 0f;
            bool closed = Closed;
            for (int i = 0; i < _path.Count; i++)
            {
                Vector2Int cell = _path[i];
                float factor = CostFactorAt != null ? CostFactorAt(cell) : 1f;
                HighwayLane lane = _map.LaneAt(cell);
                bool bridge = _map.IsCrossing(_path, i); // мост строится заново, он не бесплатный
                bool existingLane = lane.Active && lane.OneWay == OneWayMode && !bridge;
                bool highwayConnection = OneWayMode && !bridge &&
                    ((lane.Active && !lane.OneWay) || (!lane.Active && _map.Mask(cell) != 0));
                bool closingCell = closed && i == _path.Count - 1; // клетка замыкания уже оплачена как первая
                float cost = existingLane || highwayConnection || closingCell ? 0f : unitCost * factor;
                if (respectMoney && _economy != null && _economy.Money < total + cost) break;
                if (accepted != null) accepted.Add(cell);
                total += cost;
            }

            return total;
        }

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
            if (IsOccupied != null && IsOccupied(cell)) return false;
            if (_map.Has(cell)) return true;
            Vector2 center = new Vector2((cell.x + 0.5f) * CellSize, (cell.y + 0.5f) * CellSize);
            return _sectors == null || _sectors.IsOpenAt(center);
        }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            Color previewColor = RoadSpecs.BodyColor(RoadType.Local);
            previewColor.a = 0.6f;
            _previewMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = previewColor
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
            Cancel();
        }

        void OnDestroy()
        {
            if (_previewMaterial != null) Destroy(_previewMaterial);
        }

        Vector2Int ToCell(Vector2 screen)
        {
            Vector2 p = GroundRay.ToGround(screen, Cam);
            return new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));
        }

        void Update()
        {
            if (_map == null || Cam == null) return;
            PointerInput.Poll();

            if (PointerInput.Gesture)
            {
                _pending = false;
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
                        ? "Односторонняя дорога строится на свободных клетках и может пересекать обычные дороги и шоссе"
                        : "Шоссе строится на свободных клетках или поверх обычной дороги");
                    return;
                }

                HighwayLane startLane = _map.LaneAt(start);
                bool canExtendCurrentType = startLane.Active && startLane.OneWay == OneWayMode &&
                                            Side.Count(_map.Mask(start)) == 1;
                bool oneWayCanJoinHighway = OneWayMode && startLane.Active && !startLane.OneWay;
                // Шоссе можно отвести в сторону от середины другого шоссе.
                bool highwayCanBranch = !OneWayMode && startLane.Active && !startLane.OneWay &&
                                        Side.Count(_map.Mask(start)) == 2;
                // Съезд: от середины односторонней дороги можно пустить новую в свободную сторону.
                bool oneWayCanBranch = OneWayMode && startLane.Active && startLane.OneWay &&
                                       Side.Count(_map.Mask(start)) == 2;
                if (startLane.Active && !canExtendCurrentType && !oneWayCanJoinHighway && !highwayCanBranch &&
                    !oneWayCanBranch)
                {
                    Message?.Invoke("Здесь уже проходит другой тип дороги");
                    return;
                }

                _pending = false;
                _drawing = true;
                _path.Clear();
                _path.Add(start);
                _axisKnown = false;
                UpdatePreview();
            }

            if (!_drawing) return;
            if (PointerInput.Pressed && !PointerInput.Up) Follow(ToCell(PointerInput.Position));
            if (PointerInput.Up) EndStroke();
        }

        /// <summary>Минимум клеток до замыкания кольца: периметр квадрата 3×3.</summary>
        const int MinRingCells = 8;

        bool Closed => _path.Count > 2 && _path[0] == _path[_path.Count - 1];

        void Follow(Vector2Int target)
        {
            for (int guard = 0; guard < 128; guard++)
            {
                Vector2Int tail = _path[_path.Count - 1];
                if (tail == target) return;
                int existing = _path.IndexOf(target);
                bool toRingStart = existing == 0 && !Closed && _path.Count >= MinRingCells &&
                                   !_map.LaneAt(_path[0]).Active;
                if (toRingStart && Mathf.Abs(tail.x - target.x) + Mathf.Abs(tail.y - target.y) == 1)
                {
                    // Тянем к началу и на последнем шаге замыкаем кольцо (как МКАД).
                    _path.Add(target);
                    UpdatePreview();
                    return;
                }

                // Палец прошёл над началом длинного маршрута, но хвост ещё не рядом: не откатываем маршрут,
                // а продолжаем тянуть к цели.
                if (existing >= 0 && !toRingStart)
                {
                    _path.RemoveRange(existing + 1, _path.Count - existing - 1);
                    _axisKnown = false;
                    UpdatePreview();
                    return;
                }

                if (Closed) return; // кольцо замкнуто: дальше тянуть некуда, можно только откатить назад

                Vector2Int delta = target - tail;
                if (!_axisKnown)
                {
                    _horizontal = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
                    _axisKnown = true;
                }

                int along = _horizontal ? delta.x : delta.y;
                int across = _horizontal ? delta.y : delta.x;
                // Не меняем ось, пока не дошли до цели по текущему направлению.
                // Иначе при диагональном движении палец заставляет маршрут метаться между осями.
                if (along == 0 && Mathf.Abs(across) >= Mathf.Max(1, TurnThreshold))
                {
                    _horizontal = !_horizontal;
                    along = _horizontal ? delta.x : delta.y;
                }
                if (along == 0) return;

                Vector2Int step = _horizontal
                    ? new Vector2Int(along > 0 ? 1 : -1, 0)
                    : new Vector2Int(0, along > 0 ? 1 : -1);
                Vector2Int next = tail + step;
                if (!CanStep(tail, next)) return;

                _path.Add(next);
                UpdatePreview();
            }
        }

        /// <summary>
        /// Можно ли продлить маршрут с клетки from на соседнюю to.
        /// Шоссе и односторонние дороги проходят друг сквозь друга по прямой: на пересечении строится мост.
        /// Если маршрут заканчивается на чужой полосе, это присоединение (развилка), а не мост.
        /// Односторонняя дорога может примыкать к повороту шоссе (съезд и въезд) с любой свободной стороны.
        /// </summary>
        bool CanStep(Vector2Int from, Vector2Int to)
        {
            if (!Allowed(to) || _path.Contains(to)) return false;
            HighwayLane lane = _map.LaneAt(to);
            int side = Side.FromDelta(to - from);
            if (side == 0) return false;
            // Вторая односторонняя дорога навстречу первой от конца обычной дороги: общий участок
            // и расхождение (развилка).
            if (OneWayMode)
            {
                var probe = new List<Vector2Int>(_path) { to };
                if (_map.IsForkPrefix(probe)) return true;
            }

            HighwayLane fromLane = _map.LaneAt(from);
            int fromMask = _map.Mask(from);

            if (_path.Count == 1 && fromLane.Active)
            {
                // Со своей полосы или с чужой можно уйти только в свободную сторону, не вдоль готовой связи.
                if ((fromMask & side) != 0) return false;
                // Отвод от середины шоссе идёт только вбок, а не вдоль него.
                if (!OneWayMode && Side.Count(fromMask) == 2 && (fromMask & Side.Opposite(side)) != 0) return false;
            }

            if (_path.Count > 1 && fromLane.Active)
            {
                // Мы стоим на чужой полосе: идти дальше можно только по прямой сквозь неё (мост).
                // Иначе маршрут здесь кончается (присоединение к середине, концу или повороту).
                bool straight = Side.FromDelta(from - _path[_path.Count - 2]) == side;
                if (!(straight && _map.CanCross(from, side))) return false;
            }

            if (!OneWayMode)
            {
                if (!lane.Active) return true;
                if (lane.OneWay) return _map.CanCross(to, side); // односторонняя поперёк: мост
                int toMask = _map.Mask(to);
                if (Side.Count(toMask) == 1) return (toMask & Side.Opposite(side)) == 0; // конец шоссе
                // Середина шоссе: примыкаем боковым подходом (развилка) или проходим сквозь мостом.
                return Side.Count(toMask) == 2 && (toMask & (side | Side.Opposite(side))) == 0;
            }

            if (lane.Active && lane.OneWay)
            {
                int toMask = _map.Mask(to);
                if (Side.Count(toMask) == 1) return (toMask & Side.Opposite(side)) == 0; // конец: соединение
                // Середина: мост насквозь, либо въезд/съезд с свободной стороны (поворот и прямая).
                return _map.CanCross(to, side) ||
                       (Side.Count(toMask) == 2 && (toMask & Side.Opposite(side)) == 0);
            }
            // Шоссе: примыкаем к концу, к середине, к повороту (съезд) или проходим мостом. Заходим только с свободной стороны.
            if (lane.Active) return (_map.Mask(to) & Side.Opposite(side)) == 0;
            int mask = _map.Mask(to);
            if (mask == 0) return true;
            // Обычная дорога: клетка не должна быть уже соединена с нашей стороны, то есть не вдоль дороги.
            return (mask & Side.Opposite(side)) == 0;
        }

        /// <summary>
        /// Если палец отпущен на клетке середины односторонней полосы, присоединиться там нельзя:
        /// такой хвост (мост без выезда) отбрасываем.
        /// </summary>
        void TrimDanglingBridge()
        {
            while (_path.Count > 2)
            {
                Vector2Int last = _path[_path.Count - 1];
                HighwayLane lane = _map.LaneAt(last);
                bool dangling = lane.Active && lane.OneWay && Side.Count(_map.Mask(last)) >= 2;
                if (dangling && Side.Count(_map.Mask(last)) == 2)
                {
                    // Отпустили палец на середине односторонней дороги с её свободной стороны: это въезд, не мост.
                    int link = Side.FromDelta(_path[_path.Count - 2] - last);
                    if (link != 0 && (_map.Mask(last) & link) == 0) break;
                }
                if (dangling) _path.RemoveAt(_path.Count - 1);
                else break;
            }
        }

        string FailMessage(List<Vector2Int> accepted)
        {
            return accepted.Count < 2
                ? "Нужно провести маршрут минимум через две клетки"
                : _map.LaneAt(accepted[0]).Active && _map.LaneAt(accepted[accepted.Count - 1]).Active
                    ? "Эти концы не соединить: направления встречные. Веди от выезда одной полосы ко въезду другой"
                    : OneWayMode
                        ? "Соединять можно выезд одной дороги с въездом другой. Чтобы пересечь дорогу мостом, веди прямо и дальше за неё. Развилка: от конца или начала обычной дороги проведи вторую полосу по той же дороге и разведи в сторону"
                        : "Соединять можно выезд одной полосы с въездом другой, либо примыкать сбоку к середине шоссе";
        }

        /// <summary>Палец отпущен: подготавливаем маршрут и ждём подтверждения.</summary>
        void EndStroke()
        {
            _drawing = false;
            int pathBefore = _path.Count;
            if (!Closed) TrimDanglingBridge();
            _trimmed = _path.Count < pathBefore;
            // Отпустили палец рядом с началом: замыкаем кольцо сами, не заставляя попадать точно в первую клетку.
            if (!Closed && _path.Count >= MinRingCells && !_map.LaneAt(_path[0]).Active &&
                !_map.LaneAt(_path[_path.Count - 1]).Active)
            {
                Vector2Int a = _path[_path.Count - 1], b = _path[0];
                if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1) _path.Add(b);
            }

            if (_path.Count < 2)
            {
                Commit(); // сообщит, что нужно минимум две клетки
                return;
            }

            // Пробная постройка: сразу видно, получится ли такой маршрут.
            var all = new List<Vector2Int>();
            Evaluate(all, false);
            bool fits = OneWayMode ? _map.DryRunPath(all, true) : _map.DryRunPath(all, false);
            _problem = fits ? null : FailMessage(all);
            _pending = true;
            UpdatePreview();
        }

        void Commit()
        {
            _drawing = false;
            bool trimmed = _trimmed;
            var accepted = new List<Vector2Int>();
            float total = Evaluate(accepted, true);
            int bridges = 0;
            for (int i = 0; i < accepted.Count; i++)
                if (_map.IsCrossing(accepted, i)) bridges++;
            bool ok = accepted.Count >= 2 && (OneWayMode
                ? _map.SetOneWayPath(accepted)
                : _map.SetHighwayPath(accepted, true));
            if (ok && total > 0f && _economy != null) _economy.TrySpend(total);
            if (ok) Committed?.Invoke(accepted, total);
            bool ringBuilt = ok && CellRoadMap.IsClosedPath(accepted);
            Message?.Invoke(!ok ? FailMessage(accepted) :
                _map.LastForkBuilt ? (_map.LastForkReversed
                    ? "Развилка построена · вторая полоса идёт навстречу первой, общий участок стал обычной дорогой"
                    : "Развилка построена · общий участок стал обычной двухполосной дорогой") :
                _map.LastJoinFlipped ? "Концы соединены · направление одной из дорог развёрнуто, чтобы потоки сошлись" :
                bridges > 0 ? (bridges == 1 ? "Построен мост" : "Построены мосты: " + bridges + " · дороги на пересечении не соединены") :
                trimmed ? "Мост нужно довести до конца: отпусти палец за пересекаемой дорогой" :
                ringBuilt ? (OneWayMode ? "Кольцо односторонней дороги замкнуто" : "Кольцевое шоссе замкнуто · встречное кольцо проложи рядом обратным ходом") :
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
