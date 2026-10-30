using System;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>
    /// Деньги и налоги. Раз в TickSeconds каждый район платит
    /// IncomePerCell[категория] × множитель слияния × коэффициент довольства.
    /// Все числа временные, подбираются в инспекторе.
    /// </summary>
    public sealed class Economy : MonoBehaviour
    {
        public float Money = 1000f;
        public float TickSeconds = 1f;

        [Tooltip("X: доход одной ячейки в секунду. Порядок: жильё, торговля, промышленность, служба")]
        public float[] IncomePerCell = { 1f, 1f, 1f, 0f };

        [Tooltip("Цена одной ячейки. Порядок как выше")]
        public float[] BuildCost = { 100f, 100f, 100f, 300f };

        [Tooltip("Цена службы по виду: скорая, полиция, пожарные")]
        public float[] ServiceCost = { 300f, 300f, 300f };

        [Tooltip("Цена коммунальных станций: электростанция, водонапорная башня")]
        public float[] UtilityCost = { 350f, 250f };

        [Tooltip("Цена одной клетки дороги")]
        public float RoadCostPerCell = 5f;
        [Tooltip("Цена одной клетки самостоятельного шоссе")]
        public float HighwayCostPerCell = 10f;

        [Tooltip("Цена малого кольца, кольца шоссе и эстакады")]
        public float RoundaboutCost = 50f;
        public float HighwayRoundaboutCost = 140f;
        public float OverpassCost = 100f;

        [Tooltip("Цена одной новой клетки канала. Море бесплатно: там уже вода")]
        public float CanalCostPerCell = 8f;
        [Tooltip("Цена порта")]
        public float PortCost = 400f;
        [Tooltip("Цена автостанции")]
        public float BusStationCost = 250f;

        /// <summary>Доля клеток района с электричеством и водой, 0..1. Без сети всё считается подключённым.</summary>
        public Func<District, float> UtilityFactor;

        /// <summary>Доход, который не считается по районам (порты и корабли), в секунду.</summary>
        public Func<float> ExtraIncome;

        [Tooltip("Минимальный доход в секунду, идёт всегда")]
        public float BaseIncomePerSecond = 0.5f;

        [Tooltip("Доля налога при нулевом довольстве. При полном довольстве налог 100%")]
        [Range(0f, 1f)] public float SatisfactionFloor = 0.25f;

        public event Action<District, float> TaxCollected;
        public event Action<float> MoneyChanged;

        CityModel _model;
        ServiceCoverage _coverage;
        Biomes _biomes;

        public void Bind(CityModel model, ServiceCoverage coverage = null, Biomes biomes = null)
        {
            _model = model;
            _coverage = coverage;
            _biomes = biomes;
            StopAllCoroutines();
            StartCoroutine(TickLoop());
        }

        /// <summary>Цена здания в клетке: биом снега и пустыни даёт скидку.</summary>
        public float CostAt(BuildingCategory c, ServiceKind kind, Vector2Int cell)
        {
            float k = _biomes != null ? _biomes.CostFactor(cell) : 1f;
            return CostOf(c, kind) * k;
        }

        /// <summary>Общая прибавка к налогу района: службы и лес, доля.</summary>
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

        /// <summary>Доход в секунду с учётом слияния и довольства.</summary>
        public float IncomePerSecond
        {
            get
            {
                float sum = BaseIncomePerSecond + (ExtraIncome != null ? ExtraIncome() : 0f);
                if (_model != null)
                    foreach (District d in _model.Districts) sum += DistrictIncomePerSecond(d);
                return sum;
            }
        }

        public float CostOf(BuildingCategory c, ServiceKind kind = ServiceKind.Ambulance)
        {
            if (c != BuildingCategory.Service) return BuildCost[(int)c];
            int k = (int)kind;
            if (ServiceSpecs.IsUtility(kind))
            {
                int u = k - (int)ServiceKind.PowerPlant;
                return u < UtilityCost.Length ? UtilityCost[u] : 300f;
            }

            return k < ServiceCost.Length ? ServiceCost[k] : 300f;
        }

        public bool CanAfford(BuildingCategory c, ServiceKind kind = ServiceKind.Ambulance)
        {
            return Money >= CostOf(c, kind);
        }

        /// <summary>Деньги потрачены на постройку: любой инструмент строительства сообщает об этом здесь.</summary>
        public event Action Spent;

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
        {
            return SatisfactionFloor + (1f - SatisfactionFloor) * Mathf.Clamp01(d.Satisfaction);
        }

        public float DistrictIncomePerSecond(District d)
        {
            float bonus = BonusOf(d);
            float served = UtilityFactor != null ? UtilityFactor(d) : 1f;
            return IncomePerCell[(int)d.Category] * d.Multiplier * SatisfactionFactor(d) * (1f + bonus) * served;
        }

        System.Collections.IEnumerator TickLoop()
        {
            var wait = new WaitForSeconds(TickSeconds);
            while (true)
            {
                yield return wait;
                Tick();
            }
        }

        void Tick()
        {
            float total = (BaseIncomePerSecond + (ExtraIncome != null ? ExtraIncome() : 0f)) * TickSeconds;
            foreach (District d in _model.Districts)
            {
                float tax = DistrictIncomePerSecond(d) * TickSeconds;
                if (tax <= 0f) continue;
                total += tax;
                TaxCollected?.Invoke(d, tax);
            }

            Add(total);
        }
    }
}
