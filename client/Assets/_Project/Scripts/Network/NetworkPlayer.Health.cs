using System;
using Fusion;
using SSAFYPlayTime.Game.GhostThrow;
using UnityEngine;

/// <summary>
/// NetworkPlayer GhostThrow RPC 채널과 사망 알림 이벤트.
/// HP / 사망 상태 관리는 NetworkPlayer.Vitals.cs 에서 담당한다.
/// </summary>
public sealed partial class NetworkPlayer
{
    /// <summary>
    /// 이 플레이어가 사망했을 때 모든 클라이언트에서 발행된다.
    /// Vitals.cs의 UpdateLocalDeathPresentation()에서 발행하므로
    /// InputAuthority 여부와 무관하게 관찰 중인 모든 클라이언트가 수신한다.
    /// </summary>
    public event Action<NetworkPlayer> OnNetworkPlayerDied;

    /// <summary>
    /// GhostThrow 스폰 요청.
    /// InputAuthority 클라이언트에서 호출 → StateAuthority가 아니면 RPC로 전달.
    /// </summary>
    public bool TryRequestGhostThrow(bool isBanana, Vector3 spawnPos, Vector3 direction)
    {
        if (Runner == null || Object == null || !Object.IsValid)
            return false;

        if (HasStateAuthority)
            return TrySpawnGhostThrowOnStateAuthority(isBanana, spawnPos, direction);

        if (!HasInputAuthority)
            return false;

        RPC_RequestGhostThrow(isBanana, spawnPos, direction);
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestGhostThrow(bool isBanana, Vector3 spawnPos, Vector3 direction)
    {
        TrySpawnGhostThrowOnStateAuthority(isBanana, spawnPos, direction);
    }

    private bool TrySpawnGhostThrowOnStateAuthority(bool isBanana, Vector3 spawnPos, Vector3 direction)
    {
        var manager = GhostThrowManager.FindManagerForPlayer(this);
        if (manager == null)
        {
            Debug.LogWarning($"[NetworkPlayer] {gameObject.name}: GhostThrowManager를 찾지 못했습니다.");
            return false;
        }

        return manager.TrySpawnOnlineFromRequest(isBanana, spawnPos, direction);
    }
}
