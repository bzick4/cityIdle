using System;
using System.Collections.Generic;
using CityGame.Roads;
using UnityEngine;

namespace CityGame.Sectors
{
    /// <summary>
    /// Какие шестиугольные сектора открыты. Открывать можно только соседние с открытой областью.
    /// Стартовый сектор (0,0) открыт сразу.
    /// </summary>
    public sealed class SectorMap
    {
        public readonly float HexSize;
        readonly HashSet<HexCoord> _open = new HashSet<HexCoord>();
        readonly HashSet<HexCoord> _frontier = new HashSet<HexCoord>();

        public IEnumerable<HexCoord> Open => _open;
        /// <summary>Закрытые сектора, граничащие с открытой областью. Их можно открыть.</summary>
        public IEnumerable<HexCoord> Frontier => _frontier;

        public event Action Changed;

        public SectorMap(float hexSize)
        {
            HexSize = hexSize;
            Add(new HexCoord(0, 0));
        }

        public HexCoord SectorAt(Vector2 world)
        {
            return HexMath.FromWorld(world, HexSize);
        }

        public bool IsOpen(HexCoord h)
        {
            return _open.Contains(h);
        }

        public bool IsOpenAt(Vector2 world)
        {
            return _open.Contains(SectorAt(world));
        }

        public bool CanOpen(HexCoord h)
        {
            return _frontier.Contains(h);
        }

        public bool TryOpen(HexCoord h)
        {
            if (!CanOpen(h)) return false;
            Add(h);
            Changed?.Invoke();
            return true;
        }

        void Add(HexCoord h)
        {
            _open.Add(h);
            _frontier.Remove(h);
            for (int d = 0; d < 6; d++)
            {
                HexCoord n = h.Neighbor(d);
                if (!_open.Contains(n)) _frontier.Add(n);
            }
        }
    }
}
