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
        Fire = 2,
        PowerPlant = 3,
        WaterTower = 4
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
                case BuildingCategory.Residential: return new Color32(0x54, 0x98, 0xEC, 0xFF);
                case BuildingCategory.Commercial: return new Color32(0xF2, 0x6C, 0x72, 0xFF);
                case BuildingCategory.Industrial: return new Color32(0xF5, 0xBC, 0x2C, 0xFF);
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
        /// <summary>Коммунальные службы: электростанция и водонапорная башня. Дают не налог, а подключение.</summary>
        public static bool IsUtility(ServiceKind k)
        {
            return k == ServiceKind.PowerPlant || k == ServiceKind.WaterTower;
        }

        /// <summary>Сколько клеток зданий тянет одна станция.</summary>
        public static int Capacity(ServiceKind k)
        {
            return IsUtility(k) ? 30 : 0;
        }

        public static float Radius(ServiceKind k)
        {
            switch (k)
            {
                case ServiceKind.PowerPlant: return 6f;
                case ServiceKind.WaterTower: return 6f;
                case ServiceKind.Ambulance: return 4f;
                case ServiceKind.Police: return 3f;
                default: return 3f;
            }
        }

        /// <summary>Прибавка к налогу клетки, доля. Профильный тип получает больше.</summary>
        public static float Bonus(ServiceKind k, BuildingCategory target)
        {
            if (IsUtility(k)) return 0f;
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
                case ServiceKind.Ambulance: return new Color32(0xEA, 0xF2, 0xFA, 0xFF);
                case ServiceKind.Police: return new Color32(0x3E, 0x64, 0xC8, 0xFF);
                case ServiceKind.PowerPlant: return new Color32(0xF5, 0xBC, 0x2C, 0xFF);
                case ServiceKind.WaterTower: return new Color32(0x42, 0xA8, 0xF0, 0xFF);
                default: return new Color32(0xE8, 0x3C, 0x3C, 0xFF);
            }
        }

        public static string Title(ServiceKind k)
        {
            switch (k)
            {
                case ServiceKind.Ambulance: return "Скорая";
                case ServiceKind.Police: return "Полиция";
                case ServiceKind.PowerPlant: return "Электро-станция";
                case ServiceKind.WaterTower: return "Водо-башня";
                default: return "Пожарные";
            }
        }
    }
}
