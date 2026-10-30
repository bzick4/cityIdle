using CityGame.Roads;
using UnityEngine;

namespace CityGame
{
    /// <summary>
    /// Камера под вертикальный телефон. Держит постоянной ширину видимой карты, а не высоту,
    /// поэтому картинка одинакова на любых пропорциях экрана.
    ///
    /// Управление:
    /// - два пальца: щипок приближает и отдаляет, движение двух пальцев двигает карту (точка под пальцами остаётся под ними);
    /// - один палец в режиме «Рука»: двигает карту;
    /// - в редакторе: колесо мыши масштабирует, правая или средняя кнопка двигает.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] Camera _cam;
        public Camera Cam => _cam;

        [Tooltip("Ширина видимой карты в мировых единицах")]
        [SerializeField] float _worldWidth = 18f;
        public float WorldWidth => _worldWidth;
        [SerializeField] float _minWorldWidth = 6f;
        [SerializeField] float _maxWorldWidth = 60f;
        [SerializeField] Color _background = new Color32(0x2B, 0x31, 0x3A, 0xFF);

        [Tooltip("Один палец двигает карту (режим «Рука»)")]
        public bool PanEnabled { get; set; }
        public System.Func<Vector2, bool> IsPointerBlocked { get; set; }

        bool _dragging;
        Vector2 _last;
        bool _gesture;
        Vector2 _lastMid;
        float _lastDist;
        bool _secondaryActive;
        Vector2 _secondaryLast;

        void Awake()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = _background;
            // Стартуем над центром стартового сектора.
            _cam.transform.position = new Vector3(0f, 0f, -10f);
            _cam.transform.rotation = Quaternion.identity;

            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortraitUpsideDown = false;
            FitWidth();
        }

        /// <summary>Положение и масштаб камеры (для загрузки сохранения).</summary>
        public void SetView(Vector2 position, float worldWidth)
        {
            if (_cam == null) return;
            _worldWidth = Mathf.Clamp(worldWidth, _minWorldWidth, _maxWorldWidth);
            _cam.transform.position = new Vector3(position.x, position.y, -10f);
            FitWidth();
        }

        /// <summary>Центрирует стартовый сектор и оставляет по краям часть соседних секторов.</summary>
        public void SetStartView(Vector2 center, float hexSize, float neighborFraction)
        {
            float sectorWidth = 1.7320508f * hexSize;
            float width = sectorWidth * (1f + 2f * Mathf.Clamp01(neighborFraction));
            SetView(center, width);
        }

        void FitWidth()
        {
            if (_cam == null) return;
            float aspect = Mathf.Max(0.1f, (float)Screen.width / Mathf.Max(1, Screen.height));
            _cam.orthographicSize = _worldWidth * 0.5f / aspect;
        }

        Vector3 ScreenToWorld(Vector2 screen)
        {
            return _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
        }

        /// <summary>
        /// Точка мира под prevScreen после изменения масштаба оказывается под nowScreen.
        /// Так один и тот же код делает и сдвиг, и масштаб вокруг пальцев.
        /// </summary>
        void Apply(Vector2 prevScreen, Vector2 nowScreen, float zoomFactor)
        {
            Vector3 before = ScreenToWorld(prevScreen);
            _worldWidth = Mathf.Clamp(_worldWidth * zoomFactor, _minWorldWidth, _maxWorldWidth);
            FitWidth();
            Vector3 after = ScreenToWorld(nowScreen);
            _cam.transform.position += new Vector3(before.x - after.x, before.y - after.y, 0f);
        }

        void Update()
        {
            if (_cam == null) return;
            FitWidth();

            PointerInput.Poll();

            if (PointerInput.Gesture)
            {
                _dragging = false;
                Vector2 a = PointerInput.FingerA;
                Vector2 b = PointerInput.FingerB;
                Vector2 mid = (a + b) * 0.5f;
                float dist = Vector2.Distance(a, b);

                if (!_gesture || dist < 1f)
                {
                    _gesture = dist >= 1f;
                }
                else
                {
                    // Пальцы раздвинулись: dist вырос, ширина видимой карты уменьшается.
                    Apply(_lastMid, mid, _lastDist / dist);
                }

                _lastMid = mid;
                _lastDist = Mathf.Max(1f, dist);
                return;
            }

            _gesture = false;

            // Для проверки в редакторе мышью.
            if (PointerInput.Scroll != 0f)
                Apply(PointerInput.Position, PointerInput.Position, 1f - 0.12f * PointerInput.Scroll);

            if (PointerInput.SecondaryHeld)
            {
                if (_secondaryActive) Apply(_secondaryLast, PointerInput.Position, 1f);
                _secondaryActive = true;
                _secondaryLast = PointerInput.Position;
            }
            else
            {
                _secondaryActive = false;
            }

            if (!PanEnabled)
            {
                _dragging = false;
                return;
            }

            if (!_dragging)
            {
                if (PointerInput.Down && (IsPointerBlocked == null || !IsPointerBlocked(PointerInput.Position)))
                {
                    _dragging = true;
                    _last = PointerInput.Position;
                }

                return;
            }

            if (!PointerInput.Pressed || PointerInput.Up)
            {
                _dragging = false;
                return;
            }

            Vector2 now = PointerInput.Position;
            Apply(_last, now, 1f);
            _last = now;
        }
    }
}
