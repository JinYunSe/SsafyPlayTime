using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public Transform[] spawnPoints;
    private int nextSpawnIndex = 0;

    public Vector3 GetSpawnPosition()
    {
        Transform selectedPoint = spawnPoints[nextSpawnIndex];
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Length;

        return selectedPoint.position;
    }
}
