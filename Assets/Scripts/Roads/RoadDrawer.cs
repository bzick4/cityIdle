using System;
using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.CellRoads;
using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>
    /// Рисование свободной дороги пальцем или мышью: собираем точки, пока палец прижат,
    /// упрощаем, сглаживаем и передаём в RoadBuilder.
    /// После отпускания палец ставит дорогу в режим ожидания (Pending): нужно нажать «Подтвердить».
    /// </summary>
    public sealed class RoadDrawer : MonoBehaviour, IRoadStroke
    {
        [Header("Камера (если пусто, берётся Camera.main)")]
        public Camera Cam;

        [Header("Что рисуем")]
        public RoadType Type = RoadType.Local;

        /// <summary>Режим односторонней дороги: ребро получает OneWay = 1.</summary>
        public bool OneWayMode;

        [Header("Настройки в мировых единицах")]
        public float MinPointDistance = 0.2f;
        public float SimplifyEpsilon = 0.12f;
        public float SmoothSpacing = 0.25f;
        public float SnapRadius = 0.6f;
        public float MinRoadLength = 0.8f;

        /// <summary>Вернуть true, если палец сейчас над интерфейсом. Рисование тогда не начинается.</summary>
        public Func<Vector2, bool> IsPointerBlocked;

        /// <summary>Правила секторов: вернуть false, если в этой точке строить нельзя.</summary>
        public Func<Vector2, bool> CanBuildAt;

        /// <summary>Экономика для расчёта стоимости и списания денег.</summary>
        public Economy Economy;

        /// <summary>Множитель цены за мировую единицу длины для точки (например, мост дороже). Null — без множителя.</summary>
        public Func<Vector2, float> CostFactorAt;

        // ——— IRoadStroke ———
        public bool Drawing => _drawing;
        public bool Pending => _pending;
        public string Title => Type == RoadType.Highway ? "Шоссе" : OneWayMode ? "Односторонняя" : "Дорога";
        public string Problem => null;
        public IReadOnlyList<Vector2Int> Path => _emptyPath;
        public float Money => Economy != null ? Economy.Money : 0f;
        public float StrokeCost => _pendingCost;
        public float StrokeWidth => RoadSpecs.Width(Type);
        public string LengthLabel => _pendingLength > 0f ? Mathf.RoundToInt(_pendingLength) + " м." : "";
        Camera IRoadStroke.Cam => Cam;

        public void Confirm()
        {
            if (!_pending) return;
            _pending = false;
            CommitEdge();
        }

        public void Cancel()
        {
            _pending = false;
            _drawing = false;
            _raw.Clear();
            _pendingPoints = null;
            _pendingCost = 0f;
            _pendingLength = 0f;
            UpdatePreviewLine();
        }

        /// <summary>Дорога построена: переданы пустой список клеток и потраченная сумма.</summary>
        public event Action<IReadOnlyList<Vector2Int>, float> Committed;

        public event Action<RoadEdge> RoadBuilt;

        // ——————————————

        RoadGraph _graph;
        readonly List<Vector2> _raw = new List<Vector2>();
        static readonly List<Vector2Int> _emptyPath = new List<Vector2Int>();
        bool _drawing;
        bool _pending;
        List<Vector2> _pendingPoints;
        float _pendingCost;
        float _pendingLength;
        LineRenderer _preview;
        Material _previewMaterial;

        public void Bind(RoadGraph graph)
        {
            _graph = graph;
        }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            CreatePreview();
        }

        void OnDisable()
        {
            Cancel();
        }

        void OnDestroy()
        {
            if (_previewMaterial != null) Destroy(_previewMaterial);
        }

        void CreatePreview()
        {
            var go = new GameObject("RoadPreview");
            go.transform.SetParent(transform, false);
            _preview = go.AddComponent<LineRenderer>();
            _preview.useWorldSpace = true;
            _preview.numCapVertices = 4;
            _preview.numCornerVertices = 2;
            _preview.sortingOrder = 100;
            _preview.positionCount = 0;

            _previewMaterial = new Material(Shader.Find("Sprites/Default"));
            _preview.sharedMaterial = _previewMaterial;
            RefreshPreviewStyle();
        }

        void RefreshPreviewStyle()
        {
            if (_previewMaterial == null) return;
            Color c = RoadSpecs.BodyColor(Type);
            _previewMaterial.color = new Color(c.r, c.g, c.b, 0.6f);
            if (_preview != null) _preview.widthMultiplier = RoadSpecs.Width(Type);
        }

        void Update()
        {
            if (_graph == null || Cam == null) return;

            PointerInput.Poll();

            if (!_drawing)
            {
                if (_pending) return;
                if (PointerInput.Down)
                {
                    bool blocked = IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position);
                    if (!blocked) Begin();
                }
                return;
            }

            if (PointerInput.Pressed && !PointerInput.Up) AddPoint();
            else Finish();
        }

        Vector2 PointerWorld()
        {
            Vector3 p = Cam.ScreenToWorldPoint(new Vector3(PointerInput.Position.x, PointerInput.Position.y,
                -Cam.transform.position.z));
            return new Vector2(p.x, p.y);
        }

        void Begin()
        {
            _drawing = true;
            _raw.Clear();
            RefreshPreviewStyle();
            AddPoint();
        }

        void AddPoint()
        {
            Vector2 p = PointerWorld();
            if (CanBuildAt != null && !CanBuildAt(p)) return;

            if (_raw.Count == 0 || Vector2.Distance(_raw[_raw.Count - 1], p) >= MinPointDistance)
            {
                _raw.Add(p);
                UpdatePreviewLine();
            }
        }

        void UpdatePreviewLine()
        {
            if (_raw.Count >= 2)
            {
                _preview.positionCount = _raw.Count;
                for (int i = 0; i < _raw.Count; i++)
                    _preview.SetPosition(i, new Vector3(_raw[i].x, _raw[i].y, 0f));
            }
            else if (_pendingPoints != null)
            {
                _preview.positionCount = _pendingPoints.Count;
                for (int i = 0; i < _pendingPoints.Count; i++)
                    _preview.SetPosition(i, new Vector3(_pendingPoints[i].x, _pendingPoints[i].y, 0f));
            }
            else
            {
                _preview.positionCount = 0;
            }
        }

        void Finish()
        {
            _drawing = false;

            if (_raw.Count < 2)
            {
                _raw.Clear();
                return;
            }

            List<Vector2> simplified = PolylineUtil.Simplify(_raw, SimplifyEpsilon);
            List<Vector2> smooth = PolylineUtil.SmoothCatmullRom(simplified, SmoothSpacing);
            _raw.Clear();

            if (smooth == null || smooth.Count < 2 || PolylineUtil.Length(smooth) < MinRoadLength)
                return;

            _pendingPoints = smooth;
            _pendingLength = PolylineUtil.Length(smooth);
            _pendingCost = CalcCost(smooth);
            _pending = true;
            UpdatePreviewLine();
        }

        float CalcCost(List<Vector2> pts)
        {
            if (Economy == null) return 0f;
            float pricePerUnit = Type == RoadType.Highway ? Economy.HighwayCostPerCell : Economy.RoadCostPerCell;
            float length = PolylineUtil.Length(pts);
            float factor = 1f;
            if (CostFactorAt != null)
            {
                int samples = Mathf.Max(2, Mathf.Min(8, pts.Count));
                float sum = 0f;
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)(samples - 1);
                    int idx = Mathf.RoundToInt(t * (pts.Count - 1));
                    sum += CostFactorAt(pts[idx]);
                }
                factor = sum / samples;
            }
            return length * pricePerUnit * factor;
        }

        void CommitEdge()
        {
            if (_pendingPoints == null) return;
            List<Vector2> pts = _pendingPoints;
            _pendingPoints = null;
            float cost = _pendingCost;
            _pendingCost = 0f;
            _pendingLength = 0f;
            _preview.positionCount = 0;

            if (Economy != null && cost > 0f && !Economy.TrySpend(cost))
            {
                // Нет денег: строим столько, сколько хватает.
                float pricePerUnit = Type == RoadType.Highway ? Economy.HighwayCostPerCell : Economy.RoadCostPerCell;
                float affordable = Economy.Money;
                float needed = pricePerUnit;
                if (CostFactorAt != null) needed *= CostFactorAt(pts[0]);
                if (affordable < needed)
                    return;
                float budget = affordable;
                int split = pts.Count - 1;
                float acc = 0f;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    float segLen = Vector2.Distance(pts[i], pts[i + 1]);
                    float f = CostFactorAt != null ? CostFactorAt(pts[i]) : 1f;
                    acc += segLen * pricePerUnit * f;
                    if (acc > budget) { split = i + 1; break; }
                }
                pts = pts.GetRange(0, split + 1);
                cost = acc - (acc > budget ? (acc - budget) : 0f);
                if (pts.Count < 2) return;
                Economy.TrySpend(cost);
            }

            RoadEdge edge = RoadBuilder.Build(_graph, pts, Type, SnapRadius, MinRoadLength);
            if (edge != null)
            {
                if (OneWayMode) edge.OneWay = 1;
                RoadBuilt?.Invoke(edge);
            }
            Committed?.Invoke(_emptyPath, cost);
        }
    }
}
