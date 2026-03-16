using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tornado : MonoBehaviour
{
    [Header("Force Settings")]
    public float pullStrength = 10f;    // 중심으로 당기는 힘
    public float rotationSpeed = 15f;  // 회전 속도
    public float liftForce = 20f;      // 위로 띄우는 힘

    [Header("Control")]
    public float liftDelay = 1.0f;     // 바닥에서 회전하며 머무는 시간

    // 각 오브젝트가 들어온 시간을 추적하기 위한 딕셔너리 (선택 사항)
    // 단순하게 구현하려면 시간 체크 없이 높이 기반으로 조절해도 됩니다.

    private void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.GetComponent<Rigidbody>();

        if (rb != null)
        {
            // 1. 중심 방향 벡터 계산
            Vector3 centerPos = transform.position;
            Vector3 objectPos = other.transform.position;

            Vector3 directionToCenter = centerPos - objectPos;
            directionToCenter.y = 0; // 수평 방향으로만 당김

            // 2. 중심으로 당기는 힘 (구심력)
            rb.AddForce(directionToCenter.normalized * pullStrength, ForceMode.Acceleration);

            // 3. 회전하는 힘 (접선 방향)
            // 중심 방향과 위쪽 방향을 외적(Cross Product)하면 회전 방향이 나옵니다.
            Vector3 rotationDir = Vector3.Cross(directionToCenter, Vector3.up);
            rb.AddForce(rotationDir.normalized * rotationSpeed, ForceMode.Acceleration);

            // 4. 단계별 상승 로직
            // 바닥(회오리 중심 Y값) 근처에 있을 때는 회전만 시키고, 조금 지나면 위로 쏨
            float heightDiff = objectPos.y - centerPos.y;

            if (heightDiff < 0.5f)
            {
                // 바닥 구간: 아주 약한 상승력만 주어 바닥에 붙어있지 않게 함
                rb.AddForce(Vector3.up * (liftForce * 0.2f), ForceMode.Acceleration);
            }
            else
            {
                // 공중 구간: 본격적으로 위로 밀어 올림
                rb.AddForce(Vector3.up * liftForce, ForceMode.Acceleration);
            }

            // 부드러운 연출을 위해 공기 저항(Drag)을 일시적으로 높여주면 좋습니다.
            rb.drag = 2f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // 영역을 벗어나면 저항을 원래대로 (보통 0)
            rb.drag = 0.05f;
        }
    }
}
