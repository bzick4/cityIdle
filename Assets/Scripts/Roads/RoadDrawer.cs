using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Roads
{
    /// <summary>
    /// Рисование дороги пальцем или мышью: собираем точки, пока палец прижат,
    /// затем упрощаем, сглаживаем и передаём в RoadBuilder.
    /// </summary>
    public sealed class RoadDrawer : MonoBehaviour
    {
        [Header("Камера (если пусто, берётся Camera.main)")]
        public Camera Cam;

        [Header("Что рисуем")]
        public RoadType Type = RoadType.Local;

        [Header("Настройки в мировых единицах")]
        public float MinPointDistance = 0.2f;
        public float SimplifyEpsilon = 0.12f;
        public float SmoothSpacing = 0.25f;
        public float SnapRadius = 0.6f;
        public float MinRoadLength = 0.8f;

        /// <summary>Вернуть true, если палец сейчас над интерфейсом (шторка, кнопки). Рисование тогда не начинается.</summary>
        public Func<Vector2, bool> IsPointerBlocked;

        /// <summary>Правила секторов: вернуть false, если в этой точке строить нельзя. Точка тогда пропускается.</summary>
        public Func<Vector2, bool> CanBuildAt;

        public event Action<RoadEdge> RoadBuilt;

        RoadGraph _graph;
        readonly List<Vector2> _raw = new List<Vector2>();
        bool _drawing;
        LineRenderer _preview;

        public void Bind(RoadGraph graph)
        {
            _graph = graph;
        }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            CreatePreview();
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

            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = new Color(0.12f, 0.16f, 0.22f, 0.55f);
            _preview.sharedMaterial = material;
        }

        void Update()
        {
            if (_graph == null || Cam == null) return;

            PointerInput.Poll();

            if (!_drawing)
            {
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
            _preview.widthMultiplier = RoadSpecs.Width(Type);
            AddPoint();
        }

        void AddPoint()
        {
            Vector2 p = PointerWorld();
            if (CanBuildAt != null && !CanBuildAt(p)) return;

            if (_raw.Count == 0 || Vector2.Distance(_raw[_raw.Count - 1], p) >= MinPointDistance)
            {
                _raw.Add(p);
                UpdatePreview();
            }
        }

        void UpdatePreview()
        {
            _preview.positionCount = _raw.Count;
            for (int i = 0; i < _raw.Count; i++)
                _preview.SetPosition(i, new Vector3(_raw[i].x, _raw[i].y, 0f));
        }

        void Finish()
        {
            _drawing = false;
            _preview.positionCount = 0;

            if (_raw.Count < 2)
            {
                _raw.Clear();
                return;
            }

            List<Vector2> simplified = PolylineUtil.Simplify(_raw, SimplifyEpsilon);
            List<Vector2> smooth = PolylineUtil.SmoothCatmullRom(simplified, SmoothSpacing);
            _raw.Clear();

            RoadEdge edge = RoadBuilder.Build(_graph, smooth, Type, SnapRadius, MinRoadLength);
            if (edge != null) RoadBuilt?.Invoke(edge);
        }
    }
}
