using CityGame.Buildings;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Water
{
    /// <summary>
    /// Вода каналов: цвет моря, лёгкая пена у берега внутри клетки и песчаный борт снаружи.
    /// Рисуется над землёй и под деревьями, зданиями и мостами.
    /// </summary>
    public sealed class CanalRenderer : MonoBehaviour
    {
        public float CellSize { get; set; } = 1f;
        public int SortingOrder = -19;

        static readonly Color Bank = new Color32(0xD5, 0xC2, 0x91, 0xFF);
        static readonly Color Foam = new Color(0.93f, 0.98f, 1f, 0.75f);
        static readonly Color Ripple = new Color(1f, 1f, 1f, 0.35f);

        CanalMap _canals;
        Biomes _biomes;
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;
        Material _material;

        public void Bind(CanalMap canals, Biomes biomes)
        {
            Unbind();
            _canals = canals;
            _biomes = biomes;
            _canals.Changed += Redraw;
            _biomes.Changed += Redraw;
            Redraw();
        }

        void Unbind()
        {
            if (_canals != null) _canals.Changed -= Redraw;
            if (_biomes != null) _biomes.Changed -= Redraw;
        }

        void OnDestroy()
        {
            Unbind();
            if (_material != null) Destroy(_material);
        }

        static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0)
        };

        void Redraw()
        {
            if (_canals == null) return;
            _batch.Clear();
            float cs = CellSize;
            float bank = cs * 0.09f;
            float foam = cs * 0.05f;
            Color water = BiomeSpecs.Ground(Biome.Sea);

            // Борт рисуем первым, чтобы вода соседней клетки его не перекрывала.
            foreach (Vector2Int c in _canals.Cells)
            {
                Vector2 min = new Vector2(c.x * cs, c.y * cs);
                Vector2 max = min + new Vector2(cs, cs);
                foreach (Vector2Int d in Dirs)
                {
                    if (_biomes.IsWater(c + d)) continue;
                    if (d.y > 0) _batch.Rect(new Vector2(min.x - bank, max.y), new Vector2(max.x + bank, max.y + bank), Bank);
                    else if (d.y < 0) _batch.Rect(new Vector2(min.x - bank, min.y - bank), new Vector2(max.x + bank, min.y), Bank);
                    else if (d.x > 0) _batch.Rect(new Vector2(max.x, min.y), new Vector2(max.x + bank, max.y), Bank);
                    else _batch.Rect(new Vector2(min.x - bank, min.y), new Vector2(min.x, max.y), Bank);
                }
            }

            foreach (Vector2Int c in _canals.Cells)
            {
                Vector2 min = new Vector2(c.x * cs, c.y * cs);
                Vector2 max = min + new Vector2(cs, cs);
                _batch.Rect(min, max, water);

                foreach (Vector2Int d in Dirs)
                {
                    if (_biomes.IsWater(c + d)) continue;
                    if (d.y > 0) _batch.Rect(new Vector2(min.x, max.y - foam), max, Foam);
                    else if (d.y < 0) _batch.Rect(min, new Vector2(max.x, min.y + foam), Foam);
                    else if (d.x > 0) _batch.Rect(new Vector2(max.x - foam, min.y), max, Foam);
                    else _batch.Rect(min, new Vector2(min.x + foam, max.y), Foam);
                }

                // Рябь: короткая светлая черта, положение зависит от клетки.
                int h = ((c.x * 73856093) ^ (c.y * 19349663)) & 0x7fffffff;
                if (h % 3 == 0)
                {
                    float ox = (h >> 4) % 5 * 0.1f + 0.2f;
                    float oy = (h >> 8) % 5 * 0.12f + 0.2f;
                    Vector2 p = min + new Vector2(ox, oy) * cs;
                    _batch.Rect(p, p + new Vector2(cs * 0.22f, cs * 0.025f), Ripple);
                }
            }

            if (_view == null)
            {
                _view = new GameObject("Canals");
                _view.transform.SetParent(transform, false);
                _view.AddComponent<MeshFilter>();
                _material = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
                var mr = _view.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material;
                mr.sortingOrder = SortingOrder;
            }

            var f = _view.GetComponent<MeshFilter>();
            f.sharedMesh = _batch.ToMesh(f.sharedMesh);
        }
    }
}
