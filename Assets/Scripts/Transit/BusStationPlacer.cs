using CityGame.Buildings;
using CityGame.CellRoads;
using CityGame.Sectors;
using CityGame.Water;
using UnityEngine;

namespace CityGame.Transit
{
    /// <summary>
    /// Ставит автостанцию одним касанием. Нужна свободная суша у дороги, на сектор одна станция.
    /// </summary>
    public sealed class BusStationPlacer : TapPlacer
    {
        BusMap _buses;
        PortMap _ports;
        CanalMap _canals;
        CityModel _city;
        CellRoadMap _roads;
        SectorMap _sectors;
        Biomes _biomes;
        Economy _economy;

        public void Bind(BusMap buses, PortMap ports, CanalMap canals, CityModel city, CellRoadMap roads,
            SectorMap sectors, Biomes biomes, Economy economy)
        {
            _buses = buses;
            _ports = ports;
            _canals = canals;
            _city = city;
            _roads = roads;
            _sectors = sectors;
            _biomes = biomes;
            _economy = economy;
        }

        protected override void TryPlace(Vector2Int cell)
        {
            if (_buses == null || _buses.Has(cell)) return;
            if (!_sectors.IsOpenAt(CenterOf(cell))) return;

            if (_biomes.IsWater(cell) || !_biomes.CanBuild(cell))
            {
                Say("Автостанцию строят на суше, не в горах и не на воде");
                return;
            }

            if (_city.HasCell(cell) || _roads.Has(cell) || _ports.Has(cell) || _canals.Has(cell))
            {
                Say("Клетка занята");
                return;
            }

            if (!BusRoutes.TouchesRoad(_roads, cell))
            {
                Say("Станция должна стоять вплотную к дороге");
                return;
            }

            if (SectorHasStation(cell))
            {
                Say("В этом секторе автостанция уже есть");
                return;
            }

            if (_economy.Money < _economy.BusStationCost)
            {
                Say("Не хватает денег: " + _economy.BusStationCost.ToString("0"));
                return;
            }

            if (_buses.Add(cell)) _economy.TrySpend(_economy.BusStationCost);
        }

        bool SectorHasStation(Vector2Int cell)
        {
            var h = _sectors.SectorAt(CenterOf(cell));
            foreach (Vector2Int p in _buses.Cells)
                if (_sectors.SectorAt(CenterOf(p)) == h) return true;
            return false;
        }
    }
}
