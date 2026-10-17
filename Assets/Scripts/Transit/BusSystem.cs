using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.CellRoads;
using CityGame.Roads;
using CityGame.Sectors;
using CityGame.World;
using UnityEngine;

namespace CityGame.Transit
{
    /// <summary>Помощники для маршрутов автобусов по дорожной карте клеток.</summary>
    public static class BusRoutes
    {
        /// <summary>По этой клетке автобус может ехать: обычная двусторонняя дорога, не шоссе и не односторонняя.</summary>
        public static bool Drivable(CellRoadMap roads, Vector2Int cell)
        {
            return roads.Has(cell) && !roads.HasHighwayAt(cell);
        }

        public static bool TouchesRoad(CellRoadMap roads, Vector2Int cell)
        {
            foreach (int side in Side.All)
                if (Drivable(roads, cell + Side.Vec(side))) return true;
            return false;
        }
    }

    /// <summary>
    /// Автобусы между станциями. От каждой станции поиском вширь по обычным дорогам находим две ближайшие
    /// другие станции и пускаем по дороге автобус туда и обратно. Станции, до которых можно доехать,
    /// дают больше денег: <see cref="IncomePerSecond"/>.
    /// </summary>
    public sealed class BusSystem : MonoBehaviour
    {
        public float CellSize = 1f;
        [Tooltip("Скорость автобуса, клеток в секунду")]
        public float Speed = 1.1f;
        [Tooltip("Сколько секунд автобус стоит на остановке")]
        public float DockSeconds = 2.5f;
        [Tooltip("Доход одной станции у дороги в секунду, даже если ехать некуда")]
        public float StationIncome = 0.3f;
        [Tooltip("Добавка в секунду за станцию, до которой ходит автобус")]
        public float ConnectedBonus = 1.5f;
        [Tooltip("Сколько клеток дороги просматриваем от станции")]
        public int SearchLimit = 4000;
        [Tooltip("Ближайших станций на каждую станцию, к которым ходят автобусы")]
        public int LinksPerStation = 2;
        [Tooltip("Смещение к правому краю дороги, доля клетки")]
        public float LaneOffset = 0.125f;
        public float BusLength = 0.62f;
        public float BusWidth = 0.22f;

        sealed class Bus
        {
            public Vector2[] Pts;
            public float[] Cum;
            public float Len;
            public float S;
            public int Dir = 1;
            public float Wait;
            public Transform Tf;
            public float Angle;
        }

        BusMap _stations;
        CellRoadMap _roads;
        DayNight _day;
        readonly List<Bus> _buses = new List<Bus>();
        readonly List<GameObject> _objects = new List<GameObject>();
        readonly HashSet<Vector2Int> _connected = new HashSet<Vector2Int>();
        Material _material;
        bool _dirty = true;
        long _signature;
        bool _built;

        static readonly Color[] Bodies =
        {
            new Color32(0x2E, 0x8B, 0x77, 0xFF), new Color32(0xE0, 0x8A, 0x2E, 0xFF),
            new Color32(0x3D, 0x5A, 0xA8, 0xFF)
        };

        public int BusCount => _buses.Count;
        public int ConnectedStations => _connected.Count;

        public float IncomePerSecond
        {
            get
            {
                if (_stations == null) return 0f;
                int active = 0;
                foreach (Vector2Int s in _stations.Cells)
                    if (BusRoutes.TouchesRoad(_roads, s)) active++;
                return active * StationIncome + _connected.Count * ConnectedBonus;
            }
        }

        public void Bind(BusMap stations, CellRoadMap roads, DayNight day)
        {
            Unbind();
            _stations = stations;
            _roads = roads;
            _day = day;
            _stations.Changed += MarkDirty;
            _roads.Changed += MarkDirty;
            _dirty = true;
        }

        void Unbind()
        {
            if (_stations != null) _stations.Changed -= MarkDirty;
            if (_roads != null) _roads.Changed -= MarkDirty;
        }

        void OnDestroy()
        {
            Unbind();
            ClearBuses();
            if (_material != null) Destroy(_material);
        }

        void MarkDirty()
        {
            _dirty = true;
        }

        Vector2 Center(Vector2Int c)
        {
            return new Vector2((c.x + 0.5f) * CellSize, (c.y + 0.5f) * CellSize);
        }

        void ClearBuses()
        {
            foreach (GameObject go in _objects)
            {
                if (go == null) continue;
                foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>())
                    if (f.sharedMesh != null) Destroy(f.sharedMesh);
                Destroy(go);
            }

            _objects.Clear();
            _buses.Clear();
        }

        void Rebuild()
        {
            _dirty = false;
            var routes = new List<List<Vector2>>();
            var connected = new HashSet<Vector2Int>();
            long sig = 17;

            if (_stations.Count >= 2)
            {
                var all = new List<Vector2Int>(_stations.Cells);
                all.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                var made = new HashSet<long>();
                foreach (Vector2Int start in all)
                {
                    var found = new List<KeyValuePair<Vector2Int, List<Vector2Int>>>();
                    Search(start, found);
                    for (int i = 0; i < found.Count && i < LinksPerStation; i++)
                    {
                        Vector2Int end = found[i].Key;
                        if (!made.Add(Key(start, end))) continue;

                        var pts = new List<Vector2> { Center(start) };
                        foreach (Vector2Int c in found[i].Value) pts.Add(Center(c));
                        pts.Add(Center(end));
                        routes.Add(pts);
                        connected.Add(start);
                        connected.Add(end);
                        unchecked
                        {
                            foreach (Vector2Int c in found[i].Value) sig = sig * 31 + c.x * 7919 + c.y;
                        }
                    }
                }
            }

            _connected.Clear();
            foreach (Vector2Int c in connected) _connected.Add(c);

            // Маршруты не изменились: автобусы едут как ехали.
            if (_built && sig == _signature) return;
            _built = true;
            _signature = sig;

            ClearBuses();
            int index = 0;
            foreach (List<Vector2> route in routes)
            {
                CreateBus(route, index++);
                if (route.Count > 12) CreateBus(route, index++, 0.5f);
            }
        }

        static long Key(Vector2Int a, Vector2Int b)
        {
            long ka = ((long)a.x << 32) ^ (uint)a.y;
            long kb = ((long)b.x << 32) ^ (uint)b.y;
            return ka < kb ? ka * 1000003L + kb : kb * 1000003L + ka;
        }

        /// <summary>
        /// Поиск вширь по дорогам от станции. Другие станции складываются по росту расстояния;
        /// значение это клетки дороги между ними, станции в путь не входят.
        /// </summary>
        void Search(Vector2Int station, List<KeyValuePair<Vector2Int, List<Vector2Int>>> found)
        {
            var queue = new Queue<Vector2Int>();
            var parent = new Dictionary<Vector2Int, Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            foreach (int side in Side.All)
            {
                Vector2Int n = station + Side.Vec(side);
                if (!BusRoutes.Drivable(_roads, n) || !seen.Add(n)) continue;
                queue.Enqueue(n);
            }

            var linked = new HashSet<Vector2Int>();
            int expanded = 0;
            while (queue.Count > 0 && expanded < SearchLimit)
            {
                Vector2Int cur = queue.Dequeue();
                expanded++;

                foreach (int side in Side.All)
                {
                    Vector2Int other = cur + Side.Vec(side);
                    if (other == station || !_stations.Has(other) || !linked.Add(other)) continue;
                    var path = new List<Vector2Int>();
                    for (Vector2Int c = cur; ; c = parent[c])
                    {
                        path.Add(c);
                        if (!parent.ContainsKey(c)) break;
                    }

                    path.Reverse();
                    found.Add(new KeyValuePair<Vector2Int, List<Vector2Int>>(other, path));
                }

                int mask = _roads.Mask(cur);
                foreach (int side in Side.All)
                {
                    if ((mask & side) == 0) continue;
                    Vector2Int n = cur + Side.Vec(side);
                    if (!BusRoutes.Drivable(_roads, n) || (_roads.Mask(n) & Side.Opposite(side)) == 0) continue;
                    if (!seen.Add(n)) continue;
                    parent[n] = cur;
                    queue.Enqueue(n);
                }
            }
        }

        void CreateBus(List<Vector2> pts, int index, float startAt = 0f)
        {
            var arr = pts.ToArray();
            var cum = new float[arr.Length];
            for (int i = 1; i < arr.Length; i++) cum[i] = cum[i - 1] + Vector2.Distance(arr[i - 1], arr[i]);
            float len = cum[cum.Length - 1];
            if (len < 0.01f) return;

            if (_material == null) _material = new Material(Shader.Find("Sprites/Default")) { color = Color.white };

            var go = new GameObject("Bus " + index);
            go.transform.SetParent(transform, false);
            var batch = new MeshBatch();
            BuildBus(batch, Bodies[index % Bodies.Length], CellSize);
            go.AddComponent<MeshFilter>().sharedMesh = batch.ToMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.sortingOrder = RoadSpecs.SortingOrder(RoadLayer.Ground) + 5;

            if (_day != null)
            {
                var glow = new GameObject("Bus lights");
                glow.transform.SetParent(go.transform, false);
                var lb = new MeshBatch();
                float hl = BusLength * CellSize * 0.5f;
                float hw = BusWidth * CellSize * 0.5f;
                LightMesh.Glow(lb, new Vector2(hl + CellSize * 0.1f, hw * 0.55f), CellSize * 0.22f, new Color(1f, 0.95f, 0.72f, 0.5f), 8);
                LightMesh.Glow(lb, new Vector2(hl + CellSize * 0.1f, -hw * 0.55f), CellSize * 0.22f, new Color(1f, 0.95f, 0.72f, 0.5f), 8);
                LightMesh.Glow(lb, Vector2.zero, CellSize * 0.4f, new Color(1f, 0.9f, 0.6f, 0.22f), 10);
                glow.AddComponent<MeshFilter>().sharedMesh = lb.ToMesh();
                var lr = glow.AddComponent<MeshRenderer>();
                lr.sharedMaterial = _day.LightMaterial;
                lr.sortingOrder = DayNight.LightOrder;
            }

            var bus = new Bus { Pts = arr, Cum = cum, Len = len, S = len * startAt, Tf = go.transform };
            bus.Dir = startAt > 0f ? -1 : 1;
            Place(bus, 1f, true);
            _buses.Add(bus);
            _objects.Add(go);
        }

        void BuildBus(MeshBatch b, Color body, float cs)
        {
            float hl = BusLength * cs * 0.5f;
            float hw = BusWidth * cs * 0.5f;
            Vector2 o = Vector2.zero;
            Vector2 u = Vector2.right, v = Vector2.up;
            b.Local(o + new Vector2(0.03f, -0.04f) * cs, u, v, -hl, hl, -hw, hw, new Color(0f, 0f, 0f, 0.18f));
            b.Local(o, u, v, -hl, hl, -hw, hw, body);
            b.Local(o, u, v, -hl * 0.92f, hl * 0.55f, -hw * 0.62f, hw * 0.62f, Color.Lerp(body, Color.white, 0.28f));
            // Окна рядом по борту и лобовое.
            for (int i = 0; i < 4; i++)
            {
                float x0 = -hl * 0.85f + i * hl * 0.36f;
                b.Local(o, u, v, x0, x0 + hl * 0.26f, -hw * 0.46f, hw * 0.46f, new Color(0.12f, 0.16f, 0.22f, 0.9f));
            }

            b.Local(o, u, v, hl * 0.62f, hl * 0.92f, -hw * 0.8f, hw * 0.8f, new Color(0.12f, 0.16f, 0.22f, 0.95f));
            b.Local(o, u, v, hl * 0.96f, hl, -hw * 0.8f, -hw * 0.45f, new Color(1f, 0.93f, 0.6f, 1f));
            b.Local(o, u, v, hl * 0.96f, hl, hw * 0.45f, hw * 0.8f, new Color(1f, 0.93f, 0.6f, 1f));
        }

        static Vector2 Sample(Bus sh, float s)
        {
            s = Mathf.Clamp(s, 0f, sh.Len);
            int i = 1;
            while (i < sh.Cum.Length - 1 && sh.Cum[i] < s) i++;
            float seg = sh.Cum[i] - sh.Cum[i - 1];
            float t = seg > 1e-5f ? (s - sh.Cum[i - 1]) / seg : 0f;
            return Vector2.Lerp(sh.Pts[i - 1], sh.Pts[i], t);
        }

        void Place(Bus sh, float ease, bool snap)
        {
            const float probe = 0.3f;
            Vector2 a = Sample(sh, sh.S - probe);
            Vector2 b = Sample(sh, sh.S + probe);
            Vector2 pos = (a + b) * 0.5f;
            if (sh.S <= 0.001f || sh.S >= sh.Len - 0.001f) pos = Sample(sh, sh.S);
            Vector2 h = (b - a) * sh.Dir;
            if (h.sqrMagnitude > 1e-6f)
            {
                float target = Mathf.Atan2(h.y, h.x) * Mathf.Rad2Deg;
                sh.Angle = snap ? target : Mathf.LerpAngle(sh.Angle, target, ease);
            }

            // Едем по правой полосе: сдвиг вправо от курса.
            float rad = sh.Angle * Mathf.Deg2Rad;
            Vector2 right = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
            pos += right * (LaneOffset * CellSize);

            sh.Tf.localPosition = new Vector3(pos.x, pos.y, 0f);
            sh.Tf.localRotation = Quaternion.Euler(0f, 0f, sh.Angle);
        }

        void Update()
        {
            if (_stations == null) return;
            if (_dirty) Rebuild();

            float dt = Time.deltaTime;
            float ease = 1f - Mathf.Exp(-10f * dt);
            foreach (Bus sh in _buses)
            {
                if (sh.Wait > 0f)
                {
                    sh.Wait -= dt;
                }
                else
                {
                    sh.S += sh.Dir * Speed * CellSize * dt;
                    if (sh.S >= sh.Len)
                    {
                        sh.S = sh.Len;
                        sh.Dir = -1;
                        sh.Wait = DockSeconds;
                    }
                    else if (sh.S <= 0f)
                    {
                        sh.S = 0f;
                        sh.Dir = 1;
                        sh.Wait = DockSeconds;
                    }
                }

                Place(sh, ease, false);
            }
        }
    }
}
