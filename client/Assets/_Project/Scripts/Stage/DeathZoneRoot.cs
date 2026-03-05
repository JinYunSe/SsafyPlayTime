using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 데스존(DeathZone)의 높이를 시간에 따라 점진적으로 올리는 컴포넌트.
// 일정 간격마다 Y 스케일을 증가시켜 데스존이 서서히 위로 올라오는 효과를 만든다.
public class DeathZoneRoot : MonoBehaviour
{
    [Header("Height Settings")]
    // 한 번에 올라가는 Y 스케일 증가량
    public float heightStep = 0.5f;

    // 스케일이 증가하는 시간 간격 (초)
    public float heightInterval = 5.0f;

    // 올라갈 수 있는 최대 Y 스케일 값
    public float maxHeight = 3.5f;

    // 다음 높이 증가 시각 (Time.time 기준)
    float nextHeightTime;

    // 매 프레임 최대 높이에 도달하지 않았으면 일정 간격으로 Y 스케일을 증가시킨다.
    void Update()
    {
        if (transform.localScale.y < maxHeight)
        {
            if (Time.time >= nextHeightTime)
            {
                transform.localScale += new Vector3(0, heightStep, 0);

                nextHeightTime = Time.time + heightInterval;
            }
        }
    }
}
