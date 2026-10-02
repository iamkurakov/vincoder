using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Определяет покрытие под каждым колесом. Порядок проверки:
    /// 1) компонент SurfaceType на коллайдере или его родителях;
    /// 2) Unity Terrain — самый сильный слой текстуры в точке касания;
    /// 3) Physics Material коллайдера;
    /// 4) покрытие по умолчанию.
    /// Вешается на машину рядом с VehicleController.
    /// </summary>
    public class SurfaceDetector : MonoBehaviour
    {
        [Serializable]
        public class TerrainLayerMapping
        {
            public TerrainLayer layer;
            public SurfaceData surface;
        }

        [Serializable]
        public class PhysicsMaterialMapping
        {
            public PhysicsMaterial material;
            public SurfaceData surface;
        }

        [Tooltip("Покрытие, если ничего не найдено (обычно асфальт).")]
        public SurfaceData defaultSurface;
        [Tooltip("Соответствие слоёв Terrain и покрытий.")]
        public TerrainLayerMapping[] terrainLayers = new TerrainLayerMapping[0];
        [Tooltip("Соответствие Physics Material и покрытий (необязательно).")]
        public PhysicsMaterialMapping[] physicsMaterials = new PhysicsMaterialMapping[0];
        [Tooltip("Как часто обновлять покрытие под колесом, сек.")]
        public float updateInterval = 0.1f;

        /// <summary>Покрытие, на котором стоит больше всего колёс.</summary>
        public SurfaceData DominantSurface { get; private set; }

        private readonly Dictionary<Collider, SurfaceData> colliderCache = new Dictionary<Collider, SurfaceData>();
        private readonly Dictionary<SurfaceData, int> counter = new Dictionary<SurfaceData, int>();
        private float timer;

        private void Awake()
        {
            DominantSurface = defaultSurface;
        }

        /// <summary>Обновляет wheel.surface у всех колёс. Вызывается из VehicleController.FixedUpdate.</summary>
        public void UpdateWheels(WheelSetup[] wheels)
        {
            timer -= Time.fixedDeltaTime;
            if (timer > 0f) return;
            timer = updateInterval;

            counter.Clear();
            SurfaceData best = null;
            int bestCount = 0;

            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                SurfaceData s = Detect(w.collider);
                w.surface = s;
                w.isGroundedOnSurface = s != null;
                if (s == null) continue;
                counter.TryGetValue(s, out int c);
                c++;
                counter[s] = c;
                if (c > bestCount)
                {
                    bestCount = c;
                    best = s;
                }
            }
            if (best != null) DominantSurface = best;
        }

        /// <summary>Покрытие под одним колесом или null, если колесо в воздухе.</summary>
        public SurfaceData Detect(WheelCollider wheel)
        {
            if (!wheel.GetGroundHit(out WheelHit hit)) return null;
            Collider col = hit.collider;
            if (col == null) return defaultSurface;

            if (col is TerrainCollider)
            {
                var terrain = col.GetComponent<Terrain>();
                SurfaceData fromTerrain = terrain != null ? FromTerrain(terrain, hit.point) : null;
                return fromTerrain != null ? fromTerrain : defaultSurface;
            }

            if (colliderCache.TryGetValue(col, out SurfaceData cached)) return cached;

            SurfaceData result = null;
            var tag = col.GetComponentInParent<SurfaceType>();
            if (tag != null && tag.surface != null) result = tag.surface;

            if (result == null && col.sharedMaterial != null && physicsMaterials != null)
            {
                foreach (var m in physicsMaterials)
                    if (m != null && m.material == col.sharedMaterial) { result = m.surface; break; }
            }

            if (result == null) result = defaultSurface;
            colliderCache[col] = result;
            return result;
        }

        private SurfaceData FromTerrain(Terrain terrain, Vector3 worldPoint)
        {
            TerrainData td = terrain.terrainData;
            if (td == null || td.alphamapLayers == 0) return null;

            Vector3 local = worldPoint - terrain.transform.position;
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(local.z / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);

            float[,,] alpha = td.GetAlphamaps(x, z, 1, 1);
            int bestLayer = 0;
            float bestWeight = -1f;
            for (int i = 0; i < td.alphamapLayers; i++)
            {
                if (alpha[0, 0, i] > bestWeight)
                {
                    bestWeight = alpha[0, 0, i];
                    bestLayer = i;
                }
            }

            TerrainLayer[] layers = td.terrainLayers;
            if (bestLayer >= layers.Length) return null;
            TerrainLayer layer = layers[bestLayer];
            if (terrainLayers == null) return null;
            foreach (var m in terrainLayers)
                if (m != null && m.layer == layer) return m.surface;
            return null;
        }

        /// <summary>Сброс кэша (если в сцене поменяли объекты на лету).</summary>
        public void ClearCache() => colliderCache.Clear();
    }
}
