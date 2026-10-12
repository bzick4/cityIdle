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

        /// <summary>Открывает случайные соседние стартовому сектору области. Выбор повторяется при том же seed.</summary>
        public void OpenRandomStartingNeighbors(int seed, int count)
        {
            var directions = new List<int> { 0, 1, 2, 3, 4, 5 };
            var random = new System.Random(seed);
            for (int i = directions.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int temp = directions[i];
                directions[i] = directions[j];
                directions[j] = temp;
            }

            int openCount = Mathf.Clamp(count, 0, directions.Count);
            HexCoord start = new HexCoord(0, 0);
            for (int i = 0; i < openCount; i++) Add(start.Neighbor(directions[i]));
            if (openCount > 0) Changed?.Invoke();
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

        /// <summary>Заменяет набор открытых секторов, одно событие Changed. Для загрузки сохранения.</summary>
        public void Load(IEnumerable<HexCoord> open)
        {
            _open.Clear();
            _frontier.Clear();
            foreach (HexCoord h in open) Add(h);
            if (_open.Count == 0) Add(new HexCoord(0, 0));
            Changed?.Invoke();
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
