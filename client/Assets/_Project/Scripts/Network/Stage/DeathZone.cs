using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeathZone : MonoBehaviour
{
    [Header("Damage Settings")]
    public int damageAmount = 10;
    public float damageInterval = 1.0f;
    float nextDamageTime;

    void OnTriggerStay(Collider other)
    {
        // 플레이어 태그로 확인
        if (other.CompareTag("Player"))
        {
            if (Time.time >= nextDamageTime)
            {
                // PlayerStats 라는 파일이 플레이어 component에 있다고 가정을 하고 진행
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
