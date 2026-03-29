using System.Collections;
using System.Collections.Generic;

using Fusion;

using UnityEngine;

public class NetworkButton : NetworkBehaviour
{
    [Networked] public NetworkBool IsPressed {  get; set; }

    [SerializeField] private Transform buttonVisual; // 실제 움직일 메쉬
    [SerializeField] private float pressDepth = 0.2f; // 내려갈 깊이
    [SerializeField] private float moveSpeed = 10f; // 움직임 속도

    private Vector3 _upPosition;
    private Vector3 _downPosition;

    public override void Spawned()
    {
        _upPosition = buttonVisual.localPosition;
        _downPosition = _upPosition + Vector3.down * pressDepth;
    }

    // 물리적 감지는 호스트(State Authority)에서 결정
    private void OnTriggerStay(Collider other)
    {
        if (HasStateAuthority && other.CompareTag("Player"))
        {
            IsPressed = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (HasStateAuthority && other.CompareTag("Player"))
        {
            IsPressed = false;
        }
    }

    // 시각적 부드러운 이동은 모든 클라이언트에서 실행
    public override void Render()
    {
        Vector3 targetPos = IsPressed ? _downPosition : _upPosition;
        buttonVisual.localPosition = Vector3.Lerp(buttonVisual.localPosition, targetPos, Time.deltaTime * moveSpeed);
    }
}
