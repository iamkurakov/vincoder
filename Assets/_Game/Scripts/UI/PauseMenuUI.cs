using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainDrive
{
    /// <summary>
    /// Меню паузы (продолжить, рестарт, вернуться на чекпоинт, настройки, выход)
    /// и экран результата заезда. Вешается на объект внутри HUD-канваса.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("Пауза")]
        public GameObject pausePanel;
        public Button resumeButton;
        public Button restartButton;
        public Button respawnButton;
        public Button settingsButton;
        public Button garageButton;
        public Button menuButton;

        [Header("Результат")]
        public GameObject resultsPanel;
        public TMP_Text resultTitle;
        public TMP_Text resultReason;
        public TMP_Text resultTime;
        public TMP_Text resultBest;
        public TMP_Text resultMedal;
        public TMP_Text resultReward;
        public Button resultRestartButton;
        public Button resultGarageButton;
        public Button resultMenuButton;

        [Header("Настройки")]
        public SettingsUI settings;

        [Header("Затемнение фона")]
        public GameObject dimmer;

        private static GameModeManager Mode => GameModeManager.Instance;

        private void Awake()
        {
            Bind(resumeButton, () => Mode?.Resume());
            Bind(restartButton, () => Mode?.Restart());
            Bind(respawnButton, () => Mode?.RespawnAtLastCheckpoint());
            Bind(settingsButton, OpenSettings);
            Bind(garageButton, () => Mode?.ExitToGarage());
            Bind(menuButton, () => Mode?.ExitToMenu());
            Bind(resultRestartButton, () => Mode?.Restart());
            Bind(resultGarageButton, () => Mode?.ExitToGarage());
            Bind(resultMenuButton, () => Mode?.ExitToMenu());

            if (settings != null) settings.Closed += OnSettingsClosed;
            SetActive(pausePanel, false);
            SetActive(resultsPanel, false);
            SetActive(dimmer, false);
            if (settings != null) settings.gameObject.SetActive(false);
        }

        private static void Bind(Button b, UnityEngine.Events.UnityAction action)
        {
            if (b != null) b.onClick.AddListener(action);
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null) go.SetActive(on);
        }

        public void ShowPause()
        {
            SetActive(dimmer, true);
            SetActive(pausePanel, true);
            SetActive(resultsPanel, false);
        }

        public void HidePause()
        {
            SetActive(dimmer, false);
            SetActive(pausePanel, false);
            if (settings != null) settings.gameObject.SetActive(false);
        }

        private void OpenSettings()
        {
            SetActive(pausePanel, false);
            if (settings != null) settings.Open();
        }

        private void OnSettingsClosed()
        {
            if (Mode != null && Mode.IsPaused) SetActive(pausePanel, true);
        }

        public void ShowResults(ResultData r)
        {
            SetActive(dimmer, true);
            SetActive(pausePanel, false);
            SetActive(resultsPanel, true);

            if (resultTitle != null) resultTitle.text = r.title;
            if (resultReason != null) resultReason.text = r.reason;
            if (resultTime != null)
            {
                string t = "Время: " + VehicleHUD.FormatTime(r.time);
                if (r.distanceKm > 0.01f) t += $"   Дистанция: {r.distanceKm:0.00} км";
                if (r.heavyCollisions > 0) t += $"   Столкновений: {r.heavyCollisions}";
                resultTime.text = t;
            }
            if (resultBest != null)
            {
                if (!r.success) resultBest.text = "";
                else if (r.newRecord) resultBest.text = "Новый рекорд!";
                else resultBest.text = "Рекорд: " + VehicleHUD.FormatTime(r.bestTime);
            }
            if (resultMedal != null)
            {
                resultMedal.text = !r.success ? "" :
                    r.medal == 3 ? "Золото" :
                    r.medal == 2 ? "Серебро" :
                    r.medal == 1 ? "Бронза" : "Без медали";
            }
            if (resultReward != null)
            {
                resultReward.text = r.success
                    ? $"+{r.reward} монет   +{r.xp} опыта"
                    : (r.xp > 0 ? $"+{r.xp} опыта за пройденный путь" : "");
            }
        }
    }
}
