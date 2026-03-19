using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewTornadoData", menuName = "Gimmicks/Tornado Data")]
public class TornadoData : ScriptableObject
{
    [Header("Tornado Settings")]
    public float liftSpeed = 5f;
    public float spinSpeed = 300f;

    [Header("Launch Settings")]
    public float launchUpForce = 10f;
    public float launchOutForce = 10f;

    [Tooltip("꼭대기에서 사출되는 지점 (Height - Offset)")]
    public float launchThreshold = 0.8f;
}
