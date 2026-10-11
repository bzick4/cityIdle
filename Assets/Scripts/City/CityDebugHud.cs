using System;
using CityGame.CellRoads;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Временный интерфейс на IMGUI под вертикальный телефон. Раскладка рассчитана на ширину 420 условных единиц,
    /// масштаб берётся по ширине экрана, учитывается вырез и нижняя полоса (safe area).
    /// Кнопки инструментов внизу, под большим пальцем.
    /// </summary>
    public sealed class CityDebugHud : MonoBehaviour
    {
        const float RefWidth = 420f;
        const float RefHeight = 800f;

        /// <summary>Отладочная строка про ввод и координаты. Выключи, когда всё заработает.</summary>
        public bool ShowDiagnostics = true;

        CitySystem _city;
        string _toast = "";
        float _toastUntil;
        Rect _topRect;
        Rect _bottomRect;
        Rect _listRect;
        Rect _dialogRect;
        Rect _junctionRect;
        Rect _serviceRect;

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

        float Scale => Mathf.Max(0.5f, Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight));

        /// <summary>screen: пиксели экрана, начало внизу слева.</summary>
        public bool IsPointerBlocked(Vector2 screen)
        {
            var gui = new Vector2(screen.x, Screen.height - screen.y) / Scale;
            return _topRect.Contains(gui) || _bottomRect.Contains(gui) || _listRect.Contains(gui) ||
                   (_city != null && _city.Tapper != null && _city.Tapper.HasPending && _dialogRect.Contains(gui)) ||
                   (_city != null && _city.JunctionTool != null && _city.JunctionTool.HasSelection &&
                    _junctionRect.Contains(gui)) ||
                   (_city != null && _city.CurrentTool == Tool.Service && _serviceRect.Contains(gui));
        }

        static readonly string[] Names =
        {
            "Рука", "Дорога", "Развилка", "Жильё", "Торговля", "Промышл.", "Служба", "Снести", "Расширить"
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

        void OnGUI()
        {
            if (_city == null || _city.Economy == null) return;

            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s;
            float h = Screen.height / s;

            Rect safe = Screen.safeArea;
            float topInset = (Screen.height - safe.yMax) / s;
            float bottomInset = safe.yMin / s;

            GUI.skin.label.fontSize = 14;
            GUI.skin.button.fontSize = 13;
            GUI.skin.box.fontSize = 13;

            // Верхняя панель
            _topRect = new Rect(0, topInset, w, 44);
            GUI.Box(_topRect, GUIContent.none);
            GUI.Label(new Rect(8, topInset + 11, w - 190, 24),
                string.Format("{0:0} $   +{1:0.0}/с", _city.Economy.Money, _city.Economy.IncomePerSecond));

            bool cars = _city.Traffic.Visible;
            bool newCars = GUI.Toggle(new Rect(w - 176, topInset + 6, 60, 32), cars, "Авто", GUI.skin.button);
            if (newCars != cars) _city.Traffic.Visible = newCars;

            bool heat = _city.Heatmap.Visible;
            bool newHeat = GUI.Toggle(new Rect(w - 108, topInset + 6, 100, 32), heat, "Нагрузка", GUI.skin.button);
            if (newHeat != heat) _city.Heatmap.Visible = newHeat;

            // Компактный список районов слева под верхней панелью
            var districts = _city.Model.Districts;
            int rows = Mathf.Min(districts.Count, 5);
            if (rows > 0 || _city.Flow.UnconnectedDistricts > 0)
            {
                float listH = 8 + (rows + (_city.Flow.UnconnectedDistricts > 0 ? 1 : 0)) * 19;
                _listRect = new Rect(6, topInset + 50, 230, listH);
                GUI.Box(_listRect, GUIContent.none);
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

                    GUI.Label(new Rect(12, topInset + 52 + i * 19, 200, 20), line);
                }

                if (_city.Flow.UnconnectedDistricts > 0)
                    GUI.Label(new Rect(12, topInset + 52 + rows * 19, 200, 20),
                        "Без дороги: " + _city.Flow.UnconnectedDistricts);
            }
            else
            {
                _listRect = Rect.zero;
            }

            if (ShowDiagnostics)
            {
                Vector2 p = CityGame.Roads.PointerInput.Position;
                Camera cam = _city.Rig.Cam;
                Vector3 wp = cam != null
                    ? cam.ScreenToWorldPoint(new Vector3(p.x, p.y, -cam.transform.position.z))
                    : Vector3.zero;
                GUI.skin.label.fontSize = 11;
                var cellUnder = new Vector2Int(Mathf.FloorToInt(wp.x / _city.CellSize), Mathf.FloorToInt(wp.y / _city.CellSize));
                var sectorUnder = _city.Sectors.SectorAt(wp);
                var trees = _city.Biomes.TreesOf(sectorUnder);
                GUI.Label(new Rect(6, h - 2 * 58 - 10 - bottomInset - 50, w - 12, 16),
                    string.Format("биом {0}, деревья {1}/{2}, лес-бонус {3}",
                        CityGame.Sectors.BiomeSpecs.Title(_city.Biomes.BiomeAt(cellUnder)), trees.y, trees.x,
                        _city.Biomes.ForestBonusActive(sectorUnder) ? "да" : "нет"));
                GUI.Label(new Rect(6, h - 2 * 58 - 10 - bottomInset - 36, w - 12, 34),
                    string.Format("{0} | экран {1}x{2} | касание {3:0},{4:0} | мир {5:0.0},{6:0.0} | авто {7}, стоят {8}",
                        CityGame.Roads.PointerInput.Source, Screen.width, Screen.height, p.x, p.y, wp.x, wp.y,
                        _city.Traffic.Count, _city.Traffic.Stopped));
                GUI.skin.label.fontSize = 14;
            }

            // Кнопки внизу: два ряда
            int tools = Enum.GetValues(typeof(Tool)).Length;
            int perRow = (tools + 1) / 2;
            float bh = 58;
            float barH = 2 * bh + 10;
            _bottomRect = new Rect(0, h - barH - bottomInset, w, barH + bottomInset);
            GUI.Box(_bottomRect, GUIContent.none);

            float bw = (w - 8) / perRow;
            for (int i = 0; i < tools; i++)
            {
                var tool = (Tool)i;
                string label = LabelFor(tool);
                int row = i / perRow;
                int col = i % perRow;
                bool on = _city.CurrentTool == tool;
                var rect = new Rect(4 + col * bw, h - barH - bottomInset + 4 + row * (bh + 2), bw - 2, bh);
                bool pressed = GUI.Toggle(rect, on, label, GUI.skin.button);
                if (pressed && !on) _city.Select(tool);
                else if (!pressed && on && tool != Tool.Pan) _city.Select(Tool.Pan); // повторный тап: назад к «Рука»
            }

            // Выбор службы: вид, радиус и бонус.
            if (_city.CurrentTool == Tool.Service)
            {
                float panelH = 92;
                float panelY = h - barH - bottomInset - 40 - panelH;
                _serviceRect = new Rect(8, panelY, w - 16, panelH);
                GUI.Box(_serviceRect, GUIContent.none);

                ServiceKind cur = _city.CurrentService;
                GUI.Label(new Rect(16, panelY + 4, w - 32, 22),
                    string.Format("Радиус {0} кл. Профильный тип +{1:0}% к налогу, остальные +{2:0}%",
                        ServiceSpecs.Radius(cur), ServiceSpecs.Bonus(cur, ProfileOf(cur)) * 100f,
                        ServiceSpecs.Bonus(cur, OtherOf(cur)) * 100f));

                float sw = (w - 32) / 3f;
                for (int i = 0; i < 3; i++)
                {
                    var kind = (ServiceKind)i;
                    bool on = cur == kind;
                    var r = new Rect(16 + i * sw, panelY + 34, sw - 4, 52);
                    string text = ServiceSpecs.Title(kind) + "\n" + _city.Economy.ServiceCost[i];
                    bool pressed = GUI.Toggle(r, on, text, GUI.skin.button);
                    if (pressed && !on) _city.SelectService(kind);
                }
            }
            else
            {
                _serviceRect = Rect.zero;
            }

            // Выбор развилки: варианты и подтверждение покупки.
            JunctionTapper jt = _city.JunctionTool;
            if (jt != null && jt.HasSelection)
            {
                float panelH = 138;
                float panelY = h - barH - bottomInset - 40 - panelH;
                _junctionRect = new Rect(8, panelY, w - 16, panelH);
                GUI.Box(_junctionRect, GUIContent.none);

                GUI.Label(new Rect(16, panelY + 6, w - 32, 22),
                    "Развилка сейчас: " + JunctionSpecs.Title(jt.Current));

                var types = jt.Available();
                float ow = (w - 32) / Mathf.Max(1, types.Count);
                for (int i = 0; i < types.Count; i++)
                {
                    JunctionType t = types[i];
                    float cost = jt.CostFor(t);
                    string price = t == jt.Current ? "сейчас" : cost > 0f ? cost.ToString("0") : "бесплатно";
                    bool on = jt.Pending == t;
                    var r = new Rect(16 + i * ow, panelY + 32, ow - 4, 54);
                    bool pressed = GUI.Toggle(r, on, JunctionSpecs.Short(t) + "\n" + price, GUI.skin.button);
                    if (pressed && !on) jt.Choose(t);
                }

                bool changed = jt.Pending != jt.Current;
                float pay = jt.CostFor(jt.Pending);
                GUI.enabled = changed && _city.Economy.Money >= pay;
                string applyText = !changed ? "Выбери вариант" : pay > 0f ? "Применить за " + pay.ToString("0") : "Применить";
                if (GUI.Button(new Rect(16, panelY + 92, (w - 32) * 0.62f - 4, 40), applyText)) jt.Apply();
                GUI.enabled = true;
                if (GUI.Button(new Rect(16 + (w - 32) * 0.62f, panelY + 92, (w - 32) * 0.38f, 40), "Отмена")) jt.Cancel();
            }
            else
            {
                _junctionRect = Rect.zero;
            }

            if (Time.unscaledTime < _toastUntil)
            {
                GUI.skin.label.fontSize = 15;
                GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(0, h - 2 * 58 - 10 - bottomInset - 64, w, 26), _toast);
                GUI.skin.label.alignment = TextAnchor.UpperLeft;
                GUI.skin.label.fontSize = 14;
            }

            // Подтверждение открытия сектора
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
    }
}
