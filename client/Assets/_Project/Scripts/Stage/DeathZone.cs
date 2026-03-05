using System.Collections;
using System.Collections.Generic;

using UnityEngine;

// 데스존(낙사 구역) 콜라이더 안에 플레이어가 머무르면
// 일정 간격마다 데미지를 입히는 컴포넌트.
public class DeathZone : MonoBehaviour
{
    [Header("Damage Settings")]
    // 한 번에 입히는 데미지량
    public int damageAmount = 10;

    // 데미지를 반복하는 시간 간격 (초)
    public float damageInterval = 1.0f;

    // 다음 데미지를 입힐 수 있는 시각 (Time.time 기준)
    float nextDamageTime;

    // 콜라이더 안에 플레이어가 있는 동안 매 물리 프레임마다 호출.
    // "Player" 태그 오브젝트의 PlayerStats에 데미지를 전달한다.
    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (Time.time >= nextDamageTime)
            {
                PlayerStats player = other.GetComponent<PlayerStats>();
                if (player != null)
                {
                    player.TakeDamage(damageAmount);
                    nextDamageTime = Time.time + damageInterval;
                }
            }
        }
    }
}
