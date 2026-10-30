using System;
using System.Collections.Generic;
using CityGame;
using CityGame.Buildings;
using CityGame.CellRoads;
using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>
    /// Рисование свободной дороги: тап в начало (снап к узлу) → тянешь в конец → кубический безье.
    /// Касательная старта берётся от существующего ребра; конец снапится аналогично.
    /// После отпускания — Pending (подтверждение).
    /// </summary>
    public sealed class RoadDrawer : MonoBehaviour, IRoadStroke
    {
        [Header("Камера (если пусто, берётся Camera.main)")]
        public Camera Cam;

        [Header("Что рисуем")]
        public RoadType Type = RoadType.Local;
        public bool OneWayMode;

        [Header("Параметры (мировые единицы)")]
        public float SnapRadius    = 0.6f;
        public float MinRoadLength = 0.8f;

        // Оставлены для совместимости с Inspector, больше не используются.
        public float MinPointDistance = 0.2f;
        public float SimplifyEpsilon  = 0.12f;
        public float SmoothSpacing    = 0.25f;

        public Func<Vector2, bool>  IsPointerBlocked;
        public Func<Vector2, bool>  CanBuildAt;
        public Economy              Economy;
        public Func<Vector2, float> CostFactorAt;

        // ——— IRoadStroke ———
        public bool                        Drawing     => _drawing;
        public bool                        Pending     => _pending;
        public string                      Title       => Type == RoadType.Highway ? "Шоссе" : OneWayMode ? "Односторонняя" : "Дорога";
        public string                      Problem     => null;
        public IReadOnlyList<Vector2Int>   Path        => _emptyPath;
        public float                       Money       => Economy != null ? Economy.Money : 0f;
        public float                       StrokeCost  => _pendingCost;
        public float                       StrokeWidth => RoadSpecs.Width(Type);
        public string                      LengthLabel => _pendingLength > 0f ? Mathf.RoundToInt(_pendingLength) + " м." : "";
        Camera IRoadStroke.Cam => Cam;

        public void Confirm()
        {
            if (!_pending) return;
            _pending = false;
            CommitEdge();
        }

        public void Cancel()
        {
            _pending      = false;
            _drawing      = false;
            _pendingPoints = null;
            _pendingCost   = 0f;
            _pendingLength = 0f;
            _curve         = null;
            UpdatePreviewLine();
        }

        public event Action<IReadOnlyList<Vector2Int>, float> Committed;
        public event Action<RoadEdge>                         RoadBuilt;

        // ——————————————

        RoadGraph _graph;
        static readonly List<Vector2Int> _emptyPath = new List<Vector2Int>();

        bool    _drawing;
        bool    _pending;

        // Состояние активного штриха
        Vector2   _startPoint;
        Vector2   _startTangent; // ненулевой после первого drag
        RoadNode  _startNode;
        Vector2   _endPoint;
        RoadNode  _endNode;
        List<Vector2> _curve;

        // Состояние pending
        List<Vector2> _pendingPoints;
        float         _pendingCost;
        float         _pendingLength;

        LineRenderer _preview;
        Material     _previewMaterial;

        public void Bind(RoadGraph graph) { _graph = graph; }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            CreatePreview();
        }

        void OnDisable() => Cancel();

        void OnDestroy()
        {
            if (_previewMaterial != null) Destroy(_previewMaterial);
        }

        void CreatePreview()
        {
            var go = new GameObject("RoadPreview");
            go.transform.SetParent(transform, false);
            _preview = go.AddComponent<LineRenderer>();
            _preview.useWorldSpace    = true;
            _preview.numCapVertices   = 4;
            _preview.numCornerVertices = 2;
            _preview.sortingOrder     = 100;
            _preview.positionCount    = 0;

            _previewMaterial = new Material(Shader.Find("Sprites/Default"));
            _preview.sharedMaterial = _previewMaterial;
            RefreshPreviewStyle();
        }

        void RefreshPreviewStyle()
        {
            if (_previewMaterial == null) return;
            Color c = RoadSpecs.BodyColor(Type);
            _previewMaterial.color       = new Color(c.r, c.g, c.b, 0.6f);
            if (_preview != null) _preview.widthMultiplier = RoadSpecs.Width(Type);
        }

        // ——— Update ———

        void Update()
        {
            if (_graph == null || Cam == null) return;
            PointerInput.Poll();

            if (!_drawing)
            {
                if (_pending) return;
                if (PointerInput.Down)
                {
                    if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;
                    BeginAt(PointerWorld());
                }
                return;
            }

            if (PointerInput.Pressed && !PointerInput.Up)
                UpdateEnd(PointerWorld());
            else
                FinishDraw();
        }

        Vector2 PointerWorld()
        {
            return GroundRay.ToGround(PointerInput.Position, Cam);
        }

        // ——— Стейт-машина рисования ———

        void BeginAt(Vector2 world)
        {
            RoadNode snap = _graph.FindNodeNear(world, SnapRadius, RoadSpecs.LayerFor(Type));
            if (snap != null)
            {
                _startPoint   = snap.Position;
                _startTangent = ContinuationTangent(snap);
                _startNode    = snap;
            }
            else
            {
                if (CanBuildAt != null && !CanBuildAt(world)) return;
                _startPoint   = world;
                _startTangent = Vector2.zero;
                _startNode    = null;
            }

            _endPoint = _startPoint;
            _endNode  = null;
            _curve    = null;
            _drawing  = true;
            RefreshPreviewStyle();
            UpdatePreviewLine();
        }

        void UpdateEnd(Vector2 world)
        {
            RoadNode snap = _graph.FindNodeNear(world, SnapRadius, RoadSpecs.LayerFor(Type));
            if (snap != null && snap != _startNode)
            {
                _endPoint = snap.Position;
                _endNode  = snap;
            }
            else
            {
                _endNode = null;
                if (CanBuildAt != null && !CanBuildAt(world)) return;
                _endPoint = world;
            }

            float dist = Vector2.Distance(_startPoint, _endPoint);

            // Инициализируем касательную старта от первого значимого движения
            if (_startTangent == Vector2.zero && dist > 0.25f)
                _startTangent = (_endPoint - _startPoint).normalized;

            if (dist > 0.05f) RebuildCurve();
            else              _curve = null;

            UpdatePreviewLine();
        }

        void FinishDraw()
        {
            _drawing = false;
            float len = _curve != null ? PolylineUtil.Length(_curve) : 0f;
            if (_curve == null || _curve.Count < 2 || len < MinRoadLength)
            {
                _curve = null;
                UpdatePreviewLine();
                return;
            }

            _pendingPoints = _curve;
            _curve         = null;
            _pendingLength = len;
            _pendingCost   = CalcCost(_pendingPoints);
            _pending       = true;
            UpdatePreviewLine();
        }

        // ——— Безье ———

        void RebuildCurve()
        {
            float dist = Vector2.Distance(_startPoint, _endPoint);

            // Касательная старта: от ребра или вдоль хорды
            Vector2 chord = (_endPoint - _startPoint).normalized;
            Vector2 t0 = _startTangent == Vector2.zero ? chord : _startTangent;

            // Защита от разворота (острый угол к хорде)
            if (Vector2.Dot(t0, chord) < -0.1f)
                t0 = Vector2.Lerp(chord, t0, 0.3f).normalized;

            // Касательная конца: касательная конечного узла или хорда
            Vector2 t1;
            if (_endNode != null)
            {
                Vector2 ec = ContinuationTangent(_endNode);
                t1 = ec != Vector2.zero ? ec : chord;
            }
            else
            {
                t1 = chord;
            }

            float h = dist * 0.38f;
            Vector2 p1 = _startPoint + t0 * h;
            Vector2 p2 = _endPoint   - t1 * h;

            int steps = Mathf.Clamp(Mathf.RoundToInt(dist / 0.18f), 6, 64);
            _curve = new List<Vector2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1f - t;
                _curve.Add(u*u*u * _startPoint
                         + 3f*u*u*t * p1
                         + 3f*u*t*t * p2
                         + t*t*t * _endPoint);
            }
        }

        /// <summary>
        /// Направление продолжения дороги от узла (берётся из единственного ребра;
        /// у развилки — нулевой вектор, тогда используется направление drag).
        /// </summary>
        Vector2 ContinuationTangent(RoadNode node)
        {
            if (node == null || node.EdgeIds.Count != 1) return Vector2.zero;

            RoadEdge e = _graph.GetEdge(node.EdgeIds[0]);
            if (e == null || e.Points.Count < 2) return Vector2.zero;

            var pts = e.Points;
            // Направление «наружу» от узла = продолжение дороги
            return e.NodeA == node.Id
                ? (pts[1]              - pts[0]).normalized
                : (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized;
        }

        // ——— Превью ———

        void UpdatePreviewLine()
        {
            List<Vector2> pts = _pending ? _pendingPoints : _curve;
            if (pts == null || pts.Count < 2) { _preview.positionCount = 0; return; }
            _preview.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++)
                _preview.SetPosition(i, new Vector3(pts[i].x, pts[i].y, 0f));
        }

        // ——— Экономика ———

        float CalcCost(List<Vector2> pts)
        {
            if (Economy == null) return 0f;
            float pricePerUnit = Type == RoadType.Highway ? Economy.HighwayCostPerCell : Economy.RoadCostPerCell;
            float length       = PolylineUtil.Length(pts);
            float factor       = 1f;

            if (CostFactorAt != null)
            {
                int samples = Mathf.Clamp(pts.Count, 2, 8);
                float sum = 0f;
                for (int i = 0; i < samples; i++)
                {
                    float t   = i / (float)(samples - 1);
                    int   idx = Mathf.RoundToInt(t * (pts.Count - 1));
                    sum += CostFactorAt(pts[idx]);
                }
                factor = sum / samples;
            }
            return length * pricePerUnit * factor;
        }

        void CommitEdge()
        {
            if (_pendingPoints == null) return;
            List<Vector2> pts  = _pendingPoints;
            _pendingPoints     = null;
            float cost         = _pendingCost;
            _pendingCost       = 0f;
            _pendingLength     = 0f;
            _preview.positionCount = 0;

            if (Economy != null && cost > 0f && !Economy.TrySpend(cost))
            {
                float pricePerUnit = Type == RoadType.Highway ? Economy.HighwayCostPerCell : Economy.RoadCostPerCell;
                float affordable   = Economy.Money;
                float firstSeg     = pricePerUnit * (CostFactorAt != null ? CostFactorAt(pts[0]) : 1f);
                if (affordable < firstSeg) return;

                int   split = pts.Count - 1;
                float acc   = 0f;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    float f = CostFactorAt != null ? CostFactorAt(pts[i]) : 1f;
                    acc += Vector2.Distance(pts[i], pts[i + 1]) * pricePerUnit * f;
                    if (acc > affordable) { split = i + 1; break; }
                }
                pts  = pts.GetRange(0, split + 1);
                cost = Mathf.Min(acc, affordable);
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
