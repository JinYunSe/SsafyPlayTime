using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 에디터에서 SpawnPoint 위치를 구체 기즈모로 시각화하는 컴포넌트.
// 런타임에는 아무 동작도 하지 않으며, 씬 뷰에서만 표시된다.
public class MyGizmo : MonoBehaviour
{
    // 기즈모 색상 (Inspector에서 조정 가능)
    public Color color = Color.yellow;

    // 기즈모 구체 반지름
    public float radius = 0.1f;

    void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.DrawSphere(transform.position, radius);
    }
}
