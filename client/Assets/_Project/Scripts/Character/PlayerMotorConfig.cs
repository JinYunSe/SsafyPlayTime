using UnityEngine;

namespace SSAFYPlayTime.Character
{
    // 캐릭터 이동/점프/접지 판정에 필요한 수치 설정을 담는 ScriptableObject.
    // 캐릭터 프리팹에 할당하며, 수치 조정 시 재컴파일 없이 에셋만 수정하면 된다.
    [CreateAssetMenu(menuName = "SSAFYPlayTime/Character/PlayerMotorConfig")]
    public sealed class PlayerMotorConfig : ScriptableObject
    {
        [Header("Move")]
        public float maxSpeed = 3f;          // 최대 이동 속도 (m/s)
        public float acceleration = 30f;     // 이동 가속도
        public float rotateSpeedDeg = 300f;  // 회전 속도 (도/초)
        public float sprintSpeedMultiplier = 1.8f; // 스프린트 속도 배율

        [Header("Jump/Gravity")]
        public float jumpImpulse = 20f;      // 점프 시 순간 충격량 (Impulse 모드)
        public float extraGravity = 10f;     // 공중에 있을 때 추가 중력 가속도

        [Header("Ground")]
        public float groundProbeRadius = 0.1f;    // 접지 판정 구체 반지름
        public float groundProbeDistance = 0.5f;  // 접지 판정 캐스트 거리

        [Header("State")]
        public float fallEnterSec = 0.8f;      // 이 시간(초) 이상 공중이면 Fall 상태 진입
        public float freeFallEnterSec = 2.0f;  // 이 시간(초) 이상 공중이면 FreeFall 상태 진입
    }
}
