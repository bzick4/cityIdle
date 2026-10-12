using CityGame.CellRoads;
using CityGame.Cars;
using CityGame.Flow;
using System.Collections.Generic;
using CityGame.Roads;
using CityGame.Save;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    public enum Tool
    {
        Pan,
        Road,
        Highway,
        OneWay,
        Junction,
        Residential,
        Commercial,
        Industrial,
        Service,
        Erase,
        Expand
    }

    /// <summary>
    /// Точка входа. Повесьте на тот же объект, что и Road System.
    /// Создаёт модель города, экономику, дороги по клеткам, секторы, расчёт потоков и интерфейс.
    /// </summary>
    [RequireComponent(typeof(RoadSystem))]
    public sealed class CitySystem : MonoBehaviour
    {
        [Tooltip("Размер клетки. Одна клетка это одна постройка: дорога или здание")]
        public float CellSize = 1f;
        [Tooltip("Расстояние от центра сектора до угла")]
        public float HexSize = 5f;

        public CityModel Model { get; private set; }
        public Economy Economy { get; private set; }
        public BuildingPlacer Placer { get; private set; }
        public SectorMap Sectors { get; private set; }
        public SectorTapper Tapper { get; private set; }
        public FlowSolver Flow { get; private set; }
        public FlowHeatmap Heatmap { get; private set; }
        public CameraRig Rig { get; private set; }
        public CellRoadMap RoadMap { get; private set; }
        public CellRoadDrawer RoadDrawer { get; private set; }
        public HighwayLaneTool HighwayTool { get; private set; }
        public JunctionTapper JunctionTool { get; private set; }
        public Biomes Biomes { get; private set; }
        public ServiceCoverage Coverage { get; private set; }
        public ServiceOverlay ServiceZones { get; private set; }
        public CarSystem Traffic { get; private set; }
        public ServiceKind CurrentService { get; private set; } = ServiceKind.Ambulance;
        public RoadGraph RoadNetwork { get; private set; }
        public Tool CurrentTool { get; private set; } = Tool.Pan;
        public CityTutorial Tutorial => _tutorial;

        [Tooltip("Как часто сохранять город, секунд")]
        public float AutosaveSeconds = 20f;

        public bool Started { get; private set; }

        RoadSystem _roads;
        CityDebugHud _hud;
        CityTutorial _tutorial;
        MainMenu _menu;
        CellRoadGraphSync _graphSync;
        BiomeMap _biomeMap;
        readonly List<Component> _added = new List<Component>();
        int _baseChildren;
        float _saveTimer;

        void Start()
        {
            // Игра вертикальная. В редакторе настройка игнорируется, там выбери портретный размер окна Game.
            Screen.orientation = ScreenOrientation.Portrait;
            _baseChildren = transform.childCount;
            ShowMenu();
        }

        // --- меню, новая игра, загрузка ---

        void ShowMenu()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color32(0x2B, 0x31, 0x3A, 0xFF);
            }

            _menu = gameObject.AddComponent<MainMenu>();
            _menu.Bind(this);
        }

        void CloseMenu()
        {
            if (_menu != null) Destroy(_menu);
            _menu = null;
        }

        /// <summary>Новый город со случайным рельефом. Старое сохранение перезаписывается сразу.</summary>
        public void NewGame()
        {
            if (Started) return;
            bool shouldTutorial = (!SaveSystem.Exists && !CityTutorial.IsCompleted) || CityTutorial.IsInProgress;
            CloseMenu();
            SaveSystem.Delete();
            StartGame(null, Random.Range(1, 1000000));
            Save();
            if (shouldTutorial) _tutorial.BeginOrResume();
        }

        public void ContinueGame()
        {
            if (Started) return;
            if (!SaveSystem.TryRead(out SaveData data)) return;
            CloseMenu();
            StartGame(data, data.seed);
            if (CityTutorial.IsInProgress) _tutorial.BeginOrResume();
        }

        /// <summary>Сохраняет и возвращается в меню.</summary>
        public void ExitToMenu()
        {
            if (!Started) return;
            Save();

            foreach (Component c in _added)
                if (c != null) Destroy(c);
            _added.Clear();
            for (int i = transform.childCount - 1; i >= _baseChildren; i--)
                Destroy(transform.GetChild(i).gameObject);

            Started = false;
            Model = null;
            RoadMap = null;
            ShowMenu();
        }

        public void Save()
        {
            if (!Started) return;
            SaveSystem.Write(Capture());
            _saveTimer = 0f;
        }

        SaveData Capture()
        {
            var data = new SaveData { seed = _biomeMap.Seed, money = Economy.Money };
            if (Rig != null && Rig.Cam != null)
            {
                data.camX = Rig.Cam.transform.position.x;
                data.camY = Rig.Cam.transform.position.y;
                data.camWidth = Rig.WorldWidth;
            }

            foreach (HexCoord h in Sectors.Open) data.openSectors.Add(new HexSave { q = h.Q, r = h.R });

            var types = new Dictionary<Vector2Int, CellRoads.JunctionType>();
            foreach (var kv in RoadMap.ExplicitTypes) types[kv.Key] = kv.Value;
            foreach (var kv in RoadMap.Cells)
            {
                types.TryGetValue(kv.Key, out CellRoads.JunctionType t);
                data.roads.Add(new RoadCellSave { x = kv.Key.x, y = kv.Key.y, mask = kv.Value, type = (int)t });
            }

            foreach (var kv in RoadMap.HighwayDirections)
                data.highway.Add(new HighwayCellSave { x = kv.Key.x, y = kv.Key.y, heading = kv.Value,
                    kind = (RoadMap.IsRampAt(kv.Key) ? 1 : 0) |
                           (RoadMap.IsStandaloneHighwayAt(kv.Key) ? 2 : 0) |
                           (RoadMap.IsOwnedHighwayAt(kv.Key) ? 4 : 0),
                    originalMask = RoadMap.StandaloneOriginalMaskAt(kv.Key) });

            foreach (BuildingCell c in Model.Cells)
                data.buildings.Add(new BuildingSave
                {
                    x = c.Pos.x, y = c.Pos.y, category = (int)c.Category, kind = (int)c.Kind, serial = c.Serial
                });

            return data;
        }

        void Apply(SaveData data)
        {
            var open = new List<HexCoord>();
            foreach (HexSave h in data.openSectors) open.Add(new HexCoord(h.q, h.r));
            Sectors.Load(open);

            var masks = new Dictionary<Vector2Int, int>();
            var types = new Dictionary<Vector2Int, CellRoads.JunctionType>();
            var highway = new Dictionary<Vector2Int, int>();
            var ramps = new HashSet<Vector2Int>();
            var standaloneHighways = new HashSet<Vector2Int>();
            var ownedHighways = new HashSet<Vector2Int>();
            var highwayOriginalMasks = new Dictionary<Vector2Int, int>();
            foreach (RoadCellSave r in data.roads)
            {
                var cell = new Vector2Int(r.x, r.y);
                masks[cell] = r.mask;
                if (r.type != 0) types[cell] = (CellRoads.JunctionType)r.type;
            }

            if (data.highway != null)
                foreach (HighwayCellSave h in data.highway)
                    if (h != null)
                    {
                        var cell = new Vector2Int(h.x, h.y);
                        highway[cell] = h.heading;
                        if ((h.kind & 1) != 0) ramps.Add(cell);
                        if ((h.kind & 2) != 0) standaloneHighways.Add(cell);
                        if ((h.kind & 4) != 0) ownedHighways.Add(cell);
                        if ((h.kind & 2) != 0) highwayOriginalMasks[cell] = h.originalMask;
                    }

            RoadMap.Load(masks, types, highway, ramps, standaloneHighways, ownedHighways, highwayOriginalMasks);

            var cells = new List<BuildingCell>();
            foreach (BuildingSave b in data.buildings)
                cells.Add(new BuildingCell(new Vector2Int(b.x, b.y), (BuildingCategory)b.category, (ServiceKind)b.kind)
                    { Serial = b.serial });
            Model.Load(cells);

            Economy.Money = data.money;
            if (data.camWidth > 0f) Rig.SetView(new Vector2(data.camX, data.camY), data.camWidth);
        }

        void Update()
        {
            if (!Started) return;
            _saveTimer += Time.unscaledDeltaTime;
            if (_saveTimer >= AutosaveSeconds) Save();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        void OnApplicationQuit()
        {
            Save();
        }

        void StartGame(SaveData data, int seed)
        {
            _roads = GetComponent<RoadSystem>();
            // Старое рисование дорог от руки отключено: дороги теперь по клеткам.
            _roads.Drawer.enabled = false;

            Model = new CityModel();
            RoadMap = new CellRoadMap();
            RoadNetwork = new RoadGraph();
            _graphSync = new CellRoadGraphSync(RoadMap, RoadNetwork, CellSize);

            Sectors = new SectorMap(HexSize);
            _biomeMap = new BiomeMap { Seed = seed };
            Biomes = new Biomes(_biomeMap, Sectors, Model, RoadMap, CellSize);

            Coverage = new ServiceCoverage(Model);
            Economy = GetOrAdd<Economy>();
            Economy.Bind(Model, Coverage, Biomes);

            ServiceZones = GetOrAdd<ServiceOverlay>();
            ServiceZones.CellSize = CellSize;
            ServiceZones.Bind(Model, Coverage);

            var roadRenderer = GetOrAdd<CellRoadRenderer>();
            roadRenderer.CellSize = CellSize;
            roadRenderer.Bind(RoadMap);

            var sectorRenderer = GetOrAdd<SectorRenderer>();
            sectorRenderer.CellSize = CellSize;
            sectorRenderer.Bind(Sectors, Biomes.Map);
            var trees = GetOrAdd<TreeRenderer>();
            trees.CellSize = CellSize;
            trees.Drops = sectorRenderer;
            sectorRenderer.DropFinished += trees.Redraw;
            trees.Bind(Biomes, Sectors);
            Tapper = GetOrAdd<SectorTapper>();
            Tapper.Bind(Sectors, sectorRenderer);

            Placer = GetOrAdd<BuildingPlacer>();
            Placer.CellSize = CellSize;
            Placer.Bind(Model, Economy, RoadMap, Biomes);
            Placer.CanPlaceAt = Sectors.IsOpenAt;

            RoadDrawer = GetOrAdd<CellRoadDrawer>();
            RoadDrawer.CellSize = CellSize;
            RoadDrawer.Bind(RoadMap, Model, Sectors, Economy);
            RoadDrawer.CostFactorAt = Biomes.RoadCostFactor;
            HighwayTool = GetOrAdd<HighwayLaneTool>();
            HighwayTool.CellSize = CellSize;
            HighwayTool.Bind(RoadMap, Model, Sectors, Economy);
            HighwayTool.CostFactorAt = Biomes.RoadCostFactor;
            var bridges = GetOrAdd<BridgeRenderer>();
            bridges.CellSize = CellSize;
            bridges.Bind(RoadMap, Biomes);

            JunctionTool = GetOrAdd<JunctionTapper>();
            JunctionTool.CellSize = CellSize;
            JunctionTool.Bind(RoadMap, Economy);

            Flow = GetOrAdd<FlowSolver>();
            Flow.CellSizeScale = CellSize;
            Flow.Bind(RoadNetwork, Model);

            // Здания рисуем после потоков: свечение района зависит от довольства.
            var cityRenderer = GetOrAdd<CityRenderer>();
            cityRenderer.CellSize = CellSize;
            cityRenderer.Bind(Model, Flow);

            Traffic = GetOrAdd<CarSystem>();
            Traffic.CellSize = CellSize;
            Traffic.Bind(Flow, RoadNetwork, RoadMap, Model);
            Heatmap = GetOrAdd<FlowHeatmap>();
            Heatmap.Bind(Flow, RoadNetwork);

            Rig = GetOrAdd<CameraRig>();

            _hud = GetOrAdd<CityDebugHud>();
            _hud.Bind(this);
            _tutorial = GetOrAdd<CityTutorial>();
            _tutorial.Bind(this);

            RoadDrawer.IsPointerBlocked = _hud.IsPointerBlocked;
            HighwayTool.IsPointerBlocked = _hud.IsPointerBlocked;
            Placer.IsPointerBlocked = _hud.IsPointerBlocked;
            JunctionTool.IsPointerBlocked = _hud.IsPointerBlocked;
            Tapper.IsPointerBlocked = _hud.IsPointerBlocked;
            Rig.IsPointerBlocked = _hud.IsPointerBlocked;

            Started = true;
            if (data != null) Apply(data);

            // Старт без инструмента: палец двигает карту. Повторный тап по активному инструменту возвращает сюда.
            Select(Tool.Pan);
        }

        public void SelectService(ServiceKind kind)
        {
            CurrentService = kind;
            Placer.Kind = kind;
        }

        T GetOrAdd<T>() where T : Component
        {
            var c = GetComponent<T>();
            if (c != null) return c;
            c = gameObject.AddComponent<T>();
            _added.Add(c);
            return c;
        }

        public void Select(Tool tool)
        {
            CurrentTool = tool;
            bool build = tool >= Tool.Residential && tool <= Tool.Erase;

            RoadDrawer.enabled = tool == Tool.Road;
            HighwayTool.enabled = tool == Tool.Highway || tool == Tool.OneWay;
            HighwayTool.OneWayMode = tool == Tool.OneWay;
            JunctionTool.enabled = tool == Tool.Junction;
            Placer.enabled = build;
            Tapper.enabled = tool == Tool.Expand;
            Rig.PanEnabled = tool == Tool.Pan;

            ServiceZones.Emphasis = tool == Tool.Service;
            Placer.EraseMode = tool == Tool.Erase;
            switch (tool)
            {
                case Tool.Residential: Placer.Category = BuildingCategory.Residential; break;
                case Tool.Commercial: Placer.Category = BuildingCategory.Commercial; break;
                case Tool.Industrial: Placer.Category = BuildingCategory.Industrial; break;
                case Tool.Service:
                    Placer.Category = BuildingCategory.Service;
                    Placer.Kind = CurrentService;
                    break;
            }
        }
    }
}
