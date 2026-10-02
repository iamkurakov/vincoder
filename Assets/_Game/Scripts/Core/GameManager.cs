using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TerrainDrive
{
    /// <summary>Игровые режимы.</summary>
    public enum GameMode
    {
        FreeRide,
        TimeTrial,
        CheckpointRace,
        OffroadChallenge,
        CityChallenge,
        HighwayRun
    }

    /// <summary>Глобальное состояние игры.</summary>
    public enum GameState
    {
        Boot,
        Menu,
        Garage,
        Loading,
        Countdown,
        Playing,
        Paused,
        Results
    }

    /// <summary>Описание игровой зоны (сцены) для меню выбора.</summary>
    [Serializable]
    public class ZoneInfo
    {
        public string sceneName = "TestTrack";
        public string displayName = "Тестовый полигон";
        [TextArea] public string description;
        [Tooltip("С какого уровня игрока зона открывается.")]
        public int unlockLevel = 1;
        [Tooltip("Какие режимы доступны в зоне.")]
        public GameMode[] modes = { GameMode.FreeRide };
    }

    /// <summary>
    /// «Дирижёр» игры. Живёт всё время (DontDestroyOnLoad), хранит прогресс,
    /// выбранный режим и зону, загружает сцены.
    /// Где лежит: префаб Resources/GameSystems (ставится в сцену Boot).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Сцены")]
        public string bootScene = "Boot";
        public string mainMenuScene = "MainMenu";
        public string garageScene = "Garage";

        [Header("Каталог")]
        [Tooltip("Все машины игры. Первая в списке открыта с самого начала.")]
        public VehicleData[] vehicles;
        [Tooltip("Все игровые зоны.")]
        public ZoneInfo[] zones;

        [Header("Экран загрузки (необязательно)")]
        public CanvasGroup loadingOverlay;
        public Image loadingBar;

        public GameState State { get; private set; } = GameState.Boot;
        public GameMode CurrentMode { get; private set; } = GameMode.FreeRide;
        public string CurrentZone { get; private set; }
        /// <summary>true, если игрок запустил заезд из меню (а не сцену напрямую в редакторе).</summary>
        public bool HasLaunchRequest { get; private set; }
        public PlayerProgress Progress { get; private set; }

        public event Action<GameState> StateChanged;

        private Coroutine loadRoutine;

        /// <summary>
        /// Гарантирует, что системы игры существуют. Вызывается из любой сцены,
        /// поэтому любую сцену можно запускать в редакторе напрямую.
        /// </summary>
        public static GameManager EnsureExists()
        {
            if (Instance != null) return Instance;

            var prefab = Resources.Load<GameObject>("GameSystems");
            if (prefab != null)
            {
                var go = Instantiate(prefab);
                go.name = "GameSystems";
            }
            else
            {
                Debug.LogWarning("[GameManager] Resources/GameSystems не найден — создаю минимальный набор систем.");
                var go = new GameObject("GameSystems");
                go.AddComponent<PlatformManager>();
                go.AddComponent<GameManager>();
                go.AddComponent<GraphicsSettingsManager>();
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Progress = new PlayerProgress(SaveSystem.Load(), vehicles);
            Progress.LevelUp += OnLevelUp;
            RefreshUnlockedZones();
            ApplyAudioSettings();

            if (loadingOverlay != null) SetOverlay(false);
        }

        private void Start()
        {
            if (SceneManager.GetActiveScene().name == bootScene)
                LoadMainMenu();
            else
                SetState(GuessStateForScene(SceneManager.GetActiveScene().name));
        }

        private GameState GuessStateForScene(string sceneName)
        {
            if (sceneName == mainMenuScene) return GameState.Menu;
            if (sceneName == garageScene) return GameState.Garage;
            return GameState.Playing;
        }

        // ---------- Состояние ----------

        public void SetState(GameState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        // ---------- Навигация ----------

        public void LoadMainMenu() => LoadScene(mainMenuScene, GameState.Menu);

        public void LoadGarage() => LoadScene(garageScene, GameState.Garage);

        /// <summary>Запуск заезда: зона + режим.</summary>
        public void StartGame(string zoneScene, GameMode mode)
        {
            CurrentZone = zoneScene;
            CurrentMode = mode;
            HasLaunchRequest = true;
            LoadScene(zoneScene, GameState.Countdown);
        }

        /// <summary>Перезапуск текущей сцены (рестарт заезда).</summary>
        public void ReloadCurrent()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(CurrentZone)) CurrentZone = scene;
            HasLaunchRequest = true;
            LoadScene(scene, GameState.Countdown);
        }

        public void QuitGame()
        {
            Progress.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void LoadScene(string sceneName, GameState stateAfter)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[GameManager] Сцена «{sceneName}» не добавлена в Build Settings → Scenes In Build.");
                return;
            }
            if (loadRoutine != null) StopCoroutine(loadRoutine);
            loadRoutine = StartCoroutine(LoadRoutine(sceneName, stateAfter));
        }

        private IEnumerator LoadRoutine(string sceneName, GameState stateAfter)
        {
            SetState(GameState.Loading);
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SetOverlay(true);
            if (loadingBar != null) loadingBar.fillAmount = 0f;

            // Кадр на то, чтобы экран загрузки успел отрисоваться.
            yield return null;

            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            while (op != null && !op.isDone)
            {
                if (loadingBar != null) loadingBar.fillAmount = Mathf.Clamp01(op.progress / 0.9f);
                yield return null;
            }

            SetOverlay(false);
            SetState(stateAfter);
            loadRoutine = null;
        }

        private void SetOverlay(bool visible)
        {
            if (loadingOverlay == null) return;
            loadingOverlay.alpha = visible ? 1f : 0f;
            loadingOverlay.blocksRaycasts = visible;
            loadingOverlay.gameObject.SetActive(visible);
        }

        /// <summary>Имя текущей зоны; если сцену запустили напрямую — имя активной сцены.</summary>
        public string CurrentZoneOrScene()
        {
            string active = SceneManager.GetActiveScene().name;
            return !string.IsNullOrEmpty(CurrentZone) && CurrentZone == active ? CurrentZone : active;
        }

        // ---------- Каталог ----------

        public VehicleData GetVehicle(string id)
        {
            if (vehicles == null) return null;
            foreach (var v in vehicles)
                if (v != null && v.id == id) return v;
            return null;
        }

        public ZoneInfo GetZone(string sceneName)
        {
            if (zones == null) return null;
            foreach (var z in zones)
                if (z != null && z.sceneName == sceneName) return z;
            return null;
        }

        public bool IsZoneUnlocked(ZoneInfo zone)
        {
            if (zone == null) return false;
            return Progress.Level >= zone.unlockLevel || Progress.Data.unlockedZones.Contains(zone.sceneName);
        }

        private void OnLevelUp(int level) => RefreshUnlockedZones();

        private void RefreshUnlockedZones()
        {
            if (zones == null) return;
            bool changed = false;
            foreach (var z in zones)
            {
                if (z == null) continue;
                if (Progress.Level >= z.unlockLevel && !Progress.Data.unlockedZones.Contains(z.sceneName))
                {
                    Progress.Data.unlockedZones.Add(z.sceneName);
                    changed = true;
                }
            }
            if (changed) Progress.Save();
        }

        // ---------- Звук ----------

        public void ApplyAudioSettings()
        {
            AudioListener.volume = Mathf.Clamp01(Progress.Data.settings.masterVolume);
        }

        // ---------- Жизненный цикл приложения ----------

        private void OnApplicationPause(bool paused)
        {
            if (paused) Progress?.Save();
        }

        private void OnApplicationQuit()
        {
            Progress?.Save();
        }
    }
}
