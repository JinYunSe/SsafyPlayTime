using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class MapGimmickData : ScriptableObject
{
    public string gimmickName;
    [Tooltip("게임 시작 후 몇 초 뒤에 이 기믹이 시잘될지")]
    public float startDelay;
}
