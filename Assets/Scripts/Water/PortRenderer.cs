using CityGame.Buildings;
using UnityEngine;

namespace CityGame.Water
{
    /// <summary>Порт: деревянный пирс на воде, контейнеры и портовый кран. Один порт это одна клетка.</summary>
    public sealed class PortRenderer : MonoBehaviour
    {
        public float CellSize = 1f;
        public int SortingOrder = -9;

        static readonly Color Rim = new Color32(0x7A, 0x5A, 0x3C, 0xFF);
        static readonly Color Deck = new Color32(0xB9, 0x8B, 0x5E, 0xFF);
        static readonly Color Plank = new Color32(0x9C, 0x72, 0x48, 0xFF);
        static readonly Color Steel = new Color32(0x7A, 0x83, 0x8C, 0xFF);
        static readonly Color SteelDark = new Color32(0x4B, 0x54, 0x5E, 0xFF);
        static readonly Color[] Boxes =
        {
            new Color32(0xD9, 0x4F, 0x45, 0xFF), new Color32(0x3D, 0x5A, 0xA8, 0xFF),
            new Color32(0xF2, 0xC1, 0x4E, 0xFF), new Color32(0x6F, 0xBF, 0x73, 0xFF)
        };

        PortMap _ports;
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;
        Material _material;

        public void Bind(PortMap ports)
        {
            if (_ports != null) _ports.Changed -= Redraw;
            _ports = ports;
            _ports.Changed += Redraw;
            Redraw();
        }

        void OnDestroy()
        {
            if (_ports != null) _ports.Changed -= Redraw;
            if (_material != null) Destroy(_material);
        }

        void Redraw()
        {
            if (_ports == null) return;
            _batch.Clear();
            float cs = CellSize;
            foreach (Vector2Int p in _ports.Cells)
            {
                Vector2 c = new Vector2((p.x + 0.5f) * cs, (p.y + 0.5f) * cs);
                int h = ((p.x * 73856093) ^ (p.y * 19349663)) & 0x7fffffff;
                bool flip = (h & 1) == 1;
                Vector2 u = flip ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
                Vector2 v = flip ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
                float r = cs * 0.44f;

                // Тень на воде, борт и настил.
                _batch.Rect(c + new Vector2(0.05f, -0.07f) * cs - new Vector2(r, r),
                    c + new Vector2(0.05f, -0.07f) * cs + new Vector2(r, r), new Color(0.1f, 0.25f, 0.4f, 0.3f));
                _batch.Local(c, u, v, -r, r, -r, r, Rim);
                _batch.Local(c, u, v, -r + cs * 0.03f, r - cs * 0.03f, -r + cs * 0.03f, r - cs * 0.03f, Deck);
                for (int i = -2; i <= 2; i++)
                    _batch.Local(c, u, v, -r + cs * 0.03f, r - cs * 0.03f, i * cs * 0.16f - cs * 0.008f,
                        i * cs * 0.16f + cs * 0.008f, Plank);

                // Контейнеры.
                Color a = Boxes[(h >> 3) % Boxes.Length];
                Color b = Boxes[(h >> 6) % Boxes.Length];
                _batch.Local(c, u, v, -r * 0.8f, -r * 0.1f, -r * 0.8f, -r * 0.35f, a);
                _batch.Local(c, u, v, -r * 0.8f, -r * 0.1f, -r * 0.5f, -r * 0.35f, Color.Lerp(a, Color.white, 0.25f));
                _batch.Local(c, u, v, -r * 0.8f, -r * 0.1f, -r * 0.3f, r * 0.1f, b);
                _batch.Local(c, u, v, -r * 0.8f, -r * 0.1f, -r * 0.05f, r * 0.1f, Color.Lerp(b, Color.white, 0.25f));

                // Кран: основание, стрела и крюк.
                Vector2 baseC = c + u * (r * 0.55f) + v * (r * 0.55f);
                _batch.Local(baseC, u, v, -r * 0.18f, r * 0.18f, -r * 0.18f, r * 0.18f, SteelDark);
                _batch.Local(baseC, u, v, -r * 0.12f, r * 0.12f, -r * 0.12f, r * 0.12f, Steel);
                _batch.Local(baseC, u, v, -r * 1.05f, r * 0.1f, -r * 0.04f, r * 0.04f, SteelDark);
                _batch.Disc(baseC - u * (r * 0.95f), cs * 0.04f, 8, new Color32(0xE0, 0x45, 0x45, 0xFF));
            }

            if (_view == null)
            {
                _view = new GameObject("Ports");
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
