using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainDrive
{
    /// <summary>
    /// Единая точка ввода для машины. Собирает команды из клавиатуры, геймпада,
    /// экранных кнопок (MobileInputUI) и наклона устройства и отдаёт машине готовые значения:
    /// Throttle, Brake, Steer (-1..1), Handbrake.
    /// Разовые команды (камера, пауза, рестарт, «на колёса») рассылаются статическими событиями,
    /// чтобы на них могли подписаться камера, режим игры и интерфейс.
    /// Вешается на корень машины.
    /// </summary>
    public class VehicleInput : MonoBehaviour
    {
        // ---- Значения от экранных кнопок (пишет MobileInputUI) ----
        public static float MobileThrottle;
        public static float MobileBrake;
        public static float MobileSteer;
        public static bool MobileHandbrake;

        // ---- Разовые команды ----
        public static event Action ResetRequested;
        public static event Action CameraSwitchRequested;
        public static event Action PauseRequested;
        public static event Action RestartRequested;

        public static void TriggerReset() => ResetRequested?.Invoke();
        public static void TriggerCameraSwitch() => CameraSwitchRequested?.Invoke();
        public static void TriggerPause() => PauseRequested?.Invoke();
        public static void TriggerRestart() => RestartRequested?.Invoke();

        [Header("Наклон устройства")]
        [Tooltip("Насколько сильно наклон поворачивает руль.")]
        public float tiltGain = 2.4f;
        [Tooltip("Мёртвая зона наклона (не реагировать на мелкую дрожь рук).")]
        public float tiltDeadZone = 0.05f;

        /// <summary>Газ 0..1.</summary>
        public float Throttle { get; private set; }
        /// <summary>Тормоз / задний ход 0..1.</summary>
        public float Brake { get; private set; }
        /// <summary>Руль -1 (влево) .. 1 (вправо).</summary>
        public float Steer { get; private set; }
        public bool Handbrake { get; private set; }
        /// <summary>Поворот камеры (мышь/Q-E/правый стик), -1..1.</summary>
        public float LookYaw { get; private set; }
        /// <summary>Последнее устройство — сенсорный экран.</summary>
        public static bool LastInputWasTouch { get; private set; }

        private InputAction throttleAction;
        private InputAction brakeAction;
        private InputAction steerAction;
        private InputAction handbrakeAction;
        private InputAction lookAction;
        private InputAction mouseLookAction;
        private InputAction mouseLookHoldAction;
        private InputAction resetAction;
        private InputAction cameraAction;
        private InputAction pauseAction;
        private InputAction restartAction;

        private bool tiltEnabled;

        private void Awake()
        {
            throttleAction = new InputAction("Throttle", InputActionType.Value);
            throttleAction.AddBinding("<Keyboard>/w");
            throttleAction.AddBinding("<Keyboard>/upArrow");
            throttleAction.AddBinding("<Gamepad>/rightTrigger");

            brakeAction = new InputAction("Brake", InputActionType.Value);
            brakeAction.AddBinding("<Keyboard>/s");
            brakeAction.AddBinding("<Keyboard>/downArrow");
            brakeAction.AddBinding("<Gamepad>/leftTrigger");

            steerAction = new InputAction("Steer", InputActionType.Value);
            steerAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            steerAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            steerAction.AddBinding("<Gamepad>/leftStick/x");

            handbrakeAction = new InputAction("Handbrake", InputActionType.Button);
            handbrakeAction.AddBinding("<Keyboard>/space");
            handbrakeAction.AddBinding("<Gamepad>/buttonSouth");

            lookAction = new InputAction("Look", InputActionType.Value);
            lookAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q")
                .With("Positive", "<Keyboard>/e");
            lookAction.AddBinding("<Gamepad>/rightStick/x");

            mouseLookAction = new InputAction("MouseLook", InputActionType.Value, "<Mouse>/delta/x");
            mouseLookHoldAction = new InputAction("MouseLookHold", InputActionType.Button, "<Mouse>/rightButton");

            resetAction = new InputAction("Reset", InputActionType.Button);
            resetAction.AddBinding("<Keyboard>/r");
            resetAction.AddBinding("<Gamepad>/buttonWest");

            cameraAction = new InputAction("Camera", InputActionType.Button);
            cameraAction.AddBinding("<Keyboard>/c");
            cameraAction.AddBinding("<Gamepad>/buttonNorth");

            pauseAction = new InputAction("Pause", InputActionType.Button);
            pauseAction.AddBinding("<Keyboard>/escape");
            pauseAction.AddBinding("<Keyboard>/p");
            pauseAction.AddBinding("<Gamepad>/start");

            restartAction = new InputAction("Restart", InputActionType.Button);
            restartAction.AddBinding("<Keyboard>/backspace");
            restartAction.AddBinding("<Gamepad>/select");
        }

        private void OnEnable()
        {
            SetEnabled(true);
            MobileThrottle = MobileBrake = MobileSteer = 0f;
            MobileHandbrake = false;
        }

        private void OnDisable() => SetEnabled(false);

        private void OnDestroy()
        {
            throttleAction?.Dispose();
            brakeAction?.Dispose();
            steerAction?.Dispose();
            handbrakeAction?.Dispose();
            lookAction?.Dispose();
            mouseLookAction?.Dispose();
            mouseLookHoldAction?.Dispose();
            resetAction?.Dispose();
            cameraAction?.Dispose();
            pauseAction?.Dispose();
            restartAction?.Dispose();
        }

        private void SetEnabled(bool on)
        {
            InputAction[] all =
            {
                throttleAction, brakeAction, steerAction, handbrakeAction, lookAction, mouseLookAction,
                mouseLookHoldAction, resetAction, cameraAction, pauseAction, restartAction
            };
            foreach (var a in all)
            {
                if (a == null) continue;
                if (on) a.Enable();
                else a.Disable();
            }
        }

        private void Update()
        {
            SettingsData settings = GameManager.Instance != null ? GameManager.Instance.Progress.Settings : null;

            float kThrottle = throttleAction.ReadValue<float>();
            float kBrake = brakeAction.ReadValue<float>();
            float kSteer = steerAction.ReadValue<float>();
            bool kHandbrake = handbrakeAction.IsPressed();

            Throttle = Mathf.Clamp01(Mathf.Max(kThrottle, MobileThrottle));
            Brake = Mathf.Clamp01(Mathf.Max(kBrake, MobileBrake));
            Handbrake = kHandbrake || MobileHandbrake;

            float steer = kSteer;
            if (Mathf.Abs(steer) < 0.01f) steer = MobileSteer;

            // Наклон устройства (если выбран в настройках).
            bool wantTilt = settings != null && settings.steeringMode == 1 && PlatformManager.IsMobile;
            if (wantTilt != tiltEnabled) SetTilt(wantTilt);
            if (tiltEnabled && Mathf.Abs(kSteer) < 0.01f && Accelerometer.current != null)
            {
                Vector3 acc = Accelerometer.current.acceleration.ReadValue();
                // В альбомной ориентации поворот «как руль» меняет ось Y устройства.
                float raw = Screen.orientation == ScreenOrientation.LandscapeRight ? acc.y : -acc.y;
                if (settings.invertTilt) raw = -raw;
                raw = Mathf.Abs(raw) < tiltDeadZone ? 0f : raw;
                steer = Mathf.Clamp(raw * tiltGain * settings.tiltSensitivity, -1f, 1f);
            }
            Steer = Mathf.Clamp(steer, -1f, 1f);

            // Взгляд камеры.
            float look = lookAction.ReadValue<float>();
            if (mouseLookHoldAction.IsPressed())
                look += Mathf.Clamp(mouseLookAction.ReadValue<float>() * 0.05f, -1f, 1f);
            LookYaw = Mathf.Clamp(look, -1f, 1f);

            // Разовые команды.
            if (resetAction.WasPressedThisFrame()) TriggerReset();
            if (cameraAction.WasPressedThisFrame()) TriggerCameraSwitch();
            if (pauseAction.WasPressedThisFrame()) TriggerPause();
            if (restartAction.WasPressedThisFrame()) TriggerRestart();

            // Какое устройство использовалось последним (для показа/скрытия экранных кнопок).
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                LastInputWasTouch = true;
            else if (Mathf.Abs(kThrottle) + Mathf.Abs(kBrake) + Mathf.Abs(kSteer) > 0.01f)
                LastInputWasTouch = false;
        }

        private void SetTilt(bool on)
        {
            tiltEnabled = on;
            if (Accelerometer.current == null) return;
            if (on) InputSystem.EnableDevice(Accelerometer.current);
            else InputSystem.DisableDevice(Accelerometer.current);
        }
    }
}
