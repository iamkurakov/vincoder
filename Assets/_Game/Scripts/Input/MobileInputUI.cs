using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TerrainDrive
{
    /// <summary>
    /// Экранные кнопки для смартфонов и планшетов. Показывается только на мобильных устройствах
    /// (или при ручном выборе раскладки), прячется, если подключили геймпад.
    /// Раскладка «Планшет» — кнопки крупнее и выше (под большие пальцы при хвате двумя руками).
    /// Вешается на панель MobileControls внутри HUD-канваса.
    /// </summary>
    public class MobileInputUI : MonoBehaviour
    {
        [Header("Корни")]
        public CanvasGroup controlsGroup;
        [Tooltip("Растягивается по безопасной зоне экрана (без выреза камеры).")]
        public RectTransform safeAreaRoot;
        public RectTransform leftCluster;
        public RectTransform rightCluster;
        [Tooltip("Кнопки ◀ ▶ (прячутся при управлении наклоном).")]
        public GameObject steerButtonsRoot;

        [Header("Педали и руль")]
        public HoldButton gasButton;
        public HoldButton brakeButton;
        public HoldButton leftButton;
        public HoldButton rightButton;
        public HoldButton handbrakeButton;

        [Header("Сервисные кнопки")]
        public Button cameraButton;
        public Button resetButton;
        public Button pauseButton;
        [Tooltip("Рестарт по удержанию 1 с, чтобы не нажать случайно.")]
        public HoldButton restartHoldButton;

        [Header("Раскладки")]
        public float phoneScale = 1f;
        public float tabletScale = 1.12f;
        [Tooltip("На сколько поднять кластеры кнопок на планшете (в единицах эталонного экрана 1920×1080).")]
        public float tabletRaise = 90f;

        [Header("Редактор")]
        [Tooltip("Показывать кнопки в редакторе на ПК (для отладки мышью).")]
        public bool showOnDesktopInEditor = false;

        private Vector2 leftBasePos;
        private Vector2 rightBasePos;
        private bool basesStored;
        private bool gamepadMode;
        private bool visible;

        private void Awake()
        {
            StoreBases();
            if (cameraButton != null) cameraButton.onClick.AddListener(VehicleInput.TriggerCameraSwitch);
            if (resetButton != null) resetButton.onClick.AddListener(VehicleInput.TriggerReset);
            if (pauseButton != null) pauseButton.onClick.AddListener(VehicleInput.TriggerPause);
            if (restartHoldButton != null) restartHoldButton.onHoldComplete.AddListener(VehicleInput.TriggerRestart);
        }

        private void StoreBases()
        {
            if (basesStored) return;
            if (leftCluster != null) leftBasePos = leftCluster.anchoredPosition;
            if (rightCluster != null) rightBasePos = rightCluster.anchoredPosition;
            basesStored = true;
        }

        private void OnEnable()
        {
            PlatformManager.DeviceChanged += OnDeviceChanged;
            ApplyLayout();
            visible = !ShouldShow(); // заставит Update применить видимость в первом кадре
        }

        private void OnDisable()
        {
            PlatformManager.DeviceChanged -= OnDeviceChanged;
            ClearValues();
        }

        private void OnDeviceChanged(DeviceClass device) => ApplyLayout();

        private static void ClearValues()
        {
            VehicleInput.MobileThrottle = 0f;
            VehicleInput.MobileBrake = 0f;
            VehicleInput.MobileSteer = 0f;
            VehicleInput.MobileHandbrake = false;
        }

        /// <summary>Применить раскладку, размер и прозрачность кнопок (вызывать после смены настроек).</summary>
        public void ApplyLayout()
        {
            StoreBases();
            SettingsData s = GameManager.Instance != null ? GameManager.Instance.Progress.Settings : new SettingsData();
            bool tablet = PlatformManager.Device == DeviceClass.Tablet;
            float scale = (tablet ? tabletScale : phoneScale) * Mathf.Clamp(s.buttonScale, 0.7f, 1.4f);
            float raise = tablet ? tabletRaise : 0f;

            if (leftCluster != null)
            {
                leftCluster.localScale = Vector3.one * scale;
                leftCluster.anchoredPosition = leftBasePos + new Vector2(0f, raise);
            }
            if (rightCluster != null)
            {
                rightCluster.localScale = Vector3.one * scale;
                rightCluster.anchoredPosition = rightBasePos + new Vector2(0f, raise);
            }
            if (steerButtonsRoot != null) steerButtonsRoot.SetActive(s.steeringMode == 0);

            float alpha = Mathf.Clamp(s.buttonOpacity, 0.2f, 1f);
            HoldButton[] holds = { gasButton, brakeButton, leftButton, rightButton, handbrakeButton };
            foreach (var h in holds)
            {
                if (h == null || h.targetGraphic == null) continue;
                Color c = h.targetGraphic.color;
                c.a = alpha;
                h.targetGraphic.color = c;
                h.RefreshBase(alpha);
            }

            ApplySafeArea();
        }

        private void ApplySafeArea()
        {
            if (safeAreaRoot == null) return;
            Rect r = PlatformManager.SafeAreaNormalized;
            safeAreaRoot.anchorMin = r.min;
            safeAreaRoot.anchorMax = r.max;
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;
        }

        private bool ShouldShow()
        {
#if UNITY_EDITOR
            if (showOnDesktopInEditor) return !gamepadMode;
#endif
            return PlatformManager.IsMobile && !gamepadMode;
        }

        private void Update()
        {
            DetectGamepad();

            bool show = ShouldShow();
            if (show != visible)
            {
                visible = show;
                if (controlsGroup != null)
                {
                    controlsGroup.alpha = show ? 1f : 0f;
                    controlsGroup.interactable = show;
                    controlsGroup.blocksRaycasts = show;
                }
                if (!show) ClearValues();
            }

            if (!visible) return;

            VehicleInput.MobileThrottle = gasButton != null ? gasButton.Value : 0f;
            VehicleInput.MobileBrake = brakeButton != null ? brakeButton.Value : 0f;
            float steer = 0f;
            if (rightButton != null && rightButton.IsPressed) steer += 1f;
            if (leftButton != null && leftButton.IsPressed) steer -= 1f;
            VehicleInput.MobileSteer = steer;
            VehicleInput.MobileHandbrake = handbrakeButton != null && handbrakeButton.IsPressed;
        }

        private void DetectGamepad()
        {
            var pad = Gamepad.current;
            if (pad != null)
            {
                bool padUsed = pad.leftStick.ReadValue().sqrMagnitude > 0.04f
                               || pad.rightTrigger.ReadValue() > 0.1f
                               || pad.leftTrigger.ReadValue() > 0.1f;
                if (padUsed) gamepadMode = true;
            }
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                gamepadMode = false;
        }
    }
}
