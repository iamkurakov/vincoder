using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Параметры одного покрытия. Create → Terrain Drive → Surface Data.
    /// Все множители относительно асфальта (асфальт = 1).
    /// </summary>
    [CreateAssetMenu(menuName = "Terrain Drive/Surface Data", fileName = "Surface_")]
    public class SurfaceData : ScriptableObject
    {
        public SurfaceKind kind = SurfaceKind.Asphalt;
        public string displayName = "Асфальт";

        [Header("Физика")]
        [Tooltip("Сцепление шин (асфальт = 1). Меньше — сильнее скольжение и пробуксовка.")]
        [Range(0.1f, 1.3f)] public float grip = 1f;
        [Tooltip("Множитель максимальной скорости.")]
        [Range(0.2f, 1f)] public float speedMultiplier = 1f;
        [Tooltip("Сопротивление качению: чем больше, тем сильнее «вязнет» машина.")]
        [Range(0f, 6f)] public float rollingResistance = 0f;
        [Tooltip("Эффективность тормозов (мокрый асфальт < 1 = длиннее тормозной путь).")]
        [Range(0.2f, 1.2f)] public float brakeMultiplier = 1f;
        [Tooltip("Неровность: тряска камеры и случайные толчки подвески (камни).")]
        [Range(0f, 1f)] public float bumpiness = 0f;

        [Header("Звук и эффекты")]
        [Tooltip("Зацикленный звук качения по этому покрытию.")]
        public AudioClip rollingLoop;
        [Tooltip("Визг шин при заносе возможен только на твёрдых покрытиях.")]
        public bool tireSqueal = true;
        public bool emitDust;
        public Color dustColor = new Color(0.6f, 0.5f, 0.4f, 0.6f);
    }
}
