using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>Рекорд для пары «зона + режим».</summary>
    [Serializable]
    public class RecordEntry
    {
        public string key;
        public float bestTime = -1f;     // секунды; -1 = нет результата
        public float bestScore;          // очки/дистанция
        public int bestMedal;            // 0 нет, 1 бронза, 2 серебро, 3 золото
        public int completions;
    }

    /// <summary>Улучшения и состояние конкретной машины.</summary>
    [Serializable]
    public class VehicleState
    {
        public string vehicleId;
        public int engineLevel;
        public int gripLevel;
        public int durabilityLevel;
        [Range(0f, 1f)] public float health01 = 1f;
    }

    /// <summary>Настройки игрока. -1 означает «авто / как в пресете».</summary>
    [Serializable]
    public class SettingsData
    {
        [Header("Графика")]
        public int graphicsPreset = -1;      // -1 авто, 0 низкая ... 3 ультра
        public int drawDistance = -1;        // -1 авто, 0 близко, 1 средне, 2 далеко
        public int grassDensity = -1;        // -1 авто, 0 нет, 1 мало, 2 средне, 3 много
        public int treeDensity = -1;         // -1 авто, 0 мало, 1 средне, 2 много
        public int shadowQuality = -1;       // -1 авто, 0 выкл, 1 низкие, 2 высокие
        public int aiTrafficCount = -1;      // -1 авто, иначе число машин
        public int postProcessing = -1;      // -1 авто, 0 выкл, 1 вкл
        public int fpsLimit = -1;            // -1 авто, 30/60/90/120, 0 без ограничения
        public int resolutionIndex = -1;     // только ПК
        public bool fullscreen = true;

        [Header("Звук")]
        public float masterVolume = 1f;
        public float musicVolume = 0.6f;
        public float sfxVolume = 1f;

        [Header("Управление")]
        public int steeringMode;             // 0 кнопки, 1 наклон устройства
        public float tiltSensitivity = 1f;
        public bool invertTilt;
        public int layoutOverride;           // 0 авто, 1 телефон, 2 планшет, 3 ПК
        public float buttonScale = 1f;
        public float buttonOpacity = 0.65f;
        public int cameraMode;
    }

    /// <summary>Всё, что сохраняется на диск.</summary>
    [Serializable]
    public class SaveData
    {
        public int version = SaveSystem.CurrentVersion;
        public int currency = 1000;
        public int xp;
        public int level = 1;
        public float totalDistanceKm;
        public string selectedVehicleId = "sedan";
        public List<string> unlockedVehicles = new List<string> { "sedan" };
        public List<string> unlockedZones = new List<string>();
        public List<RecordEntry> records = new List<RecordEntry>();
        public List<VehicleState> vehicleStates = new List<VehicleState>();
        public List<string> achievements = new List<string>();
        public SettingsData settings = new SettingsData();
    }

    /// <summary>
    /// Читает и пишет сохранение в JSON-файл в Application.persistentDataPath.
    /// Запись «атомарная»: сначала во временный файл, затем подмена; предыдущая версия — в .bak.
    /// </summary>
    public static class SaveSystem
    {
        public const int CurrentVersion = 1;
        private const string FileName = "save.json";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);
        private static string BackupPath => FilePath + ".bak";
        private static string TempPath => FilePath + ".tmp";

        public static SaveData Load()
        {
            SaveData data = TryRead(FilePath) ?? TryRead(BackupPath) ?? new SaveData();
            Migrate(data);
            return data;
        }

        private static SaveData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Не удалось прочитать {path}: {e.Message}");
                return null;
            }
        }

        public static void Save(SaveData data)
        {
            if (data == null) return;
            try
            {
                data.version = CurrentVersion;
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(TempPath, json);
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, BackupPath, true);
                    File.Delete(FilePath);
                }
                File.Move(TempPath, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Ошибка сохранения: {e.Message}");
            }
        }

        /// <summary>Полный сброс прогресса.</summary>
        public static void DeleteAll()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                if (File.Exists(TempPath)) File.Delete(TempPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Ошибка удаления: {e.Message}");
            }
        }

        /// <summary>Обновление старых сохранений до текущей версии и починка пустых полей.</summary>
        private static void Migrate(SaveData d)
        {
            if (d.unlockedVehicles == null) d.unlockedVehicles = new List<string>();
            if (d.unlockedZones == null) d.unlockedZones = new List<string>();
            if (d.records == null) d.records = new List<RecordEntry>();
            if (d.vehicleStates == null) d.vehicleStates = new List<VehicleState>();
            if (d.achievements == null) d.achievements = new List<string>();
            if (d.settings == null) d.settings = new SettingsData();
            if (d.level < 1) d.level = 1;
            if (string.IsNullOrEmpty(d.selectedVehicleId)) d.selectedVehicleId = "sedan";
            if (!d.unlockedVehicles.Contains("sedan")) d.unlockedVehicles.Add("sedan");

            // Здесь будущие миграции: if (d.version < 2) { ... }
            d.version = CurrentVersion;
        }
    }
}
