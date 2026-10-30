using CityGame.Buildings;
using CityGame.CellRoads;
using UnityEngine;

namespace CityGame.Transit
{
    /// <summary>Автостанция: площадка, навес с лавкой и синий знак. Навес повёрнут от дороги, бордюр к ней.</summary>
    public sealed class BusStationRenderer : MonoBehaviour
    {
        public float CellSize { get; set; } = 1f;
        public int SortingOrder = -9;

        static readonly Color Pad = new Color32(0xE3, 0xE0, 0xD8, 0xFF);
        static readonly Color PadEdge = new Color32(0xB9, 0xB5, 0xAB, 0xFF);
        static readonly Color Roof = new Color32(0x2E, 0x8B, 0x77, 0xFF);
        static readonly Color RoofLight = new Color32(0x6C, 0xC4, 0xAE, 0xFF);
        static readonly Color Wood = new Color32(0x9C, 0x72, 0x48, 0xFF);
        static readonly Color Sign = new Color32(0x3D, 0x5A, 0xA8, 0xFF);
        static readonly Color Steel = new Color32(0x4B, 0x54, 0x5E, 0xFF);

        BusMap _buses;
        CellRoadMap _roads;
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;
        Material _material;

        public void Bind(BusMap buses, CellRoadMap roads)
        {
            if (_buses != null) _buses.Changed -= Redraw;
            if (_roads != null) _roads.Changed -= Redraw;
            _buses = buses;
            _roads = roads;
            _buses.Changed += Redraw;
            _roads.Changed += Redraw;
            Redraw();
        }

        void OnDestroy()
        {
            if (_buses != null) _buses.Changed -= Redraw;
            if (_roads != null) _roads.Changed -= Redraw;
            if (_material != null) Destroy(_material);
        }

        void Redraw()
        {
            if (_buses == null) return;
            _batch.Clear();
            float cs = CellSize;
            foreach (Vector2Int p in _buses.Cells)
            {
                Vector2 c = new Vector2((p.x + 0.5f) * cs, (p.y + 0.5f) * cs);
                // u смотрит в сторону дороги, v поперёк.
                Vector2 u = Vector2.down;
                foreach (int side in Side.All)
                {
                    Vector2Int n = p + Side.Vec(side);
                    if (!_roads.Has(n)) continue;
                    Vector2Int d = Side.Vec(side);
                    u = new Vector2(d.x, d.y);
                    break;
                }

                Vector2 v = new Vector2(-u.y, u.x);
                float r = cs * 0.44f;

                _batch.Local(c + new Vector2(0.05f, -0.06f) * cs, u, v, -r, r, -r, r, new Color(0f, 0f, 0f, 0.14f));
                _batch.Local(c, u, v, -r, r, -r, r, PadEdge);
                _batch.Local(c, u, v, -r + cs * 0.03f, r - cs * 0.03f, -r + cs * 0.03f, r - cs * 0.03f, Pad);
                // Бордюр у дороги и разметка остановки.
                _batch.Local(c, u, v, r - cs * 0.1f, r - cs * 0.06f, -r, r, PadEdge);
                _batch.Local(c, u, v, r - cs * 0.2f, r - cs * 0.15f, -r * 0.6f, r * 0.6f, Color.white);

                // Навес в глубине площадки, лавка под ним.
                _batch.Local(c, u, v, -r * 0.62f, -r * 0.1f, -r * 0.7f, r * 0.7f, new Color(0f, 0f, 0f, 0.16f));
                _batch.Local(c, u, v, -r * 0.58f, -r * 0.14f, -r * 0.66f, r * 0.66f, Roof);
                _batch.Local(c, u, v, -r * 0.58f, -r * 0.44f, -r * 0.66f, r * 0.66f, RoofLight);
                _batch.Local(c, u, v, -r * 0.84f, -r * 0.7f, -r * 0.4f, r * 0.4f, Wood);

                // Знак: столб и синяя табличка с силуэтом автобуса.
                Vector2 sp = c + u * (r * 0.28f) + v * (r * 0.78f);
                _batch.Local(sp, u, v, -r * 0.03f, r * 0.03f, -r * 0.03f, r * 0.03f, Steel);
                _batch.Disc(sp, r * 0.22f, 14, Sign);
                _batch.Local(sp, u, v, -r * 0.12f, r * 0.12f, -r * 0.17f, r * 0.17f, Color.white);
                _batch.Local(sp, u, v, -r * 0.05f, r * 0.05f, -r * 0.1f, r * 0.1f, Sign);
            }

            if (_view == null)
            {
                _view = new GameObject("Bus stations");
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
