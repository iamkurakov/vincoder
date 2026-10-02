using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainDrive
{
    /// <summary>
    /// Главное меню: «Играть» → выбор зоны → выбор режима; гараж; настройки; выход.
    /// Шапка показывает монеты, уровень, опыт и выбранную машину.
    /// Вешается на корень канваса сцены MainMenu.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Панели")]
        public GameObject mainPanel;
        public GameObject zonePanel;
        public GameObject modePanel;
        public SettingsUI settings;

        [Header("Кнопки")]
        public Button playButton;
        public Button garageButton;
        public Button settingsButton;
        public Button quitButton;
        public Button zoneBackButton;
        public Button modeBackButton;

        [Header("Списки")]
        public RectTransform zoneList;
        public RectTransform modeList;
        [Tooltip("Шаблон кнопки списка (Button с TMP_Text внутри), выключен.")]
        public Button listButtonTemplate;
        public TMP_Text modeHeader;

        [Header("Шапка")]
        public TMP_Text currencyText;
        public TMP_Text levelText;
        public Image xpFill;
        public TMP_Text vehicleText;

        private GameManager GM => GameManager.Instance;

        private void Awake()
        {
            GameManager.EnsureExists();
            if (listButtonTemplate != null) listButtonTemplate.gameObject.SetActive(false);

            if (playButton != null) playButton.onClick.AddListener(ShowZones);
            if (garageButton != null) garageButton.onClick.AddListener(() => GM.LoadGarage());
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (quitButton != null)
            {
                quitButton.onClick.AddListener(() => GM.QuitGame());
                quitButton.gameObject.SetActive(!Application.isMobilePlatform);
            }
            if (zoneBackButton != null) zoneBackButton.onClick.AddListener(ShowMain);
            if (modeBackButton != null) modeBackButton.onClick.AddListener(ShowZones);
            if (settings != null) settings.Closed += ShowMain;
        }

        private void OnEnable()
        {
            GM.Progress.Changed += RefreshHeader;
            RefreshHeader();
            ShowMain();
        }

        private void OnDisable()
        {
            if (GM != null) GM.Progress.Changed -= RefreshHeader;
        }

        private void RefreshHeader()
        {
            var p = GM.Progress;
            if (currencyText != null) currencyText.text = $"{p.Currency} монет";
            if (levelText != null) levelText.text = $"Уровень {p.Level}";
            if (xpFill != null) xpFill.fillAmount = p.XpNeededForNext > 0 ? (float)p.XpIntoLevel / p.XpNeededForNext : 1f;
            if (vehicleText != null)
            {
                var v = p.SelectedVehicle;
                vehicleText.text = v != null ? "Машина: " + v.displayName : "";
            }
        }

        private void ShowOnly(GameObject panel)
        {
            if (mainPanel != null) mainPanel.SetActive(panel == mainPanel);
            if (zonePanel != null) zonePanel.SetActive(panel == zonePanel);
            if (modePanel != null) modePanel.SetActive(panel == modePanel);
            if (settings != null && panel != null) settings.gameObject.SetActive(false);
        }

        public void ShowMain() => ShowOnly(mainPanel);

        private void OpenSettings()
        {
            ShowOnly(null);
            if (settings != null) settings.Open();
        }

        public void ShowZones()
        {
            ShowOnly(zonePanel);
            ClearList(zoneList);
            if (GM.zones == null) return;
            foreach (var zone in GM.zones)
            {
                if (zone == null) continue;
                bool unlocked = GM.IsZoneUnlocked(zone);
                string label = unlocked ? zone.displayName : $"{zone.displayName}   (с уровня {zone.unlockLevel})";
                ZoneInfo captured = zone;
                AddListButton(zoneList, label, unlocked, () => ShowModes(captured));
            }
        }

        private void ShowModes(ZoneInfo zone)
        {
            ShowOnly(modePanel);
            ClearList(modeList);
            if (modeHeader != null) modeHeader.text = zone.displayName;
            foreach (var mode in zone.modes)
            {
                string label = GameModeManager.ModeTitle(mode);
                var rec = GM.Progress.GetRecord(PlayerProgress.RecordKey(zone.sceneName, mode));
                if (rec != null && rec.bestTime > 0f) label += "   Рекорд " + VehicleHUD.FormatTime(rec.bestTime);
                GameMode captured = mode;
                AddListButton(modeList, label, true, () => GM.StartGame(zone.sceneName, captured));
            }
        }

        private void ClearList(RectTransform list)
        {
            if (list == null) return;
            for (int i = list.childCount - 1; i >= 0; i--)
            {
                Transform child = list.GetChild(i);
                if (listButtonTemplate != null && child == listButtonTemplate.transform) continue;
                Destroy(child.gameObject);
            }
        }

        private void AddListButton(RectTransform list, string label, bool interactable, UnityEngine.Events.UnityAction onClick)
        {
            if (list == null || listButtonTemplate == null) return;
            Button b = Instantiate(listButtonTemplate, list);
            b.gameObject.SetActive(true);
            b.interactable = interactable;
            var t = b.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = label;
            b.onClick.AddListener(onClick);
        }
    }
}
