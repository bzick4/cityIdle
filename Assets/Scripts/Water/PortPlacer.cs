using System;
using CityGame.Buildings;
using CityGame.CellRoads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Water
{
    /// <summary>
    /// Ставит порт одним касанием. Порт можно только на воде: в море или на канале.
    /// На один сектор приходится один порт.
    /// </summary>
    public sealed class PortPlacer : TapPlacer
    {
        PortMap _ports;
        CityModel _city;
        CellRoadMap _roads;
        SectorMap _sectors;
        Biomes _biomes;
        Economy _economy;

        public void Bind(PortMap ports, CityModel city, CellRoadMap roads, SectorMap sectors, Biomes biomes, Economy economy)
        {
            _ports = ports;
            _city = city;
            _roads = roads;
            _sectors = sectors;
            _biomes = biomes;
            _economy = economy;
        }

        protected override void TryPlace(Vector2Int cell)
        {
            if (_ports == null || _ports.Has(cell)) return;
            if (!_sectors.IsOpenAt(CenterOf(cell))) return;

            if (!_biomes.IsWater(cell))
            {
                Say("Порт строят только на воде: в море или на канале");
                return;
            }

            if (_city.HasCell(cell) || _roads.Has(cell))
            {
                Say("Клетка занята");
                return;
            }

            if (SectorHasPort(cell))
            {
                Say("В этом секторе порт уже есть");
                return;
            }

            if (_economy.Money < _economy.PortCost)
            {
                Say("Не хватает денег: " + _economy.PortCost.ToString("0"));
                return;
            }

            if (_ports.Add(cell)) _economy.TrySpend(_economy.PortCost);
        }

        bool SectorHasPort(Vector2Int cell)
        {
            var h = _sectors.SectorAt(CenterOf(cell));
            foreach (Vector2Int p in _ports.Cells)
                if (_sectors.SectorAt(CenterOf(p)) == h) return true;
            return false;
        }
    }
}
