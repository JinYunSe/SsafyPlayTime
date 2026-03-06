using System;

using UnityEngine;

// 플레이어의 체력을 관리하고, 시작 시 SpawnManager에서 스폰 위치를 받아 배치하는 컴포넌트.
// (현재는 네트워크 동기화 없이 로컬에서만 동작하는 프로토타입 구현)
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

    // 데미지를 받아 currentHealth를 감소시킨다. 0 이하가 되면 Die()를 호출한다.
    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0)
            return;

        currentHealth -= damage;
        Debug.Log("현재 체력: " + currentHealth);

        if (currentHealth <= 0)
            Die();
    }

    // 체력이 0 이하가 됐을 때 호출. 현재는 오브젝트를 즉시 삭제한다.
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
