using System;
using CityGame.CellRoads;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Временный интерфейс на IMGUI под вертикальный телефон. Весь интерфейс внизу, под большим пальцем,
    /// потому что сверху у телефонов вырез и «остров».
    ///
    /// Внизу полоска: слева круглая кнопка «магазин», справа бюджет, доход и текущий инструмент.
    /// Нажатие на кнопку «магазин» открывает над полоской панель выбора, повторное нажатие закрывает.
    /// Нажатие на сам бюджет открывает панель с районами, переключателями и отладкой.
    /// Панели выбора службы и развилки встают над ними, пока нужны.
    /// Раскладка рассчитана на ширину 420 условных единиц, масштаб по ширине экрана, учтена safe area.
    /// Цвета временные.
    /// </summary>
    public sealed class CityDebugHud : MonoBehaviour
    {
        const float RefWidth = 420f;
        const float RefHeight = 800f;

        /// <summary>Отладочные строки (ввод, биом) в панели информации. Выключи, когда всё заработает.</summary>
        public bool ShowDiagnostics = true;

        public Color BarColor = new Color32(0xBF, 0xBF, 0xBF, 0xFF);
        public Color ShopColor = new Color32(0x8F, 0xE0, 0xCE, 0xFF);
        public Color PanelColor = new Color32(0xE4, 0xE4, 0xE4, 0xF2);

        enum Popup { None, Tools, Info }

        CitySystem _city;
        Popup _popup = Popup.None;
        string _toast = "";
        float _toastUntil;
        Rect _barRect;
        Rect _popupRect;
        Rect _dialogRect;
        Rect _junctionRect;
        Rect _serviceRect;

        Texture2D _pillTex, _circleTex, _panelTex;
        GUIStyle _pill, _circle, _panel;
        GUIStyle _lblTile;
        Texture2D[] _icons;
        GUIStyle _lbl, _lblSmall, _lblSmallRight, _lblBig, _lblCenter, _lblShop;

        public void Bind(CitySystem city)
        {
            _city = city;
            _city.Placer.Message += text =>
            {
                _toast = text;
                _toastUntil = Time.unscaledTime + 1.5f;
            };
            if (_city.JunctionTool != null)
                _city.JunctionTool.Message += text =>
                {
                    _toast = text;
                    _toastUntil = Time.unscaledTime + 1.8f;
                };
        }

        void OnDestroy()
        {
            if (_pillTex != null) Destroy(_pillTex);
            if (_circleTex != null) Destroy(_circleTex);
            if (_panelTex != null) Destroy(_panelTex);
            if (_icons != null)
                foreach (Texture2D t in _icons)
                    if (t != null) Destroy(t);
        }

        float Scale => Mathf.Max(0.5f, Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight));

        /// <summary>screen: пиксели экрана, начало внизу слева.</summary>
        public bool IsPointerBlocked(Vector2 screen)
        {
            var gui = new Vector2(screen.x, Screen.height - screen.y) / Scale;
            return _barRect.Contains(gui) || _popupRect.Contains(gui) ||
                   (_city != null && _city.Tapper != null && _city.Tapper.HasPending && _dialogRect.Contains(gui)) ||
                   (_city != null && _city.JunctionTool != null && _city.JunctionTool.HasSelection &&
                    _junctionRect.Contains(gui)) ||
                   (_city != null && _city.CurrentTool == Tool.Service && _serviceRect.Contains(gui));
        }

        static readonly string[] Names =
        {
            "нет", "Дорога", "Развилка", "Жильё", "Торговля", "Промышл.", "Служба", "Снести", "Расширить"
        };

        string LabelFor(Tool tool)
        {
            string name = Names[(int)tool];
            switch (tool)
            {
                case Tool.Road: return name + "\n" + _city.Economy.RoadCostPerCell + "/кл";
                case Tool.Junction: return name + "\n" + _city.Economy.RoundaboutCost + "/" + _city.Economy.OverpassCost;
                case Tool.Residential: return name + "\n" + _city.Economy.BuildCost[(int)BuildingCategory.Residential];
                case Tool.Commercial: return name + "\n" + _city.Economy.BuildCost[(int)BuildingCategory.Commercial];
                case Tool.Industrial: return name + "\n" + _city.Economy.BuildCost[(int)BuildingCategory.Industrial];
                case Tool.Service: return name + "\n" + _city.Economy.ServiceCost[(int)_city.CurrentService];
                default: return name;
            }
        }

        // --- стили ---

        /// <summary>Скруглённый прямоугольник из текстуры: радиус равен половине стороны даёт круг/пилюлю.</summary>
        static Texture2D RoundedTexture(int size, float radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            float c = size * 0.5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - c) - (c - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - c) - (c - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static GUIStyle Tinted(Texture2D tex, Color color, int border)
        {
            var s = new GUIStyle();
            s.normal.background = tex;
            s.normal.textColor = Color.black;
            s.border = new RectOffset(border, border, border, border);
            // Цвет рисуется через GUI.color при вызове.
            return s;
        }

        static GUIStyle Label(int size, TextAnchor align, FontStyle fontStyle = FontStyle.Normal)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = align,
                fontStyle = fontStyle,
                wordWrap = true
            };
            s.normal.textColor = Color.black;
            return s;
        }

        void EnsureStyles()
        {
            if (_pill != null) return;
            _pillTex = RoundedTexture(64, 32f);
            _circleTex = RoundedTexture(64, 32f);
            _panelTex = RoundedTexture(48, 16f);
            int toolCount = Enum.GetValues(typeof(Tool)).Length - 1;
            _icons = new Texture2D[toolCount];
            for (int i = 0; i < toolCount; i++) _icons[i] = ToolIcons.Make((Tool)(i + 1));
            _pill = Tinted(_pillTex, BarColor, 32);
            _circle = Tinted(_circleTex, ShopColor, 32);
            _panel = Tinted(_panelTex, PanelColor, 16);
            _lbl = Label(14, TextAnchor.UpperLeft);
            _lblTile = Label(10, TextAnchor.UpperCenter);
            _lblTile.wordWrap = false;
            _lblSmall = Label(11, TextAnchor.UpperLeft);
            _lblSmallRight = Label(11, TextAnchor.MiddleRight);
            _lblBig = Label(18, TextAnchor.MiddleRight, FontStyle.Bold);
            _lblCenter = Label(14, TextAnchor.MiddleCenter);
            _lblShop = Label(30, TextAnchor.MiddleCenter);
        }

        void Draw(Rect r, GUIStyle style, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.Box(r, GUIContent.none, style);
            GUI.color = old;
        }

        // ---------------------------------------------------------------

        void OnGUI()
        {
            if (_city == null || _city.Economy == null) return;
            EnsureStyles();

            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s;
            float h = Screen.height / s;

            Rect safe = Screen.safeArea;
            float bottomInset = safe.yMin / s;

            GUI.skin.button.fontSize = 13;

            const float margin = 8f;
            const float barH = 60f;
            float barY = h - bottomInset - barH - 8f;
            _barRect = new Rect(margin, barY, w - 2f * margin, barH);

            // Полоска, справа бюджет и инструмент.
            Draw(_barRect, _pill, BarColor);
            var textRect = new Rect(margin + barH + 4f, barY + 6f, w - 2f * margin - barH - 24f, barH - 12f);
            GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 28f),
                string.Format("{0:0} $   +{1:0.0}/с", _city.Economy.Money, _city.Economy.IncomePerSecond), _lblBig);
            string tool = Names[(int)_city.CurrentTool];
            if (_city.CurrentTool == Tool.Service) tool += ": " + ServiceSpecs.Title(_city.CurrentService);
            GUI.Label(new Rect(textRect.x, textRect.y + 26f, textRect.width, 20f),
                "Инструмент: " + tool + " · машин " + _city.Traffic.Count, _lblSmallRight);

            var infoHit = new Rect(margin + barH, barY, w - 2f * margin - barH, barH);
            if (GUI.Button(infoHit, GUIContent.none, GUIStyle.none))
                SetPopup(_popup == Popup.Info ? Popup.None : Popup.Info);

            // Круглая кнопка «магазин».
            var shop = new Rect(margin, barY, barH, barH);
            Draw(shop, _circle, ShopColor);
            GUI.Label(shop, _popup == Popup.Tools ? "×" : "$", _lblShop);
            if (GUI.Button(shop, GUIContent.none, GUIStyle.none))
                SetPopup(_popup == Popup.Tools ? Popup.None : Popup.Tools);

            // Панели встают стопкой вверх от полоски.
            float top = barY - 6f;
            _popupRect = Rect.zero;

            if (_popup == Popup.Tools) top = DrawTools(w, top);
            else if (_popup == Popup.Info) top = DrawInfo(w, top);

            top = DrawService(w, top);
            top = DrawJunction(w, top);

            // Подсказка над всеми панелями.
            if (Time.unscaledTime < _toastUntil)
            {
                var r = new Rect(w * 0.5f - 150f, top - 34f, 300f, 28f);
                Draw(r, _panel, PanelColor);
                GUI.Label(r, _toast, _lblCenter);
            }

            // Подтверждение открытия сектора.
            if (_city.Tapper.HasPending)
            {
                _dialogRect = new Rect(w * 0.5f - 130, h * 0.45f - 60, 260, 120);
                GUI.Box(_dialogRect, "Открыть эту область?");
                if (GUI.Button(new Rect(_dialogRect.x + 15, _dialogRect.y + 50, 110, 48), "Открыть"))
                    _city.Tapper.ConfirmPending();
                if (GUI.Button(new Rect(_dialogRect.x + 135, _dialogRect.y + 50, 110, 48), "Отмена"))
                    _city.Tapper.CancelPending();
            }
        }

        /// <summary>
        /// Строить и покупать можно только пока открыта панель выбора. Как только она закрывается,
        /// инструмент сбрасывается на «Рука», а незавершённый выбор развилки или области отменяется.
        /// </summary>
        void SetPopup(Popup next)
        {
            if (_popup == Popup.Tools && next != Popup.Tools)
            {
                _city.Select(Tool.Pan);
                if (_city.JunctionTool != null && _city.JunctionTool.HasSelection) _city.JunctionTool.Cancel();
                if (_city.Tapper.HasPending) _city.Tapper.CancelPending();
            }

            _popup = next;
        }

        /// <summary>Панель выбора, что строить: квадратные плитки с цветным значком и подписью, по четыре в ряд.</summary>
        float DrawTools(float w, float top)
        {
            // «Рука» не нужна: без выбранного инструмента карта и так двигается пальцем.
            int tools = Enum.GetValues(typeof(Tool)).Length - 1;
            int perRow = (tools + 1) / 2;
            const float tile = 68f;
            const float gap = 6f;
            float gridW = perRow * tile + (perRow - 1) * gap;
            float panelH = 2f * tile + gap + 16f;
            _popupRect = new Rect(8f, top - panelH, w - 16f, panelH);
            Draw(_popupRect, _panel, PanelColor);

            float x0 = _popupRect.x + (_popupRect.width - gridW) * 0.5f;
            for (int i = 0; i < tools; i++)
            {
                var t = (Tool)(i + 1);
                int row = i / perRow, col = i % perRow;
                bool on = _city.CurrentTool == t;
                var rect = new Rect(x0 + col * (tile + gap), _popupRect.y + 8f + row * (tile + gap), tile, tile);

                Draw(rect, _panel, on ? ShopColor : new Color32(0xF7, 0xF7, 0xF7, 0xFF));
                GUI.DrawTexture(new Rect(rect.x + (tile - 36f) * 0.5f, rect.y + 4f, 36f, 36f), _icons[i], ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(rect.x, rect.y + 40f, tile, tile - 40f), LabelFor(t), _lblTile);

                if (!GUI.Button(rect, GUIContent.none, GUIStyle.none)) continue;

                // Панель остаётся открытой, пока покупаешь. Повторный тап по активному инструменту снимает его,
                // и палец снова двигает карту.
                _city.Select(on ? Tool.Pan : t);
            }

            return _popupRect.y - 6f;
        }

        /// <summary>Панель информации: переключатели, районы, отладка.</summary>
        float DrawInfo(float w, float top)
        {
            var districts = _city.Model.Districts;
            int rows = Mathf.Min(districts.Count, 6);
            int extra = _city.Flow.UnconnectedDistricts > 0 ? 1 : 0;
            float diagH = ShowDiagnostics ? 34f : 0f;
            float panelH = 10f + 36f + (rows + extra) * 20f + (rows + extra > 0 ? 6f : 0f) + diagH + 8f;
            _popupRect = new Rect(8f, top - panelH, w - 16f, panelH);
            Draw(_popupRect, _panel, PanelColor);

            float x = _popupRect.x + 10f;
            float y = _popupRect.y + 8f;

            bool cars = _city.Traffic.Visible;
            bool newCars = GUI.Toggle(new Rect(x, y, 100f, 30f), cars, "Авто", GUI.skin.button);
            if (newCars != cars) _city.Traffic.Visible = newCars;

            bool heat = _city.Heatmap.Visible;
            bool newHeat = GUI.Toggle(new Rect(x + 108f, y, 120f, 30f), heat, "Нагрузка", GUI.skin.button);
            if (newHeat != heat) _city.Heatmap.Visible = newHeat;
            y += 38f;

            for (int i = 0; i < rows; i++)
            {
                District d = districts[i];
                string line;
                if (d.Category == BuildingCategory.Service)
                    line = string.Format("{0}: {1} кл.", d.Title, ServiceSpecs.Radius(d.Kind));
                else
                {
                    float bonus = _city.Economy.BonusOf(d);
                    line = string.Format("{0} {1}: x{2} · {3:0}%", d.Title, d.Count, d.Multiplier, d.Satisfaction * 100f);
                    if (bonus > 0.001f) line += string.Format(" · +{0:0}%", bonus * 100f);
                }

                GUI.Label(new Rect(x, y, _popupRect.width - 20f, 20f), line, _lbl);
                y += 20f;
            }

            if (extra > 0)
            {
                GUI.Label(new Rect(x, y, _popupRect.width - 20f, 20f), "Без дороги: " + _city.Flow.UnconnectedDistricts, _lbl);
                y += 20f;
            }

            if (ShowDiagnostics)
            {
                Vector2 p = CityGame.Roads.PointerInput.Position;
                Camera cam = _city.Rig.Cam;
                Vector3 wp = cam != null
                    ? cam.ScreenToWorldPoint(new Vector3(p.x, p.y, -cam.transform.position.z))
                    : Vector3.zero;
                var cellUnder = new Vector2Int(Mathf.FloorToInt(wp.x / _city.CellSize), Mathf.FloorToInt(wp.y / _city.CellSize));
                var sectorUnder = _city.Sectors.SectorAt(wp);
                var trees = _city.Biomes.TreesOf(sectorUnder);
                GUI.Label(new Rect(x, y + 4f, _popupRect.width - 20f, 16f),
                    string.Format("биом {0}, деревья {1}/{2}, лес-бонус {3} | авто {4} (в пути {5}, стоят {6}) накоплено {7:0.0}",
                        CityGame.Sectors.BiomeSpecs.Title(_city.Biomes.BiomeAt(cellUnder)), trees.y, trees.x,
                        _city.Biomes.ForestBonusActive(sectorUnder) ? "да" : "нет",
                        _city.Traffic.Count, _city.Traffic.OnRoad, _city.Traffic.Stopped, _city.Traffic.FleetValue),
                    _lblSmall);
                GUI.Label(new Rect(x, y + 20f, _popupRect.width - 20f, 16f),
                    string.Format("{0} | экран {1}x{2} | касание {3:0},{4:0} | мир {5:0.0},{6:0.0}",
                        CityGame.Roads.PointerInput.Source, Screen.width, Screen.height, p.x, p.y, wp.x, wp.y),
                    _lblSmall);
            }

            return _popupRect.y - 6f;
        }

        float DrawService(float w, float top)
        {
            if (_city.CurrentTool != Tool.Service)
            {
                _serviceRect = Rect.zero;
                return top;
            }

            const float panelH = 92f;
            _serviceRect = new Rect(8f, top - panelH, w - 16f, panelH);
            Draw(_serviceRect, _panel, PanelColor);

            ServiceKind cur = _city.CurrentService;
            GUI.Label(new Rect(16f, _serviceRect.y + 4f, w - 32f, 22f),
                string.Format("Радиус {0} кл. Профильный тип +{1:0}% к налогу, остальные +{2:0}%",
                    ServiceSpecs.Radius(cur), ServiceSpecs.Bonus(cur, ProfileOf(cur)) * 100f,
                    ServiceSpecs.Bonus(cur, OtherOf(cur)) * 100f), _lblSmall);

            float sw = (w - 32f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var kind = (ServiceKind)i;
                bool on = cur == kind;
                var r = new Rect(16f + i * sw, _serviceRect.y + 34f, sw - 4f, 52f);
                string text = ServiceSpecs.Title(kind) + "\n" + _city.Economy.ServiceCost[i];
                bool pressed = GUI.Toggle(r, on, text, GUI.skin.button);
                if (pressed && !on) _city.SelectService(kind);
            }

            return _serviceRect.y - 6f;
        }

        float DrawJunction(float w, float top)
        {
            JunctionTapper jt = _city.JunctionTool;
            if (jt == null || !jt.HasSelection)
            {
                _junctionRect = Rect.zero;
                return top;
            }

            const float panelH = 138f;
            _junctionRect = new Rect(8f, top - panelH, w - 16f, panelH);
            Draw(_junctionRect, _panel, PanelColor);
            float panelY = _junctionRect.y;

            GUI.Label(new Rect(16f, panelY + 6f, w - 32f, 22f), "Развилка сейчас: " + JunctionSpecs.Title(jt.Current), _lbl);

            var types = jt.Available();
            float ow = (w - 32f) / Mathf.Max(1, types.Count);
            for (int i = 0; i < types.Count; i++)
            {
                JunctionType t = types[i];
                float cost = jt.CostFor(t);
                string price = t == jt.Current ? "сейчас" : cost > 0f ? cost.ToString("0") : "бесплатно";
                bool on = jt.Pending == t;
                var r = new Rect(16f + i * ow, panelY + 32f, ow - 4f, 54f);
                bool pressed = GUI.Toggle(r, on, JunctionSpecs.Short(t) + "\n" + price, GUI.skin.button);
                if (pressed && !on) jt.Choose(t);
            }

            bool changed = jt.Pending != jt.Current;
            float pay = jt.CostFor(jt.Pending);
            GUI.enabled = changed && _city.Economy.Money >= pay;
            string applyText = !changed ? "Выбери вариант" : pay > 0f ? "Применить за " + pay.ToString("0") : "Применить";
            if (GUI.Button(new Rect(16f, panelY + 92f, (w - 32f) * 0.62f - 4f, 40f), applyText)) jt.Apply();
            GUI.enabled = true;
            if (GUI.Button(new Rect(16f + (w - 32f) * 0.62f, panelY + 92f, (w - 32f) * 0.38f, 40f), "Отмена")) jt.Cancel();

            return _junctionRect.y - 6f;
        }

        static BuildingCategory ProfileOf(ServiceKind k)
        {
            switch (k)
            {
                case ServiceKind.Ambulance: return BuildingCategory.Residential;
                case ServiceKind.Police: return BuildingCategory.Commercial;
                default: return BuildingCategory.Industrial;
            }
        }

        static BuildingCategory OtherOf(ServiceKind k)
        {
            return k == ServiceKind.Ambulance ? BuildingCategory.Commercial : BuildingCategory.Residential;
        }
    }
}
