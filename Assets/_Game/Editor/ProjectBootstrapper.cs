#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TerrainDrive.EditorTools
{
    /// <summary>
    /// Автосборка проекта одной командой: меню Terrain Drive → «Собрать проект».
    /// Создаёт материалы, текстуры, покрытия, 5 машин, префабы (машина-заготовка, AI-машина,
    /// чекпоинт, системы игры), 9 сцен (Boot, MainMenu, Garage, TestTrack, Highway, City, Forest,
    /// Offroad, OpenWorld), список сцен сборки и базовые Player Settings.
    /// Повторный запуск безопасен: ассеты обновляются, сцены пересоздаются.
    /// </summary>
    public static partial class ProjectBootstrapper
    {
        private const string Root = "Assets/_Game";
        private const string MatDir = Root + "/Materials/Generated";
        private const string TexDir = Root + "/Art/Generated";
        private const string SurfDir = Root + "/ScriptableObjects/Surfaces";
        private const string VehDir = Root + "/ScriptableObjects/Vehicles";
        private const string PrefabDir = Root + "/Prefabs";
        private const string ResDir = Root + "/Resources";
        private const string SceneDir = Root + "/Scenes";
        private const string TerrainDir = Root + "/Art/Generated/Terrain";

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static readonly Dictionary<SurfaceKind, SurfaceData> Surfaces = new Dictionary<SurfaceKind, SurfaceData>();
        private static readonly Dictionary<string, TerrainLayer> Layers = new Dictionary<string, TerrainLayer>();
        private static Sprite whiteSprite;
        private static Sprite roundSprite;
        private static GameObject playerPrefab;
        private static GameObject aiPrefab;
        private static GameObject checkpointPrefab;
        private static GameObject systemsPrefab;
        private static VehicleData[] vehicleAssets;

        [MenuItem("Terrain Drive/Собрать проект (данные, префабы, сцены)", priority = 0)]
        public static void BuildAll()
        {
            if (!CheckTmp()) return;
            if (!EditorUtility.DisplayDialog("Terrain Drive",
                    "Будут созданы (или пересозданы) материалы, данные машин и покрытий, префабы и 9 сцен в Assets/_Game.\n\n" +
                    "Сцены с теми же именами будут перезаписаны. Продолжить?", "Собрать", "Отмена"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                Progress("Папки", 0.02f);
                CreateFolders();
                Progress("Текстуры и материалы", 0.06f);
                CreateTexturesAndMaterials();
                Progress("Покрытия", 0.12f);
                CreateSurfaces();
                CreateTerrainLayers();
                Progress("Машина игрока", 0.18f);
                playerPrefab = CreatePlayerVehiclePrefab();
                vehicleAssets = CreateVehicleData(playerPrefab);
                Progress("AI-машина и чекпоинт", 0.24f);
                aiPrefab = CreateAIPrefab();
                checkpointPrefab = CreateCheckpointPrefab();
                Progress("Системы игры", 0.28f);
                systemsPrefab = CreateGameSystemsPrefab(vehicleAssets);

                Progress("Сцена Boot", 0.32f);
                CreateBootScene();
                Progress("Сцена MainMenu", 0.36f);
                CreateMainMenuScene();
                Progress("Сцена Garage", 0.40f);
                CreateGarageScene();
                Progress("Сцена TestTrack", 0.46f);
                CreateTestTrackScene();
                Progress("Сцена Highway", 0.54f);
                CreateHighwayScene();
                Progress("Сцена City", 0.62f);
                CreateCityScene();
                Progress("Сцена Forest", 0.70f);
                CreateForestScene();
                Progress("Сцена Offroad", 0.80f);
                CreateOffroadScene();
                Progress("Сцена OpenWorld", 0.90f);
                CreateOpenWorldScene();

                Progress("Build Settings", 0.97f);
                SetBuildScenes();
                ConfigurePlayerSettings();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorSceneManager.OpenScene(SceneDir + "/Boot.unity");
            EditorUtility.DisplayDialog("Terrain Drive",
                "Готово! Откройте сцену Boot и нажмите Play.\n\n" +
                "Подсказка: чтобы увидеть экранные кнопки в редакторе, включите Simulate In Editor " +
                "у PlatformManager (префаб Resources/GameSystems).", "OK");
        }

        [MenuItem("Terrain Drive/Удалить сохранение игрока", priority = 20)]
        public static void DeleteSave()
        {
            SaveSystem.DeleteAll();
            Debug.Log("[Terrain Drive] Сохранение удалено: " + SaveSystem.FilePath);
        }

        [MenuItem("Terrain Drive/Открыть папку сохранений", priority = 21)]
        public static void OpenSaveFolder()
        {
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }

        private static void Progress(string step, float t)
        {
            EditorUtility.DisplayProgressBar("Terrain Drive — сборка проекта", step, t);
        }

        private static bool CheckTmp()
        {
            TMP_FontAsset font = null;
            try
            {
                font = TMP_Settings.defaultFontAsset;
            }
            catch
            {
                // TMP Essentials ещё не импортированы
            }
            if (font != null) return true;
            EditorUtility.DisplayDialog("Нужен TextMeshPro",
                "Сначала импортируйте ресурсы TextMeshPro:\nWindow → TextMeshPro → Import TMP Essential Resources.\n\n" +
                "После импорта снова запустите Terrain Drive → Собрать проект.", "Понятно");
            return false;
        }

        // ================= Папки =================

        private static void CreateFolders()
        {
            string[] folders =
            {
                Root + "/Art", TexDir, TerrainDir, Root + "/Materials", MatDir, Root + "/ScriptableObjects",
                SurfDir, VehDir, PrefabDir, PrefabDir + "/Vehicles", PrefabDir + "/AI", PrefabDir + "/Checkpoints",
                ResDir, SceneDir, Root + "/Audio", Root + "/Settings"
            };
            foreach (string f in folders) EnsureFolder(f);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static string FullPath(string assetPath) =>
            Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ================= Текстуры и материалы =================

        private static Texture2D MakeTexture(string name, Color baseColor, float noise, int size = 64, bool sprite = false)
        {
            string path = TexDir + "/" + name + ".png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rnd = new System.Random(name.GetHashCode());
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = noise > 0f ? (float)(rnd.NextDouble() * 2.0 - 1.0) * noise : 0f;
                float p = noise > 0f ? (Mathf.PerlinNoise(x * 0.15f, y * 0.15f) - 0.5f) * noise : 0f;
                Color c = baseColor * (1f + n + p);
                c.a = baseColor.a;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            File.WriteAllBytes(FullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                if (sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.mipmapEnabled = false;
                }
                else
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.mipmapEnabled = true;
                }
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Sprite MakeRoundSprite()
        {
            const int size = 128;
            string path = TexDir + "/UI_Round.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
            }
            tex.Apply();
            File.WriteAllBytes(FullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Shader LitShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        private static Material Mat(string name, Color color, float smoothness = 0.25f, float metallic = 0f,
            Texture2D texture = null, float emission = 0f)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(LitShader());
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = LitShader();
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (texture != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
            }
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        private static void CreateTexturesAndMaterials()
        {
            MakeTexture("UI_White", Color.white, 0f, 8, true);
            whiteSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "/UI_White.png");
            roundSprite = MakeRoundSprite();

            Texture2D asphaltTex = MakeTexture("T_Asphalt", new Color(0.25f, 0.25f, 0.27f), 0.18f);
            Texture2D gravelTex = MakeTexture("T_Gravel", new Color(0.55f, 0.5f, 0.42f), 0.35f);
            Texture2D mudTex = MakeTexture("T_Mud", new Color(0.3f, 0.21f, 0.13f), 0.25f);
            Texture2D grassTex = MakeTexture("T_Grass", new Color(0.28f, 0.48f, 0.2f), 0.3f);
            Texture2D sandTex = MakeTexture("T_Sand", new Color(0.86f, 0.76f, 0.52f), 0.15f);
            Texture2D soilTex = MakeTexture("T_ForestSoil", new Color(0.27f, 0.24f, 0.15f), 0.3f);
            Texture2D rockTex = MakeTexture("T_Rocks", new Color(0.48f, 0.47f, 0.45f), 0.4f);

            Mat("Asphalt", Color.white, 0.2f, 0f, asphaltTex);
            Mat("WetAsphalt", new Color(0.6f, 0.65f, 0.75f), 0.85f, 0f, asphaltTex);
            Mat("Gravel", Color.white, 0.1f, 0f, gravelTex);
            Mat("Mud", Color.white, 0.55f, 0f, mudTex);
            Mat("Grass", Color.white, 0.1f, 0f, grassTex);
            Mat("Sand", Color.white, 0.05f, 0f, sandTex);
            Mat("ForestSoil", Color.white, 0.1f, 0f, soilTex);
            Mat("Rocks", Color.white, 0.2f, 0f, rockTex);
            Mat("Water", new Color(0.15f, 0.35f, 0.55f, 1f), 0.95f);

            Mat("CarBody", new Color(0.8f, 0.8f, 0.82f), 0.75f, 0.4f);
            Mat("CarTrim", new Color(0.06f, 0.06f, 0.07f), 0.4f);
            Mat("Glass", new Color(0.08f, 0.12f, 0.16f), 0.95f, 0.1f);
            Mat("Tire", new Color(0.05f, 0.05f, 0.05f), 0.15f);
            Mat("Rim", new Color(0.75f, 0.76f, 0.78f), 0.8f, 0.9f);
            Mat("Headlight", new Color(1f, 0.97f, 0.85f), 0.9f, 0f, null, 1.5f);
            Mat("BrakeLight", new Color(1f, 0.05f, 0.05f), 0.9f, 0f, null, 3f);
            Mat("TailLight", new Color(0.45f, 0.02f, 0.02f), 0.8f);
            Mat("ReverseLight", new Color(1f, 1f, 1f), 0.9f, 0f, null, 2f);

            Mat("AIBody", new Color(0.8f, 0.2f, 0.2f), 0.6f, 0.3f);
            Mat("Checkpoint", new Color(0.1f, 0.9f, 1f), 0.5f, 0f, null, 1.5f);
            Mat("Repair", new Color(0.2f, 0.95f, 0.4f), 0.5f, 0f, null, 1.2f);
            Mat("RoadLine", new Color(0.95f, 0.95f, 0.9f), 0.3f);
            Mat("Curb", new Color(0.7f, 0.7f, 0.68f), 0.2f);
            Mat("Barrier", new Color(0.78f, 0.8f, 0.82f), 0.7f, 0.8f);
            Mat("Pole", new Color(0.35f, 0.36f, 0.38f), 0.5f, 0.6f);
            Mat("BuildingA", new Color(0.82f, 0.76f, 0.66f), 0.2f);
            Mat("BuildingB", new Color(0.55f, 0.58f, 0.62f), 0.3f);
            Mat("BuildingC", new Color(0.62f, 0.33f, 0.26f), 0.2f);
            Mat("Trunk", new Color(0.33f, 0.23f, 0.15f), 0.1f);
            Mat("Leaves", new Color(0.16f, 0.36f, 0.16f), 0.15f);
            Mat("LeavesDark", new Color(0.1f, 0.26f, 0.14f), 0.15f);
            Mat("Ramp", new Color(0.95f, 0.55f, 0.1f), 0.3f);
            Mat("Cone", new Color(1f, 0.45f, 0.05f), 0.4f);
            Mat("Turntable", new Color(0.22f, 0.24f, 0.26f), 0.6f, 0.5f);
        }

        // ================= Покрытия =================

        private static SurfaceData Surface(SurfaceKind kind, string displayName, float grip, float speed,
            float resistance, float brake, float bump, bool squeal, bool dust, Color dustColor)
        {
            var s = LoadOrCreate<SurfaceData>(SurfDir + "/Surface_" + kind + ".asset");
            s.kind = kind;
            s.displayName = displayName;
            s.grip = grip;
            s.speedMultiplier = speed;
            s.rollingResistance = resistance;
            s.brakeMultiplier = brake;
            s.bumpiness = bump;
            s.tireSqueal = squeal;
            s.emitDust = dust;
            s.dustColor = dustColor;
            EditorUtility.SetDirty(s);
            Surfaces[kind] = s;
            return s;
        }

        private static void CreateSurfaces()
        {
            Surface(SurfaceKind.Asphalt, "Асфальт", 1f, 1f, 0f, 1f, 0f, true, false, Color.gray);
            Surface(SurfaceKind.WetAsphalt, "Мокрый асфальт", 0.72f, 0.95f, 0f, 0.6f, 0f, true, false, Color.gray);
            Surface(SurfaceKind.Gravel, "Гравий", 0.7f, 0.8f, 0.6f, 0.8f, 0.15f, false, true, new Color(0.6f, 0.55f, 0.45f, 0.6f));
            Surface(SurfaceKind.Mud, "Грязь", 0.4f, 0.45f, 3.5f, 0.5f, 0.1f, false, false, new Color(0.3f, 0.2f, 0.1f, 0.7f));
            Surface(SurfaceKind.Grass, "Трава", 0.6f, 0.75f, 0.8f, 0.7f, 0.1f, false, false, new Color(0.3f, 0.45f, 0.2f, 0.4f));
            Surface(SurfaceKind.Sand, "Песок", 0.5f, 0.55f, 2.5f, 0.6f, 0.05f, false, true, new Color(0.85f, 0.75f, 0.5f, 0.6f));
            Surface(SurfaceKind.ForestSoil, "Лесная почва", 0.62f, 0.7f, 1f, 0.7f, 0.2f, false, true, new Color(0.35f, 0.3f, 0.2f, 0.5f));
            Surface(SurfaceKind.Rocks, "Камни", 0.55f, 0.6f, 1.2f, 0.7f, 0.6f, false, true, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            Surface(SurfaceKind.Water, "Вода", 0.45f, 0.4f, 4.5f, 0.5f, 0.05f, false, false, new Color(0.6f, 0.7f, 0.8f, 0.6f));
        }

        private static void CreateTerrainLayers()
        {
            System.Action<string, string, float> Layer = (name, tex, tile) =>
            {
                string path = TerrainDir + "/TL_" + name + ".terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (l == null)
                {
                    l = new TerrainLayer();
                    AssetDatabase.CreateAsset(l, path);
                }
                l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + tex + ".png");
                l.tileSize = new Vector2(tile, tile);
                EditorUtility.SetDirty(l);
                Layers[name] = l;
            };

            Layer("Grass", "T_Grass", 6f);
            Layer("Mud", "T_Mud", 5f);
            Layer("Sand", "T_Sand", 6f);
            Layer("Dirt", "T_Gravel", 4f);
            Layer("Rocks", "T_Rocks", 5f);
            Layer("Asphalt", "T_Asphalt", 4f);
        }

        // ================= Примитивы =================

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos,
            Vector3 localScale, Material mat, bool keepCollider = false, Vector3? localEuler = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (localEuler.HasValue) go.transform.localEulerAngles = localEuler.Value;
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            if (!keepCollider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            return go;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ================= Машина игрока =================

        private static GameObject CreatePlayerVehiclePrefab()
        {
            var root = new GameObject("Vehicle_Placeholder");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1500f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var bodyCol = root.AddComponent<BoxCollider>();
            bodyCol.center = new Vector3(0f, 0.85f, 0f);
            bodyCol.size = new Vector3(1.9f, 0.75f, 4.5f);
            var cabinCol = root.AddComponent<BoxCollider>();
            cabinCol.center = new Vector3(0f, 1.5f, -0.3f);
            cabinCol.size = new Vector3(1.7f, 0.6f, 2.4f);

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            GameObject body = Prim(PrimitiveType.Cube, "Body", visual, new Vector3(0f, 0.85f, 0f), new Vector3(1.9f, 0.7f, 4.5f), Mats["CarBody"]);
            GameObject cabin = Prim(PrimitiveType.Cube, "Cabin", visual, new Vector3(0f, 1.5f, -0.3f), new Vector3(1.7f, 0.6f, 2.4f), Mats["CarBody"]);
            Prim(PrimitiveType.Cube, "Windshield", visual, new Vector3(0f, 1.5f, 0.91f), new Vector3(1.62f, 0.5f, 0.04f), Mats["Glass"]);
            Prim(PrimitiveType.Cube, "RearGlass", visual, new Vector3(0f, 1.5f, -1.51f), new Vector3(1.62f, 0.5f, 0.04f), Mats["Glass"]);
            Prim(PrimitiveType.Cube, "SideGlassL", visual, new Vector3(-0.86f, 1.5f, -0.3f), new Vector3(0.02f, 0.45f, 2.2f), Mats["Glass"]);
            Prim(PrimitiveType.Cube, "SideGlassR", visual, new Vector3(0.86f, 1.5f, -0.3f), new Vector3(0.02f, 0.45f, 2.2f), Mats["Glass"]);
            Prim(PrimitiveType.Cube, "BumperF", visual, new Vector3(0f, 0.55f, 2.27f), new Vector3(1.94f, 0.3f, 0.12f), Mats["CarTrim"]);
            Prim(PrimitiveType.Cube, "BumperR", visual, new Vector3(0f, 0.55f, -2.27f), new Vector3(1.94f, 0.3f, 0.12f), Mats["CarTrim"]);
            Prim(PrimitiveType.Cube, "HeadlightL", visual, new Vector3(-0.62f, 0.95f, 2.26f), new Vector3(0.45f, 0.12f, 0.04f), Mats["Headlight"]);
            Prim(PrimitiveType.Cube, "HeadlightR", visual, new Vector3(0.62f, 0.95f, 2.26f), new Vector3(0.45f, 0.12f, 0.04f), Mats["Headlight"]);
            Prim(PrimitiveType.Cube, "TailBar", visual, new Vector3(0f, 1.0f, -2.26f), new Vector3(1.75f, 0.07f, 0.04f), Mats["TailLight"]);

            var brakeLights = new GameObject("BrakeLights");
            brakeLights.transform.SetParent(root.transform, false);
            Prim(PrimitiveType.Cube, "BrakeL", brakeLights.transform, new Vector3(-0.68f, 0.98f, -2.28f), new Vector3(0.38f, 0.12f, 0.04f), Mats["BrakeLight"]);
            Prim(PrimitiveType.Cube, "BrakeR", brakeLights.transform, new Vector3(0.68f, 0.98f, -2.28f), new Vector3(0.38f, 0.12f, 0.04f), Mats["BrakeLight"]);
            brakeLights.SetActive(false);

            var reverseLights = new GameObject("ReverseLights");
            reverseLights.transform.SetParent(root.transform, false);
            Prim(PrimitiveType.Cube, "ReverseL", reverseLights.transform, new Vector3(-0.32f, 0.75f, -2.28f), new Vector3(0.16f, 0.08f, 0.04f), Mats["ReverseLight"]);
            Prim(PrimitiveType.Cube, "ReverseR", reverseLights.transform, new Vector3(0.32f, 0.75f, -2.28f), new Vector3(0.16f, 0.08f, 0.04f), Mats["ReverseLight"]);
            reverseLights.SetActive(false);

            // Колёса
            var wheels = new WheelSetup[4];
            Vector3[] pos =
            {
                new Vector3(-0.84f, 0.5f, 1.4f), new Vector3(0.84f, 0.5f, 1.4f),
                new Vector3(-0.84f, 0.5f, -1.4f), new Vector3(0.84f, 0.5f, -1.4f)
            };
            string[] names = { "FL", "FR", "RL", "RR" };
            Transform colliders = new GameObject("WheelColliders").transform;
            colliders.SetParent(root.transform, false);
            for (int i = 0; i < 4; i++)
            {
                var wcGo = new GameObject("WheelCollider_" + names[i]);
                wcGo.transform.SetParent(colliders, false);
                wcGo.transform.localPosition = pos[i];
                var wc = wcGo.AddComponent<WheelCollider>();
                wc.radius = 0.36f;
                wc.suspensionDistance = 0.22f;
                wc.mass = 20f;

                var pivot = new GameObject("Wheel_" + names[i]).transform;
                pivot.SetParent(visual, false);
                pivot.localPosition = pos[i] - new Vector3(0f, 0.11f, 0f);
                Prim(PrimitiveType.Cylinder, "Tire", pivot, Vector3.zero, new Vector3(0.72f, 0.13f, 0.72f), Mats["Tire"], false, new Vector3(0f, 0f, 90f));
                float side = i % 2 == 0 ? -1f : 1f;
                Prim(PrimitiveType.Cylinder, "Rim", pivot, new Vector3(0.02f * side, 0f, 0f), new Vector3(0.46f, 0.135f, 0.46f), Mats["Rim"], false, new Vector3(0f, 0f, 90f));
                Prim(PrimitiveType.Cube, "Spoke", pivot, new Vector3(0.14f * side, 0f, 0f), new Vector3(0.02f, 0.4f, 0.08f), Mats["CarTrim"]);

                wheels[i] = new WheelSetup { collider = wc, visual = pivot, front = i < 2, left = i % 2 == 0 };
            }

            root.AddComponent<VehicleInput>();
            var detector = root.AddComponent<SurfaceDetector>();
            detector.defaultSurface = Surfaces[SurfaceKind.Asphalt];
            detector.terrainLayers = new[]
            {
                new SurfaceDetector.TerrainLayerMapping { layer = Layers["Grass"], surface = Surfaces[SurfaceKind.Grass] },
                new SurfaceDetector.TerrainLayerMapping { layer = Layers["Mud"], surface = Surfaces[SurfaceKind.Mud] },
                new SurfaceDetector.TerrainLayerMapping { layer = Layers["Sand"], surface = Surfaces[SurfaceKind.Sand] },
                new SurfaceDetector.TerrainLayerMapping { layer = Layers["Dirt"], surface = Surfaces[SurfaceKind.Gravel] },
                new SurfaceDetector.TerrainLayerMapping { layer = Layers["Rocks"], surface = Surfaces[SurfaceKind.Rocks] },
                new SurfaceDetector.TerrainLayerMapping { layer = Layers["Asphalt"], surface = Surfaces[SurfaceKind.Asphalt] },
            };
            var damage = root.AddComponent<DamageSystem>();
            var controller = root.AddComponent<VehicleController>();
            controller.wheels = wheels;
            controller.input = root.GetComponent<VehicleInput>();
            controller.surfaceDetector = detector;
            controller.damage = damage;
            controller.bodyRenderers = new[] { body.GetComponent<Renderer>(), cabin.GetComponent<Renderer>() };
            controller.brakeLights = brakeLights;
            controller.reverseLights = reverseLights;

            var audioGo = new GameObject("EngineAudio");
            audioGo.transform.SetParent(root.transform, false);
            audioGo.AddComponent<AudioSource>();
            var engineAudio = audioGo.AddComponent<EngineAudioController>();
            engineAudio.vehicle = controller;
            engineAudio.damage = damage;

            return SavePrefab(root, PrefabDir + "/Vehicles/Vehicle_Placeholder.prefab");
        }

        // ================= Данные машин =================

        private static VehicleData Vehicle(string id, string displayName, string description, DriveType drive,
            Color color, int price, int level, float mass, float torque, float topSpeed, float offroad,
            float suspension, float spring, float damper, float durability, int sSpeed, int sAccel, int sOff, int sDur,
            float fwdStiff, float sideStiff, float comY, float antiRoll, float downforce, GameObject prefab)
        {
            var v = LoadOrCreate<VehicleData>(VehDir + "/Vehicle_" + id + ".asset");
            v.id = id;
            v.displayName = displayName;
            v.description = description;
            v.drive = drive;
            v.bodyColor = color;
            v.price = price;
            v.requiredLevel = level;
            v.mass = mass;
            v.maxMotorTorque = torque;
            v.maxBrakeTorque = Mathf.Lerp(4000f, 7500f, Mathf.InverseLerp(1200f, 2500f, mass));
            v.handbrakeTorque = 9000f;
            v.topSpeedKmh = topSpeed;
            v.offroadAbility = offroad;
            v.suspensionDistance = suspension;
            v.spring = spring;
            v.damper = damper;
            v.durability = durability;
            v.statSpeed = sSpeed;
            v.statAcceleration = sAccel;
            v.statOffroad = sOff;
            v.statDurability = sDur;
            v.forwardStiffness = fwdStiff;
            v.sidewaysStiffness = sideStiff;
            v.centerOfMassOffset = new Vector3(0f, comY, 0.05f);
            v.antiRollForce = antiRoll;
            v.downforce = downforce;
            v.wheelRadius = 0.36f;
            v.prefab = prefab;
            v.repairFullCost = 200 + price / 20;
            v.upgradeBaseCost = 300 + price / 15;
            EditorUtility.SetDirty(v);
            return v;
        }

        private static VehicleData[] CreateVehicleData(GameObject prefab)
        {
            return new[]
            {
                Vehicle("sedan", "Городской седан", "Послушный и экономный. Хорош в городе, на бездорожье буксует.",
                    DriveType.FWD, new Color(0.78f, 0.79f, 0.82f), 0, 1, 1350f, 2400f, 185f, 0.15f,
                    0.18f, 32000f, 4000f, 100f, 6, 5, 2, 5, 1.4f, 1.6f, -0.45f, 6000f, 40f, prefab),
                Vehicle("suv", "Внедорожник", "Большой современный внедорожник: полный привод, высокая подвеска, прочный кузов.",
                    DriveType.AWD, new Color(0.42f, 0.05f, 0.12f), 2500, 2, 2300f, 4300f, 190f, 0.85f,
                    0.3f, 46000f, 6000f, 160f, 6, 6, 9, 8, 1.5f, 1.6f, -0.65f, 9000f, 45f, prefab),
                Vehicle("pickup", "Пикап", "Тяжёлый и неубиваемый. Медленно разгоняется, зато везде проедет.",
                    DriveType.AWD, new Color(0.93f, 0.93f, 0.9f), 3500, 3, 2400f, 3600f, 170f, 0.7f,
                    0.3f, 50000f, 6500f, 200f, 5, 3, 8, 10, 1.45f, 1.55f, -0.6f, 9000f, 35f, prefab),
                Vehicle("rally", "Ралли-кар", "Универсал: быстрый на гравии и уверенный на асфальте.",
                    DriveType.AWD, new Color(0.12f, 0.35f, 0.85f), 4500, 3, 1250f, 3300f, 210f, 0.6f,
                    0.25f, 38000f, 4500f, 100f, 8, 9, 7, 5, 1.6f, 1.75f, -0.5f, 7000f, 60f, prefab),
                Vehicle("sport", "Спортивный автомобиль", "Лучший на трассе. Низкий, быстрый и очень нежный к бездорожью.",
                    DriveType.RWD, new Color(0.85f, 0.08f, 0.08f), 6000, 4, 1300f, 3700f, 250f, 0.05f,
                    0.12f, 45000f, 5000f, 70f, 10, 9, 1, 3, 1.7f, 1.9f, -0.4f, 8000f, 90f, prefab),
            };
        }

        // ================= AI-машина =================

        private static GameObject CreateAIPrefab()
        {
            var root = new GameObject("AI_Car");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = 1500f;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.95f, 0f);
            col.size = new Vector3(1.8f, 1.3f, 4.3f);

            GameObject body = Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.8f, 0f), new Vector3(1.8f, 0.7f, 4.3f), Mats["AIBody"]);
            GameObject cabin = Prim(PrimitiveType.Cube, "Cabin", root.transform, new Vector3(0f, 1.42f, -0.2f), new Vector3(1.6f, 0.55f, 2.2f), Mats["AIBody"]);
            Prim(PrimitiveType.Cube, "Glass", root.transform, new Vector3(0f, 1.42f, -0.2f), new Vector3(1.62f, 0.42f, 2.0f), Mats["Glass"]);
            Prim(PrimitiveType.Cube, "TailBar", root.transform, new Vector3(0f, 0.95f, -2.16f), new Vector3(1.6f, 0.1f, 0.04f), Mats["BrakeLight"]);
            Prim(PrimitiveType.Cube, "Headlights", root.transform, new Vector3(0f, 0.9f, 2.16f), new Vector3(1.5f, 0.1f, 0.04f), Mats["Headlight"]);

            var wheelPivots = new List<Transform>();
            Vector3[] pos =
            {
                new Vector3(-0.82f, 0.36f, 1.35f), new Vector3(0.82f, 0.36f, 1.35f),
                new Vector3(-0.82f, 0.36f, -1.35f), new Vector3(0.82f, 0.36f, -1.35f)
            };
            foreach (var p in pos)
            {
                var pivot = new GameObject("Wheel").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = p;
                Prim(PrimitiveType.Cylinder, "Tire", pivot, Vector3.zero, new Vector3(0.7f, 0.12f, 0.7f), Mats["Tire"], false, new Vector3(0f, 0f, 90f));
                Prim(PrimitiveType.Cube, "Spoke", pivot, new Vector3(0.13f * Mathf.Sign(p.x), 0f, 0f), new Vector3(0.02f, 0.4f, 0.08f), Mats["Rim"]);
                wheelPivots.Add(pivot);
            }

            var ai = root.AddComponent<AITrafficCar>();
            ai.bodyRenderers = new[] { body.GetComponent<Renderer>(), cabin.GetComponent<Renderer>() };
            ai.wheelVisuals = wheelPivots.ToArray();
            ai.wheelRadius = 0.35f;
            return SavePrefab(root, PrefabDir + "/AI/AI_Car.prefab");
        }

        // ================= Чекпоинт =================

        private static GameObject CreateCheckpointPrefab()
        {
            var root = new GameObject("Checkpoint");
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 3f, 0f);
            trigger.size = new Vector3(13f, 6f, 2f);

            Material m = Mats["Checkpoint"];
            var renderers = new List<Renderer>
            {
                Prim(PrimitiveType.Cube, "PostL", root.transform, new Vector3(-6.5f, 3f, 0f), new Vector3(0.45f, 6f, 0.45f), m).GetComponent<Renderer>(),
                Prim(PrimitiveType.Cube, "PostR", root.transform, new Vector3(6.5f, 3f, 0f), new Vector3(0.45f, 6f, 0.45f), m).GetComponent<Renderer>(),
                Prim(PrimitiveType.Cube, "Top", root.transform, new Vector3(0f, 6f, 0f), new Vector3(13.4f, 0.45f, 0.45f), m).GetComponent<Renderer>(),
            };
            GameObject marker = Prim(PrimitiveType.Cylinder, "Beacon", root.transform, new Vector3(0f, 16f, 0f), new Vector3(0.5f, 9f, 0.5f), m);
            renderers.Add(marker.GetComponent<Renderer>());

            var respawn = new GameObject("Respawn").transform;
            respawn.SetParent(root.transform, false);
            respawn.localPosition = new Vector3(0f, 0.5f, 4f);

            var cp = root.AddComponent<Checkpoint>();
            cp.tintRenderers = renderers.ToArray();
            cp.activeMarker = marker;
            cp.respawnPoint = respawn;
            return SavePrefab(root, PrefabDir + "/Checkpoints/Checkpoint.prefab");
        }

        // ================= Системы игры =================

        private static GameObject CreateGameSystemsPrefab(VehicleData[] vehicles)
        {
            var root = new GameObject("GameSystems");
            root.AddComponent<PlatformManager>();
            var gm = root.AddComponent<GameManager>();
            root.AddComponent<GraphicsSettingsManager>();
            gm.vehicles = vehicles;
            gm.zones = new[]
            {
                new ZoneInfo { sceneName = "TestTrack", displayName = "Тестовый полигон", unlockLevel = 1,
                    description = "Все покрытия, трамплины и слалом",
                    modes = new[] { GameMode.FreeRide, GameMode.TimeTrial, GameMode.CheckpointRace } },
                new ZoneInfo { sceneName = "Highway", displayName = "Трасса", unlockLevel = 1,
                    description = "Длинное шоссе с плавными поворотами и трафиком",
                    modes = new[] { GameMode.HighwayRun, GameMode.TimeTrial, GameMode.CheckpointRace, GameMode.FreeRide } },
                new ZoneInfo { sceneName = "City", displayName = "Город", unlockLevel = 2,
                    description = "Кварталы, перекрёстки, бордюры и машины",
                    modes = new[] { GameMode.CityChallenge, GameMode.CheckpointRace, GameMode.TimeTrial, GameMode.FreeRide } },
                new ZoneInfo { sceneName = "Forest", displayName = "Лес", unlockLevel = 2,
                    description = "Узкая гравийка между деревьями",
                    modes = new[] { GameMode.TimeTrial, GameMode.CheckpointRace, GameMode.FreeRide } },
                new ZoneInfo { sceneName = "Offroad", displayName = "Бездорожье", unlockLevel = 3,
                    description = "Холмы, грязь, песок, камни и брод",
                    modes = new[] { GameMode.OffroadChallenge, GameMode.TimeTrial, GameMode.FreeRide } },
                new ZoneInfo { sceneName = "OpenWorld", displayName = "Открытый мир", unlockLevel = 4,
                    description = "Большая карта со всеми типами местности",
                    modes = new[] { GameMode.FreeRide } },
            };

            // EventSystem живёт вместе с системами игры (одна на всю игру)
            var es = new GameObject("EventSystem");
            es.transform.SetParent(root.transform, false);
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            BuildLoadingOverlay(root.transform, gm);
            return SavePrefab(root, ResDir + "/GameSystems.prefab");
        }

        // ================= Build Settings и Player Settings =================

        private static readonly string[] SceneOrder =
            { "Boot", "MainMenu", "Garage", "TestTrack", "Highway", "City", "Forest", "Offroad", "OpenWorld" };

        private static void SetBuildScenes()
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (string s in SceneOrder)
            {
                string path = SceneDir + "/" + s + ".unity";
                if (File.Exists(FullPath(path))) list.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "VinCoder";
            PlayerSettings.productName = "Terrain Drive";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.vincoder.terraindrive");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.vincoder.terraindrive");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        }
    }
}
#endif
