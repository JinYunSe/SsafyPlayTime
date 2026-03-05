using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MyGizmo : MonoBehaviour
{
    public Color color = Color.yellow;
    public float radius = 0.1f;

    // Start is called before the first frame update
    void OnDrawGizmos()
    {
        // 기즈모 색상 설정
        Gizmos.color = color;
        // 구 형태의 기즈모 생성
        Gizmos.DrawSphere(transform.position, radius);
    }
}
