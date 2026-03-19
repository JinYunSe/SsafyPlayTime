using Fusion;
using UnityEngine;
using RootMotion.Dynamics;
using SSAFYPlayTime.Character;
using SSAFYPlayTime.Gameplay.Items;

/// <summary>
/// 멀티플레이 손 그랩 핸들러.
/// GrabDriveProfile에 따라 FixedJoint(딱딱) 또는 ConfigurableJoint(탄성) 모드 선택 가능.
/// StateAuthority(호스트)에서만 물리 연산 실행.
/// </summary>
public class HandGrabHandler : MonoBehaviour
{
    public enum HandSide { Left, Right }

    [Header("Hand Identity")]
    [SerializeField] HandSide handSide = HandSide.Left;
    public HandSide Side => handSide;

    [SerializeField] Animator animator;

    [Header("Grab Profile (선택)")]
    [Tooltip("프로파일이 없으면 기본 FixedJoint 모드로 동작")]
    [SerializeField] GrabDriveProfile grabProfile;

    [Header("Grab Physics (프로파일 미사용 시 폴백)")]
    [SerializeField] float breakForce = 2000f;
    [SerializeField] float breakTorque = 2000f;
    [SerializeField] float dualGrabBreakMultiplier = 3f;

    [Header("Grab Distance")]
    [Tooltip("손과 잡힌 앵커 사이 이 거리 초과 시 자동 해제")]
    [SerializeField] float maxGrabDistance = 2.5f;

    [Header("Palm Anchor")]
    [Tooltip("손 뼈 원점에서 손바닥 표면까지의 로컬 오프셋 (조인트 anchor로 사용)")]
    [SerializeField] Vector3 palmAnchorOffset = new Vector3(0f, -0.02f, 0.06f);

    [Header("Opponent Weaken")]
    [SerializeField] float grabbedPinWeight = 0.3f;
    [SerializeField] float grabbedMuscleWeight = 0.3f;

    // 런타임 — 두 조인트 중 하나만 사용
    FixedJoint _fixedJoint;
    ConfigurableJoint _configurableJoint;
    Rigidbody rigidbody3D;
    NetworkPlayer networkPlayer;
    ItemRuntimeHost itemRuntimeHost;
    Transform _holdPoint;
    NetworkPlayer _grabbedPlayer;

    // Reach intent — TryGrab에서 타겟을 찾으면 저장, 근접/접촉 시 실제 attach
    Rigidbody _pendingReachTarget;
    float _reachIntentTime;
    const float ReachAttachRadius = 0.18f;  // 손바닥 근접 판정 거리
    const float ReachTimeout = 0.6f;        // reach intent 자동 만료

    // 잡힌 PuppetMaster 약화 추적
    PuppetMaster _grabbedPuppet;
    float _originalPinWeight;
    float _originalMuscleWeight;

    // 동일 PuppetMaster를 잡고 있는 핸들러 수 (양손 중복 약화 방지)
    static readonly System.Collections.Generic.Dictionary<PuppetMaster, int> _grabRefCounts
        = new System.Collections.Generic.Dictionary<PuppetMaster, int>();

    // --- 프로퍼티: 어느 조인트든 활성이면 잡고 있는 것 ---
    Joint ActiveJoint => (Joint)_fixedJoint ?? _configurableJoint;
    public bool IsHolding => ActiveJoint != null;
    public bool IsHoldingStunnedPlayer => _grabbedPlayer != null && !_grabbedPlayer.IsActiveRagdoll;
    public bool IsHoldingConsciousPlayer => IsHolding && _grabbedPlayer != null && _grabbedPlayer.IsActiveRagdoll;
    public bool IsHoldingThrowableTarget => IsHolding && (_grabbedPlayer == null || !_grabbedPlayer.IsActiveRagdoll);
    public PuppetMaster GrabbedPuppet => _grabbedPuppet;

    /// <summary>reach intent 중인 타겟 (아직 attach 전)</summary>
    public Rigidbody PendingReachTarget => _pendingReachTarget;
    public bool IsReaching => _pendingReachTarget != null && !IsHolding;

    /// <summary>현재 잡고 있는 대상의 종류 (로컬 파생값 — 네트워크 동기화 불필요)</summary>
    public GrabDriveProfile.GrabTargetType GrabbedTargetKind => _currentGrabTargetType;

    /// <summary>현재 잡고 있는 대상의 Rigidbody</summary>
    public Rigidbody GrabTarget => GetConnectedBody();

    /// <summary>현재 잡고 있는 대상의 루트 Transform (같은 캐릭터의 다른 부위 비교용)</summary>
    public Transform GrabTargetRoot
    {
        get
        {
            var rb = GetConnectedBody();
            return rb != null ? rb.transform.root : null;
        }
    }

    bool UseConfigurableJoint => grabProfile != null && grabProfile.jointMode == GrabDriveProfile.GrabJointMode.ConfigurableJoint;

    /// <summary>
    /// 현재 잡고 있는 앵커의 월드 좌표.
    /// ProceduralGrabArm의 IK 타겟 및 거리 기반 해제 검사에 사용.
    /// </summary>
    public Vector3 GetGrabAnchorWorldPosition()
    {
        var joint = ActiveJoint;
        if (joint == null || joint.connectedBody == null)
            return transform.position;
        return joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
    }

    void Awake()
    {
        networkPlayer = transform.root.GetComponent<NetworkPlayer>();
        rigidbody3D = GetComponent<Rigidbody>();

        // 손 Rigidbody solver budget: 프로젝트 기본 10 기반, 그랩 안정성을 위해 약간 상향.
        // 255는 CPU 과부하 위험 — 보고서 권장 10~20 범위 준수.
        if (rigidbody3D != null)
            rigidbody3D.solverIterations = 16;

        if (animator == null)
            animator = GetComponentInParent<Animator>();

        // Inspector에서 미설정 시 트랜스폼 이름으로 자동 감지
        AutoDetectHandSide();
    }

    private void AutoDetectHandSide()
    {
        var nameLower = transform.name.ToLowerInvariant();
        if (nameLower.Contains("right") || nameLower.Contains("_r_") || nameLower.EndsWith("_r"))
            handSide = HandSide.Right;
    }

    void OnJointBreak(float breakForceAmount)
    {
        RestoreGrabbedPuppet();
        NotifyGrabReleased();
        _fixedJoint = null;
        _configurableJoint = null;
        _grabbedPlayer = null;
        _grabbedPuppet = null;
    }

    public void SetHoldPoint(Transform point)
    {
        _holdPoint = point;
    }

    public void SetItemRuntimeHost(ItemRuntimeHost runtimeHost)
    {
        itemRuntimeHost = runtimeHost;
    }

    public void UpdateState()
    {
        if (networkPlayer == null) return;

        // 잡고 있는 동안 시간 경과에 따라 breakForce 약화 (weakening curve)
        // 기절자 잡기 시 약화 스킵 가능 (안정적 운반을 위해)
        if (IsHolding && _configurableJoint != null && grabProfile != null
            && !grabProfile.ShouldSkipWeakening(_currentGrabTargetType))
        {
            var holdDuration = Time.time - _grabStartTime;
            var weakened = grabProfile.EvaluateWeakenedBreakForce(_currentGrabTargetType, holdDuration);
            _configurableJoint.breakForce = weakened;
            _configurableJoint.breakTorque = weakened;
        }

        // 손-앵커 거리 초과 시 강제 해제 (HFF/PA 방식)
        // 조인트가 살아 있어도 손이 실제로 닿지 못하면 그랩 유지 불가
        if (IsHolding)
        {
            var anchorWorld = GetGrabAnchorWorldPosition();
            var handDist = Vector3.Distance(transform.position, anchorWorld);
            if (handDist > maxGrabDistance)
            {
                RestoreGrabbedPuppet();
                NotifyGrabReleased();
                DestroyActiveJoint();
                _grabbedPlayer = null;
                _grabbedPuppet = null;
                return;
            }
        }

        // Reach intent 처리: 타겟에 근접하면 attach, 타임아웃이면 해제
        if (_pendingReachTarget != null && !IsHolding)
        {
            if (_pendingReachTarget == null || (Time.time - _reachIntentTime) > ReachTimeout)
            {
                _pendingReachTarget = null;
            }
            else
            {
                var closestPoint = _pendingReachTarget.ClosestPointOnBounds(transform.position);
                var dist = Vector3.Distance(transform.position, closestPoint);
                if (dist <= ReachAttachRadius)
                {
                    AttachGrab(_pendingReachTarget, closestPoint);
                    _pendingReachTarget = null;
                }
            }
        }

        if (networkPlayer.IsHandGrabActive(handSide))
            return;

        // grab 해제 시 reach intent도 클리어
        _pendingReachTarget = null;

        if (IsHolding)
        {
            RestoreGrabbedPuppet();
            NotifyGrabReleased();
            DestroyActiveJoint();
            _grabbedPlayer = null;
            _grabbedPuppet = null;
        }
    }

    public void TryGrab()
    {
        if (networkPlayer != null && networkPlayer.Object != null
            && networkPlayer.Object.IsValid && !networkPlayer.HasStateAuthority)
            return;

        if (IsHolding) return;
        if (networkPlayer != null && !networkPlayer.IsActiveRagdoll) return;

        float grabRadius = 0.8f;
        Collider[] hits = Physics.OverlapSphere(transform.position, grabRadius);
        Rigidbody bestTarget = null;
        float bestScore = float.MinValue;

        // 손 방향과 캐릭터 시야를 기반으로 가중 점수 계산
        var handForward = transform.forward;
        var charForward = networkPlayer != null ? networkPlayer.transform.forward : transform.forward;
        var handPos = transform.position;

        // 반대 손이 기절자를 잡고 있으면 같은 캐릭터의 다른 부위에 보너스 부여
        Transform otherHandStunnedRoot = GetOtherHandStunnedTargetRoot();

        foreach (var hit in hits)
        {
            Rigidbody rb = hit.attachedRigidbody;
            if (rb == null) continue;
            if (ShouldIgnoreGrabTarget(rb))
                continue;

            var toTarget = rb.position - handPos;
            float dist = toTarget.magnitude;
            if (dist < 0.001f) dist = 0.001f;

            // 거리 점수 (가까울수록 높음, 0~1)
            float distScore = 1f - Mathf.Clamp01(dist / grabRadius);
            // 손 방향 내적 (손이 향하는 쪽일수록 높음, -1~1 → 0~1)
            float handDot = (Vector3.Dot(handForward, toTarget / dist) + 1f) * 0.5f;
            // 캐릭터 시야 내적 (캐릭터가 바라보는 쪽일수록 높음)
            float viewDot = (Vector3.Dot(charForward, toTarget / dist) + 1f) * 0.5f;

            float score = distScore * 0.4f + handDot * 0.3f + viewDot * 0.3f;

            // 반대 손이 같은 기절자를 잡고 있으면 양손 잡기 유도 보너스
            if (otherHandStunnedRoot != null && rb.transform.root == otherHandStunnedRoot)
                score += 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = rb;
            }
        }

        if (bestTarget != null)
        {
            if (IsFieldItemRigidbody(bestTarget))
            {
                TryPickupFieldItem(bestTarget);
                return;
            }

            // 즉시 attach하지 않고 reach intent만 설정 — 근접/접촉 시 실제 attach
            _pendingReachTarget = bestTarget;
            _reachIntentTime = Time.time;
        }
    }

    public void Drop()
    {
        if (!IsHolding) return;

        Rigidbody connected = GetConnectedBody();
        if (connected != null)
            connected.AddForce(Vector3.up * 0.5f, ForceMode.Impulse);

        RestoreGrabbedPuppet();
        NotifyGrabReleased();
        DestroyActiveJoint();
        _grabbedPlayer = null;
        _grabbedPuppet = null;
    }

    public void Throw()
    {
        if (!IsHolding) return;

        Rigidbody connected = GetConnectedBody();
        bool isConsciousPlayer = _grabbedPlayer != null && _grabbedPlayer.IsActiveRagdoll;
        bool isStunnedPlayer = _grabbedPlayer != null && !_grabbedPlayer.IsActiveRagdoll;

        // Throw 힘 계산을 조인트 파괴 전에 수행 (버그 수정)
        float force;
        float throwUp;

        if (isConsciousPlayer)
        {
            // 정상 캐릭터: 밀어내기만 (약한 힘, 수평 위주)
            force = GetGrabThrowForceNormal() * (grabProfile != null ? grabProfile.consciousPushForceScale : 0.4f);
            throwUp = grabProfile != null ? grabProfile.consciousPushUpComponent : 0.1f;
        }
        else if (isStunnedPlayer)
        {
            // 기절자: 실제 던지기 (강한 힘, 포물선)
            force = GetGrabThrowForceStunned();
            throwUp = grabProfile != null ? grabProfile.throwUpComponent : 0.4f;
        }
        else
        {
            // 오브젝트
            force = 10f;
            throwUp = grabProfile != null ? grabProfile.throwUpComponent : 0.4f;
        }

        RestoreGrabbedPuppet();
        NotifyGrabReleased();
        DestroyActiveJoint();
        _grabbedPlayer = null;
        _grabbedPuppet = null;

        if (connected != null)
        {
            Vector3 throwDir = Vector3.up;
            if (networkPlayer != null)
                throwDir = (networkPlayer.transform.forward + Vector3.up * throwUp).normalized * force;

            connected.AddForce(throwDir, ForceMode.Impulse);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        TryCarryObject(collision);
    }

    bool TryCarryObject(Collision collision)
    {
        if (networkPlayer != null && networkPlayer.Object != null
            && networkPlayer.Object.IsValid && !networkPlayer.HasStateAuthority)
            return false;

        if (networkPlayer != null && !networkPlayer.IsActiveRagdoll) return false;
        if (networkPlayer != null && !networkPlayer.IsHandGrabActive(handSide)) return false;
        if (IsHolding) return false;

        if (!collision.collider.TryGetComponent(out Rigidbody otherRb))
            return false;
        if (ShouldIgnoreGrabTarget(otherRb))
            return false;

        if (IsFieldItemRigidbody(otherRb))
        {
            TryPickupFieldItem(otherRb);
            return true;
        }

        Vector3 anchorPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;

        AttachGrab(otherRb, anchorPoint);
        _pendingReachTarget = null;  // 접촉으로 attach 성공 시 reach intent 클리어
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
            Debug.Log("[HandGrabHandler] ItemRuntimeHost를 찾지 못했습니다", this);
            return false;
        }

        if (itemRuntimeHost.transform == transform.root && itemRuntimeHost.OwnerTransform == null)
            itemRuntimeHost.SetOwnerTransform(transform.root);

        if (!itemRuntimeHost.IsReady && !itemRuntimeHost.Initialize())
        {
            Debug.Log($"[HandGrabHandler] 아이템 런타임 초기화 실패: {itemRuntimeHost.LastError}", this);
            return false;
        }

        var pickedItemId = fieldDrop.ItemId;
        var pickupOrigin = fieldDrop.transform.position;
        var dropInstanceId = fieldDrop.InstanceId;

        if (!itemRuntimeHost.TryPickup(pickedItemId, out var reason))
        {
            if (!string.IsNullOrWhiteSpace(reason) &&
                reason.StartsWith("Already holding an item", System.StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(reason))
                Debug.Log($"[HandGrabHandler] 아이템 획득 실패: {reason}", this);
            return false;
        }

        fieldDrop.MarkPickedUp();

        // 키 기반 픽업과 동일하게 네트워크 브로드캐스트 — 원격 클라이언트에서도 드롭 제거
        if (networkPlayer != null)
            networkPlayer.NotifyHandGrabPickedFieldDrop(pickedItemId, dropInstanceId ?? string.Empty, pickupOrigin);

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

    // =========================================================
    // 조인트 생성 — 프로파일에 따라 FixedJoint / ConfigurableJoint
    // =========================================================

    // 잡힌 시간 추적 (weakening curve 적용용)
    private float _grabStartTime;
    private SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType _currentGrabTargetType;

    private void AttachGrab(Rigidbody targetRb, Vector3 worldAnchorPoint)
    {
        if (ShouldIgnoreGrabTarget(targetRb))
            return;

        Vector3 localAnchor = targetRb.transform.InverseTransformPoint(worldAnchorPoint);

        // 타겟 유형 판별: 기절자 / 정상 캐릭터 / 오브젝트
        var targetPlayer = targetRb.transform.root.GetComponent<NetworkPlayer>();
        if (targetPlayer != null && !targetPlayer.IsActiveRagdoll)
            _currentGrabTargetType = SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.StunnedPlayer;
        else if (targetPlayer != null)
            _currentGrabTargetType = SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.Player;
        else
            _currentGrabTargetType = SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.Object;
        _grabStartTime = Time.time;

        if (UseConfigurableJoint)
            AttachConfigurableJoint(targetRb, localAnchor, _currentGrabTargetType);
        else
            AttachFixedJoint(targetRb, localAnchor);

        _grabbedPlayer = targetPlayer;
        WeakenGrabbedPuppet(targetRb);

        // OwnerProxy용 grab 관계 보고: 누구를 잡았는지 + 앵커
        if (networkPlayer != null)
        {
            var targetNetObj = targetRb.transform.root.GetComponent<Fusion.NetworkObject>();
            var netId = targetNetObj != null ? targetNetObj.Id : default;
            networkPlayer.ReportGrabAttached(handSide, netId, localAnchor);
        }

        // 잡힌 상대에게 알림 (OwnerProxy 뼈 보간 전환용)
        if (_grabbedPlayer != null)
            _grabbedPlayer.SetGrabbedByOther(true);
    }

    private void AttachFixedJoint(Rigidbody targetRb, Vector3 localAnchor)
    {
        float bf = grabProfile != null ? grabProfile.breakForce : breakForce;
        float bt = grabProfile != null ? grabProfile.breakTorque : breakTorque;

        _fixedJoint = gameObject.AddComponent<FixedJoint>();
        _fixedJoint.connectedBody = targetRb;
        _fixedJoint.autoConfigureConnectedAnchor = false;
        _fixedJoint.anchor = palmAnchorOffset;
        _fixedJoint.connectedAnchor = localAnchor;
        _fixedJoint.breakForce = bf;
        _fixedJoint.breakTorque = bt;
    }

    private void AttachConfigurableJoint(Rigidbody targetRb, Vector3 localAnchor,
        SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType targetType = SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.Default)
    {
        var cj = gameObject.AddComponent<ConfigurableJoint>();
        cj.connectedBody = targetRb;
        cj.autoConfigureConnectedAnchor = false;
        cj.anchor = palmAnchorOffset;
        cj.connectedAnchor = localAnchor;

        // 모든 축을 Limited로 설정
        cj.xMotion = ConfigurableJointMotion.Limited;
        cj.yMotion = ConfigurableJointMotion.Limited;
        cj.zMotion = ConfigurableJointMotion.Limited;
        cj.angularXMotion = ConfigurableJointMotion.Free;
        cj.angularYMotion = ConfigurableJointMotion.Free;
        cj.angularZMotion = ConfigurableJointMotion.Free;

        // 타겟 유형별 스프링 드라이브
        var drive = grabProfile.CreateGrabDrive(false, targetType);
        cj.xDrive = drive;
        cj.yDrive = drive;
        cj.zDrive = drive;

        // 타겟 유형별 리니어 리미트
        cj.linearLimit = grabProfile.CreateLinearLimit(targetType);
        cj.linearLimitSpring = grabProfile.CreateLimitSpring();

        // 타겟 유형별 breakForce
        var bf = grabProfile.EvaluateWeakenedBreakForce(targetType, 0f);
        cj.breakForce = bf;
        cj.breakTorque = bf;

        _configurableJoint = cj;
    }

    // =========================================================
    // 조인트 유틸
    // =========================================================

    private Rigidbody GetConnectedBody()
    {
        if (_fixedJoint != null) return _fixedJoint.connectedBody;
        if (_configurableJoint != null) return _configurableJoint.connectedBody;
        return null;
    }

    private void DestroyActiveJoint()
    {
        if (_fixedJoint != null)
        {
            Destroy(_fixedJoint);
            _fixedJoint = null;
        }

        if (_configurableJoint != null)
        {
            Destroy(_configurableJoint);
            _configurableJoint = null;
        }
    }

    // =========================================================
    // 던지기 힘 설정
    // =========================================================

    private float GetGrabThrowForceNormal()
    {
        return CombatSettings.Instance != null ? CombatSettings.Instance.grabThrowForceNormal : 10f;
    }

    private float GetGrabThrowForceStunned()
    {
        return CombatSettings.Instance != null ? CombatSettings.Instance.grabThrowForceStunned : 15f;
    }

    // =========================================================
    // 상대 PuppetMaster 약화/복원
    // =========================================================

    private void WeakenGrabbedPuppet(Rigidbody targetRb)
    {
        PuppetMaster pm = targetRb.transform.root.GetComponentInChildren<PuppetMaster>(true);
        if (pm == null) return;

        _grabbedPuppet = pm;

        // BodyPartPhysicsManager가 있으면 부위별 프로파일로 전환
        var bodyPartManager = targetRb.transform.root.GetComponentInChildren<SSAFYPlayTime.Character.BodyPartPhysicsManager>(true);
        if (bodyPartManager != null)
        {
            if (!_grabRefCounts.ContainsKey(pm) || _grabRefCounts[pm] <= 0)
            {
                _originalPinWeight = pm.pinWeight;
                _originalMuscleWeight = pm.muscleWeight;
                _grabRefCounts[pm] = 1;
            }
            else
            {
                _originalPinWeight = pm.pinWeight;
                _originalMuscleWeight = pm.muscleWeight;
                _grabRefCounts[pm]++;
                BoostAllGrabJoints(pm);
            }
            return;
        }

        // 폴백: 기존 전역 가중치 방식
        float pinW = grabProfile != null ? grabProfile.grabbedPinWeight : grabbedPinWeight;
        float muscleW = grabProfile != null ? grabProfile.grabbedMuscleWeight : grabbedMuscleWeight;

        if (!_grabRefCounts.ContainsKey(pm) || _grabRefCounts[pm] <= 0)
        {
            _originalPinWeight = pm.pinWeight;
            _originalMuscleWeight = pm.muscleWeight;
            pm.pinWeight = pinW;
            pm.muscleWeight = muscleW;
            _grabRefCounts[pm] = 1;
        }
        else
        {
            _originalPinWeight = pinW;
            _originalMuscleWeight = muscleW;
            _grabRefCounts[pm]++;

            BoostAllGrabJoints(pm);
        }
    }

    private void RestoreGrabbedPuppet()
    {
        if (_grabbedPuppet == null) return;

        if (_grabRefCounts.ContainsKey(_grabbedPuppet))
        {
            _grabRefCounts[_grabbedPuppet]--;

            if (_grabRefCounts[_grabbedPuppet] <= 0)
            {
                // BodyPartPhysicsManager가 있으면 Normal 상태로 복원
                var bodyPartManager = _grabbedPuppet.transform.root.GetComponentInChildren<SSAFYPlayTime.Character.BodyPartPhysicsManager>(true);
                if (bodyPartManager == null)
                {
                    _grabbedPuppet.pinWeight = _originalPinWeight;
                    _grabbedPuppet.muscleWeight = _originalMuscleWeight;
                }

                _grabRefCounts.Remove(_grabbedPuppet);
            }
        }
    }

    private void BoostAllGrabJoints(PuppetMaster targetPm)
    {
        if (networkPlayer == null) return;

        float dualMult = grabProfile != null ? grabProfile.dualGrabMultiplier : dualGrabBreakMultiplier;

        var handlers = networkPlayer.GetComponentsInChildren<HandGrabHandler>(true);
        foreach (var h in handlers)
        {
            if (!h.IsHolding || h._grabbedPuppet != targetPm)
                continue;

            if (h._fixedJoint != null)
            {
                float bf = grabProfile != null ? grabProfile.breakForce : breakForce;
                float bt = grabProfile != null ? grabProfile.breakTorque : breakTorque;
                h._fixedJoint.breakForce = bf * dualMult;
                h._fixedJoint.breakTorque = bt * dualMult;
            }

            if (h._configurableJoint != null && grabProfile != null)
            {
                var drive = grabProfile.CreateGrabDrive(true, h._currentGrabTargetType);
                h._configurableJoint.xDrive = drive;
                h._configurableJoint.yDrive = drive;
                h._configurableJoint.zDrive = drive;
                var bf = grabProfile.EvaluateWeakenedBreakForce(h._currentGrabTargetType, 0f) * dualMult;
                h._configurableJoint.breakForce = bf;
                h._configurableJoint.breakTorque = bf;
            }
        }
    }

    /// <summary>
    /// grab 해제 시 NetworkPlayer에 관계 해제 + 잡힌 상대에게 알림.
    /// OnJointBreak / UpdateState release / Drop / Throw 모든 경로에서 호출.
    /// </summary>
    private void NotifyGrabReleased()
    {
        if (networkPlayer != null)
            networkPlayer.ReportGrabDetached(handSide);
        if (_grabbedPlayer != null)
            _grabbedPlayer.SetGrabbedByOther(false);
    }

    /// <summary>
    /// 반대 손이 기절자를 잡고 있으면 해당 기절자의 루트 Transform 반환.
    /// 양손 잡기 유도에 사용.
    /// </summary>
    private Transform GetOtherHandStunnedTargetRoot()
    {
        if (networkPlayer == null) return null;

        var handlers = networkPlayer.GetComponentsInChildren<HandGrabHandler>(true);
        foreach (var h in handlers)
        {
            if (h == this || h == null) continue;
            if (h.IsHoldingStunnedPlayer)
                return h.GrabTargetRoot;
        }
        return null;
    }

    private bool ShouldIgnoreGrabTarget(Rigidbody targetRb)
    {
        if (targetRb == null)
            return true;

        if (targetRb == rigidbody3D)
            return true;

        if (targetRb.transform == transform || targetRb.transform.IsChildOf(transform))
            return true;

        if (networkPlayer == null)
            return false;

        return targetRb.transform.root == networkPlayer.transform;
    }
}
