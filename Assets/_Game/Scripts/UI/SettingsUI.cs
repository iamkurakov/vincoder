using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainDrive
{
    /// <summary>
    /// Экран настроек. Строки создаются из шаблона: «Название — [Значение]», нажатие на значение
    /// переключает его по кругу. Набор строк зависит от устройства (на ПК — разрешение,
    /// на мобильных — руль, раскладка и размер кнопок). Используется в меню и в паузе.
    /// </summary>
    public class SettingsUI : MonoBehaviour
    {
        [Tooltip("Контейнер строк (с VerticalLayoutGroup).")]
        public RectTransform rowsContainer;
        [Tooltip("Шаблон строки: дочерние объекты Label (TMP_Text) и Value (Button с TMP_Text внутри).")]
        public GameObject rowTemplate;
        public Button closeButton;

        public event Action Closed;

        private class Row
        {
            public TMP_Text value;
            public Func<string> get;
        }

        private readonly List<Row> rows = new List<Row>();
        private bool built;
        private float resetConfirmUntil;

        private SettingsData S => GameManager.Instance.Progress.Settings;

        private void Awake()
        {
            GameManager.EnsureExists();
            if (rowTemplate != null) rowTemplate.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        public void Open()
        {
            gameObject.SetActive(true);
            if (!built) Build();
            RefreshAll();
        }

        public void Close()
        {
            GameManager.Instance.Progress.Save();
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void Build()
        {
            built = true;
            string[] auto4 = { "Авто", "Низкая", "Средняя", "Высокая", "Ультра" };

            AddRow("Качество графики", () =>
            {
                if (S.graphicsPreset < 0)
                    return "Авто (" + auto4[(int)GraphicsSettingsManager.AutoPreset() + 1] + ")";
                return auto4[S.graphicsPreset + 1];
            }, () => S.graphicsPreset = Cycle(S.graphicsPreset, -1, 3));

            AddChoice("Дальность прорисовки", () => S.drawDistance, v => S.drawDistance = v,
                new[] { -1, 0, 1, 2 }, new[] { "Авто", "Близко", "Средне", "Далеко" });
            AddChoice("Трава", () => S.grassDensity, v => S.grassDensity = v,
                new[] { -1, 0, 1, 2, 3 }, new[] { "Авто", "Нет", "Мало", "Средне", "Много" });
            AddChoice("Деревья", () => S.treeDensity, v => S.treeDensity = v,
                new[] { -1, 0, 1, 2 }, new[] { "Авто", "Близко", "Средне", "Далеко" });
            AddChoice("Тени", () => S.shadowQuality, v => S.shadowQuality = v,
                new[] { -1, 0, 1, 2 }, new[] { "Авто", "Выкл", "Низкие", "Высокие" });
            AddChoice("AI-машины", () => S.aiTrafficCount, v => S.aiTrafficCount = v,
                new[] { -1, 0, 4, 8, 14 }, new[] { "Авто", "Нет", "4", "8", "14" });
            AddChoice("Постобработка", () => S.postProcessing, v => S.postProcessing = v,
                new[] { -1, 0, 1 }, new[] { "Авто", "Выкл", "Вкл" });
            AddChoice("Лимит FPS", () => S.fpsLimit, v => S.fpsLimit = v,
                new[] { -1, 30, 60, 90, 120, 0 }, new[] { "Авто", "30", "60", "90", "120", "Без лимита" });

            if (!Application.isMobilePlatform)
            {
                AddRow("Разрешение", () =>
                {
                    Resolution[] list = Screen.resolutions;
                    if (S.resolutionIndex < 0 || S.resolutionIndex >= list.Length) return "Авто";
                    Resolution r = list[S.resolutionIndex];
                    return $"{r.width}×{r.height}";
                }, () => S.resolutionIndex = Cycle(S.resolutionIndex, -1, Screen.resolutions.Length - 1));
                AddRow("Полный экран", () => S.fullscreen ? "Да" : "Нет", () => S.fullscreen = !S.fullscreen);
            }

            AddStep("Общая громкость", () => S.masterVolume, v => S.masterVolume = v, 0f, 1f, 0.1f, true);
            AddStep("Музыка", () => S.musicVolume, v => S.musicVolume = v, 0f, 1f, 0.1f, true);
            AddStep("Звуки", () => S.sfxVolume, v => S.sfxVolume = v, 0f, 1f, 0.1f, true);

            bool mobileRows = PlatformManager.DetectedDevice != DeviceClass.Desktop;
#if UNITY_EDITOR
            mobileRows = true;
#endif
            if (mobileRows)
            {
                AddChoice("Руль", () => S.steeringMode, v => S.steeringMode = v,
                    new[] { 0, 1 }, new[] { "Кнопки", "Наклон" });
                AddStep("Чувствительность наклона", () => S.tiltSensitivity, v => S.tiltSensitivity = v, 0.5f, 2f, 0.25f, false);
                AddRow("Инвертировать наклон", () => S.invertTilt ? "Да" : "Нет", () => S.invertTilt = !S.invertTilt);
                AddChoice("Раскладка кнопок", () => S.layoutOverride, v => S.layoutOverride = v,
                    new[] { 0, 1, 2, 3 }, new[] { "Авто", "Телефон", "Планшет", "ПК (без кнопок)" });
                AddStep("Размер кнопок", () => S.buttonScale, v => S.buttonScale = v, 0.8f, 1.3f, 0.1f, true);
                AddStep("Прозрачность кнопок", () => S.buttonOpacity, v => S.buttonOpacity = v, 0.3f, 1f, 0.1f, true);
            }

            AddRow("Сбросить прогресс", () => Time.unscaledTime < resetConfirmUntil ? "Нажмите ещё раз" : "Сбросить", () =>
            {
                if (Time.unscaledTime < resetConfirmUntil)
                {
                    GameManager.Instance.Progress.ResetAll();
                    resetConfirmUntil = 0f;
                }
                else resetConfirmUntil = Time.unscaledTime + 4f;
            }, applyAfter: false);
        }

        // ---------- Конструкторы строк ----------

        private void AddRow(string label, Func<string> get, Action onClick, bool applyAfter = true)
        {
            if (rowTemplate == null || rowsContainer == null) return;
            GameObject go = Instantiate(rowTemplate, rowsContainer);
            go.SetActive(true);
            go.name = "Row_" + label;

            Transform labelT = go.transform.Find("Label");
            Transform valueT = go.transform.Find("Value");
            if (labelT != null)
            {
                var lt = labelT.GetComponent<TMP_Text>();
                if (lt != null) lt.text = label;
            }
            var row = new Row { get = get };
            if (valueT != null)
            {
                row.value = valueT.GetComponentInChildren<TMP_Text>();
                var btn = valueT.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() =>
                    {
                        onClick();
                        if (applyAfter) ApplySettings();
                        RefreshAll();
                    });
                }
            }
            rows.Add(row);
        }

        private void AddChoice(string label, Func<int> get, Action<int> set, int[] values, string[] names)
        {
            AddRow(label, () =>
            {
                int i = Array.IndexOf(values, get());
                return i >= 0 ? names[i] : names[0];
            }, () =>
            {
                int i = Array.IndexOf(values, get());
                set(values[(i + 1) % values.Length]);
            });
        }

        private void AddStep(string label, Func<float> get, Action<float> set, float min, float max, float step, bool percent)
        {
            AddRow(label, () => percent ? Mathf.RoundToInt(get() * 100f) + "%" : get().ToString("0.00"), () =>
            {
                float v = get() + step;
                if (v > max + 0.001f) v = min;
                set(Mathf.Round(v * 100f) / 100f);
            });
        }

        private static int Cycle(int value, int min, int max) => value >= max ? min : value + 1;

        private void RefreshAll()
        {
            foreach (var r in rows)
                if (r.value != null) r.value.text = r.get();
        }

        private void ApplySettings()
        {
            var gm = GameManager.Instance;
            PlatformManager.ApplyLayoutOverride(S.layoutOverride);
            if (GraphicsSettingsManager.Instance != null) GraphicsSettingsManager.Instance.ApplyAll();
            gm.ApplyAudioSettings();
            foreach (var m in FindObjectsByType<MobileInputUI>(FindObjectsSortMode.None)) m.ApplyLayout();
        }
    }
}
