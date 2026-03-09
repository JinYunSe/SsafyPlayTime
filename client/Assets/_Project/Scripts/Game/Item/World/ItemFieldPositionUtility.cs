/*
 * 파일 개요:
 * - ItemFieldPositionUtility 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
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

