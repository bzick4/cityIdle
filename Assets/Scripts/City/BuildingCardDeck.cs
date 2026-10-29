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

        /// <summary>Инструмент выбран тапом по карточке.</summary>
        public event System.Action<Tool> ToolRequested;

        /// <summary>Последний выбранный тип службы (читает CitySystem перед ToolRequested(Tool.Service)).</summary>
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
        //  Игровая палитра
        // ──────────────────────────────────────────────────────────

        static readonly Color32 CAT_ROADS  = new Color32(0x3B, 0x45, 0x52, 0xFF);
        static readonly Color32 CAT_DIST   = new Color32(0x6F, 0xA8, 0xDC, 0xFF);
        static readonly Color32 CAT_SERV   = new Color32(0xD9, 0x4F, 0x45, 0xFF);
        static readonly Color32 CAT_ENERGY = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        static readonly Color32 CAT_INFRA  = new Color32(0x7A, 0xB8, 0x62, 0xFF);
        static readonly Color32 CAT_ERASE  = new Color32(0x9A, 0xA0, 0xA6, 0xFF);

        static readonly Color32 ROAD_ROAD  = new Color32(0x3B, 0x45, 0x52, 0xFF);
        static readonly Color32 ROAD_HWY   = new Color32(0x28, 0x38, 0x52, 0xFF);
        static readonly Color32 ROAD_OW    = new Color32(0x42, 0x55, 0x68, 0xFF);
        static readonly Color32 ROAD_JCT   = new Color32(0x48, 0x40, 0x68, 0xFF);
        static readonly Color32 ROAD_CANAL = new Color32(0x4F, 0xA3, 0xE0, 0xFF);
        static readonly Color32 ROAD_RAMP  = new Color32(0xC8, 0x7A, 0x38, 0xFF);

        static readonly Color32 INFRA_EXP  = new Color32(0xCD, 0xE5, 0xB0, 0xFF);
        static readonly Color32 INFRA_PORT = new Color32(0xA9, 0xD6, 0xF2, 0xFF);
        static readonly Color32 INFRA_BUS  = new Color32(0xF2, 0xC1, 0x4E, 0xFF);

        static readonly Color32 BLD_RES  = new Color32(0x6F, 0xA8, 0xDC, 0xFF);
        static readonly Color32 BLD_COM  = new Color32(0xE8, 0x83, 0x8B, 0xFF);
        static readonly Color32 BLD_IND  = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        static readonly Color32 SVC_AMB  = new Color32(0xD9, 0x4F, 0x45, 0xFF);
        static readonly Color32 SVC_POL  = new Color32(0x3D, 0x5A, 0xA8, 0xFF);
        static readonly Color32 SVC_FIRE = new Color32(0xC8, 0x3C, 0x3C, 0xFF);
        static readonly Color32 SVC_PWR  = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        static readonly Color32 SVC_WAT  = new Color32(0x4F, 0xA3, 0xE0, 0xFF);

        // ──────────────────────────────────────────────────────────
        //  Определения карточек
        // ──────────────────────────────────────────────────────────

        enum CatAction { ShowRoads, ShowDistricts, ShowServices, ShowEnergy, ShowInfra, Erase }

        struct CatDef  { public string Title, Sub, Icon; public Color32 Art; public CatAction Action; }
        struct ToolDef { public string Title, Sub, Icon; public Color32 Art; public Tool     Tool;    }
        struct BldDef  { public string Title, Sub, Icon; public Color32 Art;
                         public BuildingCategory Category; public ServiceKind Kind; public Tool PlaceTool; }

        static readonly CatDef[] CatDefs =
        {
            new CatDef { Title="ДОРОГИ",  Sub="трассы",  Icon="--",Art=CAT_ROADS,  Action=CatAction.ShowRoads     },
            new CatDef { Title="РАЙОНЫ",  Sub="здания",  Icon="H", Art=CAT_DIST,   Action=CatAction.ShowDistricts },
            new CatDef { Title="СЛУЖБЫ",  Sub="сервисы", Icon="+", Art=CAT_SERV,   Action=CatAction.ShowServices  },
            new CatDef { Title="ЭНЕРГИЯ", Sub="питание", Icon="#", Art=CAT_ENERGY, Action=CatAction.ShowEnergy    },
            new CatDef { Title="ИНФРА",   Sub="сектора", Icon="<>",Art=CAT_INFRA,  Action=CatAction.ShowInfra     },
            new CatDef { Title="СНЕСТИ",  Sub="убрать",  Icon="X", Art=CAT_ERASE,  Action=CatAction.Erase         },
        };

        static readonly ToolDef[] RoadDefs =
        {
            new ToolDef { Title="Дорога",  Sub="улица",  Icon="__", Art=ROAD_ROAD,  Tool=Tool.Road     },
            new ToolDef { Title="Шоссе",   Sub="быстро", Icon="==", Art=ROAD_HWY,   Tool=Tool.Highway  },
            new ToolDef { Title="Одност.", Sub="полоса", Icon="->", Art=ROAD_OW,    Tool=Tool.OneWay   },
            new ToolDef { Title="Развилка",Sub="узел",   Icon="Y",  Art=ROAD_JCT,   Tool=Tool.Junction },
            new ToolDef { Title="Канал",   Sub="вода",   Icon="~~", Art=ROAD_CANAL, Tool=Tool.Canal    },
            new ToolDef { Title="Съезд",   Sub="рампа",  Icon="/\\",Art=ROAD_RAMP,  Tool=Tool.Ramp     },
        };

        static readonly ToolDef[] InfraDefs =
        {
            new ToolDef { Title="Расшир.", Sub="сектор", Icon="[]", Art=INFRA_EXP,  Tool=Tool.Expand },
            new ToolDef { Title="Порт",    Sub="море",   Icon="|>", Art=INFRA_PORT, Tool=Tool.Port   },
            new ToolDef { Title="Автобус", Sub="маршр.", Icon="()", Art=INFRA_BUS,  Tool=Tool.Bus    },
        };

        static readonly BldDef[] DistrictDefs =
        {
            new BldDef { Title="Жильё",   Sub="жилой",  Icon="H", Art=BLD_RES,
                         Category=BuildingCategory.Residential, PlaceTool=Tool.Residential },
            new BldDef { Title="Торговля", Sub="коммер.",Icon="$", Art=BLD_COM,
                         Category=BuildingCategory.Commercial,  PlaceTool=Tool.Commercial  },
            new BldDef { Title="Произв.",  Sub="завод",  Icon="F", Art=BLD_IND,
                         Category=BuildingCategory.Industrial,  PlaceTool=Tool.Industrial  },
        };

        static readonly BldDef[] ServiceDefs =
        {
            new BldDef { Title="Скорая",   Sub="здоровье", Icon="+", Art=SVC_AMB,
                         Category=BuildingCategory.Service, Kind=ServiceKind.Ambulance,  PlaceTool=Tool.Service },
            new BldDef { Title="Полиция",  Sub="охрана",   Icon="*", Art=SVC_POL,
                         Category=BuildingCategory.Service, Kind=ServiceKind.Police,     PlaceTool=Tool.Service },
            new BldDef { Title="Пожарные", Sub="тушение",  Icon="!", Art=SVC_FIRE,
                         Category=BuildingCategory.Service, Kind=ServiceKind.Fire,       PlaceTool=Tool.Service },
        };

        static readonly BldDef[] EnergyDefs =
        {
            new BldDef { Title="Электро",   Sub="ток",  Icon="#", Art=SVC_PWR,
                         Category=BuildingCategory.Service, Kind=ServiceKind.PowerPlant, PlaceTool=Tool.Service },
            new BldDef { Title="Водобашня", Sub="вода", Icon="~", Art=SVC_WAT,
                         Category=BuildingCategory.Service, Kind=ServiceKind.WaterTower, PlaceTool=Tool.Service },
        };

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

            int n = CatDefs.Length;
            SpawnFan(n, i =>
            {
                var d = CatDefs[i];
                var s = new CardSlot { CatIndex=i };
                s.Rt = CreateCard(d.Title, d.Sub, d.Icon, d.Art, 0, -CardH - 50f, 0);
                return s;
            });
        }

        void ShowTools(ToolDef[] defs)
        {
            ClearActive();
            _state = DeckState.SecondLevel;
            _canvas.gameObject.SetActive(true);
            UpdateDeckY();

            int total = defs.Length + 1;
            SpawnFan(total, i =>
            {
                if (i == 0)
                {
                    var back = new CardSlot { IsBackCard=true };
                    back.Rt = CreateCard("<", "назад", "<", new Color32(0x60,0x60,0x60,0xFF), 0, -CardH - 50f, 0);
                    return back;
                }
                var d = defs[i - 1];
                var s = new CardSlot { ToolDef=d };
                s.Rt = CreateCard(d.Title, d.Sub, d.Icon, d.Art, 0, -CardH - 50f, 0);
                return s;
            });
        }

        void ShowBuildings(BldDef[] defs)
        {
            ClearActive();
            _state = DeckState.SecondLevel;
            _canvas.gameObject.SetActive(true);
            UpdateDeckY();

            int total = defs.Length + 1;
            SpawnFan(total, i =>
            {
                if (i == 0)
                {
                    var back = new CardSlot { IsBackCard=true };
                    back.Rt = CreateCard("<", "назад", "<", new Color32(0x60,0x60,0x60,0xFF), 0, -CardH - 50f, 0);
                    return back;
                }
                var d = defs[i - 1];
                string sub = _economy != null ? _economy.CostOf(d.Category, d.Kind).ToString("0") + "B" : d.Sub;
                var s = new CardSlot { IsBldCard=true, BldDef=d };
                s.Rt = CreateCard(d.Title, sub, d.Icon, d.Art, 0, -CardH - 50f, 0);
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

        RectTransform CreateCard(string title, string sub, string icon, Color32 art, float x, float y, float rot)
        {
            var go = new GameObject("Card_" + title);
            go.transform.SetParent(_deckRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0f);
            rt.anchorMax        = new Vector2(0.5f, 0f);
            rt.pivot            = new Vector2(0.5f, 0f);
            rt.sizeDelta        = new Vector2(CardW, CardH);
            rt.anchoredPosition = new Vector2(x, y);
            rt.localEulerAngles = new Vector3(0, 0, rot);

            // Тень
            {
                var sh = new GameObject("Shadow"); sh.transform.SetParent(go.transform, false);
                var shRt = sh.AddComponent<RectTransform>();
                shRt.anchorMin=Vector2.zero; shRt.anchorMax=Vector2.one;
                shRt.offsetMin=new Vector2(3,-4); shRt.offsetMax=new Vector2(4,-3);
                sh.AddComponent<Image>().color = new Color(0,0,0,0.20f);
            }
            // Рамка
            AddRect(go.transform, "Frame", Vector2.zero, Vector2.zero, Vector2.zero,
                    new Color32(0x4A,0x40,0x34,0xFF));

            // Фон
            AddRect(go.transform, "Bg", Vector2.zero, new Vector2(2,2), new Vector2(-2,-2),
                    new Color32(0xF0,0xEB,0xE2,0xFF));

            // Арт-панель (верхние ~64%)
            var artGo = new GameObject("Art"); artGo.transform.SetParent(go.transform, false);
            var artRt = artGo.AddComponent<RectTransform>();
            artRt.anchorMin = new Vector2(0f, 0.36f); artRt.anchorMax = Vector2.one;
            artRt.offsetMin = new Vector2(2,0); artRt.offsetMax = new Vector2(-2,-2);
            artGo.AddComponent<Image>().color = art;

            // Иконка — большой белый текст по центру (верхние 62% арта)
            if (!string.IsNullOrEmpty(icon))
                AddLabel(artGo.transform, "Icon", icon, 22, FontStyle.Bold,
                         new Color(1f,1f,1f,0.88f), TextAnchor.MiddleCenter,
                         new Vector2(0f,0.38f), Vector2.one);

            // Название — нижние 38% арта
            AddLabel(artGo.transform, "Title", title, 9, FontStyle.Bold,
                     Color.white, TextAnchor.LowerCenter,
                     Vector2.zero, new Vector2(1f,0.38f));

            // Нижняя полоса
            var botGo = new GameObject("Bot"); botGo.transform.SetParent(go.transform, false);
            var botRt = botGo.AddComponent<RectTransform>();
            botRt.anchorMin = new Vector2(0f,0f); botRt.anchorMax = new Vector2(1f,0.36f);
            botRt.offsetMin = new Vector2(2,2); botRt.offsetMax = new Vector2(-2,0);
            botGo.AddComponent<Image>().color = new Color32(0xE0,0xD9,0xCF,0xFF);

            AddLabel(botGo.transform, "Sub", sub, 8, FontStyle.Normal,
                     new Color32(0x4A,0x3C,0x2C,0xFF), TextAnchor.MiddleCenter,
                     Vector2.zero, Vector2.one);

            return rt;
        }

        static void AddRect(Transform parent, string name, Vector2 offset, Vector2 oMin, Vector2 oMax, Color color,
                            Vector2? extraOffset = null)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = oMin;
            rt.offsetMax = oMax;
            rt.anchoredPosition += offset;
            go.AddComponent<Image>().color = color;
        }

        static void AddLabel(Transform parent, string name, string text, int size, FontStyle style,
                             Color color, TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = new Vector2(1,1); rt.offsetMax = new Vector2(-1,-1);
            var t = go.AddComponent<Text>();
            t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color;
            t.alignment = anchor; t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
        //  Ввод — hover + tap (без drag-to-place)
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

                // Возврат карточки — только если колода ещё видна (не была скрыта в этот же кадр)
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
            if (slot.IsBackCard)
            {
                ShowCategories();
                return;
            }

            if (_state == DeckState.Categories)
            {
                switch (CatDefs[slot.CatIndex].Action)
                {
                    case CatAction.ShowRoads:      ShowTools(RoadDefs);         break;
                    case CatAction.ShowDistricts:  ShowBuildings(DistrictDefs); break;
                    case CatAction.ShowServices:   ShowBuildings(ServiceDefs);  break;
                    case CatAction.ShowEnergy:     ShowBuildings(EnergyDefs);   break;
                    case CatAction.ShowInfra:      ShowTools(InfraDefs);        break;
                    case CatAction.Erase:
                        HideAll();
                        ToolRequested?.Invoke(Tool.Erase);
                        break;
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
