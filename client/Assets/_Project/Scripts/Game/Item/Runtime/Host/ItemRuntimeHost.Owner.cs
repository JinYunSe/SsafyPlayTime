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
