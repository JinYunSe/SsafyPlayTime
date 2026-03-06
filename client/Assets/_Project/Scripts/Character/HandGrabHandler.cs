using UnityEngine;

/// <summary>
/// 멀티플레이 대응 그랩 핸들러.
/// 
/// 좌클릭 꾹 = 그랩 모드
///   - OnCollisionEnter에서 FixedJoint 생성 (물체 or 다른 플레이어)
///   - 다른 플레이어가 active ragdoll → 붙잡아서 이동 방해
///   - 다른 플레이어가 기절 → 아이템처럼 들고 다니다가 던지기 가능
/// 좌클릭 연타 = 펀치 (HandGrabHandler 밖에서 NetworkPlayer가 처리)
///
/// StateAuthority(호스트)에서만 물리 연산 실행.
/// </summary>
public class HandGrabHandler : MonoBehaviour
{
    [SerializeField] Animator animator;

    // 런타임에 생성되는 FixedJoint
    FixedJoint fixedJoint;

    // 이 손의 Rigidbody
    Rigidbody rigidbody3D;

    // 상위 NetworkPlayer 참조
    NetworkPlayer networkPlayer;

    // 홀드 포인트 (인터페이스 호환)
    Transform _holdPoint;

    // 잡힌 대상이 플레이어인지 추적
    NetworkPlayer _grabbedPlayer;

    /// <summary>현재 무언가를 잡고 있는지</summary>
    public bool IsHolding => fixedJoint != null;

    /// <summary>잡힌 대상이 기절한 플레이어인지</summary>
    public bool IsHoldingStunnedPlayer => _grabbedPlayer != null && !_grabbedPlayer.IsActiveRagdoll;

    void Awake()
    {
        networkPlayer = transform.root.GetComponent<NetworkPlayer>();
        rigidbody3D = GetComponent<Rigidbody>();

        if (rigidbody3D != null)
            rigidbody3D.solverIterations = 255;

        if (animator == null)
            animator = GetComponentInParent<Animator>();
    }

    /// <summary>홀드 포인트를 외부에서 지정 (기존 인터페이스 호환)</summary>
    public void SetHoldPoint(Transform point)
    {
        _holdPoint = point;
    }

    /// <summary>
    /// NetworkPlayer.FixedUpdateNetwork()에서 매 틱 호출 (호스트 전용).
    /// 좌클릭 꾹(GrabHold) 상태에 따라 그랩 유지/해제.
    /// </summary>
    public void UpdateState()
    {
        if (networkPlayer == null) return;

        // 좌클릭 꾹(그랩 모드) 활성화 중
        if (networkPlayer.IsGrabActive)
        {
            if (animator != null)
                animator.SetBool("isGrabbing", true);
        }
        else
        {
            // 좌클릭 뗌 → 그랩 해제
            if (fixedJoint != null)
            {
                if (fixedJoint.connectedBody != null)
                {
                    float forceMultiplier;

                    // 잡힌 대상이 기절한 플레이어 → 강하게 던짐 (아이템처럼)
                    if (_grabbedPlayer != null)
                    {
                        float throwForce = !_grabbedPlayer.IsActiveRagdoll
                            ? GetGrabThrowForceStunned()
                            : GetGrabThrowForceNormal();

                        forceMultiplier = throwForce;
                    }
                    else
                    {
                        forceMultiplier = 0.1f; // 일반 오브젝트
                    }

                    Vector3 throwDir = (networkPlayer.transform.forward + Vector3.up * 0.25f) * forceMultiplier;
                    fixedJoint.connectedBody.AddForce(throwDir, ForceMode.Impulse);
                }

                Destroy(fixedJoint);
                _grabbedPlayer = null;
            }

            if (animator != null)
            {
                animator.SetBool("isCarrying", false);
                animator.SetBool("isGrabbing", false);
            }
        }
    }

    /// <summary>
    /// OverlapSphere 방식의 그랩 시도.
    /// 네트워크 환경에서도 호스트에서만 호출.
    /// 다른 플레이어도 잡기 대상에 포함.
    /// </summary>
    public void TryGrab()
    {
        if (networkPlayer != null && networkPlayer.Object != null
            && networkPlayer.Object.IsValid && !networkPlayer.HasStateAuthority)
            return;

        if (fixedJoint != null) return;
        if (networkPlayer != null && !networkPlayer.IsActiveRagdoll) return;

        float grabRadius = 0.8f;
        Collider[] hits = Physics.OverlapSphere(transform.position, grabRadius);
        Rigidbody bestTarget = null;
        float bestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            if (networkPlayer != null && hit.transform.root == networkPlayer.transform)
                continue;

            Rigidbody rb = hit.attachedRigidbody;
            if (rb == null) continue;

            float dist = Vector3.Distance(transform.position, rb.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = rb;
            }
        }

        if (bestTarget != null)
        {
            AttachGrab(bestTarget, transform.position);
        }
    }

    /// <summary>내려놓기 (F키)</summary>
    public void Drop()
    {
        if (fixedJoint == null) return;

        if (fixedJoint.connectedBody != null)
            fixedJoint.connectedBody.AddForce(Vector3.up * 0.5f, ForceMode.Impulse);

        Destroy(fixedJoint);
        _grabbedPlayer = null;

        if (animator != null)
        {
            animator.SetBool("isCarrying", false);
            animator.SetBool("isGrabbing", false);
        }
    }

    /// <summary>던지기 (우클릭)</summary>
    public void Throw()
    {
        if (fixedJoint == null) return;

        if (fixedJoint.connectedBody != null)
        {
            float force;
            if (_grabbedPlayer != null && !_grabbedPlayer.IsActiveRagdoll)
                force = GetGrabThrowForceStunned();
            else if (_grabbedPlayer != null)
                force = GetGrabThrowForceNormal();
            else
                force = 10f;

            Vector3 throwDir = Vector3.up;
            if (networkPlayer != null)
                throwDir = (networkPlayer.transform.forward + Vector3.up * 0.3f).normalized * force;

            fixedJoint.connectedBody.AddForce(throwDir, ForceMode.Impulse);
        }

        Destroy(fixedJoint);
        _grabbedPlayer = null;

        if (animator != null)
        {
            animator.SetBool("isCarrying", false);
            animator.SetBool("isGrabbing", false);
        }
    }

    /// <summary>
    /// 충돌 시 자동 그랩.
    /// 좌클릭 꾹(GrabHold) 상태에서 충돌하면 FixedJoint 연결.
    /// 다른 플레이어도 대상이 됨.
    /// </summary>
    void OnCollisionEnter(Collision collision)
    {
        TryCarryObject(collision);
    }

    bool TryCarryObject(Collision collision)
    {
        // StateAuthority만 실행
        if (networkPlayer != null && networkPlayer.Object != null
            && networkPlayer.Object.IsValid && !networkPlayer.HasStateAuthority)
            return false;

        if (networkPlayer != null && !networkPlayer.IsActiveRagdoll) return false;
        if (networkPlayer != null && !networkPlayer.IsGrabActive) return false;
        if (fixedJoint != null) return false;

        // 자기 자신 불가
        if (networkPlayer != null && collision.transform.root == networkPlayer.transform)
            return false;

        if (!collision.collider.TryGetComponent(out Rigidbody otherRb))
            return false;

        Vector3 anchorPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;

        AttachGrab(otherRb, anchorPoint);
        return true;
    }

    private void AttachGrab(Rigidbody targetRb, Vector3 worldAnchorPoint)
    {
        fixedJoint = gameObject.AddComponent<FixedJoint>();
        fixedJoint.connectedBody = targetRb;
        fixedJoint.autoConfigureConnectedAnchor = false;
        fixedJoint.connectedAnchor = targetRb.transform.InverseTransformPoint(worldAnchorPoint);

        // 잡힌 대상이 다른 플레이어인지 체크
        _grabbedPlayer = targetRb.transform.root.GetComponent<NetworkPlayer>();

        if (animator != null)
            animator.SetBool("isCarrying", true);
    }

    // CSV 수치 헬퍼
    private float GetGrabThrowForceNormal()
    {
        return CombatSettings.Instance != null ? CombatSettings.Instance.grabThrowForceNormal : 10f;
    }

    private float GetGrabThrowForceStunned()
    {
        return CombatSettings.Instance != null ? CombatSettings.Instance.grabThrowForceStunned : 15f;
    }
}
