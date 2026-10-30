using CityGame.Buildings;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.Sectors
{
    /// <summary>Рисует деревья на свободных клетках стандартного биома: тень, крона и блик.</summary>
    public sealed class TreeRenderer : MonoBehaviour
    {
        public float CellSize { get; set; } = 1f;
        [SerializeField] int _sortingOrder = -20;
        public int SortingOrder => _sortingOrder;

        /// <summary>Сектора, которые сейчас «падают»: их деревья рисует SectorRenderer вместе с плиткой.</summary>
        public SectorRenderer Drops;

        Biomes _biomes;
        SectorMap _sectors;
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;

        static readonly Color Shadow = new Color(0.1f, 0.18f, 0.1f, 0.22f);
        static readonly Color Crown = new Color32(0x58, 0x9E, 0x55, 0xFF);
        static readonly Color Light = new Color32(0x7C, 0xC0, 0x6C, 0xFF);

        public void Bind(Biomes biomes, SectorMap sectors)
        {
            if (_biomes != null) _biomes.Changed -= Redraw;
            _biomes = biomes;
            _sectors = sectors;
            _biomes.Changed += Redraw;
            Redraw();
        }

        void OnDestroy()
        {
            if (_biomes != null) _biomes.Changed -= Redraw;
        }

        /// <summary>Добавляет деревья одного сектора. origin вычитается из координат (для локальной сетки).</summary>
        public void AddHex(HexCoord h, MeshBatch batch, Vector2 origin)
        {
            if (_biomes == null) return;
            float cs = CellSize;
                if (_biomes.Map.BiomeOf(h) != Biome.Standard) return;
                Vector2 c = HexMath.ToWorld(h, _sectors.HexSize);
                float rx = _sectors.HexSize * 0.8660254f + cs;
                float ry = _sectors.HexSize + cs;
                int x0 = Mathf.FloorToInt((c.x - rx) / cs), x1 = Mathf.CeilToInt((c.x + rx) / cs);
                int y0 = Mathf.FloorToInt((c.y - ry) / cs), y1 = Mathf.CeilToInt((c.y + ry) / cs);

                for (int ix = x0; ix <= x1; ix++)
                for (int iy = y0; iy <= y1; iy++)
                {
                    var cell = new Vector2Int(ix, iy);
                    if (!_biomes.HasTree(cell)) continue;
                    Vector2 center = new Vector2((ix + 0.5f) * cs, (iy + 0.5f) * cs);
                    if (_sectors.SectorAt(center) != h) continue;

                    uint m = BiomeMap.Mix(ix, iy, 91);
                    float jx = ((m & 0xFF) / 255f - 0.5f) * 0.22f * cs;
                    float jy = (((m >> 8) & 0xFF) / 255f - 0.5f) * 0.22f * cs;
                    float r = cs * (0.22f + ((m >> 16) & 0xFF) / 255f * 0.10f);
                    Vector2 p = center + new Vector2(jx, jy);

                    batch.Disc(p - origin + new Vector2(0.05f, -0.06f) * cs, r, 24, Shadow);
                    batch.Disc(p - origin, r, 24, Crown);
                    batch.Disc(p - origin + new Vector2(-0.35f, 0.4f) * r, r * 0.55f, 16, Light);
                }
        }

        public void Redraw()
        {
            if (_biomes == null) return;
            _batch.Clear();

            foreach (HexCoord h in _sectors.Open)
            {
                if (Drops != null && Drops.Dropping.Contains(h)) continue;
                AddHex(h, _batch, Vector2.zero);
            }

            if (_view == null)
            {
                _view = new GameObject("Trees");
                _view.transform.SetParent(transform, false);
                _view.AddComponent<MeshFilter>();
                var mat = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
                var mr = _view.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.sortingOrder = _sortingOrder;
            }

            var f = _view.GetComponent<MeshFilter>();
            f.sharedMesh = _batch.ToMesh(f.sharedMesh);
        }
    }
}
