using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

// 데스존 안에 들어온 플레이어를 처리한다.
public class DeathZone : MonoBehaviour
{
    [Header("Kill Settings")]
    [SerializeField] private bool killInstantly = true;

    [Header("Damage Settings")]
    [SerializeField] private int damageAmount = 10;
    [SerializeField] private float damageInterval = 1.0f;

    private readonly Dictionary<PlayerStats, Coroutine> damageCoroutines = new Dictionary<PlayerStats, Coroutine>();

    private void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<PlayerStats>();
        if (player == null)
            return;

        if (killInstantly)
        {
            TryKillPlayer(player);
            return;
        }

        if (!damageCoroutines.ContainsKey(player))
        {
            var newRoutine = StartCoroutine(DamageTickRoutine(player));
            damageCoroutines.Add(player, newRoutine);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!killInstantly)
            return;

        var player = other.GetComponentInParent<PlayerStats>();
        if (player != null)
            TryKillPlayer(player);
    }

    private void OnTriggerExit(Collider other)
    {
        if (killInstantly)
            return;

        var player = other.GetComponentInParent<PlayerStats>();
        if (player != null && damageCoroutines.TryGetValue(player, out var routine))
        {
            StopCoroutine(routine);
            damageCoroutines.Remove(player);
        }
    }

    private IEnumerator DamageTickRoutine(PlayerStats player)
    {
        yield return new WaitForSeconds(damageInterval);

        while (player != null && player.currentHealth > 0)
        {
            player.TakeDamage(damageAmount);
            yield return new WaitForSeconds(damageInterval);
        }

        if (damageCoroutines.ContainsKey(player))
            damageCoroutines.Remove(player);
    }

    private static void TryKillPlayer(PlayerStats player)
    {
        if (player == null || player.IsDead)
            return;

        var networkPlayer = player.GetComponent<NetworkPlayer>();
        if (networkPlayer != null)
        {
            networkPlayer.KillImmediately("DeathZone");
            return;
        }

        player.TakeDamage(Mathf.Max(1, player.currentHealth));
    }
}
