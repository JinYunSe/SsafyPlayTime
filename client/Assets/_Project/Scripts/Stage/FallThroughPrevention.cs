using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵 하단으로 낙사한 캐릭터를 지정된 Y 위치로 복귀시킵니다.
/// ImmediateDeath 콜라이더가 막지 못하는 아래쪽 빈 면을 보완합니다.
///
/// - OnTriggerEnter: 빠른 낙하 즉시 감지
/// - OnTriggerStay:  느린 침강 감지
/// - 쿨다운:         같은 캐릭터의 여러 래그돌 뼈대 콜라이더 중복 처리 방지
/// </summary>
public class FallThroughPrevention : MonoBehaviour
{
    [Tooltip("복귀시킬 Y 위치")]
    [SerializeField] private float repositionY = 8f;

    [Tooltip("같은 캐릭터에 대한 최소 재처리 간격 (초)")]
    [SerializeField] private float cooldownSeconds = 1f;

    private readonly Dictionary<NetworkPlayer, float> _lastRepositionTime = new();

    private void OnTriggerEnter(Collider other)
    {
        HandleCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        HandleCollider(other);
    }

    private void HandleCollider(Collider other)
    {
        // 소멸된 NetworkPlayer 참조를 딕셔너리에서 정리 (메모리 누수 방지)
        var stale = new List<NetworkPlayer>();
        foreach (var key in _lastRepositionTime.Keys)
            if (key == null) stale.Add(key);
        foreach (var key in stale)
            _lastRepositionTime.Remove(key);

        // 래그돌 뼈대 콜라이더일 수 있으므로 부모에서 NetworkPlayer를 탐색
        var networkPlayer = other.GetComponentInParent<NetworkPlayer>();
        if (networkPlayer == null)
            return;

        // StateAuthority(호스트)만 위치를 제어
        if (networkPlayer.Object != null && networkPlayer.Object.IsValid && !networkPlayer.HasStateAuthority)
            return;

        // 이미 사망한 캐릭터는 처리하지 않음
        if (networkPlayer.IsDeadState)
            return;

        // 쿨다운: 동일 캐릭터의 복수 뼈대 콜라이더 중복 발동 방지
        var now = Time.time;
        if (_lastRepositionTime.TryGetValue(networkPlayer, out var lastTime) && now - lastTime < cooldownSeconds)
            return;

        _lastRepositionTime[networkPlayer] = now;
        RepositionCharacter(networkPlayer);
    }

    private void RepositionCharacter(NetworkPlayer networkPlayer)
    {
        var currentPos = networkPlayer.transform.position;
        var deltaY = repositionY - currentPos.y;

        // 루트 및 모든 래그돌 뼈대 Rigidbody를 동일한 Y 델타만큼 이동
        // 낙하 속도 초기화 없이는 즉시 다시 뚫릴 수 있으므로 velocity도 리셋
        foreach (var rb in networkPlayer.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.position += new Vector3(0f, deltaY, 0f);
            if (!rb.isKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        // NetworkTransform이 참조하는 루트 transform 동기화
        networkPlayer.transform.position = new Vector3(currentPos.x, repositionY, currentPos.z);

        Debug.Log($"[FallThroughPrevention] '{networkPlayer.name}' 복귀 완료: ({currentPos.x:F1}, {repositionY}, {currentPos.z:F1})");
    }

    private void OnDestroy()
    {
        _lastRepositionTime.Clear();
    }
}
