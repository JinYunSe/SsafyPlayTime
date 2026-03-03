using UnityEngine;

namespace SSAFYPlayTime.Character
{
    [CreateAssetMenu(menuName = "SSAFYPlayTime/Character/PlayerMotorConfig")]
    public sealed class PlayerMotorConfig : ScriptableObject
    {
        [Header("Move")]
        public float maxSpeed = 3f;
        public float acceleration = 30f;
        public float rotateSpeedDeg = 300f;

        [Header("Jump/Gravity")]
        public float jumpImpulse = 20f;
        public float extraGravity = 10f;

        [Header("Ground")]
        public float groundProbeRadius = 0.1f;
        public float groundProbeDistance = 0.5f;

        [Header("State")]
        public float fallEnterSec = 0.8f;
        public float freeFallEnterSec = 2.0f;
    }
}
