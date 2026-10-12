using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>Первый запуск: короткие подсказки мэра поверх города. Положение и завершение хранятся в PlayerPrefs.</summary>
    public sealed class CityTutorial : MonoBehaviour
    {
        const string DoneKey = "HexaCity.Tutorial.Done";
        const string StartedKey = "HexaCity.Tutorial.Started";
        const string StepKey = "HexaCity.Tutorial.Step";
        const float RefWidth = 420f;

        static readonly string[] Titles =
        {
            "Добро пожаловать!", "Сначала проложим дорогу", "Построй первый район",
            "Как город зарабатывает", "Связность решает", "Службы помогают городу",
            "Шоссе и односторонние дороги", "Город всегда можно расширить"
        };

        static readonly string[] Pages =
        {
            "Я мэр, буду подсказывать по ходу дела. Это твой город: можешь сразу пробовать строить. Нажми «Дальше», чтобы начать.",
            "Открой панель строительства кнопкой с деньгами и выбери «Дорога». Проведи пальцем по клеткам, чтобы соединить участки. Дороги стоят денег.",
            "Выбери жильё, торговлю или промышленность и ставь здания на свободные клетки рядом с дорогой. Одинаковые соседние здания образуют район.",
            "Город получает налоги автоматически. Доход показан сверху справа: базовые деньги идут всегда, а здания приносят налог каждый игровой тик.",
            "Районы должны иметь путь к дорожной сети. Довольство жителей влияет на налог, а объединённые районы одного типа получают множитель дохода.",
            "Построй службу и размести её рядом с районами. Скорая, полиция и пожарная часть дают профильный налоговый бонус в зоне действия.",
            "Шоссе — отдельное широкое полотно: строй его на земле или поверх обычной дороги. Односторонняя дорога строится на свободных клетках, соединяется с шоссе и может пересечь его мостом.",
            "Открывай новые сектора инструментом расширения. Город сохраняется автоматически; в меню можно вернуться кнопкой «В меню» в панели информации. Удачи!"
        };

        CitySystem _city;
        Texture2D _mayor;
        Texture2D _panelTex;
        GUIStyle _panel;
        GUIStyle _title;
        GUIStyle _body;
        GUIStyle _small;
        Rect _rect;
        int _step;
        bool _visible;

        public static bool IsCompleted => PlayerPrefs.GetInt(DoneKey, 0) == 1;
        public static bool IsInProgress => PlayerPrefs.GetInt(StartedKey, 0) == 1 && !IsCompleted;

        public void Bind(CitySystem city) { _city = city; }

        public void BeginOrResume()
        {
            if (IsCompleted) return;
            _step = Mathf.Clamp(PlayerPrefs.GetInt(StepKey, 0), 0, Titles.Length - 1);
            PlayerPrefs.SetInt(StartedKey, 1);
            PlayerPrefs.Save();
            _visible = true;
        }

        void EnsureStyles()
        {
            if (_panel != null) return;
            _mayor = ToolIcons.MakeMayor();
            _panelTex = CityDebugHud.RoundedTexture(64, 14f);
            _panel = CityDebugHud.Tinted(_panelTex, Color.white, 14);
            _title = CityDebugHud.Label(14, TextAnchor.MiddleLeft, FontStyle.Bold);
            _body = CityDebugHud.Label(12, TextAnchor.UpperLeft);
            _small = CityDebugHud.Label(10, TextAnchor.MiddleCenter);
        }

        void OnDestroy()
        {
            if (_mayor != null) Destroy(_mayor);
            if (_panelTex != null) Destroy(_panelTex);
        }

        float Scale => Mathf.Max(0.5f, Mathf.Min(Screen.width / RefWidth, Screen.height / 800f));

        public bool Contains(Vector2 screen)
        {
            if (!_visible || _rect.width <= 0) return false;
            Vector2 gui = new Vector2(screen.x, Screen.height - screen.y) / Scale;
            return _rect.Contains(gui);
        }

        void OnGUI()
        {
            if (!_visible || _city == null || !_city.Started) return;
            EnsureStyles();
            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s;
            float safeTop = (Screen.height - Screen.safeArea.yMax) / s;
            float panelW = Mathf.Min(w - 20f, 400f);
            const float panelH = 164f;
            _rect = new Rect((w - panelW) * 0.5f, safeTop + 12f, panelW, panelH);

            Color old = GUI.color;
            GUI.color = new Color32(0x20, 0x29, 0x38, 0xF4);
            GUI.Box(_rect, GUIContent.none, _panel);
            GUI.color = old;
            GUI.DrawTexture(new Rect(_rect.x + 12f, _rect.y + 12f, 44f, 44f), _mayor, ScaleMode.ScaleToFit, true);

            float textX = _rect.x + 66f;
            float textW = _rect.width - 78f;
            GUI.Label(new Rect(textX, _rect.y + 11f, textW, 23f), "МЭР ГОРОДА  ·  " + (_step + 1) + "/" + Titles.Length,
                StyleColor(_small, new Color32(0xF2, 0xB8, 0x4B, 0xFF)));
            GUI.Label(new Rect(textX, _rect.y + 31f, textW, 26f), Titles[_step],
                StyleColor(_title, new Color32(0xF7, 0xF7, 0xF4, 0xFF)));
            GUI.Label(new Rect(_rect.x + 14f, _rect.y + 62f, _rect.width - 28f, 55f), Pages[_step],
                StyleColor(_body, new Color32(0xE7, 0xEB, 0xEE, 0xFF)));

            float by = _rect.yMax - 38f;
            if (_step > 0) DrawButton(new Rect(_rect.x + 12f, by, 66f, 27f), "Назад", false, Previous);
            DrawButton(new Rect(_rect.xMax - 110f, by, 98f, 27f), "Пропустить", false, Skip);
            DrawButton(new Rect(_rect.xMax - 206f, by, 88f, 27f),
                _step == Titles.Length - 1 ? "Готово" : "Дальше", true, Next);
        }

        GUIStyle StyleColor(GUIStyle source, Color color)
        {
            CityGame.Buildings.CityDebugHud.SetText(source, color);
            return source;
        }

        void DrawButton(Rect r, string text, bool accent, System.Action action)
        {
            Color bg = accent ? new Color32(0xF2, 0xB8, 0x4B, 0xFF) : new Color32(0x3B, 0x45, 0x52, 0xFF);
            Color old = GUI.color;
            GUI.color = bg;
            GUI.Box(r, GUIContent.none, _panel);
            GUI.color = old;
            GUIStyle label = StyleColor(_small, accent ? new Color32(0x20, 0x29, 0x38, 0xFF) : Color.white);
            GUI.Label(r, text, label);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) action();
        }

        void Previous()
        {
            _step = Mathf.Max(0, _step - 1);
            StoreStep();
        }

        void Next()
        {
            if (_step >= Titles.Length - 1) { Finish(); return; }
            _step++;
            StoreStep();
        }

        void Skip() { Finish(); }

        void StoreStep()
        {
            PlayerPrefs.SetInt(StepKey, _step);
            PlayerPrefs.Save();
        }

        void Finish()
        {
            _visible = false;
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.SetInt(StartedKey, 0);
            PlayerPrefs.Save();
            if (_city != null) _city.Save();
        }
    }
}
