using System;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>Класс устройства: от него зависят управление, интерфейс и графика.</summary>
    public enum DeviceClass
    {
        Desktop,
        Tablet,
        Phone
    }

    /// <summary>
    /// Определяет, на чём запущена игра: ПК/ноутбук, планшет или смартфон.
    /// Планшет от телефона отличается по диагонали экрана (по умолчанию от 7 дюймов).
    /// Где лежит: префаб Resources/GameSystems.
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    public class PlatformManager : MonoBehaviour
    {
        public static PlatformManager Instance { get; private set; }

        [Tooltip("Диагональ, начиная с которой мобильное устройство считается планшетом.")]
        public float tabletMinDiagonalInches = 7f;

        [Header("Отладка в редакторе")]
        [Tooltip("Имитировать мобильное устройство в редакторе (чтобы видеть экранные кнопки).")]
        public bool simulateInEditor;
        public DeviceClass editorSimulatedDevice = DeviceClass.Phone;

        /// <summary>Вызывается, когда класс устройства меняется (например, из настроек).</summary>
        public static event Action<DeviceClass> DeviceChanged;

        private static bool detected;
        private static DeviceClass device;
        private static DeviceClass detectedDevice;
        private static float diagonalInches;

        public static DeviceClass Device
        {
            get
            {
                if (!detected) Detect();
                return device;
            }
        }

        public static bool IsMobile => Device != DeviceClass.Desktop;
        public static bool IsTablet => Device == DeviceClass.Tablet;
        public static bool IsPhone => Device == DeviceClass.Phone;
        public static float DiagonalInches
        {
            get
            {
                if (!detected) Detect();
                return diagonalInches;
            }
        }

        /// <summary>Устройство, определённое автоматически (без учёта ручной настройки).</summary>
        public static DeviceClass DetectedDevice
        {
            get
            {
                if (!detected) Detect();
                return detectedDevice;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            detected = false;
            Detect();
        }

        private static void Detect()
        {
            detected = true;
            float tabletThreshold = Instance != null ? Instance.tabletMinDiagonalInches : 7f;

            float dpi = Screen.dpi > 1f ? Screen.dpi : 160f;
            float w = Screen.width / dpi;
            float h = Screen.height / dpi;
            diagonalInches = Mathf.Sqrt(w * w + h * h);

            if (Application.isMobilePlatform)
            {
                detectedDevice = diagonalInches >= tabletThreshold ? DeviceClass.Tablet : DeviceClass.Phone;
#if UNITY_IOS
                if (SystemInfo.deviceModel.StartsWith("iPad")) detectedDevice = DeviceClass.Tablet;
#endif
            }
            else
            {
                detectedDevice = DeviceClass.Desktop;
            }

#if UNITY_EDITOR
            if (Instance != null && Instance.simulateInEditor)
                detectedDevice = Instance.editorSimulatedDevice;
#endif
            device = detectedDevice;
        }

        /// <summary>
        /// Ручной выбор раскладки из настроек:
        /// 0 — авто, 1 — телефон, 2 — планшет, 3 — ПК (без экранных кнопок).
        /// </summary>
        public static void ApplyLayoutOverride(int layoutOverride)
        {
            if (!detected) Detect();
            DeviceClass newDevice;
            switch (layoutOverride)
            {
                case 1: newDevice = DeviceClass.Phone; break;
                case 2: newDevice = DeviceClass.Tablet; break;
                case 3: newDevice = DeviceClass.Desktop; break;
                default: newDevice = detectedDevice; break;
            }
            if (newDevice == device) return;
            device = newDevice;
            DeviceChanged?.Invoke(device);
        }

        /// <summary>Безопасная зона экрана (без выреза камеры и скруглённых углов) в долях экрана.</summary>
        public static Rect SafeAreaNormalized
        {
            get
            {
                Rect sa = Screen.safeArea;
                float sw = Mathf.Max(1, Screen.width);
                float sh = Mathf.Max(1, Screen.height);
                return new Rect(sa.x / sw, sa.y / sh, sa.width / sw, sa.height / sh);
            }
        }
    }
}
