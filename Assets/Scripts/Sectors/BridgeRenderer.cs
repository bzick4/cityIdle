using CityGame.Buildings;
using CityGame.CellRoads;
using UnityEngine;

namespace CityGame.Sectors
{
    /// <summary>
    /// Дороги на море это мосты: под полотном каменная плита с перилами и тенью на воде.
    /// Рисуется под самой дорогой, поэтому дорога и машины лежат поверх.
    /// </summary>
    public sealed class BridgeRenderer : MonoBehaviour
    {
        public float CellSize = 1f;
        [Tooltip("Ширина плиты моста, доля клетки")]
        public float DeckWidth = 0.8f;
        public int SortingOrder = -3;

        CellRoadMap _map;
        Biomes _biomes;
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;

        static readonly Color Deck = new Color32(0xD9, 0xDD, 0xE1, 0xFF);
        static readonly Color Rail = new Color32(0x5A, 0x66, 0x72, 0xFF);
        static readonly Color Shadow = new Color(0.1f, 0.25f, 0.4f, 0.28f);

        public void Bind(CellRoadMap map, Biomes biomes)
        {
            if (_map != null) _map.Changed -= Redraw;
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
        }

        void Redraw()
        {
            if (_map == null) return;
            _batch.Clear();
            float cs = CellSize;
            float dw = cs * DeckWidth * 0.5f;
            float rail = cs * 0.04f;
            Vector2 so = new Vector2(0.05f, -0.08f) * cs;

            // Сначала все тени, потом плиты, потом перила, чтобы тень не ложилась на соседнюю плиту.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var kv in _map.Cells)
                {
                    if (!_biomes.IsWater(kv.Key)) continue;
                    Vector2 c = new Vector2((kv.Key.x + 0.5f) * cs, (kv.Key.y + 0.5f) * cs);
                    int mask = kv.Value;

                    if (pass < 2)
                    {
                        Color col = pass == 0 ? Shadow : Deck;
                        Vector2 o = pass == 0 ? so : Vector2.zero;
                        _batch.Rect(c + o - new Vector2(dw, dw), c + o + new Vector2(dw, dw), col);
                        foreach (int side in Side.All)
                        {
                            if ((mask & side) == 0) continue;
                            Vector2 d = Side.Vec(side);
                            Vector2 p = new Vector2(-d.y, d.x);
                            _batch.Local(c + o, d, p, 0f, cs * 0.5f + (pass == 1 ? 0.002f : 0f), -dw, dw, col);
                        }

                        continue;
                    }

                    // Перила: вдоль рукавов по обеим сторонам, и по сторонам центра без рукава.
                    foreach (int side in Side.All)
                    {
                        Vector2 d = Side.Vec(side);
                        Vector2 p = new Vector2(-d.y, d.x);
                        if ((mask & side) != 0)
                        {
                            _batch.Local(c, d, p, dw, cs * 0.5f + 0.002f, dw - rail, dw, Rail);
                            _batch.Local(c, d, p, dw, cs * 0.5f + 0.002f, -dw, -dw + rail, Rail);
                        }
                        else
                        {
                            _batch.Local(c, d, p, dw - rail, dw, -dw, dw, Rail);
                        }
                    }
                }
            }

            if (_view == null)
            {
                _view = new GameObject("Bridges");
                _view.transform.SetParent(transform, false);
                _view.AddComponent<MeshFilter>();
                var mr = _view.AddComponent<MeshRenderer>();
                mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
                mr.sortingOrder = SortingOrder;
            }

            var f = _view.GetComponent<MeshFilter>();
            f.sharedMesh = _batch.ToMesh(f.sharedMesh);
        }
    }
}
