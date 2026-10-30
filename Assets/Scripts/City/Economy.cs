using System;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    public sealed class Economy : MonoBehaviour
    {
        [SerializeField] float _tickSeconds = 1f;

        [Header("Доход (временные значения)")]
        [SerializeField] float[] _incomePerCell = { 1f, 1f, 1f, 0f };
        [SerializeField] float[] _buildCost = { 100f, 100f, 100f, 300f };
        [SerializeField] float[] _serviceCost = { 300f, 300f, 300f };
        [SerializeField] float[] _utilityCost = { 350f, 250f };

        [Header("Цены дорог")]
        [SerializeField] float _roadCostPerCell = 5f;
        [SerializeField] float _highwayCostPerCell = 10f;
        [SerializeField] float _roundaboutCost = 50f;
        [SerializeField] float _highwayRoundaboutCost = 140f;
        [SerializeField] float _overpassCost = 100f;

        [Header("Цены инфраструктуры")]
        [SerializeField] float _canalCostPerCell = 8f;
        [SerializeField] float _portCost = 400f;
        [SerializeField] float _busStationCost = 250f;

        [Header("Баланс")]
        [SerializeField] float _baseIncomePerSecond = 0.5f;
        [SerializeField] [Range(0f, 1f)] float _satisfactionFloor = 0.25f;
        [SerializeField] float _startMoney = 1000f;

        public float TickSeconds => _tickSeconds;
        public float[] IncomePerCell => _incomePerCell;
        public float[] BuildCost => _buildCost;
        public float[] ServiceCost => _serviceCost;
        public float[] UtilityCost => _utilityCost;
        public float RoadCostPerCell => _roadCostPerCell;
        public float HighwayCostPerCell => _highwayCostPerCell;
        public float RoundaboutCost => _roundaboutCost;
        public float HighwayRoundaboutCost => _highwayRoundaboutCost;
        public float OverpassCost => _overpassCost;
        public float CanalCostPerCell => _canalCostPerCell;
        public float PortCost => _portCost;
        public float BusStationCost => _busStationCost;
        public float BaseIncomePerSecond => _baseIncomePerSecond;

        public float Money { get; private set; }

        public Func<District, float> UtilityFactor { get; set; }
        public Func<float> ExtraIncome { get; set; }

        public event Action<District, float> TaxCollected;
        public event Action<float> MoneyChanged;
        public event Action Spent;

        CityModel _model;
        ServiceCoverage _coverage;
        Biomes _biomes;

        public void Bind(CityModel model, ServiceCoverage coverage = null, Biomes biomes = null)
        {
            _model = model;
            _coverage = coverage;
            _biomes = biomes;
            Money = _startMoney;
            StopAllCoroutines();
            StartCoroutine(TickLoop());
        }

        public float CostAt(BuildingCategory c, ServiceKind kind, Vector2Int cell)
        {
            float k = _biomes != null ? _biomes.CostFactor(cell) : 1f;
            return CostOf(c, kind) * k;
        }

        public float BonusOf(District d)
        {
            float bonus = _coverage != null ? _coverage.BonusOf(d) : 0f;
            if (_biomes != null && d.Count > 0)
            {
                float sum = 0f;
                foreach (BuildingCell c in d.Cells) sum += _biomes.TaxBonusAt(c.Pos);
                bonus += sum / d.Count;
            }
            return bonus;
        }

        public float IncomePerSecond
        {
            get
            {
                float sum = _baseIncomePerSecond + (ExtraIncome != null ? ExtraIncome() : 0f);
                if (_model != null)
                    foreach (District d in _model.Districts) sum += DistrictIncomePerSecond(d);
                return sum;
            }
        }

        public float CostOf(BuildingCategory c, ServiceKind kind = ServiceKind.Ambulance)
        {
            if (c != BuildingCategory.Service) return _buildCost[(int)c];
            int k = (int)kind;
            if (ServiceSpecs.IsUtility(kind))
            {
                int u = k - (int)ServiceKind.PowerPlant;
                return u < _utilityCost.Length ? _utilityCost[u] : 300f;
            }
            return k < _serviceCost.Length ? _serviceCost[k] : 300f;
        }

        public bool CanAfford(BuildingCategory c, ServiceKind kind = ServiceKind.Ambulance)
            => Money >= CostOf(c, kind);

        /// <summary>Восстановление денег из сохранения.</summary>
        public void RestoreMoney(float amount)
        {
            Money = amount;
            MoneyChanged?.Invoke(Money);
        }

        public bool TrySpend(float amount)
        {
            if (Money < amount) return false;
            Money -= amount;
            MoneyChanged?.Invoke(Money);
            Spent?.Invoke();
            return true;
        }

        public void Add(float amount)
        {
            Money += amount;
            MoneyChanged?.Invoke(Money);
        }

        public float SatisfactionFactor(District d)
            => _satisfactionFloor + (1f - _satisfactionFloor) * Mathf.Clamp01(d.Satisfaction);

        public float DistrictIncomePerSecond(District d)
        {
            float bonus = BonusOf(d);
            float served = UtilityFactor != null ? UtilityFactor(d) : 1f;
            return _incomePerCell[(int)d.Category] * d.Multiplier * SatisfactionFactor(d) * (1f + bonus) * served;
        }

        System.Collections.IEnumerator TickLoop()
        {
            var wait = new WaitForSeconds(_tickSeconds);
            while (true)
            {
                yield return wait;
                Tick();
            }
        }

        void Tick()
        {
            float total = (_baseIncomePerSecond + (ExtraIncome != null ? ExtraIncome() : 0f)) * _tickSeconds;
            foreach (District d in _model.Districts)
            {
                float tax = DistrictIncomePerSecond(d) * _tickSeconds;
                if (tax <= 0f) continue;
                total += tax;
                TaxCollected?.Invoke(d, tax);
            }
            Add(total);
        }
    }
}
