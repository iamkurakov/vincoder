using System.Collections.Generic;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Маршрут для AI-машин: цепочка точек (дочерние объекты по порядку).
    /// Точки ставятся по центру дороги; AI едет со смещением вправо (полоса).
    /// </summary>
    public class WaypointPath : MonoBehaviour
    {
        public bool loop = true;
        [Tooltip("Если пусто — берутся дочерние объекты по порядку.")]
        public List<Transform> points = new List<Transform>();
        public Color gizmoColor = new Color(1f, 0.6f, 0f);

        public int Count => points.Count;

        private void Awake()
        {
            Collect();
        }

        public void Collect()
        {
            if (points.Count > 0) return;
            foreach (Transform child in transform) points.Add(child);
        }

        public int Wrap(int i)
        {
            if (Count == 0) return 0;
            if (loop) return ((i % Count) + Count) % Count;
            return Mathf.Clamp(i, 0, Count - 1);
        }

        public Vector3 GetPoint(int i) => Count == 0 ? transform.position : points[Wrap(i)].position;

        public bool IsLast(int i) => !loop && i >= Count - 1;

        public int ClosestIndex(Vector3 position)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                float d = (points[i].position - position).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        private void OnDrawGizmos()
        {
            List<Transform> list = points.Count > 0 ? points : null;
            if (list == null)
            {
                list = new List<Transform>();
                foreach (Transform child in transform) list.Add(child);
            }
            if (list.Count < 2) return;
            Gizmos.color = gizmoColor;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                Gizmos.DrawSphere(list[i].position, 0.6f);
                int next = i + 1;
                if (next >= list.Count)
                {
                    if (!loop) break;
                    next = 0;
                }
                if (list[next] != null) Gizmos.DrawLine(list[i].position, list[next].position);
            }
        }
    }
}
