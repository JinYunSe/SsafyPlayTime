using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlatformRotator : MonoBehaviour
{
    public enum RotationAxis { X, Y, Z }

    public RotationAxis rotationAxis = RotationAxis.Y;
    public float rotationSpeed = 50.0f;

    // Rigidbody 컴포넌트를 Inspector에서 할당해야 합니다.
    public Rigidbody rb;

    void FixedUpdate() // 기존 Update 대신 FixedUpdate 사용
    {
        float rotationValue = rotationSpeed * Time.fixedDeltaTime;

        // 쿼터니언을 사용하여 회전값 계산
        Quaternion deltaRotation = Quaternion.AngleAxis(rotationValue, GetAxisVector());

        // 물리 엔진을 통해 회전 적용
        rb.MoveRotation(rb.rotation * deltaRotation);
    }

    Vector3 GetAxisVector()
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
