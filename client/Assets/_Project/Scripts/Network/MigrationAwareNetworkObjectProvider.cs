using System.Collections.Generic;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

/// <summary>
/// Preserves scene objects for host migration and pools the networked field-drop wrapper prefab.
/// </summary>
public class MigrationAwareNetworkObjectProvider : NetworkObjectProviderDefault
{
    [SerializeField] private bool enablePoolDebugLog;

    private readonly Dictionary<NetworkPrefabId, Stack<NetworkObject>> _pooledPrefabs = new();

    public override NetworkObjectAcquireResult AcquirePrefabInstance(
        NetworkRunner runner,
        in NetworkPrefabAcquireContext context,
        out NetworkObject instance)
    {
        instance = null;

        if (DelayIfSceneManagerIsBusy && runner.SceneManager.IsBusy)
        {
            return NetworkObjectAcquireResult.Retry;
        }

        NetworkObject prefab;
        try
        {
            prefab = runner.Prefabs.Load(context.PrefabId, isSynchronous: context.IsSynchronous);
        }
        catch (System.Exception ex)
        {
            Log.Error($"Failed to load prefab: {ex}");
            return NetworkObjectAcquireResult.Failed;
        }

        if (!prefab)
        {
            return NetworkObjectAcquireResult.Retry;
        }

        if (ShouldPoolFieldDropPrefab(prefab) &&
            TryAcquirePooledInstance(context.PrefabId, out instance))
        {
            PreparePooledInstanceForReuse(runner, context, instance);
            if (enablePoolDebugLog)
            {
                Debug.Log($"[MigrationAwareNetworkObjectProvider] Reused pooled field-drop wrapper: prefabId={context.PrefabId}, name={instance.name}", this);
            }
            runner.Prefabs.AddInstance(context.PrefabId);
            return NetworkObjectAcquireResult.Success;
        }

        return base.AcquirePrefabInstance(runner, context, out instance);
    }

    public override void ReleaseInstance(NetworkRunner runner, in NetworkObjectReleaseContext context)
    {
        var instance = context.Object;

        if (!context.IsBeingDestroyed &&
            context.TypeId.IsPrefab &&
            instance != null &&
            ShouldPoolFieldDropPrefab(instance))
        {
            ReturnFieldDropInstanceToPool(context.TypeId.AsPrefabId, instance);
            runner.Prefabs.RemoveInstance(context.TypeId.AsPrefabId);
            return;
        }

        base.ReleaseInstance(runner, context);
    }

    protected override void DestroySceneObject(
        NetworkRunner runner,
        NetworkSceneObjectId sceneObjectId,
        NetworkObject instance)
    {
        Debug.Log(
            $"[MigrationAwareNetworkObjectProvider] Preserving scene object '{instance.name}' (id={sceneObjectId}) for host migration.");
    }

    private static bool ShouldPoolFieldDropPrefab(NetworkObject prefabOrInstance)
    {
        return prefabOrInstance != null &&
               prefabOrInstance.GetComponent<NetworkedItemFieldDrop>() != null &&
               prefabOrInstance.GetComponent<ItemFieldDrop>() != null;
    }

    private bool TryAcquirePooledInstance(NetworkPrefabId prefabId, out NetworkObject instance)
    {
        instance = null;
        if (!_pooledPrefabs.TryGetValue(prefabId, out var pool))
        {
            return false;
        }

        while (pool.Count > 0)
        {
            instance = pool.Pop();
            if (instance != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void PreparePooledInstanceForReuse(
        NetworkRunner runner,
        in NetworkPrefabAcquireContext context,
        NetworkObject instance)
    {
        if (instance == null)
        {
            return;
        }

        instance.gameObject.SetActive(true);
        if (context.DontDestroyOnLoad)
        {
            runner.MakeDontDestroyOnLoad(instance.gameObject);
        }
        else
        {
            runner.MoveToRunnerScene(instance.gameObject);
        }
    }

    private void ReturnFieldDropInstanceToPool(NetworkPrefabId prefabId, NetworkObject instance)
    {
        if (instance == null)
        {
            return;
        }

        if (!_pooledPrefabs.TryGetValue(prefabId, out var pool))
        {
            pool = new Stack<NetworkObject>();
            _pooledPrefabs[prefabId] = pool;
        }

        var networkedDrop = instance.GetComponent<NetworkedItemFieldDrop>();
        if (networkedDrop != null)
        {
            networkedDrop.ResetForDespawn();
        }

        var body = instance.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
        }

        var colliders = instance.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }

        instance.gameObject.SetActive(false);
        pool.Push(instance);

        if (enablePoolDebugLog)
        {
            Debug.Log($"[MigrationAwareNetworkObjectProvider] Returned field-drop wrapper to pool: prefabId={prefabId}, pooledCount={pool.Count}, name={instance.name}", this);
        }
    }
}
