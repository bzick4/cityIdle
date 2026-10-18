using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.CellRoads;
using CityGame.Flow;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Cars
{
    /// <summary>
    /// Парк машин. Каждый жилой блок даёт от 0.2 до 0.6 машины, значения копятся: машина появляется на карте,
    /// когда накопленная сумма достигает очередного целого. Первые три жилых блока дают по целой машине.
    /// Машина живёт в городе постоянно: стоит у дома, едет в торговую или промышленную цель, пережидает там,
    /// возвращается домой. Если под ней перестроили дороги, она ищет путь заново от своей точки и не исчезает.
    /// На въезд в ребро и в развилку нужен «талон»: интервал равен 1 / (пропускная способность × CarsPerFlow),
    /// поэтому при перегрузе перед узким местом растёт очередь со стоп-сигналами.
    /// Это визуальный слой: налоги считает FlowSolver.
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

        [Header("Парк")]
        [Tooltip("Сколько машин даёт один жилой блок: от и до")]
        public float ContributionMin = 0.2f;
        public float ContributionMax = 0.6f;
        [Tooltip("Первые столько жилых блоков дают по целой машине")]
        public int FreeCars = 3;
        [Tooltip("Сколько стоит дома между поездками, секунд: от и до")]
        public Vector2 ParkSeconds = new Vector2(2f, 6f);
        [Tooltip("Сколько стоит у цели, секунд: от и до")]
        public Vector2 DwellSeconds = new Vector2(3f, 8f);

        [Header("Движение")]
        [Tooltip("Задаёт пропускную способность в машинах в секунду: способность × это число")]
        public float CarsPerFlow = 0.15f;
        [Tooltip("Множитель к скорости из расчёта потоков")]
        public float SpeedScale = 0.8f;
        [Tooltip("Радиус поиска дороги под машиной после перестройки сети")]
        public float RerouteRadius = 1.0f;

        /// <summary>Всего машин в парке, включая стоящих дома и у цели.</summary>
        public int Count => _cars.Count;
        public int OnRoad { get; private set; }
        public int Stopped { get; private set; }
        /// <summary>Накопленная сумма вкладов жилых блоков. Машин на карте: её целая часть.</summary>
        public float FleetValue { get; private set; }

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

        enum State { Parked, Driving, Inside }

        sealed class Car
        {
            public Vector2Int Home;
            public State State;
            public float Timer;
            public FlowSolver.Route Route;
            public int Step;
            public float Q;          // расстояние вдоль участка в направлении движения, от начала ребра в этом направлении
            public float Speed;
            public bool Cleared;     // талон на следующий участок уже получен
            public bool Returning;
            public Vector2Int GoalCell;
            public Color Color;
            public Vector2 Pos;
            public Vector2 Dir;
            public bool Placed;
            public bool Braking;
        }

        FlowSolver _flow;
        RoadGraph _graph;
        CellRoadMap _map;
        CityModel _model;
        readonly List<Car> _cars = new List<Car>();
        readonly Dictionary<long, List<Car>> _lanes = new Dictionary<long, List<Car>>();
        readonly Stack<List<Car>> _pool = new Stack<List<Car>>();
        readonly Dictionary<long, float> _edgeNext = new Dictionary<long, float>();
        readonly Dictionary<int, float> _nodeNext = new Dictionary<int, float>();
        readonly MeshBatch _lowBatch = new MeshBatch();
        readonly MeshBatch _highBatch = new MeshBatch();
        readonly MeshBatch _lightBatch = new MeshBatch();
        MeshRenderer _low, _high, _lights;

        /// <summary>Время суток. Ночью у машин горят фары.</summary>
        public CityGame.World.DayNight Day;
        bool _visible = true;
        float _now;
        int _rotate;
        int _serial;

        static readonly Color[] Palette =
        {
            new Color32(0xF4, 0xF1, 0xEA, 0xFF), new Color32(0xE8, 0x5D, 0x5D, 0xFF), new Color32(0x5B, 0x8D, 0xEF, 0xFF),
            new Color32(0xF2, 0xC1, 0x4E, 0xFF), new Color32(0x6F, 0xBF, 0x73, 0xFF), new Color32(0xF2, 0x99, 0x4A, 0xFF)
        };

        public void Bind(FlowSolver flow, RoadGraph graph, CellRoadMap map, CityModel model)
        {
            if (_flow != null) _flow.Solved -= OnSolved;
            if (_model != null) _model.Rebuilt -= SyncFleet;
            _flow = flow;
            _graph = graph;
            _map = map;
            _model = model;
            _flow.Solved += OnSolved;
            _model.Rebuilt += SyncFleet;
            SyncFleet();

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
            if (_model != null) _model.Rebuilt -= SyncFleet;
        }

        void OnSolved()
        {
            // Номера рёбер и узлов после перестройки сети новые, старые талоны ничего не значат.
            _edgeNext.Clear();
            _nodeNext.Clear();
        }

        // --- парк ---

        float Contribution(Vector2Int cell)
        {
            float t = (BiomeMap.Mix(cell.x, cell.y, 33) & 0xFFFF) / 65535f;
            return Mathf.Lerp(ContributionMin, ContributionMax, t);
        }

        Vector2 ParkedPos(Vector2Int home)
        {
            return new Vector2((home.x + 0.5f) * CellSize, (home.y + 0.3f) * CellSize);
        }

        /// <summary>Пересчитывает парк по жилым блокам в порядке постройки.</summary>
        void SyncFleet()
        {
            var residents = new List<BuildingCell>();
            foreach (BuildingCell c in _model.Cells)
                if (c.Category == BuildingCategory.Residential) residents.Add(c);
            residents.Sort((a, b) => a.Serial.CompareTo(b.Serial));

            var homes = new List<Vector2Int>();
            float sum = 0f;
            for (int i = 0; i < residents.Count; i++)
            {
                // Первые блоки дают машину сразу, остальные копят случайную долю.
                sum += i < FreeCars ? 1f : Contribution(residents[i].Pos);
                while (homes.Count < Mathf.FloorToInt(sum + 1e-4f)) homes.Add(residents[i].Pos);
            }

            FleetValue = sum;

            for (int i = 0; i < homes.Count; i++)
            {
                if (i < _cars.Count)
                {
                    _cars[i].Home = homes[i];
                    continue;
                }

                _cars.Add(new Car
                {
                    Home = homes[i],
                    State = State.Parked,
                    Timer = Random.Range(ParkSeconds.x, ParkSeconds.y),
                    Color = Palette[(_serial++ * 5 + 1) % Palette.Length],
                    Pos = ParkedPos(homes[i]),
                    Dir = Vector2.right,
                    Placed = true
                });
            }

            if (_cars.Count > homes.Count) _cars.RemoveRange(homes.Count, _cars.Count - homes.Count);
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
            if (_flow == null || _graph == null || _model == null) return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            _now += dt;

            RebuildLanes();
            Think(dt);
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
                if (c.State != State.Driving) continue;
                FlowSolver.RouteStep st = c.Route.Steps[c.Step];
                AddToLane(c, LaneKey(st.Edge, DirOf(st)));
            }
        }

        void AddToLane(Car c, long key)
        {
            if (!_lanes.TryGetValue(key, out List<Car> list))
            {
                list = _pool.Count > 0 ? _pool.Pop() : new List<Car>();
                _lanes[key] = list;
            }

            list.Add(c);
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
                if (o == car) continue;
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
                if (Mathf.Abs(o.Q - q0) < CarLength + MinGap) return false;
            return true;
        }

        bool EdgeTokenFree(FlowSolver.RouteStep st)
        {
            return !_edgeNext.TryGetValue(LaneKey(st.Edge, DirOf(st)), out float t) || t <= _now;
        }

        bool EdgeAlive(RoadEdge e)
        {
            return _graph.GetEdge(e.Id) == e;
        }

        // --- решения: поехать из дома, вернуться от цели ---

        void Think(float dt)
        {
            foreach (Car car in _cars)
            {
                if (car.State == State.Parked)
                {
                    car.Timer -= dt;
                    if (car.Timer <= 0f) TryStartOutbound(car);
                }
                else if (car.State == State.Inside)
                {
                    car.Timer -= dt;
                    if (car.Timer <= 0f) TryStartReturn(car);
                }
            }
        }

        bool Launch(Car car, FlowSolver.Route route)
        {
            FlowSolver.RouteStep st = route.Steps[0];
            if (!EdgeAlive(st.Edge)) return false;

            int dir = DirOf(st);
            float q0 = QOf(st.Edge, dir, st.From);
            if (!EdgeTokenFree(st) || !StartFree(st, q0)) return false;

            _edgeNext[LaneKey(st.Edge, dir)] = _now + EdgeHeadway(st.Edge);
            car.Route = route;
            car.Step = 0;
            car.Q = q0;
            car.Cleared = false;
            car.State = State.Driving;
            // Сразу вносим в полосу: следующая за ним проверка в этом кадре должна его видеть.
            AddToLane(car, LaneKey(st.Edge, dir));
            return true;
        }

        void TryStartOutbound(Car car)
        {
            if (_flow.Dirty) { car.Timer = 0.2f; return; }

            District home = _model.Get(car.Home)?.District;
            if (home == null) { car.Timer = 1f; return; }

            // Выбор цели по потокам из расчёта: чем больше поток, тем чаще едут туда.
            float total = 0f;
            foreach (FlowSolver.Route r in _flow.Routes)
                if (r.SrcDistrictId == home.Id) total += r.Flow;

            if (total <= 0f) { car.Timer = Random.Range(1f, 2f); return; }

            float pick = Random.value * total;
            FlowSolver.Route chosen = null;
            foreach (FlowSolver.Route r in _flow.Routes)
            {
                if (r.SrcDistrictId != home.Id) continue;
                chosen = r;
                pick -= r.Flow;
                if (pick <= 0f) break;
            }

            if (chosen == null || !Launch(car, chosen)) { car.Timer = 0.3f; return; }
            car.Returning = false;
            car.GoalCell = chosen.DstCell;
        }

        void TryStartReturn(Car car)
        {
            if (_flow.Dirty) { car.Timer = 0.2f; return; }

            District from = _model.Get(car.GoalCell)?.District;
            District home = _model.Get(car.Home)?.District;
            if (from == null || home == null) { GoHome(car); return; }

            FlowSolver.Route found = null;
            foreach (FlowSolver.Route r in _flow.Routes)
                if (r.SrcDistrictId == from.Id && r.DstDistrictId == home.Id) { found = r; break; }

            if (found == null) { GoHome(car); return; }
            if (!Launch(car, found)) { car.Timer = 0.3f; return; }
            car.Returning = true;
            car.GoalCell = car.Home;
        }

        /// <summary>Пути нет: машина оказывается дома, а не пропадает.</summary>
        void GoHome(Car car)
        {
            car.State = State.Parked;
            car.Timer = Random.Range(ParkSeconds.x, ParkSeconds.y);
            car.Placed = false;
            car.Speed = 0f;
            car.Braking = false;
        }

        void Arrive(Car car)
        {
            if (car.Returning)
            {
                car.State = State.Parked;
                car.Timer = Random.Range(ParkSeconds.x, ParkSeconds.y);
            }
            else
            {
                car.State = State.Inside;
                car.Timer = Random.Range(DwellSeconds.x, DwellSeconds.y);
            }

            car.Speed = 0f;
            car.Braking = false;
        }

        /// <summary>Сеть перестроили под машиной: ищем путь от её точки к текущей цели.</summary>
        void Reroute(Car car)
        {
            FlowSolver.Route r = _flow.RouteFrom(car.Pos, car.Dir, car.GoalCell, RerouteRadius);
            if (r == null || r.Steps.Count == 0)
            {
                GoHome(car);
                return;
            }

            car.Route = r;
            car.Step = 0;
            FlowSolver.RouteStep st = r.Steps[0];
            car.Q = QOf(st.Edge, DirOf(st), st.From);
            car.Cleared = false;
        }

        bool StepsAlive(Car car)
        {
            if (!EdgeAlive(car.Route.Steps[car.Step].Edge)) return false;
            return car.Step + 1 >= car.Route.Steps.Count || EdgeAlive(car.Route.Steps[car.Step + 1].Edge);
        }

        void Move(float dt)
        {
            Stopped = 0;
            OnRoad = 0;
            int n = _cars.Count;
            _rotate++;
            for (int k = 0; k < n; k++)
            {
                Car car = _cars[(k + _rotate) % n];
                if (car.State != State.Driving) continue;
                OnRoad++;

                if (!StepsAlive(car))
                {
                    // Расчёт ещё не обновлён: стоим на месте и ждём.
                    car.Speed = 0f;
                    if (!_flow.Dirty) Reroute(car);
                    if (car.State != State.Driving) continue;
                    if (!StepsAlive(car)) continue;
                }

                FlowSolver.RouteStep st = car.Route.Steps[car.Step];
                RoadEdge e = st.Edge;
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
                if (toNode && !car.Cleared && car.Q + move >= stop - 1e-4f && TryClear(car))
                    car.Cleared = true;

                car.Q += move;

                if (car.Q >= q1 - 1e-3f && (!toNode || car.Cleared || last))
                {
                    if (last)
                    {
                        Arrive(car);
                        continue;
                    }

                    FlowSolver.RouteStep next = car.Route.Steps[car.Step + 1];
                    float q0 = QOf(next.Edge, DirOf(next), next.From);
                    if (EdgeAlive(next.Edge) && StartFree(next, q0))
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

        /// <summary>
        /// Плавный поворот курса машины по углу. Смешивать векторы нельзя: при развороте на 180 градусов
        /// смесь схлопывается в ноль, после нормализации курс не меняется, и машина едет задом.
        /// Резкий разворот (больше 120 градусов, например с места или в тупике) делаем сразу.
        /// </summary>
        static Vector2 Turn(Vector2 current, Vector2 target, float ease)
        {
            if (target.sqrMagnitude < 1e-8f) return current;
            if (current.sqrMagnitude < 1e-8f) return target.normalized;
            float a = Mathf.Atan2(current.y, current.x) * Mathf.Rad2Deg;
            float b = Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg;
            float d = Mathf.DeltaAngle(a, b);
            a = Mathf.Abs(d) > 120f ? b : a + d * ease;
            float r = a * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        void Render(float dt)
        {
            _lowBatch.Clear();
            _highBatch.Clear();
            _lightBatch.Clear();
            if (_low == null) return;

            bool lit = Day != null && Day.LightLevel > 0.02f;

            float ease = 1f - Mathf.Exp(-16f * dt);
            Color window = new Color(0.12f, 0.14f, 0.18f, 0.9f);
            Color brake = new Color(1f, 0.25f, 0.2f, 1f);

            foreach (Car car in _cars)
            {
                if (car.State == State.Inside) continue;

                Vector2 target;
                Vector2 fwd;
                if (car.State == State.Driving)
                {
                    FlowSolver.RouteStep st = car.Route.Steps[car.Step];
                    RoadEdge e = st.Edge;
                    int dir = DirOf(st);
                    float s = dir > 0 ? car.Q : e.Length - car.Q;

                    Vector2 tan;
                    Vector2 p = Sample(e.Points, s, out tan);
                    fwd = tan * dir;
                    Vector2 rightV = new Vector2(fwd.y, -fwd.x);
                    float off = e.OneWay != 0 ? 0f : LaneOffset * CellSize;
                    target = p + rightV * off;
                }
                else
                {
                    target = ParkedPos(car.Home);
                    fwd = Vector2.right;
                }

                if (!car.Placed)
                {
                    car.Pos = target;
                    car.Dir = fwd;
                    car.Placed = true;
                }
                else
                {
                    car.Pos = Vector2.Lerp(car.Pos, target, ease);
                    car.Dir = Turn(car.Dir, fwd, ease);
                }

                // На мосту машина сверху, если едет вдоль полотна моста, иначе под ним.
                bool upper = false;
                if (car.State == State.Driving)
                {
                    var cell = new Vector2Int(Mathf.FloorToInt(car.Pos.x / CellSize), Mathf.FloorToInt(car.Pos.y / CellSize));
                    JunctionType type = _map.TypeAt(cell);
                    if (JunctionSpecs.IsOverpass(type) && Side.Count(_map.Mask(cell)) == 4)
                    {
                        bool horizontal = Mathf.Abs(car.Dir.x) > Mathf.Abs(car.Dir.y);
                        upper = type == JunctionType.Overpass ? horizontal : !horizontal;
                    }
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

                if (lit)
                {
                    // Фары: два тёплых пятна впереди и красные огни сзади.
                    Color beam = new Color(1f, 0.95f, 0.72f, 0.5f);
                    CityGame.World.LightMesh.Glow(_lightBatch, car.Pos + f * (hl + CellSize * 0.1f) + r * (hw * 0.55f),
                        CellSize * 0.2f, beam, 8);
                    CityGame.World.LightMesh.Glow(_lightBatch, car.Pos + f * (hl + CellSize * 0.1f) - r * (hw * 0.55f),
                        CellSize * 0.2f, beam, 8);
                    CityGame.World.LightMesh.Glow(_lightBatch, car.Pos - f * hl, CellSize * 0.1f,
                        new Color(1f, 0.2f, 0.15f, car.Braking ? 0.9f : 0.45f), 6);
                }
            }

            _low.GetComponent<MeshFilter>().sharedMesh = _lowBatch.ToMesh(_low.GetComponent<MeshFilter>().sharedMesh);
            _high.GetComponent<MeshFilter>().sharedMesh = _highBatch.ToMesh(_high.GetComponent<MeshFilter>().sharedMesh);

            if (Day == null) return;
            if (_lights == null)
            {
                var go = new GameObject("Car lights");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = "Car lights" };
                _lights = go.AddComponent<MeshRenderer>();
                _lights.sharedMaterial = Day.LightMaterial;
                _lights.sortingOrder = CityGame.World.DayNight.LightOrder;
            }

            _lights.enabled = _visible && lit;
            if (lit) _lights.GetComponent<MeshFilter>().sharedMesh = _lightBatch.ToMesh(_lights.GetComponent<MeshFilter>().sharedMesh);
        }
    }
}
