using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 물 트리거 영역. MeshCollider(IsTrigger=true)와 함께 Water 오브젝트에 부착한다.
// 플레이어가 물 안에 머무르는 동안 일정 간격마다 데미지를 입힌다.
// 데미지는 StateAuthority에서만 처리된다 (ApplyHealthDamage 내부 가드).
public class WaterDamageZone : MonoBehaviour
{
    [Header("Damage Settings")]
    [SerializeField] private float damageAmount = 20f;
    [SerializeField] private float damageInterval = 1.0f;
    [SerializeField] private float initialDelay = 0.5f;

    private readonly Dictionary<NetworkPlayer, Coroutine> _damageCoroutines = new();

    private void OnTriggerEnter(Collider other)
    {
        var networkPlayer = other.GetComponentInParent<NetworkPlayer>();
        if (networkPlayer == null || _damageCoroutines.ContainsKey(networkPlayer))
            return;

        var routine = StartCoroutine(DamageTickRoutine(networkPlayer));
        _damageCoroutines.Add(networkPlayer, routine);
    }

    private void OnTriggerExit(Collider other)
    {
        var networkPlayer = other.GetComponentInParent<NetworkPlayer>();
        if (networkPlayer == null)
            return;

        if (_damageCoroutines.TryGetValue(networkPlayer, out var routine))
        {
            StopCoroutine(routine);
            _damageCoroutines.Remove(networkPlayer);
        }
    }

    private IEnumerator DamageTickRoutine(NetworkPlayer player)
    {
        yield return new WaitForSeconds(initialDelay);

        while (player != null && !player.IsDeadState)
        {
            player.ApplyHealthDamage(damageAmount);
            yield return new WaitForSeconds(damageInterval);
        }

        if (_damageCoroutines.ContainsKey(player))
            _damageCoroutines.Remove(player);
    }
}
