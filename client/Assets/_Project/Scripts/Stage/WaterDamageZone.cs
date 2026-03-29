using System.Collections;
using System.Collections.Generic;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

// 물 트리거 존.
// 플레이어가 물에 머무는 동안 일정 간격으로 데미지를 주고,
// 필드 아이템이 닿으면 정리한다.
public class WaterDamageZone : MonoBehaviour
{
    [Header("데미지 설정")]
    [SerializeField] private bool destroyFieldItems = true;

    [Header("데이터 소스")]
    public MapData mapData;

    private float damageAmount;
    private float damageInterval;
    private float initialDelay;

    private readonly Dictionary<NetworkPlayer, Coroutine> _damageCoroutines = new();
    private ItemRandomSpawnManager[] _spawnManagers;

    private void Awake()
    {
        _spawnManagers = FindObjectsOfType<ItemRandomSpawnManager>(true);

        if (mapData != null)
        {
            damageAmount = mapData.DamageAmount;
            damageInterval = mapData.DamageInterval;
            initialDelay = mapData.InitialDelay;
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: MapData가 할당되지 않았습니다.");
            damageAmount = 20f;
            damageInterval = 1f;
            initialDelay = 0.5f;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHandleFieldItem(other);

        var networkPlayer = other.GetComponentInParent<NetworkPlayer>();
        if (networkPlayer == null || _damageCoroutines.ContainsKey(networkPlayer))
            return;

        var routine = StartCoroutine(DamageTickRoutine(networkPlayer));
        _damageCoroutines.Add(networkPlayer, routine);

        // 로컬 플레이어에게만 물 오버레이 표시 + 사망 시 즉시 해제 구독
        if (networkPlayer.HasInputAuthority)
        {
            GameHUD.FindOrCreate().ShowWaterOverlay();
            networkPlayer.OnNetworkPlayerDied += OnLocalPlayerDiedInWater;
        }
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

        // 로컬 플레이어에게만 물 오버레이 제거 + 사망 구독 해제
        if (networkPlayer.HasInputAuthority)
        {
            networkPlayer.OnNetworkPlayerDied -= OnLocalPlayerDiedInWater;
            GameHUD.FindOrCreate().HideWaterOverlay();
        }
    }

    // 물속에서 로컬 플레이어가 사망했을 때 수중 효과를 즉시 제거한다.
    // OnTriggerExit는 콜라이더 비활성화 시 신뢰할 수 없으므로 명시적으로 처리.
    private void OnLocalPlayerDiedInWater(NetworkPlayer deadPlayer)
    {
        deadPlayer.OnNetworkPlayerDied -= OnLocalPlayerDiedInWater;

        if (_damageCoroutines.TryGetValue(deadPlayer, out var routine))
        {
            StopCoroutine(routine);
            _damageCoroutines.Remove(deadPlayer);
        }

        GameHUD.FindOrCreate().HideWaterOverlay();
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
        {
            _damageCoroutines.Remove(player);
        }
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

        for (var i = 0; i < _spawnManagers.Length; i++)
        {
            var manager = _spawnManagers[i];
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
