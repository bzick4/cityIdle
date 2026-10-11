using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Показывает зону действия служб: тонкое кольцо радиуса службы и лёгкая заливка круга.
    /// Когда выбран инструмент «Служба», зона ярче, а накрытые клетки подкрашены цветом службы.
    /// </summary>
    public sealed class ServiceOverlay : MonoBehaviour
    {
        public float CellSize = 1f;
        public int SortingOrder = -12;

        /// <summary>Усиленный показ, пока игрок выбирает место для службы.</summary>
        public bool Emphasis
        {
            get => _emphasis;
            set
            {
                if (_emphasis == value) return;
                _emphasis = value;
                Redraw();
            }
        }

        CityModel _model;
        ServiceCoverage _coverage;
        bool _emphasis;
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;
        Material _material;

        public void Bind(CityModel model, ServiceCoverage coverage)
        {
            if (_model != null) _model.Rebuilt -= Redraw;
            _model = model;
            _coverage = coverage;
            _model.Rebuilt += Redraw;
            Redraw();
        }

        void OnDestroy()
        {
            if (_model != null) _model.Rebuilt -= Redraw;
        }

        void Redraw()
        {
            if (_model == null) return;
            if (_material == null)
            {
                _material = new Material(Shader.Find("Sprites/Default"));
                _material.color = Color.white;
            }

            _batch.Clear();
            float cs = CellSize;

            if (_emphasis && _coverage != null)
            {
                foreach (var kv in _coverage.Mask)
                {
                    Vector2 c = new Vector2((kv.Key.x + 0.5f) * cs, (kv.Key.y + 0.5f) * cs);
                    for (int k = 0; k < 3; k++)
                    {
                        if ((kv.Value & (1 << k)) == 0) continue;
                        Color col = ServiceSpecs.Color((ServiceKind)k);
                        col.a = 0.16f;
                        float e = cs * 0.46f;
                        _batch.Rect(c - new Vector2(e, e), c + new Vector2(e, e), col);
                    }
                }
            }

            foreach (BuildingCell cell in _model.Cells)
            {
                if (cell.Category != BuildingCategory.Service) continue;
                Vector2 c = new Vector2((cell.Pos.x + 0.5f) * cs, (cell.Pos.y + 0.5f) * cs);
                float r = ServiceSpecs.Radius(cell.Kind) * cs;
                Color col = ServiceSpecs.Color(cell.Kind);
                Color fill = col; fill.a = _emphasis ? 0.05f : 0.03f;
                Color ring = col; ring.a = _emphasis ? 0.75f : 0.3f;
                _batch.Disc(c, r, 48, fill);
                _batch.Ring(c, r - cs * 0.035f, r, 64, ring);
            }

            if (_view == null)
            {
                _view = new GameObject("Service overlay");
                _view.transform.SetParent(transform, false);
                _view.AddComponent<MeshFilter>();
                var r = _view.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.sortingOrder = SortingOrder;
            }

            var f = _view.GetComponent<MeshFilter>();
            f.sharedMesh = _batch.ToMesh(f.sharedMesh);
        }
    }
}
