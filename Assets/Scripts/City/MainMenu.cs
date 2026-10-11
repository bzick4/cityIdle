using System.Collections.Generic;
using System.Text;
using CityGame.Save;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Главное меню в стиле заката: живой город в сумерках (MenuBackdrop), небо-градиент, солнце,
    /// горы с елями, логотип и скошенные кнопки. «Новый город» при существующем сохранении
    /// просит подтверждение: старый город будет перезаписан.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        const float RefWidth = 420f;
        const float RefHeight = 800f;

        public string Title = "CITY BUILDER";
        public string Subtitle = "расслабленный градостроитель";

        public Color Cream = new Color32(0xF4, 0xEA, 0xDC, 0xFF);
        public Color Accent = new Color32(0xFF, 0xB8, 0x34, 0xFF);
        public Color Ink = new Color32(0x1B, 0x1F, 0x2A, 0xFF);
        public Color ButtonFill = new Color32(0x27, 0x31, 0x45, 0xEB);
        public Color ButtonRim = new Color32(0x5A, 0x68, 0x84, 0xCC);

        CitySystem _city;
        SaveData _save;
        bool _hasSave;
        bool _confirm;
        MenuBackdrop _backdrop;
        readonly List<Texture2D> _textures = new List<Texture2D>();
        Texture2D _sky, _sun, _ridges, _arc, _iconNew, _iconFolder, _iconHex;
        GUIStyle _chamfer, _title, _sub, _btn, _btnSmall, _text;

        public void Bind(CitySystem city)
        {
            _city = city;
            _hasSave = SaveSystem.TryRead(out _save);
            _backdrop = MenuBackdrop.Create(city.CellSize, city.HexSize);
        }

        void OnDestroy()
        {
            foreach (Texture2D t in _textures)
                if (t != null) Destroy(t);
            if (_backdrop != null) Destroy(_backdrop.gameObject);
        }

        Texture2D Track(Texture2D t)
        {
            _textures.Add(t);
            return t;
        }

        float Scale => Mathf.Max(0.5f, Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight));

        void EnsureStyles()
        {
            if (_chamfer != null) return;
            _sky = Track(MenuScenery.Sky());
            _sun = Track(MenuScenery.Sun());
            _ridges = Track(MenuScenery.Ridges());
            _arc = Track(MenuScenery.Arc());
            _iconNew = Track(MenuScenery.Icon(MenuScenery.IconHexPlus));
            _iconFolder = Track(MenuScenery.Icon(MenuScenery.IconFolder));
            _iconHex = Track(MenuScenery.Icon(MenuScenery.IconHex));
            Texture2D chamfer = Track(MenuScenery.Chamfer());
            _chamfer = CityDebugHud.Tinted(chamfer, Color.white, 18);

            _title = CityDebugHud.Label(26, TextAnchor.MiddleCenter, FontStyle.Bold);
            _sub = CityDebugHud.Label(13, TextAnchor.MiddleCenter);
            _btn = CityDebugHud.Label(18, TextAnchor.MiddleLeft, FontStyle.Bold);
            _btnSmall = CityDebugHud.Label(11, TextAnchor.MiddleLeft);
            _text = CityDebugHud.Label(15, TextAnchor.MiddleCenter);
        }

        static string Spaced(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ' ') sb.Append("  ");
                else sb.Append(s[i]).Append(' ');
            }

            return sb.ToString().TrimEnd();
        }

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Tex(Rect r, Texture2D t, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        void Label(Rect r, string text, GUIStyle style, Color c)
        {
            Color keep = style.normal.textColor;
            style.normal.textColor = c;
            GUI.Label(r, text, style);
            style.normal.textColor = keep;
        }

        /// <summary>Плашка со скосами и тонкой рамкой.</summary>
        void Plate(Rect r, Color fill, Color rim)
        {
            Color old = GUI.color;
            GUI.color = rim;
            GUI.Box(r, GUIContent.none, _chamfer);
            GUI.color = fill;
            GUI.Box(new Rect(r.x + 1.5f, r.y + 1.5f, r.width - 3f, r.height - 3f), GUIContent.none, _chamfer);
            GUI.color = old;
        }

        /// <summary>Кнопка с иконкой слева, названием и необязательной пояснительной строкой.</summary>
        bool MenuButton(Rect r, Texture2D icon, string text, string hint, Color fill, Color rim, Color ink, bool enabled)
        {
            Plate(r, fill, rim);
            Tex(new Rect(r.x + 18f, r.y + r.height * 0.5f - 13f, 26f, 26f), icon, ink);

            bool hasHint = !string.IsNullOrEmpty(hint);
            float ty = hasHint ? r.y + 8f : r.y + r.height * 0.5f - 14f;
            Label(new Rect(r.x + 58f, ty, r.width - 70f, 28f), text, _btn, ink);
            if (hasHint)
            {
                Color soft = ink;
                soft.a *= 0.7f;
                Label(new Rect(r.x + 58f, r.y + 34f, r.width - 70f, 16f), hint, _btnSmall, soft);
            }

            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        void OnGUI()
        {
            if (_city == null) return;
            EnsureStyles();

            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s;
            float h = Screen.height / s;
            float bottomInset = Screen.safeArea.yMin / s;

            // Сумерки поверх живого города.
            Tex(new Rect(0, 0, w, h), _sky, Color.white);

            // Солнце над дальним хребтом.
            const float sunSize = 170f;
            Tex(new Rect(w * 0.76f - sunSize * 0.5f, h * 0.705f - sunSize * 0.5f, sunSize, sunSize), _sun, Color.white);

            // Горы и ели.
            float ridgeTop = h * 0.58f;
            Tex(new Rect(0, ridgeTop, w, h - ridgeTop), _ridges, Color.white);

            DrawLogo(w, h);
            DrawButtons(w, h, bottomInset);
            if (_confirm) DrawConfirm(w, h);
        }

        void DrawLogo(float w, float h)
        {
            float cx = w * 0.5f;
            float baseY = h * 0.20f;

            // Дуга и силуэт города.
            Tex(new Rect(cx - 78f, baseY - 64f, 156f, 64f * 1.0f), _arc, Cream);
            float[,] b =
            {
                { -62, 10, 14 }, { -50, 14, 24 }, { -34, 16, 32 }, { -14, 20, 62 },
                { 8, 16, 34 }, { 25, 14, 24 }, { 40, 12, 17 }
            };
            for (int i = 0; i < b.GetLength(0); i++)
                Fill(new Rect(cx + b[i, 0], baseY - b[i, 2] - 2f, b[i, 1], b[i, 2]), Cream);
            Fill(new Rect(cx - 6f, baseY - 62f - 16f, 4f, 16f), Cream);
            Fill(new Rect(cx - 70f, baseY - 2f, 140f, 2f), Cream);

            // Название.
            string spaced = Spaced(Title);
            Rect t = new Rect(0, baseY + 8f, w, 40f);
            Label(new Rect(t.x + 1.5f, t.y + 2f, t.width, t.height), spaced, _title, new Color(0, 0, 0, 0.4f));
            Label(t, spaced, _title, Cream);

            // Линия с шестиугольником.
            float ly = baseY + 58f;
            Color line = new Color(Cream.r, Cream.g, Cream.b, 0.55f);
            Fill(new Rect(cx - 90f, ly, 66f, 1f), line);
            Fill(new Rect(cx + 24f, ly, 66f, 1f), line);
            Tex(new Rect(cx - 8f, ly - 8f, 16f, 16f), _iconHex, Cream);
            Label(new Rect(0, ly + 12f, w, 22f), Subtitle, _sub, new Color(Cream.r, Cream.g, Cream.b, 0.8f));
        }

        void DrawButtons(float w, float h, float bottomInset)
        {
            const float bh = 56f;
            const float gap = 12f;
            float bw = Mathf.Min(w - 48f, 320f);
            float x = (w - bw) * 0.5f;
            float y2 = h - bottomInset - 28f - bh;
            float y1 = y2 - gap - bh;

            Color dimInk = new Color(Cream.r, Cream.g, Cream.b, 0.4f);

            var fresh = new Rect(x, y1, bw, bh);
            if (MenuButton(fresh, _iconNew, "Новый город", "рельеф создаётся случайно",
                    Accent, new Color(1f, 0.85f, 0.5f, 0.9f), Ink, !_confirm))
            {
                if (_hasSave) _confirm = true;
                else _city.NewGame();
            }

            string info = _hasSave
                ? string.Format("{0:0} $ · зданий {1} · {2:dd.MM HH:mm}", _save.money, _save.buildings.Count, SaveSystem.LastWrite)
                : "нет сохранённого города";
            var cont = new Rect(x, y2, bw, bh);
            Color fill = _hasSave ? ButtonFill : new Color(ButtonFill.r, ButtonFill.g, ButtonFill.b, 0.6f);
            if (MenuButton(cont, _iconFolder, "Продолжить", info, fill, ButtonRim,
                    _hasSave ? Cream : dimInk, _hasSave && !_confirm))
                _city.ContinueGame();
        }

        void DrawConfirm(float w, float h)
        {
            float bw = Mathf.Min(w - 48f, 320f);
            var box = new Rect((w - bw) * 0.5f, h * 0.36f, bw, 150f);
            Fill(new Rect(0, 0, w, h), new Color(0.04f, 0.05f, 0.09f, 0.55f));
            Plate(box, new Color32(0x1F, 0x27, 0x3A, 0xF5), ButtonRim);
            Label(new Rect(box.x + 14f, box.y + 12f, box.width - 28f, 54f),
                "Начать новый город? Старый город будет удалён.", _text, Cream);

            var yes = new Rect(box.x + 14f, box.y + 84f, box.width * 0.5f - 21f, 48f);
            var no = new Rect(box.x + box.width * 0.5f + 7f, box.y + 84f, box.width * 0.5f - 21f, 48f);

            Plate(yes, Accent, new Color(1f, 0.85f, 0.5f, 0.9f));
            Label(yes, "Начать", _text, Ink);
            if (GUI.Button(yes, GUIContent.none, GUIStyle.none))
            {
                _confirm = false;
                _city.NewGame();
            }

            Plate(no, ButtonFill, ButtonRim);
            Label(no, "Отмена", _text, Cream);
            if (GUI.Button(no, GUIContent.none, GUIStyle.none)) _confirm = false;
        }
    }
}
