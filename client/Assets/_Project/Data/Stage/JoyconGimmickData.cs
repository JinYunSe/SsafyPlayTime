using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Gimmicks/Joycon")]
public class JoyconGimmickData : MapGimmickData
{
    [Header("Button Settings")]
    public float buttonPressDepth = 0.5f; // 눌리는 깊이
    public float pressSpeed = 5f; // 누르는 속도
    public float releaseSpeed = 2f; // 복귀 속도

    [Header("Random Box Settings")]
    public GameObject boxPrefab; // 소환할 박스 프리팹
    public float boxDestroyDelay = 2.0f; // 박스 파괴 후 완전히 사라지기까지의 시간 (변수화 추천!)

    [Header("Activation Conditions")]
    public int requiredButtonsForStairs = 4; // 발판 활성화에 필요한 버튼 수
}
