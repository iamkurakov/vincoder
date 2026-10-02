using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainDrive
{
    /// <summary>
    /// Гараж: просмотр машин на поворотной платформе, покупка, выбор, улучшения
    /// (двигатель, шины, прочность) и ремонт. Вешается на корень канваса сцены Garage.
    /// </summary>
    public class GarageUI : MonoBehaviour
    {
        [Header("Платформа")]
        public Transform previewSpawn;
        public float rotateSpeed = 20f;

        [Header("Тексты")]
        public TMP_Text nameText;
        public TMP_Text descriptionText;
        public TMP_Text priceText;
        public TMP_Text currencyText;
        public TMP_Text healthText;

        [Header("Характеристики (Image Filled)")]
        public Image speedBar;
        public Image accelerationBar;
        public Image offroadBar;
        public Image durabilityBar;

        [Header("Кнопки")]
        public Button prevButton;
        public Button nextButton;
        public Button actionButton;
        public TMP_Text actionLabel;
        public Button repairButton;
        public TMP_Text repairLabel;
        public Button backButton;

        [Header("Улучшения")]
        public Button engineButton;
        public TMP_Text engineLabel;
        public Button gripButton;
        public TMP_Text gripLabel;
        public Button durabilityButton;
        public TMP_Text durabilityLabel;

        private GameManager GM => GameManager.Instance;
        private int index;
        private GameObject previewHolder;

        private void Awake()
        {
            GameManager.EnsureExists();
            if (prevButton != null) prevButton.onClick.AddListener(() => Step(-1));
            if (nextButton != null) nextButton.onClick.AddListener(() => Step(1));
            if (actionButton != null) actionButton.onClick.AddListener(OnAction);
            if (repairButton != null) repairButton.onClick.AddListener(OnRepair);
            if (backButton != null) backButton.onClick.AddListener(() => GM.LoadMainMenu());
            if (engineButton != null) engineButton.onClick.AddListener(() => Upgrade(UpgradeKind.Engine));
            if (gripButton != null) gripButton.onClick.AddListener(() => Upgrade(UpgradeKind.Grip));
            if (durabilityButton != null) durabilityButton.onClick.AddListener(() => Upgrade(UpgradeKind.Durability));
        }

        private void Start()
        {
            VehicleData selected = GM.Progress.SelectedVehicle;
            index = 0;
            for (int i = 0; i < GM.vehicles.Length; i++)
                if (GM.vehicles[i] == selected) index = i;
            Show();
        }

        private VehicleData Current => GM.vehicles != null && GM.vehicles.Length > 0 ? GM.vehicles[index] : null;

        private void Step(int dir)
        {
            if (GM.vehicles == null || GM.vehicles.Length == 0) return;
            index = (index + dir + GM.vehicles.Length) % GM.vehicles.Length;
            Show();
        }

        private void Show()
        {
            VehicleData v = Current;
            if (v == null) return;
            SpawnPreview(v);
            Refresh();
        }

        private void Refresh()
        {
            VehicleData v = Current;
            if (v == null) return;
            PlayerProgress p = GM.Progress;
            bool unlocked = p.IsVehicleUnlocked(v.id);
            bool selected = p.SelectedVehicle == v;

            if (nameText != null) nameText.text = v.displayName;
            if (descriptionText != null) descriptionText.text = v.description;
            if (currencyText != null) currencyText.text = $"{p.Currency} монет · уровень {p.Level}";
            SetBar(speedBar, v.statSpeed);
            SetBar(accelerationBar, v.statAcceleration);
            SetBar(offroadBar, v.statOffroad);
            SetBar(durabilityBar, v.statDurability);

            if (priceText != null)
            {
                priceText.text = unlocked
                    ? (selected ? "Выбрана" : "В гараже")
                    : (p.Level < v.requiredLevel ? $"Откроется на уровне {v.requiredLevel}" : $"Цена: {v.price} монет");
            }

            if (actionButton != null)
            {
                if (!unlocked)
                {
                    actionButton.interactable = p.CanBuyVehicle(v);
                    SetText(actionLabel, $"Купить за {v.price}");
                }
                else
                {
                    actionButton.interactable = !selected;
                    SetText(actionLabel, selected ? "Выбрана" : "Выбрать");
                }
            }

            VehicleState st = p.GetVehicleState(v.id);
            if (healthText != null) healthText.text = unlocked ? $"Состояние: {Mathf.RoundToInt(st.health01 * 100f)}%" : "";
            if (repairButton != null)
            {
                int cost = p.RepairCost(v);
                bool needs = unlocked && st.health01 < 0.999f;
                repairButton.interactable = needs && p.Currency >= cost;
                SetText(repairLabel, needs ? $"Ремонт — {cost}" : "Машина исправна");
            }

            UpgradeRow(engineButton, engineLabel, "Двигатель", v, UpgradeKind.Engine, unlocked);
            UpgradeRow(gripButton, gripLabel, "Шины", v, UpgradeKind.Grip, unlocked);
            UpgradeRow(durabilityButton, durabilityLabel, "Прочность", v, UpgradeKind.Durability, unlocked);
        }

        private void UpgradeRow(Button b, TMP_Text label, string title, VehicleData v, UpgradeKind kind, bool unlocked)
        {
            PlayerProgress p = GM.Progress;
            int lvl = p.GetUpgradeLevel(v.id, kind);
            int cost = p.UpgradeCost(v, kind);
            string text = $"{title} {lvl}/{PlayerProgress.MaxUpgradeLevel}" + (cost >= 0 ? $" — {cost}" : " — максимум");
            SetText(label, text);
            if (b != null) b.interactable = unlocked && cost >= 0 && p.Currency >= cost;
        }

        private static void SetBar(Image bar, int value)
        {
            if (bar != null) bar.fillAmount = Mathf.Clamp01(value / 10f);
        }

        private static void SetText(TMP_Text t, string s)
        {
            if (t != null) t.text = s;
        }

        private void OnAction()
        {
            VehicleData v = Current;
            if (v == null) return;
            PlayerProgress p = GM.Progress;
            if (!p.IsVehicleUnlocked(v.id))
            {
                if (p.BuyVehicle(v)) p.SelectVehicle(v.id);
            }
            else p.SelectVehicle(v.id);
            Refresh();
        }

        private void OnRepair()
        {
            VehicleData v = Current;
            if (v != null) GM.Progress.Repair(v);
            Refresh();
        }

        private void Upgrade(UpgradeKind kind)
        {
            VehicleData v = Current;
            if (v != null) GM.Progress.BuyUpgrade(v, kind);
            Refresh();
        }

        // ---------- Превью ----------

        private void SpawnPreview(VehicleData v)
        {
            if (previewHolder != null) Destroy(previewHolder);
            if (v.prefab == null) return;

            // Создаём модель внутри выключенного объекта: скрипты машины не успеют запуститься.
            previewHolder = new GameObject("Preview_" + v.id);
            previewHolder.SetActive(false);
            if (previewSpawn != null)
                previewHolder.transform.SetPositionAndRotation(previewSpawn.position, previewSpawn.rotation);

            GameObject model = Instantiate(v.prefab, previewHolder.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            var controller = model.GetComponent<VehicleController>();
            if (controller != null && controller.bodyRenderers != null)
                foreach (var r in controller.bodyRenderers)
                    if (r != null) r.material.color = v.bodyColor;

            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            foreach (var a in model.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

            previewHolder.SetActive(true);
        }

        private void Update()
        {
            if (previewHolder != null) previewHolder.transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.World);
        }
    }
}
