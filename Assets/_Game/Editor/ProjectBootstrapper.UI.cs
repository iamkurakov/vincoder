#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainDrive.EditorTools
{
    /// <summary>Часть автосборки: создание интерфейсов.</summary>
    public static partial class ProjectBootstrapper
    {
        private static readonly Color PanelColor = new Color(0.06f, 0.09f, 0.11f, 0.92f);
        private static readonly Color AccentColor = new Color(0.03f, 0.31f, 0.38f, 1f); // бирюзовый VinCoder
        private static readonly Color ButtonColor = new Color(0.12f, 0.16f, 0.19f, 1f);
        private static readonly Color TextColor = new Color(0.95f, 0.95f, 0.93f, 1f);
        private static readonly Color MutedText = new Color(0.7f, 0.74f, 0.76f, 1f);

        // ---------- Базовые элементы ----------

        private static GameObject UI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            return go;
        }

        private static RectTransform Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Anchor(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size) =>
            Place(go, anchor, anchor, anchor, pos, size);

        private static RectTransform Stretch(GameObject go, float inset = 0f)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        private static Image Img(GameObject go, Color color, Sprite sprite = null)
        {
            var img = go.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : whiteSprite;
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI Txt(Transform parent, string name, string text, float size,
            TextAlignmentOptions align, Color color, FontStyles style = FontStyles.Normal)
        {
            GameObject go = UI(name, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static Button Btn(Transform parent, string name, string label, Vector2 size, Color color, float fontSize = 34f)
        {
            GameObject go = UI(name, parent);
            ((RectTransform)go.transform).sizeDelta = size;
            Image img = Img(go, color);
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            b.colors = cb;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = size.y;
            le.minHeight = size.y;
            if (size.x > 0f) le.preferredWidth = size.x;
            TextMeshProUGUI t = Txt(go.transform, "Label", label, fontSize, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Stretch(t.gameObject, 8f);
            return b;
        }

        private static TMP_Text LabelOf(Button b) => b.GetComponentInChildren<TMP_Text>();

        private static Canvas NewCanvas(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static RectTransform VList(GameObject go, float spacing, int padding = 0, bool fitHeight = false)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            if (fitHeight)
            {
                var f = go.AddComponent<ContentSizeFitter>();
                f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            return (RectTransform)go.transform;
        }

        private static Image Bar(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color fillColor)
        {
            GameObject bg = UI(name, parent);
            Anchor(bg, anchor, pos, size);
            Img(bg, new Color(1f, 1f, 1f, 0.15f));
            GameObject fill = UI("Fill", bg.transform);
            Stretch(fill, 3f);
            Image f = Img(fill, fillColor);
            f.type = Image.Type.Filled;
            f.fillMethod = Image.FillMethod.Horizontal;
            f.fillOrigin = 0;
            f.fillAmount = 1f;
            return f;
        }

        private static GameObject Panel(Transform parent, string name, Vector2 size, Color color)
        {
            GameObject p = UI(name, parent);
            Anchor(p, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Img(p, color);
            return p;
        }

        // ---------- Экран загрузки (в префабе GameSystems) ----------

        private static void BuildLoadingOverlay(Transform parent, GameManager gm)
        {
            Canvas c = NewCanvas("LoadingCanvas", 1000, parent);
            var group = c.gameObject.AddComponent<CanvasGroup>();
            GameObject bg = UI("Background", c.transform);
            Stretch(bg);
            Img(bg, new Color(0.03f, 0.2f, 0.25f, 1f));
            TextMeshProUGUI title = Txt(c.transform, "Title", "TERRAIN DRIVE", 96, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Anchor(title.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(1400f, 140f));
            TextMeshProUGUI sub = Txt(c.transform, "Sub", "Загрузка…", 40, TextAlignmentOptions.Center, MutedText);
            Anchor(sub.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1000f, 60f));
            Image fill = Bar(c.transform, "Progress", new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(900f, 22f), TextColor);
            gm.loadingOverlay = group;
            gm.loadingBar = fill;
            c.gameObject.SetActive(false);
        }

        // ---------- Окно настроек (меню и пауза) ----------

        private static SettingsUI BuildSettingsPanel(Transform parent)
        {
            GameObject panel = Panel(parent, "Settings", new Vector2(1200f, 920f), PanelColor);
            var settings = panel.AddComponent<SettingsUI>();

            TextMeshProUGUI title = Txt(panel.transform, "Title", "Настройки", 54, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Place(title.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(0f, 80f));

            // Прокручиваемый список
            GameObject scroll = UI("Scroll", panel.transform);
            Place(scroll, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), Vector2.zero);
            var srt = (RectTransform)scroll.transform;
            srt.offsetMin = new Vector2(40f, 130f);
            srt.offsetMax = new Vector2(-40f, -110f);
            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;

            GameObject viewport = UI("Viewport", scroll.transform);
            Stretch(viewport);
            Img(viewport, new Color(0f, 0f, 0f, 0.01f));
            viewport.AddComponent<RectMask2D>();

            GameObject content = UI("Content", viewport.transform);
            Place(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 0f));
            VList(content, 10f, 0, true);
            sr.viewport = (RectTransform)viewport.transform;
            sr.content = (RectTransform)content.transform;

            // Шаблон строки
            GameObject row = UI("RowTemplate", content.transform);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 74f;
            le.minHeight = 74f;
            Img(row, new Color(1f, 1f, 1f, 0.04f));
            TextMeshProUGUI label = Txt(row.transform, "Label", "Параметр", 32, TextAlignmentOptions.MidlineLeft, TextColor);
            Place(label.gameObject, new Vector2(0f, 0f), new Vector2(0.55f, 1f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(-20f, 0f));
            GameObject value = UI("Value", row.transform);
            Place(value, new Vector2(0.55f, 0.1f), new Vector2(1f, 0.9f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(-10f, 0f));
            Image vImg = Img(value, AccentColor);
            var vBtn = value.AddComponent<Button>();
            vBtn.targetGraphic = vImg;
            TextMeshProUGUI vText = Txt(value.transform, "Text", "Значение", 30, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Stretch(vText.gameObject, 6f);

            Button close = Btn(panel.transform, "Close", "Готово", new Vector2(420f, 90f), AccentColor, 36);
            Anchor(close.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 65f), new Vector2(420f, 90f));

            settings.rowsContainer = (RectTransform)content.transform;
            settings.rowTemplate = row;
            settings.closeButton = close;
            panel.SetActive(false);
            return settings;
        }

        // ---------- HUD игровой сцены ----------

        private class HudRefs
        {
            public VehicleHUD hud;
            public PauseMenuUI pause;
        }

        private static HudRefs BuildGameplayHud()
        {
            Canvas canvas = NewCanvas("HUD", 10);
            var hud = canvas.gameObject.AddComponent<VehicleHUD>();

            GameObject safe = UI("SafeArea", canvas.transform);
            Stretch(safe);
            hud.safeAreaRoot = (RectTransform)safe.transform;
            Transform s = safe.transform;

            // Слева сверху: задание, чекпоинты, дистанция
            TextMeshProUGUI objective = Txt(s, "Objective", "Задание", 30, TextAlignmentOptions.TopLeft, TextColor);
            Anchor(objective.gameObject, new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(760f, 90f));
            hud.objectiveText = objective;

            GameObject cpGroup = UI("Checkpoints", s);
            Anchor(cpGroup, new Vector2(0f, 1f), new Vector2(40f, -125f), new Vector2(420f, 60f));
            Img(cpGroup, new Color(0f, 0f, 0f, 0.45f));
            TextMeshProUGUI cpText = Txt(cpGroup.transform, "Text", "Чекпоинты 0 / 0", 34, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Stretch(cpText.gameObject);
            hud.checkpointGroup = cpGroup;
            hud.checkpointText = cpText;

            TextMeshProUGUI dist = Txt(s, "Distance", "0.00 км", 34, TextAlignmentOptions.Left, TextColor, FontStyles.Bold);
            Anchor(dist.gameObject, new Vector2(0f, 1f), new Vector2(40f, -195f), new Vector2(420f, 50f));
            hud.distanceText = dist;

            // Сверху по центру: таймер и компас
            GameObject timerGroup = UI("Timer", s);
            Anchor(timerGroup, new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(320f, 80f));
            Img(timerGroup, new Color(0f, 0f, 0f, 0.45f));
            TextMeshProUGUI timer = Txt(timerGroup.transform, "Text", "00:00.00", 54, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Stretch(timer.gameObject);
            hud.timerGroup = timerGroup;
            hud.timerText = timer;

            GameObject compass = UI("Compass", s);
            Anchor(compass, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(110f, 110f));
            Img(compass, new Color(0f, 0f, 0f, 0.45f), roundSprite);
            GameObject needle = UI("Needle", compass.transform);
            Place(needle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(12f, 46f));
            Img(needle, new Color(0.2f, 0.9f, 1f));
            TextMeshProUGUI compassLabel = Txt(compass.transform, "Label", "", 22, TextAlignmentOptions.Center, TextColor);
            Place(compassLabel.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(200f, 34f));
            hud.compassNeedle = (RectTransform)needle.transform;
            hud.compassLabel = compassLabel;

            // Приборы снизу по центру
            GameObject gauges = UI("Gauges", s);
            Anchor(gauges, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(440f, 210f));
            Img(gauges, new Color(0f, 0f, 0f, 0.4f));
            TextMeshProUGUI speed = Txt(gauges.transform, "Speed", "0", 96, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Anchor(speed.gameObject, new Vector2(0.5f, 1f), new Vector2(-30f, -60f), new Vector2(260f, 110f));
            TextMeshProUGUI unit = Txt(gauges.transform, "Unit", "км/ч", 28, TextAlignmentOptions.Left, MutedText);
            Anchor(unit.gameObject, new Vector2(0.5f, 1f), new Vector2(125f, -85f), new Vector2(100f, 40f));
            TextMeshProUGUI gear = Txt(gauges.transform, "Gear", "N", 56, TextAlignmentOptions.Center, new Color(0.3f, 0.9f, 1f), FontStyles.Bold);
            Anchor(gear.gameObject, new Vector2(0f, 1f), new Vector2(50f, -60f), new Vector2(80f, 80f));
            Image rpm = Bar(gauges.transform, "Rpm", new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(380f, 16f), new Color(0.3f, 0.9f, 1f));
            Image dmg = Bar(gauges.transform, "Health", new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(380f, 12f), new Color(0.3f, 0.85f, 0.4f));
            TextMeshProUGUI surface = Txt(gauges.transform, "Surface", "", 24, TextAlignmentOptions.Center, MutedText);
            Anchor(surface.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(380f, 30f));
            hud.speedText = speed;
            hud.gearText = gear;
            hud.rpmFill = rpm;
            hud.damageFill = dmg;
            hud.surfaceText = surface;

            // Сообщения и отсчёт
            GameObject msg = UI("Message", s);
            Anchor(msg, new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(1200f, 80f));
            var msgGroup = msg.AddComponent<CanvasGroup>();
            msgGroup.blocksRaycasts = false;
            TextMeshProUGUI msgText = Txt(msg.transform, "Text", "", 44, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Stretch(msgText.gameObject);
            hud.messageGroup = msgGroup;
            hud.messageText = msgText;

            TextMeshProUGUI countdown = Txt(s, "Countdown", "3", 200, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Anchor(countdown.gameObject, new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(900f, 260f));
            hud.countdownText = countdown;

            BuildMobileControls(canvas.transform);
            PauseMenuUI pause = BuildPauseMenu(canvas.transform);
            return new HudRefs { hud = hud, pause = pause };
        }

        private static HoldButton Hold(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size, Sprite sprite, float font = 36f)
        {
            GameObject go = UI(name, parent);
            Anchor(go, anchor, pos, size);
            Image img = Img(go, new Color(1f, 1f, 1f, 0.65f), sprite);
            img.raycastPadding = new Vector4(-18f, -18f, -18f, -18f); // невидимый запас для пальца
            var hb = go.AddComponent<HoldButton>();
            hb.targetGraphic = img;
            TextMeshProUGUI t = Txt(go.transform, "Label", label, font, TextAlignmentOptions.Center, new Color(0.05f, 0.08f, 0.1f), FontStyles.Bold);
            Stretch(t.gameObject);
            return hb;
        }

        private static void BuildMobileControls(Transform canvas)
        {
            GameObject root = UI("MobileControls", canvas);
            Stretch(root);
            var group = root.AddComponent<CanvasGroup>();
            var mobile = root.AddComponent<MobileInputUI>();
            mobile.controlsGroup = group;

            GameObject safe = UI("SafeArea", root.transform);
            Stretch(safe);
            mobile.safeAreaRoot = (RectTransform)safe.transform;

            // Левый кластер: руль
            GameObject left = UI("LeftCluster", safe.transform);
            Place(left, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(50f, 50f), new Vector2(470f, 240f));
            GameObject steer = UI("SteerButtons", left.transform);
            Stretch(steer);
            mobile.leftCluster = (RectTransform)left.transform;
            mobile.steerButtonsRoot = steer;
            mobile.leftButton = Hold(steer.transform, "Left", "<", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(220f, 220f), roundSprite, 90f);
            ((RectTransform)mobile.leftButton.transform).pivot = Vector2.zero;
            mobile.rightButton = Hold(steer.transform, "Right", ">", new Vector2(0f, 0f), new Vector2(250f, 0f), new Vector2(220f, 220f), roundSprite, 90f);
            ((RectTransform)mobile.rightButton.transform).pivot = Vector2.zero;

            // Правый кластер: педали и ручник
            GameObject right = UI("RightCluster", safe.transform);
            Place(right, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-50f, 50f), new Vector2(520f, 380f));
            mobile.rightCluster = (RectTransform)right.transform;
            mobile.gasButton = Hold(right.transform, "Gas", "ГАЗ", new Vector2(1f, 0f), Vector2.zero, new Vector2(210f, 340f), whiteSprite, 40f);
            ((RectTransform)mobile.gasButton.transform).pivot = new Vector2(1f, 0f);
            mobile.brakeButton = Hold(right.transform, "Brake", "ТОРМОЗ\nНАЗАД", new Vector2(1f, 0f), new Vector2(-240f, 0f), new Vector2(240f, 210f), whiteSprite, 32f);
            ((RectTransform)mobile.brakeButton.transform).pivot = new Vector2(1f, 0f);
            mobile.handbrakeButton = Hold(right.transform, "Handbrake", "РУЧНИК", new Vector2(1f, 0f), new Vector2(-240f, 235f), new Vector2(240f, 110f), whiteSprite, 28f);
            ((RectTransform)mobile.handbrakeButton.transform).pivot = new Vector2(1f, 0f);

            // Сервисные кнопки сверху справа
            GameObject top = UI("TopButtons", safe.transform);
            Place(top, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -25f), new Vector2(760f, 110f));
            var h = top.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childAlignment = TextAnchor.UpperRight;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            var restartGo = UI("Restart", top.transform);
            var rle = restartGo.AddComponent<LayoutElement>();
            rle.preferredWidth = 200f;
            rle.preferredHeight = 100f;
            Image rImg = Img(restartGo, new Color(0.1f, 0.12f, 0.14f, 0.75f));
            var restart = restartGo.AddComponent<HoldButton>();
            restart.holdDuration = 1f;
            restart.targetGraphic = rImg;
            restart.releaseOnExit = true;
            GameObject rFill = UI("HoldFill", restartGo.transform);
            Stretch(rFill);
            Image rFillImg = Img(rFill, new Color(0.3f, 0.9f, 1f, 0.5f));
            rFillImg.type = Image.Type.Filled;
            rFillImg.fillMethod = Image.FillMethod.Horizontal;
            rFillImg.fillAmount = 0f;
            rFillImg.raycastTarget = false;
            restart.holdFill = rFillImg;
            TextMeshProUGUI rText = Txt(restartGo.transform, "Label", "Рестарт\n(держать)", 24, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Stretch(rText.gameObject);
            mobile.restartHoldButton = restart;

            mobile.resetButton = Btn(top.transform, "Reset", "На колёса", new Vector2(190f, 100f), new Color(0.1f, 0.12f, 0.14f, 0.75f), 26);
            mobile.cameraButton = Btn(top.transform, "Camera", "Камера", new Vector2(170f, 100f), new Color(0.1f, 0.12f, 0.14f, 0.75f), 26);
            mobile.pauseButton = Btn(top.transform, "Pause", "II", new Vector2(100f, 100f), new Color(0.1f, 0.12f, 0.14f, 0.75f), 44);
        }

        private static PauseMenuUI BuildPauseMenu(Transform canvas)
        {
            GameObject root = UI("PauseMenu", canvas);
            Stretch(root);
            var pause = root.AddComponent<PauseMenuUI>();

            GameObject dimmer = UI("Dimmer", root.transform);
            Stretch(dimmer);
            Img(dimmer, new Color(0f, 0f, 0f, 0.6f));
            pause.dimmer = dimmer;

            // Пауза
            GameObject panel = Panel(root.transform, "PausePanel", new Vector2(720f, 860f), PanelColor);
            TextMeshProUGUI title = Txt(panel.transform, "Title", "Пауза", 60, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Place(title.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(0f, 90f));
            GameObject list = UI("Buttons", panel.transform);
            Place(list, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            ((RectTransform)list.transform).offsetMin = new Vector2(60f, 40f);
            ((RectTransform)list.transform).offsetMax = new Vector2(-60f, -130f);
            VList(list, 18f);
            pause.resumeButton = Btn(list.transform, "Resume", "Продолжить", new Vector2(0f, 96f), AccentColor);
            pause.restartButton = Btn(list.transform, "Restart", "Начать заново", new Vector2(0f, 96f), ButtonColor);
            pause.respawnButton = Btn(list.transform, "Respawn", "Вернуться на точку", new Vector2(0f, 96f), ButtonColor);
            pause.settingsButton = Btn(list.transform, "Settings", "Настройки", new Vector2(0f, 96f), ButtonColor);
            pause.garageButton = Btn(list.transform, "Garage", "Гараж", new Vector2(0f, 96f), ButtonColor);
            pause.menuButton = Btn(list.transform, "Menu", "Главное меню", new Vector2(0f, 96f), ButtonColor);
            pause.pausePanel = panel;

            // Результат
            GameObject res = Panel(root.transform, "ResultsPanel", new Vector2(1000f, 760f), PanelColor);
            pause.resultTitle = Txt(res.transform, "Title", "Заезд завершён", 64, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Place(pause.resultTitle.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(0f, 90f));
            pause.resultReason = Txt(res.transform, "Reason", "", 36, TextAlignmentOptions.Center, MutedText);
            Place(pause.resultReason.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -125f), new Vector2(-60f, 60f));
            pause.resultMedal = Txt(res.transform, "Medal", "", 72, TextAlignmentOptions.Center, new Color(1f, 0.82f, 0.3f), FontStyles.Bold);
            Place(pause.resultMedal.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -195f), new Vector2(0f, 100f));
            pause.resultTime = Txt(res.transform, "Time", "", 34, TextAlignmentOptions.Center, TextColor);
            Place(pause.resultTime.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -310f), new Vector2(-60f, 60f));
            pause.resultBest = Txt(res.transform, "Best", "", 34, TextAlignmentOptions.Center, new Color(0.3f, 0.9f, 1f), FontStyles.Bold);
            Place(pause.resultBest.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -375f), new Vector2(-60f, 60f));
            pause.resultReward = Txt(res.transform, "Reward", "", 38, TextAlignmentOptions.Center, new Color(0.5f, 1f, 0.6f), FontStyles.Bold);
            Place(pause.resultReward.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -445f), new Vector2(-60f, 60f));

            GameObject row = UI("Buttons", res.transform);
            Place(row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(-80f, 110f));
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 20f;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = true;
            pause.resultRestartButton = Btn(row.transform, "Again", "Ещё раз", new Vector2(0f, 100f), AccentColor);
            pause.resultGarageButton = Btn(row.transform, "Garage", "Гараж", new Vector2(0f, 100f), ButtonColor);
            pause.resultMenuButton = Btn(row.transform, "Menu", "Меню", new Vector2(0f, 100f), ButtonColor);
            pause.resultsPanel = res;

            pause.settings = BuildSettingsPanel(root.transform);
            return pause;
        }

        // ---------- Главное меню ----------

        private static void BuildMainMenuUI()
        {
            Canvas canvas = NewCanvas("Menu", 0);
            var menu = canvas.gameObject.AddComponent<MainMenuUI>();

            GameObject bg = UI("Background", canvas.transform);
            Stretch(bg);
            Img(bg, new Color(0.03f, 0.2f, 0.25f, 1f));

            TextMeshProUGUI title = Txt(canvas.transform, "Title", "TERRAIN DRIVE", 120, TextAlignmentOptions.Left, TextColor, FontStyles.Bold);
            Anchor(title.gameObject, new Vector2(0f, 1f), new Vector2(90f, -60f), new Vector2(1100f, 150f));
            ((RectTransform)title.transform).pivot = new Vector2(0f, 1f);
            TextMeshProUGUI sub = Txt(canvas.transform, "Subtitle", "Open Roads · VinCoder", 40, TextAlignmentOptions.Left, MutedText);
            Anchor(sub.gameObject, new Vector2(0f, 1f), new Vector2(95f, -205f), new Vector2(1100f, 60f));
            ((RectTransform)sub.transform).pivot = new Vector2(0f, 1f);

            // Шапка игрока
            GameObject header = UI("PlayerInfo", canvas.transform);
            Place(header, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -60f), new Vector2(560f, 230f));
            Img(header, new Color(0f, 0f, 0f, 0.3f));
            menu.levelText = Txt(header.transform, "Level", "Уровень 1", 40, TextAlignmentOptions.Left, TextColor, FontStyles.Bold);
            Place(menu.levelText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(-60f, 55f));
            menu.xpFill = Bar(header.transform, "Xp", new Vector2(0.5f, 1f), new Vector2(0f, -95f), new Vector2(500f, 14f), new Color(0.3f, 0.9f, 1f));
            menu.currencyText = Txt(header.transform, "Currency", "0 монет", 34, TextAlignmentOptions.Left, new Color(1f, 0.85f, 0.4f));
            Place(menu.currencyText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(30f, -120f), new Vector2(-60f, 50f));
            menu.vehicleText = Txt(header.transform, "Vehicle", "", 30, TextAlignmentOptions.Left, MutedText);
            Place(menu.vehicleText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(30f, -170f), new Vector2(-60f, 45f));

            // Главная панель
            GameObject main = UI("MainPanel", canvas.transform);
            Place(main, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(90f, 90f), new Vector2(560f, 520f));
            VList(main, 20f);
            menu.playButton = Btn(main.transform, "Play", "Играть", new Vector2(0f, 110f), AccentColor, 44);
            menu.garageButton = Btn(main.transform, "Garage", "Гараж", new Vector2(0f, 100f), ButtonColor, 38);
            menu.settingsButton = Btn(main.transform, "Settings", "Настройки", new Vector2(0f, 100f), ButtonColor, 38);
            menu.quitButton = Btn(main.transform, "Quit", "Выход", new Vector2(0f, 100f), ButtonColor, 38);
            menu.mainPanel = main;

            // Список зон
            GameObject zones = Panel(canvas.transform, "ZonePanel", new Vector2(1100f, 860f), PanelColor);
            TextMeshProUGUI zTitle = Txt(zones.transform, "Title", "Выберите зону", 54, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Place(zTitle.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(0f, 80f));
            GameObject zList = UI("List", zones.transform);
            Place(zList, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            ((RectTransform)zList.transform).offsetMin = new Vector2(60f, 150f);
            ((RectTransform)zList.transform).offsetMax = new Vector2(-60f, -130f);
            VList(zList, 14f);
            Button template = Btn(zList.transform, "ListButtonTemplate", "Пункт", new Vector2(0f, 88f), ButtonColor, 34);
            menu.zoneBackButton = Btn(zones.transform, "Back", "Назад", new Vector2(360f, 90f), ButtonColor, 34);
            Anchor(menu.zoneBackButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(360f, 90f));
            menu.zonePanel = zones;
            menu.zoneList = (RectTransform)zList.transform;
            menu.listButtonTemplate = template;

            // Список режимов
            GameObject modes = Panel(canvas.transform, "ModePanel", new Vector2(1100f, 860f), PanelColor);
            menu.modeHeader = Txt(modes.transform, "Title", "Зона", 54, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            Place(menu.modeHeader.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(0f, 80f));
            GameObject mList = UI("List", modes.transform);
            Place(mList, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            ((RectTransform)mList.transform).offsetMin = new Vector2(60f, 150f);
            ((RectTransform)mList.transform).offsetMax = new Vector2(-60f, -130f);
            VList(mList, 14f);
            menu.modeBackButton = Btn(modes.transform, "Back", "Назад", new Vector2(360f, 90f), ButtonColor, 34);
            Anchor(menu.modeBackButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(360f, 90f));
            menu.modePanel = modes;
            menu.modeList = (RectTransform)mList.transform;

            menu.settings = BuildSettingsPanel(canvas.transform);
            zones.SetActive(false);
            modes.SetActive(false);
        }

        // ---------- Гараж ----------

        private static void BuildGarageUI(Transform previewSpawn)
        {
            Canvas canvas = NewCanvas("GarageCanvas", 0);
            var g = canvas.gameObject.AddComponent<GarageUI>();
            g.previewSpawn = previewSpawn;

            g.currencyText = Txt(canvas.transform, "Currency", "", 36, TextAlignmentOptions.Right, new Color(1f, 0.85f, 0.4f), FontStyles.Bold);
            Place(g.currencyText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -40f), new Vector2(800f, 60f));
            g.backButton = Btn(canvas.transform, "Back", "Назад", new Vector2(260f, 90f), ButtonColor, 34);
            Place(g.backButton.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -30f), new Vector2(260f, 90f));

            // Левая панель: описание и характеристики
            GameObject info = UI("Info", canvas.transform);
            Place(info, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(50f, -20f), new Vector2(620f, 760f));
            Img(info, PanelColor);
            g.nameText = Txt(info.transform, "Name", "Машина", 52, TextAlignmentOptions.Left, TextColor, FontStyles.Bold);
            Place(g.nameText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(30f, -25f), new Vector2(-60f, 70f));
            g.descriptionText = Txt(info.transform, "Description", "", 28, TextAlignmentOptions.TopLeft, MutedText);
            Place(g.descriptionText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(30f, -105f), new Vector2(-60f, 120f));

            string[] statNames = { "Скорость", "Разгон", "Бездорожье", "Прочность" };
            Image[] bars = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                TextMeshProUGUI l = Txt(info.transform, "Stat" + i, statNames[i], 28, TextAlignmentOptions.Left, TextColor);
                Place(l.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -245f - i * 62f), new Vector2(220f, 44f));
                bars[i] = Bar(info.transform, "StatBar" + i, new Vector2(0f, 1f), new Vector2(250f, -267f - i * 62f), new Vector2(330f, 18f), new Color(0.3f, 0.9f, 1f));
            }
            g.speedBar = bars[0];
            g.accelerationBar = bars[1];
            g.offroadBar = bars[2];
            g.durabilityBar = bars[3];

            g.priceText = Txt(info.transform, "Price", "", 32, TextAlignmentOptions.Left, new Color(1f, 0.85f, 0.4f));
            Place(g.priceText.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(30f, 150f), new Vector2(-60f, 50f));
            g.actionButton = Btn(info.transform, "Action", "Выбрать", new Vector2(0f, 100f), AccentColor, 38);
            Place(g.actionButton.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(-60f, 100f));
            g.actionLabel = LabelOf(g.actionButton);

            // Стрелки выбора
            g.prevButton = Btn(canvas.transform, "Prev", "<", new Vector2(130f, 130f), ButtonColor, 70);
            Anchor(g.prevButton.gameObject, new Vector2(0.5f, 0f), new Vector2(-260f, 70f), new Vector2(130f, 130f));
            g.nextButton = Btn(canvas.transform, "Next", ">", new Vector2(130f, 130f), ButtonColor, 70);
            Anchor(g.nextButton.gameObject, new Vector2(0.5f, 0f), new Vector2(260f, 70f), new Vector2(130f, 130f));

            // Правая панель: улучшения и ремонт
            GameObject up = UI("Upgrades", canvas.transform);
            Place(up, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-50f, -40f), new Vector2(560f, 640f));
            Img(up, PanelColor);
            VList(up, 16f, 30);
            TextMeshProUGUI upTitle = Txt(up.transform, "Title", "Улучшения", 44, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
            upTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;
            g.engineButton = Btn(up.transform, "Engine", "Двигатель", new Vector2(0f, 92f), ButtonColor, 30);
            g.engineLabel = LabelOf(g.engineButton);
            g.gripButton = Btn(up.transform, "Grip", "Шины", new Vector2(0f, 92f), ButtonColor, 30);
            g.gripLabel = LabelOf(g.gripButton);
            g.durabilityButton = Btn(up.transform, "Durability", "Прочность", new Vector2(0f, 92f), ButtonColor, 30);
            g.durabilityLabel = LabelOf(g.durabilityButton);
            g.healthText = Txt(up.transform, "Health", "", 30, TextAlignmentOptions.Center, MutedText);
            g.healthText.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
            g.repairButton = Btn(up.transform, "Repair", "Ремонт", new Vector2(0f, 92f), AccentColor, 30);
            g.repairLabel = LabelOf(g.repairButton);
        }
    }
}
#endif
