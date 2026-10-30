using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>Значки над зданиями без тока (жёлтая молния) и без воды (синяя капля).</summary>
    public sealed class UtilityMarkers : MonoBehaviour
    {
        public float CellSize { get; set; } = 1f;
        public int SortingOrder = -4;

        CityModel _model;
        UtilityNet _net;
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;
        Material _material;

        public void Bind(CityModel model, UtilityNet net)
        {
            if (_net != null) _net.Changed -= Redraw;
            _model = model;
            _net = net;
            _net.Changed += Redraw;
            Redraw();
        }

        void OnDestroy()
        {
            if (_net != null) _net.Changed -= Redraw;
        }

        void Redraw()
        {
            if (_model == null || _net == null) return;
            if (_material == null)
            {
                _material = new Material(Shader.Find("Sprites/Default"));
                _material.color = Color.white;
            }

            _batch.Clear();
            float cs = CellSize;
            Color bolt = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
            Color drop = new Color32(0x4F, 0xA3, 0xE0, 0xFF);
            Color back = new Color(1f, 1f, 1f, 0.92f);
            foreach (BuildingCell cell in _model.Cells)
            {
                if (cell.Category == BuildingCategory.Service) continue;
                bool p = _net.LacksPower(cell.Pos), w = _net.LacksWater(cell.Pos);
                if (!p && !w) continue;
                Vector2 c = new Vector2((cell.Pos.x + 0.5f) * cs, (cell.Pos.y + 0.5f) * cs);
                float r = cs * 0.2f;
                int n = (p ? 1 : 0) + (w ? 1 : 0);
                float x0 = c.x - (n - 1) * r * 1.1f;
                if (p)
                {
                    Vector2 m = new Vector2(x0, c.y + cs * 0.28f);
                    _batch.Disc(m, r, 14, back);
                    _batch.Tri(m + new Vector2(r * 0.15f, r * 0.75f), m + new Vector2(-r * 0.5f, -r * 0.05f), m + new Vector2(r * 0.12f, -r * 0.05f), bolt);
                    _batch.Tri(m + new Vector2(-r * 0.15f, -r * 0.75f), m + new Vector2(r * 0.5f, r * 0.05f), m + new Vector2(-r * 0.12f, r * 0.05f), bolt);
                    x0 += r * 2.2f;
                }

                if (w)
                {
                    Vector2 m = new Vector2(x0, c.y + cs * 0.28f);
                    _batch.Disc(m, r, 14, back);
                    _batch.Tri(m + new Vector2(-r * 0.4f, 0f), m + new Vector2(0f, r * 0.75f), m + new Vector2(r * 0.4f, 0f), drop);
                    _batch.Disc(m + new Vector2(0f, -r * 0.2f), r * 0.42f, 10, drop);
                }
            }

            if (_view == null)
            {
                _view = new GameObject("Utility markers");
                _view.transform.SetParent(transform, false);
                _view.AddComponent<MeshFilter>();
                var rd = _view.AddComponent<MeshRenderer>();
                rd.sharedMaterial = _material;
                rd.sortingOrder = SortingOrder;
            }

            var f = _view.GetComponent<MeshFilter>();
            f.sharedMesh = _batch.ToMesh(f.sharedMesh);
        }
    }
}
