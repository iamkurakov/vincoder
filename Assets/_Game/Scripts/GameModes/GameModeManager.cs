using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>Правила одного режима в конкретной сцене.</summary>
    [Serializable]
    public class ModeRules
    {
        public GameMode mode;
        public string title;
        [TextArea(2, 3)] public string description;

        [Header("Цели")]
        public bool useCheckpoints = true;
        [Min(1)] public int laps = 1;
        [Tooltip("Лимит времени, сек. 0 — без лимита (секундомер).")]
        public float timeLimit;
        [Tooltip("Добавка времени за каждый чекпоинт, сек.")]
        public float timeBonusPerCheckpoint;
        [Tooltip("Дистанция для Highway Run, км.")]
        public float targetDistanceKm;

        [Header("Медали по времени (сек.)")]
        public float goldTime = 60f;
        public float silverTime = 80f;
        public float bronzeTime = 110f;

        [Header("Столкновения")]
        public bool damageEnabled = true;
        [Tooltip("Сколько сильных ударов можно (0 — без ограничения).")]
        public int maxHeavyCollisions;
        [Tooltip("Штраф за сильный удар, сек.")]
        public float collisionPenaltySeconds;
        [Tooltip("Штраф за возврат на чекпоинт, сек.")]
        public float respawnPenaltySeconds;

        [Header("Мир")]
        public bool trafficEnabled;

        [Header("Награды")]
        public int baseReward = 250;
        public int baseXp = 200;
        public int xpPerKm = 25;
        public int currencyPerKm = 15;
        public int discoveryReward = 50;
    }

    /// <summary>Итог заезда для экрана результата.</summary>
    public struct ResultData
    {
        public bool success;
        public GameMode mode;
        public string title;
        public string reason;
        public float time;
        public float bestTime;
        public float distanceKm;
        public int medal;
        public int reward;
        public int xp;
        public bool newRecord;
        public int heavyCollisions;
    }

    /// <summary>
    /// Управляет заездом в сцене зоны: создаёт машину, настраивает камеру/HUD/трафик,
    /// ведёт обратный отсчёт, таймер, чекпоинты, награды, победу, поражение, паузу и рестарт.
    /// Один объект на игровую сцену.
    /// </summary>
    public class GameModeManager : MonoBehaviour
    {
        private enum Phase
        {
            Countdown,
            Playing,
            Paused,
            Finished
        }

        public static GameModeManager Instance { get; private set; }

        [Header("Объекты сцены")]
        public Transform spawnPoint;
        public CheckpointRaceManager checkpointRace;
        public VehicleCameraController cameraController;
        public VehicleHUD hud;
        public PauseMenuUI pauseMenu;
        public AITrafficSpawner traffic;

        [Header("Запуск сцены напрямую (в редакторе)")]
        public GameMode defaultMode = GameMode.FreeRide;
        [Tooltip("Машина, если в сохранении ничего не выбрано.")]
        public VehicleData fallbackVehicle;

        [Header("Правила режимов для этой сцены (пусто — значения по умолчанию)")]
        public List<ModeRules> rules = new List<ModeRules>();

        public GameMode Mode { get; private set; }
        public ModeRules Rules { get; private set; }
        public VehicleController Vehicle { get; private set; }
        public float Elapsed { get; private set; }
        public float DistanceKm { get; private set; }
        public bool IsPaused => phase == Phase.Paused;
        public bool IsFinished => phase == Phase.Finished;

        private Phase phase = Phase.Countdown;
        private Phase phaseBeforePause;
        private float timeBonus;
        private float penaltyTime;
        private int heavyCollisions;
        private float lastRewardKm;
        private float sessionStartHealth = 1f;

        private GameManager GM => GameManager.Instance;
        private PlayerProgress Progress => GameManager.Instance.Progress;

        // ---------- Запуск ----------

        private void Awake()
        {
            Instance = this;
            GameManager.EnsureExists();
            Mode = GM.HasLaunchRequest ? GM.CurrentMode : defaultMode;
            Rules = GetRules(Mode);
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private void OnEnable()
        {
            VehicleInput.PauseRequested += TogglePause;
            VehicleInput.RestartRequested += Restart;
        }

        private void OnDisable()
        {
            VehicleInput.PauseRequested -= TogglePause;
            VehicleInput.RestartRequested -= Restart;
            if (checkpointRace != null)
            {
                checkpointRace.CheckpointPassed -= OnCheckpointPassed;
                checkpointRace.WrongCheckpoint -= OnWrongCheckpoint;
                checkpointRace.Finished -= OnCheckpointsFinished;
            }
            if (Vehicle != null && Vehicle.damage != null)
            {
                Vehicle.damage.Hit -= OnVehicleHit;
                Vehicle.damage.Destroyed -= OnVehicleDestroyed;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private void Start()
        {
            if (!SpawnVehicle()) return;
            SetupMode();
            StartCoroutine(StartSequence());
        }

        public ModeRules GetRules(GameMode mode)
        {
            foreach (var r in rules)
                if (r != null && r.mode == mode) return r;
            return DefaultRules(mode);
        }

        private bool SpawnVehicle()
        {
            VehicleData data = Progress.SelectedVehicle;
            if (data == null) data = fallbackVehicle;
            if (data == null || data.prefab == null)
            {
                Debug.LogError("[GameModeManager] Нет машины: проверьте список vehicles в GameManager и поле prefab у VehicleData.");
                return false;
            }

            Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.up;
            Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
            GameObject go = Instantiate(data.prefab, pos, rot);
            go.name = "Player_" + data.id;

            Vehicle = go.GetComponent<VehicleController>();
            if (Vehicle == null)
            {
                Debug.LogError("[GameModeManager] На префабе машины нет VehicleController.");
                return false;
            }
            Vehicle.isPlayer = true;
            VehicleState state = Progress.GetVehicleState(data.id);
            Vehicle.Initialize(data, state);
            Vehicle.SetSafePoint(pos, rot);
            sessionStartHealth = state.health01;

            if (Vehicle.damage != null)
            {
                Vehicle.damage.damageEnabled = Rules.damageEnabled;
                Vehicle.damage.Hit += OnVehicleHit;
                Vehicle.damage.Destroyed += OnVehicleDestroyed;
            }

            if (cameraController != null) cameraController.SetTarget(Vehicle);
            if (hud != null) hud.Bind(Vehicle);
            if (traffic != null) traffic.SetPlayer(Vehicle.transform);
            return true;
        }

        private void SetupMode()
        {
            bool useCp = Rules.useCheckpoints && checkpointRace != null && checkpointRace.checkpoints.Count > 0;
            if (checkpointRace != null)
            {
                if (useCp)
                {
                    checkpointRace.anyOrder = Mode == GameMode.FreeRide;
                    checkpointRace.laps = Mathf.Max(1, Rules.laps);
                    checkpointRace.CheckpointPassed += OnCheckpointPassed;
                    checkpointRace.WrongCheckpoint += OnWrongCheckpoint;
                    checkpointRace.Finished += OnCheckpointsFinished;
                }
                else
                {
                    checkpointRace.gameObject.SetActive(false);
                }
            }

            if (hud != null)
            {
                hud.SetTimerVisible(Mode != GameMode.FreeRide);
                hud.SetCheckpointsVisible(useCp);
                hud.SetDistance(Mode == GameMode.FreeRide || Mode == GameMode.HighwayRun ? 0f : -1f, Rules.targetDistanceKm);
                hud.SetObjective(Rules.description);
                hud.SetTimer(Rules.timeLimit > 0f ? Rules.timeLimit : 0f);
            }

            if (traffic != null) traffic.SetTrafficEnabled(Rules.trafficEnabled);
        }

        private IEnumerator StartSequence()
        {
            phase = Phase.Countdown;
            GM.SetState(GameState.Countdown);
            Vehicle.inputEnabled = false;

            if (Mode != GameMode.FreeRide && hud != null)
            {
                for (int i = 3; i > 0; i--)
                {
                    hud.ShowCountdown(i.ToString());
                    yield return new WaitForSeconds(1f);
                }
                hud.ShowCountdown("Старт!");
            }
            else if (hud != null)
            {
                hud.ShowMessage(Rules.title, 2.5f);
                yield return new WaitForSeconds(0.5f);
            }

            BeginPlay();

            yield return new WaitForSeconds(0.8f);
            if (hud != null) hud.ShowCountdown("");
        }

        private void BeginPlay()
        {
            phase = Phase.Playing;
            GM.SetState(GameState.Playing);
            Vehicle.inputEnabled = true;
            Elapsed = 0f;
            DistanceKm = 0f;
            lastRewardKm = 0f;
            if (checkpointRace != null && checkpointRace.isActiveAndEnabled && Rules.useCheckpoints)
            {
                checkpointRace.Begin();
                UpdateCompass();
            }
        }

        // ---------- Игра ----------

        private void Update()
        {
            if (phase != Phase.Playing || Vehicle == null) return;
            float dt = Time.deltaTime;
            Elapsed += dt;
            DistanceKm += Vehicle.SpeedKmh * dt / 3600f;

            if (hud != null)
            {
                if (Rules.timeLimit > 0f)
                {
                    float remaining = Rules.timeLimit + timeBonus - Elapsed - penaltyTime;
                    hud.SetTimer(remaining, remaining < 10f);
                    if (remaining <= 0f)
                    {
                        Fail("Время вышло");
                        return;
                    }
                }
                else if (Mode != GameMode.FreeRide)
                {
                    hud.SetTimer(Elapsed + penaltyTime);
                }
            }

            switch (Mode)
            {
                case GameMode.FreeRide:
                    if (hud != null) hud.SetDistance(DistanceKm);
                    if (DistanceKm - lastRewardKm >= 1f)
                    {
                        lastRewardKm += 1f;
                        Progress.AddXp(Rules.xpPerKm);
                        Progress.AddCurrency(Rules.currencyPerKm);
                        if (hud != null) hud.ShowMessage($"+1 км: +{Rules.xpPerKm} опыта, +{Rules.currencyPerKm} монет", 2f);
                    }
                    break;

                case GameMode.HighwayRun:
                    if (hud != null) hud.SetDistance(DistanceKm, Rules.targetDistanceKm);
                    if (Rules.targetDistanceKm > 0f && DistanceKm >= Rules.targetDistanceKm) Complete();
                    break;
            }
        }

        private void UpdateCompass()
        {
            if (hud == null || checkpointRace == null) return;
            var next = checkpointRace.NextCheckpoint;
            hud.SetCompassTarget(next != null ? next.transform : null);
        }

        private void OnCheckpointPassed(int passed, int total)
        {
            if (hud != null) hud.SetCheckpoints(passed, total);
            if (passed == 0) return;

            if (Mode == GameMode.FreeRide)
            {
                Progress.AddCurrency(Rules.discoveryReward);
                Progress.AddXp(Rules.discoveryReward);
                if (hud != null) hud.ShowMessage($"Точка найдена! +{Rules.discoveryReward}", 2f);
                return;
            }

            if (Rules.timeBonusPerCheckpoint > 0f)
            {
                timeBonus += Rules.timeBonusPerCheckpoint;
                if (hud != null) hud.ShowMessage($"Чекпоинт! +{Rules.timeBonusPerCheckpoint:0} с", 1.5f);
            }
            else if (hud != null) hud.ShowMessage("Чекпоинт!", 1f);
            UpdateCompass();
        }

        private void OnWrongCheckpoint(Checkpoint cp)
        {
            if (hud != null) hud.ShowMessage("Не тот чекпоинт — следуйте за стрелкой", 2f);
        }

        private void OnCheckpointsFinished()
        {
            if (Mode == GameMode.FreeRide)
            {
                Progress.AddCurrency(500);
                Progress.UnlockAchievement("explorer_" + GM.CurrentZoneOrScene());
                if (hud != null) hud.ShowMessage("Все точки найдены! +500", 3f);
                Progress.Save();
                return;
            }
            Complete();
        }

        private void OnVehicleHit(float damage, bool heavy, Vector3 point)
        {
            if (phase != Phase.Playing || !heavy) return;
            heavyCollisions++;
            if (Rules.collisionPenaltySeconds > 0f)
            {
                penaltyTime += Rules.collisionPenaltySeconds;
                if (hud != null) hud.ShowMessage($"Столкновение! +{Rules.collisionPenaltySeconds:0} с", 1.5f);
            }
            if (Rules.maxHeavyCollisions > 0)
            {
                if (heavyCollisions > Rules.maxHeavyCollisions)
                    Fail("Слишком много столкновений");
                else if (hud != null)
                    hud.ShowMessage($"Столкновений: {heavyCollisions} / {Rules.maxHeavyCollisions}", 1.5f);
            }
        }

        private void OnVehicleDestroyed()
        {
            if (Mode == GameMode.FreeRide)
            {
                Vehicle.damage.Repair(Vehicle.damage.maxHealth * 0.5f);
                Vehicle.ResetToPoint(Vehicle.lastSafePosition, Vehicle.lastSafeRotation);
                if (hud != null) hud.ShowMessage("Машина разбита — эвакуация и экстренный ремонт", 3f);
                return;
            }
            Fail("Машина разбита");
        }

        // ---------- Завершение ----------

        private void Complete()
        {
            if (phase == Phase.Finished) return;
            phase = Phase.Finished;
            GM.SetState(GameState.Results);
            Vehicle.forceBrake = true;
            if (checkpointRace != null) checkpointRace.Stop();

            float finalTime = Elapsed + penaltyTime;
            int medal;
            if (Mode == GameMode.HighwayRun)
                medal = heavyCollisions == 0 ? 3 : heavyCollisions == 1 ? 2 : 1;
            else
                medal = finalTime <= Rules.goldTime ? 3 : finalTime <= Rules.silverTime ? 2 : finalTime <= Rules.bronzeTime ? 1 : 0;

            float mul = medal == 3 ? 2f : medal == 2 ? 1.5f : medal == 1 ? 1.2f : 1f;
            int reward = Mathf.RoundToInt(Rules.baseReward * mul);
            int xp = Mathf.RoundToInt(Rules.baseXp * mul);

            string key = PlayerProgress.RecordKey(GM.CurrentZoneOrScene(), Mode);
            bool newRecord = Progress.SubmitTime(key, finalTime, medal);
            RecordEntry rec = Progress.GetRecord(key);

            Progress.AddCurrency(reward);
            Progress.AddXp(xp);
            Progress.AddDistance(DistanceKm);
            Progress.UnlockAchievement("first_finish");
            Progress.UnlockAchievement("mode_" + Mode);
            if (medal == 3) Progress.UnlockAchievement("gold_" + key);
            StoreHealth();
            Progress.Save();

            var result = new ResultData
            {
                success = true,
                mode = Mode,
                title = "Заезд завершён",
                reason = Rules.title,
                time = finalTime,
                bestTime = rec != null ? rec.bestTime : finalTime,
                distanceKm = DistanceKm,
                medal = medal,
                reward = reward,
                xp = xp,
                newRecord = newRecord,
                heavyCollisions = heavyCollisions
            };
            StartCoroutine(ShowResultsDelayed(result));
        }

        private void Fail(string reason)
        {
            if (phase == Phase.Finished) return;
            phase = Phase.Finished;
            GM.SetState(GameState.Results);
            Vehicle.forceBrake = true;
            if (checkpointRace != null) checkpointRace.Stop();

            int xp = Mathf.RoundToInt(DistanceKm * Rules.xpPerKm);
            Progress.AddXp(xp);
            Progress.AddDistance(DistanceKm);
            StoreHealth();
            Progress.Save();

            var result = new ResultData
            {
                success = false,
                mode = Mode,
                title = "Не получилось",
                reason = reason,
                time = Elapsed + penaltyTime,
                distanceKm = DistanceKm,
                xp = xp,
                heavyCollisions = heavyCollisions
            };
            StartCoroutine(ShowResultsDelayed(result));
        }

        private IEnumerator ShowResultsDelayed(ResultData result)
        {
            yield return new WaitForSeconds(1.2f);
            if (pauseMenu != null) pauseMenu.ShowResults(result);
        }

        private void StoreHealth()
        {
            if (Vehicle == null || Vehicle.data == null) return;
            float h = Vehicle.damage != null ? Vehicle.damage.Health01 : sessionStartHealth;
            Progress.StoreVehicleHealth(Vehicle.data.id, h);
        }

        // ---------- Пауза и навигация ----------

        public void TogglePause()
        {
            if (phase == Phase.Finished) return;
            if (phase == Phase.Paused) Resume();
            else PauseGame();
        }

        public void PauseGame()
        {
            if (phase == Phase.Paused || phase == Phase.Finished) return;
            phaseBeforePause = phase;
            phase = Phase.Paused;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            GM.SetState(GameState.Paused);
            if (pauseMenu != null) pauseMenu.ShowPause();
        }

        public void Resume()
        {
            if (phase != Phase.Paused) return;
            phase = phaseBeforePause;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            GM.SetState(phase == Phase.Countdown ? GameState.Countdown : GameState.Playing);
            if (pauseMenu != null) pauseMenu.HidePause();
        }

        public void Restart()
        {
            StoreHealth();
            Progress.Save();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            GM.ReloadCurrent();
        }

        public void RespawnAtLastCheckpoint()
        {
            if (Vehicle == null) return;
            Vehicle.ResetToPoint(Vehicle.lastSafePosition, Vehicle.lastSafeRotation);
            if (phase == Phase.Paused) Resume();
            if (Rules.respawnPenaltySeconds > 0f && phase == Phase.Playing)
            {
                penaltyTime += Rules.respawnPenaltySeconds;
                if (hud != null) hud.ShowMessage($"Возврат на точку: +{Rules.respawnPenaltySeconds:0} с", 1.5f);
            }
        }

        public void ExitToMenu()
        {
            StoreHealth();
            Progress.AddDistance(phase == Phase.Finished ? 0f : DistanceKm);
            Progress.Save();
            GM.LoadMainMenu();
        }

        public void ExitToGarage()
        {
            StoreHealth();
            Progress.Save();
            GM.LoadGarage();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && (phase == Phase.Playing || phase == Phase.Countdown)) PauseGame();
        }

        // ---------- Правила по умолчанию ----------

        public static ModeRules DefaultRules(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.TimeTrial:
                    return new ModeRules
                    {
                        mode = mode, title = "Заезд на время",
                        description = "Пройдите маршрут как можно быстрее",
                        useCheckpoints = true, goldTime = 70f, silverTime = 90f, bronzeTime = 120f,
                        baseReward = 250, baseXp = 200
                    };
                case GameMode.CheckpointRace:
                    return new ModeRules
                    {
                        mode = mode, title = "Гонка по чекпоинтам",
                        description = "Проезжайте чекпоинты по порядку. Каждый добавляет 10 секунд",
                        useCheckpoints = true, timeLimit = 60f, timeBonusPerCheckpoint = 10f,
                        goldTime = 70f, silverTime = 95f, bronzeTime = 130f, baseReward = 300, baseXp = 220
                    };
                case GameMode.OffroadChallenge:
                    return new ModeRules
                    {
                        mode = mode, title = "Испытание бездорожьем",
                        description = "Доберитесь до финиша по грязи, песку и камням. Не разбейте машину",
                        useCheckpoints = true, timeLimit = 300f, respawnPenaltySeconds = 5f,
                        goldTime = 120f, silverTime = 170f, bronzeTime = 240f, baseReward = 400, baseXp = 300
                    };
                case GameMode.CityChallenge:
                    return new ModeRules
                    {
                        mode = mode, title = "Городской заезд",
                        description = "Проедьте маршрут по городу. Не больше трёх сильных столкновений",
                        useCheckpoints = true, timeLimit = 240f, maxHeavyCollisions = 3, collisionPenaltySeconds = 5f,
                        trafficEnabled = true, goldTime = 90f, silverTime = 120f, bronzeTime = 170f,
                        baseReward = 350, baseXp = 260
                    };
                case GameMode.HighwayRun:
                    return new ModeRules
                    {
                        mode = mode, title = "Скоростная трасса",
                        description = "Проедьте 3 км в потоке машин. Чистая езда — золото",
                        useCheckpoints = false, targetDistanceKm = 3f, maxHeavyCollisions = 2,
                        trafficEnabled = true, goldTime = 120f, silverTime = 150f, bronzeTime = 200f,
                        baseReward = 350, baseXp = 260
                    };
                default:
                    return new ModeRules
                    {
                        mode = GameMode.FreeRide, title = "Свободная езда",
                        description = "Катайтесь где хотите и ищите отмеченные точки",
                        useCheckpoints = true, trafficEnabled = true, damageEnabled = true,
                        baseReward = 0, baseXp = 0
                    };
            }
        }

        public static string ModeTitle(GameMode mode) => DefaultRules(mode).title;
    }
}
