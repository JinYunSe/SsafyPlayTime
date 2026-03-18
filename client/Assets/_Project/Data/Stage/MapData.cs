using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMapData", menuName = "SO/MapData")]
public class MapData : ScriptableObject
{
    [Header("Basic Info")]
    public string mapId;
    public string mapName;
    public float deathZoneDamage = 10f;
    public float deathZoneInterval = 1f;

    [Header("Gimmicks")]
    // 이 리스트에 원하는 기믹 SO들을 드래그해서 넣습니다.
    public System.Collections.Generic.List<MapGimmickData> gimmicks;
}
