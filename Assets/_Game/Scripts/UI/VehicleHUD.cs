using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainDrive
{
    /// <summary>
    /// Игровой интерфейс: спидометр, тахометр, передача, повреждения, таймер, чекпоинты,
    /// компас со стрелкой на цель, текст задания, всплывающие сообщения и обратный отсчёт.
    /// Вешается на корень HUD-канваса.
    /// </summary>
    public class VehicleHUD : MonoBehaviour
    {
        public static VehicleHUD Instance { get; private set; }

        [Header("Машина (назначается из GameModeManager)")]
        public VehicleController vehicle;

        [Header("Приборы")]
        public TMP_Text speedText;
        public TMP_Text gearText;
        public Image rpmFill;
        public Image damageFill;
        public TMP_Text surfaceText;

        [Header("Задание")]
        public GameObject timerGroup;
        public TMP_Text timerText;
        public GameObject checkpointGroup;
        public TMP_Text checkpointText;
        public TMP_Text objectiveText;
        public TMP_Text distanceText;

        [Header("Компас")]
        public RectTransform compassNeedle;
        public TMP_Text compassLabel;

        [Header("Сообщения")]
        public CanvasGroup messageGroup;
        public TMP_Text messageText;
        public TMP_Text countdownText;

        [Header("Безопасная зона")]
        public RectTransform safeAreaRoot;

        [Header("Цвета")]
        public Color healthyColor = new Color(0.3f, 0.85f, 0.4f);
        public Color damagedColor = new Color(0.95f, 0.25f, 0.2f);
        public Color timerWarningColor = new Color(1f, 0.35f, 0.25f);

        private float speedRefresh;
        private float messageTimer;
        private Transform compassTarget;
        private Vector3? compassPoint;
        private Color timerBaseColor = Color.white;

        private void Awake()
        {
            Instance = this;
            if (timerText != null) timerBaseColor = timerText.color;
            if (messageGroup != null) messageGroup.alpha = 0f;
            if (countdownText != null) countdownText.gameObject.SetActive(false);
            SetTimerVisible(false);
            SetCheckpointsVisible(false);
            SetDistance(-1f);
            ApplySafeArea();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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

        public void Bind(VehicleController v)
        {
            vehicle = v;
        }

        // ---------- Методы для режимов игры ----------

        public void SetTimerVisible(bool visible)
        {
            if (timerGroup != null) timerGroup.SetActive(visible);
        }

        /// <summary>Показать время. warning — красный цвет (мало времени).</summary>
        public void SetTimer(float seconds, bool warning = false)
        {
            if (timerText == null) return;
            timerText.text = FormatTime(seconds);
            timerText.color = warning ? timerWarningColor : timerBaseColor;
        }

        public void SetCheckpointsVisible(bool visible)
        {
            if (checkpointGroup != null) checkpointGroup.SetActive(visible);
        }

        public void SetCheckpoints(int passed, int total)
        {
            if (checkpointText != null) checkpointText.text = $"Чекпоинты {passed} / {total}";
        }

        public void SetObjective(string text)
        {
            if (objectiveText == null) return;
            objectiveText.text = text;
            objectiveText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>Дистанция в км; отрицательное значение прячет поле.</summary>
        public void SetDistance(float km, float targetKm = 0f)
        {
            if (distanceText == null) return;
            bool show = km >= 0f;
            distanceText.gameObject.SetActive(show);
            if (!show) return;
            distanceText.text = targetKm > 0f ? $"{km:0.00} / {targetKm:0.0} км" : $"{km:0.00} км";
        }

        public void SetCompassTarget(Transform target)
        {
            compassTarget = target;
            compassPoint = null;
        }

        public void SetCompassPoint(Vector3? point)
        {
            compassPoint = point;
            compassTarget = null;
        }

        public void ShowMessage(string text, float duration = 2f)
        {
            if (messageText == null || messageGroup == null) return;
            messageText.text = text;
            messageGroup.alpha = 1f;
            messageTimer = duration;
        }

        /// <summary>Обратный отсчёт «3, 2, 1, Старт!». Пустая строка прячет его.</summary>
        public void ShowCountdown(string text)
        {
            if (countdownText == null) return;
            bool show = !string.IsNullOrEmpty(text);
            countdownText.gameObject.SetActive(show);
            if (show) countdownText.text = text;
        }

        public static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m:00}:{s:00.00}";
        }

        // ---------- Обновление ----------

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (messageGroup != null && messageTimer > 0f)
            {
                messageTimer -= dt;
                if (messageTimer < 0.3f) messageGroup.alpha = Mathf.Clamp01(messageTimer / 0.3f);
            }

            if (vehicle == null) return;

            speedRefresh -= dt;
            if (speedRefresh <= 0f)
            {
                speedRefresh = 0.1f; // 10 раз в секунду — цифры не мельтешат
                if (speedText != null) speedText.text = Mathf.RoundToInt(vehicle.SpeedKmh).ToString();
                if (gearText != null) gearText.text = vehicle.GearLabel;
                if (surfaceText != null)
                {
                    SurfaceData s = vehicle.CurrentSurface;
                    surfaceText.text = s != null ? s.displayName : "";
                }
            }

            if (rpmFill != null) rpmFill.fillAmount = Mathf.Lerp(rpmFill.fillAmount, vehicle.Rpm01, 1f - Mathf.Exp(-15f * dt));

            if (damageFill != null)
            {
                float h = vehicle.Health01;
                damageFill.fillAmount = h;
                damageFill.color = Color.Lerp(damagedColor, healthyColor, h);
            }

            UpdateCompass();
        }

        private void UpdateCompass()
        {
            if (compassNeedle == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.001f) return;

            Vector3? targetPos = compassTarget != null ? compassTarget.position : compassPoint;
            float angle;
            if (targetPos.HasValue)
            {
                Vector3 to = Vector3.ProjectOnPlane(targetPos.Value - vehicle.transform.position, Vector3.up);
                angle = Vector3.SignedAngle(fwd, to, Vector3.up);
                if (compassLabel != null) compassLabel.text = $"{Mathf.RoundToInt(to.magnitude)} м";
            }
            else
            {
                // Без цели — обычный компас: стрелка на север (+Z мира).
                angle = Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up);
                if (compassLabel != null) compassLabel.text = "С";
            }
            compassNeedle.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }
    }
}
