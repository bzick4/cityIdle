using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace CityGame.City
{
    using Buildings;

    /// <summary>
    /// Карточная колода зданий: веер внизу экрана.
    /// Зажать — карта поднимается. Свайп вдоль колоды — волна подъёма.
    /// Потянуть вверх на сцену — гост на карте, отпустить — размещение.
    /// </summary>
    public sealed class BuildingCardDeck : MonoBehaviour
    {
        public float CellSize = 1f;

        BuildingPlacer _placer;
        CityDebugHud _hud;
        Canvas _canvas;
        RectTransform _deckRoot;

        readonly List<CardSlot> _slots = new List<CardSlot>();
        CardSlot _held;
        bool _wantVisible;
        bool _draggingToMap;
        Vector2 _dragStart;

        GameObject _ghost;
        MeshRenderer _ghostMr;
        Camera _cam;

        // Карточка: 80×115 canvas-units, шаг 62 → 8 карт = 7×62+80 = 514 px
        const float CardW       = 80f;
        const float CardH       = 115f;
        const float CardStep    = 62f;   // шаг между центрами
        const float MaxRotDeg   = 10f;   // максимальный поворот крайней карты
        const float ArcDrop     = 14f;   // насколько крайние карты ниже центральных
        const float DeckYBase   = 10f;   // зазор под картами (в deckRoot local)
        const float LiftY       = 30f;
        const float DragThreshPx = 55f;

        static readonly Color32[] ArtColors =
        {
            new Color32(0x4C, 0xAF, 0x50, 0xFF),
            new Color32(0x21, 0x96, 0xF3, 0xFF),
            new Color32(0xFF, 0x98, 0x00, 0xFF),
            new Color32(0xF4, 0x43, 0x36, 0xFF),
            new Color32(0x3F, 0x51, 0xB5, 0xFF),
            new Color32(0xFF, 0x57, 0x22, 0xFF),
            new Color32(0xFF, 0xC1, 0x07, 0xFF),
            new Color32(0x00, 0xBC, 0xD4, 0xFF),
        };

        struct CardDef
        {
            public BuildingCategory Category;
            public ServiceKind Kind;
            public string Title;
            public string Cost;
            public Color32 Art;
        }

        static CardDef[] BuildDefs(Economy eco) => new[]
        {
            new CardDef { Category=BuildingCategory.Residential, Title="Жильё",     Cost=eco.BuildCost[0]+"₿", Art=ArtColors[0] },
            new CardDef { Category=BuildingCategory.Commercial,  Title="Торговля",  Cost=eco.BuildCost[1]+"₿", Art=ArtColors[1] },
            new CardDef { Category=BuildingCategory.Industrial,  Title="Произв.",   Cost=eco.BuildCost[2]+"₿", Art=ArtColors[2] },
            new CardDef { Category=BuildingCategory.Service, Kind=ServiceKind.Ambulance,  Title="Скорая",   Cost=eco.CostOf(BuildingCategory.Service,ServiceKind.Ambulance)+"₿",  Art=ArtColors[3] },
            new CardDef { Category=BuildingCategory.Service, Kind=ServiceKind.Police,     Title="Полиция",  Cost=eco.CostOf(BuildingCategory.Service,ServiceKind.Police)+"₿",     Art=ArtColors[4] },
            new CardDef { Category=BuildingCategory.Service, Kind=ServiceKind.Fire,       Title="Пожарные", Cost=eco.CostOf(BuildingCategory.Service,ServiceKind.Fire)+"₿",       Art=ArtColors[5] },
            new CardDef { Category=BuildingCategory.Service, Kind=ServiceKind.PowerPlant, Title="Электро",  Cost=eco.CostOf(BuildingCategory.Service,ServiceKind.PowerPlant)+"₿", Art=ArtColors[6] },
            new CardDef { Category=BuildingCategory.Service, Kind=ServiceKind.WaterTower, Title="Водобашня",Cost=eco.CostOf(BuildingCategory.Service,ServiceKind.WaterTower)+"₿", Art=ArtColors[7] },
        };

        class CardSlot
        {
            public CardDef Def;
            public RectTransform Rt;
            public float BaseX;
            public float BaseY;
            public float BaseRot;
            public bool Lifted;
            public DG.Tweening.Tween Tween;

            public void AnimateTo(float y, float rot, float dur = 0.12f)
            {
                Tween?.Kill();
                Tween = DOTween.Sequence()
                    .Append(Rt.DOAnchorPosY(y, dur).SetEase(Ease.OutQuad))
                    .Join(Rt.DOLocalRotate(new Vector3(0, 0, rot), dur).SetEase(Ease.OutQuad))
                    .Play();
            }
        }

        // ──────────────────────── public API ────────────────────────

        public void Bind(BuildingPlacer placer, Economy economy, CityDebugHud hud = null)
        {
            _placer = placer;
            _hud   = hud;
            _cam   = Camera.main;
            BuildUI(economy);
            BuildGhost();
        }

        public void SetVisible(bool v)
        {
            _wantVisible = v;
            RefreshVisible();
        }

        void RefreshVisible()
        {
            bool hudOpen = _hud != null && _hud.ToolsOpen;
            if (_canvas != null)
                _canvas.gameObject.SetActive(_wantVisible && !hudOpen);
        }

        // ──────────────────────── построение UI ────────────────────────

        void BuildUI(Economy eco)
        {
            var go = new GameObject("CardDeck_Canvas");
            go.transform.SetParent(transform, false);

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390f, 844f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            // deckRoot — анкор по нижнему центру
            var deckGo = new GameObject("DeckRoot");
            deckGo.transform.SetParent(go.transform, false);
            _deckRoot = deckGo.AddComponent<RectTransform>();
            _deckRoot.anchorMin = new Vector2(0.5f, 0f);
            _deckRoot.anchorMax = new Vector2(0.5f, 0f);
            _deckRoot.pivot     = new Vector2(0.5f, 0f);

            CardDef[] defs = BuildDefs(eco);
            int n = defs.Length;
            float totalW = (n - 1) * CardStep + CardW;
            float startX = -totalW * 0.5f + CardW * 0.5f;
            float halfN  = (n - 1) * 0.5f;  // середина индекса

            for (int i = 0; i < n; i++)
            {
                float t       = (i - halfN) / halfN;          // [-1, +1]
                float baseX   = startX + i * CardStep;
                float baseY   = DeckYBase - ArcDrop * (t * t); // дуга: центр выше
                float baseRot = -MaxRotDeg * t;                // левые наклонены влево

                var slot = new CardSlot
                {
                    Def     = defs[i],
                    BaseX   = baseX,
                    BaseY   = baseY,
                    BaseRot = baseRot
                };
                slot.Rt = BuildCard(defs[i], baseX, baseY, baseRot, i);
                _slots.Add(slot);
            }

            // Размер deckRoot — достаточно для всех карт
            float deckH = CardH + DeckYBase + ArcDrop + 20f;
            _deckRoot.sizeDelta = new Vector2(totalW + CardW, deckH);
            _deckRoot.anchoredPosition = new Vector2(0f, 0f);
        }

        RectTransform BuildCard(CardDef def, float x, float y, float rot, int order)
        {
            var go = new GameObject("Card_" + def.Title);
            go.transform.SetParent(_deckRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(CardW, CardH);
            rt.anchoredPosition    = new Vector2(x, y);
            rt.localEulerAngles    = new Vector3(0f, 0f, rot);
            rt.SetSiblingIndex(order);  // порядок отрисовки

            // Тень
            AddRect(go.transform, "Shadow", new Color(0,0,0,0.18f), new Vector2(3f,-3f),
                new Vector2(CardW-2f, CardH-2f));

            // Рамка-фон (коричневый, на всю карту)
            AddRect(go.transform, "Frame", new Color32(0x7A, 0x60, 0x42, 0xFF),
                Vector2.zero, new Vector2(CardW, CardH));

            // Основной фон карты (кремовый, 2px внутрь рамки)
            AddRect(go.transform, "Bg", new Color32(0xF5, 0xF2, 0xEC, 0xFF),
                Vector2.zero, new Vector2(CardW-4f, CardH-4f));

            // Арт-зона (верхние 60%)
            var artGo = new GameObject("Art");
            artGo.transform.SetParent(go.transform, false);
            var artRt = artGo.AddComponent<RectTransform>();
            artRt.anchorMin = new Vector2(0f, 0.38f);
            artRt.anchorMax = Vector2.one;
            artRt.offsetMin = new Vector2(3f, 0f);
            artRt.offsetMax = new Vector2(-3f, -3f);
            var artImg = artGo.AddComponent<Image>();
            artImg.color = def.Art;

            // Название на арте
            AddLabel(artGo.transform, "Name", def.Title, 9, FontStyle.Bold,
                Color.white, TextAnchor.LowerCenter,
                new Vector2(0f,0f), new Vector2(1f,0.3f));

            // Нижняя зона (стоимость)
            var botGo = new GameObject("Bot");
            botGo.transform.SetParent(go.transform, false);
            var botRt = botGo.AddComponent<RectTransform>();
            botRt.anchorMin = new Vector2(0f,0f);
            botRt.anchorMax = new Vector2(1f,0.38f);
            botRt.offsetMin = new Vector2(3f,3f);
            botRt.offsetMax = new Vector2(-3f,0f);
            var botImg = botGo.AddComponent<Image>();
            botImg.color = new Color32(0xE8, 0xE3, 0xD8, 0xFF);

            AddLabel(botGo.transform, "Cost", def.Cost, 8, FontStyle.Normal,
                new Color32(0x44,0x33,0x22,0xFF), TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one);

            return rt;
        }

        static RectTransform AddRect(Transform parent, string name, Color color, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pivot;
            go.AddComponent<Image>().color = color;
            return rt;
        }

        static void AddLabel(Transform parent, string name, string text, int size, FontStyle style,
            Color color, TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = new Vector2(2f,1f); rt.offsetMax = new Vector2(-2f,-1f);
            var t = go.AddComponent<Text>();
            t.text = text; t.fontSize = size; t.fontStyle = style;
            t.color = color; t.alignment = anchor;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.raycastTarget = false;
        }

        // ──────────────────────── гост ────────────────────────

        void BuildGhost()
        {
            _ghost = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _ghost.name = "BuildGhost";
            _ghost.transform.localScale = new Vector3(CellSize * 0.9f, CellSize * 0.9f, 1f);
            Destroy(_ghost.GetComponent<MeshCollider>());
            _ghostMr = _ghost.GetComponent<MeshRenderer>();
            _ghostMr.material = new Material(Shader.Find("Unlit/Color"));
            _ghost.SetActive(false);
        }

        void UpdateGhost(Vector2 screen, CardSlot slot)
        {
            if (_cam == null) return;
            Vector3 w = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
            Vector2Int cell = new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
            bool ok = _placer != null && _placer.CheckPlacement(cell, slot.Def.Category, slot.Def.Kind) == PlaceResult.Ok;
            _ghost.SetActive(true);
            _ghost.transform.position = new Vector3((cell.x+0.5f)*CellSize, (cell.y+0.5f)*CellSize, -0.5f);
            Color c = ok
                ? new Color(slot.Def.Art.r/255f, slot.Def.Art.g/255f, slot.Def.Art.b/255f, 0.55f)
                : new Color(1f, 0.15f, 0.15f, 0.45f);
            _ghostMr.material.color = c;
        }

        // ──────────────────────── Update ────────────────────────

        void Update()
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) return;
            RefreshVisible();
            if (!_canvas.gameObject.activeSelf) return;

            UpdateDeckPosition();
            HandleHover();
            HandleTouch();
        }

        void UpdateDeckPosition()
        {
            if (_deckRoot == null || _canvas == null) return;
            float bottomPx = _hud != null ? _hud.BottomPanelTopPx : 0f;
            float cs = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _deckRoot.anchoredPosition = new Vector2(0f, bottomPx / cs + 8f);
        }

        // ──────────────────────── ввод ────────────────────────

        void HandleHover()
        {
            if (_held != null) return;
            Vector2 pos = GetPointerScreen();
            bool pressing = GetPointerHeld();

            foreach (var slot in _slots)
            {
                bool near = pressing && IsNearCard(pos, slot, 50f);
                if (near && !slot.Lifted)
                {
                    slot.Lifted = true;
                    slot.AnimateTo(slot.BaseY + LiftY, slot.BaseRot * 0.5f);
                }
                else if (!near && slot.Lifted)
                {
                    slot.Lifted = false;
                    slot.AnimateTo(slot.BaseY, slot.BaseRot);
                }
            }
        }

        void HandleTouch()
        {
            Vector2 pos = GetPointerScreen();
            bool down = GetPointerDown();
            bool held = GetPointerHeld();
            bool up   = GetPointerUp();

            if (down && _held == null)
            {
                foreach (var slot in _slots)
                {
                    if (IsOnCard(pos, slot))
                    {
                        _held = slot;
                        _dragStart = pos;
                        _draggingToMap = false;
                        slot.AnimateTo(slot.BaseY + LiftY * 1.5f, 0f, 0.08f);
                        break;
                    }
                }
            }

            if (_held != null && held)
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

            if (up && _held != null)
            {
                var slot = _held;
                _held = null;
                _ghost.SetActive(false);

                if (_draggingToMap)
                {
                    _draggingToMap = false;
                    if (_cam != null)
                    {
                        Vector3 w = _cam.ScreenToWorldPoint(new Vector3(pos.x, pos.y, -_cam.transform.position.z));
                        Vector2Int cell = new Vector2Int(
                            Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
                        _placer?.TryPlaceAt(cell, slot.Def.Category, slot.Def.Kind);
                    }
                }

                slot.Tween?.Kill();
                slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, slot.BaseY), 0.22f).SetEase(Ease.OutBack);
                slot.Rt.DOLocalRotate(new Vector3(0,0,slot.BaseRot), 0.22f).SetEase(Ease.OutBack);
                slot.Lifted = false;
            }

            if (!held && _held != null)
            {
                var slot = _held; _held = null;
                _draggingToMap = false; _ghost.SetActive(false);
                slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, slot.BaseY), 0.18f).SetEase(Ease.OutCubic);
                slot.Rt.DOLocalRotate(new Vector3(0,0,slot.BaseRot), 0.18f).SetEase(Ease.OutCubic);
                slot.Lifted = false;
            }
        }

        // ──────────────────────── хелперы ввода ────────────────────────

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

        bool IsOnCard(Vector2 screen, CardSlot slot)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local)) return false;
            float cx = slot.BaseX, cy = slot.Rt.anchoredPosition.y + CardH * 0.5f;
            return Mathf.Abs(local.x - cx) < CardW * 0.5f && Mathf.Abs(local.y - cy) < CardH * 0.5f;
        }

        bool IsNearCard(Vector2 screen, CardSlot slot, float margin)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local)) return false;
            float cx = slot.BaseX;
            return Mathf.Abs(local.x - cx) < CardW * 0.5f + margin * 0.4f
                && local.y > slot.BaseY - 20f && local.y < slot.BaseY + CardH + margin;
        }

        void OnDestroy()
        {
            if (_ghost != null) Destroy(_ghost);
        }
    }
}
