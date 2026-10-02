using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Точка ремонта на карте: заехать в зону и постоять 1,5 секунды — машина полностью починится.
    /// Вешается на объект с коллайдером-триггером (Is Trigger = включено).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RepairPoint : MonoBehaviour
    {
        [Tooltip("Сколько стоять в зоне, сек.")]
        public float repairTime = 1.5f;
        [Tooltip("Максимальная скорость в зоне, км/ч.")]
        public float maxSpeedKmh = 12f;
        [Tooltip("Повторный ремонт возможен через, сек.")]
        public float cooldown = 20f;
        [Tooltip("Цена ремонта (0 — бесплатно).")]
        public int price = 0;

        private float timer;
        private float lastRepair = -100f;
        private float lastStayTime = -1f;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerStay(Collider other)
        {
            var vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle == null || !vehicle.isPlayer || vehicle.damage == null) return;
            if (Mathf.Approximately(Time.fixedTime, lastStayTime)) return; // у машины несколько коллайдеров
            lastStayTime = Time.fixedTime;
            if (Time.time < lastRepair + cooldown) return;
            if (vehicle.damage.Health01 >= 0.999f) return;

            if (vehicle.SpeedKmh > maxSpeedKmh)
            {
                timer = 0f;
                return;
            }

            timer += Time.fixedDeltaTime;
            if (timer < repairTime) return;
            timer = 0f;

            var gm = GameManager.Instance;
            if (price > 0 && gm != null && !gm.Progress.Spend(price))
            {
                if (VehicleHUD.Instance != null) VehicleHUD.Instance.ShowMessage("Недостаточно монет для ремонта", 2f);
                lastRepair = Time.time;
                return;
            }

            vehicle.damage.Repair();
            lastRepair = Time.time;
            if (VehicleHUD.Instance != null) VehicleHUD.Instance.ShowMessage("Машина отремонтирована", 2f);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<VehicleController>() != null) timer = 0f;
        }
    }
}
