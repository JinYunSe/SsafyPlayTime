using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

/// <summary>
/// 즉사 지대에 들어온 플레이어와 필드 아이템을 처리한다.
/// 플레이어는 즉시 사망시키고, 필드 아이템은 스폰 매니저 경유 또는 직접 제거한다.
/// </summary>
public class ImmediateDeath : MonoBehaviour
{
    [SerializeField] private bool destroyFieldItems = true;
    [SerializeField] private bool killPlayers = true;
    [SerializeField] private bool enableDebugLog = true;

    private void OnTriggerEnter(Collider other)
    {
        TryHandleCollider(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
            return;

        TryHandleCollider(collision.collider);
    }

    private void TryHandleCollider(Collider other)
    {
        if (other == null)
            return;

        if (TryHandlePlayer(other))
            return;

        TryHandleFieldItem(other);
    }

    private bool TryHandlePlayer(Collider other)
    {
        if (!killPlayers)
            return false;

        var playerStats = other.GetComponentInParent<PlayerStats>();
        if (playerStats == null || playerStats.IsDead)
            return false;

        var damage = CombatSettings.Instance != null
            ? CombatSettings.Instance.outOfBoundsHpDamage
            : 9999f;

        var networkPlayer = playerStats.GetComponent<NetworkPlayer>();
        if (networkPlayer != null)
        {
            networkPlayer.ApplyHpDamage(damage);
            DebugLog($"Player entered death zone: name={playerStats.name}, damage={damage:F0}");
            return true;
        }

        playerStats.TakeDamage(Mathf.CeilToInt(damage));
        DebugLog($"Offline player entered death zone: name={playerStats.name}, damage={damage:F0}");
        return true;
    }

    private void TryHandleFieldItem(Collider other)
    {
        if (!destroyFieldItems)
            return;

        var drop = other.GetComponentInParent<ItemFieldDrop>();
        if (drop == null)
            drop = other.GetComponent<ItemFieldDrop>();

        if (drop == null)
            return;

        var networkObject = drop.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.Id.IsValid && !networkObject.HasStateAuthority)
            return;

        var managers = FindObjectsOfType<ItemRandomSpawnManager>(true);
        for (var i = 0; i < managers.Length; i++)
        {
            var manager = managers[i];
            if (manager == null || !manager.IsManagedFieldDrop(drop))
                continue;

            manager.HandleManagedFieldDropEnteredDeathZone(drop);
            DebugLog($"Managed field item removed: itemId={drop.ItemId}, instanceId={drop.InstanceId}");
            return;
        }

        DebugLog($"Fallback field item removed: itemId={drop.ItemId}, instanceId={drop.InstanceId}");
        Destroy(drop.gameObject);
    }

    private void DebugLog(string message)
    {
        if (!enableDebugLog)
            return;

        Debug.Log($"[ImmediateDeath] {message}", this);
    }
}
