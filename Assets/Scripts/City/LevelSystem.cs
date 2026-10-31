using System;
using CityGame.Buildings;
using UnityEngine;

namespace CityGame
{
    /// <summary>
    /// Уровень игрока и опыт. Уровни с 1 по 5, каждый открывает новые инструменты.
    /// Опыт: +10 за здание, +20 за службу/порт/автостанцию, +50 за новый сектор.
    /// </summary>
    public sealed class LevelSystem
    {
        // XP нужен для перехода на следующий уровень (не накапливается сверх порога).
        static readonly int[] Thresholds = { 100, 300, 600, 1000, 0 };

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }

        public int XpToNext => Level <= 5 ? Thresholds[Level - 1] : 0;
        public float XpFraction => XpToNext > 0 ? Mathf.Clamp01((float)Xp / XpToNext) : 1f;

        public event Action<int> LevelUp;   // аргумент — новый уровень
        public event Action     XpChanged;

        public void AddXp(int amount)
        {
            if (Level >= 5) return;
            Xp += amount;
            XpChanged?.Invoke();
            while (Level < 5 && Xp >= Thresholds[Level - 1])
            {
                Xp -= Thresholds[Level - 1];
                Level++;
                LevelUp?.Invoke(Level);
                XpChanged?.Invoke();
            }
        }

        public void Restore(int level, int xp)
        {
            Level = Mathf.Clamp(level, 1, 5);
            Xp    = Mathf.Max(0, xp);
        }

        /// <summary>Сколько XP даёт постройка здания.</summary>
        public static int XpForBuilding(BuildingCategory cat, ServiceKind kind)
        {
            if (cat != BuildingCategory.Service) return 10;
            return kind == ServiceKind.PowerPlant || kind == ServiceKind.WaterTower ? 15 : 20;
        }

        public const int XpPerSector = 50;

        // ── Правила разблокировки ────────────────────────────────────────────

        public bool IsToolUnlocked(Tool tool) => tool switch
        {
            Tool.Highway => Level >= 5,
            Tool.OneWay  => Level >= 5,
            Tool.Ramp    => Level >= 5,
            Tool.Canal   => Level >= 3,
            Tool.Port    => Level >= 3,
            Tool.Bus     => Level >= 3,
            _            => true,
        };

        public bool IsBuildingUnlocked(BuildingCategory cat, ServiceKind kind)
        {
            if (cat != BuildingCategory.Service) return true;
            return kind switch
            {
                ServiceKind.PowerPlant => Level >= 2,
                ServiceKind.WaterTower => Level >= 2,
                _                      => Level >= 4,
            };
        }

        public string LevelUpMessage(int newLevel) => newLevel switch
        {
            2 => "Уровень 2 — открыта энергетика!",
            3 => "Уровень 3 — открыты порт и канал!",
            4 => "Уровень 4 — открыты службы!",
            5 => "Уровень 5 — открыты шоссе и съезды!",
            _ => "Новый уровень!",
        };
    }
}
