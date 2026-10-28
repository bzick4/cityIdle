using System;
using System.Collections.Generic;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Обучение по шагам. Каждый шаг называет цель, объясняет, зачем она нужна, и показывает, что нажимать:
    /// пульсирующая рамка и стрелка на нужной кнопке (магазин, раздел, значок инструмента), затем подсказка
    /// прямо на карте (где вести дорогу, куда ставить здание, какой сектор открыть). Шаг засчитывается сам,
    /// когда действие сделано. Положение хранится в PlayerPrefs.
    /// </summary>
    public sealed class CityTutorial : MonoBehaviour
    {
        const string DoneKey = "HexaCity.Tutorial2.Done";
        const string StartedKey = "HexaCity.Tutorial2.Started";
        const string StepKey = "HexaCity.Tutorial2.Step";
        const float RefWidth = 420f;

        sealed class Step
        {
            public string Title;
            public string Why;
            public string Do;
            public Tool Tool;
            public bool HasTool;
            public Func<int> Metric;   // что считаем: клетки дорог, здания и т. д.; нет значения: шаг ручной
            public int Need = 1;       // насколько Metric должен вырасти с начала шага
            public int Base;
            public string Anchor;
            public bool HasSwatch;
            public Color Swatch;       // цвет биома в углу панели
        }

        static readonly Color Amber = new Color32(0xF2, 0xB8, 0x4B, 0xFF);

        CitySystem _city;
        readonly List<Step> _steps = new List<Step>();
        Texture2D _mayor, _panelTex, _ring, _dot, _arrowDown, _arrowUp;
        GUIStyle _panel, _title, _body, _small, _action;
        Rect _rect;
        int _step;
        bool _visible;
        float _doneAt = -1f;

        // Подсказки на карте.
        readonly List<Vector2Int> _roadDemo = new List<Vector2Int>();
        readonly List<Vector2Int> _spots = new List<Vector2Int>();
        float _spotTime = -10f;

        public static bool IsCompleted => PlayerPrefs.GetInt(DoneKey, 0) == 1;
        public static bool IsInProgress => PlayerPrefs.GetInt(StartedKey, 0) == 1 && !IsCompleted;

        public void Bind(CitySystem city)
        {
            _city = city;
        }

        // ---------------------------------------------------------------
        // Шаги

        void BuildSteps()
        {
            if (_steps.Count > 0) return;

            _steps.Add(new Step
            {
                Title = "Добро пожаловать!",
                Why = "Я мэр. Покажу на кнопках, что нажимать и зачем. Внизу полоска: слева круглая кнопка $ для строительства, справа деньги и доход в секунду. Налоги капают сами.",
                Do = "Нажми «Дальше».",
                Anchor = "info"
            });
            _steps.Add(new Step
            {
                Title = "Проложи дорогу", Tool = Tool.Road, HasTool = true,
                Why = "Дорога соединяет дома, магазины и заводы. Без неё нет налогов, а машинам некуда ехать.",
                Do = "Открой $ → ДОРОГИ → Дорога, потом проведи пальцем по карте.",
                Metric = () => _city.RoadMap.Count, Need = 4
            });
            _steps.Add(new Step
            {
                Title = "Построй жильё", Tool = Tool.Residential, HasTool = true,
                Why = "В домах живут люди: они платят налог и ездят на машинах. Каждый дом даёт городу жителей.",
                Do = "Открой $ → РАЙОНЫ → Жильё и тапни по клетке рядом с дорогой.",
                Metric = () => Count(BuildingCategory.Residential)
            });
            _steps.Add(new Step
            {
                Title = "Построй торговлю", Tool = Tool.Commercial, HasTool = true,
                Why = "Жители ездят за покупками, магазины платят налог. Магазин должен стоять у дороги, которая ведёт к жилью.",
                Do = "Открой $ → РАЙОНЫ → Торговля и тапни по клетке у дороги.",
                Metric = () => Count(BuildingCategory.Commercial)
            });
            _steps.Add(new Step
            {
                Title = "Построй промышленность", Tool = Tool.Industrial, HasTool = true,
                Why = "Заводы дают работу и тоже платят налог. Одинаковые здания рядом сливаются в район, а район даёт больше денег.",
                Do = "Открой $ → РАЙОНЫ → Промышл. и тапни по клетке у дороги.",
                Metric = () => Count(BuildingCategory.Industrial)
            });
            _steps.Add(new Step
            {
                Title = "Смотри, как живёт город",
                Why = "Машины ездят между домом и работой, налоги идут сами. Если дорога перегружена, будет затор. Нажми на полоску внизу: увидишь районы и довольство жителей.",
                Do = "Нажми «Дальше», когда насмотришься.",
                Anchor = "info"
            });
            _steps.Add(new Step
            {
                Title = "Поставь службу", Tool = Tool.Service, HasTool = true,
                Why = "Скорая, полиция и пожарные дают бонус к налогу на ближайших клетках. Ставь их рядом с районами.",
                Do = "Открой $ → СЛУЖБЫ → любая служба и тапни по клетке рядом с домами.",
                Metric = () => Count(BuildingCategory.Service)
            });
            _steps.Add(new Step
            {
                Title = "Расширь город", Tool = Tool.Expand, HasTool = true,
                Why = "Новые сектора открываются бесплатно, но только соседние. В каждом свой рельеф: лес, снег, пустыня, горы или море.",
                Do = "Открой $ → РАЙОНЫ → Расширить, тапни по тёмному сектору и подтверди «Открыть».",
                Metric = OpenCount
            });
            _steps.Add(new Step
            {
                Title = "Что ещё есть",
                Why = "Первые три здания работают сразу, а дальше каждому нужны ток и вода: ставь электростанцию и водонапорную башню в разделе ЭНЕРГИЯ рядом с районами (одна тянет 30 клеток). Без них здание не платит налог и показывает значок. Шоссе и односторонние дороги ускоряют потоки. В горах дороги идут тоннелями. В море дороги становятся мостами, а канал и порт дают доход от кораблей. Автобусная станция (одна на сектор) пускает автобусы. Ночью зажигается свет.",
                Do = "Нажми «Готово». Обучение можно повторить кнопкой ОБУЧ. в панели с полоски.",
                Anchor = "shop"
            });
        }

        // ---------------------------------------------------------------
        // Уроки по биомам: рассказываем о каждом биоме один раз, когда он впервые открыт на карте.

        const string BiomeSeenKey = "HexaCity.BiomeSeen";
        Step _lesson;
        Biome _lessonBiome;
        float _lessonCheck;

        static string BiomeWhy(Biome b)
        {
            switch (b)
            {
                case Biome.Snow:
                    return "Здания стоят на 20% дешевле. Бонуса леса нет, зато хорошее место для дешёвого жилья и промзоны.";
                case Biome.Desert:
                    return "Здания тоже на 20% дешевле. Деревьев и налогового бонуса нет, просто недорогая земля под застройку.";
                case Biome.Mountain:
                    return "Здания в горах строить нельзя. Дорога идёт тоннелем и стоит в 1,5 раза дороже, фонарей ночью нет. Горы нужны как проход к дальним секторам.";
                case Biome.Sea:
                    return "Здания и мосты на 20% дороже. Зато можно копать каналы и ставить порт (один на сектор, только на воде): корабли приносят доход.";
                default:
                    return "Пока в секторе осталось хотя бы 30% деревьев, районы платят на 10% больше. Здания вырубают деревья под собой, так что не застраивай лес целиком.";
            }
        }

        static string BiomeName(Biome b)
        {
            switch (b)
            {
                case Biome.Snow: return "Снег: дёшево строить";
                case Biome.Desert: return "Пустыня: дёшево строить";
                case Biome.Mountain: return "Горы: только дороги";
                case Biome.Sea: return "Море: порты и мосты";
                default: return "Лес: налоговый бонус";
            }
        }

        /// <summary>Какие биомы сейчас есть среди открытых секторов (битовая маска).</summary>
        int OpenBiomeMask()
        {
            int mask = 0;
            foreach (HexCoord h in _city.Sectors.Open) mask |= 1 << (int)_city.Biomes.Map.BiomeOf(h);
            return mask;
        }

        /// <summary>Если есть биом, о котором ещё не рассказывали, показываем урок о нём.</summary>
        void TryStartLesson()
        {
            if (_lesson != null || _city == null || !_city.Started || _city.Biomes == null) return;
            int unseen = OpenBiomeMask() & ~PlayerPrefs.GetInt(BiomeSeenKey, 0);
            if (unseen == 0) return;
            for (int i = 0; i < BiomeSpecs.Count; i++)
            {
                if ((unseen & (1 << i)) == 0) continue;
                var b = (Biome)i;
                _lessonBiome = b;
                _lesson = new Step
                {
                    Title = BiomeName(b),
                    Why = BiomeWhy(b),
                    Do = "Нажми «Понятно».",
                    HasSwatch = true,
                    Swatch = BiomeSpecs.Ground(b)
                };
                return;
            }
        }

        void DismissLesson()
        {
            if (_lesson == null) return;
            PlayerPrefs.SetInt(BiomeSeenKey, PlayerPrefs.GetInt(BiomeSeenKey, 0) | (1 << (int)_lessonBiome));
            PlayerPrefs.Save();
            _lesson = null;
            TryStartLesson(); // следующий непоказанный биом, если есть
        }

        int Count(BuildingCategory category)
        {
            int n = 0;
            foreach (BuildingCell c in _city.Model.Cells)
                if (c.Category == category) n++;
            return n;
        }

        int OpenCount()
        {
            int n = 0;
            foreach (HexCoord h in _city.Sectors.Open) n++;
            return n;
        }

        // ---------------------------------------------------------------
        // Запуск и сохранение

        public void BeginOrResume()
        {
            if (IsCompleted) return;
            _locked = true;
            BuildSteps();
            _step = Mathf.Clamp(PlayerPrefs.GetInt(StepKey, 0), 0, _steps.Count - 1);
            PlayerPrefs.SetInt(StartedKey, 1);
            PlayerPrefs.Save();
            _visible = true;
            EnterStep();
        }

        /// <summary>Пройти обучение заново с первого шага.</summary>
        public void Restart()
        {
            _locked = false; // повтор из меню не блокирует игру
            PlayerPrefs.SetInt(BiomeSeenKey, 0);
            _lesson = null;
            BuildSteps();
            PlayerPrefs.SetInt(DoneKey, 0);
            PlayerPrefs.SetInt(StartedKey, 1);
            PlayerPrefs.SetInt(StepKey, 0);
            PlayerPrefs.Save();
            _step = 0;
            _visible = true;
            EnterStep();
        }

        void EnterStep()
        {
            _doneAt = -1f;
            _roadDemo.Clear();
            _spots.Clear();
            _spotTime = -10f;
            Step st = _steps[_step];
            if (st.Metric != null) st.Base = st.Metric();
        }

        void Advance()
        {
            if (_step >= _steps.Count - 1)
            {
                Finish();
                return;
            }

            _step++;
            PlayerPrefs.SetInt(StepKey, _step);
            PlayerPrefs.Save();
            EnterStep();
            TryStartLesson();
        }

        void Finish()
        {
            _visible = false;
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.SetInt(StartedKey, 0);
            PlayerPrefs.Save();
            if (_city != null) _city.Save();
        }

        void OnDestroy()
        {
            DestroyTex(ref _mayor);
            DestroyTex(ref _panelTex);
            DestroyTex(ref _ring);
            DestroyTex(ref _dot);
            DestroyTex(ref _arrowDown);
            DestroyTex(ref _arrowUp);
        }

        static void DestroyTex(ref Texture2D t)
        {
            if (t != null) Destroy(t);
            t = null;
        }

        // ---------------------------------------------------------------
        // Каждый кадр

        void Update()
        {
            if (_city == null || !_city.Started) return;
            if (!_visible)
            {
                // Обучение закончено: следим, не открылся ли новый биом.
                if (_lesson == null && Time.unscaledTime - _lessonCheck > 0.5f)
                {
                    _lessonCheck = Time.unscaledTime;
                    TryStartLesson();
                }

                return;
            }

            if (_lesson != null) return;
            Step st = _steps[_step];
            if (_locked)
            {
                // Строить можно только тем инструментом, который просит текущий шаг.
                Tool allowed = st.Metric != null && st.HasTool ? st.Tool : Tool.Pan;
                if (_city.CurrentTool != allowed && _city.CurrentTool != Tool.Pan) _city.Select(Tool.Pan);
            }
            if (_doneAt < 0f && st.Metric != null && st.Metric() - st.Base >= st.Need) _doneAt = Time.unscaledTime;
            if (_doneAt >= 0f && Time.unscaledTime - _doneAt > 0.9f) Advance();
        }

        float Scale => Mathf.Max(0.5f, Mathf.Min(Screen.width / RefWidth, Screen.height / 800f));

        bool _locked;

        /// <summary>
        /// Пока идёт первое обучение, весь интерфейс закрыт, кроме подсвеченного элемента и панели обучения.
        /// Точка задана в пикселях экрана, сверху вниз (как Event.mousePosition при единичной матрице).
        /// </summary>
        public bool BlocksUi(Vector2 guiPixel)
        {
            if (!_locked || !_visible || _steps.Count == 0 || _city == null) return false;
            if (_lesson != null) return !(_rect.width > 0f && _rect.Contains(guiPixel / Scale));
            if (_rect.width > 0f && _rect.Contains(guiPixel / Scale)) return false;

            string anchor, tag;
            CityDebugHud hud = _city.Hud;
            if (hud != null && ResolveAnchor(_steps[_step], out anchor, out tag))
            {
                Rect a;
                if (hud.Anchors.TryGetValue(anchor, out a))
                {
                    float k = hud.GuiScale;
                    Rect r = new Rect(a.x * k - 6f, a.y * k - 6f, a.width * k + 12f, a.height * k + 12f);
                    if (r.Contains(guiPixel)) return false;
                }
            }
            return true;
        }

        public bool Contains(Vector2 screen)
        {
            if ((!_visible && _lesson == null) || _rect.width <= 0) return false;
            Vector2 gui = new Vector2(screen.x, Screen.height - screen.y) / Scale;
            return _rect.Contains(gui);
        }

        // ---------------------------------------------------------------
        // Текстуры и стили

        void EnsureStyles()
        {
            if (_panel != null) return;
            _mayor = ToolIcons.MakeMayor();
            _panelTex = CityDebugHud.RoundedTexture(64, 14f);
            _panel = CityDebugHud.Tinted(_panelTex, Color.white, 14);
            _title = CityDebugHud.Label(14, TextAnchor.MiddleLeft, FontStyle.Bold);
            _body = CityDebugHud.Label(12, TextAnchor.UpperLeft);
            _action = CityDebugHud.Label(12, TextAnchor.UpperLeft, FontStyle.Bold);
            _small = CityDebugHud.Label(10, TextAnchor.MiddleCenter);
            _ring = MakeRing(64, 0.78f);
            _dot = MakeRing(32, 0f);
            _arrowDown = MakeArrow(true);
            _arrowUp = MakeArrow(false);
        }

        static Texture2D MakeRing(int n, float inner)
        {
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01((1f - d) * c * 0.5f);
                    if (inner > 0f) a = Mathf.Min(a, Mathf.Clamp01((d - inner) * c * 0.5f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeArrow(bool down)
        {
            const int n = 32;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float ty = down ? (n - 1 - y) : y;           // 0 у острия
                    float half = ty / (n - 1) * (n * 0.5f - 1f);
                    float dx = Mathf.Abs(x - (n - 1) * 0.5f);
                    float a = Mathf.Clamp01(half - dx + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ---------------------------------------------------------------
        // Что подсвечивать

        static string PageOf(Tool tool)
        {
            switch (tool)
            {
                case Tool.Road: return "Roads";
                case Tool.Service: return "Services";
                default: return "Districts";
            }
        }

        static string TileName(Tool tool)
        {
            switch (tool)
            {
                case Tool.Road: return "Дорога";
                case Tool.Residential: return "Жильё";
                case Tool.Commercial: return "Торговля";
                case Tool.Industrial: return "Промышл.";
                case Tool.Service: return "службу";
                default: return "Расширить";
            }
        }

        static string CardName(string page)
        {
            return page == "Roads" ? "ДОРОГИ" : page == "Services" ? "СЛУЖБЫ" : "РАЙОНЫ";
        }

        /// <summary>Выбирает, на какой элемент интерфейса показывать, и короткую подпись у стрелки.</summary>
        bool ResolveAnchor(Step st, out string anchor, out string tag)
        {
            anchor = null;
            tag = null;
            CityDebugHud hud = _city.Hud;
            if (hud == null) return false;

            if (st.Metric == null)
            {
                anchor = st.Anchor;
                tag = anchor == "shop" ? "Кнопка строительства" : "Деньги и доход";
                return anchor != null;
            }

            if (_city.ActiveStroke != null && _city.ActiveStroke.Pending)
            {
                anchor = "confirm";
                tag = "Подтверди";
                return true;
            }

            if (st.Tool == Tool.Expand && _city.Tapper != null && _city.Tapper.HasPending)
            {
                anchor = "open";
                tag = "Подтверди";
                return true;
            }

            if (_city.CurrentTool == st.Tool) return false; // инструмент выбран, дальше подсказки на карте

            if (!hud.ToolsOpen)
            {
                anchor = "shop";
                tag = "Нажми сюда";
                return true;
            }

            string page = PageOf(st.Tool);
            if (hud.ToolsPage == "Home")
            {
                anchor = "card:" + page;
                tag = "Раздел " + CardName(page);
                return true;
            }

            if (hud.ToolsPage != page)
            {
                anchor = "back";
                tag = "Вернись назад";
                return true;
            }

            anchor = st.Tool == Tool.Service ? "service:0" : "tool:" + st.Tool;
            tag = st.Tool == Tool.Service ? "Выбери службу" : "Выбери «" + TileName(st.Tool) + "»";
            return true;
        }

        /// <summary>Текст «Сейчас» под описанием шага, отражает ближайшее действие.</summary>
        string NowText(Step st, string anchor)
        {
            if (st.Metric == null) return st.Do;
            if (_doneAt >= 0f) return "Готово! ✓";
            if (anchor == "shop") return "Сейчас: нажми круглую кнопку $ внизу слева.";
            if (anchor != null && anchor.StartsWith("card:")) return "Сейчас: выбери раздел " + CardName(PageOf(st.Tool)) + ".";
            if (anchor == "back") return "Сейчас: нажми НАЗАД и выбери нужный раздел.";
            if (anchor == "open") return "Сейчас: нажми «ОТКРЫТЬ».";
            if (anchor == "confirm") return "Сейчас: нажми «ПОДТВЕРДИТЬ», и дорога построится.";
            if (anchor != null) return "Сейчас: выбери «" + (st.Tool == Tool.Service ? "любую службу" : TileName(st.Tool)) + "».";
            switch (st.Tool)
            {
                case Tool.Road: return "Сейчас: проведи пальцем вдоль точек по карте, дорога построится.";
                case Tool.Expand: return "Сейчас: тапни по тёмному сектору с кольцом.";
                default: return "Сейчас: тапни по клетке с кольцом, рядом с дорогой.";
            }
        }

        // ---------------------------------------------------------------
        // Подсказки на карте

        Vector2 CenterOf(Vector2Int c)
        {
            float cs = _city.CellSize;
            return new Vector2((c.x + 0.5f) * cs, (c.y + 0.5f) * cs);
        }

        bool Free(Vector2Int c)
        {
            if (_city.Model.HasCell(c) || _city.RoadMap.Has(c)) return false;
            if (!_city.Sectors.IsOpenAt(CenterOf(c))) return false;
            if (!_city.Biomes.CanBuild(c) || _city.Biomes.IsWater(c)) return false;
            return !_city.Ports.Has(c) && !_city.BusStations.Has(c);
        }

        /// <summary>Клетка на экране и не под панелью обучения.</summary>
        bool Visible(Vector2Int c, float s)
        {
            Vector2 g;
            if (!ToGui(CenterOf(c), s, out g)) return false;
            float w = Screen.width / s, h = Screen.height / s;
            if (g.x < 12f || g.x > w - 12f || g.y < 12f || g.y > h - 150f) return false;
            return !(_rect.width > 0f && _rect.Contains(g));
        }

        void RefreshSpots(Tool tool, float s)
        {
            if (Time.unscaledTime - _spotTime < 0.4f) return;
            _spotTime = Time.unscaledTime;
            Camera cam = _city.Rig != null ? _city.Rig.Cam : null;
            if (cam == null) return;
            float cs = _city.CellSize;
            Vector3 cp = cam.transform.position;
            var mid = new Vector2Int(Mathf.FloorToInt(cp.x / cs), Mathf.FloorToInt(cp.y / cs));

            if (tool == Tool.Road)
            {
                bool valid = _roadDemo.Count == 6;
                if (valid) foreach (Vector2Int c in _roadDemo) if (!Free(c) || !Visible(c, s)) { valid = false; break; }
                if (valid) return;
                _roadDemo.Clear();
                for (int r = 0; r <= 14 && _roadDemo.Count == 0; r++)
                    for (int dy = -r; dy <= r && _roadDemo.Count == 0; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                            bool ok = true;
                            for (int i = 0; i < 6 && ok; i++)
                            {
                                var cc = new Vector2Int(mid.x + dx + i - 2, mid.y + dy);
                                ok = Free(cc) && Visible(cc, s);
                            }
                            if (!ok) continue;
                            for (int i = 0; i < 6; i++) _roadDemo.Add(new Vector2Int(mid.x + dx + i - 2, mid.y + dy));
                            break;
                        }

                return;
            }

            if (tool == Tool.Expand) return;

            // Здания: свободные клетки вплотную к дороге, не ближе двух клеток друг к другу.
            _spots.Clear();
            int seen = 0;
            foreach (var kv in _city.RoadMap.Cells)
            {
                if (++seen > 600 || _spots.Count >= 3) break;
                foreach (int side in CellRoads.Side.All)
                {
                    Vector2Int n = kv.Key + CellRoads.Side.Vec(side);
                    if (!Free(n) || !Visible(n, s)) continue;
                    bool near = false;
                    foreach (Vector2Int p in _spots)
                        if (Mathf.Abs(p.x - n.x) + Mathf.Abs(p.y - n.y) < 3) near = true;
                    if (near) continue;
                    _spots.Add(n);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------
        // Рисование

        void OnGUI()
        {
            if (_city == null || !_city.Started) return;
            if (!_visible && _lesson == null) return;
            if (_visible && _steps.Count == 0 && _lesson == null) return;
            EnsureStyles();
            GUI.depth = -1000;
            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s;
            Step st = _lesson ?? _steps[_step];

            string anchor, tag;
            bool hasAnchor = ResolveAnchor(st, out anchor, out tag);
            DrawPanel(st, w, s, hasAnchor ? anchor : null);

            if (hasAnchor) DrawAnchor(anchor, tag, s);
            else if (st.Metric != null && _doneAt < 0f) DrawWorldHint(st, s);
        }

        void DrawPanel(Step st, float w, float s, string anchor)
        {
            float safeTop = (Screen.height - Screen.safeArea.yMax) / s;
            float panelW = Mathf.Min(w - 20f, 400f);
            float textW = panelW - 28f;
            string now = NowText(st, anchor);
            GUIStyle bodyStyle = StyleColor(_body, new Color32(0xE7, 0xEB, 0xEE, 0xFF));
            GUIStyle actionStyle = StyleColor(_action, _doneAt >= 0f ? new Color32(0x8F, 0xE0, 0xCE, 0xFF) : Amber);
            float whyH = bodyStyle.CalcHeight(new GUIContent(st.Why), textW);
            float nowH = actionStyle.CalcHeight(new GUIContent(now), textW);
            float panelH = 66f + whyH + 6f + nowH + 8f + 34f;
            _rect = new Rect((w - panelW) * 0.5f, safeTop + 10f, panelW, panelH);

            Color old = GUI.color;
            GUI.color = new Color32(0x20, 0x29, 0x38, 0xF4);
            GUI.Box(_rect, GUIContent.none, _panel);
            GUI.color = old;
            GUI.DrawTexture(new Rect(_rect.x + 12f, _rect.y + 10f, 44f, 44f), _mayor, ScaleMode.ScaleToFit, true);

            float textX = _rect.x + 66f;
            float tw = _rect.width - 78f;
            GUI.Label(new Rect(textX, _rect.y + 9f, tw, 20f),
                st == _lesson ? "МЭР  ·  НОВЫЙ БИОМ" : "МЭР  ·  ШАГ " + (_step + 1) + " ИЗ " + _steps.Count,
                StyleColor(_small, Amber));
            GUI.Label(new Rect(textX, _rect.y + 27f, tw, 26f), st.Title,
                StyleColor(_title, new Color32(0xF7, 0xF7, 0xF4, 0xFF)));

            if (st.HasSwatch)
            {
                Rect sw = new Rect(_rect.xMax - 38f, _rect.y + 12f, 24f, 24f);
                GUI.color = new Color32(0xF7, 0xF7, 0xF4, 0xFF);
                GUI.DrawTexture(new Rect(sw.x - 2f, sw.y - 2f, sw.width + 4f, sw.height + 4f), Texture2D.whiteTexture);
                GUI.color = st.Swatch;
                GUI.DrawTexture(sw, Texture2D.whiteTexture);
                GUI.color = old;
            }

            float y = _rect.y + 60f;
            GUI.Label(new Rect(_rect.x + 14f, y, textW, whyH), st.Why, bodyStyle);
            y += whyH + 6f;
            GUI.Label(new Rect(_rect.x + 14f, y, textW, nowH), now, actionStyle);

            float by = _rect.yMax - 36f;
            if (st == _lesson)
            {
                DrawButton(new Rect(_rect.xMax - 12f - 110f, by, 110f, 27f), "Понятно", true, DismissLesson);
                return;
            }

            DrawButton(new Rect(_rect.x + 12f, by, 120f, 27f), "Закрыть обучение", false, Finish);
            bool manual = st.Metric == null;
            string label = manual ? (_step == _steps.Count - 1 ? "Готово" : "Дальше") : "Пропустить шаг";
            DrawButton(new Rect(_rect.xMax - 12f - (manual ? 96f : 124f), by, manual ? 96f : 124f, 27f), label, manual, Advance);
        }

        GUIStyle StyleColor(GUIStyle source, Color color)
        {
            CityDebugHud.SetText(source, color);
            return source;
        }

        void DrawButton(Rect r, string text, bool accent, Action action)
        {
            Color bg = accent ? Amber : new Color32(0x3B, 0x45, 0x52, 0xFF);
            Color old = GUI.color;
            GUI.color = bg;
            GUI.Box(r, GUIContent.none, _panel);
            GUI.color = old;
            GUIStyle label = StyleColor(_small, accent ? new Color32(0x20, 0x29, 0x38, 0xFF) : Color.white);
            GUI.Label(r, text, label);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) action();
        }

        float Pulse(float speed)
        {
            return 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);
        }

        /// <summary>Рамка, стрелка и подпись у элемента интерфейса.</summary>
        void DrawAnchor(string key, string tag, float s)
        {
            CityDebugHud hud = _city.Hud;
            Rect a;
            if (hud == null || !hud.Anchors.TryGetValue(key, out a)) return;
            float k = hud.GuiScale / s;
            Rect r = new Rect(a.x * k, a.y * k, a.width * k, a.height * k);

            float t = Pulse(6f);
            Color c = new Color(Amber.r, Amber.g, Amber.b, 0.55f + 0.45f * t);
            Rect o = new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f);
            DrawFrame(o, 3f, c);
            DrawFrame(new Rect(o.x - 3f, o.y - 3f, o.width + 6f, o.height + 6f), 2f,
                new Color(Amber.r, Amber.g, Amber.b, 0.25f * t));

            float w = Screen.width / s;
            bool above = r.y > 150f;
            float bob = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) * 7f;
            Rect arrow = above
                ? new Rect(r.center.x - 12f, r.y - 4f - 22f - bob, 24f, 20f)
                : new Rect(r.center.x - 12f, r.yMax + 6f + bob, 24f, 20f);
            Color old = GUI.color;
            GUI.color = Amber;
            GUI.DrawTexture(arrow, above ? _arrowDown : _arrowUp);
            GUI.color = old;

            DrawTag(tag, new Vector2(r.center.x, above ? arrow.y - 16f : arrow.yMax + 16f), w);
        }

        void DrawFrame(Rect r, float th, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, th), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - th, r.width, th), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, th, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - th, r.y, th, r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        void DrawTag(string text, Vector2 center, float screenW)
        {
            if (string.IsNullOrEmpty(text)) return;
            const float h = 24f;
            float width = Mathf.Max(90f, text.Length * 7.2f + 20f);
            float x = Mathf.Clamp(center.x - width * 0.5f, 6f, screenW - width - 6f);
            var r = new Rect(x, center.y - h * 0.5f, width, h);
            Color old = GUI.color;
            GUI.color = Amber;
            GUI.Box(r, GUIContent.none, _panel);
            GUI.color = old;
            GUI.Label(r, text, StyleColor(_small, new Color32(0x20, 0x29, 0x38, 0xFF)));
        }

        // ---------------------------------------------------------------

        /// <summary>Точка карты в координатах GUI обучения.</summary>
        bool ToGui(Vector2 world, float s, out Vector2 gui)
        {
            gui = Vector2.zero;
            Camera cam = _city.Rig != null ? _city.Rig.Cam : null;
            if (cam == null) return false;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            gui = new Vector2(sp.x, Screen.height - sp.y) / s;
            return true;
        }

        float CellPixels(float s)
        {
            Vector2 a, b;
            Vector2 c = CenterOf(new Vector2Int(0, 0));
            if (!ToGui(c, s, out a) || !ToGui(c + new Vector2(_city.CellSize, 0f), s, out b)) return 30f;
            return Mathf.Max(14f, Mathf.Abs(b.x - a.x));
        }

        void DrawRing(Vector2 gui, float size, Color col)
        {
            Color old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(gui.x - size * 0.5f, gui.y - size * 0.5f, size, size), _ring);
            GUI.color = old;
        }

        void DrawDot(Vector2 gui, float size, Color col)
        {
            Color old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(gui.x - size * 0.5f, gui.y - size * 0.5f, size, size), _dot);
            GUI.color = old;
        }

        void DrawWorldHint(Step st, float s)
        {
            float cell = CellPixels(s);
            float t = Pulse(6f);
            Color amber = new Color(Amber.r, Amber.g, Amber.b, 0.6f + 0.4f * t);
            float screenW = Screen.width / s;

            if (st.Tool == Tool.Road)
            {
                RefreshSpots(Tool.Road, s);
                if (_roadDemo.Count < 2) return;
                for (int i = 0; i < _roadDemo.Count; i++)
                {
                    Vector2 g;
                    if (!ToGui(CenterOf(_roadDemo[i]), s, out g)) continue;
                    DrawDot(g, cell * 0.22f, new Color(Amber.r, Amber.g, Amber.b, 0.55f));
                }

                Vector2 first, last;
                if (!ToGui(CenterOf(_roadDemo[0]), s, out first) || !ToGui(CenterOf(_roadDemo[_roadDemo.Count - 1]), s, out last)) return;
                DrawRing(first, cell * (1.0f + 0.15f * t), amber);
                // «Палец»: бегущая точка показывает направление протяжки.
                float f = (Time.unscaledTime * 0.7f) % 1f;
                Vector2 finger = Vector2.Lerp(first, last, f);
                DrawDot(finger, cell * 0.5f, new Color(1f, 1f, 1f, 0.9f));
                DrawDot(finger, cell * 0.34f, Amber);
                DrawTag("Веди отсюда", new Vector2(first.x, first.y + cell * 0.95f), screenW);
                return;
            }

            if (st.Tool == Tool.Expand)
            {
                Camera cam = _city.Rig != null ? _city.Rig.Cam : null;
                if (cam == null) return;
                Vector3 cp = cam.transform.position;
                float best = float.MaxValue;
                Vector2 pick = Vector2.zero;
                bool any = false;
                foreach (HexCoord h in _city.Sectors.Frontier)
                {
                    Vector2 c = HexMath.ToWorld(h, _city.HexSize);
                    float d = (c - new Vector2(cp.x, cp.y)).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    pick = c;
                    any = true;
                }

                Vector2 g;
                if (!any || !ToGui(pick, s, out g)) return;
                DrawRing(g, cell * (3.2f + 0.4f * t), amber);
                DrawTag("Тапни здесь", new Vector2(g.x, g.y + cell * 2.2f), screenW);
                return;
            }

            RefreshSpots(st.Tool, s);
            foreach (Vector2Int p in _spots)
            {
                Vector2 g;
                if (!ToGui(CenterOf(p), s, out g)) continue;
                DrawRing(g, cell * (1.15f + 0.2f * t), amber);
                DrawDot(g, cell * 0.2f, Amber);
            }

            if (_spots.Count > 0)
            {
                Vector2 g;
                if (ToGui(CenterOf(_spots[0]), s, out g)) DrawTag("Тапни сюда", new Vector2(g.x, g.y + cell * 1.1f), screenW);
            }
        }
    }
}
