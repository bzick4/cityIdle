using UnityEngine;

namespace CityGame.Roads
{
    public enum RoadType
    {
        Local,
        Highway
    }

    /// <summary>Слой дорожной сети.</summary>
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

        /// <summary>Желтый цвет разделительной полосы шоссе.</summary>
        public static readonly Color HighwayMedianColor = new Color32(0xF5, 0xBC, 0x2C, 0xFF);
        /// <summary>Кремовый цвет разметки (пунктир, боковые линии).</summary>
        public static readonly Color MarkingColor = new Color32(0xE4, 0xDE, 0xC8, 0xFF);

        public static Color BodyColor(RoadType type)
        {
            // Оба типа дорог — один и тот же тёмный корпус. Шоссе отличается шириной и жёлтой осевой.
            return new Color(0.20f, 0.26f, 0.34f, 1f);
        }

        public static int SortingOrder(RoadLayer layer)
        {
            return (int)layer * 10;
        }
    }
}
