using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 씬에 배치된 스폰 포인트를 순서대로 순환하며 위치를 제공하는 단순 스폰 매니저.
// (현재 PlayerStats.Start()에서 호출되며, 네트워크 동기화 없이 로컬에서만 동작한다.)
public class SpawnManager : MonoBehaviour
{
    // Inspector에서 할당하는 스폰 포인트 배열
    public Transform[] spawnPoints;

    // 다음 스폰에 사용할 포인트 인덱스 (순환 방식으로 증가)
    private int nextSpawnIndex = 0;

    // 현재 인덱스의 스폰 위치를 반환하고 다음 인덱스로 넘어간다.
    public Vector3 GetSpawnPosition()
    {
        Transform selectedPoint = spawnPoints[nextSpawnIndex];
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Length;

        return selectedPoint.position;
    }
}
