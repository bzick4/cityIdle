using System;
using CityGame.CellRoads;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Временный интерфейс на IMGUI под вертикальный телефон. Весь интерфейс внизу, под большим пальцем,
    /// потому что сверху у телефонов вырез и «остров».
    ///
    /// Внизу полоска: слева круглая кнопка меню, справа бюджет, доход и текущий инструмент.
    /// Компактное меню показывает категории районов, дорог и служб с отдельными страницами.
    /// Нажатие на бюджет открывает панель города и отладки; выбор развилки и шоссе показывает контекстные панели.
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
        enum ToolPage { Home, Districts, Roads, Services }

        CitySystem _city;
        Popup _popup = Popup.None;
        ToolPage _toolPage = ToolPage.Home;
        string _toast = "";
        float _toastUntil;
        Rect _barRect;
        Rect _popupRect;
        Rect _dialogRect;
        Rect _junctionRect;
        Rect _highwayRect;
        Rect _eraseRect;

        Texture2D _pillTex, _circleTex, _panelTex;
        GUIStyle _pill, _circle, _panel;
        GUIStyle _lblTile;
        Texture2D[] _icons;
        Texture2D[] _serviceIcons;
        Texture2D[] _junctionIcons;
        GUIStyle _lblCard, _lblMenuTitle, _lblMenuCategory;
        bool _exitRequested;
        GUIStyle _lblDiag;
        GUIStyle _lbl, _lblSmall, _lblSmallRight, _lblBig, _lblCenter, _lblShop, _lblAction;

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
            if (_city.CanalTool != null)
                _city.CanalTool.Message += text =>
                {
                    _toast = text;
                    _toastUntil = Time.unscaledTime + 1.8f;
                };
            if (_city.PortTool != null)
                _city.PortTool.Message += text =>
                {
                    _toast = text;
                    _toastUntil = Time.unscaledTime + 1.8f;
                };
            if (_city.BusTool != null)
                _city.BusTool.Message += text =>
                {
                    _toast = text;
                    _toastUntil = Time.unscaledTime + 1.8f;
                };
            if (_city.HighwayTool != null)
                _city.HighwayTool.Message += text =>
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
            DestroyIcons(_serviceIcons);
            DestroyIcons(_junctionIcons);
        }

        static void DestroyIcons(Texture2D[] icons)
        {
            if (icons == null) return;
            foreach (Texture2D t in icons) if (t != null) Destroy(t);
        }

        void Update()
        {
            // Выход в меню разрушает этот объект, поэтому делаем его вне OnGUI.
            if (!_exitRequested) return;
            _exitRequested = false;
            _city.ExitToMenu();
        }

        float Scale => Mathf.Max(0.5f, Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight));

        /// <summary>screen: пиксели экрана, начало внизу слева.</summary>
        public bool IsPointerBlocked(Vector2 screen)
        {
            if (_city != null && _city.Placer != null && _city.Placer.PendingEraseCell.HasValue) return true;
            var gui = new Vector2(screen.x, Screen.height - screen.y) / Scale;
            return _barRect.Contains(gui) || _popupRect.Contains(gui) ||
                   (_city != null && _city.Tutorial != null && _city.Tutorial.Contains(screen)) ||
                   (_city != null && _city.Tapper != null && _city.Tapper.HasPending && _dialogRect.Contains(gui)) ||
                   (_popup != Popup.Tools && _city != null && _city.JunctionTool != null && _city.JunctionTool.HasSelection &&
                    _junctionRect.Contains(gui)) ||
                   (_popup != Popup.Tools && _city != null && (_city.CurrentTool == Tool.Highway || _city.CurrentTool == Tool.OneWay) &&
                    _highwayRect.Contains(gui));
        }

        static readonly string[] Names =
        {
            "нет", "Дорога", "Шоссе", "Одностор.", "Развилка", "Жильё", "Торговля", "Промышл.", "Служба", "Снести", "Расширить", "Канал", "Порт", "Автобус"
        };

        string LabelFor(Tool tool)
        {
            string name = Names[(int)tool];
            switch (tool)
            {
                case Tool.Road: return name + "\n" + _city.Economy.RoadCostPerCell + "/кл";
                case Tool.Highway: return name + "\n" + _city.Economy.HighwayCostPerCell + "/кл";
                case Tool.OneWay: return name + "\n" + _city.Economy.RoadCostPerCell + "/кл";
                case Tool.Junction: return name + "\n" + _city.Economy.RoundaboutCost + "/" +
                                           _city.Economy.HighwayRoundaboutCost + "/" + _city.Economy.OverpassCost;
                case Tool.Canal: return name + "\n" + _city.Economy.CanalCostPerCell + "/кл";
                case Tool.Port: return name + "\n" + _city.Economy.PortCost;
                case Tool.Bus: return name + "\n" + _city.Economy.BusStationCost;
                case Tool.Residential: return name + "\n" + _city.Economy.BuildCost[(int)BuildingCategory.Residential];
                case Tool.Commercial: return name + "\n" + _city.Economy.BuildCost[(int)BuildingCategory.Commercial];
                case Tool.Industrial: return name + "\n" + _city.Economy.BuildCost[(int)BuildingCategory.Industrial];
                case Tool.Service: return name + "\n" + _city.Economy.ServiceCost[(int)_city.CurrentService];
                default: return name;
            }
        }

        // --- стили ---

        /// <summary>Скруглённый прямоугольник из текстуры: радиус равен половине стороны даёт круг/пилюлю.</summary>
        internal static Texture2D RoundedTexture(int size, float radius)
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

        /// <summary>
        /// Один цвет текста во всех состояниях стиля. Иначе под пальцем или курсором (hover/active/focused)
        /// IMGUI подставляет цвет из скина, и текст сливается с фоном.
        /// </summary>
        internal static void SetText(GUIStyle s, Color c)
        {
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = c;
            s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = s.onFocused.textColor = c;
        }

        internal static GUIStyle Tinted(Texture2D tex, Color color, int border)
        {
            var s = new GUIStyle();
            s.normal.background = tex;
            SetText(s, Color.black);
            s.border = new RectOffset(border, border, border, border);
            // Цвет рисуется через GUI.color при вызове.
            return s;
        }

        // internal static GUIStyle Label(int size, TextAnchor align, FontStyle fontStyle = FontStyle.Normal)
        // {
        //     var s = new GUIStyle(GUI.skin.label)
        //     {
        //         fontSize = size,
        //         alignment = align,
        //         fontStyle = fontStyle,
        //         wordWrap = true
        //     };
        //     SetText(s, Color.black);
        //     return s;
        // }

        internal static GUIStyle Label(int size, TextAnchor align, FontStyle fontStyle = FontStyle.Normal)
{
    Font customFont = Resources.Load<Font>("Fonts/NotoSans-VariableFont_wdth,wght");
    Debug.Log($"Custom font loaded: {customFont != null}");

    var s = new GUIStyle(GUI.skin.label)
    {
         font = customFont,
        fontSize = size,
        alignment = align,
        fontStyle = fontStyle,
        wordWrap = true
    };

    if (customFont != null)
        s.font = customFont;

    SetText(s, Color.black);
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
            _serviceIcons = new Texture2D[Enum.GetValues(typeof(ServiceKind)).Length];
            for (int i = 0; i < _serviceIcons.Length; i++) _serviceIcons[i] = ToolIcons.MakeService((ServiceKind)i);
            _junctionIcons = new Texture2D[Enum.GetValues(typeof(JunctionType)).Length];
            for (int i = 0; i < _junctionIcons.Length; i++) _junctionIcons[i] = ToolIcons.MakeJunction((JunctionType)i);
            _pill = Tinted(_pillTex, BarColor, 32);
            _circle = Tinted(_circleTex, ShopColor, 32);
            _panel = Tinted(_panelTex, PanelColor, 16);
            _lbl = Label(14, TextAnchor.UpperLeft);
            _lblTile = Label(10, TextAnchor.UpperCenter);
            _lblTile.wordWrap = false;
            _lblCard = Label(9, TextAnchor.MiddleLeft);
            _lblCard.wordWrap = true;
            _lblMenuTitle = Label(15, TextAnchor.MiddleLeft, FontStyle.Bold);
            _lblMenuCategory = Label(11, TextAnchor.MiddleCenter, FontStyle.Bold);
            _lblSmall = Label(11, TextAnchor.UpperLeft);
            _lblDiag = Label(10, TextAnchor.UpperLeft);
            SetText(_lblDiag, new Color32(0x6B, 0x74, 0x80, 0xFF));
            _lblSmallRight = Label(11, TextAnchor.MiddleRight);
            _lblBig = Label(18, TextAnchor.MiddleRight, FontStyle.Bold);
            _lblCenter = Label(14, TextAnchor.MiddleCenter);
            _lblShop = Label(30, TextAnchor.MiddleCenter);
            _lblAction = Label(14, TextAnchor.MiddleCenter, FontStyle.Bold);
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
                "Инструмент: " + tool + " · машин " + _city.Traffic.Count +
                (_city.Day != null ? " · " + _city.Day.PhaseName + " " + _city.Day.Clock : ""), _lblSmallRight);

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

            if (_popup != Popup.Tools)
            {
                top = DrawJunction(w, top);
                top = DrawHighway(w, top);
            }

            if (_city.Tapper.HasPending)
            {
                DrawSectorDialog(w, top);
                top = _dialogRect.y - 6f;
            }
            else _dialogRect = Rect.zero;

            if (_city.Placer.PendingEraseCell.HasValue)
                DrawEraseDialog(w, h);
            else _eraseRect = Rect.zero;

            // Подсказка над всеми панелями.
            if (Time.unscaledTime < _toastUntil)
            {
                var r = new Rect(w * 0.5f - 150f, top - 34f, 300f, 28f);
                Draw(r, _panel, PanelColor);
                GUI.Label(r, _toast, _lblCenter);
            }

        }

        void DrawEraseDialog(float w, float h)
        {
            Vector2Int cell = _city.Placer.PendingEraseCell.Value;
            BuildingCell building = _city.Model.Get(cell);
            if (building == null) { _city.Placer.CancelEraseBuilding(); _eraseRect = Rect.zero; return; }

            GUI.color = new Color(0.10f, 0.13f, 0.16f, 0.56f);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float panelW = Mathf.Min(340f, w - 28f);
            const float panelH = 182f;
            _eraseRect = new Rect((w - panelW) * 0.5f, (h - panelH) * 0.5f, panelW, panelH);
            Draw(_eraseRect, _panel, PanelColor);
            GUI.Label(new Rect(_eraseRect.x + 18f, _eraseRect.y + 14f, panelW - 36f, 28f), "СНЕСТИ ЗДАНИЕ?", _lblMenuTitle);

            string name = building.Category == BuildingCategory.Service
                ? ServiceSpecs.Title(building.Kind)
                : CategorySpecs.Title(building.Category);
            GUI.Label(new Rect(_eraseRect.x + 18f, _eraseRect.y + 48f, panelW - 36f, 42f),
                name + " · клетка " + cell.x + ", " + cell.y, _lblSmall);
            GUI.Label(new Rect(_eraseRect.x + 18f, _eraseRect.y + 91f, panelW - 36f, 28f),
                "Это действие нельзя отменить", _lblSmall);

            const float gap = 8f;
            float buttonY = _eraseRect.y + 132f;
            float buttonW = (panelW - 36f - gap) * 0.5f;
            var cancel = new Rect(_eraseRect.x + 18f, buttonY, buttonW, 38f);
            var remove = new Rect(cancel.xMax + gap, buttonY, buttonW, 38f);
            if (DrawActionButton(cancel, "ОСТАВИТЬ", new Color32(0xF7, 0xF7, 0xF4, 0xFF),
                    new Color32(0x3B, 0x45, 0x52, 0xFF), true)) _city.Placer.CancelEraseBuilding();
            if (DrawActionButton(remove, "УДАЛИТЬ", new Color32(0xE8, 0xA4, 0x9B, 0xFF),
                    new Color32(0x3B, 0x45, 0x52, 0xFF), true)) _city.Placer.ConfirmEraseBuilding();
        }

        void DrawSectorDialog(float w, float top)
        {
            CityGame.Roads.HexCoord hex = _city.Tapper.PendingHex.Value;
            CityGame.Sectors.Biome biome = _city.Biomes.Map.BiomeOf(hex);
            Color ground = CityGame.Sectors.BiomeSpecs.Ground(biome);

            const float panelH = 128f;
            _dialogRect = new Rect(8f, top - panelH, w - 16f, panelH);
            Draw(_dialogRect, _panel, PanelColor);

            // Шестиугольник цвета биома, как выбранный сектор на карте.
            float hx = _dialogRect.x + 14f, hy = _dialogRect.y + 14f, hs = 52f;
            Draw(new Rect(hx, hy, hs, hs), _panel, ground);
            GUI.DrawTexture(new Rect(hx + 7f, hy + 7f, hs - 14f, hs - 14f), _icons[(int)Tool.Expand - 1],
                ScaleMode.ScaleToFit, true);

            float tx = hx + hs + 12f;
            GUI.Label(new Rect(tx, _dialogRect.y + 10f, _dialogRect.width - (tx - _dialogRect.x) - 10f, 26f),
                "НОВАЯ ОБЛАСТЬ · " + CityGame.Sectors.BiomeSpecs.Title(biome).ToUpper(), _lblMenuTitle);
            GUI.Label(new Rect(tx, _dialogRect.y + 36f, _dialogRect.width - (tx - _dialogRect.x) - 10f, 36f),
                CityGame.Sectors.BiomeSpecs.Hint(biome), _lblSmall);

            float gap = 8f;
            float bw = (_dialogRect.width - 28f - gap) * 0.6f;
            var open = new Rect(_dialogRect.x + 14f, _dialogRect.y + 78f, bw, 40f);
            var cancel = new Rect(open.xMax + gap, open.y, _dialogRect.width - 28f - gap - bw, 40f);
            if (DrawActionButton(open, "ОТКРЫТЬ", ShopColor, new Color32(0x2C, 0x3A, 0x40, 0xFF), true))
                _city.Tapper.ConfirmPending();
            if (DrawActionButton(cancel, "ОТМЕНА", new Color32(0xF7, 0xF7, 0xF7, 0xFF),
                    new Color32(0x3B, 0x46, 0x52, 0xFF), true))
                _city.Tapper.CancelPending();
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
            if (next == Popup.Tools) _toolPage = ToolPage.Home;
        }

        /// <summary>Модальное меню с категориями и отдельными страницами инструментов.</summary>
        float DrawTools(float w, float top)
        {
            _junctionRect = _highwayRect = Rect.zero;
            bool home = _toolPage == ToolPage.Home;
            float panelHeight = home ? 142f :
                _toolPage == ToolPage.Services ? 148f : 218f;
            Rect panel = new Rect(8f, Mathf.Max(8f, top - panelHeight), w - 16f, panelHeight);
            _popupRect = panel;
            Draw(panel, _panel, PanelColor);

            Rect back = new Rect(panel.x + 8f, panel.y + 8f, 82f, 30f);
            if (DrawActionButton(back, home ? "ЗАКРЫТЬ" : "← НАЗАД",
                    new Color32(0xF7, 0xF7, 0xF4, 0xFF), new Color32(0x3B, 0x45, 0x52, 0xFF), true))
            {
                if (home) SetPopup(Popup.None);
                else _toolPage = ToolPage.Home;
            }

            string title = home ? "СТРОИТЕЛЬСТВО" :
                _toolPage == ToolPage.Districts ? "РАЙОНЫ" :
                _toolPage == ToolPage.Roads ? "ДОРОГИ" : "СЛУЖБЫ";
            GUI.Label(new Rect(panel.x + 98f, panel.y + 8f, panel.width - 108f, 30f), title, _lblMenuTitle);

            if (home)
            {
                float gap = 6f;
                float cardW = (panel.width - 24f - gap * 3f) / 4f;
                float cardH = 84f;
                float x = panel.x + 8f;
                float y = panel.y + 44f;
                DrawCategoryCard(new Rect(x, y, cardW, cardH), "РАЙОНЫ", _icons[(int)Tool.Residential - 1],
                    ToolPage.Districts, new Color32(0xE7, 0xF0, 0xE4, 0xFF));
                DrawCategoryCard(new Rect(x + cardW + gap, y, cardW, cardH), "ДОРОГИ", _icons[(int)Tool.Road - 1],
                    ToolPage.Roads, new Color32(0xE6, 0xE9, 0xEC, 0xFF));
                DrawCategoryCard(new Rect(x + (cardW + gap) * 2f, y, cardW, cardH), "СЛУЖБЫ", _icons[(int)Tool.Service - 1],
                    ToolPage.Services, new Color32(0xF8, 0xE9, 0xE8, 0xFF));
                // «Снести» не категория, а режим: нажатие сразу включает инструмент.
                DrawEraseCard(new Rect(x + (cardW + gap) * 3f, y, cardW, cardH));
                return panel.y - 6f;
            }

            if (_toolPage == ToolPage.Services)
            {
                DrawServicePage(panel);
                return panel.y - 6f;
            }

            Tool[] tools = _toolPage == ToolPage.Roads
                ? new[] { Tool.Road, Tool.Highway, Tool.OneWay, Tool.Junction, Tool.Canal }
                : new[] { Tool.Residential, Tool.Commercial, Tool.Industrial, Tool.Expand, Tool.Port, Tool.Bus };
            DrawToolPage(panel, tools);
            return panel.y - 6f;
        }

        void DrawCategoryCard(Rect r, string title, Texture2D icon, ToolPage page, Color fill)
        {
            Draw(r, _panel, fill);
            float size = Mathf.Min(38f, r.width * 0.42f);
            GUI.DrawTexture(new Rect(r.x + (r.width - size) * 0.5f, r.y + 7f, size, size), icon,
                ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(r.x + 3f, r.y + r.height - 31f, r.width - 6f, 24f), title,
                _lblMenuCategory);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                _toolPage = page;
        }

        void DrawEraseCard(Rect r)
        {
            bool selected = _city.CurrentTool == Tool.Erase;
            Draw(r, _panel, selected ? new Color32(0xF4, 0xB8, 0xB0, 0xFF) : new Color32(0xF8, 0xE1, 0xDE, 0xFF));
            float size = Mathf.Min(38f, r.width * 0.42f);
            GUI.DrawTexture(new Rect(r.x + (r.width - size) * 0.5f, r.y + 7f, size, size),
                _icons[(int)Tool.Erase - 1], ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(r.x + 3f, r.y + r.height - 31f, r.width - 6f, 24f), "СНЕСТИ", _lblMenuCategory);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) SelectFromMenu(Tool.Erase);
        }

        void DrawToolPage(Rect panel, Tool[] tools)
        {
            const float gap = 6f;
            const int columns = 3;
            float cardW = (panel.width - 32f - gap * (columns - 1)) / columns;
            const float cardH = 72f;
            float x0 = panel.x + 8f;
            int rows = (tools.Length + columns - 1) / columns;
            float contentH = rows * cardH + (rows - 1) * gap;
            float y0 = panel.y + 42f;
            for (int i = 0; i < tools.Length; i++)
            {
                Tool tool = tools[i];
                int iconIndex = (int)tool - 1;
                Rect r = new Rect(x0 + (i % columns) * (cardW + gap),
                    y0 + (i / columns) * (cardH + gap), cardW, cardH);
                bool selected = _city.CurrentTool == tool;
                Draw(r, _panel, selected ? ShopColor : new Color32(0xF7, 0xF7, 0xF4, 0xFF));
                float iconSize = 32f;
                GUI.DrawTexture(new Rect(r.x + (r.width - iconSize) * 0.5f, r.y + 4f, iconSize, iconSize),
                    _icons[iconIndex], ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(r.x + 2f, r.y + 39f, r.width - 4f, 28f), LabelFor(tool), _lblTile);
                if (!GUI.Button(r, GUIContent.none, GUIStyle.none)) continue;
                SelectFromMenu(tool);
            }
        }

        void DrawServicePage(Rect panel)
        {
            const float gap = 6f;
            float cardW = (panel.width - 24f - gap * 2f) / 3f;
            const float cardH = 88f;
            float x0 = panel.x + 8f;
            float y = panel.y + 44f;
            for (int i = 0; i < _serviceIcons.Length; i++)
            {
                ServiceKind kind = (ServiceKind)i;
                Rect r = new Rect(x0 + i * (cardW + gap), y, cardW, cardH);
                Draw(r, _panel, _city.CurrentTool == Tool.Service && _city.CurrentService == kind
                    ? ShopColor : new Color32(0xF7, 0xF7, 0xF4, 0xFF));
                float iconSize = 36f;
                GUI.DrawTexture(new Rect(r.x + (r.width - iconSize) * 0.5f, r.y + 4f, iconSize, iconSize),
                    _serviceIcons[i], ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(r.x + 3f, r.y + 42f, r.width - 6f, 20f), ServiceSpecs.Title(kind), _lblTile);
                GUI.Label(new Rect(r.x + 3f, r.y + 63f, r.width - 6f, 18f),
                    _city.Economy.ServiceCost[i].ToString(), _lblTile);
                if (!GUI.Button(r, GUIContent.none, GUIStyle.none)) continue;
                _city.SelectService(kind);
                _city.Select(Tool.Service);
                _popup = Popup.None;
                _toolPage = ToolPage.Home;
            }
        }

        void SelectFromMenu(Tool tool)
        {
            _city.Select(tool == _city.CurrentTool ? Tool.Pan : tool);
            _popup = Popup.None;
            _toolPage = ToolPage.Home;
        }

        /// <summary>Панель информации: переключатели, районы, отладка.</summary>
        float DrawInfo(float w, float top)
        {
            var districts = _city.Model.Districts;
            int rows = Mathf.Min(districts.Count, 6);
            int extra = _city.Flow.UnconnectedDistricts > 0 ? 1 : 0;
            float diagH = ShowDiagnostics ? 34f : 0f;
            float panelH = 10f + 38f + (rows + extra) * 20f + (rows + extra > 0 ? 6f : 0f) + diagH + 8f;
            _popupRect = new Rect(8f, top - panelH, w - 16f, panelH);
            Draw(_popupRect, _panel, PanelColor);

            float x = _popupRect.x + 10f;
            float y = _popupRect.y + 8f;

            Color light = new Color32(0xF7, 0xF7, 0xF4, 0xFF);
            Color ink = new Color32(0x3B, 0x45, 0x52, 0xFF);
            Color inkOn = new Color32(0x2C, 0x3A, 0x40, 0xFF);

            // Переключатели в виде таких же плашек, как кнопки меню: включённый залит цветом магазина.
            bool cars = _city.Traffic.Visible;
            var carsRect = new Rect(x, y, 96f, 32f);
            if (DrawActionButton(carsRect, cars ? "АВТО ✓" : "АВТО", cars ? ShopColor : light, cars ? inkOn : ink, true))
                _city.Traffic.Visible = !cars;

            bool heat = _city.Heatmap.Visible;
            var heatRect = new Rect(carsRect.xMax + 6f, y, 116f, 32f);
            if (DrawActionButton(heatRect, heat ? "НАГРУЗКА ✓" : "НАГРУЗКА", heat ? ShopColor : light, heat ? inkOn : ink, true))
                _city.Heatmap.Visible = !heat;

            var menuRect = new Rect(_popupRect.xMax - 10f - 92f, y, 92f, 32f);
            if (DrawActionButton(menuRect, "В МЕНЮ", new Color32(0xF8, 0xE1, 0xDE, 0xFF), ink, true))
                _exitRequested = true;
            y += 40f;

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
                    _lblDiag);
                GUI.Label(new Rect(x, y + 20f, _popupRect.width - 20f, 16f),
                    string.Format("{0} | экран {1}x{2} | касание {3:0},{4:0} | мир {5:0.0},{6:0.0}",
                        CityGame.Roads.PointerInput.Source, Screen.width, Screen.height, p.x, p.y, wp.x, wp.y),
                    _lblDiag);
            }

            return _popupRect.y - 6f;
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
                if (DrawOptionCard(r, on, _junctionIcons[(int)t], JunctionCardTitle(t), price) && !on)
                    jt.Choose(t);
            }

            bool changed = jt.Pending != jt.Current;
            float pay = jt.CostFor(jt.Pending);
            string applyText = !changed ? "Выбери вариант" : pay > 0f ? "Применить за " + pay.ToString("0") : "Применить";
            bool canApply = changed && _city.Economy.Money >= pay;
            float rowWidth = w - 32f;
            float gap = 8f;
            float applyWidth = (rowWidth - gap) * 0.62f;
            Rect applyRect = new Rect(16f, panelY + 92f, applyWidth, 38f);
            Rect cancelRect = new Rect(applyRect.xMax + gap, panelY + 92f, rowWidth - applyWidth - gap, 38f);
            if (DrawActionButton(applyRect, applyText, canApply ? ShopColor : new Color32(0xD2, 0xD8, 0xD5, 0xFF),
                    canApply ? new Color32(0x2C, 0x3A, 0x40, 0xFF) : new Color32(0x7D, 0x85, 0x84, 0xFF), canApply))
                jt.Apply();
            if (DrawActionButton(cancelRect, "Отмена", new Color32(0xF7, 0xF7, 0xF7, 0xFF),
                    new Color32(0x3B, 0x46, 0x52, 0xFF), true)) jt.Cancel();

            return _junctionRect.y - 6f;
        }

        bool DrawOptionCard(Rect r, bool selected, Texture2D icon, string title, string detail)
        {
            Color fill = selected ? ShopColor : new Color32(0xF7, 0xF7, 0xF7, 0xFF);
            Draw(r, _panel, fill);
            if (r.width < 90f)
            {
                float compactIcon = Mathf.Min(22f, r.height * 0.42f);
                GUI.DrawTexture(new Rect(r.x + (r.width - compactIcon) * 0.5f, r.y + 2f,
                    compactIcon, compactIcon), icon, ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(r.x + 1f, r.y + 24f, r.width - 2f, 14f), title, _lblTile);
                GUI.Label(new Rect(r.x + 1f, r.y + 39f, r.width - 2f, 13f), detail, _lblTile);
                return GUI.Button(r, GUIContent.none, GUIStyle.none);
            }
            float iconSize = Mathf.Min(30f, r.height - 10f);
            GUI.DrawTexture(new Rect(r.x + 5f, r.y + (r.height - iconSize) * 0.5f, iconSize, iconSize),
                icon, ScaleMode.ScaleToFit, true);
            float tx = r.x + iconSize + 10f;
            GUI.Label(new Rect(tx, r.y + 4f, r.width - (tx - r.x) - 3f, 25f), title, _lblCard);
            GUI.Label(new Rect(tx, r.y + r.height - 23f, r.width - (tx - r.x) - 3f, 18f), detail, _lblSmall);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        static string JunctionCardTitle(JunctionType type)
        {
            switch (type)
            {
                case JunctionType.Plain: return "Перекр.";
                case JunctionType.HighwayRoundabout: return "Шоссе кольцо";
                case JunctionType.Overpass: return "Мост ↔";
                case JunctionType.OverpassVertical: return "Мост ↕";
                default: return "Кольцо";
            }
        }

        bool DrawActionButton(Rect r, string text, Color fill, Color textColor, bool enabled)
        {
            Draw(r, _panel, fill);
            SetText(_lblAction, textColor);
            GUI.Label(r, text, _lblAction);
            SetText(_lblAction, Color.black);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = enabled;
            bool pressed = GUI.Button(r, GUIContent.none, GUIStyle.none);
            GUI.enabled = wasEnabled;
            return pressed;
        }

        float DrawHighway(float w, float top)
        {
            if (_city.CurrentTool != Tool.Highway && _city.CurrentTool != Tool.OneWay)
            {
                _highwayRect = Rect.zero;
                return top;
            }

            const float panelH = 54f;
            _highwayRect = new Rect(8f, top - panelH, w - 16f, panelH);
            Draw(_highwayRect, _panel, PanelColor);
            string hint = _city.CurrentTool == Tool.OneWay
                ? "Одностороннюю можно продолжить с конца, соединить с шоссе или пересечь им. Мосты доступны в развилках."
                : "Продолжай шоссе с его конца. Новые полосы строй по клеткам; встречное направление — отдельным штрихом.";
            GUI.Label(new Rect(_highwayRect.x + 10f, _highwayRect.y + 4f, _highwayRect.width - 20f, panelH - 8f), hint, _lblSmall);
            return _highwayRect.y - 6f;
        }

    }
}
