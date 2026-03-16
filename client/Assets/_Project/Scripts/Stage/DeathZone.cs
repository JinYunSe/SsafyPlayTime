using System.Collections;
using System.Collections.Generic;

using UnityEngine;

// 데드존에 플레이어가 머무르면 일정 간격마다 데미지를 입힘
public class DeathZone : MonoBehaviour
{
    [Header("Damage Settings")]
    public int damageAmount = 10;
    public float damageInterval = 1.0f;

    // 플레이어별로 실행 중인 코루틴을 저장 (나갈 때 멈추기 위함)
    private readonly Dictionary<PlayerStats, Coroutine> damageCoroutines = new Dictionary<PlayerStats, Coroutine>();

    private void OnTriggerEnter(Collider other)
    {
        PlayerStats player = other.GetComponentInParent<PlayerStats>();

        // 플레이어 컴포넌트가 있고, 아직 데미지 루프가 시작되지 않았다면
        if (player != null && !damageCoroutines.ContainsKey(player))
        {
            // 코루틴 시작 및 딕셔너리에 저장
            Coroutine newRoutine = StartCoroutine(DamageTickRoutine(player));
            damageCoroutines.Add(player, newRoutine);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerStats player = other.GetComponentInParent<PlayerStats>();

        if (player != null && damageCoroutines.TryGetValue(player, out Coroutine routine))
        {
            // 나갈 때 해당 플레이어의 코루틴만 정지
            StopCoroutine(routine);
            damageCoroutines.Remove(player);
        }
    }

    // 실제 데미지를 주기적으로 입히는 루프
    private IEnumerator DamageTickRoutine(PlayerStats player)
    {
        // 처음 들어왔을 때 바로 데미지를 주지 않으려면 
        // yield return을 먼저 배치합니다.
        yield return new WaitForSeconds(damageInterval);

        while (player != null && player.currentHealth > 0)
        {
            player.TakeDamage(damageAmount);

            // 데미지 간격만큼 대기
            yield return new WaitForSeconds(damageInterval);
        }

        // 만약 죽었다면 관리 목록에서 제거
        if (damageCoroutines.ContainsKey(player))
        {
            damageCoroutines.Remove(player);
        }
    }
}
