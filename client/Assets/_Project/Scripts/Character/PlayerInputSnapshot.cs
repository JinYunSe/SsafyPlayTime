using UnityEngine;

namespace SSAFYPlayTime.Character
{
    public readonly struct PlayerInputSnapshot
    {
        public readonly Vector2 Move;
        public readonly bool JumpPressed;

        public PlayerInputSnapshot(Vector2 move, bool jumpPressed)
        {
            Move = move;
            JumpPressed = jumpPressed;
        }
    }
}
