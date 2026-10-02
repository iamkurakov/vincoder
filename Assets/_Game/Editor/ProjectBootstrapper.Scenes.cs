#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Random = System.Random;

namespace TerrainDrive.EditorTools
{
    /// <summary>Часть автосборки: сцены.</summary>
    public static partial class ProjectBootstrapper
    {
        private class CheckpointSpec
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        private class SceneCtx
        {
            public string name;
            public Scene scene;
            public Transform world;
            public readonly List<CheckpointSpec> checkpoints = new List<CheckpointSpec>();
            public readonly List<Vector3> aiPath = new List<Vector3>();
            public bool aiLoop;
            public bool aiTwoWay = true;
            public float[] laneOffsets = { 2f };
            public int aiMax = 8;
            public Vector3 spawnPosition;
            public Quaternion spawnRotation = Quaternion.identity;
            public Vector3? repairPosition;
            public GameMode defaultMode = GameMode.FreeRide;
            public readonly List<ModeRules> rules = new List<ModeRules>();
            public Terrain terrain;
        }

        // ================= Общие помощники сцен =================

        private static Scene NewLitScene(Color fogColor, float fogStart = 150f, float fogEnd = 450f)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = light;

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.63f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.47f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.22f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            return scene;
        }

        private static SceneCtx BeginGameplay(string name)
        {
            var c = new SceneCtx { name = name };
            c.scene = NewLitScene(new Color(0.72f, 0.79f, 0.86f));
            c.world = new GameObject("World").transform;
            return c;
        }

        private static void MarkStatic(GameObject go, bool occluder = false)
        {
            StaticEditorFlags flags = StaticEditorFlags.BatchingStatic;
            if (occluder) flags |= StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
        }

        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation,
            string mat, SurfaceKind? surface = null, bool collider = true, bool isStatic = true, bool occluder = false)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Mats[mat];
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            if (surface.HasValue) go.AddComponent<SurfaceType>().surface = Surfaces[surface.Value];
            if (isStatic) MarkStatic(go, occluder);
            return go;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Vector3 RightOf(Vector3 forward)
        {
            Vector3 f = Flat(forward).normalized;
            return Vector3.Cross(Vector3.up, f);
        }

        private static float DistToPolyline(Vector3 p, List<Vector3> line, bool closed = false)
        {
            float best = float.MaxValue;
            int n = closed ? line.Count : line.Count - 1;
            Vector2 q = new Vector2(p.x, p.z);
            for (int i = 0; i < n; i++)
            {
                Vector3 a3 = line[i];
                Vector3 b3 = line[(i + 1) % line.Count];
                Vector2 a = new Vector2(a3.x, a3.z);
                Vector2 b = new Vector2(b3.x, b3.z);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                float d = Vector2.Distance(q, a + ab * t);
                if (d < best) best = d;
            }
            return best;
        }

        private static float Rand(Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);

        /// <summary>Дорога из прямоугольных сегментов по ломаной линии.</summary>
        private static void Road(SceneCtx c, List<Vector3> pts, float width, string mat, SurfaceKind kind,
            bool closed, bool centerLine, bool barriers, float thickness = 0.4f)
        {
            Transform parent = new GameObject("Road_" + kind).transform;
            parent.SetParent(c.world, false);
            int n = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = pts[i];
                Vector3 b = pts[(i + 1) % pts.Count];
                Vector3 dir = b - a;
                float len = dir.magnitude;
                if (len < 0.01f) continue;
                Quaternion rot = Quaternion.LookRotation(dir / len, Vector3.up);
                Vector3 mid = (a + b) * 0.5f;
                Vector3 up = rot * Vector3.up;
                Box(parent, "Seg" + i, mid - up * (thickness * 0.5f), new Vector3(width, thickness, len + width * 0.25f), rot, mat, kind);

                if (centerLine && i % 2 == 0)
                    Box(parent, "Line" + i, mid + up * 0.012f, new Vector3(0.18f, 0.02f, len * 0.5f), rot, "RoadLine", null, false);

                if (barriers)
                {
                    Vector3 right = rot * Vector3.right;
                    float off = width * 0.5f + 0.6f;
                    Box(parent, "BarrierL" + i, mid - right * off + up * 0.4f, new Vector3(0.3f, 0.8f, len + 0.4f), rot, "Barrier");
                    Box(parent, "BarrierR" + i, mid + right * off + up * 0.4f, new Vector3(0.3f, 0.8f, len + 0.4f), rot, "Barrier");
                }
            }
        }

        private static void Tree(Transform parent, Vector3 pos, float scale, Random r)
        {
            var root = new GameObject("Tree");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            GameObject trunk = Prim(PrimitiveType.Cylinder, "Trunk", root.transform, new Vector3(0f, 2f * scale, 0f),
                new Vector3(0.45f * scale, 2f * scale, 0.45f * scale), Mats["Trunk"], true);
            GameObject crown = Prim(PrimitiveType.Sphere, "Crown", root.transform, new Vector3(0f, 5.2f * scale, 0f),
                new Vector3(3.6f, 4.6f, 3.6f) * scale, Mats[r.NextDouble() > 0.5 ? "Leaves" : "LeavesDark"]);
            MarkStatic(trunk);
            MarkStatic(crown);
        }

        private static void Rock(Transform parent, Vector3 pos, float scale, Random r)
        {
            GameObject rock = Prim(PrimitiveType.Sphere, "Rock", parent, pos,
                new Vector3(scale * Rand(r, 0.8f, 1.3f), scale * Rand(r, 0.5f, 0.8f), scale * Rand(r, 0.8f, 1.3f)), Mats["Rocks"], true,
                new Vector3(Rand(r, 0f, 30f), Rand(r, 0f, 360f), 0f));
            rock.AddComponent<SurfaceType>().surface = Surfaces[SurfaceKind.Rocks];
            MarkStatic(rock);
        }

        private static void Log(Transform parent, Vector3 pos, float yaw, Random r)
        {
            GameObject log = Prim(PrimitiveType.Cylinder, "Log", parent, pos + Vector3.up * 0.3f,
                new Vector3(0.6f, Rand(r, 2.5f, 4f), 0.6f), Mats["Trunk"], true, new Vector3(0f, yaw, 90f));
            MarkStatic(log);
        }

        private static void Cone(Transform parent, Vector3 pos)
        {
            GameObject cone = Prim(PrimitiveType.Cylinder, "Cone", parent, pos + Vector3.up * 0.4f,
                new Vector3(0.45f, 0.4f, 0.45f), Mats["Cone"], true);
            var rb = cone.AddComponent<Rigidbody>();
            rb.mass = 8f;
        }

        private static void AddCheckpoint(SceneCtx c, Vector3 pos, Vector3 forward)
        {
            Vector3 f = Flat(forward);
            if (f.sqrMagnitude < 0.001f) f = Vector3.forward;
            c.checkpoints.Add(new CheckpointSpec { position = pos, rotation = Quaternion.LookRotation(f.normalized, Vector3.up) });
        }

        private static void CheckpointsAlong(SceneCtx c, List<Vector3> pts, int start, int step, bool includeLast)
        {
            for (int i = start; i < pts.Count; i += step)
            {
                Vector3 fwd = i + 1 < pts.Count ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
                AddCheckpoint(c, pts[i], fwd);
            }
            int last = pts.Count - 1;
            if (includeLast && (last - start) % step != 0)
                AddCheckpoint(c, pts[last], pts[last] - pts[last - 1]);
        }

        private static ModeRules Rules(GameMode mode, float gold, float silver, float bronze, float timeLimit = -1f, float bonus = -1f)
        {
            ModeRules r = GameModeManager.DefaultRules(mode);
            r.goldTime = gold;
            r.silverTime = silver;
            r.bronzeTime = bronze;
            if (timeLimit >= 0f) r.timeLimit = timeLimit;
            if (bonus >= 0f) r.timeBonusPerCheckpoint = bonus;
            return r;
        }

        private static float GroundY(SceneCtx c, Vector3 p) =>
            c.terrain != null ? c.terrain.SampleHeight(p) + c.terrain.transform.position.y : p.y;

        // ================= Завершение игровой сцены =================

        private static void FinishGameplay(SceneCtx c)
        {
            // Точка старта
            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetPositionAndRotation(c.spawnPosition, c.spawnRotation);

            // Чекпоинты
            var cpRoot = new GameObject("Checkpoints");
            var race = cpRoot.AddComponent<CheckpointRaceManager>();
            for (int i = 0; i < c.checkpoints.Count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(checkpointPrefab);
                go.name = "Checkpoint_" + (i + 1).ToString("00");
                go.transform.SetParent(cpRoot.transform, false);
                Vector3 p = c.checkpoints[i].position;
                p.y = GroundY(c, p);
                go.transform.SetPositionAndRotation(p, c.checkpoints[i].rotation);
                var cp = go.GetComponent<Checkpoint>();
                cp.index = i;
                race.checkpoints.Add(cp);
            }

            // Трафик
            AITrafficSpawner traffic = null;
            if (c.aiPath.Count > 1)
            {
                var trafficGo = new GameObject("Traffic");
                traffic = trafficGo.AddComponent<AITrafficSpawner>();
                var paths = new List<WaypointPath> { MakePath(c, "Path_Forward", c.aiPath, trafficGo.transform) };
                if (c.aiTwoWay)
                {
                    var reversed = new List<Vector3>(c.aiPath);
                    reversed.Reverse();
                    paths.Add(MakePath(c, "Path_Backward", reversed, trafficGo.transform));
                }
                traffic.paths = paths.ToArray();
                traffic.carPrefabs = new[] { aiPrefab.GetComponent<AITrafficCar>() };
                traffic.maxCars = c.aiMax;
                traffic.laneOffsets = c.laneOffsets;
            }

            // Точка ремонта
            if (c.repairPosition.HasValue)
            {
                Vector3 rp = c.repairPosition.Value;
                rp.y = GroundY(c, rp);
                var repair = new GameObject("RepairPoint");
                repair.transform.position = rp;
                var trig = repair.AddComponent<BoxCollider>();
                trig.isTrigger = true;
                trig.center = new Vector3(0f, 1.5f, 0f);
                trig.size = new Vector3(9f, 3f, 9f);
                repair.AddComponent<RepairPoint>();
                Prim(PrimitiveType.Cube, "Pad", repair.transform, new Vector3(0f, 0.05f, 0f), new Vector3(9f, 0.06f, 9f), Mats["Repair"]);
                Prim(PrimitiveType.Cube, "SignV", repair.transform, new Vector3(0f, 6f, 0f), new Vector3(0.6f, 2.4f, 0.6f), Mats["Repair"]);
                Prim(PrimitiveType.Cube, "SignH", repair.transform, new Vector3(0f, 6f, 0f), new Vector3(2.4f, 0.6f, 0.6f), Mats["Repair"]);
            }

            // Камера
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 600f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            var camCtrl = camGo.AddComponent<VehicleCameraController>();
            camGo.transform.position = c.spawnPosition - (c.spawnRotation * Vector3.forward) * 7f + Vector3.up * 3f;
            camGo.transform.LookAt(c.spawnPosition);

            // Звуки окружения (клипы назначаются вручную)
            var ambience = new GameObject("Ambience");
            ambience.AddComponent<AudioSource>();
            ambience.AddComponent<AmbientAudioZone>();

            // Интерфейс
            HudRefs refs = BuildGameplayHud();

            // Режим игры
            var modeGo = new GameObject("GameModeManager");
            var gmm = modeGo.AddComponent<GameModeManager>();
            gmm.spawnPoint = spawn;
            gmm.checkpointRace = race;
            gmm.cameraController = camCtrl;
            gmm.hud = refs.hud;
            gmm.pauseMenu = refs.pause;
            gmm.traffic = traffic;
            gmm.defaultMode = c.defaultMode;
            gmm.fallbackVehicle = vehicleAssets[0];
            gmm.rules = new List<ModeRules>(c.rules);

            EditorSceneManager.SaveScene(c.scene, SceneDir + "/" + c.name + ".unity");
        }

        private static WaypointPath MakePath(SceneCtx c, string name, List<Vector3> pts, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var path = go.AddComponent<WaypointPath>();
            path.loop = c.aiLoop;
            for (int i = 0; i < pts.Count; i++)
            {
                var wp = new GameObject("WP" + i.ToString("000")).transform;
                wp.SetParent(go.transform, false);
                Vector3 p = pts[i];
                p.y = GroundY(c, p);
                wp.position = p;
                path.points.Add(wp);
            }
            return path;
        }

        // ================= Boot, MainMenu, Garage =================

        private static void CreateBootScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(systemsPrefab);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.2f, 0.25f);
            EditorSceneManager.SaveScene(scene, SceneDir + "/Boot.unity");
        }

        private static void CreateMainMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.2f, 0.25f);
            BuildMainMenuUI();
            EditorSceneManager.SaveScene(scene, SceneDir + "/MainMenu.unity");
        }

        private static void CreateGarageScene()
        {
            Scene scene = NewLitScene(new Color(0.1f, 0.12f, 0.14f), 40f, 120f);
            RenderSettings.fog = false;
            var world = new GameObject("World").transform;
            Box(world, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), Quaternion.identity, "Turntable");
            Box(world, "Wall", new Vector3(0f, 6f, 12f), new Vector3(60f, 13f, 1f), Quaternion.identity, "BuildingB");
            Prim(PrimitiveType.Cylinder, "Turntable", world, new Vector3(0f, 0.04f, 0f), new Vector3(8f, 0.04f, 8f), Mats["Rim"]);
            var spawn = new GameObject("PreviewSpawn").transform;
            spawn.SetPositionAndRotation(new Vector3(0f, 0.1f, 0f), Quaternion.Euler(0f, 150f, 0f));

            var lamp = new GameObject("KeyLight").AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.range = 25f;
            lamp.spotAngle = 70f;
            lamp.intensity = 40f;
            lamp.transform.SetPositionAndRotation(new Vector3(2f, 8f, -4f), Quaternion.LookRotation(new Vector3(-2f, -8f, 4f)));

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 2.4f, -9f);
            camGo.transform.LookAt(new Vector3(0f, 0.9f, 0f));

            BuildGarageUI(spawn);
            EditorSceneManager.SaveScene(scene, SceneDir + "/Garage.unity");
        }

        // ================= TestTrack: полигон =================

        private static void CreateTestTrackScene()
        {
            SceneCtx c = BeginGameplay("TestTrack");
            var r = new Random(1);
            Box(c.world, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(520f, 1f, 520f), Quaternion.identity, "Asphalt", SurfaceKind.Asphalt);

            // Полосы всех покрытий
            (SurfaceKind kind, string mat)[] patches =
            {
                (SurfaceKind.Gravel, "Gravel"), (SurfaceKind.Mud, "Mud"), (SurfaceKind.Grass, "Grass"), (SurfaceKind.Sand, "Sand"),
                (SurfaceKind.ForestSoil, "ForestSoil"), (SurfaceKind.Rocks, "Rocks"), (SurfaceKind.Water, "Water"), (SurfaceKind.WetAsphalt, "WetAsphalt")
            };
            for (int i = 0; i < patches.Length; i++)
                Box(c.world, "Patch_" + patches[i].kind, new Vector3(-105f + i * 30f, 0.01f, 60f), new Vector3(26f, 0.04f, 90f),
                    Quaternion.identity, patches[i].mat, patches[i].kind);

            // Трамплины
            Box(c.world, "RampSmall", new Vector3(0f, 1.2f, -60f), new Vector3(10f, 0.6f, 16f), Quaternion.Euler(-10f, 0f, 0f), "Ramp", SurfaceKind.Asphalt);
            Box(c.world, "RampBig", new Vector3(40f, 1.85f, -60f), new Vector3(10f, 0.6f, 16f), Quaternion.Euler(-15f, 0f, 0f), "Ramp", SurfaceKind.Asphalt);

            // Лежачие полицейские
            for (int i = 0; i < 6; i++)
            {
                GameObject bump = Prim(PrimitiveType.Cylinder, "SpeedBump", c.world, new Vector3(-60f, 0f, -140f + i * 14f),
                    new Vector3(0.8f, 4f, 0.8f), Mats["Ramp"], true, new Vector3(0f, 0f, 90f));
                MarkStatic(bump);
            }

            // Слалом из конусов
            for (int i = 0; i < 12; i++) Cone(c.world, new Vector3(60f + (i % 2 == 0 ? -3f : 3f), 0f, -130f + i * 10f));

            // Кольцевой маршрут чекпоинтов
            const float radius = 190f;
            for (int i = 1; i <= 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                Vector3 p = new Vector3(radius * Mathf.Cos(a), 0f, radius * Mathf.Sin(a));
                AddCheckpoint(c, p, new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
            }
            c.spawnPosition = new Vector3(radius, 0.6f, -12f);
            c.spawnRotation = Quaternion.LookRotation(Vector3.forward);
            c.repairPosition = new Vector3(-60f, 0f, -60f);

            // Немного деревьев по краю
            for (int i = 0; i < 60; i++)
            {
                float a = Rand(r, 0f, Mathf.PI * 2f);
                float d = Rand(r, 220f, 250f);
                Tree(c.world, new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), Rand(r, 0.9f, 1.3f), r);
            }
            // Ограждение по периметру
            Box(c.world, "WallN", new Vector3(0f, 1f, 259f), new Vector3(520f, 2f, 1f), Quaternion.identity, "Barrier");
            Box(c.world, "WallS", new Vector3(0f, 1f, -259f), new Vector3(520f, 2f, 1f), Quaternion.identity, "Barrier");
            Box(c.world, "WallE", new Vector3(259f, 1f, 0f), new Vector3(1f, 2f, 520f), Quaternion.identity, "Barrier");
            Box(c.world, "WallW", new Vector3(-259f, 1f, 0f), new Vector3(1f, 2f, 520f), Quaternion.identity, "Barrier");

            c.defaultMode = GameMode.FreeRide;
            c.rules.Add(Rules(GameMode.TimeTrial, 42f, 52f, 70f));
            c.rules.Add(Rules(GameMode.CheckpointRace, 42f, 52f, 70f, 15f, 5f));
            c.rules.Add(GameModeManager.DefaultRules(GameMode.FreeRide));
            FinishGameplay(c);
        }

        // ================= Highway: трасса =================

        private static void CreateHighwayScene()
        {
            SceneCtx c = BeginGameplay("Highway");
            var r = new Random(2);
            var pts = new List<Vector3>();
            for (int i = 0; i <= 100; i++)
            {
                float z = i * 40f - 2000f;
                float x = 90f * Mathf.Sin(i * 0.07f) + 25f * Mathf.Sin(i * 0.19f);
                pts.Add(new Vector3(x, 0.03f, z));
            }
            Box(c.world, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(900f, 1f, 4300f), Quaternion.identity, "Grass", SurfaceKind.Grass);
            Road(c, pts, 16f, "Asphalt", SurfaceKind.Asphalt, false, true, true);

            // Фонари
            for (int i = 0; i < pts.Count - 1; i += 5)
            {
                Vector3 right = RightOf(pts[i + 1] - pts[i]);
                GameObject pole = Prim(PrimitiveType.Cylinder, "Lamp", c.world, pts[i] + right * 9.6f + Vector3.up * 4f,
                    new Vector3(0.2f, 4f, 0.2f), Mats["Pole"], true);
                MarkStatic(pole);
            }
            // Деревья и холмы вдали
            for (int i = 0; i < 220; i++)
            {
                int idx = r.Next(0, pts.Count);
                float side = r.NextDouble() > 0.5 ? 1f : -1f;
                Vector3 p = pts[idx] + new Vector3(side * Rand(r, 28f, 400f), 0f, Rand(r, -20f, 20f));
                if (DistToPolyline(p, pts) < 25f) continue;
                Tree(c.world, new Vector3(p.x, 0f, p.z), Rand(r, 0.9f, 1.5f), r);
            }
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = new Vector3((i % 2 == 0 ? -1f : 1f) * Rand(r, 250f, 420f), -6f, Rand(r, -2000f, 2000f));
                Box(c.world, "Hill" + i, p, new Vector3(Rand(r, 80f, 160f), 20f, Rand(r, 120f, 260f)), Quaternion.Euler(0f, Rand(r, 0f, 90f), 8f), "Grass", SurfaceKind.Grass);
            }

            CheckpointsAlong(c, pts, 10, 10, true);
            Vector3 startRight = RightOf(pts[2] - pts[1]);
            c.spawnPosition = pts[1] + startRight * 4f + Vector3.up * 0.6f;
            c.spawnRotation = Quaternion.LookRotation(Flat(pts[2] - pts[1]).normalized);
            c.repairPosition = pts[4] + RightOf(pts[5] - pts[4]) * 16f;

            c.aiPath.AddRange(pts);
            c.aiLoop = false;
            c.aiTwoWay = true;
            c.laneOffsets = new[] { 2.2f, 5.8f };
            c.aiMax = 12;

            c.defaultMode = GameMode.HighwayRun;
            c.rules.Add(GameModeManager.DefaultRules(GameMode.HighwayRun));
            c.rules.Add(Rules(GameMode.TimeTrial, 105f, 130f, 170f));
            c.rules.Add(Rules(GameMode.CheckpointRace, 105f, 130f, 170f, 35f, 12f));
            ModeRules free = GameModeManager.DefaultRules(GameMode.FreeRide);
            free.useCheckpoints = false;
            c.rules.Add(free);
            FinishGameplay(c);
        }

        // ================= City: город =================

        private static void CreateCityScene()
        {
            SceneCtx c = BeginGameplay("City");
            var r = new Random(3);
            const int lines = 6;
            const float spacing = 70f;
            const float roadW = 14f;
            Func<int, float> L = k => -175f + k * spacing;

            Box(c.world, "Asphalt", new Vector3(0f, -0.5f, 0f), new Vector3(420f, 1f, 420f), Quaternion.identity, "Asphalt", SurfaceKind.Asphalt);
            Box(c.world, "Outskirts", new Vector3(0f, -0.52f, 0f), new Vector3(1000f, 1f, 1000f), Quaternion.identity, "Grass", SurfaceKind.Grass);

            // Разметка по осям улиц
            for (int k = 0; k < lines; k++)
            {
                for (int s = 0; s < 21; s++)
                {
                    float t = -175f + s * 17.5f;
                    Box(c.world, "LineX", new Vector3(t, 0.012f, L(k)), new Vector3(8f, 0.02f, 0.18f), Quaternion.identity, "RoadLine", null, false);
                    Box(c.world, "LineZ", new Vector3(L(k), 0.012f, t), new Vector3(0.18f, 0.02f, 8f), Quaternion.identity, "RoadLine", null, false);
                }
            }

            // Кварталы
            string[] buildingMats = { "BuildingA", "BuildingB", "BuildingC" };
            float block = spacing - roadW;
            Vector3 parkingCenter = Vector3.zero;
            for (int bx = 0; bx < lines - 1; bx++)
            for (int bz = 0; bz < lines - 1; bz++)
            {
                Vector3 center = new Vector3((L(bx) + L(bx + 1)) * 0.5f, 0f, (L(bz) + L(bz + 1)) * 0.5f);
                if (bx == 2 && bz == 2)
                {
                    // Парковка с точкой ремонта
                    parkingCenter = center;
                    for (int i = 0; i < 6; i++)
                        Box(c.world, "ParkedCar", center + new Vector3(-20f + i * 8f, 0.7f, 18f), new Vector3(1.8f, 1.4f, 4.2f), Quaternion.identity, "AIBody");
                    continue;
                }
                Box(c.world, "Sidewalk", center + Vector3.up * 0.1f, new Vector3(block, 0.2f, block), Quaternion.identity, "Curb", SurfaceKind.Asphalt);
                for (int q = 0; q < 4; q++)
                {
                    if (r.NextDouble() < 0.15) // сквер
                    {
                        Vector3 sq = center + new Vector3((q % 2 == 0 ? -1f : 1f) * 13f, 0.2f, (q < 2 ? -1f : 1f) * 13f);
                        Tree(c.world, sq, 1f, r);
                        continue;
                    }
                    float h = Rand(r, 10f, 48f);
                    float w = Rand(r, 16f, 22f);
                    float d = Rand(r, 16f, 22f);
                    Vector3 pos = center + new Vector3((q % 2 == 0 ? -1f : 1f) * 13f, 0.2f + h * 0.5f, (q < 2 ? -1f : 1f) * 13f);
                    Box(c.world, "Building", pos, new Vector3(w, h, d), Quaternion.identity, buildingMats[r.Next(0, buildingMats.Length)], null, true, true, true);
                }
            }

            // Припаркованные машины вдоль улиц и лужи
            for (int i = 0; i < 24; i++)
            {
                int k = r.Next(1, lines - 1);
                float along = L(r.Next(0, lines - 1)) + Rand(r, 15f, 55f);
                bool alongX = r.NextDouble() > 0.5;
                float side = r.NextDouble() > 0.5 ? 1f : -1f;
                Vector3 p = alongX ? new Vector3(along, 0.7f, L(k) + side * 5.6f) : new Vector3(L(k) + side * 5.6f, 0.7f, along);
                Box(c.world, "ParkedCar", p, alongX ? new Vector3(4.2f, 1.4f, 1.8f) : new Vector3(1.8f, 1.4f, 4.2f), Quaternion.identity, "AIBody");
            }
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3(L(r.Next(0, lines)), 0.01f, L(r.Next(0, lines - 1)) + Rand(r, 20f, 50f));
                Box(c.world, "Puddle", p, new Vector3(9f, 0.04f, 12f), Quaternion.identity, "WetAsphalt", SurfaceKind.WetAsphalt);
            }
            for (int i = 0; i < 16; i++) Cone(c.world, new Vector3(L(r.Next(1, lines - 1)) + Rand(r, -5f, 5f), 0f, L(r.Next(1, lines - 1)) + Rand(r, 8f, 12f)));

            // Маршрут по перекрёсткам
            Vector2Int[] route =
            {
                new Vector2Int(1, 0), new Vector2Int(3, 0), new Vector2Int(3, 2), new Vector2Int(5, 2), new Vector2Int(5, 5),
                new Vector2Int(2, 5), new Vector2Int(2, 3), new Vector2Int(0, 3), new Vector2Int(0, 1)
            };
            Vector3 prev = new Vector3(L(0), 0f, L(0));
            foreach (var g in route)
            {
                Vector3 p = new Vector3(L(g.x), 0f, L(g.y));
                AddCheckpoint(c, p, p - prev);
                prev = p;
            }
            c.spawnPosition = new Vector3(L(0) + 12f, 0.6f, L(0) - 3.5f);
            c.spawnRotation = Quaternion.LookRotation(Vector3.right);
            c.repairPosition = parkingCenter + new Vector3(0f, 0f, -12f);

            // Трафик по периметру
            for (int k = 0; k < lines; k++) c.aiPath.Add(new Vector3(L(k), 0f, L(0)));
            for (int k = 1; k < lines; k++) c.aiPath.Add(new Vector3(L(lines - 1), 0f, L(k)));
            for (int k = lines - 2; k >= 0; k--) c.aiPath.Add(new Vector3(L(k), 0f, L(lines - 1)));
            for (int k = lines - 2; k >= 1; k--) c.aiPath.Add(new Vector3(L(0), 0f, L(k)));
            c.aiLoop = true;
            c.aiTwoWay = true;
            c.laneOffsets = new[] { 3.5f };
            c.aiMax = 10;

            c.defaultMode = GameMode.CityChallenge;
            c.rules.Add(Rules(GameMode.CityChallenge, 90f, 115f, 150f, 200f));
            c.rules.Add(Rules(GameMode.CheckpointRace, 90f, 115f, 150f, 30f, 14f));
            c.rules.Add(Rules(GameMode.TimeTrial, 90f, 115f, 150f));
            c.rules.Add(GameModeManager.DefaultRules(GameMode.FreeRide));
            FinishGameplay(c);
        }

        // ================= Forest: лес =================

        private static void CreateForestScene()
        {
            SceneCtx c = BeginGameplay("Forest");
            RenderSettings.fogColor = new Color(0.55f, 0.62f, 0.58f);
            var r = new Random(4);
            var pts = new List<Vector3>();
            for (int i = 0; i <= 70; i++)
            {
                float z = -380f + i * 11f;
                float x = 60f * Mathf.Sin(i * 0.12f) + 25f * Mathf.Sin(i * 0.31f);
                pts.Add(new Vector3(x, 0.04f, z));
            }
            Box(c.world, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(700f, 1f, 900f), Quaternion.identity, "ForestSoil", SurfaceKind.ForestSoil);
            Road(c, pts, 7f, "Gravel", SurfaceKind.Gravel, false, false, false, 0.3f);

            // Лужи грязи на дороге
            foreach (int i in new[] { 15, 33, 52 })
            {
                Quaternion rot = Quaternion.LookRotation(Flat(pts[i + 1] - pts[i]).normalized);
                Box(c.world, "MudPuddle", pts[i] + Vector3.up * 0.01f, new Vector3(7.5f, 0.04f, 9f), rot, "Mud", SurfaceKind.Mud);
            }

            Vector3 repair = pts[35] + RightOf(pts[36] - pts[35]) * 9f;
            c.repairPosition = repair;

            for (int i = 0; i < 480; i++)
            {
                Vector3 p = new Vector3(Rand(r, -330f, 330f), 0f, Rand(r, -440f, 440f));
                if (DistToPolyline(p, pts) < 7.5f || Vector3.Distance(Flat(p), Flat(repair)) < 10f) continue;
                Tree(c.world, p, Rand(r, 0.8f, 1.4f), r);
            }
            for (int i = 0; i < 45; i++)
            {
                int idx = r.Next(1, pts.Count - 1);
                Vector3 side = RightOf(pts[idx + 1] - pts[idx]) * (r.NextDouble() > 0.5 ? 1f : -1f);
                Rock(c.world, pts[idx] + side * Rand(r, 5f, 9f), Rand(r, 0.8f, 2.2f), r);
            }
            for (int i = 0; i < 15; i++)
            {
                int idx = r.Next(1, pts.Count - 1);
                Vector3 side = RightOf(pts[idx + 1] - pts[idx]) * (r.NextDouble() > 0.5 ? 1f : -1f);
                Log(c.world, pts[idx] + side * Rand(r, 6f, 12f), Rand(r, 0f, 180f), r);
            }

            CheckpointsAlong(c, pts, 7, 7, true);
            c.spawnPosition = pts[1] + Vector3.up * 0.6f;
            c.spawnRotation = Quaternion.LookRotation(Flat(pts[2] - pts[1]).normalized);

            c.defaultMode = GameMode.TimeTrial;
            c.rules.Add(Rules(GameMode.TimeTrial, 62f, 80f, 110f));
            c.rules.Add(Rules(GameMode.CheckpointRace, 62f, 80f, 110f, 20f, 9f));
            c.rules.Add(GameModeManager.DefaultRules(GameMode.FreeRide));
            FinishGameplay(c);
        }

        // ================= Terrain =================

        private static Terrain CreateTerrain(SceneCtx c, string name, float size, float height,
            Func<float, float, float> heightFn, Func<float, float, float, float, float[]> alphaFn, TerrainLayer[] layers)
        {
            string path = TerrainDir + "/TD_" + name + ".asset";
            AssetDatabase.DeleteAsset(path);

            var td = new TerrainData();
            td.heightmapResolution = 257;
            td.alphamapResolution = 256;
            td.baseMapResolution = 256;
            td.size = new Vector3(size, height, size);
            Vector3 origin = new Vector3(-size * 0.5f, 0f, -size * 0.5f);

            int res = td.heightmapResolution;
            var heights = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = origin.x + x / (float)(res - 1) * size;
                float wz = origin.z + z / (float)(res - 1) * size;
                heights[z, x] = Mathf.Clamp01(heightFn(wx, wz));
            }
            td.SetHeights(0, 0, heights);
            td.terrainLayers = layers;

            int ar = td.alphamapResolution;
            var alpha = new float[ar, ar, layers.Length];
            for (int z = 0; z < ar; z++)
            for (int x = 0; x < ar; x++)
            {
                float nx = (x + 0.5f) / ar;
                float nz = (z + 0.5f) / ar;
                float wx = origin.x + nx * size;
                float wz = origin.z + nz * size;
                float h01 = td.GetInterpolatedHeight(nx, nz) / height;
                float steep = td.GetSteepness(nx, nz);
                float[] w = alphaFn(wx, wz, h01, steep);
                float sum = 0f;
                for (int i = 0; i < layers.Length; i++) sum += w[i];
                if (sum <= 0f)
                {
                    w[0] = 1f;
                    sum = 1f;
                }
                for (int i = 0; i < layers.Length; i++) alpha[z, x, i] = w[i] / sum;
            }
            td.SetAlphamaps(0, 0, alpha);
            AssetDatabase.CreateAsset(td, path);

            GameObject go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain_" + name;
            go.transform.SetParent(c.world, false);
            go.transform.position = origin;
            var terrain = go.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 300f;
            c.terrain = terrain;
            return terrain;
        }

        private static float SmoothBand(float d, float inner, float outer) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, d));

        // ================= Offroad: бездорожье =================

        private static void CreateOffroadScene()
        {
            SceneCtx c = BeginGameplay("Offroad");
            RenderSettings.fogColor = new Color(0.78f, 0.76f, 0.7f);
            var r = new Random(5);
            const float size = 600f;
            const float height = 60f;

            var route = new List<Vector3>();
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                route.Add(new Vector3(-240f + 480f * t + 60f * Mathf.Sin(t * 9f), 0f, -240f + 470f * t + 50f * Mathf.Sin(t * 6f + 1f)));
            }

            Func<float, float, float> Low = (x, z) => Mathf.PerlinNoise(x * 0.006f + 10f, z * 0.006f + 10f);
            Func<float, float, float> High = (x, z) => Mathf.PerlinNoise(x * 0.025f + 3f, z * 0.025f + 7f);
            Func<float, float, float> MudNoise = (x, z) => Mathf.PerlinNoise(x * 0.02f + 50f, z * 0.02f + 50f);
            Func<float, float, float> SandNoise = (x, z) => Mathf.PerlinNoise(x * 0.012f + 90f, z * 0.012f + 20f);

            Func<float, float, float> HeightFn = (x, z) =>
            {
                float natural = 0.15f + 0.45f * Low(x, z) + 0.12f * High(x, z);
                float m = MudNoise(x, z);
                if (m > 0.66f) natural -= 0.05f * (m - 0.66f) / 0.34f;
                float roadH = 0.16f + 0.45f * Low(x, z);
                float d = DistToPolyline(new Vector3(x, 0f, z), route);
                return Mathf.Lerp(roadH, natural, SmoothBand(d, 4f, 16f));
            };

            Func<float, float, float, float, float[]> AlphaFn = (x, z, h01, steep) =>
            {
                // 0 трава, 1 грязь, 2 песок, 3 грунтовка, 4 камни
                var w = new float[5];
                float d = DistToPolyline(new Vector3(x, 0f, z), route);
                float track = 1f - SmoothBand(d, 3.5f, 6f);
                float rocks = SmoothBand(steep, 20f, 28f);
                float mud = Mathf.Max(SmoothBand(MudNoise(x, z), 0.62f, 0.7f), 1f - SmoothBand(h01, 0.17f, 0.22f));
                float sand = SmoothBand(SandNoise(x, z), 0.6f, 0.68f);
                w[3] = track;
                float rest = 1f - track;
                w[4] = rest * rocks;
                rest *= 1f - rocks;
                w[1] = rest * mud;
                rest *= 1f - mud;
                w[2] = rest * sand;
                rest *= 1f - sand;
                w[0] = rest;
                return w;
            };

            Terrain terrain = CreateTerrain(c, "Offroad", size, height, HeightFn, AlphaFn,
                new[] { Layers["Grass"], Layers["Mud"], Layers["Sand"], Layers["Dirt"], Layers["Rocks"] });

            for (int i = 0; i < route.Count; i++)
            {
                Vector3 p = route[i];
                p.y = terrain.SampleHeight(p);
                route[i] = p;
            }

            // Брод
            Vector3 ford = route[20];
            Quaternion fordRot = Quaternion.LookRotation(Flat(route[21] - route[20]).normalized);
            Box(c.world, "Ford", ford + Vector3.up * 0.05f, new Vector3(16f, 0.3f, 14f), fordRot, "Water", SurfaceKind.Water);

            // Камни и брёвна
            for (int i = 0; i < 70; i++)
            {
                Vector3 p = new Vector3(Rand(r, -280f, 280f), 0f, Rand(r, -280f, 280f));
                if (DistToPolyline(p, route) < 7f) continue;
                p.y = terrain.SampleHeight(p);
                Rock(c.world, p, Rand(r, 1f, 3.5f), r);
            }
            for (int i = 0; i < 14; i++)
            {
                int idx = r.Next(2, route.Count - 2);
                Vector3 side = RightOf(route[idx + 1] - route[idx]) * (r.NextDouble() > 0.5 ? 1f : -1f);
                Vector3 p = route[idx] + side * Rand(r, 8f, 14f);
                p.y = terrain.SampleHeight(p);
                Log(c.world, p, Rand(r, 0f, 180f), r);
            }
            for (int i = 0; i < 90; i++)
            {
                Vector3 p = new Vector3(Rand(r, -290f, 290f), 0f, Rand(r, -290f, 290f));
                if (DistToPolyline(p, route) < 12f) continue;
                p.y = terrain.SampleHeight(p);
                Tree(c.world, p, Rand(r, 0.8f, 1.3f), r);
            }

            CheckpointsAlong(c, route, 4, 4, true);
            c.spawnPosition = route[0] + Vector3.up * 0.8f;
            c.spawnRotation = Quaternion.LookRotation(Flat(route[1] - route[0]).normalized);
            c.repairPosition = route[22] + RightOf(route[23] - route[22]) * 12f;

            c.defaultMode = GameMode.OffroadChallenge;
            c.rules.Add(Rules(GameMode.OffroadChallenge, 100f, 140f, 200f, 300f));
            c.rules.Add(Rules(GameMode.TimeTrial, 100f, 140f, 200f));
            c.rules.Add(GameModeManager.DefaultRules(GameMode.FreeRide));
            FinishGameplay(c);
        }

        // ================= OpenWorld: открытый мир =================

        private static void CreateOpenWorldScene()
        {
            SceneCtx c = BeginGameplay("OpenWorld");
            var r = new Random(6);
            const float size = 1000f;
            const float height = 50f;

            var ring = new List<Vector3>();
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                ring.Add(new Vector3(320f * Mathf.Cos(a) + 40f * Mathf.Sin(3f * a), 0f, 300f * Mathf.Sin(a) + 30f * Mathf.Cos(2f * a)));
            }
            var dirt = new List<Vector3>();
            for (int i = 0; i <= 30; i++)
            {
                float t = i / 30f;
                dirt.Add(new Vector3(-280f + 560f * t, 0f, 60f * Mathf.Sin(t * 7f)));
            }
            Vector3 town = new Vector3(0f, 0f, 220f);
            Vector3 sandZone = new Vector3(-230f, 0f, -180f);
            Vector3 mudZone = new Vector3(200f, 0f, -170f);
            Vector3 forestZone = new Vector3(-220f, 0f, 170f);

            Func<float, float, float> Low = (x, z) => Mathf.PerlinNoise(x * 0.004f + 30f, z * 0.004f + 30f);
            Func<float, float, float> High = (x, z) => Mathf.PerlinNoise(x * 0.02f + 5f, z * 0.02f + 9f);
            float townH = 0.2f + 0.35f * Low(town.x, town.z);

            Func<float, float, float> HeightFn = (x, z) =>
            {
                var p = new Vector3(x, 0f, z);
                float h = 0.2f + 0.35f * Low(x, z) + 0.1f * High(x, z);
                float smooth = 0.2f + 0.35f * Low(x, z);
                h = Mathf.Lerp(smooth, h, SmoothBand(DistToPolyline(p, ring, true), 6f, 20f));
                h = Mathf.Lerp(smooth + 0.005f, h, SmoothBand(DistToPolyline(p, dirt), 4f, 14f));
                h = Mathf.Lerp(townH, h, SmoothBand(Vector3.Distance(p, town), 60f, 100f));
                float mud = 1f - SmoothBand(Vector3.Distance(p, mudZone), 50f, 80f);
                h -= 0.03f * mud;
                return h;
            };

            Func<float, float, float, float, float[]> AlphaFn = (x, z, h01, steep) =>
            {
                // 0 трава, 1 грязь, 2 песок, 3 грунтовка, 4 камни, 5 асфальт
                var w = new float[6];
                var p = new Vector3(x, 0f, z);
                float asphalt = Mathf.Max(1f - SmoothBand(DistToPolyline(p, ring, true), 5f, 7f),
                    1f - SmoothBand(Vector3.Distance(p, town), 50f, 56f));
                float track = 1f - SmoothBand(DistToPolyline(p, dirt), 3f, 5f);
                float rocks = SmoothBand(steep, 20f, 28f);
                float mud = 1f - SmoothBand(Vector3.Distance(p, mudZone), 55f, 75f);
                float sand = 1f - SmoothBand(Vector3.Distance(p, sandZone), 70f, 95f);
                w[5] = asphalt;
                float rest = 1f - asphalt;
                w[3] = rest * track;
                rest *= 1f - track;
                w[4] = rest * rocks;
                rest *= 1f - rocks;
                w[1] = rest * mud;
                rest *= 1f - mud;
                w[2] = rest * sand;
                rest *= 1f - sand;
                w[0] = rest;
                return w;
            };

            Terrain terrain = CreateTerrain(c, "OpenWorld", size, height, HeightFn, AlphaFn,
                new[] { Layers["Grass"], Layers["Mud"], Layers["Sand"], Layers["Dirt"], Layers["Rocks"], Layers["Asphalt"] });

            Func<Vector3, Vector3> OnGround = p =>
            {
                p.y = terrain.SampleHeight(p);
                return p;
            };

            // Городок
            string[] buildingMats = { "BuildingA", "BuildingB", "BuildingC" };
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                float d = Rand(r, 28f, 45f);
                Vector3 p = OnGround(town + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d));
                if (DistToPolyline(p, ring, true) < 14f) continue;
                float h = Rand(r, 8f, 22f);
                Box(c.world, "House", p + Vector3.up * (h * 0.5f - 0.5f), new Vector3(Rand(r, 10f, 16f), h, Rand(r, 10f, 16f)),
                    Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), buildingMats[r.Next(0, 3)], null, true, true, true);
            }
            // Лес
            for (int i = 0; i < 320; i++)
            {
                Vector3 p = forestZone + new Vector3(Rand(r, -140f, 140f), 0f, Rand(r, -110f, 110f));
                if (DistToPolyline(p, ring, true) < 10f || DistToPolyline(p, dirt) < 8f) continue;
                Tree(c.world, OnGround(p), Rand(r, 0.8f, 1.5f), r);
            }
            // Одиночные деревья и камни по карте
            for (int i = 0; i < 160; i++)
            {
                Vector3 p = new Vector3(Rand(r, -480f, 480f), 0f, Rand(r, -480f, 480f));
                if (DistToPolyline(p, ring, true) < 10f || DistToPolyline(p, dirt) < 8f || Vector3.Distance(p, town) < 60f) continue;
                if (i % 3 == 0) Rock(c.world, OnGround(p), Rand(r, 1f, 3f), r);
                else Tree(c.world, OnGround(p), Rand(r, 0.9f, 1.4f), r);
            }
            // Граница мира
            float half = size * 0.5f - 2f;
            Box(c.world, "BoundN", new Vector3(0f, 40f, half), new Vector3(size, 100f, 2f), Quaternion.identity, "Barrier", null, true).GetComponent<Renderer>().enabled = false;
            Box(c.world, "BoundS", new Vector3(0f, 40f, -half), new Vector3(size, 100f, 2f), Quaternion.identity, "Barrier", null, true).GetComponent<Renderer>().enabled = false;
            Box(c.world, "BoundE", new Vector3(half, 40f, 0f), new Vector3(2f, 100f, size), Quaternion.identity, "Barrier", null, true).GetComponent<Renderer>().enabled = false;
            Box(c.world, "BoundW", new Vector3(-half, 40f, 0f), new Vector3(2f, 100f, size), Quaternion.identity, "Barrier", null, true).GetComponent<Renderer>().enabled = false;

            // Точки исследования (в Free Ride берутся в любом порядке)
            AddCheckpoint(c, town, Vector3.forward);
            AddCheckpoint(c, sandZone, Vector3.right);
            AddCheckpoint(c, mudZone, Vector3.forward);
            AddCheckpoint(c, forestZone + new Vector3(0f, 0f, -60f), Vector3.right);
            AddCheckpoint(c, dirt[15], dirt[16] - dirt[15]);
            AddCheckpoint(c, ring[16], ring[17] - ring[16]);
            AddCheckpoint(c, ring[32], ring[33] - ring[32]);
            AddCheckpoint(c, ring[48], ring[49] - ring[48]);

            Vector3 startDir = Flat(ring[1] - ring[0]).normalized;
            c.spawnPosition = OnGround(ring[0] + RightOf(startDir) * 2.2f) + Vector3.up * 0.8f;
            c.spawnRotation = Quaternion.LookRotation(startDir);
            c.repairPosition = town + new Vector3(0f, 0f, -20f);

            foreach (var p in ring) c.aiPath.Add(OnGround(p));
            c.aiLoop = true;
            c.aiTwoWay = true;
            c.laneOffsets = new[] { 2.2f };
            c.aiMax = 10;

            c.defaultMode = GameMode.FreeRide;
            c.rules.Add(GameModeManager.DefaultRules(GameMode.FreeRide));
            FinishGameplay(c);
        }
    }
}
#endif
