namespace CityGame.CellRoads
{
    /// <summary>
    /// Полоса шоссе. Две соседние прямые параллельные дороги объединяются в шоссе: каждая становится
    /// односторонней полосой. Если параллельных дорог три и больше, шоссе не получается.
    /// </summary>
    public struct HighwayLane
    {
        public bool Active;

        /// <summary>Куда едут машины по этой полосе, как сторона клетки (N, E, S, W).</summary>
        public int Heading;

        /// <summary>С какой стороны лежит вторая полоса шоссе.</summary>
        public int PartnerSide;
    }

    /// <summary>Числа шоссе, временные.</summary>
    public static class HighwaySpecs
    {
        /// <summary>Слияние двух соседних дорог в шоссе. Выключено: конфликтует при превращении. Включить: true.</summary>
        public static bool Enabled = false;

        /// <summary>Во сколько раз машины едут быстрее, чем по обычной дороге.</summary>
        public const float SpeedFactor = 2f;

        /// <summary>Во сколько раз полоса шоссе пропускает больше, чем обычная дорога.</summary>
        public const float CapacityFactor = 2f;
    }
}
