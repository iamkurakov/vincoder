using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TerrainDrive
{
    /// <summary>
    /// Экранная кнопка, которую держат пальцем (газ, тормоз, руль, ручник).
    /// Поддерживает мультитач: каждая кнопка обрабатывает свой палец независимо.
    /// Если holdDuration > 0 — кнопка срабатывает только после удержания (защита от случайного рестарта).
    /// Вешается на UI-объект с Image (Raycast Target включён).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
    {
        [Tooltip("Отпускать кнопку, если палец ушёл за её пределы.")]
        public bool releaseOnExit = false;

        [Header("Удержание (для рестарта и т. п.)")]
        [Tooltip("0 — обычная кнопка. >0 — сработает после удержания столько секунд.")]
        public float holdDuration = 0f;
        public Image holdFill;
        public UnityEvent onHoldComplete = new UnityEvent();

        [Header("Отклик")]
        public float pressedScale = 0.92f;
        public Graphic targetGraphic;
        [Range(0f, 1f)] public float pressedAlphaBoost = 0.35f;

        public bool IsPressed => pointerCount > 0;
        public float Value => IsPressed ? 1f : 0f;

        private int pointerCount;
        private float holdTimer;
        private bool holdFired;
        private Vector3 baseScale;
        private float baseAlpha = 1f;

        private void Awake()
        {
            baseScale = transform.localScale;
            if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
            if (targetGraphic != null) baseAlpha = targetGraphic.color.a;
            if (holdFill != null) holdFill.fillAmount = 0f;
        }

        private void OnDisable()
        {
            pointerCount = 0;
            holdTimer = 0f;
            holdFired = false;
            ApplyVisual();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pointerCount++;
            ApplyVisual();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pointerCount = Mathf.Max(0, pointerCount - 1);
            if (pointerCount == 0)
            {
                holdTimer = 0f;
                holdFired = false;
            }
            ApplyVisual();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!releaseOnExit) return;
            pointerCount = 0;
            holdTimer = 0f;
            holdFired = false;
            ApplyVisual();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Нужен, чтобы EventSystem корректно отслеживал выход пальца.
        }

        private void Update()
        {
            if (holdDuration <= 0f) return;
            if (IsPressed && !holdFired)
            {
                holdTimer += Time.unscaledDeltaTime;
                if (holdTimer >= holdDuration)
                {
                    holdFired = true;
                    onHoldComplete.Invoke();
                }
            }
            if (holdFill != null) holdFill.fillAmount = IsPressed && !holdFired ? holdTimer / holdDuration : 0f;
        }

        private void ApplyVisual()
        {
            if (baseScale == Vector3.zero) baseScale = Vector3.one;
            transform.localScale = IsPressed ? baseScale * pressedScale : baseScale;
            if (targetGraphic != null)
            {
                Color c = targetGraphic.color;
                c.a = IsPressed ? Mathf.Clamp01(baseAlpha + pressedAlphaBoost) : baseAlpha;
                targetGraphic.color = c;
            }
        }

        /// <summary>Пересчитать базовые масштаб и прозрачность (после смены настроек).</summary>
        public void RefreshBase(float alpha)
        {
            baseScale = transform.localScale;
            baseAlpha = alpha;
            ApplyVisual();
        }
    }
}
