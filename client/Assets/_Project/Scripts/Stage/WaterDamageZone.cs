using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;

// 물 트리거 영역. MeshCollider(IsTrigger=true)와 함께 Water 오브젝트에 부착한다.
// 플레이어가 물 안에 머무르는 동안 일정 간격마다 데미지를 입힌다.
// 데미지는 StateAuthority에서만 처리된다 (ApplyHealthDamage 내부 가드).
public class WaterDamageZone : MonoBehaviour
{
    /*
    [Header("Damage Settings")]
    [SerializeField] private float damageAmount = 20f;
    [SerializeField] private float damageInterval = 1.0f;
    [SerializeField] private float initialDelay = 0.5f;
    [SerializeField] private bool destroyFieldItems = true;
    */

    [Header("Data Source")]
    public MapData mapData; // SO 에셋을 연결할 변수

    private float damageAmount;
    private float damageInterval;
    private float initialDelay;

    private void Awake()
    {
        // mapData가 인스펙터에서 연결되었는지 확인 후 값 할당
        if (mapData != null)
        {
            damageAmount = mapData.DamageAmount; // 프로퍼티(대문자) 사용
            damageInterval = mapData.DamageInterval;
            initialDelay = mapData.InitialDelay;
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: MapData가 할당되지 않았습니다!");
            // 기본값 설정 (에러 방지용)
            damageAmount = 20f;
            damageInterval = 1f;
            initialDelay = 0.5f;
        }
    }

    private readonly Dictionary<NetworkPlayer, Coroutine> _damageCoroutines = new();

    private void OnTriggerEnter(Collider other)
    {
        TryHandleFieldItem(other);

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

    private void TryHandleFieldItem(Collider other)
    {
        if (!destroyFieldItems || other == null)
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

            manager.HandleManagedFieldDropEnteredWater(drop);
            return;
        }

        if (networkObject != null && networkObject.Id.IsValid && networkObject.Runner != null && networkObject.HasStateAuthority)
        {
            networkObject.Runner.Despawn(networkObject);
            return;
        }

        Destroy(drop.gameObject);
    }
}
