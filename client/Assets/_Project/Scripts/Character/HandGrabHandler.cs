using UnityEngine;
using SSAFYPlayTime.Gameplay.Items;

/// <summary>
/// 멀?�플?�이 ?�??그랩 ?�들??
/// 
/// 좌클�?�?= 그랩 모드
///   - ?�드 ?�이?�을 ?�으�?물리 고정 ?�??즉시 ?�득 처리
///   - OnCollisionEnter?�서 FixedJoint ?�성 (물체 or ?�른 ?�레?�어)
///   - ?�른 ?�레?�어가 active ragdoll ??붙잡?�서 ?�동 방해
///   - ?�른 ?�레?�어가 기절 ???�이?�처???�고 ?�니?��? ?��?�?가??
/// 좌클�??��? = ?��?(HandGrabHandler 밖에??NetworkPlayer가 처리)
///
/// StateAuthority(?�스???�서�?물리 ?�산 ?�행.
/// </summary>
public class HandGrabHandler : MonoBehaviour
{
    [SerializeField] Animator animator;

    // ?��??�에 ?�성?�는 FixedJoint
    FixedJoint fixedJoint;

    // ???�의 Rigidbody
    Rigidbody rigidbody3D;

    // ?�위 NetworkPlayer 참조
    NetworkPlayer networkPlayer;
    ItemRuntimeHost itemRuntimeHost;

    // ?�???�인??(?�터?�이???�환)
    Transform _holdPoint;

    // ?�힌 ?�?�이 ?�레?�어?��? 추적
    NetworkPlayer _grabbedPlayer;

    /// <summary>?�재 무언가�??�고 ?�는지</summary>
    public bool IsHolding => fixedJoint != null;

    /// <summary>?�힌 ?�?�이 기절???�레?�어?��?</summary>
    public bool IsHoldingStunnedPlayer => _grabbedPlayer != null && !_grabbedPlayer.IsActiveRagdoll;

    public bool IsHoldingConsciousPlayer => fixedJoint != null && _grabbedPlayer != null && _grabbedPlayer.IsActiveRagdoll;

    public bool IsHoldingThrowableTarget => fixedJoint != null && (_grabbedPlayer == null || !_grabbedPlayer.IsActiveRagdoll);

    void Awake()
    {
        networkPlayer = transform.root.GetComponent<NetworkPlayer>();
        rigidbody3D = GetComponent<Rigidbody>();

        if (rigidbody3D != null)
            rigidbody3D.solverIterations = 255;

        if (animator == null)
            animator = GetComponentInParent<Animator>();
    }

    /// <summary>?�???�인?��? ?��??�서 지??(기존 ?�터?�이???�환)</summary>
    public void SetHoldPoint(Transform point)
    {
        _holdPoint = point;
    }

    /// <summary>
    /// NetworkPlayer가 ?�택???��????�스?��? 공유받는??
    /// </summary>
    public void SetItemRuntimeHost(ItemRuntimeHost runtimeHost)
    {
        itemRuntimeHost = runtimeHost;
    }

    /// <summary>
    /// NetworkPlayer.FixedUpdateNetwork()?�서 �????�출 (?�스???�용).
    /// 좌클�?�?GrabHold) ?�태???�라 그랩 ?��?/?�제.
    /// </summary>
    public void UpdateState()
    {
        if (networkPlayer == null) return;

        if (networkPlayer.IsGrabActive)
            return;

        if (fixedJoint != null)
        {
            Destroy(fixedJoint);
            _grabbedPlayer = null;
        }
    }

    /// <summary>
    /// OverlapSphere 방식??그랩 ?�도.
    /// ?�트?�크 ?�경?�서???�스?�에?�만 ?�출.
    /// ?�른 ?�레?�어???�기 ?�?�에 ?�함.
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
            // ?�드 ?�이?��? ??�� ?�득 경로�??�용?�고 물리 그랩?�로 ?�백?��? ?�는??
            if (IsFieldItemRigidbody(bestTarget))
            {
                TryPickupFieldItem(bestTarget);
                return;
            }

            AttachGrab(bestTarget, transform.position);
        }
    }

    /// <summary>?�려?�기 (F??</summary>
    public void Drop()
    {
        if (fixedJoint == null) return;

        if (fixedJoint.connectedBody != null)
            fixedJoint.connectedBody.AddForce(Vector3.up * 0.5f, ForceMode.Impulse);

        Destroy(fixedJoint);
        _grabbedPlayer = null;
    }

    /// <summary>?��?�?(?�클�?</summary>
    public void Throw()
    {
        if (fixedJoint == null) return;
        if (_grabbedPlayer != null && _grabbedPlayer.IsActiveRagdoll) return;

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
    }

    /// <summary>
    /// 충돌 ???�동 그랩.
    /// 좌클�?�?GrabHold) ?�태?�서 충돌?�면 FixedJoint ?�결.
    /// ?�른 ?�레?�어???�?�이 ??
    /// </summary>
    void OnCollisionEnter(Collision collision)
    {
        TryCarryObject(collision);
    }

    bool TryCarryObject(Collision collision)
    {
        // StateAuthority�??�행
        if (networkPlayer != null && networkPlayer.Object != null
            && networkPlayer.Object.IsValid && !networkPlayer.HasStateAuthority)
            return false;

        if (networkPlayer != null && !networkPlayer.IsActiveRagdoll) return false;
        if (networkPlayer != null && !networkPlayer.IsGrabActive) return false;
        if (fixedJoint != null) return false;

        // ?�기 ?�신 불�?
        if (networkPlayer != null && collision.transform.root == networkPlayer.transform)
            return false;

        if (!collision.collider.TryGetComponent(out Rigidbody otherRb))
            return false;

        // ?�드 ?�이?��? ??�� ?�득 경로�??�용?�고 물리 그랩?�로 ?�백?��? ?�는??
        if (IsFieldItemRigidbody(otherRb))
        {
            TryPickupFieldItem(otherRb);
            return true;
        }

        Vector3 anchorPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;

        AttachGrab(otherRb, anchorPoint);
        return true;
    }

    private bool TryPickupFieldItem(Rigidbody targetRb)
    {
        if (targetRb == null)
            return false;

        var fieldDrop = targetRb.GetComponentInParent<ItemFieldDrop>();
        if (fieldDrop == null || !fieldDrop.CanBePickedUp())
            return false;

        if (itemRuntimeHost == null)
            itemRuntimeHost = ResolveItemRuntimeHostForCharacter();

        if (itemRuntimeHost == null)
        {
            Debug.Log("[HandGrabHandler] ?�이???��????�스?��? 찾�? 못했?�니??", this);
            return false;
        }

        if (itemRuntimeHost.transform == transform.root && itemRuntimeHost.OwnerTransform == null)
            itemRuntimeHost.SetOwnerTransform(transform.root);

        if (!itemRuntimeHost.IsReady && !itemRuntimeHost.Initialize())
        {
            Debug.Log($"[HandGrabHandler] ?�이???��???초기???�패: {itemRuntimeHost.LastError}", this);
            return false;
        }

        if (!itemRuntimeHost.TryPickup(fieldDrop.ItemId, out var reason))
        {
            // ?��? 보유 중이�????�?��? ?�비??것으�?간주??물리 그랩 ?�백??막는??
            if (!string.IsNullOrWhiteSpace(reason) &&
                reason.StartsWith("Already holding an item", System.StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(reason))
                Debug.Log($"[HandGrabHandler] ?�이???�득 ?�패: {reason}", this);
            return false;
        }

        fieldDrop.MarkPickedUp();

        return true;
    }

    private static bool IsFieldItemRigidbody(Rigidbody targetRb)
    {
        if (targetRb == null)
            return false;

        return targetRb.GetComponentInParent<ItemFieldDrop>() != null;
    }

    private ItemRuntimeHost ResolveItemRuntimeHostForCharacter()
    {
        if (itemRuntimeHost != null)
            return itemRuntimeHost;

        var root = transform.root;
        var direct = root.GetComponent<ItemRuntimeHost>();
        if (IsHostForCharacter(direct, root))
            return direct;

        var hosts = FindObjectsOfType<ItemRuntimeHost>(true);
        var hostCount = 0;
        ItemRuntimeHost singleFallback = direct;
        for (var i = 0; i < hosts.Length; i++)
        {
            var host = hosts[i];
            if (host == null)
                continue;

            hostCount++;
            if (singleFallback == null)
                singleFallback = host;

            if (IsHostForCharacter(host, root))
                return host;
        }

        if (direct != null)
            return direct;

        return hostCount == 1 ? singleFallback : null;
    }

    private static bool IsHostForCharacter(ItemRuntimeHost host, Transform characterRoot)
    {
        if (host == null || characterRoot == null)
            return false;

        var owner = host.OwnerTransform;
        if (owner == null)
            return host.transform == characterRoot;

        return owner == characterRoot || owner.root == characterRoot;
    }

    private void AttachGrab(Rigidbody targetRb, Vector3 worldAnchorPoint)
    {
        fixedJoint = gameObject.AddComponent<FixedJoint>();
        fixedJoint.connectedBody = targetRb;
        fixedJoint.autoConfigureConnectedAnchor = false;
        fixedJoint.connectedAnchor = targetRb.transform.InverseTransformPoint(worldAnchorPoint);

        // ?�힌 ?�?�이 ?�른 ?�레?�어?��? 체크
        _grabbedPlayer = targetRb.transform.root.GetComponent<NetworkPlayer>();
    }

    // CSV ?�치 ?�퍼
    private float GetGrabThrowForceNormal()
    {
        return CombatSettings.Instance != null ? CombatSettings.Instance.grabThrowForceNormal : 10f;
    }

    private float GetGrabThrowForceStunned()
    {
        return CombatSettings.Instance != null ? CombatSettings.Instance.grabThrowForceStunned : 15f;
    }
}



