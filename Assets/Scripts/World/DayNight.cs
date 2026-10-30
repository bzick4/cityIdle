using UnityEngine;
using CityGame.Buildings;

namespace CityGame.World
{
    /// <summary>
    /// Смена дня и ночи. Часы идут сами: сутки занимают _dayLengthSeconds реальных секунд.
    /// Ночью поверх мира ложится тёмно-синее полотно, а огни (фонари, окна, фары, корабли)
    /// рисуются выше него материалом LightMaterial, прозрачность которого растёт с темнотой.
    /// </summary>
    public sealed class DayNight : MonoBehaviour
    {
        public const int OverlayOrder = 60;
        public const int LightOrder = 61;

        [Tooltip("Сколько реальных секунд длятся игровые сутки")]
        [SerializeField] float _dayLengthSeconds = 240f;
        [Tooltip("Час начала новой игры, 0..24")]
        [Range(0f, 24f)] public float StartHour = 9f;
        [Tooltip("Насколько темно в самую тёмную ночь, 0..1")]
        [Range(0.2f, 0.85f)] public float NightStrength = 0.5f;
        [Tooltip("Если включить, время стоит на месте")]
        [SerializeField] bool _frozen;

        public float Hour { get; private set; }

        /// <summary>Темнота 0..1: 0 день, 1 глубокая ночь.</summary>
        public float Darkness { get; private set; }

        /// <summary>Доля яркости огней 0..1. Зажигаются в сумерках раньше, чем наступит полная ночь.</summary>
        public float LightLevel { get; private set; }

        /// <summary>Материал для всех огней: цвет в вершинах, прозрачность от LightLevel.</summary>
        public Material LightMaterial { get; private set; }

        public bool IsNight => Darkness > 0.5f;

        public string Clock
        {
            get
            {
                int h = Mathf.FloorToInt(Hour) % 24;
                int m = Mathf.FloorToInt((Hour - Mathf.Floor(Hour)) * 60f) % 60;
                return h.ToString("00") + ":" + m.ToString("00");
            }
        }

        public string PhaseName
        {
            get
            {
                if (Hour >= 5f && Hour < 8f) return "Рассвет";
                if (Hour >= 8f && Hour < 18f) return "День";
                if (Hour >= 18f && Hour < 21f) return "Вечер";
                return "Ночь";
            }
        }

        Material _overlayMaterial;
        GameObject _overlay;

        void Awake()
        {
            Hour = StartHour;
            LightMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 1f, 1f, 0f) };
            _overlayMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0f, 0f, 0f, 0f) };

            var batch = new MeshBatch();
            const float half = 5000f;
            batch.Rect(new Vector2(-half, -half), new Vector2(half, half), Color.white);
            _overlay = new GameObject("Night overlay");
            _overlay.transform.SetParent(transform, false);
            _overlay.AddComponent<MeshFilter>().sharedMesh = batch.ToMesh();
            var r = _overlay.AddComponent<MeshRenderer>();
            r.sharedMaterial = _overlayMaterial;
            r.sortingOrder = OverlayOrder;
            Apply();
        }

        void OnDestroy()
        {
            if (_overlay != null)
            {
                var f = _overlay.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
                Destroy(_overlay);
            }

            if (LightMaterial != null) Destroy(LightMaterial);
            if (_overlayMaterial != null) Destroy(_overlayMaterial);
        }

        public void SetHour(float hour)
        {
            Hour = Mathf.Repeat(hour, 24f);
            Apply();
        }

        void Update()
        {
            if (!_frozen && _dayLengthSeconds > 1f)
                Hour = Mathf.Repeat(Hour + Time.deltaTime * 24f / _dayLengthSeconds, 24f);
            Apply();
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Ночь с 21 до 5, рассвет 5..8, вечер 18..21.</summary>
        public static float DarknessAt(float h)
        {
            if (h < 5f) return 1f;
            if (h < 8f) return 1f - Smooth((h - 5f) / 3f);
            if (h < 18f) return 0f;
            if (h < 21f) return Smooth((h - 18f) / 3f);
            return 1f;
        }

        void Apply()
        {
            float d = DarknessAt(Hour);
            Darkness = d;
            LightLevel = Smooth((d - 0.12f) / 0.55f);

            // Ночью холодный глубокий синий, в сумерках лишь лёгкий сиренево-розовый налёт.
            // Оранжевого тона нет вовсе: он на светлой карте давал вид ржавчины.
            Color night = new Color(0.05f, 0.09f, 0.30f);
            Color dusk = new Color(0.30f, 0.20f, 0.45f);
            float twilight = 4f * d * (1f - d);
            Color tint = Color.Lerp(night, dusk, twilight * 0.5f);
            tint.a = NightStrength * d;
            if (_overlayMaterial != null) _overlayMaterial.color = tint;
            if (LightMaterial != null) LightMaterial.color = new Color(1f, 1f, 1f, LightLevel);
        }
    }
}
