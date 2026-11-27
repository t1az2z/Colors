using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Метка невидимой стены по краю карты (<see cref="LevelBuilder"/>): она держит игрока и врагов на уровне,
    /// но луч курсора мыши (прицел) сквозь неё проходит.
    /// </summary>
    public sealed class EdgeWall : MonoBehaviour
    {
        public static bool Is(Collider collider) => collider != null && collider.TryGetComponent<EdgeWall>(out _);
    }
}
