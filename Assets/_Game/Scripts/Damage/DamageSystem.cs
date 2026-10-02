using System;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Повреждения без деформации кузова: удар отнимает «здоровье» машины пропорционально силе,
    /// а низкое здоровье снижает максимальную скорость, мощность и точность руля.
    /// Вешается на корень машины (рядом с Rigidbody — иначе OnCollisionEnter не придёт).
    /// </summary>
    public class DamageSystem : MonoBehaviour
    {
        [Header("Здоровье")]
        public float maxHealth = 100f;
        [Tooltip("Включено ли получение урона (в Free Ride можно выключить).")]
        public bool damageEnabled = true;

        [Header("Удары")]
        [Tooltip("Скорость удара (м/с по нормали), ниже которой урона нет. 4 м/с ≈ 14 км/ч.")]
        public float minImpactSpeed = 4f;
        [Tooltip("Урон за каждый м/с сверх порога.")]
        public float damagePerImpactSpeed = 2.5f;
        [Tooltip("С какой скорости удар считается сильным (для штрафов City Challenge). 9 м/с ≈ 32 км/ч.")]
        public float heavyImpactSpeed = 9f;
        [Tooltip("Лёгкие предметы (конусы, урны) легче этой массы почти не наносят урона.")]
        public float lightObjectMass = 150f;
        [Tooltip("Защита от двойного засчитывания одного удара.")]
        public float hitCooldown = 0.3f;

        [Header("Влияние на машину при нулевом здоровье")]
        [Range(0.3f, 1f)] public float minSpeedMultiplier = 0.65f;
        [Range(0.3f, 1f)] public float minPowerMultiplier = 0.6f;
        [Range(0.3f, 1f)] public float minSteeringMultiplier = 0.75f;

        [Header("Визуал")]
        [Tooltip("Дым/искры, включаются при критическом повреждении.")]
        public GameObject criticalEffect;

        public float Health { get; private set; }
        public float Health01 => maxHealth > 0f ? Health / maxHealth : 1f;
        public bool IsCritical => Health01 < 0.25f;
        public bool IsDestroyed { get; private set; }

        public float SpeedMultiplier => Mathf.Lerp(minSpeedMultiplier, 1f, Health01);
        public float PowerMultiplier => Mathf.Lerp(minPowerMultiplier, 1f, Health01);
        public float SteeringMultiplier => Mathf.Lerp(minSteeringMultiplier, 1f, Mathf.Sqrt(Health01));

        /// <summary>Удар: урон, сильный ли, точка удара.</summary>
        public event Action<float, bool, Vector3> Hit;
        public event Action Destroyed;
        public event Action Repaired;

        private float lastHitTime = -10f;

        private void Awake()
        {
            if (Health <= 0f) Health = maxHealth;
        }

        /// <summary>Настроить запас здоровья (с учётом улучшения прочности) и текущее состояние.</summary>
        public void Configure(float newMaxHealth, float health01)
        {
            maxHealth = Mathf.Max(1f, newMaxHealth);
            Health = maxHealth * Mathf.Clamp01(health01);
            if (Health <= 0f) Health = maxHealth * 0.25f; // разбитую машину выпускаем с минимумом
            IsDestroyed = false;
            UpdateEffects();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!damageEnabled || IsDestroyed) return;
            if (Time.time < lastHitTime + hitCooldown) return;
            if (collision.contactCount == 0) return;

            ContactPoint contact = collision.GetContact(0);
            Vector3 normal = contact.normal;
            // Приземление на землю после прыжка не считаем ударом
            if (normal.y > 0.7f) return;

            float impact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
            if (impact < minImpactSpeed) return;

            bool lightObject = collision.rigidbody != null && !collision.rigidbody.isKinematic
                               && collision.rigidbody.mass < lightObjectMass;
            float dmg = (impact - minImpactSpeed) * damagePerImpactSpeed * (lightObject ? 0.15f : 1f);
            bool heavy = !lightObject && impact >= heavyImpactSpeed;
            lastHitTime = Time.time;
            ApplyDamage(dmg, heavy, contact.point);
        }

        public void ApplyDamage(float amount, bool heavy, Vector3 point)
        {
            if (amount <= 0f || IsDestroyed) return;
            Health = Mathf.Max(0f, Health - amount);
            Hit?.Invoke(amount, heavy, point);
            if (Health <= 0f)
            {
                IsDestroyed = true;
                Destroyed?.Invoke();
            }
            UpdateEffects();
        }

        public void Repair(float amount = float.MaxValue)
        {
            Health = Mathf.Min(maxHealth, Health + amount);
            if (Health > 0f) IsDestroyed = false;
            Repaired?.Invoke();
            UpdateEffects();
        }

        private void UpdateEffects()
        {
            if (criticalEffect != null) criticalEffect.SetActive(IsCritical);
        }
    }
}
