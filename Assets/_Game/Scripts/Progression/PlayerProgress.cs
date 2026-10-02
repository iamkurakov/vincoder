using System;
using UnityEngine;

namespace TerrainDrive
{
    public enum UpgradeKind
    {
        Engine,
        Grip,
        Durability
    }

    /// <summary>
    /// Прогресс игрока поверх SaveData: валюта, опыт и уровни, машины, улучшения,
    /// рекорды, достижения. Не MonoBehaviour — создаётся и хранится в GameManager.
    /// </summary>
    public class PlayerProgress
    {
        public const int MaxUpgradeLevel = 3;

        public SaveData Data { get; }
        public SettingsData Settings => Data.settings;

        /// <summary>Любое изменение прогресса (для обновления интерфейса).</summary>
        public event Action Changed;
        public event Action<int> LevelUp;
        public event Action<string> AchievementUnlocked;

        private readonly VehicleData[] catalog;

        public PlayerProgress(SaveData data, VehicleData[] catalog)
        {
            Data = data ?? new SaveData();
            this.catalog = catalog ?? new VehicleData[0];

            // Первая машина каталога всегда открыта.
            if (this.catalog.Length > 0 && this.catalog[0] != null && !Data.unlockedVehicles.Contains(this.catalog[0].id))
                Data.unlockedVehicles.Add(this.catalog[0].id);

            if (SelectedVehicle == null && this.catalog.Length > 0 && this.catalog[0] != null)
                Data.selectedVehicleId = this.catalog[0].id;
        }

        // ---------- Валюта ----------

        public int Currency => Data.currency;

        public void AddCurrency(int amount)
        {
            if (amount <= 0) return;
            Data.currency += amount;
            Changed?.Invoke();
        }

        public bool Spend(int amount)
        {
            if (amount < 0 || Data.currency < amount) return false;
            Data.currency -= amount;
            Changed?.Invoke();
            return true;
        }

        // ---------- Опыт и уровни ----------

        public int Level => Data.level;
        public int Xp => Data.xp;

        /// <summary>Сколько всего опыта нужно, чтобы иметь уровень level. 2 → 500, 3 → 1500, 4 → 3000…</summary>
        public static int TotalXpForLevel(int level) => 250 * (level - 1) * level;

        public int XpIntoLevel => Data.xp - TotalXpForLevel(Data.level);
        public int XpNeededForNext => TotalXpForLevel(Data.level + 1) - TotalXpForLevel(Data.level);

        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            Data.xp += amount;
            while (Data.xp >= TotalXpForLevel(Data.level + 1))
            {
                Data.level++;
                LevelUp?.Invoke(Data.level);
            }
            Changed?.Invoke();
        }

        // ---------- Машины ----------

        public VehicleData SelectedVehicle
        {
            get
            {
                foreach (var v in catalog)
                    if (v != null && v.id == Data.selectedVehicleId && IsVehicleUnlocked(v.id)) return v;
                return catalog.Length > 0 ? catalog[0] : null;
            }
        }

        public bool IsVehicleUnlocked(string id) => Data.unlockedVehicles.Contains(id);

        public bool CanBuyVehicle(VehicleData v) =>
            v != null && !IsVehicleUnlocked(v.id) && Level >= v.requiredLevel && Currency >= v.price;

        public bool BuyVehicle(VehicleData v)
        {
            if (v == null) return false;
            if (IsVehicleUnlocked(v.id)) return true;
            if (Level < v.requiredLevel) return false;
            if (!Spend(v.price)) return false;
            Data.unlockedVehicles.Add(v.id);
            UnlockAchievement("garage_" + v.id);
            Save();
            return true;
        }

        public bool SelectVehicle(string id)
        {
            if (!IsVehicleUnlocked(id)) return false;
            Data.selectedVehicleId = id;
            Save();
            return true;
        }

        public VehicleState GetVehicleState(string vehicleId)
        {
            foreach (var s in Data.vehicleStates)
                if (s.vehicleId == vehicleId) return s;
            var created = new VehicleState { vehicleId = vehicleId };
            Data.vehicleStates.Add(created);
            return created;
        }

        public int GetUpgradeLevel(string vehicleId, UpgradeKind kind)
        {
            var s = GetVehicleState(vehicleId);
            switch (kind)
            {
                case UpgradeKind.Engine: return s.engineLevel;
                case UpgradeKind.Grip: return s.gripLevel;
                default: return s.durabilityLevel;
            }
        }

        public int UpgradeCost(VehicleData v, UpgradeKind kind)
        {
            int lvl = GetUpgradeLevel(v.id, kind);
            if (lvl >= MaxUpgradeLevel) return -1;
            return v.upgradeBaseCost * (lvl + 1);
        }

        public bool BuyUpgrade(VehicleData v, UpgradeKind kind)
        {
            if (v == null || !IsVehicleUnlocked(v.id)) return false;
            int cost = UpgradeCost(v, kind);
            if (cost < 0 || !Spend(cost)) return false;
            var s = GetVehicleState(v.id);
            switch (kind)
            {
                case UpgradeKind.Engine: s.engineLevel++; break;
                case UpgradeKind.Grip: s.gripLevel++; break;
                default: s.durabilityLevel++; break;
            }
            Save();
            return true;
        }

        /// <summary>Стоимость полного ремонта в гараже.</summary>
        public int RepairCost(VehicleData v)
        {
            var s = GetVehicleState(v.id);
            return Mathf.CeilToInt((1f - s.health01) * v.repairFullCost);
        }

        public bool Repair(VehicleData v)
        {
            var s = GetVehicleState(v.id);
            if (s.health01 >= 0.999f) return true;
            if (!Spend(RepairCost(v))) return false;
            s.health01 = 1f;
            Save();
            return true;
        }

        public void StoreVehicleHealth(string vehicleId, float health01)
        {
            GetVehicleState(vehicleId).health01 = Mathf.Clamp01(health01);
        }

        // ---------- Рекорды ----------

        public static string RecordKey(string zone, GameMode mode) => zone + "_" + mode;

        public RecordEntry GetRecord(string key)
        {
            foreach (var r in Data.records)
                if (r.key == key) return r;
            return null;
        }

        private RecordEntry GetOrCreateRecord(string key)
        {
            var r = GetRecord(key);
            if (r != null) return r;
            r = new RecordEntry { key = key };
            Data.records.Add(r);
            return r;
        }

        /// <summary>Регистрирует время заезда. Возвращает true, если это новый рекорд.</summary>
        public bool SubmitTime(string key, float seconds, int medal)
        {
            var r = GetOrCreateRecord(key);
            r.completions++;
            r.bestMedal = Mathf.Max(r.bestMedal, medal);
            bool isRecord = r.bestTime < 0f || seconds < r.bestTime;
            if (isRecord) r.bestTime = seconds;
            Changed?.Invoke();
            return isRecord;
        }

        /// <summary>Регистрирует очки/дистанцию (больше — лучше). Возвращает true при новом рекорде.</summary>
        public bool SubmitScore(string key, float score, int medal)
        {
            var r = GetOrCreateRecord(key);
            r.completions++;
            r.bestMedal = Mathf.Max(r.bestMedal, medal);
            bool isRecord = score > r.bestScore;
            if (isRecord) r.bestScore = score;
            Changed?.Invoke();
            return isRecord;
        }

        // ---------- Достижения ----------

        public bool HasAchievement(string id) => Data.achievements.Contains(id);

        public bool UnlockAchievement(string id)
        {
            if (string.IsNullOrEmpty(id) || HasAchievement(id)) return false;
            Data.achievements.Add(id);
            AchievementUnlocked?.Invoke(id);
            Changed?.Invoke();
            return true;
        }

        public void AddDistance(float km)
        {
            if (km <= 0f) return;
            float before = Data.totalDistanceKm;
            Data.totalDistanceKm += km;
            int[] milestones = { 10, 50, 100, 500 };
            foreach (int m in milestones)
                if (before < m && Data.totalDistanceKm >= m)
                    UnlockAchievement("distance_" + m);
        }

        public void Save()
        {
            SaveSystem.Save(Data);
            Changed?.Invoke();
        }

        public void ResetAll()
        {
            SaveSystem.DeleteAll();
            var fresh = new SaveData();
            fresh.settings = Data.settings; // настройки не сбрасываем
            Data.currency = fresh.currency;
            Data.xp = fresh.xp;
            Data.level = fresh.level;
            Data.totalDistanceKm = 0f;
            Data.selectedVehicleId = catalog.Length > 0 && catalog[0] != null ? catalog[0].id : fresh.selectedVehicleId;
            Data.unlockedVehicles = fresh.unlockedVehicles;
            if (catalog.Length > 0 && catalog[0] != null && !Data.unlockedVehicles.Contains(catalog[0].id))
                Data.unlockedVehicles.Add(catalog[0].id);
            Data.unlockedZones = fresh.unlockedZones;
            Data.records = fresh.records;
            Data.vehicleStates = fresh.vehicleStates;
            Data.achievements = fresh.achievements;
            Save();
        }
    }
}
