using UnityEngine;

namespace SSAFYPlayTime.Character
{
    // 구체 캐스트(SphereCast)로 캐릭터가 땅에 닿아 있는지 판정하는 헬퍼 클래스.
    // 매 물리 틱마다 호출되며 GC 부담을 줄이기 위해 결과 배열을 미리 할당해 재사용한다.
    public sealed class GroundProbe
    {
        // SphereCast 결과를 담는 사전 할당 버퍼 (최대 10개 충돌)
        private readonly RaycastHit[] _hits = new RaycastHit[10];

        // 지정된 위치에서 아래 방향으로 구체 캐스트를 쏴 접지 여부를 반환한다.
        // 자기 자신의 Transform 루트는 충돌 결과에서 제외한다.
        // position : 캐스트 시작 위치 (보통 루트 Rigidbody 위치)
        // root     : 자기 자신의 루트 Transform (자기 충돌 제외용)
        // radius   : 구체 반지름
        // distance : 캐스트 거리
        public bool IsGrounded(Vector3 position, Transform root, float radius, float distance)
        {
            var count = Physics.SphereCastNonAlloc(position, radius, Vector3.down, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (_hits[i].transform.root == root)
                    continue;
                return true;
            }

            return false;
        }
    }
}
