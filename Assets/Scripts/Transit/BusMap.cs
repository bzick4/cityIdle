using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.Transit
{
    /// <summary>Автостанции. Одна станция занимает одну клетку суши, на сектор положена одна.</summary>
    public sealed class BusMap
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
