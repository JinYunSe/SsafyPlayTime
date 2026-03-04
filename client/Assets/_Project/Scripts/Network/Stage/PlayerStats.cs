using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    // 일단 PlayerStats 파일 프리팹에 오버라이드를 함
    public int currentHealth = 100;

    // 일단 플레이어 위치도 여기 Script에 구현함
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

    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        Debug.Log("현재 체력: " + currentHealth);

        if (currentHealth <= 0)
            Die();
    }

    void Die()
    {
        Destroy(gameObject);
    }
}
