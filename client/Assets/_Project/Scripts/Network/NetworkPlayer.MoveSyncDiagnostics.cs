using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    internal void UpdateMoveSyncDiagnosticsHotkey()
    {
    }

    internal void TraceMoveAuthorityInput(string source, in PlayerNetworkInput input)
    {
    }

    internal void TraceMoveAuthorityState(string source)
    {
    }

    internal void TraceMovePublish(string source)
    {
    }

    internal void TraceMoveProxyState(string source)
    {
    }

    internal void TraceMoveHoldForce(
        string source,
        Vector3 moveDirection,
        float inputMagnitude,
        float moveSpeedMultiplier,
        Vector3 planarVelocity,
        Vector3 targetVelocity,
        Vector3 velocityDelta,
        float acceleration,
        bool sprintPressed,
        bool recoilActive,
        bool unstableHitPenalty)
    {
    }

    internal void TraceMoveHoldFacing(
        string source,
        float currentYaw,
        float anchorYaw,
        float desiredYaw,
        float currentDelta,
        float desiredDelta,
        float clampedDelta,
        float softLimit,
        float hardLimit,
        float rotateSpeed,
        bool hasMoveInput)
    {
    }
}
