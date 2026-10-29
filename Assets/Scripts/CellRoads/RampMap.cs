using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGame.CellRoads
{
    public struct RampConnector
    {
        public Vector2Int FromCell;
        public Vector2Int ToCell;
    }

    public sealed class RampMap
    {
        readonly List<RampConnector> _connectors = new List<RampConnector>();

        public IReadOnlyList<RampConnector> All => _connectors;

        public event Action Changed;

        public void Add(Vector2Int from, Vector2Int to)
        {
            _connectors.Add(new RampConnector { FromCell = from, ToCell = to });
            Changed?.Invoke();
        }

        public static Vector2 CellCenter(Vector2Int cell, float cellSize)
        {
            return new Vector2((cell.x + 0.5f) * cellSize, (cell.y + 0.5f) * cellSize);
        }

        /// <summary>Точка выхода линии from→to через границу клетки from.</summary>
        public static Vector2 EdgePoint(Vector2Int from, Vector2Int to, float cellSize)
        {
            Vector2 fc = CellCenter(from, cellSize);
            Vector2 dn = (CellCenter(to, cellSize) - fc).normalized;
            float h = cellSize * 0.5f;
            float tx = Mathf.Abs(dn.x) > 1e-4f ? h / Mathf.Abs(dn.x) : float.MaxValue;
            float ty = Mathf.Abs(dn.y) > 1e-4f ? h / Mathf.Abs(dn.y) : float.MaxValue;
            return fc + dn * Mathf.Min(tx, ty);
        }
    }
}
