using CityGame.CellRoads;
using CityGame.Cars;
using CityGame.City;
using CityGame.Flow;
using System.Collections.Generic;
using CityGame.Roads;
using CityGame.Save;
using CityGame.Sectors;
using CityGame.Transit;
using CityGame.Water;
using CityGame.World;
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
        Expand,
        Canal,
        Port,
        Bus,
        Ramp
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
        [Tooltip("Доля ширины соседних секторов, видимая по краям при старте новой карты")]
        [Range(0f, 0.5f)] public float InitialNeighborVisibleFraction = 0.3f;

        public CityModel Model { get; private set; }
        public Economy Economy { get; private set; }
        public BuildingPlacer Placer { get; private set; }
        public SectorMap Sectors { get; private set; }
        public SectorTapper Tapper { get; private set; }
        public FlowSolver Flow { get; private set; }
        public FlowHeatmap Heatmap { get; private set; }
        public CameraRig Rig { get; private set; }
        public CellRoadMap RoadMap { get; private set; }
        /// <summary>Инструмент рисования свободных дорог (вариант 2).</summary>
        public Roads.RoadDrawer FreeRoadDrawer { get; private set; }
        public CellRoadDrawer RoadDrawer { get; private set; }
        public HighwayLaneTool HighwayTool { get; private set; }
        public JunctionTapper JunctionTool { get; private set; }
        public Biomes Biomes { get; private set; }
        public ServiceCoverage Coverage { get; private set; }
        public UtilityNet Utilities { get; private set; }
        public ServiceOverlay ServiceZones { get; private set; }
        public CarSystem Traffic { get; private set; }
        public CanalMap Canals { get; private set; }
        public PortMap Ports { get; private set; }
        public BusMap BusStations { get; private set; }
        public BusStationPlacer BusTool { get; private set; }
        public BusSystem Buses { get; private set; }
        public CanalDrawer CanalTool { get; private set; }
        public RampTool RampingTool { get; private set; }
        public RampMap Ramps { get; private set; }
        public BuildingTapper BuildingTapper { get; private set; }
        public CityCardView CardView { get; private set; }
        public BuildingCardDeck CardDeck { get; private set; }

        /// <summary>Штрих выбранного инструмента (дорога, шоссе, односторонняя, канал, съезд) или null.</summary>
        public IRoadStroke ActiveStroke
        {
            get
            {
                switch (CurrentTool)
                {
                    case Tool.Road: return RoadDrawer;
                    case Tool.Highway:
                    case Tool.OneWay: return HighwayTool;
                    case Tool.Canal: return CanalTool;
                    case Tool.Ramp: return RampingTool;
                    default: return null;
                }
            }
        }
        public PortPlacer PortTool { get; private set; }
        public ShipSystem Ships { get; private set; }
        public DayNight Day { get; private set; }
        public NightLights Lights { get; private set; }
        public ServiceKind CurrentService { get; private set; } = ServiceKind.Ambulance;
        public RoadGraph RoadNetwork { get; private set; }
        public Tool CurrentTool { get; private set; } = Tool.Pan;
        public CityTutorial Tutorial => _tutorial;
        public CityDebugHud Hud => _hud;
        public EventSystem Events { get; private set; }

        [Tooltip("Как часто сохранять город, секунд")]
        public float AutosaveSeconds = 20f;

        public bool Started { get; private set; }

        RoadSystem _roads;
        CityDebugHud _hud;
        CityTutorial _tutorial;
        MainMenu _menu;
        CellRoadGraphSync _graphSync;
        BiomeMap _biomeMap;
        SectorRenderer _sectorRenderer;
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
            if (_menu != null)
            {
                // Destroy выполняется в конце кадра. Сначала отключаем фон, чтобы его Update
                // не перезаписал позицию камеры сразу после установки стартового вида города.
                _menu.StopBackdrop();
                Destroy(_menu);
            }
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

            foreach (Vector2Int c in Canals.Cells) data.canals.Add(new CellSave { x = c.x, y = c.y });
            foreach (Vector2Int c in Ports.Cells) data.ports.Add(new CellSave { x = c.x, y = c.y });
            foreach (Vector2Int c in BusStations.Cells) data.buses.Add(new CellSave { x = c.x, y = c.y });
            data.dayHour = Day != null ? Day.Hour : -1f;

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

            var canalCells = new List<Vector2Int>();
            if (data.canals != null)
                foreach (CellSave c in data.canals) canalCells.Add(new Vector2Int(c.x, c.y));
            Canals.Load(canalCells);
            var portCells = new List<Vector2Int>();
            if (data.ports != null)
                foreach (CellSave c in data.ports) portCells.Add(new Vector2Int(c.x, c.y));
            Ports.Load(portCells);
            var busCells = new List<Vector2Int>();
            if (data.buses != null)
                foreach (CellSave c in data.buses) busCells.Add(new Vector2Int(c.x, c.y));
            BusStations.Load(busCells);
            if (data.dayHour >= 0f) Day.SetHour(data.dayHour);

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
            RoadMap = new CellRoadMap();
            RoadNetwork = new RoadGraph();
            Ramps = new RampMap();
            _graphSync = new CellRoadGraphSync(RoadMap, RoadNetwork, CellSize, Ramps);

            Model = new CityModel();

            Sectors = new SectorMap(HexSize);
            _biomeMap = new BiomeMap { Seed = seed };
            if (data == null) Sectors.OpenRandomStartingNeighbors(seed, 2);
            Biomes = new Biomes(_biomeMap, Sectors, Model, RoadMap, CellSize);
            Canals = new CanalMap();
            Ports = new PortMap();
            BusStations = new BusMap();
            Biomes.Occupied = BusStations.Has;
            BusStations.Changed += Biomes.Refresh;
            Biomes.BindCanals(Canals);
            Day = GetOrAdd<DayNight>();

            Coverage = new ServiceCoverage(Model);
            Economy = GetOrAdd<Economy>();
            Economy.Bind(Model, Coverage, Biomes);
            Utilities = new UtilityNet(Model);
            Economy.UtilityFactor = Utilities.ServedFraction;
            var markers = GetOrAdd<UtilityMarkers>();
            markers.CellSize = CellSize;
            markers.Bind(Model, Utilities);

            ServiceZones = GetOrAdd<ServiceOverlay>();
            ServiceZones.CellSize = CellSize;
            ServiceZones.Bind(Model, Coverage);

            var roadRenderer = GetOrAdd<CellRoadRenderer>();
            roadRenderer.CellSize = CellSize;
            roadRenderer.Bind(RoadMap);

            _sectorRenderer = GetOrAdd<SectorRenderer>();
            _sectorRenderer.CellSize = CellSize;
            _sectorRenderer.Bind(Sectors, Biomes.Map);
            var trees = GetOrAdd<TreeRenderer>();
            trees.CellSize = CellSize;
            trees.Drops = _sectorRenderer;
            _sectorRenderer.DropFinished += trees.Redraw;
            trees.Bind(Biomes, Sectors);
            Tapper = GetOrAdd<SectorTapper>();
            Tapper.Bind(Sectors, _sectorRenderer);

            Placer = GetOrAdd<BuildingPlacer>();
            Placer.CellSize = CellSize;
            Placer.Bind(Model, Economy, RoadMap, Biomes);
            Placer.CanPlaceAt = Sectors.IsOpenAt;
            Placer.Ports = Ports;
            Placer.Buses = BusStations;
            Placer.Canals = Canals;
            Placer.Ramps = Ramps;

            var canalRenderer = GetOrAdd<CanalRenderer>();
            canalRenderer.CellSize = CellSize;
            canalRenderer.Bind(Canals, Biomes);
            var busRenderer = GetOrAdd<BusStationRenderer>();
            busRenderer.CellSize = CellSize;
            busRenderer.Bind(BusStations, RoadMap);
            var portRenderer = GetOrAdd<PortRenderer>();
            portRenderer.CellSize = CellSize;
            portRenderer.Bind(Ports);

            CanalTool = GetOrAdd<CanalDrawer>();
            CanalTool.CellSize = CellSize;
            CanalTool.Bind(Canals, Ports, Model, Sectors, Biomes, Economy);
            CanalTool.IsOccupied = BusStations.Has;
            PortTool = GetOrAdd<PortPlacer>();
            PortTool.CellSize = CellSize;
            PortTool.Bind(Ports, Model, RoadMap, Sectors, Biomes, Economy);
            BusTool = GetOrAdd<BusStationPlacer>();
            BusTool.CellSize = CellSize;
            BusTool.Bind(BusStations, Ports, Canals, Model, RoadMap, Sectors, Biomes, Economy);

            RoadDrawer = GetOrAdd<CellRoadDrawer>();
            RoadDrawer.CellSize = CellSize;
            RoadDrawer.Bind(RoadMap, Model, Sectors, Economy);
            RoadDrawer.CostFactorAt = Biomes.RoadCostFactor;
            RoadDrawer.IsOccupied = c => Ports.Has(c) || BusStations.Has(c);
            RoadDrawer.enabled = false;
            HighwayTool = GetOrAdd<HighwayLaneTool>();
            HighwayTool.CellSize = CellSize;
            HighwayTool.Bind(RoadMap, Model, Sectors, Economy);
            HighwayTool.CostFactorAt = Biomes.RoadCostFactor;
            HighwayTool.IsOccupied = c => Ports.Has(c) || BusStations.Has(c);
            HighwayTool.enabled = false;

            // Свободные дороги (вариант 2): инициализируем для будущего использования.
            FreeRoadDrawer = _roads.Drawer;
            FreeRoadDrawer.Economy = Economy;
            FreeRoadDrawer.CostFactorAt = p => Biomes.RoadCostFactor(
                new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize)));
            FreeRoadDrawer.CanBuildAt = Sectors.IsOpenAt;
            FreeRoadDrawer.enabled = false;

            JunctionTool = GetOrAdd<JunctionTapper>();
            JunctionTool.CellSize = CellSize;
            JunctionTool.Bind(RoadMap, Economy);
            RampingTool = GetOrAdd<RampTool>();
            RampingTool.CellSize = CellSize;
            RampingTool.Bind(Ramps, Economy);
            RampingTool.enabled = false;
            var rampRenderer = GetOrAdd<RampRenderer>();
            rampRenderer.CellSize = CellSize;
            rampRenderer.Bind(Ramps);

            GetOrAdd<RoadDrawFx>().Bind(CellSize, RoadDrawer, HighwayTool, FreeRoadDrawer, CanalTool, RampingTool);
            var tunnels = GetOrAdd<TunnelRenderer>();
            tunnels.CellSize = CellSize;
            tunnels.Bind(RoadMap, Biomes);
            var bridges = GetOrAdd<BridgeRenderer>();
            bridges.CellSize = CellSize;
            bridges.Bind(RoadMap, Biomes);

            Flow = GetOrAdd<FlowSolver>();
            Flow.CellSizeScale = CellSize;
            Flow.Bind(RoadNetwork, Model);

            // Здания рисуем после потоков: свечение района зависит от довольства.
            var cityRenderer = GetOrAdd<CityRenderer>();
            cityRenderer.CellSize = CellSize;
            cityRenderer.Bind(Model, Flow, Biomes);

            Traffic = GetOrAdd<CarSystem>();
            Traffic.CellSize = CellSize;
            Traffic.Day = Day;
            Traffic.Bind(Flow, RoadNetwork, RoadMap, Model);

            Ships = GetOrAdd<ShipSystem>();
            Ships.CellSize = CellSize;
            Ships.Bind(Ports, Canals, Biomes, Sectors, Model, Day);
            Buses = GetOrAdd<BusSystem>();
            Buses.CellSize = CellSize;
            Buses.Bind(BusStations, RoadMap, Day);
            Economy.ExtraIncome = () => Ships.IncomePerSecond + Buses.IncomePerSecond;
            Lights = GetOrAdd<NightLights>();
            Lights.CellSize = CellSize;
            Lights.Biomes = Biomes;
            Lights.Bind(RoadMap, Model, Ports, Day);
            Lights.SetBuses(BusStations);
            Heatmap = GetOrAdd<FlowHeatmap>();
            Heatmap.Bind(Flow, RoadNetwork);

            Rig = GetOrAdd<CameraRig>();

            _hud = GetOrAdd<CityDebugHud>();
            _hud.Bind(this);
            _tutorial = GetOrAdd<CityTutorial>();
            _tutorial.Bind(this);

            FreeRoadDrawer.IsPointerBlocked = _hud.IsPointerBlocked;
            RoadDrawer.IsPointerBlocked = _hud.IsPointerBlocked;
            HighwayTool.IsPointerBlocked = _hud.IsPointerBlocked;
            RampingTool.IsPointerBlocked = _hud.IsPointerBlocked;
            Placer.IsPointerBlocked = _hud.IsPointerBlocked;
            JunctionTool.IsPointerBlocked = _hud.IsPointerBlocked;
            Tapper.IsPointerBlocked = _hud.IsPointerBlocked;
            CanalTool.IsPointerBlocked = _hud.IsPointerBlocked;
            PortTool.IsPointerBlocked = _hud.IsPointerBlocked;
            BusTool.IsPointerBlocked = _hud.IsPointerBlocked;
            Rig.IsPointerBlocked = _hud.IsPointerBlocked;

            CardView = GetOrAdd<CityCardView>();
            CardView.Bind(Model, Economy, Coverage);
            BuildingTapper = GetOrAdd<BuildingTapper>();
            BuildingTapper.CellSize = CellSize;
            BuildingTapper.Bind(Model);
            BuildingTapper.IsPointerBlocked = _hud.IsPointerBlocked;
            BuildingTapper.enabled = false;

            CardDeck = GetOrAdd<BuildingCardDeck>();
            CardDeck.CellSize = CellSize;
            CardDeck.Bind(Placer, Economy, _hud);
            CardDeck.ToolRequested += tool =>
            {
                if (tool == Tool.Service) SelectService(CardDeck.LastSelectedService);
                if (tool == Tool.Junction) JunctionTool.AutoType = CardDeck.LastSelectedJunctionType;
                Select(tool);
            };

            Events = GetOrAdd<EventSystem>();
            Events.Bind(Model, Coverage);
            var evRenderer = GetOrAdd<EventRenderer>();
            evRenderer.CellSize = CellSize;
            evRenderer.Bind(Events);

            Started = true;
            if (data != null) Apply(data);
            else Rig.SetStartView(Vector2.zero, HexSize, InitialNeighborVisibleFraction);

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
            if (_sectorRenderer != null) _sectorRenderer.SetGridVisible(tool != Tool.Pan);

            FreeRoadDrawer.enabled = false;
            RoadDrawer.enabled = tool == Tool.Road;
            HighwayTool.enabled = tool == Tool.Highway || tool == Tool.OneWay;
            HighwayTool.OneWayMode = tool == Tool.OneWay;
            JunctionTool.enabled = tool == Tool.Junction;
            Placer.enabled = build;
            Tapper.enabled = tool == Tool.Expand;
            CanalTool.enabled = tool == Tool.Canal;
            PortTool.enabled = tool == Tool.Port;
            BusTool.enabled = tool == Tool.Bus;
            RampingTool.enabled = tool == Tool.Ramp;
            Rig.PanEnabled = tool == Tool.Pan;
            if (BuildingTapper != null) BuildingTapper.enabled = tool == Tool.Pan;
            if (CardDeck != null && tool != Tool.Pan) CardDeck.HideAll();

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
