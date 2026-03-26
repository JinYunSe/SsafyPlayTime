using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Gimmicks/SeaLevel")]
public class SeaLevelData : MapGimmickData
{
    public float sinkingSpeed = 0.05f;
    public float maxWaterLevel = 5.0f;
    public float checkInterval = 1.8f;
    public float riseDuration = 0.5f;

    // [Tooltip("X축: 시간(초), Y축: 상승 속도 배율")]
    // public AnimationCurve riseCurve;
    // public float baseSpeed = 1.0f;
}
