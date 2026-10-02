using UnityEngine;

namespace TerrainDrive
{
    public enum CheckpointState
    {
        Hidden,
        Upcoming,
        Active,
        Passed
    }

    /// <summary>
    /// Ворота-чекпоинт. Коллайдер-триггер ловит машину игрока и сообщает менеджеру.
    /// Вешается на объект с BoxCollider (Is Trigger). Порядок задаётся полем index
    /// или порядком дочерних объектов у CheckpointRaceManager.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        public int index;
        [HideInInspector] public CheckpointRaceManager manager;

        [Tooltip("Куда ставить машину при возврате на этот чекпоинт (если пусто — сам чекпоинт).")]
        public Transform respawnPoint;

        [Header("Визуал")]
        [Tooltip("Рендереры, которые перекрашиваются по состоянию.")]
        public Renderer[] tintRenderers;
        [Tooltip("Столб света/маркер над активным чекпоинтом.")]
        public GameObject activeMarker;
        public Color activeColor = new Color(0.1f, 0.9f, 1f);
        public Color upcomingColor = new Color(1f, 0.85f, 0.2f);
        public Color passedColor = new Color(0.3f, 0.3f, 0.3f);

        public CheckpointState State { get; private set; } = CheckpointState.Active;
        public Transform RespawnTransform => respawnPoint != null ? respawnPoint : transform;

        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle == null || !vehicle.isPlayer) return;
            if (manager != null) manager.OnCheckpointEntered(this, vehicle);
        }

        public void SetState(CheckpointState state)
        {
            State = state;
            bool visible = state != CheckpointState.Hidden;
            foreach (var r in tintRenderers)
                if (r != null) r.enabled = visible;
            if (activeMarker != null) activeMarker.SetActive(state == CheckpointState.Active);
            if (!visible) return;

            Color c = state == CheckpointState.Active ? activeColor
                : state == CheckpointState.Upcoming ? upcomingColor
                : passedColor;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var r in tintRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                block.SetColor(EmissionId, state == CheckpointState.Active ? c * 1.5f : Color.black);
                r.SetPropertyBlock(block);
            }
        }
    }
}
