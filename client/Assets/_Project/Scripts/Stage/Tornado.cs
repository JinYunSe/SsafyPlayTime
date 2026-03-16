using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tornado : MonoBehaviour
{
    /*[Header("Force Settings")]
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
    }*/

    [Header("Tornado Settings")]
    public float liftSpeed = 5f;
    public float spinSpeed = 300f;

    [Header("Launch Settings")]
    public float launchUpForce = 10f;    // 위로 솟구치는 힘
    public float launchOutForce = 10f;   // 바깥으로 밀어내는 힘

    private Collider tornadoCollider;

    private void Awake() => tornadoCollider = GetComponent<Collider>();

    private void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.GetComponent<Rigidbody>();
        if (rb == null)
            return;

        float topY = tornadoCollider.bounds.max.y;

        // 1. 사출 판정 (꼭대기 근처)
        if (other.transform.position.y >= topY - 0.8f)
        {
            DoArcadeLaunch(rb);
        }
        else
        {
            // 2. 상승 및 회전 (물리 힘보다는 직접 위치 조작 느낌으로)
            // 중심 방향 벡터
            Vector3 centerDir = (transform.position - other.transform.position);
            centerDir.y = 0;

            // 중심으로 당기면서 위로 올림
            rb.velocity = new Vector3(centerDir.x * 2f, liftSpeed, centerDir.z * 2f);

            // 비주얼 회전
            other.transform.Rotate(0, spinSpeed * Time.deltaTime, 0);
        }
    }

    private void DoArcadeLaunch(Rigidbody rb)
    {
        // [핵심] 기존의 모든 관성과 회전속도를 0으로 초기화!
        // 이게 없으면 이전의 회전 관성 때문에 조종이 안 됩니다.
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 사출 방향 (캐릭터가 바라보는 방향 혹은 중심의 바깥 방향)
        Vector3 exitDir = (rb.transform.position - transform.position).normalized;
        exitDir.y = 0;

        // 새로운 깔끔한 힘 부여
        Vector3 finalForce = (Vector3.up * launchUpForce) + (exitDir * launchOutForce);
        rb.AddForce(finalForce, ForceMode.Impulse);

        // [플레이어 제어권 강화]
        // 여기서 플레이어의 이동 스크립트에 "나 지금 날아가는 중이야!"라고 신호를 보냅니다.
        // 예: other.GetComponent<PlayerController>().EnableAirBoost(1.0f);
    }
}
