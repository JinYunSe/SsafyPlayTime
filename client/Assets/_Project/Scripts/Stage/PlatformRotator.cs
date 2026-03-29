using Fusion;
using UnityEngine;

public class PlatformRotator : NetworkBehaviour
{
    public enum RotationAxis { X, Y, Z }

    public RotationAxis rotationAxis = RotationAxis.Y;
    public float rotationSpeed = 50.0f;

    // Rigidbody 컴포넌트를 Inspector에서 할당해야 합니다.
    public Rigidbody rb;

    [Networked] private float NetworkedAngle { get; set; }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
            NetworkedAngle += rotationSpeed * Runner.DeltaTime;

        rb.MoveRotation(Quaternion.AngleAxis(NetworkedAngle, GetAxisVector()));
    }

    public override void Render()
    {
        // 틱 사이 렌더 프레임을 Alpha(0~1)로 외삽해 부드럽게 회전 표시
        // 등속 회전이라 외삽이 정확하며 다음 틱 도착 시 오차 없음
        var alpha = new NetworkBehaviourBufferInterpolator(this).Alpha;
        var renderAngle = NetworkedAngle + rotationSpeed * Runner.DeltaTime * alpha;
        rb.MoveRotation(Quaternion.AngleAxis(renderAngle, GetAxisVector()));
    }

    // NetworkObject가 없는 경우(씬에 미배치) 폴백
    private void FixedUpdate()
    {
        if (Runner != null)
            return;

        rb.MoveRotation(rb.rotation * Quaternion.AngleAxis(rotationSpeed * Time.fixedDeltaTime, GetAxisVector()));
    }

    private Vector3 GetAxisVector()
    {
        switch (rotationAxis)
        {
            case RotationAxis.X:
                return Vector3.right;
            case RotationAxis.Y:
                return Vector3.up;
            case RotationAxis.Z:
                return Vector3.forward;
            default:
                return Vector3.up;
        }
    }
}
