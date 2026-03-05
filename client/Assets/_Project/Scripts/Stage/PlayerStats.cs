using System;

using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    public int currentHealth = 100;

    // 죽었을 때 알림 이벤트
    public event Action<PlayerStats> OnDied;

    void Start()
    {
        SpawnManager spawnManager = GameObject.FindObjectOfType<SpawnManager>();

        if (spawnManager != null)
        {
            Vector3 spawnPos = spawnManager.GetSpawnPosition();
            transform.position = spawnPos;
        }
        else
        {
            Debug.LogError("SpawnManager를 찾을 수 없습니다! 씬에 배치했는지 확인하세요");
        }
    }

    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0)
            return;

        currentHealth -= damage;
        Debug.Log("현재 체력: " + currentHealth);

        if (currentHealth <= 0)
            Die();
    }

    void Die()
    {
        currentHealth = 0;

        // 카메라/관전 전환 같은 외부 로직에게 먼저 알림
        OnDied?.Invoke(this);

        // 바로 Destroy하면 다른 스크립트가 참조 중일 때 꼬일 수 있어서
        // 최소 1프레임 뒤에 삭제하는 게 안전
        Destroy(gameObject, 0.05f);
    }
}
