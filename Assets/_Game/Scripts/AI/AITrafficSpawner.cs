using System.Collections.Generic;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Пул AI-машин (object pooling): машины создаются один раз и переиспользуются.
    /// Количество зависит от настроек графики (на слабых телефонах трафик отключается).
    /// Машины появляются вне непосредственной близости от игрока и «переезжают»,
    /// когда слишком далеко отстали.
    /// </summary>
    public class AITrafficSpawner : MonoBehaviour
    {
        public WaypointPath[] paths;
        public AITrafficCar[] carPrefabs;
        [Tooltip("Максимум машин в этой сцене (дополнительно ограничивается настройками графики).")]
        public int maxCars = 10;
        public float minSpawnDistance = 45f;
        public float despawnDistance = 260f;
        public Vector2 speedRangeKmh = new Vector2(45f, 80f);
        [Tooltip("Смещения полос от центра дороги, м (правостороннее движение — положительные).")]
        public float[] laneOffsets = { 2f };
        public Color[] colors =
        {
            new Color(0.85f, 0.1f, 0.1f), new Color(0.1f, 0.3f, 0.85f), new Color(0.9f, 0.9f, 0.9f),
            new Color(0.15f, 0.15f, 0.15f), new Color(0.95f, 0.75f, 0.1f), new Color(0.2f, 0.6f, 0.3f)
        };

        private readonly List<AITrafficCar> pool = new List<AITrafficCar>();
        private Transform player;
        private bool trafficEnabled = true;
        private float checkTimer;

        private int TargetCount
        {
            get
            {
                if (!trafficEnabled || carPrefabs == null || carPrefabs.Length == 0 || paths == null || paths.Length == 0) return 0;
                int limit = GraphicsSettingsManager.Instance != null ? GraphicsSettingsManager.Instance.AiTrafficCount : maxCars;
                return Mathf.Clamp(limit, 0, maxCars);
            }
        }

        private void OnEnable()
        {
            GraphicsSettingsManager.SettingsApplied += Refresh;
        }

        private void OnDisable()
        {
            GraphicsSettingsManager.SettingsApplied -= Refresh;
        }

        private void Start()
        {
            foreach (var p in paths)
                if (p != null) p.Collect();

            if (carPrefabs != null && carPrefabs.Length > 0)
            {
                for (int i = 0; i < maxCars; i++)
                {
                    AITrafficCar prefab = carPrefabs[i % carPrefabs.Length];
                    if (prefab == null) continue;
                    AITrafficCar car = Instantiate(prefab, transform);
                    car.name = "Traffic_" + i;
                    car.SetColor(colors.Length > 0 ? colors[i % colors.Length] : Color.gray);
                    car.gameObject.SetActive(false);
                    pool.Add(car);
                }
            }
            Refresh();
        }

        public void SetPlayer(Transform t)
        {
            player = t;
        }

        public void SetTrafficEnabled(bool on)
        {
            trafficEnabled = on;
            Refresh();
        }

        public void Refresh()
        {
            int target = TargetCount;
            for (int i = 0; i < pool.Count; i++)
            {
                bool shouldBeActive = i < target;
                AITrafficCar car = pool[i];
                if (car.gameObject.activeSelf == shouldBeActive) continue;
                car.gameObject.SetActive(shouldBeActive);
                if (shouldBeActive) Respawn(car);
            }
        }

        private void Update()
        {
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f || player == null) return;
            checkTimer = 1f;

            foreach (var car in pool)
            {
                if (!car.gameObject.activeSelf) continue;
                if (car.ReachedEnd || (car.transform.position - player.position).sqrMagnitude > despawnDistance * despawnDistance)
                    Respawn(car);
            }
        }

        private void Respawn(AITrafficCar car)
        {
            WaypointPath path = paths[Random.Range(0, paths.Length)];
            if (path == null || path.Count < 2) return;

            int maxIndex = path.loop ? path.Count : path.Count - 2; // на незамкнутом маршруте не ставим в самый конец
            int index = Random.Range(0, Mathf.Max(1, maxIndex));
            Vector3 center = player != null ? player.position : transform.position;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                int candidate = Random.Range(0, Mathf.Max(1, maxIndex));
                float d = Vector3.Distance(path.GetPoint(candidate), center);
                if (d >= minSpawnDistance && d <= despawnDistance * 0.8f && !IsOccupied(path.GetPoint(candidate), car))
                {
                    index = candidate;
                    break;
                }
            }

            float lane = laneOffsets != null && laneOffsets.Length > 0 ? laneOffsets[Random.Range(0, laneOffsets.Length)] : 2f;
            car.Place(path, index, Random.Range(speedRangeKmh.x, speedRangeKmh.y), lane);
        }

        private bool IsOccupied(Vector3 point, AITrafficCar except)
        {
            foreach (var other in pool)
            {
                if (other == except || !other.gameObject.activeSelf) continue;
                if ((other.transform.position - point).sqrMagnitude < 15f * 15f) return true;
            }
            return false;
        }
    }
}
