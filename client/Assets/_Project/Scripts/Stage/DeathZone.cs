using System.Collections.Generic;

using UnityEngine;

// 데드존에 플레이어가 머무르면 일정 간격마다 데미지를 입힘
public class DeathZone : MonoBehaviour
{
    [Header("Damage Settings")]
    // 한 번에 입히는 데미지
    public int damageAmount = 10;

    // 데미지를 입히는 간격 (초)
    public float damageInterval = 1.0f;

    // 플레이어별 다음 데미지 시간 저장
    private readonly Dictionary<PlayerStats, float> nextDamageTimes = new Dictionary<PlayerStats, float>();

    private void OnTriggerEnter(Collider other)
    {
        PlayerStats player = other.GetComponentInParent<PlayerStats>();
        if (player == null)
            return;

        if (player.currentHealth <= 0)
            return;

        // 처음 들어왔을 때 즉시 데미지를 주지 않고,
        // damageInterval 뒤에 첫 데미지가 들어가도록 예약
        if (!nextDamageTimes.ContainsKey(player))
        {
            nextDamageTimes[player] = Time.time + damageInterval;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        PlayerStats player = other.GetComponentInParent<PlayerStats>();
        if (player == null)
            return;

        if (player.currentHealth <= 0)
            return;

        if (!nextDamageTimes.TryGetValue(player, out float nextTime))
        {
            nextDamageTimes[player] = Time.time + damageInterval;
            return;
        }

        if (Time.time >= nextTime)
        {
            player.TakeDamage(damageAmount);
            nextDamageTimes[player] = Time.time + damageInterval;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerStats player = other.GetComponentInParent<PlayerStats>();
        if (player == null)
            return;

        nextDamageTimes.Remove(player);
    }
}
