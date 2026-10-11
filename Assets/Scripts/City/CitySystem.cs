using CityGame.CellRoads;
using CityGame.Cars;
using CityGame.Flow;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    public enum Tool
    {
        Pan,
        Road,
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
        public JunctionTapper JunctionTool { get; private set; }
        public Biomes Biomes { get; private set; }
        public ServiceCoverage Coverage { get; private set; }
        public ServiceOverlay ServiceZones { get; private set; }
        public CarSystem Traffic { get; private set; }
        public ServiceKind CurrentService { get; private set; } = ServiceKind.Ambulance;
        public RoadGraph RoadNetwork { get; private set; }
        public Tool CurrentTool { get; private set; } = Tool.Pan;

        RoadSystem _roads;
        CityDebugHud _hud;
        CellRoadGraphSync _graphSync;

        void Start()
        {
            // Игра вертикальная. В редакторе настройка игнорируется, там выбери портретный размер окна Game.
            Screen.orientation = ScreenOrientation.Portrait;

            _roads = GetComponent<RoadSystem>();
            // Старое рисование дорог от руки отключено: дороги теперь по клеткам.
            _roads.Drawer.enabled = false;

            Model = new CityModel();
            RoadMap = new CellRoadMap();
            RoadNetwork = new RoadGraph();
            _graphSync = new CellRoadGraphSync(RoadMap, RoadNetwork, CellSize);

            Sectors = new SectorMap(HexSize);
            Biomes = new Biomes(new BiomeMap(), Sectors, Model, RoadMap, CellSize);

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
            Traffic.Bind(Flow, RoadNetwork, RoadMap);
            Heatmap = GetOrAdd<FlowHeatmap>();
            Heatmap.Bind(Flow, RoadNetwork);

            Rig = GetOrAdd<CameraRig>();

            _hud = GetOrAdd<CityDebugHud>();
            _hud.Bind(this);

            RoadDrawer.IsPointerBlocked = _hud.IsPointerBlocked;
            Placer.IsPointerBlocked = _hud.IsPointerBlocked;
            JunctionTool.IsPointerBlocked = _hud.IsPointerBlocked;
            Tapper.IsPointerBlocked = _hud.IsPointerBlocked;
            Rig.IsPointerBlocked = _hud.IsPointerBlocked;

            // Старт в режиме «Рука»: палец двигает карту. Нажатие на активный инструмент возвращает сюда.
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
            return c != null ? c : gameObject.AddComponent<T>();
        }

        public void Select(Tool tool)
        {
            CurrentTool = tool;
            bool build = tool >= Tool.Residential && tool <= Tool.Erase;

            RoadDrawer.enabled = tool == Tool.Road;
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
