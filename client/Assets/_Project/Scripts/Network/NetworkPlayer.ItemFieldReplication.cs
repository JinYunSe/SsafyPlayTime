using System;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private void BroadcastPickedFieldDrop(string itemId, Vector3 origin)
    {
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        RPC_MarkNearestFieldDropPicked(itemId, origin);
    }

    private void BroadcastDroppedFieldItem(string itemId, Vector3 worldPosition)
    {
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        RPC_SpawnFieldDropReplica(itemId, worldPosition);
    }

    private void EnsureFieldDropReplicaForDrop(string droppedItemId, ItemRuntimeHost runtimeHost, Vector3 spawnPosition)
    {
        if (string.IsNullOrWhiteSpace(droppedItemId))
            return;

        var existing = FindNearestFieldDropByItemId(droppedItemId, spawnPosition, 0.6f);
        if (existing != null)
            return;

        var spawner = ResolveFieldDropSpawner(runtimeHost);
        if (spawner == null)
        {
            Debug.LogWarning($"[NetworkPlayer] 드롭 스포너를 찾지 못해 복제를 생략했다: {droppedItemId}", this);
            return;
        }

        spawner.SetRuntimeHost(runtimeHost);
        if (!spawner.TrySpawnItem(droppedItemId, spawnPosition, out _))
            Debug.LogWarning($"[NetworkPlayer] 드롭 복제 스폰 실패: {droppedItemId}", this);
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

    private static ItemFieldDrop FindNearestFieldDropByItemId(string itemId, Vector3 origin, float maxDistance)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        var drops = FindObjectsOfType<ItemFieldDrop>(true);
        ItemFieldDrop nearest = null;
        var bestDistance = Mathf.Max(0.01f, maxDistance) * Mathf.Max(0.01f, maxDistance);
        for (var i = 0; i < drops.Length; i++)
        {
            var drop = drops[i];
            if (drop == null || drop.IsPickedUp || !string.Equals(drop.ItemId, itemId, System.StringComparison.Ordinal))
                continue;

            var distance = (drop.transform.position - origin).sqrMagnitude;
            if (distance > bestDistance)
                continue;

            bestDistance = distance;
            nearest = drop;
        }

        return nearest;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_MarkNearestFieldDropPicked(string itemId, Vector3 origin)
    {
        if (HasStateAuthority)
            return;

        var drop = FindNearestFieldDropByItemId(itemId, origin, 3f);
        if (drop != null)
            drop.MarkPickedUp();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnFieldDropReplica(string itemId, Vector3 worldPosition)
    {
        if (HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        var spawner = ResolveFieldDropSpawner(_itemRuntimeHost);
        if (spawner == null)
            return;

        var existing = FindNearestFieldDropByItemId(itemId, worldPosition, 0.6f);
        if (existing != null)
            return;

        spawner.TrySpawnItem(itemId, worldPosition, out _);
    }
}
