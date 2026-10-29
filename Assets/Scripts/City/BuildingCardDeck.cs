using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace CityGame.City
{
    using Buildings;

    /// <summary>
    /// Двухуровневая карточная колода.
    /// Уровень 1 — 5 категорий (ДОРОГИ, РАЙОНЫ, СЛУЖБЫ, ЭНЕРГИЯ, СНЕСТИ): появляются по "$".
    /// Уровень 2 — здания выбранной категории: появляются при тапе по категории,
    ///             карточка здания перетаскивается на карту.
    /// </summary>
    public sealed class BuildingCardDeck : MonoBehaviour
    {
        public float CellSize = 1f;

        // ──────── события ────────
        public event System.Action RoadsRequested;
        public event System.Action EraseRequested;

        // ──────── зависимости ────────
        BuildingPlacer _placer;
        Economy        _economy;
        CityDebugHud   _hud;
        Canvas         _canvas;
        RectTransform  _deckRoot;
        Camera         _cam;

        // ──────── состояние ────────
        enum DeckState { Hidden, Categories, Buildings }
        DeckState _state = DeckState.Hidden;
        public bool IsVisible => _state != DeckState.Hidden;

        readonly List<CardSlot> _active = new List<CardSlot>();

        // ──────── ghost ────────
        GameObject   _ghost;
        MeshRenderer _ghostMr;

        // ──────── drag ────────
        CardSlot _held;
        bool     _draggingToMap;
        Vector2  _dragStart;

        // ──────── константы ────────
        const float CardW        = 85f;
        const float CardH        = 120f;
        const float CardStep     = 68f;
        const float MaxRotDeg    = 9f;
        const float ArcDrop      = 12f;
        const float DeckYBase    = 6f;
        const float LiftY        = 32f;
        const float DragThreshPx = 52f;
        const float AnimIn       = 0.32f;
        const float AnimOut      = 0.18f;
        const float StaggerDelay = 0.05f;

        // ──────────────────────────────────────────────────────────
        //  Определения категорий
        // ──────────────────────────────────────────────────────────

        enum CatAction { Roads, Districts, Services, Energy, Erase, Back }

        struct CatDef
        {
            public string    Title;
            public string    Sub;
            public Color32   Art;
            public CatAction Action;
        }

        static readonly CatDef[] Categories =
        {
            new CatDef { Title="ДОРОГИ",  Sub="трассы",  Art=new Color32(0x42,0x8B,0xCA,0xFF), Action=CatAction.Roads     },
            new CatDef { Title="РАЙОНЫ",  Sub="здания",  Art=new Color32(0x4C,0xAF,0x50,0xFF), Action=CatAction.Districts  },
            new CatDef { Title="СЛУЖБЫ",  Sub="сервисы", Art=new Color32(0xF4,0x43,0x36,0xFF), Action=CatAction.Services   },
            new CatDef { Title="ЭНЕРГИЯ", Sub="питание", Art=new Color32(0xFF,0xC1,0x07,0xFF), Action=CatAction.Energy     },
            new CatDef { Title="СНЕСТИ",  Sub="снести",  Art=new Color32(0x78,0x6C,0x64,0xFF), Action=CatAction.Erase      },
        };

        // ──────────────────────────────────────────────────────────
        //  Определения зданий (второй уровень)
        // ──────────────────────────────────────────────────────────

        struct BldDef
        {
            public string           Title;
            public BuildingCategory Category;
            public ServiceKind      Kind;
            public Color32          Art;
        }

        static readonly BldDef[] DistrictDefs =
        {
            new BldDef { Title="Жильё",    Category=BuildingCategory.Residential, Art=new Color32(0x4C,0xAF,0x50,0xFF) },
            new BldDef { Title="Торговля",  Category=BuildingCategory.Commercial,  Art=new Color32(0x21,0x96,0xF3,0xFF) },
            new BldDef { Title="Произв.",   Category=BuildingCategory.Industrial,  Art=new Color32(0xFF,0x98,0x00,0xFF) },
        };
        static readonly BldDef[] ServiceDefs =
        {
            new BldDef { Title="Скорая",   Category=BuildingCategory.Service, Kind=ServiceKind.Ambulance,  Art=new Color32(0xF4,0x43,0x36,0xFF) },
            new BldDef { Title="Полиция",  Category=BuildingCategory.Service, Kind=ServiceKind.Police,     Art=new Color32(0x3F,0x51,0xB5,0xFF) },
            new BldDef { Title="Пожарные", Category=BuildingCategory.Service, Kind=ServiceKind.Fire,       Art=new Color32(0xFF,0x57,0x22,0xFF) },
        };
        static readonly BldDef[] EnergyDefs =
        {
            new BldDef { Title="Электро",   Category=BuildingCategory.Service, Kind=ServiceKind.PowerPlant, Art=new Color32(0xFF,0xC1,0x07,0xFF) },
            new BldDef { Title="Водобашня", Category=BuildingCategory.Service, Kind=ServiceKind.WaterTower, Art=new Color32(0x00,0xBC,0xD4,0xFF) },
        };

        // ──────────────────────────────────────────────────────────
        //  CardSlot
        // ──────────────────────────────────────────────────────────

        class CardSlot
        {
            public RectTransform Rt;
            public float BaseX, BaseY, BaseRot;
            public bool  Lifted;
            public bool  IsDraggable;
            public bool  IsBackCard;
            public CatAction CatAction;
            public BldDef   BldDef;
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

            public void AnimateOut(float dur, float delay = 0f)
            {
                Tween?.Kill();
                var seq = DOTween.Sequence();
                if (delay > 0f) seq.AppendInterval(delay);
                seq.Append(Rt.DOAnchorPosY(-CardH - 60f, dur).SetEase(Ease.InCubic));
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
            _cam    = Camera.main;
            BuildCanvas(economy);
            BuildGhost();
        }

        void BuildCanvas(Economy eco)
        {
            var cgo = new GameObject("CardDeck_Canvas");
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
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
            float deckW = Mathf.Max(5, Categories.Length) * CardStep + CardW;
            _deckRoot.sizeDelta = new Vector2(deckW, CardH + DeckYBase + ArcDrop + 30f);

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
            if (_state == DeckState.Hidden)
                ShowCategories();
            else
                HideAll();
        }

        public void HideAll()
        {
            if (_state == DeckState.Hidden) return;
            _state  = DeckState.Hidden;
            _held   = null;
            _ghost.SetActive(false);
            AnimateOut(_active);
        }

        // ──────────────────────────────────────────────────────────
        //  Создание карточек
        // ──────────────────────────────────────────────────────────

        void ShowCategories()
        {
            ClearActive();
            _state = DeckState.Categories;
            _canvas.gameObject.SetActive(true);
            UpdateDeckPosition();

            int n = Categories.Length;
            float halfN = (n - 1) * 0.5f;
            float totalW = (n - 1) * CardStep + CardW;
            float startX = -totalW * 0.5f + CardW * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float t     = halfN > 0 ? (i - halfN) / halfN : 0f;
                float baseX = startX + i * CardStep;
                float baseY = DeckYBase - ArcDrop * (t * t);
                float baseR = -MaxRotDeg * t;
                var cat = Categories[i];

                var slot = new CardSlot
                {
                    BaseX    = baseX,
                    BaseY    = baseY,
                    BaseRot  = baseR,
                    CatAction = cat.Action,
                    IsDraggable = false,
                };
                slot.Rt = CreateCard(cat.Title, cat.Sub, cat.Art, baseX, -CardH - 60f, 0f);
                _active.Add(slot);
                slot.AnimateTo(baseX, baseY, baseR, AnimIn, i * StaggerDelay);
            }
        }

        void ShowBuildings(BldDef[] defs)
        {
            ClearActive();
            _state = DeckState.Buildings;
            _canvas.gameObject.SetActive(true);
            UpdateDeckPosition();

            // Первый слот — кнопка «назад»
            int total = defs.Length + 1;
            float halfN = (total - 1) * 0.5f;
            float totalW = (total - 1) * CardStep + CardW;
            float startX = -totalW * 0.5f + CardW * 0.5f;

            // Карточка "←"
            {
                float baseX = startX;
                float baseR = (total > 1) ? -MaxRotDeg * (-1f) : 0f;
                float baseY = DeckYBase - ArcDrop * (1f);
                var back = new CardSlot
                {
                    BaseX = baseX, BaseY = baseY, BaseRot = baseR,
                    IsBackCard = true, IsDraggable = false,
                };
                back.Rt = CreateCard("←", "назад", new Color32(0x90,0x90,0x88,0xFF), baseX, -CardH-60f, 0f);
                _active.Add(back);
                back.AnimateTo(baseX, baseY, baseR, AnimIn, 0f);
            }

            for (int i = 0; i < defs.Length; i++)
            {
                int idx = i + 1;
                float t     = halfN > 0 ? (idx - halfN) / halfN : 0f;
                float baseX = startX + idx * CardStep;
                float baseY = DeckYBase - ArcDrop * (t * t);
                float baseR = -MaxRotDeg * t;
                var d = defs[i];
                string cost = _economy != null
                    ? _economy.CostOf(d.Category, d.Kind).ToString("0") + "₿"
                    : "";
                var slot = new CardSlot
                {
                    BaseX = baseX, BaseY = baseY, BaseRot = baseR,
                    IsDraggable = true, BldDef = d,
                };
                slot.Rt = CreateCard(d.Title, cost, d.Art, baseX, -CardH-60f, 0f);
                _active.Add(slot);
                slot.AnimateTo(baseX, baseY, baseR, AnimIn, idx * StaggerDelay);
            }
        }

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

            // тень
            MakeRect(go.transform, "Shadow", new Color(0,0,0,0.16f), Vector2.zero, new Vector2(CardW, CardH), new Vector2(3f,-3f));
            // рамка
            MakeRect(go.transform, "Frame",  new Color32(0x7A,0x60,0x42,0xFF), Vector2.zero, new Vector2(CardW, CardH));
            // фон
            MakeRect(go.transform, "Bg",     new Color32(0xF5,0xF2,0xEC,0xFF), Vector2.zero, new Vector2(CardW-4f, CardH-4f));

            // арт-зона (верхние 62%)
            var artGo = MakeChild(go.transform, "Art", new Vector2(0f,0.36f), Vector2.one,
                new Vector2(3f,0f), new Vector2(-3f,-3f));
            artGo.GetComponent<Image>().color = art;

            // подпись на арте
            AddText(artGo.transform, "Title", title, 10, FontStyle.Bold,  Color.white, TextAnchor.LowerCenter,
                new Vector2(0f,0f), new Vector2(1f,0.35f));

            // нижняя зона
            var botGo = MakeChild(go.transform, "Bot", new Vector2(0f,0f), new Vector2(1f,0.36f),
                new Vector2(3f,3f), new Vector2(-3f,0f));
            botGo.GetComponent<Image>().color = new Color32(0xE8,0xE3,0xD8,0xFF);

            AddText(botGo.transform, "Sub", sub, 8, FontStyle.Normal, new Color32(0x55,0x44,0x33,0xFF),
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);

            return rt;
        }

        static void MakeRect(Transform p, string n, Color c, Vector2 pivot, Vector2 size, Vector2 offset = default)
        {
            var go = new GameObject(n);
            go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            go.AddComponent<Image>().color = c;
        }

        static GameObject MakeChild(Transform p, string n, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(n);
            go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
            go.AddComponent<Image>();
            return go;
        }

        static void AddText(Transform p, string n, string text, int size, FontStyle style,
            Color c, TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(n);
            go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = new Vector2(2f,1f); rt.offsetMax = new Vector2(-2f,-1f);
            var t = go.AddComponent<Text>();
            t.text  = text; t.fontSize = size; t.fontStyle = style;
            t.color = c;    t.alignment = anchor; t.raycastTarget = false;
            t.font  = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ──────────────────────────────────────────────────────────
        //  Утилиты
        // ──────────────────────────────────────────────────────────

        void ClearActive()
        {
            _held = null;
            _ghost.SetActive(false);
            foreach (var s in _active)
            {
                s.Tween?.Kill();
                if (s.Rt != null) Destroy(s.Rt.gameObject);
            }
            _active.Clear();
        }

        void AnimateOut(List<CardSlot> slots)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                s.Tween?.Kill();
                float delay = (slots.Count - 1 - i) * (StaggerDelay * 0.5f);
                var rt = s.Rt;
                if (rt == null) continue;
                var seq = DOTween.Sequence();
                if (delay > 0f) seq.AppendInterval(delay);
                seq.Append(rt.DOAnchorPosY(-CardH - 60f, AnimOut).SetEase(Ease.InCubic))
                   .OnComplete(() => { if (rt != null) Destroy(rt.gameObject); });
                s.Tween = seq.Play();
            }
            float total = AnimOut + slots.Count * StaggerDelay * 0.5f + 0.05f;
            DOVirtual.DelayedCall(total, () =>
            {
                if (_state == DeckState.Hidden && _canvas != null)
                    _canvas.gameObject.SetActive(false);
            });
            _active.Clear();
        }

        void UpdateDeckPosition()
        {
            if (_deckRoot == null || _canvas == null) return;
            float px = _hud != null ? _hud.BottomPanelTopPx : 0f;
            float cs = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _deckRoot.anchoredPosition = new Vector2(0f, px / cs + 8f);
        }

        // ──────────────────────────────────────────────────────────
        //  Update / ввод
        // ──────────────────────────────────────────────────────────

        void Update()
        {
            if (_state == DeckState.Hidden || _canvas == null || !_canvas.gameObject.activeSelf) return;
            UpdateDeckPosition();
            HandleInput();
        }

        void HandleInput()
        {
            Vector2 pos  = GetPointerScreen();
            bool    down = GetPointerDown();
            bool    held = GetPointerHeld();
            bool    up   = GetPointerUp();

            // Подъём при наведении (не при драге)
            if (_held == null && held)
            {
                foreach (var s in _active)
                {
                    bool near = IsNearCard(pos, s, 48f);
                    if (near && !s.Lifted) { s.Lifted = true;  s.AnimateTo(s.BaseX, s.BaseY + LiftY, s.BaseRot * 0.4f, 0.12f); }
                    else if (!near && s.Lifted) { s.Lifted = false; s.AnimateTo(s.BaseX, s.BaseY, s.BaseRot, 0.12f); }
                }
            }

            // Нажатие
            if (down && _held == null)
            {
                foreach (var s in _active)
                {
                    if (!IsOnCard(pos, s)) continue;
                    _held = s;
                    _dragStart = pos;
                    _draggingToMap = false;
                    s.AnimateTo(s.BaseX, s.BaseY + LiftY * 1.5f, 0f, 0.08f);
                    break;
                }
            }

            // Удержание
            if (_held != null && held)
            {
                if (_held.IsDraggable)
                {
                    float dy = pos.y - _dragStart.y;
                    float dx = pos.x - _dragStart.x;
                    if (!_draggingToMap && dy > DragThreshPx && Mathf.Abs(dy) > Mathf.Abs(dx) * 1.2f)
                        _draggingToMap = true;
                    if (_draggingToMap)
                    {
                        Vector2 local;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, pos, null, out local);
                        _held.Rt.anchoredPosition = new Vector2(_held.BaseX, Mathf.Max(_held.BaseY, local.y - CardH * 0.5f));
                        _held.Rt.localEulerAngles = Vector3.zero;
                        UpdateGhost(pos, _held);
                    }
                }
            }

            // Отпускание
            if (up && _held != null)
            {
                var slot = _held;
                _held = null;
                _ghost.SetActive(false);

                if (_draggingToMap && slot.IsDraggable)
                {
                    _draggingToMap = false;
                    PlaceBuilding(pos, slot.BldDef);
                }
                else if (!_draggingToMap)
                {
                    OnCardTapped(slot);
                }

                if (slot.Rt != null)
                {
                    slot.Tween?.Kill();
                    slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, slot.BaseY), 0.22f).SetEase(Ease.OutBack);
                    slot.Rt.DOLocalRotate(new Vector3(0,0,slot.BaseRot), 0.22f).SetEase(Ease.OutBack);
                    slot.Lifted = false;
                }
            }

            // Потеря касания
            if (!held && _held != null)
            {
                var slot = _held; _held = null;
                _draggingToMap = false; _ghost.SetActive(false);
                if (slot.Rt != null)
                {
                    slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, slot.BaseY), 0.18f).SetEase(Ease.OutCubic);
                    slot.Rt.DOLocalRotate(new Vector3(0,0,slot.BaseRot), 0.18f).SetEase(Ease.OutCubic);
                    slot.Lifted = false;
                }
            }
        }

        void OnCardTapped(CardSlot slot)
        {
            if (slot.IsBackCard)
            {
                ShowCategories();
                return;
            }

            if (!slot.IsDraggable)
            {
                // Категорийная карточка
                switch (slot.CatAction)
                {
                    case CatAction.Roads:
                        HideAll();
                        RoadsRequested?.Invoke();
                        break;
                    case CatAction.Districts:
                        ShowBuildings(DistrictDefs);
                        break;
                    case CatAction.Services:
                        ShowBuildings(ServiceDefs);
                        break;
                    case CatAction.Energy:
                        ShowBuildings(EnergyDefs);
                        break;
                    case CatAction.Erase:
                        HideAll();
                        EraseRequested?.Invoke();
                        break;
                }
            }
        }

        void PlaceBuilding(Vector2 screen, BldDef def)
        {
            if (_cam == null || _placer == null) return;
            Vector3 w = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
            var cell = new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
            _placer.TryPlaceAt(cell, def.Category, def.Kind);
        }

        // ──────────────────────────────────────────────────────────
        //  Ghost
        // ──────────────────────────────────────────────────────────

        void UpdateGhost(Vector2 screen, CardSlot slot)
        {
            if (_cam == null) return;
            Vector3 w = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
            var cell = new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
            bool ok = _placer != null &&
                      _placer.CheckPlacement(cell, slot.BldDef.Category, slot.BldDef.Kind) == PlaceResult.Ok;
            _ghost.SetActive(true);
            _ghost.transform.position = new Vector3((cell.x+0.5f)*CellSize, (cell.y+0.5f)*CellSize, -0.5f);
            var d = slot.BldDef.Art;
            _ghostMr.material.color = ok
                ? new Color(d.r/255f, d.g/255f, d.b/255f, 0.55f)
                : new Color(1f, 0.15f, 0.15f, 0.45f);
        }

        // ──────────────────────────────────────────────────────────
        //  Hit-testing
        // ──────────────────────────────────────────────────────────

        bool IsOnCard(Vector2 screen, CardSlot slot)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local)) return false;
            float cx = slot.BaseX;
            float cy = (slot.Rt != null ? slot.Rt.anchoredPosition.y : slot.BaseY) + CardH * 0.5f;
            return Mathf.Abs(local.x - cx) < CardW * 0.52f && Mathf.Abs(local.y - cy) < CardH * 0.52f;
        }

        bool IsNearCard(Vector2 screen, CardSlot slot, float margin)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local)) return false;
            return Mathf.Abs(local.x - slot.BaseX) < CardW * 0.5f + margin * 0.35f
                && local.y > slot.BaseY - 20f && local.y < slot.BaseY + CardH + margin;
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
            if (Input.touchCount > 0) { var ph = Input.GetTouch(0).phase; return ph == TouchPhase.Stationary || ph == TouchPhase.Moved; }
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
