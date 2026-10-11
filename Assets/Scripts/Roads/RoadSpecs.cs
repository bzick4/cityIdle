using UnityEngine;

namespace CityGame.Roads
{
    public enum RoadType
    {
        Local,
        Highway
    }

    /// <summary>Слой дороги. Шоссе лежит выше земли и соединяется с ней только съездами (позже).</summary>
    public enum RoadLayer
    {
        Tunnel = -1,
        Ground = 0,
        Highway = 1
    }

    /// <summary>Настраиваемые значения для типов дорог. Числа ниже стартовые, подбираются на глаз.</summary>
    public static class RoadSpecs
    {
        public static float Width(RoadType type)
        {
            return type == RoadType.Highway ? 0.9f : 0.45f;
        }

        public static RoadLayer LayerFor(RoadType type)
        {
            return type == RoadType.Highway ? RoadLayer.Highway : RoadLayer.Ground;
        }

        public static Color BodyColor(RoadType type)
        {
            // Цвета из макета: тёмно-серая местная дорога и жёлтое шоссе.
            return type == RoadType.Highway
                ? new Color(0.949f, 0.722f, 0.294f, 1f)
                : new Color(0.231f, 0.271f, 0.322f, 1f);
        }

        public static int SortingOrder(RoadLayer layer)
        {
            return (int)layer * 10;
        }
    }
}
