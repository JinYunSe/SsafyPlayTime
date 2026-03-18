using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 HP 이벤트 어댑터.
/// 실제 HP 로직은 NetworkPlayer.Health.cs가 담당하며,
/// 이 클래스는 OnDied 이벤트를 EndGameManager에 중계하는 역할만 한다.
/// HP 바는 게임 내에서 노출하지 않음 (숨김 처리).
/// </summary>
public class PlayerStats : MonoBehaviour
{
    // EndGameManager가 구독하는 사망 이벤트 (인터페이스 유지)
    public event Action<PlayerStats> OnDied;

    // 현재 생존 여부 (EndGameManager용)
    public bool IsDead { get; private set; } = false;

    // 하위 호환: 외부에서 currentHealth를 읽는 코드를 위한 프로퍼티
    public int currentHealth => IsDead ? 0 : 1;

    private void Start()
    {
        // Fusion 네트워크 캐릭터: 스폰 위치는 서버가 결정하므로 로컬 이동 생략
        var networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.Runner != null && networkObject.IsValid)
            return;

        // 오프라인/로컬 테스트: SpawnManager로 위치 결정
        var spawnManager = FindObjectOfType<SpawnManager>();
        if (spawnManager != null)
            transform.position = spawnManager.GetSpawnPosition();
    }

    /// <summary>
    /// NetworkPlayer.Health.cs의 RPC_OnPlayerDied에서 호출.
    /// EndGameManager에 사망 이벤트를 중계한다.
    /// </summary>
    public void NotifyDeathFromNetwork()
    {
        if (IsDead) return;
        IsDead = true;
        OnDied?.Invoke(this);
        Debug.Log($"[PlayerStats] {gameObject.name} 사망 이벤트 중계 완료");
    }

    /// <summary>
    /// 오프라인/레거시 코드 호환용. 네트워크 캐릭터에서는 NetworkPlayer.ApplyHpDamage()를 사용한다.
    /// </summary>
    public void TakeDamage(int damage)
    {
        var np = GetComponent<NetworkPlayer>();
        if (np != null)
        {
            np.ApplyHpDamage(damage);
            return;
        }

        // 순수 오프라인 폴백
        if (IsDead) return;
        IsDead = true;
        OnDied?.Invoke(this);
        Destroy(gameObject, 0.05f);
    }
}
