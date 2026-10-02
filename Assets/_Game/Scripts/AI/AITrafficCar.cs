using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Простая машина трафика. Едет по WaypointPath со смещением в свою полосу, тормозит перед
    /// поворотами и препятствиями (BoxCast вперёд), останавливается после удара игрока.
    /// Кинематическое тело — дёшево для мобильных и предсказуемо.
    /// Вешается на корень префаба AI-машины вместе с Rigidbody и коллайдером.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class AITrafficCar : MonoBehaviour
    {
        public WaypointPath path;
        public float cruiseSpeedKmh = 60f;
        [Tooltip("Смещение вправо от центра дороги, м.")]
        public float laneOffset = 2f;
        public float acceleration = 4f;
        public float brakeDeceleration = 10f;

        [Header("Датчик препятствий")]
        public float sensorBaseDistance = 6f;
        public float sensorWidth = 1.8f;
        public LayerMask obstacleMask = ~0;

        [Header("Визуал")]
        public Renderer[] bodyRenderers;
        public Transform[] wheelVisuals;
        public float wheelRadius = 0.35f;

        [Header("Удар")]
        public float stopAfterHitSeconds = 2.5f;

        public float SpeedKmh => speed * 3.6f;
        /// <summary>Доехала до конца незамкнутого маршрута (спавнер переставит её).</summary>
        public bool ReachedEnd { get; private set; }

        private Rigidbody rb;
        private int targetIndex;
        private float speed;
        private float hitTimer;
        private readonly RaycastHit[] hits = new RaycastHit[8];

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void SetColor(Color c)
        {
            if (bodyRenderers == null) return;
            foreach (var r in bodyRenderers)
                if (r != null) r.material.color = c;
        }

        /// <summary>Поставить машину на маршрут в точку index.</summary>
        public void Place(WaypointPath newPath, int index, float cruiseKmh, float lane)
        {
            path = newPath;
            cruiseSpeedKmh = cruiseKmh;
            laneOffset = lane;
            targetIndex = path.Wrap(index + 1);
            Vector3 pos = LanePoint(index);
            Vector3 dir = Flat(LanePoint(index + 1) - pos);
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            pos = SnapToGround(pos);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            speed = cruiseKmh / 3.6f * 0.6f;
            hitTimer = 0f;
            ReachedEnd = false;
        }

        private Vector3 LanePoint(int i)
        {
            Vector3 p = path.GetPoint(i);
            Vector3 next = path.GetPoint(i + 1);
            if (!path.loop && path.IsLast(i)) next = p + (p - path.GetPoint(i - 1));
            Vector3 dir = Flat(next - p);
            if (dir.sqrMagnitude < 0.001f) return p;
            Vector3 right = Vector3.Cross(Vector3.up, dir.normalized);
            return p + right * laneOffset;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void FixedUpdate()
        {
            if (path == null || path.Count < 2) return;
            float dt = Time.fixedDeltaTime;
            Vector3 pos = rb.position;

            Vector3 target = LanePoint(targetIndex);
            Vector3 toTarget = Flat(target - pos);
            if (toTarget.magnitude < Mathf.Max(4f, speed * 0.5f))
            {
                if (path.IsLast(targetIndex))
                {
                    speed = 0f;
                    ReachedEnd = true;
                    return;
                }
                targetIndex = path.Wrap(targetIndex + 1);
                target = LanePoint(targetIndex);
                toTarget = Flat(target - pos);
            }
            Vector3 dir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;

            // Тормозим перед поворотом
            Vector3 after = Flat(LanePoint(targetIndex + 1) - target);
            float turn = after.sqrMagnitude > 0.001f ? Vector3.Angle(dir, after) : 0f;
            float targetSpeed = cruiseSpeedKmh / 3.6f * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(turn / 90f));

            // Датчик препятствий
            float sensorDist = sensorBaseDistance + speed * 1.5f;
            Vector3 origin = pos + Vector3.up * 0.9f + transform.forward * 2.3f;
            int count = Physics.BoxCastNonAlloc(origin, new Vector3(sensorWidth * 0.5f, 0.5f, 0.3f), transform.forward,
                hits, transform.rotation, sensorDist, obstacleMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.rigidbody == rb || h.collider.transform.IsChildOf(transform)) continue;
                if (h.normal.y > 0.7f) continue; // земля
                if (h.distance < nearest) nearest = h.distance;
            }
            if (nearest < float.MaxValue)
                targetSpeed *= Mathf.Clamp01((nearest - 3f) / Mathf.Max(1f, sensorDist - 3f));

            if (hitTimer > 0f)
            {
                hitTimer -= dt;
                targetSpeed = 0f;
            }

            float rate = targetSpeed < speed ? brakeDeceleration : acceleration;
            speed = Mathf.MoveTowards(speed, targetSpeed, rate * dt);

            Vector3 newPos = SnapToGround(pos + dir * speed * dt);
            Quaternion newRot = Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-4f * dt));
            rb.MovePosition(newPos);
            rb.MoveRotation(newRot);

            if (wheelVisuals != null && wheelRadius > 0f)
            {
                float deg = speed * dt / wheelRadius * Mathf.Rad2Deg;
                foreach (var w in wheelVisuals)
                    if (w != null) w.Rotate(deg, 0f, 0f, Space.Self);
            }
        }

        private Vector3 SnapToGround(Vector3 p)
        {
            int count = Physics.RaycastNonAlloc(p + Vector3.up * 4f, Vector3.down, hits, 12f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            float y = p.y;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.rigidbody == rb || h.collider.transform.IsChildOf(transform)) continue;
                if (h.rigidbody != null && !h.rigidbody.isKinematic) continue; // не «залезать» на игрока
                if (h.distance < best)
                {
                    best = h.distance;
                    y = h.point.y;
                }
            }
            p.y = y;
            return p;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.GetComponentInParent<VehicleController>() != null)
                hitTimer = stopAfterHitSeconds;
        }
    }
}
