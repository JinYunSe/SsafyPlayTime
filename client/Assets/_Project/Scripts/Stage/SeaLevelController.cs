using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SeaLevelController : MonoBehaviour
{
    [Header("Height Settings")]
    // 상승 속도
    public float sinkingSpeed = 0.05f;
    // 도달할 최대 높이
    public float maxWaterLevel = 5.0f;
    // 상승을 시작할 주기 (예: 1초마다 조금씩)
    public float checkInterval = 1.0f;

    private bool isRising = false;

    void Start()
    {
        // 게임 시작 시 수위 상승 코루틴 시작
        StartCoroutine(RiseSeaLevelRoutine());
    }

    IEnumerator RiseSeaLevelRoutine()
    {
        // 수위가 최대 높이보다 낮을 때만 반복
        while (transform.position.y < maxWaterLevel)
        {
            // 목표 높이 계산 (현재 높이 + 상승 속도)
            float targetY = transform.position.y + sinkingSpeed;

            // 만약 목표 높이가 최대치를 넘는다면 최대치로 고정
            if (targetY > maxWaterLevel)
                targetY = maxWaterLevel;

            // [자연스러운 이동] 목표 높이까지 부드럽게 이동
            yield return StartCoroutine(SmoothMove(targetY));

            // 다음 상승까지 대기 시간 (주기적으로 올리고 싶을 때)
            yield return new WaitForSeconds(checkInterval);
        }

        Debug.Log("최대 수위에 도달했습니다.");
    }

    IEnumerator SmoothMove(float targetY)
    {
        // 이동에 걸리는 시간 (작을수록 빠름)
        float duration = 0.5f;
        float elapsed = 0f;
        Vector3 startPos = transform.position;
        Vector3 endPos = new Vector3(startPos.x, targetY, startPos.z);

        while (elapsed < duration)
        {
            // Lerp를 사용하여 부드럽게 이동
            transform.position = Vector3.Lerp(startPos, endPos, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null; // 다음 프레임까지 대기
        }

        transform.position = endPos;
    }
}
