using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Gimmicks/MagneticField")]
public class MagneticFieldData : MapGimmickData
{
    public float startRadius = 50f;
    public float endRadius = 5f;
    public float shrinkDuration = 120f; // 축소 완료까지 걸리는 시간
}
