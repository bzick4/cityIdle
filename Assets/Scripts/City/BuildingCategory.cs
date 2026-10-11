using UnityEngine;

namespace CityGame.Buildings
{
    public enum BuildingCategory
    {
        Residential = 0,
        Commercial = 1,
        Industrial = 2,
        Service = 3
    }

    /// <summary>Вид службы. Имеет смысл только для категории Service.</summary>
    public enum ServiceKind
    {
        Ambulance = 0,
        Police = 1,
        Fire = 2
    }

    public static class CategorySpecs
    {
        /// <summary>Сливаются жильё, торговля и промышленность. Службы нет.</summary>
        public static bool Merges(BuildingCategory c)
        {
            return c != BuildingCategory.Service;
        }

        public static Color BodyColor(BuildingCategory c, ServiceKind kind = ServiceKind.Ambulance)
        {
            switch (c)
            {
                case BuildingCategory.Residential: return new Color32(0x6F, 0xA8, 0xDC, 0xFF);
                case BuildingCategory.Commercial: return new Color32(0xE8, 0x83, 0x8B, 0xFF);
                case BuildingCategory.Industrial: return new Color32(0xF2, 0xC1, 0x4E, 0xFF);
                default: return ServiceSpecs.Color(kind);
            }
        }

        public static string Title(BuildingCategory c)
        {
            switch (c)
            {
                case BuildingCategory.Residential: return "Жильё";
                case BuildingCategory.Commercial: return "Торговля";
                case BuildingCategory.Industrial: return "Промышленность";
                default: return "Служба";
            }
        }
    }

    /// <summary>
    /// Службы. Каждая действует в круге радиусом Radius клеток и даёт прибавку к налогу
    /// клеткам внутри круга. Прибавка зависит от вида службы и типа клетки. Разные службы складываются,
    /// две одинаковые не складываются. Все числа временные.
    /// </summary>
    public static class ServiceSpecs
    {
        public static float Radius(ServiceKind k)
        {
            switch (k)
            {
                case ServiceKind.Ambulance: return 4f;
                case ServiceKind.Police: return 3f;
                default: return 3f;
            }
        }

        /// <summary>Прибавка к налогу клетки, доля. Профильный тип получает больше.</summary>
        public static float Bonus(ServiceKind k, BuildingCategory target)
        {
            switch (k)
            {
                case ServiceKind.Ambulance:
                    return target == BuildingCategory.Residential ? 0.20f : 0.10f;
                case ServiceKind.Police:
                    return target == BuildingCategory.Commercial ? 0.20f : 0.10f;
                default:
                    return target == BuildingCategory.Industrial ? 0.20f : 0.10f;
            }
        }

        public static Color Color(ServiceKind k)
        {
            switch (k)
            {
                case ServiceKind.Ambulance: return new Color32(0xE9, 0xEE, 0xF2, 0xFF);
                case ServiceKind.Police: return new Color32(0x3D, 0x5A, 0xA8, 0xFF);
                default: return new Color32(0xD9, 0x4F, 0x45, 0xFF);
            }
        }

        public static string Title(ServiceKind k)
        {
            switch (k)
            {
                case ServiceKind.Ambulance: return "Скорая";
                case ServiceKind.Police: return "Полиция";
                default: return "Пожарные";
            }
        }
    }
}
