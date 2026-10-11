using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.CellRoads;
using CityGame.Flow;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.Cars
{
    /// <summary>
    /// Машины на карте. Каждый маршрут из расчёта потоков выпускает машины с частотой Поток × CarsPerFlow в секунду,
    /// туда и обратно. Машина едет по ломаной ребра в правой полосе, держит дистанцию до переднего,
    /// на въезд в ребро и в развилку нужен «талон»: интервал между талонами равен 1 / (пропускная способность × CarsPerFlow).
    /// Поэтому при загрузке выше единицы перед узким местом растёт очередь с красными стоп-сигналами.
    /// Это визуальный слой: налоги по-прежнему считает FlowSolver.
    /// Любое изменение дорог пересобирает граф, и машины на старых рёбрах исчезают.
    /// </summary>
    public sealed class CarSystem : MonoBehaviour
    {
        [Header("Размеры, в единицах мира")]
        public float CellSize = 1f;
        public float CarLength = 0.3f;
        public float CarWidth = 0.15f;
        public float MinGap = 0.07f;
        [Tooltip("Смещение полосы от оси дороги, доля клетки")]
        public float LaneOffset = 0.125f;

        [Header("Поток")]
        [Tooltip("Сколько машин в секунду на единицу потока. Задаёт и густоту, и пропускную способность")]
        public float CarsPerFlow = 0.15f;
        public int MaxCars = 300;
        [Tooltip("Множитель к скорости из расчёта потоков")]
        public float SpeedScale = 0.8f;

        public int Count => _cars.Count;
        public int Stopped { get; private set; }

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (_low != null) _low.enabled = value;
                if (_high != null) _high.enabled = value;
            }
        }

        sealed class Car
        {
            public FlowSolver.Route Route;
            public int Step;
            public float Q;          // расстояние вдоль участка в направлении движения, от начала ребра в этом направлении
            public float Speed;
            public bool Cleared;     // талон на следующий участок уже получен
            public bool Braking;
            public Color Color;
            public Vector2 Pos;
            public Vector2 Dir;
            public bool Placed;
            public bool Upper;
            public bool Dead;
        }

        FlowSolver _flow;
        RoadGraph _graph;
        CellRoadMap _map;
        readonly List<Car> _cars = new List<Car>();
        readonly Dictionary<long, List<Car>> _lanes = new Dictionary<long, List<Car>>();
        readonly Stack<List<Car>> _pool = new Stack<List<Car>>();
        readonly Dictionary<long, float> _edgeNext = new Dictionary<long, float>();
        readonly Dictionary<int, float> _nodeNext = new Dictionary<int, float>();
        readonly List<float> _acc = new List<float>();
        readonly MeshBatch _lowBatch = new MeshBatch();
        readonly MeshBatch _highBatch = new MeshBatch();
        MeshRenderer _low, _high;
        bool _visible = true;
        bool _routesDirty = true;
        float _now;
        int _rotate;
        int _serial;

        static readonly Color[] Palette =
        {
            new Color32(0xF4, 0xF1, 0xEA, 0xFF), new Color32(0xE8, 0x5D, 0x5D, 0xFF), new Color32(0x5B, 0x8D, 0xEF, 0xFF),
            new Color32(0xF2, 0xC1, 0x4E, 0xFF), new Color32(0x6F, 0xBF, 0x73, 0xFF), new Color32(0xF2, 0x99, 0x4A, 0xFF)
        };

        public void Bind(FlowSolver flow, RoadGraph graph, CellRoadMap map)
        {
            if (_flow != null) _flow.Solved -= OnSolved;
            _flow = flow;
            _graph = graph;
            _map = map;
            _flow.Solved += OnSolved;
            OnSolved();

            _low = MakeView("Cars low", RoadSpecs.SortingOrder(RoadLayer.Ground) + 3);
            _high = MakeView("Cars high", RoadSpecs.SortingOrder(RoadLayer.Ground) + 13);
            _low.enabled = _visible;
            _high.enabled = _visible;
        }

        MeshRenderer MakeView(string viewName, int order)
        {
            var go = new GameObject(viewName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = viewName };
            var r = go.AddComponent<MeshRenderer>();
            var m = new Material(Shader.Find("Sprites/Default"));
            m.color = Color.white;
            r.sharedMaterial = m;
            r.sortingOrder = order;
            return r;
        }

        void OnDestroy()
        {
            if (_flow != null) _flow.Solved -= OnSolved;
        }

        void OnSolved()
        {
            _routesDirty = true;
        }

        void ResetSpawners()
        {
            _acc.Clear();
            for (int i = 0; i < _flow.Routes.Count; i++) _acc.Add(Random.value);
            _routesDirty = false;
        }

        // ---------------------------------------------------------------

        static long LaneKey(RoadEdge e, int dir)
        {
            return (long)e.Id * 2 + (dir > 0 ? 0 : 1);
        }

        static int DirOf(FlowSolver.RouteStep s)
        {
            return s.To >= s.From ? 1 : -1;
        }

        static float QOf(RoadEdge e, int dir, float s)
        {
            return dir > 0 ? s : e.Length - s;
        }

        void Update()
        {
            if (_flow == null || _graph == null) return;
            if (_routesDirty) ResetSpawners();

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            _now += dt;

            RebuildLanes();
            Spawn(dt);
            Move(dt);
            Render(dt);
        }

        void RebuildLanes()
        {
            foreach (var kv in _lanes)
            {
                kv.Value.Clear();
                _pool.Push(kv.Value);
            }

            _lanes.Clear();
            foreach (Car c in _cars)
            {
                if (c.Dead) continue;
                FlowSolver.RouteStep st = c.Route.Steps[c.Step];
                long key = LaneKey(st.Edge, DirOf(st));
                if (!_lanes.TryGetValue(key, out List<Car> list))
                {
                    list = _pool.Count > 0 ? _pool.Pop() : new List<Car>();
                    _lanes[key] = list;
                }

                list.Add(c);
            }
        }

        float EdgeHeadway(RoadEdge e)
        {
            return 1f / Mathf.Max(0.01f, _flow.EdgeCapacity(e) * CarsPerFlow);
        }

        /// <summary>Сколько можно проехать вперёд до переднего авто на этой полосе.</summary>
        float GapAhead(Car car, float q)
        {
            FlowSolver.RouteStep st = car.Route.Steps[car.Step];
            if (!_lanes.TryGetValue(LaneKey(st.Edge, DirOf(st)), out List<Car> list)) return 1e9f;

            float best = 1e9f;
            foreach (Car o in list)
            {
                if (o == car || o.Dead) continue;
                if (o.Q < q || (o.Q == q && o.GetHashCode() < car.GetHashCode())) continue;
                float gap = o.Q - CarLength - MinGap - q;
                if (gap < best) best = gap;
            }

            return best;
        }

        /// <summary>Есть ли свободное место у начала участка, чтобы туда въехать.</summary>
        bool StartFree(FlowSolver.RouteStep st, float q0)
        {
            if (!_lanes.TryGetValue(LaneKey(st.Edge, DirOf(st)), out List<Car> list)) return true;
            foreach (Car o in list)
                if (!o.Dead && Mathf.Abs(o.Q - q0) < CarLength + MinGap) return false;
            return true;
        }

        bool EdgeTokenFree(FlowSolver.RouteStep st)
        {
            return !_edgeNext.TryGetValue(LaneKey(st.Edge, DirOf(st)), out float t) || t <= _now;
        }

        void Spawn(float dt)
        {
            IReadOnlyList<FlowSolver.Route> routes = _flow.Routes;
            for (int i = 0; i < routes.Count && i < _acc.Count; i++)
            {
                _acc[i] += routes[i].Flow * CarsPerFlow * dt;
                while (_acc[i] >= 1f)
                {
                    if (_cars.Count >= MaxCars)
                    {
                        _acc[i] = 1f;
                        break;
                    }

                    FlowSolver.RouteStep st = routes[i].Steps[0];
                    if (_graph.GetEdge(st.Edge.Id) != st.Edge) { _acc[i] = 0f; break; }

                    int dir = DirOf(st);
                    float q0 = QOf(st.Edge, dir, st.From);
                    if (!EdgeTokenFree(st) || !StartFree(st, q0))
                    {
                        _acc[i] = Mathf.Min(_acc[i], 1.5f); // ждём, не копим пачку
                        break;
                    }

                    _edgeNext[LaneKey(st.Edge, dir)] = _now + EdgeHeadway(st.Edge);
                    var car = new Car
                    {
                        Route = routes[i],
                        Step = 0,
                        Q = q0,
                        Color = Palette[(_serial++ * 7 + i) % Palette.Length]
                    };
                    _cars.Add(car);
                    // Сразу вносим в полосу, чтобы следующая за ним проверка видела его.
                    long key = LaneKey(st.Edge, dir);
                    if (!_lanes.TryGetValue(key, out List<Car> list))
                    {
                        list = _pool.Count > 0 ? _pool.Pop() : new List<Car>();
                        _lanes[key] = list;
                    }

                    list.Add(car);
                    _acc[i] -= 1f;
                }
            }
        }

        void Move(float dt)
        {
            Stopped = 0;
            int n = _cars.Count;
            _rotate++;
            for (int k = 0; k < n; k++)
            {
                Car car = _cars[(k + _rotate) % n];
                if (car.Dead) continue;

                FlowSolver.RouteStep st = car.Route.Steps[car.Step];
                RoadEdge e = st.Edge;
                if (_graph.GetEdge(e.Id) != e) { car.Dead = true; continue; }

                int dir = DirOf(st);
                float q1 = QOf(e, dir, st.To);
                bool last = car.Step == car.Route.Steps.Count - 1;
                bool toNode = st.EndNode >= 0 && !last;
                float vFree = _flow.EdgeSpeed(e) * SpeedScale;

                // Линия остановки перед узлом: край клетки развилки.
                float stop = toNode && !car.Cleared ? q1 - (CellSize * 0.5f + CarLength * 0.5f) : q1;
                float move = vFree * dt;
                move = Mathf.Min(move, Mathf.Max(0f, GapAhead(car, car.Q)));
                move = Mathf.Min(move, Mathf.Max(0f, stop - car.Q));

                // На линии: пробуем получить талон на узел и следующий участок.
                if (toNode && !car.Cleared && car.Q + move >= stop - 1e-4f)
                {
                    if (TryClear(car))
                        car.Cleared = true;
                }

                car.Q += move;

                if (car.Q >= q1 - 1e-3f && (!toNode || car.Cleared || last))
                {
                    if (last)
                    {
                        car.Dead = true;
                        continue;
                    }

                    FlowSolver.RouteStep next = car.Route.Steps[car.Step + 1];
                    float q0 = QOf(next.Edge, DirOf(next), next.From);
                    if (_graph.GetEdge(next.Edge.Id) != next.Edge) { car.Dead = true; continue; }

                    if (StartFree(next, q0))
                    {
                        car.Step++;
                        car.Q = q0;
                        car.Cleared = false;
                    }
                    else
                    {
                        car.Q = q1;
                    }
                }

                car.Speed = move / dt;
                car.Braking = car.Speed < vFree * 0.2f;
                if (car.Braking) Stopped++;
            }

            for (int i = _cars.Count - 1; i >= 0; i--)
                if (_cars[i].Dead) _cars.RemoveAt(i);
        }

        bool TryClear(Car car)
        {
            FlowSolver.RouteStep st = car.Route.Steps[car.Step];
            FlowSolver.RouteStep next = car.Route.Steps[car.Step + 1];

            RoadNode node = _graph.GetNode(st.EndNode);
            bool limited = node != null && node.JunctionDelay > 0f;
            if (limited && _nodeNext.TryGetValue(node.Id, out float tn) && tn > _now) return false;
            if (!EdgeTokenFree(next)) return false;

            if (limited)
                _nodeNext[node.Id] = _now + 1f / Mathf.Max(0.01f, node.JunctionCapacity * CarsPerFlow);
            _edgeNext[LaneKey(next.Edge, DirOf(next))] = _now + EdgeHeadway(next.Edge);
            return true;
        }

        // ---------------------------------------------------------------

        static Vector2 Sample(IReadOnlyList<Vector2> pts, float s, out Vector2 tangent)
        {
            float acc = 0f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                float len = Vector2.Distance(pts[i], pts[i + 1]);
                if (len < 1e-5f) continue;
                if (acc + len >= s || i + 2 >= pts.Count)
                {
                    float t = Mathf.Clamp01((s - acc) / len);
                    tangent = (pts[i + 1] - pts[i]) / len;
                    return Vector2.Lerp(pts[i], pts[i + 1], t);
                }

                acc += len;
            }

            tangent = Vector2.right;
            return pts[0];
        }

        void Render(float dt)
        {
            _lowBatch.Clear();
            _highBatch.Clear();
            if (_low == null) return;

            float ease = 1f - Mathf.Exp(-16f * dt);
            Color window = new Color(0.12f, 0.14f, 0.18f, 0.9f);
            Color brake = new Color(1f, 0.25f, 0.2f, 1f);

            foreach (Car car in _cars)
            {
                if (car.Dead) continue;
                FlowSolver.RouteStep st = car.Route.Steps[car.Step];
                RoadEdge e = st.Edge;
                int dir = DirOf(st);
                float s = dir > 0 ? car.Q : e.Length - car.Q;

                Vector2 tan;
                Vector2 p = Sample(e.Points, s, out tan);
                Vector2 fwd = tan * dir;
                Vector2 right = new Vector2(fwd.y, -fwd.x);
                float off = e.OneWay != 0 ? 0f : LaneOffset * CellSize;
                Vector2 target = p + right * off;

                if (!car.Placed)
                {
                    car.Pos = target;
                    car.Dir = fwd;
                    car.Placed = true;
                }
                else
                {
                    car.Pos = Vector2.Lerp(car.Pos, target, ease);
                    Vector2 nd = Vector2.Lerp(car.Dir, fwd, ease);
                    car.Dir = nd.sqrMagnitude > 1e-6f ? nd.normalized : fwd;
                }

                // На мосту машина сверху, если едет вдоль полотна моста, иначе под ним.
                bool upper = false;
                var cell = new Vector2Int(Mathf.FloorToInt(car.Pos.x / CellSize), Mathf.FloorToInt(car.Pos.y / CellSize));
                JunctionType type = _map.TypeAt(cell);
                if (JunctionSpecs.IsOverpass(type) && Side.Count(_map.Mask(cell)) == 4)
                {
                    bool horizontal = Mathf.Abs(car.Dir.x) > Mathf.Abs(car.Dir.y);
                    upper = type == JunctionType.Overpass ? horizontal : !horizontal;
                }

                MeshBatch m = upper ? _highBatch : _lowBatch;
                Vector2 f = car.Dir;
                Vector2 r = new Vector2(f.y, -f.x);
                float hl = CarLength * 0.5f, hw = CarWidth * 0.5f;

                m.Local(car.Pos, f, r, -hl, hl, -hw, hw, car.Color);
                m.Local(car.Pos, f, r, hl * 0.05f, hl * 0.6f, -hw * 0.8f, hw * 0.8f, window);
                if (car.Braking)
                {
                    m.Local(car.Pos, f, r, -hl, -hl * 0.65f, -hw, -hw * 0.45f, brake);
                    m.Local(car.Pos, f, r, -hl, -hl * 0.65f, hw * 0.45f, hw, brake);
                }
            }

            _low.GetComponent<MeshFilter>().sharedMesh = _lowBatch.ToMesh(_low.GetComponent<MeshFilter>().sharedMesh);
            _high.GetComponent<MeshFilter>().sharedMesh = _highBatch.ToMesh(_high.GetComponent<MeshFilter>().sharedMesh);
        }
    }
}
