using System.Collections.Generic;
using CityGame.Flow;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Рисует районы, один меш с цветами в вершинах на район. Слои по порядку:
    /// свечение по периметру (сильнее у довольных районов), тень, тёмная основа (даёт контур),
    /// крыша по всему району как одно целое, детали крыши по типу: двускатная у жилья, навес и окно у торговли,
    /// пилообразная с трубой у промышленности, значок у служб.
    /// Слияние видно по общей крыше без швов.
    /// </summary>
    public sealed class CityRenderer : MonoBehaviour
    {
        public float CellSize = 1f;
        [Range(0.5f, 1f)] public float Fill = 0.88f;
        [Tooltip("Ширина контура, доля клетки")]
        public float RimWidth = 0.05f;
        [Tooltip("Ширина свечения, доля клетки")]
        public float GlowWidth = 0.16f;
        public int SortingOrder = -10;

        CityModel _model;
        FlowSolver _flow;
        readonly MeshBatch _batch = new MeshBatch();
        readonly MeshBatch _islandBatch = new MeshBatch();
        readonly List<GameObject> _views = new List<GameObject>();
        Material _material;
        GameObject _islandView;
        Biomes _biomes;

        public void Bind(CityModel model, FlowSolver flow = null, Biomes biomes = null)
        {
            Unbind();
            _model = model;
            _flow = flow;
            _biomes = biomes;
            _model.Rebuilt += Redraw;
            if (_flow != null) _flow.Solved += Redraw;
            Redraw();
        }

        void Unbind()
        {
            if (_model != null) _model.Rebuilt -= Redraw;
            if (_flow != null) _flow.Solved -= Redraw;
        }

        void OnDestroy()
        {
            Unbind();
        }

        void Clear()
        {
            foreach (GameObject go in _views)
            {
                if (go == null) continue;
                var f = go.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
                Destroy(go);
            }

            _views.Clear();
        }

        void Redraw()
        {
            if (_model == null) return;
            if (_material == null)
            {
                _material = new Material(Shader.Find("Sprites/Default"));
                _material.color = Color.white;
            }

            Clear();
            _islandBatch.Clear();
            foreach (District d in _model.Districts)
            {
                _batch.Clear();
                if (_biomes != null)
                    foreach (BuildingCell cell in d.Cells)
                        if (_biomes.IsSea(cell.Pos)) AddIsland(Center(cell.Pos), _islandBatch, Hash(cell.Pos));
                BuildDistrict(d, _batch);
                var go = new GameObject("District " + d.Id + " " + d.Title + " x" + d.Count);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _batch.ToMesh();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.sortingOrder = SortingOrder;
                _views.Add(go);
            }

            if (_islandBatch.V.Count > 0)
            {
                if (_islandView == null)
                {
                    _islandView = new GameObject("Sea building islands");
                    _islandView.transform.SetParent(transform, false);
                    _islandView.AddComponent<MeshFilter>();
                    var renderer = _islandView.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = _material;
                    renderer.sortingOrder = SortingOrder - 2;
                }
                var filter = _islandView.GetComponent<MeshFilter>();
                filter.sharedMesh = _islandBatch.ToMesh(filter.sharedMesh);
            }
            else if (_islandView != null)
            {
                var filter = _islandView.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
                Destroy(_islandView);
                _islandView = null;
            }
        }

        // ---------------------------------------------------------------

        static Color Lighten(Color c, float t)
        {
            return new Color(Mathf.Lerp(c.r, 1f, t), Mathf.Lerp(c.g, 1f, t), Mathf.Lerp(c.b, 1f, t), 1f);
        }

        static Color Darken(Color c, float t)
        {
            return new Color(c.r * (1f - t), c.g * (1f - t), c.b * (1f - t), 1f);
        }

        static int Hash(Vector2Int p)
        {
            return ((p.x * 73856093) ^ (p.y * 19349663)) & 0x7fffffff;
        }

        Vector2 Center(Vector2Int p)
        {
            return new Vector2((p.x + 0.5f) * CellSize, (p.y + 0.5f) * CellSize);
        }

        void AddIsland(Vector2 center, MeshBatch batch, int seed)
        {
            const int points = 10;
            float cs = CellSize;
            var shadow = new List<Vector2>(points);
            var shore = new List<Vector2>(points);
            var grass = new List<Vector2>(points);
            Vector2 shadowOffset = new Vector2(0.035f, -0.055f) * cs;
            for (int i = 0; i < points; i++)
            {
                float angle = Mathf.PI * 2f * i / points;
                int bits = (seed >> (i % 6 * 4)) & 0xF;
                float irregularity = 0.91f + bits / 15f * 0.16f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                shadow.Add(center + shadowOffset + Vector2.Scale(direction, new Vector2(cs * 0.64f, cs * 0.59f)) * irregularity);
                shore.Add(center + Vector2.Scale(direction, new Vector2(cs * 0.59f, cs * 0.55f)) * irregularity);
                grass.Add(center + Vector2.Scale(direction, new Vector2(cs * 0.49f, cs * 0.46f)) * irregularity);
            }

            batch.Polygon(shadow, new Color(0.11f, 0.25f, 0.34f, 0.28f));
            batch.Ring(center, cs * 0.61f, cs * 0.66f, 14, new Color(0.82f, 0.94f, 0.97f, 0.26f));
            batch.Polygon(shore, new Color32(0xD5, 0xC2, 0x91, 0xFF));
            batch.Polygon(grass, new Color32(0xA9, 0xC2, 0x98, 0xFF));
        }

        bool Same(District d, Vector2Int pos)
        {
            if (!CategorySpecs.Merges(d.Category)) return false;
            BuildingCell n = _model.Get(pos);
            return n != null && n.District == d;
        }

        void BuildDistrict(District d, MeshBatch m)
        {
            float cs = CellSize;
            float half = cs * Fill * 0.5f;
            float gap = cs - 2f * half;
            float rim = cs * RimWidth;
            float rr = half - rim; // половина крыши одной ячейки
            Color body = CategorySpecs.BodyColor(d.Category, d.Kind);
            bool merges = CategorySpecs.Merges(d.Category);
            float sat = merges ? Mathf.Clamp01(d.Satisfaction) : 1f;

            // 1. Свечение по периметру. Довольный район светится, недовольный нет.
            float glowA = merges ? Mathf.Lerp(0.03f, 0.32f, sat) : 0.32f;
            Color glow = Lighten(body, 0.45f);
            Color glowIn = new Color(glow.r, glow.g, glow.b, glowA);
            Color glowOut = new Color(glow.r, glow.g, glow.b, 0f);
            float g = cs * GlowWidth;
            foreach (BuildingCell c in d.Cells)
            {
                Vector2 ctr = Center(c.Pos);
                for (int side = 0; side < 4; side++)
                {
                    Vector2Int dv = SideVec(side);
                    if (Same(d, c.Pos + dv)) continue;

                    Vector2 dir = new Vector2(dv.x, dv.y);
                    Vector2 per = new Vector2(-dir.y, dir.x);
                    bool bridgePlus = Same(d, c.Pos + new Vector2Int((int)per.x, (int)per.y));
                    bool bridgeMinus = Same(d, c.Pos - new Vector2Int((int)per.x, (int)per.y));
                    Vector2 i0 = ctr + dir * half - per * half;
                    Vector2 i1 = ctr + dir * half + per * (half + (bridgePlus ? gap : 0f));
                    m.QuadGrad(i0, glowIn, i1, glowIn, i1 + dir * g, glowOut, i0 + dir * g, glowOut);

                    // Выпуклый угол: закрываем веером.
                    if (!bridgeMinus)
                    {
                        Vector2 p = i0;
                        m.QuadGrad(p, glowIn, p + dir * g, glowOut, p + dir * g - per * g, glowOut, p - per * g, glowOut);
                    }

                    if (!bridgePlus)
                    {
                        Vector2 p = ctr + dir * half + per * half;
                        m.QuadGrad(p, glowIn, p + per * g, glowOut, p + per * g + dir * g, glowOut, p + dir * g, glowOut);
                    }
                }
            }

            // 2. Тень. Прямоугольники без наложений, поэтому полупрозрачность не удваивается.
            Vector2 so = new Vector2(0.045f, -0.06f) * cs;
            Color shadow = new Color(0.07f, 0.09f, 0.12f, 0.22f);
            ForEachFootprint(d, half, gap, (min, max) => m.Rect(min + so, max + so, shadow), 0f);

            // 3. Основа тёмнее крыши, из-за неё получается контур.
            Color rimColor = Darken(body, 0.28f);
            ForEachFootprint(d, half, gap, (min, max) => m.Rect(min, max, rimColor), 0.002f);

            // 4. Крыша по району единым полотном.
            ForEachFootprint(d, half, gap, (min, max) => m.Rect(min, max, body), -rim);

            // 5. Детали крыши по ячейкам.
            foreach (BuildingCell c in d.Cells)
            {
                Vector2 ctr = Center(c.Pos);
                int h = Hash(c.Pos);
                bool flip = (h & 1) == 1;
                Vector2 u = flip ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
                Vector2 v = flip ? new Vector2(1f, 0f) : new Vector2(0f, 1f);

                switch (d.Category)
                {
                    case BuildingCategory.Residential: DrawHouse(m, ctr, u, v, rr, cs, body, h); break;
                    case BuildingCategory.Commercial: DrawShop(m, ctr, u, v, rr, body); break;
                    case BuildingCategory.Industrial: DrawFactory(m, ctr, u, v, rr, cs, body); break;
                    default: DrawService(m, ctr, rr, d.Kind, body); break;
                }
            }
        }

        static Vector2Int SideVec(int side)
        {
            switch (side)
            {
                case 0: return new Vector2Int(0, 1);
                case 1: return new Vector2Int(1, 0);
                case 2: return new Vector2Int(0, -1);
                default: return new Vector2Int(-1, 0);
            }
        }

        /// <summary>
        /// Перебирает прямоугольники района: ячейки, перемычки между соседями и центральные заплатки.
        /// grow больше нуля расширяет прямоугольник, меньше нуля сужает (крыша: ячейка уже, перемычка
        /// уже поперёк и шире вдоль, чтобы крыша шла без швов).
        /// </summary>
        void ForEachFootprint(District d, float half, float gap, System.Action<Vector2, Vector2> draw, float grow)
        {
            bool roof = grow < 0f;
            float inset = -grow;
            bool merges = CategorySpecs.Merges(d.Category);
            float cs = CellSize;

            foreach (BuildingCell c in d.Cells)
            {
                Vector2 p = Center(c.Pos);
                float e = roof ? half - inset : half + grow;
                draw(p - new Vector2(e, e), p + new Vector2(e, e));
                if (!merges) continue;

                bool right = Same(d, c.Pos + Vector2Int.right);
                bool up = Same(d, c.Pos + Vector2Int.up);
                float along = roof ? inset : grow; // насколько перемычка заходит в соседние ячейки
                float across = roof ? half - inset : half + grow;

                if (right)
                    draw(new Vector2(p.x + half - along, p.y - across), new Vector2(p.x + cs - half + along, p.y + across));
                if (up)
                    draw(new Vector2(p.x - across, p.y + half - along), new Vector2(p.x + across, p.y + cs - half + along));
                if (right && up && Same(d, c.Pos + new Vector2Int(1, 1)))
                    draw(new Vector2(p.x + half - along, p.y + half - along),
                        new Vector2(p.x + cs - half + along, p.y + cs - half + along));
            }
        }

        // --- детали крыш ---

        void DrawHouse(MeshBatch m, Vector2 c, Vector2 u, Vector2 v, float r, float cs, Color body, int h)
        {
            m.Local(c, u, v, -r, r, 0f, r, Lighten(body, 0.14f));
            m.Local(c, u, v, -r, r, -r, 0f, Darken(body, 0.14f));
            m.Local(c, u, v, -r, r, -cs * 0.018f, cs * 0.018f, Lighten(body, 0.45f));
            if ((h >> 3) % 3 == 0)
                m.Local(c, u, v, r * 0.45f, r * 0.75f, -r * 0.8f, -r * 0.5f, Darken(body, 0.4f));
        }

        void DrawShop(MeshBatch m, Vector2 c, Vector2 u, Vector2 v, float r, Color body)
        {
            // Полосатый навес по одной стороне.
            const int n = 5;
            Color stripeA = Color.Lerp(body, Color.white, 0.8f);
            Color stripeB = Lighten(body, 0.1f);
            float w = 2f * r / n;
            for (int i = 0; i < n; i++)
                m.Local(c, u, v, -r + i * w, -r + (i + 1) * w, r * 0.5f, r, i % 2 == 0 ? stripeA : stripeB);

            // Световой фонарь на крыше.
            m.Local(c, u, v, -r * 0.5f, r * 0.5f, -r * 0.6f, r * 0.25f, Darken(body, 0.18f));
            m.Local(c, u, v, -r * 0.4f, r * 0.4f, -r * 0.5f, r * 0.15f, Lighten(body, 0.4f));
        }

        void DrawFactory(MeshBatch m, Vector2 c, Vector2 u, Vector2 v, float r, float cs, Color body)
        {
            // Пилообразная крыша: тёмные скаты через равные полосы.
            float band = 2f * r / 3f;
            for (int i = 0; i < 3; i++)
            {
                float v0 = -r + i * band;
                m.Local(c, u, v, -r, r, v0, v0 + band * 0.45f, Darken(body, 0.2f));
                m.Local(c, u, v, -r, r, v0 + band * 0.45f, v0 + band * 0.5f, Lighten(body, 0.35f));
            }

            // Труба и дымок.
            Vector2 chimney = c + u * (r * 0.62f) + v * (r * 0.62f);
            m.Local(chimney, u, v, -r * 0.2f, r * 0.2f, -r * 0.2f, r * 0.2f, Darken(body, 0.55f));
            m.Disc(chimney + u * (cs * 0.04f) + v * (cs * 0.05f), cs * 0.05f, 10, new Color(0.95f, 0.95f, 0.95f, 0.75f));
        }

        void DrawService(MeshBatch m, Vector2 c, float r, ServiceKind kind, Color body)
        {
            float s = r * 0.72f;
            switch (kind)
            {
                case ServiceKind.Ambulance:
                {
                    Color red = new Color32(0xE0, 0x45, 0x45, 0xFF);
                    m.Rect(c + new Vector2(-s * 0.3f, -s), c + new Vector2(s * 0.3f, s), red);
                    m.Rect(c + new Vector2(-s, -s * 0.3f), c + new Vector2(s, s * 0.3f), red);
                    break;
                }
                case ServiceKind.Police:
                {
                    var shield = new List<Vector2>
                    {
                        c + new Vector2(-s * 0.85f, s), c + new Vector2(s * 0.85f, s), c + new Vector2(s * 0.85f, -s * 0.1f),
                        c + new Vector2(0f, -s * 1.05f), c + new Vector2(-s * 0.85f, -s * 0.1f)
                    };
                    m.Polygon(shield, Color.white);
                    var star = new List<Vector2>
                    {
                        c + new Vector2(0f, s * 0.55f), c + new Vector2(s * 0.28f, s * 0.12f),
                        c + new Vector2(0f, -s * 0.4f), c + new Vector2(-s * 0.28f, s * 0.12f)
                    };
                    m.Polygon(star, body);
                    break;
                }
                case ServiceKind.PowerPlant:
                {
                    Color bolt = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
                    m.Polygon(new List<Vector2>
                    {
                        c + new Vector2(s * 0.15f, s * 1.05f), c + new Vector2(-s * 0.6f, -s * 0.1f),
                        c + new Vector2(-s * 0.05f, -s * 0.1f), c + new Vector2(-s * 0.2f, -s * 1.05f),
                        c + new Vector2(s * 0.6f, s * 0.15f), c + new Vector2(s * 0.05f, s * 0.15f)
                    }, bolt);
                    break;
                }
                case ServiceKind.WaterTower:
                {
                    Color drop = new Color32(0x4F, 0xA3, 0xE0, 0xFF);
                    m.Tri(c + new Vector2(-s * 0.55f, -s * 0.05f), c + new Vector2(0f, s * 1.05f),
                        c + new Vector2(s * 0.55f, -s * 0.05f), drop);
                    m.Disc(c + new Vector2(0f, -s * 0.3f), s * 0.6f, 16, drop);
                    m.Disc(c + new Vector2(-s * 0.2f, -s * 0.2f), s * 0.14f, 8, Color.white);
                    break;
                }
                default:
                {
                    // Пламя: круг внизу и острие вверх.
                    m.Disc(c + new Vector2(0f, -s * 0.3f), s * 0.55f, 16, Color.white);
                    m.Tri(c + new Vector2(-s * 0.5f, -s * 0.15f), c + new Vector2(0f, s * 1.05f),
                        c + new Vector2(s * 0.5f, -s * 0.15f), Color.white);
                    m.Disc(c + new Vector2(0f, -s * 0.38f), s * 0.24f, 12, body);
                    break;
                }
            }
        }
    }
}
