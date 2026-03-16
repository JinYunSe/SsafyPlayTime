using System.Collections;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private void HandleRuntimeItemDropped(string itemId, ItemDropReason reason)
    {
        if (reason == ItemDropReason.Manual)
            return;

        if (!HasStateAuthority || Runner == null || string.IsNullOrWhiteSpace(itemId))
            return;

        var forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        var spawnPos = transform.position + forward.normalized * 0.6f + Vector3.up * 0.4f;
        if (!TrySpawnNetworkedFieldDrop(itemId, spawnPos, _itemRuntimeHost, out _))
            Debug.LogWarning($"[NetworkPlayer] Failed to spawn networked field drop for {itemId}", this);
    }

    internal bool TrySpawnNetworkedFieldDrop(
        string itemId,
        Vector3 worldPosition,
        ItemRuntimeHost runtimeHost,
        out ItemFieldDrop spawnedDrop)
    {
        spawnedDrop = null;
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return false;

        var spawner = ResolveFieldDropSpawner(runtimeHost);
        if (spawner == null)
        {
            Debug.LogWarning($"[NetworkPlayer] Field drop spawner missing for {itemId}", this);
            return false;
        }

        spawner.SetRuntimeHost(runtimeHost);
        return spawner.TrySpawnItem(itemId, worldPosition, out spawnedDrop);
    }

    internal void TrackDroppedFieldItem(string dropInstanceId)
    {
    }

    internal void TickFieldDropPositionSync()
    {
    }

    internal void BroadcastItemConsumed(string itemId)
    {
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        RPC_NotifyItemConsumed(itemId);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyItemConsumed(string itemId)
    {
        if (HasStateAuthority)
            return;

        _lastReplicatedHeldItemId = string.Empty;
        _heldItemPresenter?.SetReplicatedHeldItemId(string.Empty);

        // 바닥 드롭 제거는 픽업 시 BroadcastPickedFieldDrop(instanceId 기반)에서 처리.
        // 소비 시점에 itemId로 다시 찾으면 동일 아이템이 여럿일 때 잘못된 드롭을 제거할 수 있다.
    }

    private ItemFieldDropSpawner ResolveFieldDropSpawner(ItemRuntimeHost runtimeHost)
    {
        var spawners = FindObjectsOfType<ItemFieldDropSpawner>(true);
        ItemFieldDropSpawner unboundSpawner = null;
        for (var i = 0; i < spawners.Length; i++)
        {
            var spawner = spawners[i];
            if (spawner == null)
                continue;

            if (spawner.RuntimeHost == runtimeHost)
                return spawner;

            if (unboundSpawner == null && spawner.RuntimeHost == null)
                unboundSpawner = spawner;
        }

        if (unboundSpawner != null)
            return unboundSpawner;

        var spawnRoot = runtimeHost != null ? runtimeHost.gameObject : gameObject;
        var localSpawner = spawnRoot.GetComponent<ItemFieldDropSpawner>();
        if (localSpawner != null)
            return localSpawner;

        return spawnRoot.AddComponent<ItemFieldDropSpawner>();
    }

    internal IEnumerator CoResyncAllFieldDropsOnHostMigration()
    {
        yield return null;
    }
}
