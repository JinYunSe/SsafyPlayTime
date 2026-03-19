using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMapData", menuName = "SO/MapData")]
public class MapData : ScriptableObject
{
    [Header("Basic Info")]
    [SerializeField] private string mapId;
    [SerializeField] private string mapName;
    [SerializeField] private float damageAmount = 20f;
    [SerializeField] private float damageInterval = 1f;
    [SerializeField] private float initialDelay = 0.5f;

    [Header("Gimmicks")]
    // 이 리스트에 원하는 기믹 SO들을 드래그해서 넣습니다.
    [SerializeField] private List<MapGimmickData> gimmicks;

    // 외부에서 데이터를 읽을 때 사용하는 '통로' (Get 전용 프로퍼티)
    public string MapId => mapId;
    public string MapName => mapName;
    public float DamageAmount => damageAmount;
    public float DamageInterval => damageInterval;
    public float InitialDelay => initialDelay;
    public List<MapGimmickData> Gimmicks => gimmicks;
}
