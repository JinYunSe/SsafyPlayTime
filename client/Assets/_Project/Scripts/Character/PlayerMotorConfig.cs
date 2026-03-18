using UnityEngine;

namespace SSAFYPlayTime.Character
{
    [CreateAssetMenu(menuName = "SSAFYPlayTime/Character/PlayerMotorConfig")]
    public sealed class PlayerMotorConfig : ScriptableObject
    {
        [Header("Move")]
        public float maxSpeed = 3f;
        public float acceleration = 30f;
        public float airAcceleration = 10f;
        public float brakingAcceleration = 45f;
        public float rotateSpeedDeg = 300f;
        public float sprintSpeedMultiplier = 1.8f;
        public float groundStickForce = 6f;
        public float stopSpeedEpsilon = 0.1f;

        [Header("Grab Facing")]
        public float grabYawSoftLimitDeg = 60f;
        public float grabYawHardLimitDeg = 75f;
        public float grabTurnSpeedScale = 0.45f;

        [Header("Jump/Gravity")]
        public float jumpImpulse = 20f;
        public float extraGravity = 10f;

        [Header("Ground")]
        public float groundProbeRadius = 0.1f;
        public float groundProbeDistance = 0.5f;

        [Header("State")]
        public float fallEnterSec = 0.8f;
        public float freeFallEnterSec = 2.0f;

        [Header("Water")]
        public float swimBuoyancy = 8f;
        public float swimSpeedMultiplier = 0.7f;
        public float swimSurfaceThreshold = 0.8f;
    }
}
