namespace CityGame.CellRoads
{
    /// <summary>
    /// Полоса шоссе. Может быть распознана у соседних обычных дорог или проложена отдельным маршрутом.
    /// Каждая самостоятельная проезжая часть односторонняя; встречную проводят отдельным штрихом.
    /// </summary>
    public struct HighwayLane
    {
        public bool Active;

        /// <summary>Куда едут машины по этой полосе, как сторона клетки (N, E, S, W).</summary>
        public int Heading;

        /// <summary>С какой стороны лежит вторая полоса шоссе.</summary>
        public int PartnerSide;

        /// <summary>Ручная полоса, заданная маршрутом игрока.</summary>
        public bool Manual;

        /// <summary>Есть соседняя встречная полоса, поэтому можно рисовать разделитель.</summary>
        public bool Median;

        /// <summary>Обычная по ширине односторонняя дорога, без скоростного бонуса шоссе.</summary>
        public bool OneWay;

        /// <summary>Полоса построена как самостоятельный объект, а не назначена готовой дороге.</summary>
        public bool Standalone;
    }

    /// <summary>Числа шоссе, временные.</summary>
    public static class HighwaySpecs
    {
        /// <summary>Автоматически распознаёт длинные пары соседних прямых дорог.</summary>
        public static bool Enabled = true;

        /// <summary>Во сколько раз машины едут быстрее, чем по обычной дороге.</summary>
        public const float SpeedFactor = 2f;

        /// <summary>Во сколько раз полоса шоссе пропускает больше, чем обычная дорога.</summary>
        public const float CapacityFactor = 2f;
    }
}
