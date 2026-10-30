using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Sectors;
using CityGame.World;
using UnityEngine;

namespace CityGame.Water
{
    /// <summary>
    /// Корабли между портами. Для каждого порта ищем по воде (море и каналы, в открытых секторах,
    /// мимо зданий) два ближайших порта и пускаем между ними кораблик туда и обратно.
    /// Порты, до которых можно доплыть, дают больше денег: <see cref="IncomePerSecond"/>.
    /// </summary>
    public sealed class ShipSystem : MonoBehaviour
    {
        public float CellSize { get; set; } = 1f;
        [Tooltip("Скорость корабля, клеток в секунду")]
        [SerializeField] float _speed = 1.4f;
        [Tooltip("Сколько секунд корабль стоит у причала")]
        [SerializeField] float _dockSeconds = 2.5f;
        [Tooltip("Доход одного порта в секунду, даже если плыть некуда")]
        [SerializeField] float _portIncome = 0.4f;
        [Tooltip("Добавка в секунду за порт, до которого доплывает корабль")]
        [SerializeField] float _connectedBonus = 2f;
        [Tooltip("Сколько клеток воды просматриваем от порта")]
        [SerializeField] int _searchLimit = 6000;
        [Tooltip("Ближайших портов на каждый порт, к которым ходят корабли")]
        [SerializeField] int _linksPerPort = 2;
        [SerializeField] int _sortingOrder = -6;

        sealed class Ship
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

        PortMap _ports;
        CanalMap _canals;
        Biomes _biomes;
        SectorMap _sectors;
        CityModel _model;
        DayNight _day;
        readonly List<Ship> _ships = new List<Ship>();
        readonly List<GameObject> _objects = new List<GameObject>();
        readonly HashSet<Vector2Int> _connected = new HashSet<Vector2Int>();
        Material _material;
        bool _dirty = true;

        static readonly Color[] Hulls =
        {
            new Color32(0xD9, 0x4F, 0x45, 0xFF), new Color32(0x3D, 0x5A, 0xA8, 0xFF),
            new Color32(0x2E, 0x8B, 0x77, 0xFF), new Color32(0xE0, 0x8A, 0x2E, 0xFF)
        };

        public int ShipCount => _ships.Count;
        public int ConnectedPorts => _connected.Count;

        public float IncomePerSecond =>
            _ports == null ? 0f : _ports.Count * _portIncome + _connected.Count * _connectedBonus;

        public void Bind(PortMap ports, CanalMap canals, Biomes biomes, SectorMap sectors, CityModel model, DayNight day)
        {
            Unbind();
            _ports = ports;
            _canals = canals;
            _biomes = biomes;
            _sectors = sectors;
            _model = model;
            _day = day;
            _ports.Changed += MarkDirty;
            _canals.Changed += MarkDirty;
            _sectors.Changed += MarkDirty;
            _model.Rebuilt += MarkDirty;
            _dirty = true;
        }

        void Unbind()
        {
            if (_ports != null) _ports.Changed -= MarkDirty;
            if (_canals != null) _canals.Changed -= MarkDirty;
            if (_sectors != null) _sectors.Changed -= MarkDirty;
            if (_model != null) _model.Rebuilt -= MarkDirty;
        }

        void OnDestroy()
        {
            Unbind();
            ClearShips();
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

        bool Sailable(Vector2Int c)
        {
            return _biomes.IsWater(c) && !_model.HasCell(c) && _sectors.IsOpenAt(Center(c));
        }

        static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        void ClearShips()
        {
            foreach (GameObject go in _objects)
            {
                if (go == null) continue;
                foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>())
                    if (f.sharedMesh != null) Destroy(f.sharedMesh);
                Destroy(go);
            }

            _objects.Clear();
            _ships.Clear();
        }

        long _signature;
        bool _built;

        void Rebuild()
        {
            _dirty = false;
            var paths = new List<List<Vector2Int>>();
            var connected = new HashSet<Vector2Int>();
            long sig = 17;

            if (_ports.Count >= 2)
            {
                var all = new List<Vector2Int>(_ports.Cells);
                all.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                var made = new HashSet<long>();
                foreach (Vector2Int start in all)
                {
                    var found = new List<Vector2Int>();
                    var parent = new Dictionary<Vector2Int, Vector2Int>();
                    Search(start, found, parent);
                    for (int i = 0; i < found.Count && i < _linksPerPort; i++)
                    {
                        Vector2Int end = found[i];
                        if (!made.Add(Key(start, end))) continue;

                        var path = new List<Vector2Int>();
                        for (Vector2Int c = end; ; c = parent[c])
                        {
                            path.Add(c);
                            if (c == start) break;
                        }

                        path.Reverse();
                        paths.Add(path);
                        connected.Add(start);
                        connected.Add(end);
                        unchecked
                        {
                            foreach (Vector2Int c in path) sig = sig * 31 + c.x * 7919 + c.y;
                        }
                    }
                }
            }

            _connected.Clear();
            foreach (Vector2Int c in connected) _connected.Add(c);

            // Маршруты не изменились (например, поставили дом вдали от воды): корабли плывут как плыли.
            if (_built && sig == _signature) return;
            _built = true;
            _signature = sig;

            ClearShips();
            int index = 0;
            foreach (List<Vector2Int> path in paths)
            {
                CreateShip(path, index++);
                if (path.Count > 14) CreateShip(path, index++, 0.5f);
            }
        }

        static long Key(Vector2Int a, Vector2Int b)
        {
            long ka = ((long)a.x << 32) ^ (uint)a.y;
            long kb = ((long)b.x << 32) ^ (uint)b.y;
            return ka < kb ? ka * 1000003L + kb : kb * 1000003L + ka;
        }

        /// <summary>Поиск вширь по воде. Порты складываются в found по росту расстояния, сквозь порт не плывём.</summary>
        void Search(Vector2Int start, List<Vector2Int> found, Dictionary<Vector2Int, Vector2Int> parent)
        {
            var queue = new Queue<Vector2Int>();
            var seen = new HashSet<Vector2Int> { start };
            queue.Enqueue(start);
            int expanded = 0;
            while (queue.Count > 0 && expanded < _searchLimit)
            {
                Vector2Int cur = queue.Dequeue();
                expanded++;
                foreach (Vector2Int d in Dirs)
                {
                    Vector2Int n = cur + d;
                    if (!seen.Add(n)) continue;
                    if (_ports.Has(n))
                    {
                        parent[n] = cur;
                        found.Add(n);
                        continue;
                    }

                    if (!Sailable(n)) continue;
                    parent[n] = cur;
                    queue.Enqueue(n);
                }
            }
        }

        void CreateShip(List<Vector2Int> path, int index, float startAt = 0f)
        {
            var pts = new Vector2[path.Count];
            var cum = new float[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                pts[i] = Center(path[i]);
                if (i > 0) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            }

            float len = cum[cum.Length - 1];
            if (len < 0.01f) return;

            if (_material == null) _material = new Material(Shader.Find("Sprites/Default")) { color = Color.white };

            var go = new GameObject("Ship " + index);
            go.transform.SetParent(transform, false);
            var batch = new MeshBatch();
            BuildShip(batch, Hulls[index % Hulls.Length], CellSize);
            go.AddComponent<MeshFilter>().sharedMesh = batch.ToMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.sortingOrder = _sortingOrder;

            if (_day != null)
            {
                var glow = new GameObject("Ship lights");
                glow.transform.SetParent(go.transform, false);
                var lb = new MeshBatch();
                LightMesh.Glow(lb, new Vector2(0.34f, 0f) * CellSize, CellSize * 0.5f, new Color(1f, 0.95f, 0.7f, 0.5f));
                LightMesh.Glow(lb, new Vector2(-0.12f, 0f) * CellSize, CellSize * 0.4f, new Color(1f, 0.85f, 0.5f, 0.35f));
                lb.Disc(new Vector2(0.34f, 0f) * CellSize, CellSize * 0.035f, 8, Color.white);
                glow.AddComponent<MeshFilter>().sharedMesh = lb.ToMesh();
                var lr = glow.AddComponent<MeshRenderer>();
                lr.sharedMaterial = _day.LightMaterial;
                lr.sortingOrder = DayNight.LightOrder;
            }

            var ship = new Ship { Pts = pts, Cum = cum, Len = len, S = len * startAt, Tf = go.transform };
            ship.Dir = startAt > 0f ? -1 : 1;
            Place(ship, 1f, true);
            _ships.Add(ship);
            _objects.Add(go);
        }

        static Vector2 P(float cs, float x, float y)
        {
            return new Vector2(x, y) * cs;
        }

        static void BuildShip(MeshBatch b, Color hull, float cs)
        {
            // Кильватер.
            b.Tri(P(cs, -0.3f, 0.09f), P(cs, -0.3f, -0.09f), P(cs, -0.75f, 0f), new Color(1f, 1f, 1f, 0.35f));
            // Тень.
            var shadow = new List<Vector2>
            {
                P(cs, 0.4f, -0.04f), P(cs, 0.26f, 0.09f), P(cs, -0.24f, 0.09f), P(cs, -0.32f, 0.05f),
                P(cs, -0.32f, -0.13f), P(cs, -0.24f, -0.17f), P(cs, 0.26f, -0.17f)
            };
            b.Polygon(shadow, new Color(0.1f, 0.25f, 0.4f, 0.25f));
            // Корпус.
            var body = new List<Vector2>
            {
                P(cs, 0.38f, 0f), P(cs, 0.24f, 0.13f), P(cs, -0.26f, 0.13f), P(cs, -0.34f, 0.09f),
                P(cs, -0.34f, -0.09f), P(cs, -0.26f, -0.13f), P(cs, 0.24f, -0.13f)
            };
            b.Polygon(body, hull);
            // Палуба.
            var deck = new List<Vector2>
            {
                P(cs, 0.3f, 0f), P(cs, 0.21f, 0.09f), P(cs, -0.24f, 0.09f), P(cs, -0.29f, 0.06f),
                P(cs, -0.29f, -0.06f), P(cs, -0.24f, -0.09f), P(cs, 0.21f, -0.09f)
            };
            b.Polygon(deck, new Color32(0xF4, 0xF1, 0xEA, 0xFF));
            // Контейнеры и рубка.
            b.Rect(P(cs, 0.02f, -0.07f), P(cs, 0.17f, -0.01f), Color.Lerp(hull, Color.white, 0.2f));
            b.Rect(P(cs, 0.02f, 0.01f), P(cs, 0.17f, 0.07f), new Color32(0xF2, 0xC1, 0x4E, 0xFF));
            b.Rect(P(cs, -0.24f, -0.07f), P(cs, -0.06f, 0.07f), Color.Lerp(hull, Color.black, 0.2f));
            b.Rect(P(cs, -0.2f, -0.05f), P(cs, -0.1f, 0.05f), new Color32(0xE9, 0xEE, 0xF2, 0xFF));
        }

        /// <summary>Точка на ломаной на расстоянии s от начала.</summary>
        static Vector2 Sample(Ship sh, float s)
        {
            s = Mathf.Clamp(s, 0f, sh.Len);
            int i = 1;
            while (i < sh.Cum.Length - 1 && sh.Cum[i] < s) i++;
            float seg = sh.Cum[i] - sh.Cum[i - 1];
            float t = seg > 1e-5f ? (s - sh.Cum[i - 1]) / seg : 0f;
            return Vector2.Lerp(sh.Pts[i - 1], sh.Pts[i], t);
        }

        void Place(Ship sh, float ease, bool snap)
        {
            const float probe = 0.45f;
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

            sh.Tf.localPosition = new Vector3(pos.x, pos.y, 0f);
            sh.Tf.localRotation = Quaternion.Euler(0f, 0f, sh.Angle);
        }

        void Update()
        {
            if (_ports == null) return;
            if (_dirty) Rebuild();

            float dt = Time.deltaTime;
            float ease = 1f - Mathf.Exp(-8f * dt);
            foreach (Ship sh in _ships)
            {
                if (sh.Wait > 0f)
                {
                    sh.Wait -= dt;
                }
                else
                {
                    sh.S += sh.Dir * _speed * CellSize * dt;
                    if (sh.S >= sh.Len)
                    {
                        sh.S = sh.Len;
                        sh.Dir = -1;
                        sh.Wait = _dockSeconds;
                    }
                    else if (sh.S <= 0f)
                    {
                        sh.S = 0f;
                        sh.Dir = 1;
                        sh.Wait = _dockSeconds;
                    }
                }

                Place(sh, ease, false);
            }
        }
    }
}
