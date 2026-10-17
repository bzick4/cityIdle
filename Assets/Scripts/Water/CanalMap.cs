using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Water
{
    /// <summary>Клетки каналов. Море не хранится здесь, оно берётся из биома.</summary>
    public sealed class CanalMap
    {
        readonly HashSet<Vector2Int> _cells = new HashSet<Vector2Int>();

        public IEnumerable<Vector2Int> Cells => _cells;
        public int Count => _cells.Count;
        public event Action Changed;

        public bool Has(Vector2Int cell)
        {
            return _cells.Contains(cell);
        }

        public int AddRange(IEnumerable<Vector2Int> cells)
        {
            int added = 0;
            foreach (Vector2Int c in cells)
                if (_cells.Add(c)) added++;
            if (added > 0) Changed?.Invoke();
            return added;
        }

        public bool Remove(Vector2Int cell)
        {
            if (!_cells.Remove(cell)) return false;
            Changed?.Invoke();
            return true;
        }

        public void Load(IEnumerable<Vector2Int> cells)
        {
            _cells.Clear();
            foreach (Vector2Int c in cells) _cells.Add(c);
            Changed?.Invoke();
        }
    }

    /// <summary>Порты. Один порт занимает одну клетку воды.</summary>
    public sealed class PortMap
    {
        readonly HashSet<Vector2Int> _cells = new HashSet<Vector2Int>();

        public IEnumerable<Vector2Int> Cells => _cells;
        public int Count => _cells.Count;
        public event Action Changed;

        public bool Has(Vector2Int cell)
        {
            return _cells.Contains(cell);
        }

        public bool Add(Vector2Int cell)
        {
            if (!_cells.Add(cell)) return false;
            Changed?.Invoke();
            return true;
        }

        public bool Remove(Vector2Int cell)
        {
            if (!_cells.Remove(cell)) return false;
            Changed?.Invoke();
            return true;
        }

        public void Load(IEnumerable<Vector2Int> cells)
        {
            _cells.Clear();
            foreach (Vector2Int c in cells) _cells.Add(c);
            Changed?.Invoke();
        }
    }
}
