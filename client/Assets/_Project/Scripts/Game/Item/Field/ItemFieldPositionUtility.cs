using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 필드 아이템 배치 위치 계산을 담당한다.
    /// </summary>
    public static class ItemFieldPositionUtility
    {
        public static Vector3 GetRingOffset(int index, int totalCount, float radius)
        {
            if (totalCount <= 0)
            {
                return Vector3.zero;
            }

            var clampedRadius = Mathf.Max(0.1f, radius);
            var angle = (Mathf.PI * 2f * index) / totalCount;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * clampedRadius;
        }

        public static Vector3 ResolveGroundPosition(
            Vector3 candidate,
            bool useGroundRaycast,
            LayerMask groundMask,
            float heightOffset)
        {
            if (!useGroundRaycast)
            {
                return candidate;
            }

            var rayOrigin = candidate + Vector3.up * 25f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, 100f, groundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * Mathf.Max(0f, heightOffset);
            }

            return candidate;
        }
    }
}
