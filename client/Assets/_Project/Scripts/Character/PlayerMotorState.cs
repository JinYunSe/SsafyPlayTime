namespace SSAFYPlayTime.Character
{
    // 플레이어 이동 상태를 나타내는 열거형.
    // NetworkPlayer의 Animator 파라미터 "MotorState"에 int로 전달된다.
    public enum PlayerMotorState
    {
        Idle,        // 정지 상태
        Walk,        // 이동 중
        Jump,        // 점프 직후
        Fall,        // 짧게 공중에 뜬 상태 (fallEnterSec 이상)
        FreeFall,    // 장시간 낙하 상태 (freeFallEnterSec 이상)
        Unconscious, // 기절 상태 (미사용 예약)
        Swimming     // 수영 상태 (물 안)
    }
}
