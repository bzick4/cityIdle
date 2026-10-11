namespace CityGame.CellRoads
{
    /// <summary>Тип развилки в клетке, где сходятся три или четыре дороги.</summary>
    public enum JunctionType
    {
        /// <summary>Обычный перекрёсток: можно повернуть, но машины ждут друг друга.</summary>
        Plain = 0,
        /// <summary>Кольцо: пропускает заметно больше машин и почти не задерживает.</summary>
        Roundabout = 1,
        /// <summary>Мост: дорога запад-восток идёт поверх юг-север, поворачивать нельзя. Только для четырёх дорог.</summary>
        Overpass = 2,
        /// <summary>Мост: дорога север-юг идёт поверх запад-восток, поворачивать нельзя. Только для четырёх дорог.</summary>
        OverpassVertical = 3,
        /// <summary>Крупное кольцо, рассчитанное на разведение потоков шоссе.</summary>
        HighwayRoundabout = 4
    }

    /// <summary>Числа развилок, временные. Задержка в секундах, пропускная способность в поездках.</summary>
    public static class JunctionSpecs
    {
        public static bool IsOverpass(JunctionType t)
        {
            return t == JunctionType.Overpass || t == JunctionType.OverpassVertical;
        }

        public static float Delay(JunctionType t)
        {
            switch (t)
            {
                case JunctionType.Roundabout: return 0.4f;
                case JunctionType.HighwayRoundabout: return 0.25f;
                case JunctionType.Overpass:
                case JunctionType.OverpassVertical: return 0f;
                default: return 1.0f;
            }
        }

        public static float Capacity(JunctionType t)
        {
            switch (t)
            {
                case JunctionType.Roundabout: return 30f;
                case JunctionType.HighwayRoundabout: return 72f;
                case JunctionType.Overpass:
                case JunctionType.OverpassVertical: return 60f;
                default: return 12f;
            }
        }

        public static string Title(JunctionType t)
        {
            switch (t)
            {
                case JunctionType.Roundabout: return "Кольцо";
                case JunctionType.HighwayRoundabout: return "Кольцо шоссе";
                case JunctionType.Overpass: return "Мост горизонтальный";
                case JunctionType.OverpassVertical: return "Мост вертикальный";
                default: return "Перекрёсток";
            }
        }

        /// <summary>Короткая подпись для кнопки.</summary>
        public static string Short(JunctionType t)
        {
            switch (t)
            {
                case JunctionType.Roundabout: return "Кольцо";
                case JunctionType.HighwayRoundabout: return "Кольцо\nшоссе";
                case JunctionType.Overpass: return "Мост\nгориз.";
                case JunctionType.OverpassVertical: return "Мост\nверт.";
                default: return "Перекрёсток";
            }
        }
    }
}
