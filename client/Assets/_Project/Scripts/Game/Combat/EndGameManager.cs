using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생존자 수를 추적하고 마지막 1명이 남으면 게임 종료 이벤트를 발생시킵니다.
/// Scene에 하나만 배치하면 됩니다.
/// </summary>
public class EndGameManager : MonoBehaviour
{
    public static EndGameManager Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("총 플레이어 수 (기본 4명)")]
    public int totalPlayers = 4;

    // 현재 살아있는 플레이어 수 (읽기 전용)
    public int AliveCount => _alivePlayers.Count;

    // 게임 종료 이벤트: 마지막 생존자 PlayerStats를 인자로 전달 (null이면 전원 사망)
    public event Action<PlayerStats> OnGameEnd;

    private readonly HashSet<PlayerStats> _alivePlayers = new();
    private bool _gameEnded = false;

    // ─── 싱글턴 ────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ─── 초기화: 씬 내 모든 PlayerStats 자동 등록 ──────────────
    private void Start()
    {
        // 씬에 이미 존재하는 플레이어 자동 등록
        foreach (var ps in FindObjectsByType<PlayerStats>(FindObjectsSortMode.None))
            RegisterPlayer(ps);

        Debug.Log($"[EndGameManager] 초기 생존자: {AliveCount}명 / 전체: {totalPlayers}명");
    }

    // ─── 외부에서 플레이어 등록 (SpawnManager 등에서 호출 가능) ──
    public void RegisterPlayer(PlayerStats player)
    {
        if (player == null || _alivePlayers.Contains(player)) return;

        _alivePlayers.Add(player);
        player.OnDied += OnPlayerDied;
        Debug.Log($"[EndGameManager] 플레이어 등록: {player.name} | 현재 생존자: {AliveCount}명");
    }

    // ─── 플레이어 사망 콜백 ──────────────────────────────────────
    private void OnPlayerDied(PlayerStats deadPlayer)
    {
        if (_gameEnded) return;

        _alivePlayers.Remove(deadPlayer);
        deadPlayer.OnDied -= OnPlayerDied;

        Debug.Log($"[EndGameManager] {deadPlayer.name} 사망! 남은 생존자: {AliveCount}명");

        CheckGameEnd();
    }

    // ─── 종료 조건 검사 ─────────────────────────────────────────
    private void CheckGameEnd()
    {
        if (_gameEnded) return;

        // 생존자가 1명 이하이면 게임 종료
        if (AliveCount <= 1)
        {
            _gameEnded = true;
            PlayerStats winner = AliveCount == 1
                ? GetFirstAlive()
                : null; // 전원 동시 사망

            string winnerName = winner != null ? winner.name : "없음 (전원 사망)";
            Debug.Log($"[EndGameManager] 🏆 게임 종료! 최후 생존자: {winnerName}");

            OnGameEnd?.Invoke(winner);
        }
    }

    private PlayerStats GetFirstAlive()
    {
        foreach (var p in _alivePlayers)
            return p;
        return null;
    }

    // ─── 디버그: 강제 게임 종료 (테스트용) ─────────────────────
    [ContextMenu("강제 게임 종료 (테스트)")]
    public void ForceGameEnd()
    {
        if (!_gameEnded)
        {
            _gameEnded = true;
            OnGameEnd?.Invoke(GetFirstAlive());
        }
    }
}
