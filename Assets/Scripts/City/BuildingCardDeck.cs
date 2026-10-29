using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace CityGame.City
{
    using Buildings;

    /// <summary>
    /// Двухуровневая карточная колода.
    /// Уровень 1 — 6 категорий (ДОРОГИ, РАЙОНЫ, СЛУЖБЫ, ЭНЕРГИЯ, ИНФРА, СНЕСТИ).
    /// Уровень 2 — инструменты или здания категории.
    ///
    /// Здание: тяни вверх → карта следует за пальцем →
    /// выше 40% экрана карта прячется, появляется цветной пин на карте.
    /// Инструменты дорог/инфры: тап → активирует инструмент.
    /// </summary>
    public sealed class BuildingCardDeck : MonoBehaviour
    {
        public float CellSize = 1f;

        // ──────────────────────────────────────────────────────────
        //  Событие
        // ──────────────────────────────────────────────────────────

        /// <summary>Сработало при выборе инструмента из карточки (дорога, служба, снести…).</summary>
        public event System.Action<Tool> ToolRequested;

        // ──────────────────────────────────────────────────────────
        //  Зависимости / состояние
        // ──────────────────────────────────────────────────────────

        BuildingPlacer _placer;
        Economy        _economy;
        CityDebugHud   _hud;
        Canvas         _canvas;
        RectTransform  _deckRoot;
        Camera         _cam;

        enum DeckState { Hidden, Categories, SecondLevel }
        DeckState _state = DeckState.Hidden;
        public bool IsVisible => _state != DeckState.Hidden;

        /// <summary>True если точка экрана над зоной колоды — для блокировки панорамирования.</summary>
        public bool ContainsScreenPoint(Vector2 screen)
        {
            if (_state == DeckState.Hidden || _deckRoot == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_deckRoot, screen, null);
        }

        readonly List<CardSlot> _active = new List<CardSlot>();

        // ──────────────────────────────────────────────────────────
        //  Ghost / pin
        // ──────────────────────────────────────────────────────────

        GameObject   _ghost;
        MeshRenderer _ghostMr;
        static readonly Color GhostColorOk  = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color GhostColorBad = new Color(0.9f, 0.15f, 0.15f, 0.45f);

        // ──────────────────────────────────────────────────────────
        //  Drag
        // ──────────────────────────────────────────────────────────

        CardSlot _held;
        bool     _dragging;      // начали тянуть вверх
        bool     _pinMode;       // выше 40% экрана — карта стала пином
        Vector2  _dragStart;
        const float DragStartPx  = 18f;
        const float PinThreshold = 0.40f; // доля высоты экрана

        // ──────────────────────────────────────────────────────────
        //  Константы карточек
        // ──────────────────────────────────────────────────────────

        const float CardW     = 82f;
        const float CardH     = 115f;
        const float MaxStep   = 64f;   // максимальный шаг при мало карт
        const float MaxRotDeg = 9f;
        const float ArcDrop   = 12f;
        const float DeckYBase = 6f;
        const float LiftY     = 30f;
        const float AnimIn    = 0.28f;
        const float AnimOut   = 0.16f;
        const float Stagger   = 0.045f;

        // ──────────────────────────────────────────────────────────
        //  Цвета — привязаны к игровой палитре
        // ──────────────────────────────────────────────────────────

        // Категории
        static readonly Color32 CAT_ROADS   = new Color32(0x3B, 0x45, 0x52, 0xFF); // асфальт
        static readonly Color32 CAT_DIST    = new Color32(0x6F, 0xA8, 0xDC, 0xFF); // жильё синий
        static readonly Color32 CAT_SERV    = new Color32(0xD9, 0x4F, 0x45, 0xFF); // пожарные красный
        static readonly Color32 CAT_ENERGY  = new Color32(0xF2, 0xC1, 0x4E, 0xFF); // желтый
        static readonly Color32 CAT_INFRA   = new Color32(0x7A, 0xB8, 0x62, 0xFF); // зелёный (леса)
        static readonly Color32 CAT_ERASE   = new Color32(0x9A, 0xA0, 0xA6, 0xFF); // горный серый

        // Дорожные инструменты
        static readonly Color32 ROAD_ROAD   = new Color32(0x3B, 0x45, 0x52, 0xFF);
        static readonly Color32 ROAD_HWY    = new Color32(0x28, 0x38, 0x52, 0xFF);
        static readonly Color32 ROAD_OW     = new Color32(0x42, 0x55, 0x68, 0xFF);
        static readonly Color32 ROAD_JCT    = new Color32(0x48, 0x40, 0x68, 0xFF);
        static readonly Color32 ROAD_CANAL  = new Color32(0x4F, 0xA3, 0xE0, 0xFF);
        static readonly Color32 ROAD_RAMP   = new Color32(0xC8, 0x7A, 0x38, 0xFF);

        // Инфра
        static readonly Color32 INFRA_EXP   = new Color32(0xCD, 0xE5, 0xB0, 0xFF);
        static readonly Color32 INFRA_PORT  = new Color32(0xA9, 0xD6, 0xF2, 0xFF);
        static readonly Color32 INFRA_BUS   = new Color32(0xF2, 0xC1, 0x4E, 0xFF);

        // Здания — прямо из BuildingCategory.Color()
        static readonly Color32 BLD_RES     = new Color32(0x6F, 0xA8, 0xDC, 0xFF);
        static readonly Color32 BLD_COM     = new Color32(0xE8, 0x83, 0x8B, 0xFF);
        static readonly Color32 BLD_IND     = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        static readonly Color32 SVC_AMB     = new Color32(0xD9, 0x4F, 0x45, 0xFF);
        static readonly Color32 SVC_POL     = new Color32(0x3D, 0x5A, 0xA8, 0xFF);
        static readonly Color32 SVC_FIRE    = new Color32(0xC8, 0x3C, 0x3C, 0xFF);
        static readonly Color32 SVC_PWR     = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        static readonly Color32 SVC_WAT     = new Color32(0x4F, 0xA3, 0xE0, 0xFF);

        // ──────────────────────────────────────────────────────────
        //  Определения карточек
        // ──────────────────────────────────────────────────────────

        enum CatAction { ShowRoads, ShowDistricts, ShowServices, ShowEnergy, ShowInfra, Erase }

        struct CatDef  { public string Title, Sub; public Color32 Art; public CatAction Action; }
        struct ToolDef { public string Title, Sub; public Color32 Art; public Tool     Tool;    }
        struct BldDef  { public string Title, Sub; public Color32 Art;
                         public BuildingCategory Category; public ServiceKind Kind; }

        static readonly CatDef[] CatDefs =
        {
            new CatDef { Title="ДОРОГИ",  Sub="трассы",   Art=CAT_ROADS,  Action=CatAction.ShowRoads      },
            new CatDef { Title="РАЙОНЫ",  Sub="здания",   Art=CAT_DIST,   Action=CatAction.ShowDistricts  },
            new CatDef { Title="СЛУЖБЫ",  Sub="сервисы",  Art=CAT_SERV,   Action=CatAction.ShowServices   },
            new CatDef { Title="ЭНЕРГИЯ", Sub="питание",  Art=CAT_ENERGY, Action=CatAction.ShowEnergy     },
            new CatDef { Title="ИНФРА",   Sub="сектора",  Art=CAT_INFRA,  Action=CatAction.ShowInfra      },
            new CatDef { Title="СНЕСТИ",  Sub="убрать",   Art=CAT_ERASE,  Action=CatAction.Erase          },
        };

        static readonly ToolDef[] RoadDefs =
        {
            new ToolDef { Title="Дорога",  Sub="улица",  Art=ROAD_ROAD,  Tool=Tool.Road     },
            new ToolDef { Title="Шоссе",   Sub="быстро", Art=ROAD_HWY,   Tool=Tool.Highway  },
            new ToolDef { Title="Одност.", Sub="полоса", Art=ROAD_OW,    Tool=Tool.OneWay   },
            new ToolDef { Title="Развилка",Sub="узел",   Art=ROAD_JCT,   Tool=Tool.Junction },
            new ToolDef { Title="Канал",   Sub="вода",   Art=ROAD_CANAL, Tool=Tool.Canal    },
            new ToolDef { Title="Съезд",   Sub="рампа",  Art=ROAD_RAMP,  Tool=Tool.Ramp     },
        };

        static readonly ToolDef[] InfraDefs =
        {
            new ToolDef { Title="Расшир.", Sub="сектор", Art=INFRA_EXP,  Tool=Tool.Expand },
            new ToolDef { Title="Порт",    Sub="море",   Art=INFRA_PORT, Tool=Tool.Port   },
            new ToolDef { Title="Автобус", Sub="маршр.", Art=INFRA_BUS,  Tool=Tool.Bus    },
        };

        static readonly BldDef[] DistrictDefs =
        {
            new BldDef { Title="Жильё",   Sub="жилой",  Art=BLD_RES, Category=BuildingCategory.Residential },
            new BldDef { Title="Торговля", Sub="коммер.",Art=BLD_COM, Category=BuildingCategory.Commercial  },
            new BldDef { Title="Произв.",  Sub="завод",  Art=BLD_IND, Category=BuildingCategory.Industrial  },
        };

        static readonly BldDef[] ServiceDefs =
        {
            new BldDef { Title="Скорая",   Sub="здоровье",Art=SVC_AMB,  Category=BuildingCategory.Service, Kind=ServiceKind.Ambulance  },
            new BldDef { Title="Полиция",  Sub="охрана",  Art=SVC_POL,  Category=BuildingCategory.Service, Kind=ServiceKind.Police     },
            new BldDef { Title="Пожарные", Sub="тушение", Art=SVC_FIRE, Category=BuildingCategory.Service, Kind=ServiceKind.Fire       },
        };

        static readonly BldDef[] EnergyDefs =
        {
            new BldDef { Title="Электро",   Sub="ток",  Art=SVC_PWR, Category=BuildingCategory.Service, Kind=ServiceKind.PowerPlant },
            new BldDef { Title="Водобашня", Sub="вода", Art=SVC_WAT, Category=BuildingCategory.Service, Kind=ServiceKind.WaterTower },
        };

        // ──────────────────────────────────────────────────────────
        //  CardSlot
        // ──────────────────────────────────────────────────────────

        class CardSlot
        {
            public RectTransform Rt;
            public float BaseX, BaseY, BaseRot;
            public bool  Lifted;
            public bool  IsDraggable; // здания — тяни на карту
            public bool  IsBackCard;
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
            _cam     = Camera.main;
            BuildCanvas();
            BuildGhost();
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
            _deckRoot.sizeDelta = new Vector2(400f, CardH + DeckYBase + ArcDrop + 30f);
            _canvas.gameObject.SetActive(false);
        }

        void BuildGhost()
        {
            _ghost = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _ghost.name = "BuildGhost";
            _ghost.transform.localScale = Vector3.one * CellSize * 0.9f;
            Destroy(_ghost.GetComponent<MeshCollider>());
            _ghostMr = _ghost.GetComponent<MeshRenderer>();
            _ghostMr.material = new Material(Shader.Find("Unlit/Color"));
            _ghost.SetActive(false);
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
            _state  = DeckState.Hidden;
            _held   = null;
            _pinMode   = false;
            _dragging  = false;
            _ghost.SetActive(false);
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
            UpdateDeckPosition();
            SpawnCards(CatDefs.Length, (i, baseX, baseY, baseR) =>
            {
                var d = CatDefs[i];
                var slot = new CardSlot { BaseX=baseX, BaseY=baseY, BaseRot=baseR };
                slot.Rt = CreateCard(d.Title, d.Sub, d.Art, baseX, baseY, baseR);
                // Сохраняем action через CatAction — кодируем в ToolDef.Tool как -1...-N
                slot.ToolDef = new ToolDef { Title=d.Title, Art=d.Art, Tool=(Tool)(-1 - i) };
                _active.Add(slot);
                AnimateIn(slot, i);
            });
        }

        void ShowTools(ToolDef[] defs)
        {
            ClearActive();
            _state = DeckState.SecondLevel;
            _canvas.gameObject.SetActive(true);
            UpdateDeckPosition();
            int total = defs.Length + 1;
            float step = CalcStep(total);
            float totalW = (total - 1) * step + CardW;
            float startX = -totalW * 0.5f + CardW * 0.5f;
            float halfN  = (total - 1) * 0.5f;

            // Back card
            {
                float t = halfN > 0 ? -1f : 0f;
                float baseX = startX, baseY = DeckYBase - ArcDrop*(t*t), baseR = -MaxRotDeg*t;
                var slot = new CardSlot { BaseX=baseX, BaseY=baseY, BaseRot=baseR, IsBackCard=true };
                slot.Rt = CreateCard("←", "назад", CAT_ROADS, baseX, baseY, baseR);
                _active.Add(slot);
                AnimateIn(slot, 0);
            }
            for (int i = 0; i < defs.Length; i++)
            {
                int idx = i + 1;
                float t = halfN > 0 ? (idx - halfN) / halfN : 0f;
                float baseX = startX + idx * step;
                float baseY = DeckYBase - ArcDrop*(t*t);
                float baseR = -MaxRotDeg * t;
                var d = defs[i];
                var slot = new CardSlot { BaseX=baseX, BaseY=baseY, BaseRot=baseR, ToolDef=d };
                slot.Rt = CreateCard(d.Title, d.Sub, d.Art, baseX, baseY, baseR);
                _active.Add(slot);
                AnimateIn(slot, idx);
            }
        }

        void ShowBuildings(BldDef[] defs)
        {
            ClearActive();
            _state = DeckState.SecondLevel;
            _canvas.gameObject.SetActive(true);
            UpdateDeckPosition();
            int total = defs.Length + 1;
            float step = CalcStep(total);
            float totalW = (total - 1) * step + CardW;
            float startX = -totalW * 0.5f + CardW * 0.5f;
            float halfN  = (total - 1) * 0.5f;

            // Back card
            {
                float t = halfN > 0 ? -1f : 0f;
                float baseX = startX, baseY = DeckYBase - ArcDrop*(t*t), baseR = -MaxRotDeg*t;
                var slot = new CardSlot { BaseX=baseX, BaseY=baseY, BaseRot=baseR, IsBackCard=true };
                slot.Rt = CreateCard("←", "назад", new Color32(0x78,0x78,0x78,0xFF), baseX, baseY, baseR);
                _active.Add(slot);
                AnimateIn(slot, 0);
            }
            for (int i = 0; i < defs.Length; i++)
            {
                int idx = i + 1;
                float t = halfN > 0 ? (idx - halfN) / halfN : 0f;
                float baseX = startX + idx * step;
                float baseY = DeckYBase - ArcDrop*(t*t);
                float baseR = -MaxRotDeg * t;
                var d = defs[i];
                string cost = _economy != null ? _economy.CostOf(d.Category, d.Kind).ToString("0") + "₿" : d.Sub;
                var slot = new CardSlot { BaseX=baseX, BaseY=baseY, BaseRot=baseR, IsDraggable=true, BldDef=d };
                slot.Rt = CreateCard(d.Title, cost, d.Art, baseX, baseY, baseR);
                _active.Add(slot);
                AnimateIn(slot, idx);
            }
        }

        // helper: spawn N cards with fan layout
        void SpawnCards(int n, System.Action<int, float, float, float> onCreate)
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
                onCreate(i, baseX, baseY, baseR);
            }
        }

        static float CalcStep(int n) => n <= 1 ? 0f : Mathf.Min(MaxStep, 360f / (n - 1));

        // ──────────────────────────────────────────────────────────
        //  Построение UI одной карточки
        // ──────────────────────────────────────────────────────────

        RectTransform CreateCard(string title, string sub, Color32 art, float x, float y, float rot)
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

            MakeRect(go.transform, "Shadow", new Color(0,0,0,0.18f), new Vector2(3f,-3f), new Vector2(CardW, CardH));
            MakeRect(go.transform, "Frame",  new Color32(0x55,0x4A,0x3C,0xFF), Vector2.zero, new Vector2(CardW, CardH));
            MakeRect(go.transform, "Bg",     new Color32(0xF2,0xEE,0xE6,0xFF), Vector2.zero, new Vector2(CardW-4f, CardH-4f));

            var artGo = MakeChild(go.transform, "Art", new Vector2(0f,0.34f), Vector2.one,
                new Vector2(3f,0f), new Vector2(-3f,-3f));
            artGo.GetComponent<Image>().color = art;
            AddText(artGo.transform, "Title", title, 10, FontStyle.Bold, Color.white,
                TextAnchor.LowerCenter, new Vector2(0f,0f), new Vector2(1f,0.38f));

            var botGo = MakeChild(go.transform, "Bot", new Vector2(0f,0f), new Vector2(1f,0.34f),
                new Vector2(3f,3f), new Vector2(-3f,0f));
            botGo.GetComponent<Image>().color = new Color32(0xE4,0xDE,0xD4,0xFF);
            AddText(botGo.transform, "Sub", sub, 8, FontStyle.Normal,
                new Color32(0x4A,0x3C,0x2C,0xFF), TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);

            return rt;
        }

        static void MakeRect(Transform p, string n, Color c, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(n); go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f,0.5f); rt.anchorMax = new Vector2(0.5f,0.5f);
            rt.pivot = new Vector2(0.5f,0.5f); rt.sizeDelta = size; rt.anchoredPosition = offset;
            go.AddComponent<Image>().color = c;
        }

        static GameObject MakeChild(Transform p, string n, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(n); go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            go.AddComponent<Image>();
            return go;
        }

        static void AddText(Transform p, string n, string text, int size, FontStyle style,
            Color c, TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(n); go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = new Vector2(2f,1f); rt.offsetMax = new Vector2(-2f,-1f);
            var t = go.AddComponent<Text>();
            t.text = text; t.fontSize = size; t.fontStyle = style; t.color = c;
            t.alignment = anchor; t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ──────────────────────────────────────────────────────────
        //  Анимации
        // ──────────────────────────────────────────────────────────

        void AnimateIn(CardSlot slot, int order)
        {
            if (slot.Rt == null) return;
            slot.Rt.anchoredPosition = new Vector2(slot.BaseX, -CardH - 50f);
            slot.Rt.localEulerAngles = Vector3.zero;
            slot.AnimateTo(slot.BaseX, slot.BaseY, slot.BaseRot, AnimIn, order * Stagger);
        }

        void AnimateOutAndDestroy(List<CardSlot> slots)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                s.Tween?.Kill();
                if (s.Rt == null) continue;
                var rt = s.Rt;
                float delay = (slots.Count - 1 - i) * Stagger * 0.5f;
                var seq = DOTween.Sequence();
                if (delay > 0f) seq.AppendInterval(delay);
                seq.Append(rt.DOAnchorPosY(-CardH - 60f, AnimOut).SetEase(Ease.InCubic))
                   .OnComplete(() => { if (rt != null) Destroy(rt.gameObject); });
                s.Tween = seq.Play();
            }
            float total = AnimOut + slots.Count * Stagger * 0.5f + 0.05f;
            DOVirtual.DelayedCall(total, () => { if (_state == DeckState.Hidden && _canvas != null) _canvas.gameObject.SetActive(false); });
            slots.Clear();
        }

        void ClearActive()
        {
            _held = null; _pinMode = false; _dragging = false; _ghost.SetActive(false);
            foreach (var s in _active) { s.Tween?.Kill(); if (s.Rt != null) Destroy(s.Rt.gameObject); }
            _active.Clear();
        }

        // ──────────────────────────────────────────────────────────
        //  Update
        // ──────────────────────────────────────────────────────────

        void Update()
        {
            if (_state == DeckState.Hidden || _canvas == null || !_canvas.gameObject.activeSelf) return;
            UpdateDeckPosition();
            HandleInput();
        }

        void UpdateDeckPosition()
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

            // Hover-подъём (только без удержания)
            if (_held == null && held)
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
                    _held = s; _dragStart = pos; _dragging = false; _pinMode = false;
                    s.AnimateTo(s.BaseX, s.BaseY + LiftY * 1.5f, 0f, 0.08f);
                    break;
                }
            }

            // Удержание
            if (_held != null && (held || down))
            {
                float dy = pos.y - _dragStart.y;
                if (!_dragging && dy > DragStartPx) _dragging = true;

                if (_dragging && _held.IsDraggable)
                {
                    bool wasPin = _pinMode;
                    _pinMode = pos.y > Screen.height * PinThreshold;

                    if (_pinMode)
                    {
                        // Карта уходит в пин-режим
                        if (_held.Rt != null) _held.Rt.gameObject.SetActive(false);
                        UpdateGhost(pos, _held, pinMode: true);
                    }
                    else
                    {
                        // Карта следует за пальцем в пространстве колоды
                        if (_held.Rt != null)
                        {
                            _held.Rt.gameObject.SetActive(true);
                            Vector2 local;
                            RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, pos, null, out local);
                            _held.Rt.anchoredPosition = new Vector2(_held.BaseX, Mathf.Max(_held.BaseY, local.y - CardH * 0.5f));
                            _held.Rt.localEulerAngles = Vector3.zero;
                        }
                        _ghost.SetActive(false);
                    }
                }
            }

            // Отпускание
            if (up && _held != null)
            {
                var slot = _held; _held = null;

                if (_dragging && slot.IsDraggable && _pinMode)
                {
                    // Разместить здание в точке пина
                    _pinMode = false; _dragging = false;
                    _ghost.SetActive(false);
                    PlaceBuilding(pos, slot.BldDef);
                }
                else
                {
                    _pinMode = false; _dragging = false; _ghost.SetActive(false);
                    if (slot.Rt != null) slot.Rt.gameObject.SetActive(true);
                    OnCardTapped(slot);
                }

                // Вернуть карточку (если ещё существует)
                if (slot.Rt != null && slot.Rt.gameObject != null)
                {
                    slot.Tween?.Kill();
                    slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, slot.BaseY), 0.22f).SetEase(Ease.OutBack);
                    slot.Rt.DOLocalRotate(new Vector3(0,0,slot.BaseRot), 0.22f).SetEase(Ease.OutBack);
                    slot.Lifted = false;
                }
            }

            // Потеря касания (не в тот же фрейм, что down)
            if (!down && !held && _held != null)
            {
                var slot = _held; _held = null;
                _pinMode = false; _dragging = false; _ghost.SetActive(false);
                if (slot.Rt != null)
                {
                    slot.Rt.gameObject.SetActive(true);
                    slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, slot.BaseY), 0.18f).SetEase(Ease.OutCubic);
                    slot.Rt.DOLocalRotate(new Vector3(0,0,slot.BaseRot), 0.18f).SetEase(Ease.OutCubic);
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
                // ToolDef.Tool закодирован как -(1+index)
                int catIdx = -(int)slot.ToolDef.Tool - 1;
                var act = CatDefs[catIdx].Action;
                switch (act)
                {
                    case CatAction.ShowRoads:      ShowTools(RoadDefs);    break;
                    case CatAction.ShowDistricts:  ShowBuildings(DistrictDefs); break;
                    case CatAction.ShowServices:   ShowBuildings(ServiceDefs);  break;
                    case CatAction.ShowEnergy:     ShowBuildings(EnergyDefs);   break;
                    case CatAction.ShowInfra:      ShowTools(InfraDefs);   break;
                    case CatAction.Erase:
                        HideAll();
                        ToolRequested?.Invoke(Tool.Erase);
                        break;
                }
            }
            else // SecondLevel — tool card
            {
                HideAll();
                ToolRequested?.Invoke(slot.ToolDef.Tool);
            }
        }

        // ──────────────────────────────────────────────────────────
        //  Размещение здания
        // ──────────────────────────────────────────────────────────

        void PlaceBuilding(Vector2 screen, BldDef def)
        {
            if (_cam == null || _placer == null) return;
            Vector3 w = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
            var cell = new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
            _placer.TryPlaceAt(cell, def.Category, def.Kind);
        }

        // ──────────────────────────────────────────────────────────
        //  Ghost / pin
        // ──────────────────────────────────────────────────────────

        void UpdateGhost(Vector2 screen, CardSlot slot, bool pinMode)
        {
            if (_cam == null) return;
            Vector3 w = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
            var cell = new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
            bool ok = _placer != null &&
                      _placer.CheckPlacement(cell, slot.BldDef.Category, slot.BldDef.Kind) == PlaceResult.Ok;

            _ghost.SetActive(true);
            _ghost.transform.position = new Vector3((cell.x + 0.5f) * CellSize, (cell.y + 0.5f) * CellSize, -0.5f);

            if (pinMode)
            {
                // Маленький яркий пин
                _ghost.transform.localScale = Vector3.one * CellSize * 0.35f;
                var d = slot.BldDef.Art;
                _ghostMr.material.color = ok
                    ? new Color(d.r/255f, d.g/255f, d.b/255f, 1f)
                    : new Color(0.9f, 0.1f, 0.1f, 1f);
            }
            else
            {
                _ghost.transform.localScale = Vector3.one * CellSize * 0.9f;
                var d = slot.BldDef.Art;
                _ghostMr.material.color = ok
                    ? new Color(d.r/255f, d.g/255f, d.b/255f, 0.55f)
                    : new Color(1f, 0.15f, 0.15f, 0.45f);
            }
        }

        // ──────────────────────────────────────────────────────────
        //  Hit-testing
        // ──────────────────────────────────────────────────────────

        bool IsOnCard(Vector2 screen, CardSlot slot)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local)) return false;
            float cy = (slot.Rt != null ? slot.Rt.anchoredPosition.y : slot.BaseY) + CardH * 0.5f;
            return Mathf.Abs(local.x - slot.BaseX) < CardW * 0.52f && Mathf.Abs(local.y - cy) < CardH * 0.52f;
        }

        bool IsNearCard(Vector2 screen, CardSlot slot)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local)) return false;
            return Mathf.Abs(local.x - slot.BaseX) < CardW * 0.5f + 30f
                && local.y > slot.BaseY - 20f && local.y < slot.BaseY + CardH + 48f;
        }

        // ──────────────────────────────────────────────────────────
        //  Pointer input
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
            if (Input.touchCount > 0) { var p = Input.GetTouch(0).phase; return p == TouchPhase.Stationary || p == TouchPhase.Moved; }
            return Input.GetMouseButton(0);
        }
        static bool GetPointerUp()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).phase == TouchPhase.Ended;
            return Input.GetMouseButtonUp(0);
        }

        void OnDestroy()
        {
            if (_ghost != null) Destroy(_ghost);
        }
    }
}
