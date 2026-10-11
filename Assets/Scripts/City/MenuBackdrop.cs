using System.Collections.Generic;
using CityGame.Cars;
using CityGame.CellRoads;
using CityGame.Flow;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Фон главного меню: живой игрушечный город. Случайный рельеф, сетка дорог с кольцом и мостом,
    /// кварталы жилья, торговли и промышленности, службы, деревья и настоящие машины, которые едут по дорогам.
    /// Камера медленно плывёт и «дышит». Использует те же рисовальщики, что и игра, поэтому фон выглядит как игра.
    /// Всё живёт на отдельном объекте и целиком исчезает вместе с меню.
    /// </summary>
    public sealed class MenuBackdrop : MonoBehaviour
    {
        public float CellSize = 1f;
        public float HexSize = 5f;
        public int OpenRadius = 3;
        [Tooltip("Ширина видимой карты, единиц мира")]
        public float Width = 15f;

        Camera _cam;
        float _t;
        CellRoadGraphSync _sync;

        public static MenuBackdrop Create(float cellSize, float hexSize)
        {
            var go = new GameObject("MenuBackdrop");
            var b = go.AddComponent<MenuBackdrop>();
            b.CellSize = cellSize;
            b.HexSize = hexSize;
            b.Build();
            return b;
        }

        static float Rand(int x, int y, int salt)
        {
            return (BiomeMap.Mix(x, y, salt) & 0xFFFF) / 65535f;
        }

        void Build()
        {
            _cam = Camera.main;
            if (_cam != null)
            {
                _cam.orthographic = true;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color32(0x1F, 0x23, 0x29, 0xFF);
            }

            int seed = Random.Range(1, 1000000);
            int salt = seed % 97;

            // Дороги: три улицы в каждую сторону.
            var map = new CellRoadMap();
            int[] rows = { -5, 0, 5 };
            int[] cols = { -8, 0, 8 };
            foreach (int y in rows)
            {
                var path = new List<Vector2Int>();
                for (int x = -12; x <= 12; x++) path.Add(new Vector2Int(x, y));
                map.AddPath(path);
            }

            foreach (int x in cols)
            {
                var path = new List<Vector2Int>();
                for (int y = -12; y <= 12; y++) path.Add(new Vector2Int(x, y));
                map.AddPath(path);
            }

            map.SetType(new Vector2Int(0, 0), JunctionType.Roundabout);
            map.SetType(new Vector2Int(8, -5), JunctionType.Overpass);
            map.SetType(new Vector2Int(-8, 5), JunctionType.Roundabout);

            // Кварталы вдоль улиц. Тип зависит от блока 6x6, поэтому районы получаются крупными и сливаются.
            var cells = new Dictionary<Vector2Int, BuildingCell>();
            int serial = 0;
            for (int x = -11; x <= 11; x++)
            for (int y = -11; y <= 11; y++)
            {
                var p = new Vector2Int(x, y);
                if (map.Has(p)) continue;
                bool nearRoad = map.Has(p + Vector2Int.up) || map.Has(p + Vector2Int.down) ||
                                map.Has(p + Vector2Int.left) || map.Has(p + Vector2Int.right);
                if (!nearRoad || Rand(x, y, salt) > 0.8f) continue;

                int bx = Mathf.FloorToInt(x / 6f), by = Mathf.FloorToInt(y / 6f);
                int cat = (int)(BiomeMap.Mix(bx, by, salt + 5) % 3u);
                cells[p] = new BuildingCell(p, (BuildingCategory)cat) { Serial = ++serial };
            }

            // Три службы рядом с улицами.
            PutService(cells, map, new Vector2Int(2, 1), ServiceKind.Ambulance, ref serial);
            PutService(cells, map, new Vector2Int(-3, -4), ServiceKind.Police, ref serial);
            PutService(cells, map, new Vector2Int(4, 4), ServiceKind.Fire, ref serial);

            // Если в сетке не вышло жилья, превращаем один блок в жилой: машины нужны.
            bool anyHome = false;
            foreach (var c in cells.Values) if (c.Category == BuildingCategory.Residential) anyHome = true;
            if (!anyHome)
                foreach (var key in new List<Vector2Int>(cells.Keys))
                    if (key.x < 0 && key.y > 0 && cells[key].Category != BuildingCategory.Service)
                        cells[key] = new BuildingCell(key, BuildingCategory.Residential) { Serial = cells[key].Serial };

            var model = new CityModel();
            model.Load(cells.Values);

            var graph = new RoadGraph();
            _sync = new CellRoadGraphSync(map, graph, CellSize);

            var sectors = new SectorMap(HexSize);
            var open = new List<HexCoord>();
            for (int q = -OpenRadius; q <= OpenRadius; q++)
            for (int r = -OpenRadius; r <= OpenRadius; r++)
                if ((Mathf.Abs(q) + Mathf.Abs(r) + Mathf.Abs(q + r)) / 2 <= OpenRadius) open.Add(new HexCoord(q, r));
            sectors.Load(open);

            var biomeMap = new BiomeMap { Seed = seed };
            var biomes = new Biomes(biomeMap, sectors, model, map, CellSize);

            var sectorR = gameObject.AddComponent<SectorRenderer>();
            sectorR.CellSize = CellSize;
            sectorR.Bind(sectors, biomeMap);
            var trees = gameObject.AddComponent<TreeRenderer>();
            trees.CellSize = CellSize;
            trees.Bind(biomes, sectors);
            var roadR = gameObject.AddComponent<CellRoadRenderer>();
            roadR.CellSize = CellSize;
            roadR.Bind(map);
            var bridges = gameObject.AddComponent<BridgeRenderer>();
            bridges.CellSize = CellSize;
            bridges.Bind(map, biomes);

            var flow = gameObject.AddComponent<FlowSolver>();
            flow.CellSizeScale = CellSize;
            flow.Bind(graph, model);
            var cityR = gameObject.AddComponent<CityRenderer>();
            cityR.CellSize = CellSize;
            cityR.Bind(model, flow);
            var cars = gameObject.AddComponent<CarSystem>();
            cars.CellSize = CellSize;
            cars.Bind(flow, graph, map, model);
        }

        static void PutService(Dictionary<Vector2Int, BuildingCell> cells, CellRoadMap map, Vector2Int p,
            ServiceKind kind, ref int serial)
        {
            if (map.Has(p)) return;
            cells[p] = new BuildingCell(p, BuildingCategory.Service, kind) { Serial = ++serial };
        }

        void Update()
        {
            if (_cam == null) return;
            _t += Time.unscaledDeltaTime;

            float aspect = Mathf.Max(0.1f, (float)Screen.width / Mathf.Max(1, Screen.height));
            float width = Width + Mathf.Sin(_t * 0.11f) * 1.5f;
            _cam.orthographicSize = width * 0.5f / aspect;
            _cam.transform.position = new Vector3(Mathf.Sin(_t * 0.06f) * 5f, Mathf.Cos(_t * 0.045f) * 6f, -10f);
        }
    }
}
