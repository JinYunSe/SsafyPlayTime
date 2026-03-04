namespace SSAFYPlayTime.Character
{
    public sealed class PlayerMotorStateMachine
    {
        public PlayerMotorState CurrentState { get; private set; } = PlayerMotorState.Idle;

        private float _airTime;

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

        public void SetJump()
        {
            CurrentState = PlayerMotorState.Jump;
        }
    }
}
