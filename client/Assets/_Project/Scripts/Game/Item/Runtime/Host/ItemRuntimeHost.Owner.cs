/*
 * 파일 개요:
 * - ItemRuntimeHost.Owner 스크립트가 들어 있는 파일이다.
 * - Runtime/Host 계층에서 MonoBehaviour 기반 진입점과 외부 시스템 이벤트 연결을 담당한다.
 * - 씬, 캐릭터, Dev 러너가 런타임 컨트롤러를 사용할 때 거치는 허브 역할이므로 참조 안정성을 우선 유지한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemRuntimeHost
    {
        // 준비되지 않은 경우 1회 초기화를 시도하고 실패 사유를 반환한다.
        private bool EnsureReady(out string reason)
        {
            if (_controller != null)
            {
                reason = string.Empty;
                return true;
            }

            if (Initialize())
            {
                reason = string.Empty;
                return true;
            }

            reason = string.IsNullOrWhiteSpace(_lastError) ? "ItemRuntimeHost is not initialized." : _lastError;
            return false;
        }

        private Vector3 ResolveOwnerPosition()
        {
            return ownerTransform != null ? ownerTransform.position : transform.position;
        }

        private Vector3 ResolveOwnerForward()
        {
            var forward = ownerTransform != null ? ownerTransform.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemRuntimeHost] {message}", this);
        }
    }
}

