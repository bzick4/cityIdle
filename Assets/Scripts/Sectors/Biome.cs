using CityGame.Roads;
using UnityEngine;

namespace CityGame.Sectors
{
    public enum Biome
    {
        Standard = 0,
        Snow = 1,
        Mountain = 2,
        Desert = 3,
        Sea = 4
    }

    /// <summary>Числа и цвета биомов, временные.</summary>
    public static class BiomeSpecs
    {
        /// <summary>Бонус к налогам стандартного биома, пока деревьев хватает.</summary>
        public const float TreeBonus = 0.10f;

        /// <summary>Доля деревьев от изначального числа, ниже которой бонус пропадает.</summary>
        public const float TreeThreshold = 0.30f;

        /// <summary>Доля клеток стандартного биома с деревом.</summary>
        public const float TreeDensity = 0.5f;

        public const int Count = 5;

        /// <summary>Море: здания и мосты дороже на 20%.</summary>
        public const float SeaCostFactor = 1.2f;

        public static readonly Color ClosedColor = new Color32(0x1F, 0x23, 0x29, 0xFF);

        public static Color Ground(Biome b)
        {
            switch (b)
            {
                case Biome.Snow: return new Color32(0xF7, 0xFA, 0xFC, 0xFF);
                case Biome.Mountain: return new Color32(0x9A, 0xA0, 0xA6, 0xFF);
                case Biome.Desert: return new Color32(0xE8, 0xD5, 0xA3, 0xFF);
                case Biome.Sea: return new Color32(0xA9, 0xD6, 0xF2, 0xFF);
                default: return new Color32(0xCD, 0xE5, 0xB0, 0xFF);
            }
        }

        /// <summary>Множитель цены зданий.</summary>
        public static float BuildCostFactor(Biome b)
        {
            if (b == Biome.Sea) return SeaCostFactor;
            return b == Biome.Snow || b == Biome.Desert ? 0.8f : 1f;
        }

        /// <summary>Множитель цены клетки дороги. На море это мост, он дороже.</summary>
        public static float RoadCostFactor(Biome b)
        {
            return b == Biome.Sea ? SeaCostFactor : 1f;
        }

        public static bool CanBuildBuildings(Biome b)
        {
            return b != Biome.Mountain;
        }

        /// <summary>Короткая подсказка про биом для окна открытия области.</summary>
        public static string Hint(Biome b)
        {
            switch (b)
            {
                case Biome.Snow: return "Строить дешевле на 20%";
                case Biome.Mountain: return "Только дороги, здания не построить";
                case Biome.Desert: return "Строить дешевле на 20%";
                case Biome.Sea: return "Здания и мосты дороже на 20%";
                default: return "Бонус к налогам +10%, пока осталось 30% деревьев";
            }
        }

        public static string Title(Biome b)
        {
            switch (b)
            {
                case Biome.Snow: return "Снег";
                case Biome.Mountain: return "Горы";
                case Biome.Desert: return "Пустыня";
                case Biome.Sea: return "Море";
                default: return "Лес";
            }
        }
    }

    /// <summary>
    /// Биом каждого сектора. Считается по шуму из координат сектора, поэтому один и тот же при любом порядке открытия,
    /// соседние сектора часто одного биома. Сектор в радиусе 1 от старта всегда стандартный.
    /// </summary>
    public sealed class BiomeMap
    {
        public int Seed = 7;

        public Biome BiomeOf(HexCoord h)
        {
            int dist = (Mathf.Abs(h.Q) + Mathf.Abs(h.R) + Mathf.Abs(h.Q + h.R)) / 2;
            if (dist <= 1) return Biome.Standard;

            float x = h.Q + h.R * 0.5f;
            float y = h.R * 0.866f;
            float height = Noise(x * 0.45f, y * 0.45f, Seed);
            float temp = Noise(x * 0.35f + 100f, y * 0.35f + 100f, Seed + 1);

            float wet = Noise(x * 0.40f + 300f, y * 0.40f - 200f, Seed + 2);

            if (height > 0.68f) return Biome.Mountain;
            if (wet > 0.70f) return Biome.Sea;
            if (temp < 0.28f) return Biome.Snow;
            if (temp > 0.72f) return Biome.Desert;
            return Biome.Standard;
        }

        public static uint Mix(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return h;
            }
        }

        static float Lattice(int x, int y, int seed)
        {
            return (Mix(x, y, seed) & 0xFFFF) / 65535f;
        }

        static float Noise(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Lattice(x0, y0, seed), Lattice(x0 + 1, y0, seed), fx);
            float b = Mathf.Lerp(Lattice(x0, y0 + 1, seed), Lattice(x0 + 1, y0 + 1, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }
    }
}
