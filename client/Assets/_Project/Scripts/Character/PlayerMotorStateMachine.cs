namespace SSAFYPlayTime.Character
{
    // 캐릭터의 이동 상태(Idle/Walk/Jump/Fall/FreeFall)를 관리하는 경량 상태 머신.
    // NetworkPlayer.FixedUpdateNetwork()에서 매 틱 호출되며,
    // 결과는 Animator 파라미터 "MotorState"에 전달된다.
    public sealed class PlayerMotorStateMachine
    {
        // 현재 이동 상태. 외부에서는 읽기만 가능
        public PlayerMotorState CurrentState { get; private set; } = PlayerMotorState.Idle;

        // 접지되지 않은 누적 시간 (공중에 있는 시간)
        private float _airTime;

        // 매 틱 호출. 접지 여부·이동 입력·경과 시간을 바탕으로 상태를 갱신한다.
        // grounded      : 현재 땅에 닿아 있는지
        // moveMagnitude : 이동 입력의 크기 (0이면 정지)
        // deltaTime     : 이번 틱의 경과 시간
        // config        : Fall/FreeFall 전환 임계값 설정
        public void Tick(bool grounded, float moveMagnitude, float deltaTime, PlayerMotorConfig config)
        {
            if (grounded)
            {
                _airTime = 0f;
                CurrentState = moveMagnitude > 0.01f ? PlayerMotorState.Walk : PlayerMotorState.Idle;
                return;
            }

            _airTime += deltaTime;
            if (_airTime >= config.freeFallEnterSec)
            {
                CurrentState = PlayerMotorState.FreeFall;
            }
            else if (_airTime >= config.fallEnterSec)
            {
                CurrentState = PlayerMotorState.Fall;
            }
        }

        // 점프 입력 시 호출. 즉시 Jump 상태로 전환한다.
        public void SetJump()
        {
            CurrentState = PlayerMotorState.Jump;
        }

        // 수영 상태 진입/해제. 물 안에 있을 때 호출.
        public void SetSwimming(bool swimming)
        {
            if (swimming)
            {
                _airTime = 0f;
                CurrentState = PlayerMotorState.Swimming;
            }
            else if (CurrentState == PlayerMotorState.Swimming)
            {
                CurrentState = PlayerMotorState.Fall;
            }
        }
    }
}
