using System;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private void BroadcastPickedFieldDrop(string itemId, string dropInstanceId, Vector3 origin)
    {
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        RPC_MarkFieldDropPicked(itemId, dropInstanceId ?? string.Empty, origin);
    }

    private void BroadcastDroppedFieldItem(string itemId, Vector3 worldPosition, string dropInstanceId)
    {
        if (Runner == null || !HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        RPC_SpawnFieldDropReplica(itemId, worldPosition, dropInstanceId ?? string.Empty);
    }

    private string CreateFieldDropReplicaId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private void EnsureFieldDropReplicaForDrop(string droppedItemId, ItemRuntimeHost runtimeHost, Vector3 spawnPosition, string dropInstanceId)
    {
        if (string.IsNullOrWhiteSpace(droppedItemId))
            return;

        if (!string.IsNullOrWhiteSpace(dropInstanceId))
        {
            var exactDrop = FindFieldDropByInstanceId(dropInstanceId);
            if (exactDrop != null)
                return;
        }

        var existing = FindNearestFieldDropByItemId(droppedItemId, spawnPosition, 0.6f);
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(dropInstanceId))
            {
                existing.SetInstanceId(dropInstanceId);
            }

            return;
        }

        var spawner = ResolveFieldDropSpawner(runtimeHost);
        if (spawner == null)
        {
            Debug.LogWarning($"[NetworkPlayer] Field drop spawner missing for {droppedItemId}", this);
            return;
        }

        spawner.SetRuntimeHost(runtimeHost);
        if (!spawner.TrySpawnItem(droppedItemId, spawnPosition, dropInstanceId, out _))
        {
            Debug.LogWarning($"[NetworkPlayer] Failed to spawn field drop replica for {droppedItemId}", this);
        }
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

    private static ItemFieldDrop FindFieldDropByInstanceId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return null;

        var drops = FindObjectsOfType<ItemFieldDrop>(true);
        for (var i = 0; i < drops.Length; i++)
        {
            var drop = drops[i];
            if (drop == null || !string.Equals(drop.InstanceId, instanceId, StringComparison.Ordinal))
                continue;

            return drop;
        }

        return null;
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
            if (drop == null || drop.IsPickedUp || !string.Equals(drop.ItemId, itemId, StringComparison.Ordinal))
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
    private void RPC_MarkFieldDropPicked(string itemId, string dropInstanceId, Vector3 origin)
    {
        if (HasStateAuthority)
            return;

        var drop = FindFieldDropByInstanceId(dropInstanceId);
        if (drop == null)
        {
            drop = FindNearestFieldDropByItemId(itemId, origin, 3f);
        }

        if (drop != null)
            drop.MarkPickedUp();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnFieldDropReplica(string itemId, Vector3 worldPosition, string dropInstanceId)
    {
        if (HasStateAuthority || string.IsNullOrWhiteSpace(itemId))
            return;

        var spawner = ResolveFieldDropSpawner(_itemRuntimeHost);
        if (spawner == null)
            return;

        var exactDrop = FindFieldDropByInstanceId(dropInstanceId);
        if (exactDrop != null)
            return;

        var existing = FindNearestFieldDropByItemId(itemId, worldPosition, 0.6f);
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(dropInstanceId))
            {
                existing.SetInstanceId(dropInstanceId);
            }

            StabilizeRemoteFieldDrop(existing);
            return;
        }

        if (spawner.TrySpawnItem(itemId, worldPosition, dropInstanceId, out var spawned))
        {
            StabilizeRemoteFieldDrop(spawned);
        }
    }

    private static void StabilizeRemoteFieldDrop(ItemFieldDrop fieldDrop)
    {
        if (fieldDrop == null)
            return;

        var body = fieldDrop.GetComponent<Rigidbody>();
        if (body == null)
            return;

        // 한국어: 현재 필드 아이템은 연속 위치 동기화가 없으므로 원격 복제품은 고정해 물리 드리프트를 막는다.
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.useGravity = false;
    }
}
