using System;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>Одно колесо: физический WheelCollider + видимая модель.</summary>
    [Serializable]
    public class WheelSetup
    {
        public WheelCollider collider;
        [Tooltip("Видимая модель колеса (пустой объект-пивот, внутри — сама модель).")]
        public Transform visual;
        public bool front;
        public bool left;

        [NonSerialized] public SurfaceData surface;
        [NonSerialized] public bool isGroundedOnSurface;
        [NonSerialized] public bool grounded;
        [NonSerialized] public float forwardSlip;
        [NonSerialized] public float sidewaysSlip;
    }

    /// <summary>
    /// Аркадно-реалистичная физика машины на WheelCollider: двигатель с автоматической коробкой,
    /// тормоз и задний ход одной кнопкой, ручник, зависимость руля от скорости, сцепление по покрытиям,
    /// стабилизаторы, прижимная сила, помощники (антипробуксовка, стабилизация, выравнивание в воздухе),
    /// переворот на колёса и возврат при падении с карты.
    /// Вешается на корень машины вместе с Rigidbody.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        [Header("Данные")]
        public VehicleData data;
        public WheelSetup[] wheels = new WheelSetup[4];
        [Tooltip("Машина игрока (реагирует на ввод и триггеры чекпоинтов).")]
        public bool isPlayer = true;

        [Header("Связанные компоненты (найдутся автоматически)")]
        public VehicleInput input;
        public SurfaceDetector surfaceDetector;
        public DamageSystem damage;

        [Header("Визуал")]
        [Tooltip("Рендереры кузова, которые красятся в цвет из VehicleData.")]
        public Renderer[] bodyRenderers;
        public GameObject brakeLights;
        public GameObject reverseLights;

        [Header("Помощники")]
        public bool tractionControl = true;
        [Range(0f, 1f)] public float stabilityAssist = 0.6f;
        [Tooltip("Насколько сильно машина выравнивается в воздухе.")]
        public float airControl = 2.5f;
        [Tooltip("Через сколько секунд лёжа на боку/крыше машина сама встанет на колёса.")]
        public float autoFlipDelay = 3f;
        [Tooltip("Если машина упала ниже этой высоты — вернуть на последнюю точку.")]
        public float fallResetHeight = -50f;

        [Header("Управление со стороны режима игры")]
        public bool inputEnabled = true;
        [Tooltip("Принудительно тормозить (после финиша).")]
        public bool forceBrake;

        // ---------- Публичное состояние ----------
        public float SpeedKmh { get; private set; }
        public float ForwardSpeedKmh { get; private set; }
        public float EngineRpm { get; private set; }
        public float Rpm01 => data != null ? Mathf.InverseLerp(data.idleRpm, data.maxRpm, EngineRpm) : 0f;
        public int GearIndex => gear;
        public bool IsReversing => reversing;
        public int GroundedWheelCount { get; private set; }
        public float MaxSidewaysSlip { get; private set; }
        public float MaxForwardSlip { get; private set; }
        public float ThrottleInput { get; private set; }
        public float BrakeInput { get; private set; }
        public float SteerInput => steer;
        public bool HandbrakeActive { get; private set; }
        public bool IsUpsideDown => Vector3.Dot(transform.up, Vector3.up) < 0.3f;
        public Rigidbody Body => rb;
        public SurfaceData CurrentSurface => surfaceDetector != null ? surfaceDetector.DominantSurface : null;
        public float Health01 => damage != null ? damage.Health01 : 1f;

        public string GearLabel
        {
            get
            {
                if (reversing) return "R";
                if (SpeedKmh < 1f && ThrottleInput < 0.05f) return "N";
                return (gear + 1).ToString();
            }
        }

        /// <summary>Машину переставили (рестарт, «на колёса», возврат на чекпоинт).</summary>
        public event Action Respawned;
        public event Action<int> GearChanged;

        /// <summary>Последняя безопасная точка (обновляется режимом игры на чекпоинтах).</summary>
        [NonSerialized] public Vector3 lastSafePosition;
        [NonSerialized] public Quaternion lastSafeRotation = Quaternion.identity;

        // ---------- Внутреннее ----------
        private Rigidbody rb;
        private float steer;
        private int gear;
        private float shiftTimer;
        private bool reversing;
        private float reverseHoldTimer;
        private float upsideDownTimer;
        private float engineMul = 1f, gripMul = 1f, topSpeedMul = 1f;
        private int frontL = -1, frontR = -1, rearL = -1, rearR = -1;
        private float wheelBase = 2.7f;
        private bool initialized;
        private readonly RaycastHit[] groundHits = new RaycastHit[8];

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (input == null) input = GetComponent<VehicleInput>();
            if (surfaceDetector == null) surfaceDetector = GetComponent<SurfaceDetector>();
            if (damage == null) damage = GetComponent<DamageSystem>();
            FindWheelPairs();
            lastSafePosition = transform.position;
            lastSafeRotation = transform.rotation;
        }

        private void Start()
        {
            if (!initialized && data != null) Initialize(data, null);
            if (wheels.Length > 0 && wheels[0] != null && wheels[0].collider != null)
                wheels[0].collider.ConfigureVehicleSubsteps(5f, 12, 15); // устойчивость WheelCollider на малой скорости
        }

        private void OnEnable()
        {
            if (isPlayer) VehicleInput.ResetRequested += FlipUpright;
        }

        private void OnDisable()
        {
            VehicleInput.ResetRequested -= FlipUpright;
        }

        private void FindWheelPairs()
        {
            float frontZ = 0f, rearZ = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null || w.collider == null) continue;
                float z = transform.InverseTransformPoint(w.collider.transform.position).z;
                if (w.front)
                {
                    frontZ = z;
                    if (w.left) frontL = i; else frontR = i;
                }
                else
                {
                    rearZ = z;
                    if (w.left) rearL = i; else rearR = i;
                }
            }
            wheelBase = Mathf.Max(1f, Mathf.Abs(frontZ - rearZ));
        }

        /// <summary>Применяет параметры машины и улучшения игрока.</summary>
        public void Initialize(VehicleData d, VehicleState state)
        {
            if (d == null) return;
            data = d;
            if (rb == null) rb = GetComponent<Rigidbody>();

            rb.mass = d.mass;
            rb.linearDamping = d.linearDamping;
            rb.angularDamping = d.angularDamping;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.ResetCenterOfMass();
            rb.centerOfMass += d.centerOfMassOffset;

            int engineLvl = state != null ? state.engineLevel : 0;
            int gripLvl = state != null ? state.gripLevel : 0;
            int durLvl = state != null ? state.durabilityLevel : 0;
            engineMul = 1f + 0.10f * engineLvl;
            topSpeedMul = 1f + 0.03f * engineLvl;
            gripMul = 1f + 0.06f * gripLvl;

            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                WheelCollider wc = w.collider;
                wc.mass = d.wheelMass;
                wc.radius = d.wheelRadius;
                wc.suspensionDistance = d.suspensionDistance;
                wc.forceAppPointDistance = 0.1f;
                wc.wheelDampingRate = 0.25f;

                JointSpring spring = wc.suspensionSpring;
                spring.spring = d.spring;
                spring.damper = d.damper;
                spring.targetPosition = 0.5f;
                wc.suspensionSpring = spring;

                WheelFrictionCurve fwd = wc.forwardFriction;
                fwd.extremumSlip = 0.4f;
                fwd.extremumValue = 1f;
                fwd.asymptoteSlip = 0.8f;
                fwd.asymptoteValue = 0.5f;
                fwd.stiffness = d.forwardStiffness;
                wc.forwardFriction = fwd;

                WheelFrictionCurve side = wc.sidewaysFriction;
                side.extremumSlip = 0.25f;
                side.extremumValue = 1f;
                side.asymptoteSlip = 0.5f;
                side.asymptoteValue = 0.75f;
                side.stiffness = d.sidewaysStiffness;
                wc.sidewaysFriction = side;
            }

            if (bodyRenderers != null)
                foreach (var r in bodyRenderers)
                    if (r != null) r.material.color = d.bodyColor;

            if (damage != null)
                damage.Configure(d.durability * (1f + 0.2f * durLvl), state != null ? state.health01 : 1f);

            gear = 0;
            EngineRpm = d.idleRpm;
            FindWheelPairs();
            initialized = true;
        }

        // ---------- Физика ----------

        private void FixedUpdate()
        {
            if (data == null || rb == null) return;
            float dt = Time.fixedDeltaTime;

            // 1. Ввод
            bool canDrive = inputEnabled && input != null && !forceBrake;
            ThrottleInput = canDrive ? input.Throttle : 0f;
            BrakeInput = canDrive ? input.Brake : 0f;
            float steerIn = canDrive ? input.Steer : 0f;
            HandbrakeActive = canDrive && input.Handbrake;

            Vector3 velocity = rb.linearVelocity;
            float fwdSpeed = Vector3.Dot(velocity, transform.forward); // м/с
            SpeedKmh = velocity.magnitude * 3.6f;
            ForwardSpeedKmh = fwdSpeed * 3.6f;

            // 2. Тормоз = задний ход, если машина почти стоит
            if (!reversing)
            {
                if (BrakeInput > 0.1f && fwdSpeed < 0.6f && ThrottleInput < 0.1f)
                {
                    reverseHoldTimer += dt;
                    if (reverseHoldTimer > 0.25f) reversing = true;
                }
                else reverseHoldTimer = 0f;
            }
            else if (ThrottleInput > 0.1f && fwdSpeed > -0.6f)
            {
                reversing = false;
                reverseHoldTimer = 0f;
            }

            float drive;   // -1..1 (минус — назад)
            float brake;   // 0..1
            if (forceBrake)
            {
                drive = 0f;
                brake = 1f;
            }
            else if (reversing)
            {
                drive = -BrakeInput;
                brake = ThrottleInput;
            }
            else
            {
                drive = ThrottleInput;
                brake = fwdSpeed > 0.6f ? BrakeInput : 0f;
            }

            // 3. Покрытия под колёсами
            if (surfaceDetector != null) surfaceDetector.UpdateWheels(wheels);

            float speedMulSum = 0f, resistanceSum = 0f;
            int grounded = 0;
            MaxSidewaysSlip = 0f;
            MaxForwardSlip = 0f;
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                w.grounded = w.collider.GetGroundHit(out WheelHit hit);
                if (w.grounded)
                {
                    grounded++;
                    w.forwardSlip = hit.forwardSlip;
                    w.sidewaysSlip = hit.sidewaysSlip;
                    MaxSidewaysSlip = Mathf.Max(MaxSidewaysSlip, Mathf.Abs(hit.sidewaysSlip));
                    MaxForwardSlip = Mathf.Max(MaxForwardSlip, Mathf.Abs(hit.forwardSlip));
                    SurfaceValues(w.surface, out _, out float sm, out float res, out _);
                    speedMulSum += sm;
                    resistanceSum += res;
                }
                else
                {
                    w.forwardSlip = 0f;
                    w.sidewaysSlip = 0f;
                }
            }
            GroundedWheelCount = grounded;
            float avgSpeedMul = grounded > 0 ? speedMulSum / grounded : 1f;
            float avgResistance = grounded > 0 ? resistanceSum / grounded : 0f;

            // 4. Руль: на скорости угол меньше
            float speedFactor = Mathf.InverseLerp(0f, data.topSpeedKmh * 0.75f, Mathf.Abs(ForwardSpeedKmh));
            float maxAngle = Mathf.Lerp(data.maxSteerAngle, data.highSpeedSteerAngle, speedFactor);
            if (damage != null) maxAngle *= damage.SteeringMultiplier;
            steer = Mathf.MoveTowards(steer, steerIn, data.steerSpeed * dt);
            float steerAngle = steer * maxAngle;

            // 5. Коробка передач и обороты
            UpdateGearbox(dt);

            // 6. Момент на колёса
            float topSpeed = (reversing ? data.reverseTopSpeedKmh : data.topSpeedKmh * topSpeedMul) * avgSpeedMul;
            if (damage != null) topSpeed *= damage.SpeedMultiplier;
            float limiter = Mathf.Clamp01((topSpeed - Mathf.Abs(ForwardSpeedKmh)) / 8f);

            float ratioFactor = reversing
                ? data.reverseRatio / data.gearRatios[0]
                : data.gearRatios[Mathf.Clamp(gear, 0, data.gearRatios.Length - 1)] / data.gearRatios[0];
            float curve = data.torqueCurve.Evaluate(Rpm01);
            float power = damage != null ? damage.PowerMultiplier : 1f;
            float shiftCut = shiftTimer > 0f ? 0.25f : 1f;
            float totalTorque = data.maxMotorTorque * engineMul * curve * ratioFactor * drive * limiter * power * shiftCut;

            int drivenCount = 0;
            foreach (var w in wheels)
                if (w != null && w.collider != null && IsDriven(w)) drivenCount++;
            float torquePerWheel = drivenCount > 0 ? totalTorque / drivenCount : 0f;

            bool idle = Mathf.Abs(drive) < 0.05f && brake < 0.05f;
            bool holdStill = idle && SpeedKmh < 1.5f && grounded > 0;

            // 7. Применение к каждому колесу
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                WheelCollider wc = w.collider;
                SurfaceValues(w.surface, out float grip, out _, out _, out float brakeMul);

                if (w.front) wc.steerAngle = steerAngle;

                float motor = IsDriven(w) ? torquePerWheel : 0f;
                if (tractionControl && w.grounded && Mathf.Abs(w.forwardSlip) > 0.45f && Mathf.Abs(drive) > 0f)
                    motor *= 0.55f;
                wc.motorTorque = motor;

                float bt = brake * data.maxBrakeTorque * brakeMul;
                if (idle) bt = data.maxBrakeTorque * (holdStill ? 0.4f : 0.02f); // торможение двигателем / удержание на месте
                bool rearHandbrake = HandbrakeActive && !w.front;
                if (rearHandbrake) bt = Mathf.Max(bt, data.handbrakeTorque);
                wc.brakeTorque = bt;

                WheelFrictionCurve f = wc.forwardFriction;
                f.stiffness = data.forwardStiffness * grip * gripMul;
                wc.forwardFriction = f;

                WheelFrictionCurve s = wc.sidewaysFriction;
                s.stiffness = data.sidewaysStiffness * grip * gripMul * (rearHandbrake ? 0.45f : 1f);
                wc.sidewaysFriction = s;

                // Неровности (камни): случайные толчки подвески
                if (w.grounded && w.surface != null && w.surface.bumpiness > 0f && SpeedKmh > 5f
                    && UnityEngine.Random.value < w.surface.bumpiness * 0.12f)
                {
                    float kick = UnityEngine.Random.Range(0.4f, 1f) * w.surface.bumpiness * rb.mass * 0.25f;
                    rb.AddForceAtPosition(Vector3.up * kick, wc.transform.position, ForceMode.Impulse);
                }
            }

            // 8. Сопротивление покрытия (грязь, песок, вода)
            if (grounded > 0 && avgResistance > 0f)
            {
                Vector3 planar = Vector3.ProjectOnPlane(velocity, Vector3.up);
                rb.AddForce(-planar * avgResistance * 0.06f * (grounded / 4f), ForceMode.Acceleration);
            }

            // 9. Стабилизаторы, прижим, помощники
            if (frontL >= 0 && frontR >= 0) AntiRoll(wheels[frontL].collider, wheels[frontR].collider);
            if (rearL >= 0 && rearR >= 0) AntiRoll(wheels[rearL].collider, wheels[rearR].collider);

            if (grounded > 0)
            {
                rb.AddForce(-transform.up * data.downforce * Mathf.Abs(fwdSpeed));

                if (grounded >= 2 && !HandbrakeActive && SpeedKmh > 5f && stabilityAssist > 0f)
                {
                    float desiredYaw = fwdSpeed * Mathf.Tan(steerAngle * Mathf.Deg2Rad) / wheelBase;
                    Vector3 localAng = transform.InverseTransformDirection(rb.angularVelocity);
                    float yawError = localAng.y - desiredYaw;
                    rb.AddTorque(-transform.up * yawError * stabilityAssist * 2f, ForceMode.Acceleration);
                }
            }
            else
            {
                // Выравнивание в воздухе: машина плавно поворачивается колёсами вниз
                Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
                rb.AddTorque(axis * airControl, ForceMode.Acceleration);
            }

            // 10. Авто-переворот и падение с карты
            if (IsUpsideDown && SpeedKmh < 5f)
            {
                upsideDownTimer += dt;
                if (upsideDownTimer > autoFlipDelay) FlipUpright();
            }
            else upsideDownTimer = 0f;

            if (transform.position.y < fallResetHeight) ResetToPoint(lastSafePosition, lastSafeRotation);
        }

        private bool IsDriven(WheelSetup w)
        {
            switch (data.drive)
            {
                case DriveType.FWD: return w.front;
                case DriveType.RWD: return !w.front;
                default: return true;
            }
        }

        /// <summary>Параметры покрытия с учётом «внедорожности» машины.</summary>
        private void SurfaceValues(SurfaceData s, out float grip, out float speedMul, out float resistance, out float brakeMul)
        {
            if (s == null)
            {
                grip = 1f; speedMul = 1f; resistance = 0f; brakeMul = 1f;
                return;
            }
            float off = data.offroadAbility;
            grip = s.grip < 1f ? s.grip + (1f - s.grip) * off * 0.5f : s.grip;
            speedMul = s.speedMultiplier + (1f - s.speedMultiplier) * off * 0.5f;
            resistance = s.rollingResistance * (1f - off * 0.5f);
            brakeMul = s.brakeMultiplier;
        }

        private void UpdateGearbox(float dt)
        {
            float wheelRpm = 0f;
            int n = 0;
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null || !IsDriven(w)) continue;
                wheelRpm += Mathf.Abs(w.collider.rpm);
                n++;
            }
            if (n > 0) wheelRpm /= n;

            float targetRpm;
            if (reversing)
            {
                targetRpm = wheelRpm * data.reverseRatio * data.finalDrive;
            }
            else
            {
                gear = Mathf.Clamp(gear, 0, data.gearRatios.Length - 1);
                targetRpm = wheelRpm * data.gearRatios[gear] * data.finalDrive;

                // «Сцепление» на первой передаче: двигатель может раскрутиться при старте
                if (gear == 0) targetRpm = Mathf.Max(targetRpm, data.idleRpm + ThrottleInput * data.maxRpm * 0.45f);

                if (shiftTimer <= 0f)
                {
                    if (targetRpm > data.maxRpm * 0.9f && gear < data.gearRatios.Length - 1 && ThrottleInput > 0.1f)
                    {
                        gear++;
                        shiftTimer = 0.25f;
                        GearChanged?.Invoke(gear);
                    }
                    else if (targetRpm < data.maxRpm * 0.4f && gear > 0)
                    {
                        gear--;
                        shiftTimer = 0.2f;
                        GearChanged?.Invoke(gear);
                    }
                }
            }
            shiftTimer -= dt;
            EngineRpm = Mathf.Lerp(EngineRpm, Mathf.Clamp(targetRpm, data.idleRpm, data.maxRpm), dt * 10f);
        }

        private void AntiRoll(WheelCollider left, WheelCollider right)
        {
            if (left == null || right == null) return;
            float travelL = 1f, travelR = 1f;
            bool gl = left.GetGroundHit(out WheelHit hitL);
            bool gr = right.GetGroundHit(out WheelHit hitR);
            if (gl) travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / Mathf.Max(0.01f, left.suspensionDistance);
            if (gr) travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / Mathf.Max(0.01f, right.suspensionDistance);
            float force = (travelL - travelR) * data.antiRollForce;
            if (gl) rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (gr) rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        // ---------- Визуал ----------

        private void Update()
        {
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null || w.visual == null) continue;
                w.collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
                w.visual.SetPositionAndRotation(pos, rot);
            }
            bool braking = forceBrake || (reversing ? ThrottleInput > 0.05f : BrakeInput > 0.05f && ForwardSpeedKmh > 2f) || HandbrakeActive;
            if (brakeLights != null && brakeLights.activeSelf != braking) brakeLights.SetActive(braking);
            if (reverseLights != null && reverseLights.activeSelf != reversing) reverseLights.SetActive(reversing);
        }

        // ---------- Перестановка машины ----------

        /// <summary>Поставить машину на колёса на текущем месте (клавиша R / кнопка ↺).</summary>
        public void FlipUpright()
        {
            if (rb == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(-transform.up, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 pos = transform.position;
            if (FindGround(pos, out Vector3 ground)) pos = ground;
            ResetToPoint(pos + Vector3.up * 0.6f, Quaternion.LookRotation(forward.normalized, Vector3.up));
        }

        /// <summary>Мгновенно переставить машину в точку, обнулив скорость.</summary>
        public void ResetToPoint(Vector3 position, Quaternion rotation)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            steer = 0f;
            reversing = false;
            upsideDownTimer = 0f;
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                w.collider.motorTorque = 0f;
                w.collider.brakeTorque = data != null ? data.maxBrakeTorque : 5000f;
            }
            Respawned?.Invoke();
        }

        public void SetSafePoint(Vector3 position, Quaternion rotation)
        {
            lastSafePosition = position;
            lastSafeRotation = rotation;
        }

        private bool FindGround(Vector3 around, out Vector3 point)
        {
            point = around;
            int count = Physics.RaycastNonAlloc(around + Vector3.up * 20f, Vector3.down, groundHits, 80f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = groundHits[i];
                if (h.rigidbody == rb) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    point = h.point;
                    found = true;
                }
            }
            return found;
        }
    }
}
