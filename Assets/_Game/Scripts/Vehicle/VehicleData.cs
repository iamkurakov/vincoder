using UnityEngine;

namespace TerrainDrive
{
    public enum DriveType
    {
        FWD, // передний привод
        RWD, // задний привод
        AWD  // полный привод
    }

    /// <summary>
    /// Все числа одной машины. Создаётся через Create → Terrain Drive → Vehicle Data.
    /// Геймдизайнер меняет здесь ощущения от машины без программиста.
    /// </summary>
    [CreateAssetMenu(menuName = "Terrain Drive/Vehicle Data", fileName = "VehicleData")]
    public class VehicleData : ScriptableObject
    {
        [Header("Описание")]
        public string id = "sedan";
        public string displayName = "Городской седан";
        [TextArea(2, 4)] public string description;
        public GameObject prefab;
        public Color bodyColor = new Color(0.8f, 0.8f, 0.82f);
        public int price;
        public int requiredLevel = 1;

        [Header("Карточка в гараже (0–10)")]
        [Range(0, 10)] public int statSpeed = 5;
        [Range(0, 10)] public int statAcceleration = 5;
        [Range(0, 10)] public int statOffroad = 3;
        [Range(0, 10)] public int statDurability = 5;

        [Header("Масса и устойчивость")]
        [Tooltip("Масса, кг.")] public float mass = 1400f;
        [Tooltip("Сопротивление воздуха (Rigidbody Linear Damping).")] public float linearDamping = 0.3f;
        [Tooltip("Гашение вращения (Rigidbody Angular Damping).")] public float angularDamping = 1f;
        [Tooltip("Смещение центра масс от геометрического. Отрицательный Y = ниже = устойчивее.")]
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.45f, 0.05f);
        [Tooltip("Сила стабилизаторов поперечной устойчивости (меньше крен).")] public float antiRollForce = 6000f;
        [Tooltip("Прижимная сила на скорости.")] public float downforce = 40f;

        [Header("Двигатель и тормоза")]
        public DriveType drive = DriveType.FWD;
        [Tooltip("Момент на колёсах на 1-й передаче в пике, Н·м (суммарно).")] public float maxMotorTorque = 2200f;
        [Tooltip("Тормозной момент на колесо, Н·м.")] public float maxBrakeTorque = 5000f;
        public float handbrakeTorque = 9000f;
        public float topSpeedKmh = 180f;
        public float reverseTopSpeedKmh = 35f;
        [Tooltip("Множитель момента по оборотам (0..1 по горизонтали = холостые..отсечка).")]
        public AnimationCurve torqueCurve = new AnimationCurve(
            new Keyframe(0f, 0.65f), new Keyframe(0.45f, 1f), new Keyframe(0.85f, 0.95f), new Keyframe(1f, 0.7f));
        public float idleRpm = 900f;
        public float maxRpm = 6500f;
        public float[] gearRatios = { 3.6f, 2.15f, 1.5f, 1.15f, 0.92f, 0.76f };
        public float finalDrive = 3.9f;
        public float reverseRatio = 3.3f;

        [Header("Руль")]
        [Tooltip("Угол колёс на малой скорости.")] public float maxSteerAngle = 34f;
        [Tooltip("Угол колёс на максимальной скорости.")] public float highSpeedSteerAngle = 9f;
        [Tooltip("Скорость поворота руля (доля полного хода в секунду).")] public float steerSpeed = 4f;

        [Header("Шины")]
        public float forwardStiffness = 1.4f;
        public float sidewaysStiffness = 1.6f;
        [Tooltip("0 — шоссейная машина, 1 — настоящий внедорожник. Уменьшает штрафы плохих покрытий.")]
        [Range(0f, 1f)] public float offroadAbility = 0.2f;

        [Header("Подвеска")]
        public float wheelRadius = 0.36f;
        public float wheelMass = 20f;
        public float suspensionDistance = 0.22f;
        public float spring = 35000f;
        public float damper = 4500f;

        [Header("Прочность")]
        [Tooltip("Запас здоровья (условные единицы).")] public float durability = 100f;
        [Tooltip("Цена полного ремонта в гараже.")] public int repairFullCost = 300;

        [Header("Улучшения")]
        [Tooltip("Цена 1-го уровня улучшения; 2-й в 2 раза дороже, 3-й в 3 раза.")]
        public int upgradeBaseCost = 400;
    }
}
