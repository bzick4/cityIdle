using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityGame.Save
{
    [Serializable] public class RoadCellSave { public int x, y, mask, type; }
    [Serializable] public class HighwayCellSave { public int x, y, heading, kind, originalMask; }
    [Serializable] public class BuildingSave { public int x, y, category, kind, serial; }
    [Serializable] public class HexSave { public int q, r; }
    [Serializable] public class CellSave { public int x, y; }

    /// <summary>Узел свободного графа дорог.</summary>
    [Serializable] public class FreeRoadNodeSave { public int id; public float x, y; }
    /// <summary>Ребро свободного графа дорог. points — плоский массив x0,y0,x1,y1,…</summary>
    [Serializable] public class FreeRoadEdgeSave { public int id, nodeA, nodeB, type, oneWay; public float[] points; }

    /// <summary>Всё, что нужно для восстановления города. Деревья, машины и довольство считаются заново.</summary>
    [Serializable]
    public class SaveData
    {
        public int version = 2;
        public int seed;
        public float money;
        public float camX, camY, camWidth;
        public List<HexSave> openSectors = new List<HexSave>();
        public List<RoadCellSave> roads = new List<RoadCellSave>();
        public List<HighwayCellSave> highway = new List<HighwayCellSave>();
        public List<BuildingSave> buildings = new List<BuildingSave>();
        public List<CellSave> canals = new List<CellSave>();
        public List<CellSave> ports = new List<CellSave>();
        public List<CellSave> buses = new List<CellSave>();
        /// <summary>Час суток 0..24. Меньше нуля: в старых сохранениях нет, берём час старта.</summary>
        public float dayHour = -1f;
        /// <summary>Узлы свободного графа дорог (версия 2+).</summary>
        public List<FreeRoadNodeSave> freeNodes = new List<FreeRoadNodeSave>();
        /// <summary>Рёбра свободного графа дорог (версия 2+).</summary>
        public List<FreeRoadEdgeSave> freeEdges = new List<FreeRoadEdgeSave>();
    }

    /// <summary>Файл сохранения в папке данных приложения. Одно сохранение: новая игра его перезаписывает.</summary>
    public static class SaveSystem
    {
        public const int CurrentVersion = 2;

        static string PathFile => Path.Combine(Application.persistentDataPath, "city_save.json");

        public static bool Exists => File.Exists(PathFile);

        public static DateTime LastWrite => File.Exists(PathFile) ? File.GetLastWriteTime(PathFile) : DateTime.MinValue;

        public static bool TryRead(out SaveData data)
        {
            data = null;
            try
            {
                if (!File.Exists(PathFile)) return false;
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathFile));
                // Версия 1: клеточные дороги (игнорируем). Версии выше текущей не читаем.
                return data != null && data.version >= 1 && data.version <= CurrentVersion;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Не удалось прочитать сохранение: " + e.Message);
                data = null;
                return false;
            }
        }

        public static bool Write(SaveData data)
        {
            try
            {
                // Сначала во временный файл, потом подмена: обрыв посреди записи не портит старое сохранение.
                string tmp = PathFile + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data));
                if (File.Exists(PathFile)) File.Delete(PathFile);
                File.Move(tmp, PathFile);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Не удалось сохранить: " + e.Message);
                return false;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(PathFile)) File.Delete(PathFile);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Не удалось удалить сохранение: " + e.Message);
            }
        }
    }
}
