using CityGame.Buildings;
using CityGame.CellRoads;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.Sectors
{
    /// <summary>
    /// Дороги в горах идут тоннелями: поверх полотна лежит каменный гребень, а машины под ним скрыты.
    /// Там, где дорога выходит из гор или обрывается, остаётся короткий открытый участок и тёмный портал.
    /// Рисуется выше машин и ниже ночного полотна.
    /// </summary>
    public sealed class TunnelRenderer : MonoBehaviour
    {
        public float CellSize = 1f;
        [Tooltip("Ширина гребня над тоннелем, доля клетки")]
        public float RidgeWidth = 0.8f;
        [Tooltip("Длина открытого участка у портала, доля клетки")]
        public float PortalGap = 0.3f;
        public int SortingOrder = 17;

        CellRoadMap _map;
        Biomes _biomes;
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;
        Material _material;

        static readonly Color Shadow = new Color(0.12f, 0.14f, 0.17f, 0.3f);
        static readonly Color Rock = new Color32(0x7C, 0x83, 0x8A, 0xFF);
        static readonly Color RockLight = new Color32(0x92, 0x99, 0xA0, 0xFF);
        static readonly Color Portal = new Color32(0x25, 0x2A, 0x31, 0xFF);
        static readonly Color PortalFrame = new Color32(0xB4, 0xB9, 0xBE, 0xFF);

        public void Bind(CellRoadMap map, Biomes biomes)
        {
            if (_map != null) _map.Changed -= Redraw;
            if (_biomes != null) _biomes.Changed -= Redraw;
            _map = map;
            _biomes = biomes;
            _map.Changed += Redraw;
            _biomes.Changed += Redraw;
            Redraw();
        }

        void OnDestroy()
        {
            if (_map != null) _map.Changed -= Redraw;
            if (_biomes != null) _biomes.Changed -= Redraw;
            if (_material != null) Destroy(_material);
        }

        bool InMountain(Vector2Int cell)
        {
            return _biomes.BiomeAt(cell) == Biome.Mountain;
        }

        /// <summary>Сторона клетки, откуда въезжают в тоннель: дальше нет дороги в горах.</summary>
        bool IsPortalSide(Vector2Int cell, int side)
        {
            Vector2Int n = cell + Side.Vec(side);
            return !(_map.Has(n) && InMountain(n) && (_map.Mask(n) & Side.Opposite(side)) != 0);
        }

        void Redraw()
        {
            if (_map == null) return;
            _batch.Clear();
            float cs = CellSize;
            float hw = cs * RidgeWidth * 0.5f;
            float gap = cs * PortalGap;
            Vector2 so = new Vector2(0.04f, -0.06f) * cs;

            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var kv in _map.Cells)
                {
                    Vector2Int cell = kv.Key;
                    if (!InMountain(cell)) continue;
                    int mask = kv.Value;
                    Vector2 c = new Vector2((cell.x + 0.5f) * cs, (cell.y + 0.5f) * cs);

                    float w = pass == 2 ? hw * 0.55f : hw;
                    Color col = pass == 0 ? Shadow : pass == 1 ? Rock : RockLight;
                    Vector2 o = pass == 0 ? so : Vector2.zero;
                    // Центр гребня и рукава. Рукав к порталу короче, чтобы оставить открытую дорогу.
                    float eN = w, eE = w, eS = w, eW = w;
                    foreach (int side in Side.All)
                    {
                        if ((mask & side) == 0) continue;
                        Vector2 d = Side.Vec(side);
                        Vector2 p = new Vector2(-d.y, d.x);
                        bool portal = IsPortalSide(cell, side);
                        float reach = portal ? cs * 0.5f - gap : cs * 0.5f + 0.003f;
                        _batch.Local(c + o, d, p, 0f, Mathf.Max(0f, reach), -w, w, col);
                        // Центр не должен закрывать открытый участок у портала.
                        if (portal)
                        {
                            float e = Mathf.Min(w, reach);
                            if (side == Side.N) eN = e;
                            else if (side == Side.E) eE = e;
                            else if (side == Side.S) eS = e;
                            else eW = e;
                        }
                    }

                    _batch.Rect(c + o - new Vector2(eW, eS), c + o + new Vector2(eE, eN), col);
                }
            }

            // Порталы: тёмная арка и светлая рамка на конце гребня.
            foreach (var kv in _map.Cells)
            {
                Vector2Int cell = kv.Key;
                if (!InMountain(cell)) continue;
                Vector2 c = new Vector2((cell.x + 0.5f) * cs, (cell.y + 0.5f) * cs);
                foreach (int side in Side.All)
                {
                    if ((kv.Value & side) == 0 || !IsPortalSide(cell, side)) continue;
                    Vector2 d = Side.Vec(side);
                    Vector2 p = new Vector2(-d.y, d.x);
                    float edge = cs * 0.5f - gap;
                    _batch.Local(c, d, p, edge - cs * 0.02f, edge + cs * 0.05f, -hw * 0.82f, hw * 0.82f, PortalFrame);
                    _batch.Local(c, d, p, edge - cs * 0.02f, edge + cs * 0.035f, -hw * 0.66f, hw * 0.66f, Portal);
                }
            }

            if (_view == null)
            {
                _view = new GameObject("Tunnels");
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
