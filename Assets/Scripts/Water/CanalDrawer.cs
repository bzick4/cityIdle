using System;
using System.Collections.Generic;
using CityGame.Buildings;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Water
{
    /// <summary>
    /// Рисование канала пальцем. Идёт по клеткам строго по осям, как дорога. Ширина 1 клетка.
    /// Нельзя в горах, на зданиях и портах.
    /// Море копать не нужно, там и так вода, поэтому оно бесплатно.
    /// </summary>
    public sealed class CanalDrawer : MonoBehaviour
    {
        public Camera Cam;
        public float CellSize = 1f;
        [Tooltip("Насколько дальше в сторону нужно увести палец, чтобы канал повернул, в клетках")]
        public int TurnThreshold = 2;

        public Func<Vector2, bool> IsPointerBlocked;

        /// <summary>Клетка занята чем-то, кроме зданий и портов (например, автостанцией).</summary>
        public Func<Vector2Int, bool> IsOccupied;
        public event Action<string> Message;

        CanalMap _canals;
        PortMap _ports;
        CityModel _city;
        SectorMap _sectors;
        Biomes _biomes;
        Economy _economy;

        readonly List<Vector2Int> _path = new List<Vector2Int>();
        readonly MeshBatch _batch = new MeshBatch();
        bool _drawing;
        bool _axisKnown;
        bool _axisHorizontal;
        bool _previewDirty;
        GameObject _preview;
        Material _previewMaterial;

        public void Bind(CanalMap canals, PortMap ports, CityModel city, SectorMap sectors, Biomes biomes, Economy economy)
        {
            _canals = canals;
            _ports = ports;
            _city = city;
            _sectors = sectors;
            _biomes = biomes;
            _economy = economy;
        }

        void Awake()
        {
            if (Cam == null) Cam = Camera.main;
            _previewMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.25f, 0.55f, 0.9f, 0.6f) };
        }

        void OnDisable()
        {
            _drawing = false;
            _path.Clear();
            ClearPreview();
        }

        void OnDestroy()
        {
            if (_previewMaterial != null) Destroy(_previewMaterial);
        }

        Vector2Int ToCell(Vector2 world)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x / CellSize), Mathf.FloorToInt(world.y / CellSize));
        }

        Vector2 CellCenter(Vector2Int c)
        {
            return new Vector2((c.x + 0.5f) * CellSize, (c.y + 0.5f) * CellSize);
        }

        Vector2 PointerWorld()
        {
            Vector3 p = Cam.ScreenToWorldPoint(new Vector3(PointerInput.Position.x, PointerInput.Position.y,
                -Cam.transform.position.z));
            return new Vector2(p.x, p.y);
        }

        bool Allowed(Vector2Int cell)
        {
            if (_city.HasCell(cell) || _ports.Has(cell)) return false;
            if (IsOccupied != null && IsOccupied(cell)) return false;
            if (!_sectors.IsOpenAt(CellCenter(cell))) return false;
            return _biomes.BiomeAt(cell) != Biome.Mountain;
        }

        void Update()
        {
            if (_canals == null || Cam == null) return;

            PointerInput.Poll();

            if (PointerInput.Gesture)
            {
                if (_drawing)
                {
                    _drawing = false;
                    _path.Clear();
                    ClearPreview();
                }

                return;
            }

            if (!_drawing)
            {
                if (!PointerInput.Down) return;
                if (IsPointerBlocked != null && IsPointerBlocked(PointerInput.Position)) return;

                Vector2Int start = ToCell(PointerWorld());
                if (!Allowed(start))
                {
                    if (_biomes.BiomeAt(start) == Biome.Mountain) Message?.Invoke("В горах каналы не строят");
                    return;
                }

                _drawing = true;
                _axisKnown = false;
                _path.Clear();
                _path.Add(start);
                _previewDirty = true;
            }

            if (!PointerInput.Pressed || PointerInput.Up)
            {
                Commit();
                return;
            }

            Follow(ToCell(PointerWorld()));
            if (_previewDirty) RedrawPreview();
        }

        void Follow(Vector2Int target)
        {
            for (int guard = 0; guard < 128; guard++)
            {
                Vector2Int tail = _path[_path.Count - 1];
                if (tail == target) return;

                int idx = _path.IndexOf(target);
                if (idx >= 0)
                {
                    _path.RemoveRange(idx + 1, _path.Count - idx - 1);
                    _axisKnown = false;
                    if (_path.Count >= 2)
                    {
                        _axisHorizontal = _path[_path.Count - 1].y == _path[_path.Count - 2].y;
                        _axisKnown = true;
                    }

                    _previewDirty = true;
                    return;
                }

                Vector2Int d = target - tail;
                if (!_axisKnown)
                {
                    _axisHorizontal = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
                    _axisKnown = true;
                }

                int along = _axisHorizontal ? d.x : d.y;
                int across = _axisHorizontal ? d.y : d.x;
                bool turn = across != 0 && (along == 0 ||
                                            (Mathf.Abs(across) >= TurnThreshold && Mathf.Abs(across) > Mathf.Abs(along)));
                if (turn)
                {
                    _axisHorizontal = !_axisHorizontal;
                    along = _axisHorizontal ? d.x : d.y;
                }

                if (along == 0) return;

                Vector2Int step = _axisHorizontal
                    ? new Vector2Int(along > 0 ? 1 : -1, 0)
                    : new Vector2Int(0, along > 0 ? 1 : -1);
                Vector2Int next = tail + step;
                if (!Allowed(next) || _path.Contains(next)) return;

                _path.Add(next);
                _previewDirty = true;
            }
        }

        /// <summary>Клетка штриха даёт одну клетку воды, если она разрешена.</summary>
        IEnumerable<Vector2Int> Footprint(Vector2Int p)
        {
            if (Allowed(p)) yield return p;
        }

        void Commit()
        {
            _drawing = false;
            ClearPreview();
            if (_path.Count == 0) return;

            var accepted = new HashSet<Vector2Int>();
            float total = 0f;
            bool lackMoney = false;
            foreach (Vector2Int p in _path)
            {
                var fresh = new List<Vector2Int>();
                foreach (Vector2Int c in Footprint(p))
                    if (!accepted.Contains(c) && !_biomes.IsWater(c)) fresh.Add(c);

                float cost = fresh.Count * _economy.CanalCostPerCell;
                if (_economy.Money < total + cost)
                {
                    lackMoney = true;
                    break;
                }

                total += cost;
                foreach (Vector2Int c in fresh) accepted.Add(c);
            }

            _path.Clear();
            if (accepted.Count == 0)
            {
                Message?.Invoke(lackMoney ? "Не хватает денег на канал" : "Здесь уже вода");
                return;
            }

            if (total > 0f) _economy.TrySpend(total);
            _canals.AddRange(accepted);
            if (lackMoney) Message?.Invoke("Не хватило денег, канал построен частично");
        }

        void RedrawPreview()
        {
            _previewDirty = false;
            _batch.Clear();
            var seen = new HashSet<Vector2Int>();
            foreach (Vector2Int p in _path)
                foreach (Vector2Int c in Footprint(p))
                    if (seen.Add(c))
                        _batch.Rect(new Vector2(c.x * CellSize, c.y * CellSize),
                            new Vector2((c.x + 1) * CellSize, (c.y + 1) * CellSize), Color.white);

            if (_preview == null)
            {
                _preview = new GameObject("CanalPreview");
                _preview.transform.SetParent(transform, false);
                _preview.AddComponent<MeshFilter>();
                var r = _preview.AddComponent<MeshRenderer>();
                r.sharedMaterial = _previewMaterial;
                r.sortingOrder = 16;
            }

            var f = _preview.GetComponent<MeshFilter>();
            f.sharedMesh = _batch.ToMesh(f.sharedMesh);
        }

        void ClearPreview()
        {
            if (_preview == null) return;
            var f = _preview.GetComponent<MeshFilter>();
            if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
            Destroy(_preview);
            _preview = null;
        }
    }
}
