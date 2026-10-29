using CityGame.Buildings;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace CityGame.City
{
    /// <summary>
    /// Карточка здания — выезжает снизу при тапе на постройку в режиме «Рука».
    /// Создаёт Canvas программно, не требует префабов.
    /// </summary>
    public sealed class CityCardView : MonoBehaviour
    {
        CityModel _model;
        Economy _economy;
        ServiceCoverage _coverage;

        Canvas _canvas;
        RectTransform _panel;
        Text _title;
        Text _body;
        Button _closeBtn;
        Button _demolishBtn;

        BuildingCell _current;

        const float PanelHeight = 260f;
        const float AnimDuration = 0.28f;

        public void Bind(CityModel model, Economy economy, ServiceCoverage coverage)
        {
            _model = model;
            _economy = economy;
            _coverage = coverage;
            BuildUI();
        }

        void BuildUI()
        {
            var go = new GameObject("CityCard_Canvas");
            go.transform.SetParent(transform, false);

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            // Затемнение фона
            var overlay = NewImage(go.transform, "Overlay", new Color(0, 0, 0, 0));
            overlay.rectTransform.anchorMin = Vector2.zero;
            overlay.rectTransform.anchorMax = Vector2.one;
            overlay.rectTransform.offsetMin = Vector2.zero;
            overlay.rectTransform.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().raycastTarget = true;
            var overlayBtn = overlay.gameObject.AddComponent<Button>();
            overlayBtn.transition = Selectable.Transition.None;
            overlayBtn.onClick.AddListener(Hide);

            // Панель снизу
            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(go.transform, false);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0.13f, 0.16f, 0.20f, 0.97f);
            _panel = panelGo.GetComponent<RectTransform>();
            _panel.anchorMin = new Vector2(0f, 0f);
            _panel.anchorMax = new Vector2(1f, 0f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.sizeDelta = new Vector2(0f, PanelHeight);
            _panel.anchoredPosition = new Vector2(0f, -PanelHeight);

            // Заголовок
            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(_panel, false);
            _title = titleGo.AddComponent<Text>();
            StyleTitle(_title);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(-32f, 44f);
            titleRt.anchoredPosition = new Vector2(0f, -16f);

            // Тело карточки
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_panel, false);
            _body = bodyGo.AddComponent<Text>();
            StyleBody(_body);
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0f, 1f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.pivot = new Vector2(0.5f, 1f);
            bodyRt.sizeDelta = new Vector2(-32f, 120f);
            bodyRt.anchoredPosition = new Vector2(0f, -72f);

            // Кнопка «Снести»
            _demolishBtn = MakeButton(_panel, "Снести", new Color(0.75f, 0.22f, 0.22f));
            var demoRt = _demolishBtn.GetComponent<RectTransform>();
            demoRt.anchorMin = new Vector2(0f, 0f);
            demoRt.anchorMax = new Vector2(0.48f, 0f);
            demoRt.pivot = new Vector2(0f, 0f);
            demoRt.sizeDelta = new Vector2(-16f, 44f);
            demoRt.anchoredPosition = new Vector2(16f, 20f);
            _demolishBtn.onClick.AddListener(Demolish);

            // Кнопка «Закрыть»
            _closeBtn = MakeButton(_panel, "Закрыть", new Color(0.24f, 0.28f, 0.35f));
            var closeRt = _closeBtn.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(0.52f, 0f);
            closeRt.anchorMax = new Vector2(1f, 0f);
            closeRt.pivot = new Vector2(1f, 0f);
            closeRt.sizeDelta = new Vector2(-16f, 44f);
            closeRt.anchoredPosition = new Vector2(-16f, 20f);
            _closeBtn.onClick.AddListener(Hide);

            go.SetActive(false);
        }

        public void Show(BuildingCell cell)
        {
            _current = cell;
            RefreshContent();

            _canvas.gameObject.SetActive(true);
            _panel.DOKill();
            _panel.anchoredPosition = new Vector2(0f, -PanelHeight);
            _panel.DOAnchorPosY(0f, AnimDuration).SetEase(Ease.OutCubic);
        }

        public void Hide()
        {
            _panel.DOKill();
            _panel.DOAnchorPosY(-PanelHeight, AnimDuration).SetEase(Ease.InCubic)
                .OnComplete(() => _canvas.gameObject.SetActive(false));
            _current = null;
        }

        void RefreshContent()
        {
            if (_current == null) return;
            District d = _current.District;

            _title.text = d != null ? d.Title : BuildingCategory.Service.ToString();
            if (d == null) { _body.text = ""; return; }

            float income = _economy != null ? _economy.DistrictIncomePerSecond(d) : 0f;
            float bonus = _economy != null ? _economy.BonusOf(d) : 0f;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Зданий: {d.Count}   Множитель: ×{d.Multiplier}");
            sb.AppendLine($"Доход: {income:F1}/с");
            if (bonus > 0.001f) sb.AppendLine($"Бонус: +{bonus * 100f:F0}%");
            if (_coverage != null)
            {
                float sat = d.Satisfaction;
                if (sat < 0.999f) sb.AppendLine($"Обслуживание: {sat * 100f:F0}%");
            }

            _body.text = sb.ToString();
        }

        void Demolish()
        {
            if (_current == null || _model == null) return;
            Vector2Int pos = _current.Pos;
            Hide();
            _model.Remove(pos);
        }

        // ---------- helpers ----------

        static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            go.AddComponent<RectTransform>();
            return img;
        }

        static Button MakeButton(Transform parent, string label, Color bg)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = bg;

            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.highlightedColor = new Color(bg.r + 0.08f, bg.g + 0.08f, bg.b + 0.08f);
            cb.pressedColor = new Color(bg.r - 0.1f, bg.g - 0.1f, bg.b - 0.1f);
            btn.colors = cb;

            var txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontSize = 15;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            return btn;
        }

        static void StyleTitle(Text t)
        {
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 18;
            t.fontStyle = FontStyle.Bold;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
        }

        static void StyleBody(Text t)
        {
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 14;
            t.color = new Color(0.75f, 0.82f, 0.90f);
            t.alignment = TextAnchor.UpperLeft;
            t.lineSpacing = 1.3f;
        }
    }
}
