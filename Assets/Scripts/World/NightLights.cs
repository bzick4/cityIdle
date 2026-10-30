using CityGame.Buildings;
using CityGame.CellRoads;
using CityGame.Sectors;
using CityGame.Transit;
using CityGame.Water;
using UnityEngine;

namespace CityGame.World
{
    /// <summary>Общие приёмы для огней: мягкое свечение это диск, у которого центр непрозрачный, а край прозрачный.</summary>
    public static class LightMesh
    {
        public static void Glow(MeshBatch b, Vector2 center, float radius, Color col, int segments = 14)
        {
            int c = b.Add(center, col);
            Color edge = new Color(col.r, col.g, col.b, 0f);
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                b.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, edge);
            }

            for (int i = 0; i < segments; i++)
            {
                b.T.Add(c);
                b.T.Add(c + 1 + i);
                b.T.Add(c + 1 + (i + 1) % segments);
            }
        }
    }

    /// <summary>
    /// Огни города ночью: фонари вдоль дорог через клетку, светящиеся окна жилья, неон торговли,
    /// огни промышленности, маячки служб и фонари портов. Меш собирается при изменениях,
    /// а яркость задаёт материал DayNight.LightMaterial.
    /// </summary>
    public sealed class NightLights : MonoBehaviour
    {
        public float CellSize { get; set; } = 1f;

        static readonly Color Warm = new Color(1f, 0.86f, 0.5f, 1f);
        static readonly Color Cool = new Color(0.8f, 0.94f, 1f, 1f);

        CellRoadMap _roads;
        CityModel _model;
        PortMap _ports;
        DayNight _day;

        /// <summary>Для пропуска фонарей в тоннелях. Можно не задавать.</summary>
        public Biomes Biomes;

        BusMap _buses;

        /// <summary>Автостанции светятся ночью. Можно не задавать.</summary>
        public void SetBuses(BusMap buses)
        {
            if (_buses != null) _buses.Changed -= MarkDirty;
            _buses = buses;
            if (_buses != null) _buses.Changed += MarkDirty;
            _dirty = true;
        }
        readonly MeshBatch _batch = new MeshBatch();
        GameObject _view;
        bool _dirty = true;

        public void Bind(CellRoadMap roads, CityModel model, PortMap ports, DayNight day)
        {
            Unbind();
            _roads = roads;
            _model = model;
            _ports = ports;
            _day = day;
            _roads.Changed += MarkDirty;
            _model.Rebuilt += MarkDirty;
            _ports.Changed += MarkDirty;
            _dirty = true;
        }

        void Unbind()
        {
            if (_roads != null) _roads.Changed -= MarkDirty;
            if (_model != null) _model.Rebuilt -= MarkDirty;
            if (_ports != null) _ports.Changed -= MarkDirty;
            if (_buses != null) _buses.Changed -= MarkDirty;
        }

        void OnDestroy()
        {
            Unbind();
        }

        void MarkDirty()
        {
            _dirty = true;
        }

        void LateUpdate()
        {
            if (_day == null) return;
            if (_view != null) _view.SetActive(_day.LightLevel > 0.01f);
            if (!_dirty || _day.LightLevel <= 0.01f && _view != null) return;
            _dirty = false;
            Rebuild();
        }

        static int Hash(Vector2Int p)
        {
            return ((p.x * 73856093) ^ (p.y * 19349663)) & 0x7fffffff;
        }

        Vector2 Center(Vector2Int p)
        {
            return new Vector2((p.x + 0.5f) * CellSize, (p.y + 0.5f) * CellSize);
        }

        void Rebuild()
        {
            _batch.Clear();
            float cs = CellSize;

            // Фонари: каждая вторая клетка прямого участка, у края полотна.
            foreach (var kv in _roads.Cells)
            {
                Vector2Int p = kv.Key;
                if (((p.x + p.y) & 1) != 0) continue;
                if (Biomes != null && Biomes.BiomeAt(p) == Biome.Mountain) continue;
                int mask = kv.Value;
                bool horizontal = (mask & (Side.E | Side.W)) != 0 && (mask & (Side.N | Side.S)) == 0;
                bool vertical = (mask & (Side.N | Side.S)) != 0 && (mask & (Side.E | Side.W)) == 0;
                if (!horizontal && !vertical) continue;
                Vector2 pos = Center(p) + (horizontal ? new Vector2(0f, 0.31f) : new Vector2(0.31f, 0f)) * cs;
                LightMesh.Glow(_batch, pos, cs * 0.85f, new Color(Warm.r, Warm.g, Warm.b, 0.34f));
                _batch.Disc(pos, cs * 0.05f, 8, new Color(1f, 0.97f, 0.8f, 1f));
            }

            foreach (BuildingCell c in _model.Cells) AddBuilding(c);

            foreach (Vector2Int p in _ports.Cells)
            {
                Vector2 pos = Center(p);
                LightMesh.Glow(_batch, pos, cs * 1.1f, new Color(Warm.r, Warm.g, Warm.b, 0.4f));
                _batch.Disc(pos + new Vector2(-0.3f, 0.3f) * cs, cs * 0.05f, 8, new Color(1f, 0.97f, 0.8f, 1f));
            }

            if (_buses != null)
                foreach (Vector2Int p in _buses.Cells)
                {
                    Vector2 pos = Center(p);
                    LightMesh.Glow(_batch, pos, cs * 1.0f, new Color(0.6f, 0.95f, 0.85f, 0.34f));
                    _batch.Disc(pos + new Vector2(0.28f, 0.34f) * cs, cs * 0.05f, 8, new Color(0.85f, 1f, 0.95f, 1f));
                }

            if (_view == null)
            {
                _view = new GameObject("Night lights");
                _view.transform.SetParent(transform, false);
                _view.AddComponent<MeshFilter>();
                var mr = _view.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _day.LightMaterial;
                mr.sortingOrder = DayNight.LightOrder;
            }

            var f = _view.GetComponent<MeshFilter>();
            f.sharedMesh = _batch.ToMesh(f.sharedMesh);
            _view.SetActive(_day.LightLevel > 0.01f);
        }

        void AddBuilding(BuildingCell c)
        {
            float cs = CellSize;
            Vector2 ctr = Center(c.Pos);
            int h = Hash(c.Pos);
            bool flip = (h & 1) == 1;
            Vector2 u = flip ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            Vector2 v = flip ? new Vector2(1f, 0f) : new Vector2(0f, 1f);

            switch (c.Category)
            {
                case BuildingCategory.Residential:
                {
                    LightMesh.Glow(_batch, ctr, cs * 0.75f, new Color(Warm.r, Warm.g, Warm.b, 0.26f));
                    for (int i = 0; i < 4; i++)
                    {
                        if (((h >> (i + 2)) & 3) == 0) continue; // часть окон тёмная
                        float x = (i % 2 == 0 ? -0.2f : 0.2f) * cs;
                        float y = (i < 2 ? -0.18f : 0.18f) * cs;
                        float s = cs * 0.075f;
                        _batch.Local(ctr, u, v, x - s, x + s, y - s, y + s, new Color(1f, 0.9f, 0.55f, 1f));
                    }

                    break;
                }
                case BuildingCategory.Commercial:
                {
                    Color neon = new Color(1f, 0.55f, 0.7f, 1f);
                    LightMesh.Glow(_batch, ctr, cs * 0.85f, new Color(neon.r, neon.g, neon.b, 0.3f));
                    _batch.Local(ctr, u, v, -cs * 0.34f, cs * 0.34f, cs * 0.2f, cs * 0.26f, neon);
                    _batch.Local(ctr, u, v, -cs * 0.22f, cs * 0.22f, -cs * 0.22f, -cs * 0.12f, new Color(1f, 0.95f, 0.8f, 0.95f));
                    break;
                }
                case BuildingCategory.Industrial:
                {
                    Color fire = new Color(1f, 0.6f, 0.25f, 1f);
                    LightMesh.Glow(_batch, ctr + u * (cs * 0.27f) + v * (cs * 0.27f), cs * 0.6f,
                        new Color(fire.r, fire.g, fire.b, 0.35f));
                    LightMesh.Glow(_batch, ctr, cs * 0.7f, new Color(Cool.r, Cool.g, Cool.b, 0.2f));
                    for (int i = 0; i < 3; i++)
                    {
                        float x = (-0.25f + i * 0.2f) * cs;
                        float s = cs * 0.05f;
                        _batch.Local(ctr, u, v, x - s, x + s, -cs * 0.25f - s, -cs * 0.25f + s, new Color(Cool.r, Cool.g, Cool.b, 1f));
                    }

                    break;
                }
                default:
                {
                    Color col = ServiceSpecs.Color(c.Kind);
                    if (c.Kind == ServiceKind.Ambulance) col = new Color(1f, 0.45f, 0.45f);
                    else if (c.Kind == ServiceKind.Police) col = new Color(0.5f, 0.65f, 1f);
                    LightMesh.Glow(_batch, ctr, cs * 0.95f, new Color(col.r, col.g, col.b, 0.38f));
                    _batch.Disc(ctr, cs * 0.07f, 10, new Color(1f, 1f, 1f, 0.95f));
                    break;
                }
            }
        }
    }
}
