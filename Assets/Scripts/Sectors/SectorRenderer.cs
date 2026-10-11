using System.Collections.Generic;
using CityGame.Roads;
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

        SectorMap _map;
        BiomeMap _biomes;
        Material _material;
        readonly List<GameObject> _openViews = new List<GameObject>();
        readonly List<GameObject> _frontierViews = new List<GameObject>();
        readonly List<GameObject> _gridViews = new List<GameObject>();
        GameObject _pending;

        public HexCoord? Pending { get; private set; }

        public void Bind(SectorMap map, BiomeMap biomes)
        {
            if (_map != null) _map.Changed -= Redraw;
            _map = map;
            _biomes = biomes;
            _map.Changed += Redraw;
            _material = new Material(Shader.Find("Sprites/Default"));
            Redraw();
        }

        void OnDestroy()
        {
            if (_map != null) _map.Changed -= Redraw;
        }

        public void SetPending(HexCoord? h)
        {
            Pending = h;
            Redraw();
        }

        void Redraw()
        {
            DestroyAll(_openViews);
            DestroyAll(_frontierViews);
            DestroyAll(_gridViews);
            Destroy(ref _pending);

            var pendingList = new List<HexCoord>();
            var open = new List<HexCoord>[4];
            var frontier = new List<HexCoord>[4];
            for (int i = 0; i < 4; i++)
            {
                open[i] = new List<HexCoord>();
                frontier[i] = new List<HexCoord>();
            }

            foreach (HexCoord h in _map.Open) open[(int)_biomes.BiomeOf(h)].Add(h);
            foreach (HexCoord h in _map.Frontier)
            {
                if (Pending.HasValue && Pending.Value == h) pendingList.Add(h);
                else frontier[(int)_biomes.BiomeOf(h)].Add(h);
            }

            for (int i = 0; i < 4; i++)
            {
                var b = (Biome)i;
                Color ground = BiomeSpecs.Ground(b);
                Add(_openViews, Make("Open " + b, open[i], ground, -30));
                Add(_frontierViews, Make("Closed " + b, frontier[i], Color.Lerp(FrontierColor, ground, ClosedHint), -30));
                Color grid = Color.Lerp(ground, Color.black, 0.07f);
                Add(_gridViews, MakeGrid(open[i], grid));
            }

            _pending = Make("Pending sector", pendingList, PendingColor, -29);
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
        GameObject MakeGrid(List<HexCoord> hexes, Color gridColor)
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
                        Line(verts, tris, new Vector2(lx - t, by - t), new Vector2(lx + t, by + cs + t));
                        Line(verts, tris, new Vector2(lx - t, by - t), new Vector2(lx + cs + t, by + t));
                        if (!InOpen(ix + 1, iy))
                            Line(verts, tris, new Vector2(lx + cs - t, by - t), new Vector2(lx + cs + t, by + cs + t));
                        if (!InOpen(ix, iy + 1))
                            Line(verts, tris, new Vector2(lx - t, by + cs - t), new Vector2(lx + cs + t, by + cs + t));
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

        GameObject Make(string goName, List<HexCoord> hexes, Color color, int order)
        {
            if (hexes.Count == 0) return null;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (HexCoord h in hexes)
            {
                Vector2 c = HexMath.ToWorld(h, _map.HexSize);
                int start = verts.Count;
                verts.Add(c);
                for (int i = 0; i < 6; i++)
                {
                    Vector2 corner = HexMath.Corner(h, i, _map.HexSize);
                    verts.Add(c + (corner - c) * Gap);
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
