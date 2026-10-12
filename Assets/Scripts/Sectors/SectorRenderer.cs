using System.Collections.Generic;
using CityGame.Roads;
using CityGame.Buildings;
using UnityEngine;

namespace CityGame.Sectors
{
    /// <summary>
    /// Рисует открытые сектора светлой плиткой, а доступные для открытия приглушённой.
    /// Предпросмотр содержимого (биомы) появится вместе с генерацией.
    /// </summary>
    public sealed class SectorRenderer : MonoBehaviour
    {
        public Color OpenColor = new Color32(0xF6, 0xF3, 0xEC, 0xFF);
        [Tooltip("Закрытые сектора: тёмно-серый, ближе к чёрному")]
        public Color FrontierColor = new Color32(0x1F, 0x23, 0x29, 0xFF);
        [Tooltip("Насколько закрытый сектор намекает на свой биом, 0..1")]
        [Range(0f, 0.4f)] public float ClosedHint = 0.12f;
        public Color PendingColor = new Color32(0xBF, 0xD9, 0xEE, 0xFF);
        [Range(0.9f, 1f)] public float Gap = 0.985f;
        public float CellSize = 1f;
        [Range(0.01f, 0.1f)] public float GridLine = 0.04f;

        [Header("Анимация открытия сектора (подкручивай здесь или в инспекторе)")]
        [Tooltip("Сколько секунд сектор уменьшается до размера карты")]
        public float DropDuration = 1.1f;
        [Tooltip("Мягкость остановки масштаба. 0 = ровно, больше = лёгкий отскок")]
        [Range(0f, 0.5f)] public float DropSoftness = 0.08f;
        [Tooltip("Начальный масштаб: сектор появляется увеличенным и сжимается до размера клетки")]
        [Range(1f, 1.8f)] public float DropStartScale = 1.48f;
        [Tooltip("Какую часть времени сектор проявляется из прозрачности. 0.5 = за первую половину падения")]
        [Range(0.05f, 1f)] public float DropFadeShare = 0.5f;
        [Tooltip("Длительность волны по сетке после сжатия сектора")]
        public float GridWaveDuration = 0.9f;
        [Tooltip("Яркость тёплой подсветки, расходящейся по сетке от центра")]
        [Range(0f, 1.5f)] public float GridWaveStrength = 0.95f;
        [Tooltip("Ширина светящегося кольца волны в размерах клетки")]
        [Range(0.2f, 1.5f)] public float GridWaveWidth = 0.8f;

        /// <summary>Сектора, которые сейчас падают. Остальные рендереры не рисуют их в общих мешах.</summary>
        public readonly HashSet<HexCoord> Dropping = new HashSet<HexCoord>();

        /// <summary>Анимация закончилась и сектор нарисован обычным образом.</summary>
        public event System.Action DropFinished;

        sealed class Drop
        {
            public HexCoord H;
            public float Start;
            public GameObject Root;
            public readonly List<Material> Mats = new List<Material>();
            public readonly List<Color> Base = new List<Color>();
            public Mesh WaveMesh;
            public float[] WaveDistances;
            public float WaveMaxDistance;
        }

        readonly List<Drop> _drops = new List<Drop>();

        SectorMap _map;
        BiomeMap _biomes;
        Material _material;
        readonly List<GameObject> _openViews = new List<GameObject>();
        readonly List<GameObject> _frontierViews = new List<GameObject>();
        readonly List<GameObject> _gridViews = new List<GameObject>();
        GameObject _pending;
        bool _gridVisible;

        public HexCoord? Pending { get; private set; }

        public void SetGridVisible(bool visible)
        {
            _gridVisible = visible;
            foreach (GameObject view in _gridViews)
                if (view != null) view.SetActive(visible);
        }

        public void Bind(SectorMap map, BiomeMap biomes)
        {
            UnsubscribeMap();
            _map = map;
            _biomes = biomes;
            SubscribeMap();
            if (_material == null) _material = new Material(Shader.Find("Sprites/Default"));
            Redraw();
        }

        void OnEnable() => SubscribeMap();

        void OnDisable() => UnsubscribeMap();

        void SubscribeMap()
        {
            if (_map == null) return;
            _map.Changed -= Redraw;
            _map.Changed += Redraw;
        }

        void UnsubscribeMap()
        {
            if (_map != null) _map.Changed -= Redraw;
        }

        void OnDestroy()
        {
            UnsubscribeMap();
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }

        /// <summary>Запускает падение сектора. Вызывается перед открытием в SectorMap.</summary>
        public void BeginDrop(HexCoord h)
        {
            Dropping.Add(h);
            _drops.Add(new Drop { H = h, Start = Time.time });
        }

        TreeRenderer _trees;

        void Update()
        {
            if (_drops.Count == 0) return;
            bool finished = false;
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                Drop d = _drops[i];
                float elapsed = Time.time - d.Start;
                float fallT = elapsed / Mathf.Max(0.05f, DropDuration);
                if (fallT < 1f)
                {
                    ApplyDrop(d, fallT);
                    continue;
                }

                ApplyDrop(d, 1f);
                float waveT = (elapsed - DropDuration) / Mathf.Max(0.05f, GridWaveDuration);
                ApplyGridWave(d, waveT);
                if (waveT >= 1f)
                {
                    Dropping.Remove(d.H);
                    DestroyDrop(d);
                    _drops.RemoveAt(i);
                    finished = true;
                }
            }

            if (finished)
            {
                Redraw();
                DropFinished?.Invoke();
            }
        }

        /// <summary>Мягкое замедление к концу с едва заметным подскоком (easeOutBack).</summary>
        float Ease(float t)
        {
            float c1 = DropSoftness * 1.2f;
            float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        void ApplyDrop(Drop d, float t)
        {
            if (d.Root == null) return;
            float e = Ease(Mathf.Clamp01(t));
            Vector2 c = HexMath.ToWorld(d.H, _map.HexSize);
            d.Root.transform.position = new Vector3(c.x, c.y, 0f);
            d.Root.transform.localScale = Vector3.one * Mathf.LerpUnclamped(Mathf.Max(1f, DropStartScale), 1f, e);
            float a = Mathf.Clamp01(t / Mathf.Max(0.05f, DropFadeShare));
            a = a * a * (3f - 2f * a);
            for (int i = 0; i < d.Mats.Count; i++)
            {
                Color col = d.Base[i];
                col.a *= a;
                d.Mats[i].color = col;
            }
        }

        void ApplyGridWave(Drop d, float t)
        {
            if (d.WaveMesh == null || d.WaveDistances == null || GridWaveStrength <= 0f) return;
            float radius = d.WaveMaxDistance * Mathf.Clamp01(t);
            float width = Mathf.Max(0.08f, GridWaveWidth * CellSize);
            var colors = new Color[d.WaveDistances.Length * 4];
            for (int i = 0; i < d.WaveDistances.Length; i++)
            {
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d.WaveDistances[i] - radius) / width);
                ring = ring * ring * (3f - 2f * ring) * GridWaveStrength;
                Color color = new Color(1f, 0.88f, 0.58f, ring);
                int v = i * 4;
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = color;
            }
            d.WaveMesh.colors = colors;
        }

        void DestroyDrop(Drop d)
        {
            if (d.Root == null) return;
            foreach (MeshFilter f in d.Root.GetComponentsInChildren<MeshFilter>())
                if (f.sharedMesh != null) UnityEngine.Object.Destroy(f.sharedMesh);
            foreach (Material m in d.Mats)
                if (m != null) UnityEngine.Object.Destroy(m);
            UnityEngine.Object.Destroy(d.Root);
            d.Root = null;
            d.Mats.Clear();
            d.Base.Clear();
            d.WaveMesh = null;
            d.WaveDistances = null;
        }

        public void SetPending(HexCoord? h)
        {
            Pending = h;
            if (isActiveAndEnabled && gameObject.activeInHierarchy) Redraw();
        }

        public void ClearPending() => SetPending(null);

        void Redraw()
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy || _map == null || _biomes == null) return;
            DestroyAll(_openViews);
            DestroyAll(_frontierViews);
            DestroyAll(_gridViews);
            Destroy(ref _pending);

            var pendingList = new List<HexCoord>();
            var open = new List<HexCoord>[BiomeSpecs.Count];
            var frontier = new List<HexCoord>[BiomeSpecs.Count];
            for (int i = 0; i < BiomeSpecs.Count; i++)
            {
                open[i] = new List<HexCoord>();
                frontier[i] = new List<HexCoord>();
            }

            foreach (HexCoord h in _map.Open)
                if (!Dropping.Contains(h)) open[(int)_biomes.BiomeOf(h)].Add(h);
            // Под падающим сектором остаётся тёмная закрытая плитка, на неё он и приземляется.
            foreach (Drop d in _drops) frontier[(int)_biomes.BiomeOf(d.H)].Add(d.H);
            foreach (HexCoord h in _map.Frontier)
            {
                if (Pending.HasValue && Pending.Value == h) pendingList.Add(h);
                else frontier[(int)_biomes.BiomeOf(h)].Add(h);
            }

            for (int i = 0; i < BiomeSpecs.Count; i++)
            {
                var b = (Biome)i;
                Color ground = BiomeSpecs.Ground(b);
                Add(_openViews, Make("Open " + b, open[i], ground, -30));
                Add(_frontierViews, Make("Closed " + b, frontier[i], Color.Lerp(FrontierColor, ground, ClosedHint), -30));
                Color grid = Color.Lerp(ground, Color.black, 0.07f);
                GameObject gridView = MakeGrid(open[i], grid);
                if (gridView != null)
                {
                    gridView.SetActive(_gridVisible);
                    _gridViews.Add(gridView);
                }
            }

            _pending = Make("Pending sector", pendingList, PendingColor, -29);

            foreach (Drop d in _drops) BuildDrop(d);
        }

        void BuildDrop(Drop d)
        {
            DestroyDrop(d);
            if (_trees == null) _trees = GetComponent<TreeRenderer>();
            Vector2 c = HexMath.ToWorld(d.H, _map.HexSize);
            Biome b = _biomes.BiomeOf(d.H);
            var one = new List<HexCoord> { d.H };

            d.Root = new GameObject("Dropping sector");
            d.Root.transform.SetParent(transform, false);

            // Плитка и сетка. Координаты локальные, чтобы масштаб шёл от центра сектора.
            GameObject ground = Make("Drop ground", one, BiomeSpecs.Ground(b), -19, c);   // выше деревьев соседей, пока сектор в воздухе
            GameObject grid = MakeGrid(one, Color.Lerp(BiomeSpecs.Ground(b), Color.black, 0.07f), c);
            if (grid != null) grid.GetComponent<MeshRenderer>().sortingOrder = -18;
            AddToDrop(d, ground);
            AddToDrop(d, grid);
            GameObject wave = MakeGridWave(d, c);
            if (wave != null)
            {
                d.WaveMesh = wave.GetComponent<MeshFilter>().sharedMesh;
                AddToDrop(d, wave);
            }

            if (_trees != null)
            {
                var batch = new MeshBatch();
                _trees.AddHex(d.H, batch, c);
                if (batch.V.Count > 0)
                {
                    var go = new GameObject("Drop trees");
                    go.AddComponent<MeshFilter>().sharedMesh = batch.ToMesh();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
                    mr.sortingOrder = _trees.SortingOrder + 3;
                    AddToDrop(d, go);
                }
            }

            ApplyDrop(d, Mathf.Clamp01((Time.time - d.Start) / Mathf.Max(0.05f, DropDuration)));
        }

        GameObject MakeGridWave(Drop drop, Vector2 origin)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var distances = new List<float>();
            float cs = CellSize;
            // Шире основного шва, чтобы импульс читался как отдельная волна.
            float t = GridLine * cs * 1.1f;
            Vector2 center = HexMath.ToWorld(drop.H, _map.HexSize);
            float rx = _map.HexSize * 0.8660254f + cs;
            float ry = _map.HexSize + cs;
            int x0 = Mathf.FloorToInt((center.x - rx) / cs);
            int x1 = Mathf.CeilToInt((center.x + rx) / cs);
            int y0 = Mathf.FloorToInt((center.y - ry) / cs);
            int y1 = Mathf.CeilToInt((center.y + ry) / cs);

            for (int ix = x0; ix <= x1; ix++)
            for (int iy = y0; iy <= y1; iy++)
            {
                if (!InHex(ix, iy, drop.H)) continue;
                float lx = ix * cs;
                float by = iy * cs;
                Vector2 o = origin;
                WaveLine(verts, tris, distances, new Vector2(lx - t, by - t) - o, new Vector2(lx + t, by + cs + t) - o, center, o);
                WaveLine(verts, tris, distances, new Vector2(lx - t, by - t) - o, new Vector2(lx + cs + t, by + t) - o, center, o);
                if (!InHex(ix + 1, iy, drop.H))
                    WaveLine(verts, tris, distances, new Vector2(lx + cs - t, by - t) - o, new Vector2(lx + cs + t, by + cs + t) - o, center, o);
                if (!InHex(ix, iy + 1, drop.H))
                    WaveLine(verts, tris, distances, new Vector2(lx - t, by + cs - t) - o, new Vector2(lx + cs + t, by + cs + t) - o, center, o);
            }

            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = "SectorGridWave" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.colors = new Color[verts.Count];
            mesh.RecalculateBounds();
            drop.WaveDistances = distances.ToArray();
            foreach (float distance in drop.WaveDistances)
                if (distance > drop.WaveMaxDistance) drop.WaveMaxDistance = distance;

            var go = new GameObject("Grid wave");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(_material) { color = Color.white };
            renderer.sortingOrder = -17;
            return go;
        }

        static void WaveLine(List<Vector3> verts, List<int> tris, List<float> distances,
            Vector2 min, Vector2 max, Vector2 center, Vector2 origin)
        {
            Line(verts, tris, min, max);
            Vector2 midpoint = (min + max) * 0.5f + origin;
            distances.Add(Vector2.Distance(midpoint, center));
        }

        static void AddToDrop(Drop d, GameObject go)
        {
            if (go == null) return;
            go.transform.SetParent(d.Root.transform, false);
            var r = go.GetComponent<MeshRenderer>();
            d.Mats.Add(r.sharedMaterial);
            d.Base.Add(r.sharedMaterial.color);
        }

        static void Add(List<GameObject> list, GameObject go)
        {
            if (go != null) list.Add(go);
        }

        static void DestroyAll(List<GameObject> list)
        {
            foreach (GameObject go in list)
            {
                if (go == null) continue;
                var f = go.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) Object.Destroy(f.sharedMesh);
                var r = go.GetComponent<MeshRenderer>();
                if (r != null && r.sharedMaterial != null) Object.Destroy(r.sharedMaterial);
                Object.Destroy(go);
            }

            list.Clear();
        }

        /// <summary>Лёгкая сетка клеток внутри открытых секторов. Клетка принадлежит сектору по своему центру.</summary>
        GameObject MakeGrid(List<HexCoord> hexes, Color gridColor, Vector2 origin = default)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float cs = CellSize;
            float t = GridLine * cs * 0.5f;

            foreach (HexCoord h in hexes)
            {
                Vector2 c = HexMath.ToWorld(h, _map.HexSize);
                float rx = _map.HexSize * 0.8660254f + cs;
                float ry = _map.HexSize + cs;
                int x0 = Mathf.FloorToInt((c.x - rx) / cs);
                int x1 = Mathf.CeilToInt((c.x + rx) / cs);
                int y0 = Mathf.FloorToInt((c.y - ry) / cs);
                int y1 = Mathf.CeilToInt((c.y + ry) / cs);

                for (int ix = x0; ix <= x1; ix++)
                {
                    for (int iy = y0; iy <= y1; iy++)
                    {
                        if (!InHex(ix, iy, h)) continue;

                        float lx = ix * cs;
                        float by = iy * cs;
                        // Левая и нижняя грани есть у каждой клетки, правая и верхняя только на краю области.
                        Vector2 o = origin;
                        Line(verts, tris, new Vector2(lx - t, by - t) - o, new Vector2(lx + t, by + cs + t) - o);
                        Line(verts, tris, new Vector2(lx - t, by - t) - o, new Vector2(lx + cs + t, by + t) - o);
                        if (!InOpen(ix + 1, iy))
                            Line(verts, tris, new Vector2(lx + cs - t, by - t) - o, new Vector2(lx + cs + t, by + cs + t) - o);
                        if (!InOpen(ix, iy + 1))
                            Line(verts, tris, new Vector2(lx - t, by + cs - t) - o, new Vector2(lx + cs + t, by + cs + t) - o);
                    }
                }
            }

            if (verts.Count == 0) return null;

            var mesh = new Mesh { name = "SectorGrid" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Sector grid");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(_material) { color = gridColor };
            r.sortingOrder = -25;
            return go;
        }

        bool InHex(int ix, int iy, HexCoord h)
        {
            return _map.SectorAt(new Vector2((ix + 0.5f) * CellSize, (iy + 0.5f) * CellSize)) == h;
        }

        bool InOpen(int ix, int iy)
        {
            return _map.IsOpenAt(new Vector2((ix + 0.5f) * CellSize, (iy + 0.5f) * CellSize));
        }

        static void Line(List<Vector3> v, List<int> t, Vector2 min, Vector2 max)
        {
            int i = v.Count;
            v.Add(new Vector3(min.x, min.y, 0));
            v.Add(new Vector3(min.x, max.y, 0));
            v.Add(new Vector3(max.x, max.y, 0));
            v.Add(new Vector3(max.x, min.y, 0));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        GameObject Make(string goName, List<HexCoord> hexes, Color color, int order, Vector2 origin = default)
        {
            if (hexes.Count == 0) return null;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (HexCoord h in hexes)
            {
                Vector2 c = HexMath.ToWorld(h, _map.HexSize);
                int start = verts.Count;
                c -= origin;
                verts.Add(c);
                for (int i = 0; i < 6; i++)
                {
                    Vector2 corner = HexMath.Corner(h, i, _map.HexSize);
                    verts.Add(c + (corner - (c + origin)) * Gap);
                }

                // Углы идут против часовой стрелки, поэтому треугольники собираем наоборот.
                for (int i = 0; i < 6; i++)
                {
                    tris.Add(start);
                    tris.Add(start + 1 + (i + 1) % 6);
                    tris.Add(start + 1 + i);
                }
            }

            var mesh = new Mesh { name = goName };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(_material) { color = color };
            r.sortingOrder = order;
            return go;
        }

        static void Destroy(ref GameObject go)
        {
            if (go == null) return;
            var f = go.GetComponent<MeshFilter>();
            if (f != null && f.sharedMesh != null) Object.Destroy(f.sharedMesh);
            var r = go.GetComponent<MeshRenderer>();
            if (r != null && r.sharedMaterial != null) Object.Destroy(r.sharedMaterial);
            Object.Destroy(go);
            go = null;
        }
    }
}
