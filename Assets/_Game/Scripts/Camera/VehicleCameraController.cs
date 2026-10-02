using System;
using UnityEngine;

namespace TerrainDrive
{
    public enum CameraMode
    {
        ThirdPerson,
        Chase,
        Hood,
        Cockpit,
        Cinematic
    }

    /// <summary>
    /// Камера машины: пять режимов, плавное следование, FOV от скорости, тряска на неровностях
    /// и ударах, защита от прохождения сквозь стены, осмотр мышью/Q-E/правым стиком.
    /// Вешается на объект с Camera. Цель задаётся SetTarget() (это делает GameModeManager).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VehicleCameraController : MonoBehaviour
    {
        [Serializable]
        public class ModeSettings
        {
            public CameraMode mode;
            public string displayName;
            [Tooltip("Смещение от машины (x вправо, y вверх, z вперёд).")]
            public Vector3 offset;
            [Tooltip("Высота точки, куда смотрит камера.")]
            public float lookHeight = 1.2f;
            [Tooltip("Насколько быстро камера догоняет позицию.")]
            public float followSharpness = 8f;
            [Tooltip("Насколько быстро поворачивается за машиной.")]
            public float rotationSharpness = 5f;
            [Tooltip("Камера жёстко закреплена на машине (капот, салон).")]
            public bool attached;
            public float baseFov = 60f;
        }

        public VehicleController target;

        public ModeSettings[] modes =
        {
            new ModeSettings { mode = CameraMode.ThirdPerson, displayName = "Сзади", offset = new Vector3(0f, 2.3f, -6.2f), lookHeight = 1.2f, followSharpness = 9f, rotationSharpness = 5f, baseFov = 60f },
            new ModeSettings { mode = CameraMode.Chase, displayName = "Дальняя", offset = new Vector3(0f, 3.6f, -10f), lookHeight = 1.0f, followSharpness = 5f, rotationSharpness = 3f, baseFov = 58f },
            new ModeSettings { mode = CameraMode.Hood, displayName = "Капот", offset = new Vector3(0f, 1.45f, 0.9f), lookHeight = 1.4f, attached = true, followSharpness = 30f, rotationSharpness = 20f, baseFov = 65f },
            new ModeSettings { mode = CameraMode.Cockpit, displayName = "Салон", offset = new Vector3(-0.38f, 1.25f, -0.15f), lookHeight = 1.2f, attached = true, followSharpness = 40f, rotationSharpness = 25f, baseFov = 68f },
            new ModeSettings { mode = CameraMode.Cinematic, displayName = "Кино", offset = new Vector3(8f, 2.2f, 30f), lookHeight = 1f, followSharpness = 2f, rotationSharpness = 6f, baseFov = 45f },
        };

        [Header("Скорость и тряска")]
        [Tooltip("На сколько градусов расширяется обзор на максимальной скорости.")]
        public float speedFovBoost = 12f;
        public float fovMaxSpeedKmh = 180f;
        public float roughShake = 0.06f;
        public float impactShake = 0.25f;

        [Header("Столкновения камеры")]
        public LayerMask collisionMask = ~0;
        public float collisionRadius = 0.3f;

        [Header("Осмотр")]
        public float lookYawSpeed = 140f;
        public float lookReturnDelay = 1.5f;

        [Header("Мобильные")]
        [Tooltip("На телефоне камера чуть дальше и выше — видно больше дороги.")]
        public float phoneDistanceMultiplier = 1.15f;

        public CameraMode CurrentMode => modes[current].mode;
        public event Action<CameraMode> ModeChanged;

        private Camera cam;
        private int current;
        private float smoothedYaw;
        private float extraYaw;
        private float lastLookTime;
        private float shakeImpulse;
        private Vector3 cinematicPoint;
        private float cinematicTimer;
        private readonly RaycastHit[] hits = new RaycastHit[16];

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            VehicleInput.CameraSwitchRequested += NextMode;
        }

        private void OnDisable()
        {
            VehicleInput.CameraSwitchRequested -= NextMode;
            if (target != null && target.damage != null) target.damage.Hit -= OnHit;
            if (target != null) target.Respawned -= Snap;
        }

        public void SetTarget(VehicleController vehicle)
        {
            if (target != null && target.damage != null) target.damage.Hit -= OnHit;
            if (target != null) target.Respawned -= Snap;
            target = vehicle;
            if (target == null) return;
            if (target.damage != null) target.damage.Hit += OnHit;
            target.Respawned -= Snap;
            target.Respawned += Snap;

            int saved = GameManager.Instance != null ? GameManager.Instance.Progress.Settings.cameraMode : 0;
            SetMode(Mathf.Clamp(saved, 0, modes.Length - 1), false);
            Snap();
        }

        public void NextMode()
        {
            SetMode((current + 1) % modes.Length);
            if (GameManager.Instance != null) GameManager.Instance.Progress.Settings.cameraMode = current;
        }

        public void SetMode(int index, bool announce = true)
        {
            current = Mathf.Clamp(index, 0, modes.Length - 1);
            cinematicTimer = 0f;
            Snap();
            ModeChanged?.Invoke(modes[current].mode);
            if (announce && VehicleHUD.Instance != null) VehicleHUD.Instance.ShowMessage("Камера: " + modes[current].displayName, 1f);
        }

        private void OnHit(float damage, bool heavy, Vector3 point)
        {
            shakeImpulse = Mathf.Max(shakeImpulse, heavy ? impactShake : impactShake * 0.4f);
        }

        /// <summary>Мгновенно поставить камеру на место (после рестарта/телепорта).</summary>
        public void Snap()
        {
            if (target == null) return;
            smoothedYaw = target.transform.eulerAngles.y;
            extraYaw = 0f;
            ModeSettings m = modes[current];
            if (m.attached)
            {
                transform.SetPositionAndRotation(target.transform.TransformPoint(m.offset), target.transform.rotation);
            }
            else if (m.mode != CameraMode.Cinematic)
            {
                Quaternion yaw = Quaternion.Euler(0f, smoothedYaw, 0f);
                transform.position = target.transform.position + yaw * ScaledOffset(m);
                transform.LookAt(target.transform.position + Vector3.up * m.lookHeight);
            }
        }

        private Vector3 ScaledOffset(ModeSettings m)
        {
            float mul = PlatformManager.IsPhone && !m.attached ? phoneDistanceMultiplier : 1f;
            return m.offset * mul;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return; // пауза
            ModeSettings m = modes[current];
            Transform t = target.transform;

            // Осмотр
            float look = target.input != null ? target.input.LookYaw : 0f;
            if (Mathf.Abs(look) > 0.01f)
            {
                extraYaw += look * lookYawSpeed * dt;
                lastLookTime = Time.time;
            }
            else if (Time.time - lastLookTime > lookReturnDelay)
            {
                extraYaw = Mathf.Lerp(extraYaw, 0f, 1f - Mathf.Exp(-3f * dt));
            }
            extraYaw = Mathf.Clamp(extraYaw, -170f, 170f);

            Vector3 desiredPos;
            Quaternion desiredRot;
            Vector3 lookPoint = t.position + Vector3.up * m.lookHeight;

            switch (m.mode)
            {
                case CameraMode.Hood:
                case CameraMode.Cockpit:
                    desiredPos = t.TransformPoint(m.offset);
                    desiredRot = t.rotation * Quaternion.Euler(0f, extraYaw, 0f);
                    transform.position = desiredPos;
                    transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-m.rotationSharpness * dt));
                    break;

                case CameraMode.Cinematic:
                    cinematicTimer -= dt;
                    if (cinematicTimer <= 0f || Vector3.Distance(cinematicPoint, t.position) > 60f)
                    {
                        Vector3 flatFwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                        Vector3 right = Vector3.Cross(Vector3.up, flatFwd);
                        float side = UnityEngine.Random.value > 0.5f ? 1f : -1f;
                        cinematicPoint = t.position + flatFwd * m.offset.z + right * m.offset.x * side + Vector3.up * m.offset.y;
                        cinematicTimer = 6f;
                        transform.position = cinematicPoint;
                    }
                    desiredRot = Quaternion.LookRotation(lookPoint - transform.position);
                    transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-m.rotationSharpness * dt));
                    break;

                default:
                    // Камера поворачивается за курсом машины, а не за каждым её покачиванием.
                    float carYaw = t.eulerAngles.y;
                    if (target.IsReversing && target.ForwardSpeedKmh < -3f)
                        carYaw = t.eulerAngles.y; // при заднем ходе остаёмся позади машины
                    smoothedYaw = Mathf.LerpAngle(smoothedYaw, carYaw, 1f - Mathf.Exp(-m.rotationSharpness * dt));
                    Quaternion yawRot = Quaternion.Euler(0f, smoothedYaw + extraYaw, 0f);
                    desiredPos = t.position + yawRot * ScaledOffset(m);
                    desiredPos = AvoidWalls(lookPoint, desiredPos);
                    transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-m.followSharpness * dt));
                    transform.rotation = Quaternion.LookRotation(lookPoint - transform.position);
                    break;
            }

            // FOV от скорости
            float speed01 = Mathf.Clamp01(target.SpeedKmh / fovMaxSpeedKmh);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, m.baseFov + speedFovBoost * speed01 * speed01, 1f - Mathf.Exp(-4f * dt));

            // Тряска: неровное покрытие + удары
            float rough = 0f;
            SurfaceData s = target.CurrentSurface;
            if (s != null && target.GroundedWheelCount > 0) rough = s.bumpiness * roughShake * Mathf.Clamp01(target.SpeedKmh / 60f);
            shakeImpulse = Mathf.MoveTowards(shakeImpulse, 0f, dt * 1.2f);
            float amount = rough + shakeImpulse;
            if (amount > 0.0001f)
            {
                float tt = Time.time * 25f;
                Vector3 offset = new Vector3(Mathf.PerlinNoise(tt, 0f) - 0.5f, Mathf.PerlinNoise(0f, tt) - 0.5f, 0f) * amount;
                transform.position += transform.rotation * offset;
            }
        }

        private Vector3 AvoidWalls(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return to;
            dir /= dist;
            int count = Physics.SphereCastNonAlloc(from, collisionRadius, dir, hits, dist, collisionMask, QueryTriggerInteraction.Ignore);
            float best = dist;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.rigidbody != null && h.rigidbody == target.Body) continue;
                if (h.distance > 0f && h.distance < best) best = h.distance;
            }
            return from + dir * Mathf.Max(0.8f, best - 0.1f);
        }
    }
}
