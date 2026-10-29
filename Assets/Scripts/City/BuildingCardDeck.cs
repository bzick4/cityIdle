using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace CityGame.City
{
    using Buildings;

    public sealed class BuildingCardDeck : MonoBehaviour
    {
        public float CellSize = 1f;

        // ──────────────────────────────────────────────────────────
        //  События
        // ──────────────────────────────────────────────────────────

        public event System.Action<Tool> ToolRequested;
        public ServiceKind LastSelectedService { get; private set; } = ServiceKind.Ambulance;

        // ──────────────────────────────────────────────────────────
        //  Зависимости / состояние
        // ──────────────────────────────────────────────────────────

        BuildingPlacer _placer;
        Economy        _economy;
        CityDebugHud   _hud;
        Canvas         _canvas;
        RectTransform  _deckRoot;

        enum DeckState { Hidden, Categories, SecondLevel }
        DeckState _state = DeckState.Hidden;
        public bool IsVisible => _state != DeckState.Hidden;

        public bool ContainsScreenPoint(Vector2 screen)
        {
            if (_state == DeckState.Hidden || _deckRoot == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_deckRoot, screen, null);
        }

        readonly List<CardSlot> _active = new List<CardSlot>();

        // Кэш текстур иконок — генерируются один раз
        static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        static Texture2D TexTool(Tool t)
        {
            string k = "T:" + t;
            if (!_texCache.TryGetValue(k, out var tex)) { tex = ToolIcons.Make(t); _texCache[k] = tex; }
            return tex;
        }
        static Texture2D TexService(ServiceKind k)
        {
            string key = "S:" + k;
            if (!_texCache.TryGetValue(key, out var tex)) { tex = ToolIcons.MakeService(k); _texCache[key] = tex; }
            return tex;
        }

        // ──────────────────────────────────────────────────────────
        //  Tap state
        // ──────────────────────────────────────────────────────────

        CardSlot _held;
        Vector2  _pressPos;
        const float TapMovePx = 20f;

        // ──────────────────────────────────────────────────────────
        //  Размеры и анимация
        // ──────────────────────────────────────────────────────────

        const float CardW     = 68f;
        const float CardH     = 96f;
        const float MaxStep   = 56f;
        const float MaxRotDeg = 7f;
        const float ArcDrop   = 9f;
        const float DeckYBase = 6f;
        const float LiftY     = 26f;
        const float AnimIn    = 0.26f;
        const float AnimOut   = 0.15f;
        const float Stagger   = 0.04f;

        // ──────────────────────────────────────────────────────────
        //  Цвета фона арт-панели
        // ──────────────────────────────────────────────────────────

        static readonly Color32 ART_BG    = new Color32(0xF4, 0xF0, 0xE8, 0xFF); // нейтральный под иконки
        static readonly Color32 ART_ROADS = new Color32(0xD8, 0xDD, 0xE5, 0xFF);
        static readonly Color32 ART_DIST  = new Color32(0xD4, 0xE5, 0xF4, 0xFF);
        static readonly Color32 ART_SERV  = new Color32(0xF5, 0xD4, 0xD2, 0xFF);
        static readonly Color32 ART_ENRG  = new Color32(0xF8, 0xEC, 0xCE, 0xFF);
        static readonly Color32 ART_BACK  = new Color32(0xE4, 0xE0, 0xDA, 0xFF);
        static readonly Color32 ART_ERASE = new Color32(0xF0, 0xD4, 0xD4, 0xFF);

        // ──────────────────────────────────────────────────────────
        //  Определения карточек
        // ──────────────────────────────────────────────────────────

        enum CatAction { ShowRoads, ShowDistricts, ShowServices, ShowEnergy, ShowInfra, Erase }

        struct CatDef  { public string Title, Sub; public Color32 ArtBg; public Texture2D Icon; public CatAction Action; }
        struct ToolDef { public string Title, Sub; public Color32 ArtBg; public Texture2D Icon; public Tool     Tool;    }
        struct BldDef  { public string Title, Sub; public Color32 ArtBg; public Texture2D Icon;
                         public BuildingCategory Category; public ServiceKind Kind; public Tool PlaceTool; }

        CatDef[]  _catDefs;
        ToolDef[] _roadDefs, _infraDefs;
        BldDef[]  _distDefs, _servDefs, _enrgDefs;

        void InitDefs()
        {
            _catDefs = new[]
            {
                new CatDef { Title="ДОРОГИ",  Sub="трассы",  ArtBg=ART_ROADS, Icon=TexTool(Tool.Road),        Action=CatAction.ShowRoads     },
                new CatDef { Title="РАЙОНЫ",  Sub="здания",  ArtBg=ART_DIST,  Icon=TexTool(Tool.Residential), Action=CatAction.ShowDistricts },
                new CatDef { Title="СЛУЖБЫ",  Sub="сервисы", ArtBg=ART_SERV,  Icon=TexTool(Tool.Service),     Action=CatAction.ShowServices  },
                new CatDef { Title="ЭНЕРГИЯ", Sub="питание", ArtBg=ART_ENRG,  Icon=TexService(ServiceKind.PowerPlant), Action=CatAction.ShowEnergy },
                new CatDef { Title="ИНФРА",   Sub="сектора", ArtBg=ART_DIST,  Icon=TexTool(Tool.Expand),      Action=CatAction.ShowInfra     },
                new CatDef { Title="СНЕСТИ",  Sub="убрать",  ArtBg=ART_ERASE, Icon=TexTool(Tool.Erase),       Action=CatAction.Erase         },
            };
            _roadDefs = new[]
            {
                new ToolDef { Title="Дорога",  Sub="улица",  ArtBg=ART_ROADS, Icon=TexTool(Tool.Road),    Tool=Tool.Road     },
                new ToolDef { Title="Шоссе",   Sub="быстро", ArtBg=ART_ROADS, Icon=TexTool(Tool.Highway), Tool=Tool.Highway  },
                new ToolDef { Title="Одност.", Sub="полоса", ArtBg=ART_ROADS, Icon=TexTool(Tool.OneWay),  Tool=Tool.OneWay   },
                new ToolDef { Title="Развилка",Sub="узел",   ArtBg=ART_ROADS, Icon=TexTool(Tool.Junction),Tool=Tool.Junction },
                new ToolDef { Title="Канал",   Sub="вода",   ArtBg=ART_BG,    Icon=TexTool(Tool.Canal),   Tool=Tool.Canal    },
                new ToolDef { Title="Съезд",   Sub="рампа",  ArtBg=ART_ROADS, Icon=TexTool(Tool.Ramp),    Tool=Tool.Ramp     },
            };
            _distDefs = new[]
            {
                new BldDef { Title="Жильё",   Sub="жилой",  ArtBg=ART_DIST,  Icon=TexTool(Tool.Residential),
                             Category=BuildingCategory.Residential, PlaceTool=Tool.Residential },
                new BldDef { Title="Торговля", Sub="коммер.",ArtBg=ART_DIST,  Icon=TexTool(Tool.Commercial),
                             Category=BuildingCategory.Commercial,  PlaceTool=Tool.Commercial  },
                new BldDef { Title="Произв.",  Sub="завод",  ArtBg=ART_DIST,  Icon=TexTool(Tool.Industrial),
                             Category=BuildingCategory.Industrial,  PlaceTool=Tool.Industrial  },
            };
            _servDefs = new[]
            {
                new BldDef { Title="Скорая",   Sub="здоровье", ArtBg=ART_SERV,
                             Icon=TexService(ServiceKind.Ambulance),
                             Category=BuildingCategory.Service, Kind=ServiceKind.Ambulance,  PlaceTool=Tool.Service },
                new BldDef { Title="Полиция",  Sub="охрана",   ArtBg=ART_SERV,
                             Icon=TexService(ServiceKind.Police),
                             Category=BuildingCategory.Service, Kind=ServiceKind.Police,     PlaceTool=Tool.Service },
                new BldDef { Title="Пожарные", Sub="тушение",  ArtBg=ART_SERV,
                             Icon=TexService(ServiceKind.Fire),
                             Category=BuildingCategory.Service, Kind=ServiceKind.Fire,       PlaceTool=Tool.Service },
            };
            _enrgDefs = new[]
            {
                new BldDef { Title="Электро",   Sub="ток",  ArtBg=ART_ENRG,
                             Icon=TexService(ServiceKind.PowerPlant),
                             Category=BuildingCategory.Service, Kind=ServiceKind.PowerPlant, PlaceTool=Tool.Service },
                new BldDef { Title="Водобашня", Sub="вода", ArtBg=ART_ENRG,
                             Icon=TexService(ServiceKind.WaterTower),
                             Category=BuildingCategory.Service, Kind=ServiceKind.WaterTower, PlaceTool=Tool.Service },
            };
            _infraDefs = new[]
            {
                new ToolDef { Title="Расшир.", Sub="сектор", ArtBg=ART_DIST,  Icon=TexTool(Tool.Expand), Tool=Tool.Expand },
                new ToolDef { Title="Порт",    Sub="море",   ArtBg=ART_DIST,  Icon=TexTool(Tool.Port),   Tool=Tool.Port   },
                new ToolDef { Title="Автобус", Sub="маршр.", ArtBg=ART_ENRG,  Icon=TexTool(Tool.Bus),    Tool=Tool.Bus    },
            };
        }

        // ──────────────────────────────────────────────────────────
        //  CardSlot
        // ──────────────────────────────────────────────────────────

        class CardSlot
        {
            public RectTransform Rt;
            public float BaseX, BaseY, BaseRot;
            public bool  Lifted;
            public bool  IsBackCard;
            public bool  IsBldCard;
            public int   CatIndex = -1;
            public ToolDef ToolDef;
            public BldDef  BldDef;
            public DG.Tweening.Tween Tween;

            public void AnimateTo(float x, float y, float rot, float dur, float delay = 0f)
            {
                Tween?.Kill();
                var seq = DOTween.Sequence();
                if (delay > 0f) seq.AppendInterval(delay);
                seq.Append(Rt.DOAnchorPos(new Vector2(x, y), dur).SetEase(Ease.OutBack))
                   .Join(Rt.DOLocalRotate(new Vector3(0, 0, rot), dur).SetEase(Ease.OutBack));
                Tween = seq.Play();
            }
        }

        // ──────────────────────────────────────────────────────────
        //  Init
        // ──────────────────────────────────────────────────────────

        public void Bind(BuildingPlacer placer, Economy economy, CityDebugHud hud = null)
        {
            _placer  = placer;
            _economy = economy;
            _hud     = hud;
            InitDefs();
            BuildCanvas();
        }

        void BuildCanvas()
        {
            var cgo = new GameObject("CardDeck_Canvas");
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var sc = cgo.AddComponent<CanvasScaler>();
            sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(390f, 844f);
            sc.matchWidthOrHeight  = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();

            var dgo = new GameObject("DeckRoot");
            dgo.transform.SetParent(cgo.transform, false);
            _deckRoot = dgo.AddComponent<RectTransform>();
            _deckRoot.anchorMin = new Vector2(0.5f, 0f);
            _deckRoot.anchorMax = new Vector2(0.5f, 0f);
            _deckRoot.pivot     = new Vector2(0.5f, 0f);
            _deckRoot.sizeDelta = new Vector2(420f, CardH + DeckYBase + ArcDrop + 30f);
            _canvas.gameObject.SetActive(false);
        }

        // ──────────────────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────────────────

        public void ToggleDeck()
        {
            if (_state == DeckState.Hidden) ShowCategories();
            else HideAll();
        }

        public void HideAll()
        {
            if (_state == DeckState.Hidden) return;
            _state = DeckState.Hidden;
            _held  = null;
            AnimateOutAndDestroy(_active);
        }

        // ──────────────────────────────────────────────────────────
        //  Показ карточек
        // ──────────────────────────────────────────────────────────

        void ShowCategories()
        {
            ClearActive();
            _state = DeckState.Categories;
            _canvas.gameObject.SetActive(true);
            UpdateDeckY();

            SpawnFan(_catDefs.Length, i =>
            {
                var d = _catDefs[i];
                var s = new CardSlot { CatIndex = i };
                s.Rt = CreateCard(d.Title, d.Sub, d.ArtBg, d.Icon);
                return s;
            });
        }

        void ShowTools(ToolDef[] defs)
        {
            ClearActive();
            _state = DeckState.SecondLevel;
            _canvas.gameObject.SetActive(true);
            UpdateDeckY();

            SpawnFan(defs.Length + 1, i =>
            {
                if (i == 0)
                {
                    var back = new CardSlot { IsBackCard = true };
                    back.Rt = CreateCard("<", "назад", ART_BACK, null);
                    return back;
                }
                var d = defs[i - 1];
                var s = new CardSlot { ToolDef = d };
                s.Rt = CreateCard(d.Title, d.Sub, d.ArtBg, d.Icon);
                return s;
            });
        }

        void ShowBuildings(BldDef[] defs)
        {
            ClearActive();
            _state = DeckState.SecondLevel;
            _canvas.gameObject.SetActive(true);
            UpdateDeckY();

            SpawnFan(defs.Length + 1, i =>
            {
                if (i == 0)
                {
                    var back = new CardSlot { IsBackCard = true };
                    back.Rt = CreateCard("<", "назад", ART_BACK, null);
                    return back;
                }
                var d = defs[i - 1];
                string sub = _economy != null ? _economy.CostOf(d.Category, d.Kind).ToString("0") + "B" : d.Sub;
                var s = new CardSlot { IsBldCard = true, BldDef = d };
                s.Rt = CreateCard(d.Title, sub, d.ArtBg, d.Icon);
                return s;
            });
        }

        void SpawnFan(int n, System.Func<int, CardSlot> factory)
        {
            float step   = CalcStep(n);
            float totalW = (n - 1) * step + CardW;
            float startX = -totalW * 0.5f + CardW * 0.5f;
            float halfN  = (n - 1) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t     = halfN > 0 ? (i - halfN) / halfN : 0f;
                float baseX = startX + i * step;
                float baseY = DeckYBase - ArcDrop * (t * t);
                float baseR = -MaxRotDeg * t;
                var slot = factory(i);
                slot.BaseX = baseX; slot.BaseY = baseY; slot.BaseRot = baseR;
                slot.Rt.anchoredPosition = new Vector2(baseX, -CardH - 50f);
                slot.Rt.localEulerAngles = Vector3.zero;
                _active.Add(slot);
                slot.AnimateTo(baseX, baseY, baseR, AnimIn, i * Stagger);
            }
        }

        static float CalcStep(int n) => n <= 1 ? 0f : Mathf.Min(MaxStep, 320f / (n - 1));

        // ──────────────────────────────────────────────────────────
        //  Построение одной карточки
        // ──────────────────────────────────────────────────────────

        RectTransform CreateCard(string title, string sub, Color32 artBg, Texture2D iconTex)
        {
            var go = new GameObject("Card_" + title);
            go.transform.SetParent(_deckRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0f);
            rt.anchorMax        = new Vector2(0.5f, 0f);
            rt.pivot            = new Vector2(0.5f, 0f);
            rt.sizeDelta        = new Vector2(CardW, CardH);
            rt.anchoredPosition = new Vector2(0, -CardH - 50f);
            rt.localEulerAngles = Vector3.zero;

            // Тень
            var sh = new GameObject("Sh"); sh.transform.SetParent(go.transform, false);
            var shRt = sh.AddComponent<RectTransform>();
            shRt.anchorMin=Vector2.zero; shRt.anchorMax=Vector2.one;
            shRt.offsetMin=new Vector2(3,-4); shRt.offsetMax=new Vector2(4,-3);
            sh.AddComponent<Image>().color = new Color(0,0,0,0.18f);

            // Рамка
            var fr = new GameObject("Fr"); fr.transform.SetParent(go.transform, false);
            var frRt = fr.AddComponent<RectTransform>();
            frRt.anchorMin=Vector2.zero; frRt.anchorMax=Vector2.one;
            frRt.offsetMin=Vector2.zero; frRt.offsetMax=Vector2.zero;
            fr.AddComponent<Image>().color = new Color32(0x4A,0x40,0x34,0xFF);

            // Фон карты
            var bg = new GameObject("Bg"); bg.transform.SetParent(go.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin=Vector2.zero; bgRt.anchorMax=Vector2.one;
            bgRt.offsetMin=new Vector2(2,2); bgRt.offsetMax=new Vector2(-2,-2);
            bg.AddComponent<Image>().color = new Color32(0xF0,0xEB,0xE2,0xFF);

            // Арт-панель (верхние ~64%)
            var art = new GameObject("Art"); art.transform.SetParent(go.transform, false);
            var artRt = art.AddComponent<RectTransform>();
            artRt.anchorMin=new Vector2(0f,0.34f); artRt.anchorMax=Vector2.one;
            artRt.offsetMin=new Vector2(2,0); artRt.offsetMax=new Vector2(-2,-2);
            art.AddComponent<Image>().color = artBg;

            // Иконка-текстура (квадратная, вписанная в арт-панель)
            if (iconTex != null)
            {
                var ic = new GameObject("Ico"); ic.transform.SetParent(art.transform, false);
                var icRt = ic.AddComponent<RectTransform>();
                // Центрируем и оставляем небольшой отступ
                icRt.anchorMin=new Vector2(0.5f,0.5f); icRt.anchorMax=new Vector2(0.5f,0.5f);
                icRt.pivot=new Vector2(0.5f,0.5f);
                icRt.anchoredPosition=new Vector2(0, 4f); // чуть выше центра арт-панели
                icRt.sizeDelta=new Vector2(CardW * 0.72f, CardW * 0.72f); // квадрат
                var raw = ic.AddComponent<RawImage>();
                raw.texture = iconTex;
                raw.raycastTarget = false;
            }

            // Подпись в нижней полосе арта
            var tn = new GameObject("Tn"); tn.transform.SetParent(art.transform, false);
            var tnRt = tn.AddComponent<RectTransform>();
            tnRt.anchorMin=new Vector2(0,0); tnRt.anchorMax=new Vector2(1,0.30f);
            tnRt.offsetMin=new Vector2(1,0); tnRt.offsetMax=new Vector2(-1,0);
            var tnTxt = tn.AddComponent<Text>();
            tnTxt.text=title; tnTxt.fontSize=9; tnTxt.fontStyle=FontStyle.Bold;
            tnTxt.color=new Color32(0x2A,0x22,0x16,0xFF);
            tnTxt.alignment=TextAnchor.LowerCenter; tnTxt.raycastTarget=false;
            tnTxt.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Нижняя полоса с суб-текстом
            var bot = new GameObject("Bot"); bot.transform.SetParent(go.transform, false);
            var botRt = bot.AddComponent<RectTransform>();
            botRt.anchorMin=new Vector2(0,0); botRt.anchorMax=new Vector2(1,0.34f);
            botRt.offsetMin=new Vector2(2,2); botRt.offsetMax=new Vector2(-2,0);
            bot.AddComponent<Image>().color = new Color32(0xE0,0xD9,0xCF,0xFF);

            var su = new GameObject("Su"); su.transform.SetParent(bot.transform, false);
            var suRt = su.AddComponent<RectTransform>();
            suRt.anchorMin=Vector2.zero; suRt.anchorMax=Vector2.one;
            suRt.offsetMin=new Vector2(1,0); suRt.offsetMax=new Vector2(-1,0);
            var suTxt = su.AddComponent<Text>();
            suTxt.text=sub; suTxt.fontSize=8; suTxt.color=new Color32(0x4A,0x3C,0x2C,0xFF);
            suTxt.alignment=TextAnchor.MiddleCenter; suTxt.raycastTarget=false;
            suTxt.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return rt;
        }

        // ──────────────────────────────────────────────────────────
        //  Анимации
        // ──────────────────────────────────────────────────────────

        void AnimateOutAndDestroy(List<CardSlot> slots)
        {
            var copy = new List<CardSlot>(slots);
            slots.Clear();
            for (int i = 0; i < copy.Count; i++)
            {
                var s = copy[i];
                s.Tween?.Kill();
                if (s.Rt == null) continue;
                var rt = s.Rt;
                float delay = (copy.Count - 1 - i) * Stagger * 0.5f;
                var seq = DOTween.Sequence();
                if (delay > 0f) seq.AppendInterval(delay);
                seq.Append(rt.DOAnchorPosY(-CardH - 60f, AnimOut).SetEase(Ease.InCubic))
                   .OnComplete(() => { if (rt != null) Destroy(rt.gameObject); });
                s.Tween = seq.Play();
            }
            float hideDelay = AnimOut + copy.Count * Stagger * 0.5f + 0.05f;
            DOVirtual.DelayedCall(hideDelay, () =>
            {
                if (_state == DeckState.Hidden && _canvas != null)
                    _canvas.gameObject.SetActive(false);
            });
        }

        void ClearActive()
        {
            _held = null;
            foreach (var s in _active) { s.Tween?.Kill(); if (s.Rt != null) Destroy(s.Rt.gameObject); }
            _active.Clear();
        }

        // ──────────────────────────────────────────────────────────
        //  Update
        // ──────────────────────────────────────────────────────────

        void Update()
        {
            if (_state == DeckState.Hidden || _canvas == null || !_canvas.gameObject.activeSelf) return;
            UpdateDeckY();
            HandleInput();
        }

        void UpdateDeckY()
        {
            if (_deckRoot == null || _canvas == null) return;
            float px = _hud != null ? _hud.BottomPanelTopPx : 0f;
            float cs = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _deckRoot.anchoredPosition = new Vector2(0f, px / cs + 8f);
        }

        // ──────────────────────────────────────────────────────────
        //  Ввод
        // ──────────────────────────────────────────────────────────

        void HandleInput()
        {
            Vector2 pos  = GetPointerScreen();
            bool    down = GetPointerDown();
            bool    held = GetPointerHeld();
            bool    up   = GetPointerUp();

            // Hover-подъём
            if (_held == null)
            {
                foreach (var s in _active)
                {
                    bool near = IsNearCard(pos, s);
                    if (near  && !s.Lifted) { s.Lifted=true;  s.AnimateTo(s.BaseX, s.BaseY+LiftY, s.BaseRot*0.4f, 0.12f); }
                    if (!near && s.Lifted)  { s.Lifted=false; s.AnimateTo(s.BaseX, s.BaseY, s.BaseRot, 0.12f); }
                }
            }

            // Нажатие
            if (down && _held == null)
            {
                foreach (var s in _active)
                {
                    if (!IsOnCard(pos, s)) continue;
                    _held = s; _pressPos = pos;
                    s.AnimateTo(s.BaseX, s.BaseY + LiftY * 1.4f, 0f, 0.07f);
                    break;
                }
            }

            // Отпускание
            if (up && _held != null)
            {
                var slot = _held; _held = null;
                bool isTap = Vector2.Distance(pos, _pressPos) < TapMovePx;

                // Возврат на место — только если колода ещё не скрыта
                if (_state != DeckState.Hidden && slot.Rt != null && slot.Rt.gameObject != null)
                {
                    slot.Tween?.Kill();
                    slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, slot.BaseY), 0.20f).SetEase(Ease.OutBack);
                    slot.Rt.DOLocalRotate(new Vector3(0, 0, slot.BaseRot), 0.20f).SetEase(Ease.OutBack);
                    slot.Lifted = false;
                }

                if (isTap) OnCardTapped(slot);
            }

            // Потеря касания
            if (!down && !held && _held != null)
            {
                var slot = _held; _held = null;
                if (slot.Rt != null && slot.Rt.gameObject != null)
                {
                    slot.Tween?.Kill();
                    slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, slot.BaseY), 0.16f).SetEase(Ease.OutCubic);
                    slot.Rt.DOLocalRotate(new Vector3(0, 0, slot.BaseRot), 0.16f).SetEase(Ease.OutCubic);
                    slot.Lifted = false;
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        //  Тап
        // ──────────────────────────────────────────────────────────

        void OnCardTapped(CardSlot slot)
        {
            if (slot.IsBackCard) { ShowCategories(); return; }

            if (_state == DeckState.Categories)
            {
                switch (_catDefs[slot.CatIndex].Action)
                {
                    case CatAction.ShowRoads:      ShowTools(_roadDefs);    break;
                    case CatAction.ShowDistricts:  ShowBuildings(_distDefs); break;
                    case CatAction.ShowServices:   ShowBuildings(_servDefs); break;
                    case CatAction.ShowEnergy:     ShowBuildings(_enrgDefs); break;
                    case CatAction.ShowInfra:      ShowTools(_infraDefs);    break;
                    case CatAction.Erase:
                        HideAll(); ToolRequested?.Invoke(Tool.Erase); break;
                }
                return;
            }

            // SecondLevel
            if (slot.IsBldCard)
            {
                if (slot.BldDef.Category == BuildingCategory.Service)
                    LastSelectedService = slot.BldDef.Kind;
                HideAll();
                ToolRequested?.Invoke(slot.BldDef.PlaceTool);
            }
            else
            {
                HideAll();
                ToolRequested?.Invoke(slot.ToolDef.Tool);
            }
        }

        // ──────────────────────────────────────────────────────────
        //  Hit-testing
        // ──────────────────────────────────────────────────────────

        bool IsOnCard(Vector2 screen, CardSlot slot)
        {
            if (slot.Rt == null) return false;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local)) return false;
            float cx = slot.Rt.anchoredPosition.x;
            float cy = slot.Rt.anchoredPosition.y + CardH * 0.5f;
            return Mathf.Abs(local.x - cx) < CardW * 0.55f && Mathf.Abs(local.y - cy) < CardH * 0.55f;
        }

        bool IsNearCard(Vector2 screen, CardSlot slot)
        {
            if (slot.Rt == null) return false;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local)) return false;
            float cx = slot.Rt.anchoredPosition.x;
            float cy = slot.Rt.anchoredPosition.y;
            return Mathf.Abs(local.x - cx) < CardW * 0.5f + 22f
                && local.y > cy - 12f && local.y < cy + CardH + 36f;
        }

        // ──────────────────────────────────────────────────────────
        //  Pointer helpers
        // ──────────────────────────────────────────────────────────

        static Vector2 GetPointerScreen()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).position;
            return Input.mousePosition;
        }
        static bool GetPointerDown()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).phase == TouchPhase.Began;
            return Input.GetMouseButtonDown(0);
        }
        static bool GetPointerHeld()
        {
            if (Input.touchCount > 0)
            {
                var p = Input.GetTouch(0).phase;
                return p == TouchPhase.Stationary || p == TouchPhase.Moved;
            }
            return Input.GetMouseButton(0);
        }
        static bool GetPointerUp()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).phase == TouchPhase.Ended;
            return Input.GetMouseButtonUp(0);
        }
    }
}
