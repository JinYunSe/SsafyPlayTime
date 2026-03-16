using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

/// <summary>
/// 아이템이나 플레이어가 즉사 구역에 닿았을 때 처리하는 스테이지 기믹.
/// 현재는 필드 아이템 정리 용도로 먼저 사용한다.
/// </summary>
public class ImmediateDeath : MonoBehaviour
{
    [SerializeField] private bool destroyFieldItems = true;
    [SerializeField] private bool enableDebugLog = true;

    private void OnTriggerEnter(Collider other)
    {
        TryHandleFieldItem(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
        {
            return;
        }

        TryHandleFieldItem(collision.collider);
    }

    private void TryHandleFieldItem(Collider other)
    {
        if (!destroyFieldItems || other == null)
        {
            return;
        }

        var drop = other.GetComponentInParent<ItemFieldDrop>();
        if (drop == null)
        {
            drop = other.GetComponent<ItemFieldDrop>();
        }

        if (drop == null)
        {
            return;
        }

        var networkObject = drop.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.Id.IsValid && !networkObject.HasStateAuthority)
        {
            return;
        }

        var managers = FindObjectsOfType<ItemRandomSpawnManager>(true);
        for (var i = 0; i < managers.Length; i++)
        {
            var manager = managers[i];
            if (manager == null || !manager.IsManagedFieldDrop(drop))
            {
                continue;
            }

            manager.HandleManagedFieldDropEnteredDeathZone(drop);
            DebugLog($"관리 스폰 아이템 제거: itemId={drop.ItemId}, instanceId={drop.InstanceId}");
            return;
        }

        // 한국어: 관리 대상이 아닌 일반 필드 아이템도 로컬에서는 정리한다.
        DebugLog($"일반 필드 아이템 제거: itemId={drop.ItemId}, instanceId={drop.InstanceId}");
        Destroy(drop.gameObject);
    }

    private void DebugLog(string message)
    {
        if (!enableDebugLog)
        {
            return;
        }

        Debug.Log($"[ImmediateDeath] {message}", this);
    }
}
