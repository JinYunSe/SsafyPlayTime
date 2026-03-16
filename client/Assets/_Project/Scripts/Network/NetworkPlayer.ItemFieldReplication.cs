using System;
using System.Collections;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private ItemFieldDrop _trackedFieldDrop;
    private float _nextFieldDropSyncTime;
    private const float FieldDropSyncInterval = 0.1f;
    private const float FieldDropOutOfBoundsY = -10f;
    private const float FieldDropSettledVelocitySqr = 0.01f * 0.01f;

    private void HandleRuntimeItemDropped(string itemId, ItemDropReason reason)
    {
        if (reason == ItemDropReason.Manual)
            return;

        if (!HasStateAuthority || Runner == null)
            return;

        if (string.IsNullOrWhiteSpace(itemId))
            return;

        var forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        var spawnPos = transform.position + forward.normalized * 0.6f + Vector3.up * 0.4f;
        var dropInstanceId = CreateFieldDropReplicaId();
        EnsureFieldDropReplicaForDrop(itemId, _itemRuntimeHost, spawnPos, dropInstanceId);
        BroadcastDroppedFieldItem(itemId, spawnPos, dropInstanceId);
        TrackDroppedFieldItem(dropInstanceId);
    }

    internal void TrackDroppedFieldItem(string dropInstanceId)
    {
        _trackedFieldDrop = FindFieldDropByInstanceId(dropInstanceId);
    }

    internal void TickFieldDropPositionSync()
    {
        if (Runner == null || !HasStateAuthority)
            return;

        if (_trackedFieldDrop == null)
            return;

        if (_trackedFieldDrop.IsPickedUp)
        {
            _trackedFieldDrop = null;
            return;
        }

        var dropPos = _trackedFieldDrop.transform.position;

        // Issue 6: 맵 밖으로 떨어진 드롭 아이템 제거
        if (dropPos.y < FieldDropOutOfBoundsY)
        {
            var instanceId = _trackedFieldDrop.InstanceId;
            Destroy(_trackedFieldDrop.gameObject);
            _trackedFieldDrop = null;
            RPC_DestroyFieldDrop(instanceId);
            return;
        }

        // Issue 5: 물리 정착 감지 - 속도가 임계값 이하면 추적 중단
        var body = _trackedFieldDrop.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic && body.velocity.sqrMagnitude < FieldDropSettledVelocitySqr)
        {
            // 마지막으로 최종 위치 한 번만 동기화 후 추적 종료
            RPC_SyncFieldDropPosition(_trackedFieldDrop.InstanceId, dropPos);
            _trackedFieldDrop = null;
            return;
        }

        var now = (float)Runner.SimulationTime;
        if (now < _nextFieldDropSyncTime)
            return;

        _nextFieldDropSyncTime = now + FieldDropSyncInterval;
        RPC_SyncFieldDropPosition(_trackedFieldDrop.InstanceId, dropPos);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DestroyFieldDrop(string instanceId)
    {
        if (HasStateAuthority)
            return;

        var drop = FindFieldDropByInstanceId(instanceId);
        if (drop != null)
            Destroy(drop.gameObject);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SyncFieldDropPosition(string instanceId, Vector3 position)
    {
        if (HasStateAuthority)
            return;

        var drop = FindFieldDropByInstanceId(instanceId);
        if (drop == null || drop.IsPickedUp)
            return;

        drop.transform.position = position;
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
    }

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

        // 한국어: 원격 복제품은 초기 스폰 시 kinematic으로 고정하고, 이후 RPC_SyncFieldDropPosition으로 위치를 동기화한다.
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.useGravity = false;
    }

    // Issue 8: 호스트 마이그레이션 후 씬에 남아 있는 모든 드롭 아이템 위치를 클라이언트에 재동기화한다.
    internal IEnumerator CoResyncAllFieldDropsOnHostMigration()
    {
        yield return null; // 한 프레임 대기: Spawned 직후 Runner 상태 안정화

        if (Runner == null || !HasStateAuthority)
            yield break;

        var drops = FindObjectsOfType<ItemFieldDrop>(true);
        for (var i = 0; i < drops.Length; i++)
        {
            var drop = drops[i];
            if (drop == null || drop.IsPickedUp)
                continue;

            if (string.IsNullOrWhiteSpace(drop.InstanceId))
                continue;

            RPC_SyncFieldDropPosition(drop.InstanceId, drop.transform.position);
        }
    }
}
