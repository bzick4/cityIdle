using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace CityGame.City
{
    using Buildings;

    /// <summary>
    /// Карточная колода для выбора здания. Карты разложены веером внизу экрана.
    /// Зажать карту — она приподнимается. Провести пальцем по колоде — карты
    /// приподнимаются по очереди. Перетащить карту вверх на карту — здание
    /// размещается в точке отпускания, карта возвращается в колоду.
    /// </summary>
    public sealed class BuildingCardDeck : MonoBehaviour
    {
        public float CellSize = 1f;

        BuildingPlacer _placer;
        Canvas _canvas;
        RectTransform _deckRoot;

        readonly List<CardSlot> _slots = new List<CardSlot>();
        CardSlot _held;         // карта под пальцем
        bool _draggingToMap;    // фаза «тащим на карту»
        Vector2 _dragStart;
        int _fingerId = -1;

        // Гост-объект на сцене
        GameObject _ghost;
        MeshRenderer _ghostMr;
        Vector2Int _ghostCell;
        bool _ghostValid;

        Camera _cam;

        const float CardW = 120f;
        const float CardH = 170f;
        const float CardSpacing = 86f;   // горизонтальный шаг между картами
        const float DeckY = 100f;        // высота нижней грани колоды над низом экрана
        const float LiftY = 28f;         // подъём при наведении
        const float DragThreshold = 60f; // сколько пикселей вверх до перехода в режим drag-to-map

        // Цвета арт-зоны карты
        static readonly Color32[] ArtColors =
        {
            new Color32(0x4C, 0xAF, 0x50, 0xFF), // Residential — зелёный
            new Color32(0x21, 0x96, 0xF3, 0xFF), // Commercial  — синий
            new Color32(0xFF, 0x98, 0x00, 0xFF), // Industrial  — оранжевый
            new Color32(0xF4, 0x43, 0x36, 0xFF), // Ambulance
            new Color32(0x3F, 0x51, 0xB5, 0xFF), // Police
            new Color32(0xFF, 0x57, 0x22, 0xFF), // Fire
            new Color32(0xFF, 0xEB, 0x3B, 0xFF), // PowerPlant
            new Color32(0x00, 0xBC, 0xD4, 0xFF), // WaterTower
        };

        struct CardDef
        {
            public BuildingCategory Category;
            public ServiceKind Kind;
            public bool IsService;
            public string Title;
            public string Sub;
            public Color32 Art;
        }

        static CardDef[] BuildDefs(Economy eco)
        {
            return new[]
            {
                new CardDef { Category = BuildingCategory.Residential, Title = "Жильё",    Sub = "+" + eco.BuildCost[0] + "₿",   Art = ArtColors[0] },
                new CardDef { Category = BuildingCategory.Commercial,  Title = "Торговля", Sub = "+" + eco.BuildCost[1] + "₿",   Art = ArtColors[1] },
                new CardDef { Category = BuildingCategory.Industrial,  Title = "Произв.",  Sub = "+" + eco.BuildCost[2] + "₿",   Art = ArtColors[2] },
                new CardDef { Category = BuildingCategory.Service, IsService = true, Kind = ServiceKind.Ambulance, Title = "Скорая",    Sub = ServiceSpecs.Title(ServiceKind.Ambulance),    Art = ArtColors[3] },
                new CardDef { Category = BuildingCategory.Service, IsService = true, Kind = ServiceKind.Police,    Title = "Полиция",   Sub = ServiceSpecs.Title(ServiceKind.Police),       Art = ArtColors[4] },
                new CardDef { Category = BuildingCategory.Service, IsService = true, Kind = ServiceKind.Fire,      Title = "Пожарные",  Sub = ServiceSpecs.Title(ServiceKind.Fire),         Art = ArtColors[5] },
                new CardDef { Category = BuildingCategory.Service, IsService = true, Kind = ServiceKind.PowerPlant, Title = "Электро",  Sub = "р.30кл",                                    Art = ArtColors[6] },
                new CardDef { Category = BuildingCategory.Service, IsService = true, Kind = ServiceKind.WaterTower, Title = "Водо-башня", Sub = "р.30кл",                                  Art = ArtColors[7] },
            };
        }

        class CardSlot
        {
            public CardDef Def;
            public RectTransform Rt;
            public float BaseX;   // позиция в колоде (deckRoot local)
            public float BaseY => 0f;
            public bool Lifted;
            public Tweener LiftTween;

            public void SetLift(float y, float dur = 0.12f)
            {
                LiftTween?.Kill();
                LiftTween = Rt.DOAnchorPosY(y, dur).SetEase(Ease.OutQuad);
            }
        }

        // -------- публичный API --------

        public void Bind(BuildingPlacer placer, Economy economy)
        {
            _placer = placer;
            _cam = Camera.main;
            BuildUI(economy);
            BuildGhost();
        }

        public void SetVisible(bool v)
        {
            if (_canvas != null) _canvas.gameObject.SetActive(v);
        }

        // -------- построение UI --------

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

            // Корень колоды — зафиксирован снизу по центру
            var deckGo = new GameObject("DeckRoot");
            deckGo.transform.SetParent(go.transform, false);
            _deckRoot = deckGo.AddComponent<RectTransform>();
            _deckRoot.anchorMin = new Vector2(0.5f, 0f);
            _deckRoot.anchorMax = new Vector2(0.5f, 0f);
            _deckRoot.pivot = new Vector2(0.5f, 0f);
            _deckRoot.sizeDelta = new Vector2(600f, CardH + DeckY + 20f);
            _deckRoot.anchoredPosition = new Vector2(0f, 0f);

            CardDef[] defs = BuildDefs(eco);
            int n = defs.Length;
            float totalW = (n - 1) * CardSpacing + CardW;
            float startX = -totalW * 0.5f + CardW * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float bx = startX + i * CardSpacing;
                var slot = new CardSlot { Def = defs[i], BaseX = bx };
                slot.Rt = BuildCard(slot.Def, bx);
                _slots.Add(slot);
            }
        }

        RectTransform BuildCard(CardDef def, float x)
        {
            var go = new GameObject("Card_" + def.Title);
            go.transform.SetParent(_deckRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(CardW, CardH);
            rt.anchoredPosition = new Vector2(x, DeckY);

            // Тень
            var shadow = MakeChild(go.transform, "Shadow");
            var si = shadow.gameObject.AddComponent<Image>();
            si.color = new Color(0, 0, 0, 0.22f);
            shadow.sizeDelta = new Vector2(CardW - 4f, CardH - 4f);
            shadow.anchorMin = shadow.anchorMax = new Vector2(0.5f, 0.5f);
            shadow.pivot = new Vector2(0.5f, 0.5f);
            shadow.anchoredPosition = new Vector2(3f, -3f);

            // Фон карты
            var bg = MakeChild(go.transform, "Bg");
            var bgi = bg.gameObject.AddComponent<Image>();
            bgi.color = new Color32(0xF0, 0xEB, 0xE0, 0xFF);
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero;

            // Арт-зона (верх 62%)
            var art = MakeChild(go.transform, "Art");
            var arti = art.gameObject.AddComponent<Image>();
            arti.color = def.Art;
            art.anchorMin = new Vector2(0f, 0.38f);
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(4f, 0f);
            art.offsetMax = new Vector2(-4f, -4f);

            // Название в арт-зоне (снизу арта)
            MakeLabel(art, "ArtLabel", def.Title, 11, FontStyle.Bold,
                new Color(1f, 1f, 1f, 0.85f), TextAnchor.LowerCenter,
                new Vector2(0f, 0f), new Vector2(1f, 0.28f));

            // Нижняя полоска с подписью
            var bot = MakeChild(go.transform, "Bot");
            var boti = bot.gameObject.AddComponent<Image>();
            boti.color = new Color32(0xD6, 0xCB, 0xBA, 0xFF);
            bot.anchorMin = new Vector2(0f, 0f);
            bot.anchorMax = new Vector2(1f, 0.38f);
            bot.offsetMin = new Vector2(4f, 4f);
            bot.offsetMax = new Vector2(-4f, 0f);

            // Подзаголовок
            MakeLabel(bot, "Sub", def.Sub, 9, FontStyle.Normal,
                new Color32(0x55, 0x44, 0x33, 0xFF), TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one);

            // Рамка
            var frame = MakeChild(go.transform, "Frame");
            var fi = frame.gameObject.AddComponent<Image>();
            fi.color = new Color32(0x8B, 0x72, 0x50, 0xFF);
            fi.raycastTarget = false;
            frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero; frame.offsetMax = Vector2.zero;
            // Имитируем рамку через отдельный внутренний прямоугольник
            var inner = MakeChild(go.transform, "Inner");
            var ini = inner.gameObject.AddComponent<Image>();
            ini.color = Color.clear;
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);
            fi.raycastTarget = false;

            return rt;
        }

        static RectTransform MakeChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static void MakeLabel(RectTransform parent, string name, string text, int size, FontStyle style,
            Color color, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(2f, 2f); rt.offsetMax = new Vector2(-2f, -2f);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
        }

        // -------- гост здания --------

        void BuildGhost()
        {
            _ghost = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _ghost.name = "BuildingGhost";
            _ghost.transform.localScale = new Vector3(CellSize * 0.92f, CellSize * 0.92f, 1f);
            Destroy(_ghost.GetComponent<MeshCollider>());
            _ghostMr = _ghost.GetComponent<MeshRenderer>();
            _ghostMr.material = new Material(Shader.Find("Unlit/Color"));
            _ghost.SetActive(false);
        }

        void UpdateGhost(Vector2 screenPos, CardSlot slot)
        {
            if (_cam == null) return;
            Vector3 w = _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -_cam.transform.position.z));
            Vector2Int cell = new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
            _ghostCell = cell;

            PlaceResult pr = _placer != null
                ? _placer.CheckPlacement(cell, slot.Def.Category, slot.Def.Kind)
                : PlaceResult.Occupied;
            _ghostValid = (pr == PlaceResult.Ok);

            _ghost.SetActive(true);
            _ghost.transform.position = new Vector3((cell.x + 0.5f) * CellSize, (cell.y + 0.5f) * CellSize, -0.5f);
            Color c = _ghostValid
                ? new Color(slot.Def.Art.r / 255f, slot.Def.Art.g / 255f, slot.Def.Art.b / 255f, 0.55f)
                : new Color(1f, 0.2f, 0.2f, 0.45f);
            _ghostMr.material.color = c;
        }

        // -------- ввод --------

        void Update()
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) return;

            // Лёгкий эффект: карты реагируют на близость пальца без захвата
            HandleHoverLift();

            // Основная логика захвата и перетаскивания
            HandleTouch();
        }

        void HandleHoverLift()
        {
            // Если карта захвачена — её подъём управляется из HandleTouch
            if (_held != null) return;

            Vector2 pos = GetPointerScreen();
            if (!Input.GetMouseButton(0) && (Input.touchCount == 0))
            {
                // Убрать все подъёмы
                foreach (var s in _slots)
                    if (s.Lifted) { s.Lifted = false; s.SetLift(DeckY); }
                return;
            }

            foreach (var slot in _slots)
            {
                bool near = IsNearCard(pos, slot, 55f);
                if (near && !slot.Lifted)  { slot.Lifted = true;  slot.SetLift(DeckY + LiftY); }
                if (!near && slot.Lifted)  { slot.Lifted = false; slot.SetLift(DeckY); }
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
                // Захватить карту под пальцем
                foreach (var slot in _slots)
                {
                    if (IsOnCard(pos, slot))
                    {
                        _held = slot;
                        _dragStart = pos;
                        _draggingToMap = false;
                        slot.SetLift(DeckY + LiftY * 1.6f, 0.08f);
                        break;
                    }
                }
            }

            if (_held != null && held)
            {
                float dy = pos.y - _dragStart.y;
                float dx = pos.x - _dragStart.x;

                if (!_draggingToMap && dy > DragThreshold && Mathf.Abs(dy) > Mathf.Abs(dx) * 1.3f)
                    _draggingToMap = true;

                if (_draggingToMap)
                {
                    // Карточка следует за пальцем (только вверх, ограниченно)
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _deckRoot, pos, null, out local);
                    _held.Rt.anchoredPosition = new Vector2(_held.BaseX, Mathf.Max(DeckY, local.y - CardH * 0.5f));

                    UpdateGhost(pos, _held);
                }
            }

            if (up && _held != null)
            {
                var slot = _held;
                _held = null;

                if (_draggingToMap)
                {
                    _draggingToMap = false;
                    _ghost.SetActive(false);

                    // Место под пальцем в момент отпускания
                    if (_cam != null)
                    {
                        Vector3 w = _cam.ScreenToWorldPoint(new Vector3(pos.x, pos.y, -_cam.transform.position.z));
                        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(w.x / CellSize), Mathf.FloorToInt(w.y / CellSize));
                        _placer?.TryPlaceAt(cell, slot.Def.Category, slot.Def.Kind);
                    }
                }

                // Карта возвращается на место
                slot.LiftTween?.Kill();
                slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, DeckY), 0.22f).SetEase(Ease.OutBack);
                slot.Lifted = false;
            }

            if (!held && _held != null)
            {
                // Потеряли касание — сбросить
                var slot = _held;
                _held = null;
                _draggingToMap = false;
                _ghost.SetActive(false);
                slot.Rt.DOAnchorPos(new Vector2(slot.BaseX, DeckY), 0.18f).SetEase(Ease.OutCubic);
                slot.Lifted = false;
            }
        }

        // -------- утилиты ввода --------

        static Vector2 GetPointerScreen()
        {
#if UNITY_EDITOR
            return Input.mousePosition;
#else
            if (Input.touchCount > 0) return Input.GetTouch(0).position;
            return Input.mousePosition;
#endif
        }

        static bool GetPointerDown()
        {
#if UNITY_EDITOR
            return Input.GetMouseButtonDown(0);
#else
            if (Input.touchCount > 0) return Input.GetTouch(0).phase == TouchPhase.Began;
            return Input.GetMouseButtonDown(0);
#endif
        }

        static bool GetPointerHeld()
        {
#if UNITY_EDITOR
            return Input.GetMouseButton(0);
#else
            if (Input.touchCount > 0)
            {
                var p = Input.GetTouch(0).phase;
                return p == TouchPhase.Stationary || p == TouchPhase.Moved;
            }
            return Input.GetMouseButton(0);
#endif
        }

        static bool GetPointerUp()
        {
#if UNITY_EDITOR
            return Input.GetMouseButtonUp(0);
#else
            if (Input.touchCount > 0) return Input.GetTouch(0).phase == TouchPhase.Ended;
            return Input.GetMouseButtonUp(0);
#endif
        }

        // Проверяем попадание в карту по экранным координатам
        bool IsOnCard(Vector2 screen, CardSlot slot)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local))
                return false;
            float cx = slot.BaseX;
            float cy = slot.Rt.anchoredPosition.y + CardH * 0.5f;
            return Mathf.Abs(local.x - cx) < CardW * 0.5f && Mathf.Abs(local.y - cy) < CardH * 0.5f;
        }

        bool IsNearCard(Vector2 screen, CardSlot slot, float margin)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_deckRoot, screen, null, out local))
                return false;
            float cx = slot.BaseX;
            float cy = DeckY + CardH * 0.5f;
            return Mathf.Abs(local.x - cx) < CardW * 0.5f + margin * 0.5f && local.y > DeckY - 20f && local.y < DeckY + CardH + margin;
        }

        void OnDestroy()
        {
            if (_ghost != null) Destroy(_ghost);
        }
    }
}
