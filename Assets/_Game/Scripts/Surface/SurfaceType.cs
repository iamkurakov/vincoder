using UnityEngine;

namespace TerrainDrive
{
    /// <summary>Виды покрытий в игре.</summary>
    public enum SurfaceKind
    {
        Asphalt,
        WetAsphalt,
        Gravel,
        Mud,
        Grass,
        Sand,
        ForestSoil,
        Rocks,
        Water
    }

    /// <summary>
    /// Метка «из чего сделан этот объект». Вешается на дорогу, мост, площадку, лужу и т. п.
    /// Колесо, коснувшись коллайдера этого объекта (или его дочерних), берёт параметры из surface.
    /// Для Unity Terrain метка не нужна — покрытие берётся из слоёв текстур (см. SurfaceDetector).
    /// </summary>
    [DisallowMultipleComponent]
    public class SurfaceType : MonoBehaviour
    {
        [Tooltip("Параметры покрытия (ScriptableObject SurfaceData).")]
        public SurfaceData surface;
    }
}
