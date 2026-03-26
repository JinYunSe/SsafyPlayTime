using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using SSAFYPlayTime.Gameplay.Items;

/// <summary>
/// 개별 랜덤 박스 오브젝트.
/// 활성화 상태(RandomBoxO)에서 플레이어 충돌을 받으면
/// 비활성화 상태(RandomBoxX)로 전환되며 랜덤 아이템을 위로 팝업 스폰한다.
/// 비활성화 상태로 전환된 뒤 일정 시간이 지나면 자동 Despawn.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public sealed class NetworkedRandomBox : NetworkBehaviour
{
    // ── 비주얼 ──
    [Header("비주얼")]
    [SerializeField] private GameObject activeVisual;   // RandomBoxO 비주얼
    [SerializeField] private GameObject inactiveVisual; // RandomBoxX 비주얼

    // ── 설정 ──
    [Header("설정")]
    [SerializeField] private float despawnDelaySec = 5f;
    [SerializeField] private float itemPopForce = 6f;
    [SerializeField] private float itemPopScatterRadius = 0.3f;
    [SerializeField] private float hitCooldownSec = 0.5f;

    // ── 디버그 ──
    [Header("디버그")]
    [SerializeField] private bool enableDebugLog = false;

    // ── 네트워크 상태 ──
    [Networked] private NetworkBool IsOpened { get; set; }
    [Networked] private TickTimer DespawnTimer { get; set; }

    // ── 로컬 상태 ──
    private float _lastHitTime = -999f;

    // ── 네트워크 프리팹 로드 경로 (ItemFieldDropFactory.NetworkedDropResourcePath와 동일) ──
    private const string NetworkedDropResourcePath = "_Project/Prefabs/Items/NetworkedFieldItemDrop";

    // ────────────────────────────────────────
    // Fusion 라이프사이클
    // ────────────────────────────────────────

    public override void Spawned()
    {
        SyncVisuals();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (IsOpened && DespawnTimer.Expired(Runner))
        {
            DebugLog("Despawn timer expired, removing box.");
            Runner.Despawn(Object);
        }
    }

    public override void Render()
    {
        SyncVisuals();
    }

    // ────────────────────────────────────────
    // 펀치(충돌) 감지
    // ────────────────────────────────────────

    /// <summary>
    /// 플레이어의 물리 뼈(래그돌 파트)가 박스 콜라이더에 부딪히면 호출.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (!HasStateAuthority || IsOpened)
            return;

        // 쿨다운
        if (Time.time - _lastHitTime < hitCooldownSec)
            return;

        // 플레이어인지 확인
        var player = collision.collider.GetComponentInParent<NetworkPlayer>();
        if (player == null)
            return;

        _lastHitTime = Time.time;
        Open();
    }

    // ────────────────────────────────────────
    // 박스 열기
    // ────────────────────────────────────────

    private void Open()
    {
        if (IsOpened)
            return;

        IsOpened = true;

        // 1) 비주얼 전환 (전 클라이언트)
        RPC_OnBoxOpened();

        // 2) 랜덤 아이템 스폰 (서버만)
        SpawnRandomItemPopup();

        // 3) Despawn 타이머 시작
        DespawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.5f, despawnDelaySec));
        DebugLog($"Box opened. Despawn in {despawnDelaySec:F1}s.");
    }

    // ────────────────────────────────────────
    // 비주얼 동기화
    // ────────────────────────────────────────

    private void SyncVisuals()
    {
        if (activeVisual != null)
            activeVisual.SetActive(!IsOpened);

        if (inactiveVisual != null)
            inactiveVisual.SetActive(IsOpened);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnBoxOpened()
    {
        SyncVisuals();
        DebugLog("RPC_OnBoxOpened received — visuals switched.");
    }

    // ────────────────────────────────────────
    // 아이템 팝업 스폰
    // ────────────────────────────────────────

    private void SpawnRandomItemPopup()
    {
        if (Runner == null)
        {
            DebugLog("Cannot spawn item: Runner is null.");
            return;
        }

        // 네트워크 프리팹 로드
        var prefab = Resources.Load<GameObject>(NetworkedDropResourcePath);
        if (prefab == null)
        {
            DebugLog($"Cannot spawn item: prefab not found at Resources/{NetworkedDropResourcePath}");
            return;
        }

        // 랜덤 아이템 ID 선택
        var itemId = PickRandomItemId();
        if (string.IsNullOrEmpty(itemId))
        {
            DebugLog("Cannot spawn item: no valid item ID found.");
            return;
        }

        // 스폰 위치: 박스 위쪽
        var spawnPosition = transform.position + Vector3.up * 0.5f;

        var spawnedObject = Runner.Spawn(
            prefab,
            spawnPosition,
            Quaternion.identity,
            onBeforeSpawned: (_, obj) =>
            {
                var networkedDrop = obj.GetComponent<NetworkedItemFieldDrop>();
                if (networkedDrop != null)
                {
                    networkedDrop.ResetForSpawn(itemId, spawnPosition, Quaternion.identity);
                    networkedDrop.InitializeMetadata(itemId);
                }
            });

        if (spawnedObject == null)
        {
            DebugLog($"Runner.Spawn failed for item: {itemId}");
            return;
        }

        // 위쪽으로 팝업 임펄스
        var body = spawnedObject.GetComponent<Rigidbody>();
        if (body != null)
        {
            var scatter = UnityEngine.Random.insideUnitCircle * itemPopScatterRadius;
            var popDirection = (Vector3.up + new Vector3(scatter.x, 0f, scatter.y) * 0.3f).normalized;
            body.isKinematic = false;
            body.useGravity = true;
            body.AddForce(popDirection * itemPopForce, ForceMode.VelocityChange);
        }

        DebugLog($"Spawned popup item: {itemId} at {spawnPosition}");
    }

    // ────────────────────────────────────────
    // 랜덤 아이템 선택
    // ────────────────────────────────────────

    private string PickRandomItemId()
    {
        var options = ItemCatalogLoader.CreateDefaultOptions();
        var provider = new ItemFieldCatalogProvider();

        if (!provider.TryGetCatalog(options, out var catalog, out var error))
        {
            DebugLog($"Failed to load item catalog: {error}");
            return null;
        }

        var validIds = new List<string>();
        foreach (var pair in catalog.Definitions)
        {
            var def = pair.Value;
            if (def == null || !def.Master.Enabled)
                continue;
            if (string.IsNullOrWhiteSpace(def.Master.ItemId) ||
                string.IsNullOrWhiteSpace(def.Master.PrefabPath))
                continue;
            // WaterMelonSword 제외
            if (string.Equals(def.Master.ItemId, ItemIds.WaterMelonSword, StringComparison.Ordinal))
                continue;

            validIds.Add(def.Master.ItemId);
        }

        if (validIds.Count == 0)
            return null;

        return validIds[UnityEngine.Random.Range(0, validIds.Count)];
    }

    // ────────────────────────────────────────
    // 디버그
    // ────────────────────────────────────────

    private void DebugLog(string message)
    {
        if (!enableDebugLog)
            return;

        Debug.Log($"[NetworkedRandomBox] {message}", this);
    }
}
