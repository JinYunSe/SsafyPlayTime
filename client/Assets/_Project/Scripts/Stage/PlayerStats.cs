using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 플레이어의 체력을 관리하고, 시작 시 SpawnManager에서 스폰 위치를 받아 배치하는 컴포넌트.
// (현재는 네트워크 동기화 없이 로컬에서만 동작하는 프로토타입 구현)
public class PlayerStats : MonoBehaviour
{
    // 현재 체력 (0 이하가 되면 Die() 호출)
    public int currentHealth = 100;

    // 게임 시작 시 씬의 SpawnManager를 찾아 스폰 위치를 지정한다.
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
            Debug.LogError("SpawnManager를 찾을 수 없습니다! 씬에 배치했는지 확인하세요.");
        }
    }

    // 데미지를 받아 currentHealth를 감소시킨다. 0 이하가 되면 Die()를 호출한다.
    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        Debug.Log("현재 체력: " + currentHealth);

        if (currentHealth <= 0)
            Die();
    }

    // 체력이 0 이하가 됐을 때 호출. 현재는 오브젝트를 즉시 삭제한다.
    void Die()
    {
        Destroy(gameObject);
    }
}
