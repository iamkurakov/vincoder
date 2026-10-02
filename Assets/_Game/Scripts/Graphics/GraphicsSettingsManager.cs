using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TerrainDrive
{
    public enum GraphicsPreset
    {
        Low,
        Medium,
        High,
        Ultra
    }

    /// <summary>Набор значений одного пресета качества.</summary>
    [Serializable]
    public class GraphicsPresetValues
    {
        public string name = "Средняя";
        [Range(0.5f, 1.5f)] public float renderScale = 1f;
        public float shadowDistance = 60f;
        [Range(1, 4)] public int shadowCascades = 2;
        [Tooltip("Сглаживание MSAA: 1 (выкл), 2, 4.")] public int msaa = 1;
        public float lodBias = 1f;
        [Tooltip("0 — полные текстуры, 1 — в 2 раза меньше, 2 — в 4 раза.")] public int textureMipLimit;
        [Tooltip("Дальность прорисовки камеры, м.")] public float drawDistance = 500f;
        [Range(0f, 1f)] public float grassDensity = 0.5f;
        public float grassDistance = 50f;
        public float treeDistance = 400f;
        public float treeBillboardDistance = 100f;
        [Tooltip("Точность ландшафта: меньше = детальнее и тяжелее.")] public float terrainPixelError = 8f;
        public int aiCount = 6;
        public bool postProcessing = true;
        public int targetFps = 60;
        [Tooltip("Шаг физики, сек. 0.02 = 50 раз в секунду.")] public float fixedTimestep = 0.02f;

        /// <summary>Копия значений (ручные настройки не портят исходный пресет).</summary>
        public GraphicsPresetValues Clone() => (GraphicsPresetValues)MemberwiseClone();
    }

    /// <summary>
    /// Применяет качество графики: автоматически по устройству или вручную из настроек.
    /// Управляет URP (масштаб рендера, тени, MSAA), LOD, текстурами, Terrain (трава, деревья),
    /// дальностью камеры, постобработкой, числом AI-машин, лимитом FPS и частотой физики.
    /// Где лежит: префаб Resources/GameSystems.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public class GraphicsSettingsManager : MonoBehaviour
    {
        public static GraphicsSettingsManager Instance { get; private set; }
        public static event Action SettingsApplied;

        public GraphicsPresetValues[] presets =
        {
            new GraphicsPresetValues { name = "Низкая", renderScale = 0.75f, shadowDistance = 30f, shadowCascades = 1, msaa = 1, lodBias = 0.6f, textureMipLimit = 1,
                drawDistance = 300f, grassDensity = 0f, grassDistance = 0f, treeDistance = 220f, treeBillboardDistance = 50f, terrainPixelError = 14f,
                aiCount = 0, postProcessing = false, targetFps = 30, fixedTimestep = 0.025f },
            new GraphicsPresetValues { name = "Средняя", renderScale = 0.85f, shadowDistance = 50f, shadowCascades = 1, msaa = 1, lodBias = 0.85f, textureMipLimit = 0,
                drawDistance = 450f, grassDensity = 0.35f, grassDistance = 35f, treeDistance = 350f, treeBillboardDistance = 80f, terrainPixelError = 10f,
                aiCount = 4, postProcessing = false, targetFps = 60, fixedTimestep = 0.02f },
            new GraphicsPresetValues { name = "Высокая", renderScale = 1f, shadowDistance = 90f, shadowCascades = 2, msaa = 2, lodBias = 1.2f, textureMipLimit = 0,
                drawDistance = 700f, grassDensity = 0.7f, grassDistance = 60f, treeDistance = 600f, treeBillboardDistance = 150f, terrainPixelError = 6f,
                aiCount = 8, postProcessing = true, targetFps = 60, fixedTimestep = 0.02f },
            new GraphicsPresetValues { name = "Ультра", renderScale = 1f, shadowDistance = 150f, shadowCascades = 4, msaa = 4, lodBias = 1.6f, textureMipLimit = 0,
                drawDistance = 1000f, grassDensity = 1f, grassDistance = 90f, treeDistance = 1000f, treeBillboardDistance = 220f, terrainPixelError = 4f,
                aiCount = 14, postProcessing = true, targetFps = 120, fixedTimestep = 0.0166f },
        };

        [Tooltip("В редакторе изменения URP-ассета сохраняются в файл проекта. По умолчанию в редакторе не трогаем.")]
        public bool modifyUrpAssetInEditor;

        public GraphicsPreset ActivePreset { get; private set; } = GraphicsPreset.Medium;
        /// <summary>Итоговые значения с учётом ручных настроек.</summary>
        public GraphicsPresetValues Current { get; private set; } = new GraphicsPresetValues();
        public int AiTrafficCount => Current.aiCount;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Instance = null;
            }
        }

        private void Start()
        {
            var gm = GameManager.Instance;
            if (gm != null) PlatformManager.ApplyLayoutOverride(gm.Progress.Settings.layoutOverride);
            ApplyAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyToScene();
            SettingsApplied?.Invoke();
        }

        public static GraphicsPreset AutoPreset()
        {
            int ram = SystemInfo.systemMemorySize;     // МБ
            int vram = SystemInfo.graphicsMemorySize;  // МБ
            switch (PlatformManager.Device)
            {
                case DeviceClass.Desktop:
                    if (vram >= 6000) return GraphicsPreset.Ultra;
                    if (vram >= 2500) return GraphicsPreset.High;
                    return GraphicsPreset.Medium;
                case DeviceClass.Tablet:
                    if (ram >= 8000) return GraphicsPreset.High;
                    if (ram >= 3500) return GraphicsPreset.Medium;
                    return GraphicsPreset.Low;
                default:
                    if (ram >= 8000) return GraphicsPreset.Medium;
                    return GraphicsPreset.Low;
            }
        }

        /// <summary>Пересчитать и применить всё. Вызывать после изменения настроек.</summary>
        public void ApplyAll()
        {
            SettingsData s = GameManager.Instance != null ? GameManager.Instance.Progress.Settings : new SettingsData();
            ActivePreset = s.graphicsPreset >= 0 ? (GraphicsPreset)Mathf.Clamp(s.graphicsPreset, 0, presets.Length - 1) : AutoPreset();
            Current = BuildEffective(presets[(int)ActivePreset], s);

            ApplyGlobal();
            ApplyToScene();
            ApplyResolution(s);
            SettingsApplied?.Invoke();
        }

        private static GraphicsPresetValues BuildEffective(GraphicsPresetValues p, SettingsData s)
        {
            var v = p.Clone();

            switch (s.drawDistance)
            {
                case 0: v.drawDistance = 250f; break;
                case 1: v.drawDistance = 450f; break;
                case 2: v.drawDistance = 850f; break;
            }
            switch (s.grassDensity)
            {
                case 0: v.grassDensity = 0f; v.grassDistance = 0f; break;
                case 1: v.grassDensity = 0.3f; v.grassDistance = Mathf.Max(30f, v.grassDistance); break;
                case 2: v.grassDensity = 0.6f; v.grassDistance = Mathf.Max(50f, v.grassDistance); break;
                case 3: v.grassDensity = 1f; v.grassDistance = Mathf.Max(80f, v.grassDistance); break;
            }
            switch (s.treeDensity)
            {
                case 0: v.treeDistance = 200f; v.treeBillboardDistance = 50f; break;
                case 1: v.treeDistance = 400f; v.treeBillboardDistance = 110f; break;
                case 2: v.treeDistance = 900f; v.treeBillboardDistance = 200f; break;
            }
            switch (s.shadowQuality)
            {
                case 0: v.shadowDistance = 0f; v.shadowCascades = 1; break;
                case 1: v.shadowDistance = 35f; v.shadowCascades = 1; break;
                case 2: v.shadowDistance = 120f; v.shadowCascades = 4; break;
            }
            if (s.aiTrafficCount >= 0) v.aiCount = s.aiTrafficCount;
            if (s.postProcessing >= 0) v.postProcessing = s.postProcessing == 1;
            if (s.fpsLimit >= 0) v.targetFps = s.fpsLimit;
            v.drawDistance = Mathf.Max(v.drawDistance, 150f);
            return v;
        }

        private void ApplyGlobal()
        {
            var v = Current;
            bool touchUrp = true;
#if UNITY_EDITOR
            touchUrp = modifyUrpAssetInEditor;
#endif
            if (touchUrp && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = v.renderScale;
                urp.shadowDistance = v.shadowDistance;
                urp.shadowCascadeCount = v.shadowCascades;
                urp.msaaSampleCount = v.msaa;
            }
            QualitySettings.lodBias = v.lodBias;
            QualitySettings.globalTextureMipmapLimit = v.textureMipLimit;
            QualitySettings.vSyncCount = 0;

            int fps = v.targetFps;
            if (fps == 0) fps = Application.isMobilePlatform
                ? Mathf.Max(60, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value))
                : -1;
            Application.targetFrameRate = fps;
            Time.fixedDeltaTime = v.fixedTimestep;
        }

        /// <summary>Настройки, которые живут в объектах сцены (Terrain, камеры).</summary>
        public void ApplyToScene()
        {
            var v = Current;
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                t.detailObjectDensity = v.grassDensity;
                t.detailObjectDistance = v.grassDistance;
                t.treeDistance = Mathf.Min(v.treeDistance, v.drawDistance);
                t.treeBillboardDistance = v.treeBillboardDistance;
                t.heightmapPixelError = v.terrainPixelError;
                t.basemapDistance = Mathf.Min(v.drawDistance, 300f);
                t.drawTreesAndFoliage = v.treeDistance > 0f;
            }

            // Туман прячет границу прорисовки
            if (RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear)
            {
                RenderSettings.fogStartDistance = v.drawDistance * 0.35f;
                RenderSettings.fogEndDistance = v.drawDistance * 0.95f;
            }

            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || cam.targetTexture != null) continue; // мини-карты и т. п. не трогаем
                cam.farClipPlane = v.drawDistance;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = v.postProcessing;
            }
        }

        private void ApplyResolution(SettingsData s)
        {
            if (Application.isMobilePlatform) return;
            Resolution[] list = Screen.resolutions;
            FullScreenMode mode = s.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (s.resolutionIndex >= 0 && s.resolutionIndex < list.Length)
            {
                Resolution r = list[s.resolutionIndex];
                if (Screen.width != r.width || Screen.height != r.height || Screen.fullScreenMode != mode)
                    Screen.SetResolution(r.width, r.height, mode);
            }
            else if (Screen.fullScreenMode != mode)
            {
                Screen.fullScreenMode = mode;
            }
        }

        public string PresetName(GraphicsPreset p) => presets[(int)p].name;
    }
}
