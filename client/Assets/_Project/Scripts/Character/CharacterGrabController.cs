using UnityEngine;
using RootMotion.Dynamics;

/// <summary>
/// Grab/carry 관련 상태를 한곳에 모으는 read-only 허브.
/// 이번 단계에서는 기존 시스템을 대체하지 않고,
/// 흩어진 상태를 통합 표현으로 승격하는 역할만 담당한다.
/// </summary>
public sealed class CharacterGrabController : MonoBehaviour
{
    public enum GrabActionState : byte
    {
        Idle = 0,
        ReachLeft = 1,
        ReachRight = 2,
        ReachBoth = 3,
        AttachPending = 4,
        HoldOneHandObject = 5,
        HoldOneHandConscious = 6,
        HoldOneHandStunned = 7,
        HoldTwoHandStunned = 8,
        FrontCarry = 9,
        OverheadCarry = 10,
        DualCarry = 11,
        ThrowWindup = 12,
        Release = 13,
        Struggle = 14,
        RecoverFromCarry = 15
    }

    public enum HoldVariant : byte
    {
        None = 0,
        Object = 1,
        ConsciousPlayer = 2,
        StunnedPlayer = 3,
        FrontCarry = 4,
        OverheadCarry = 5,
        DualCarry = 6,
        CarriedVictim = 7
    }

    public enum HandHoldMode : byte
    {
        None = 0,
        Reach = 1,
        Object = 2,
        ConsciousPlayer = 3,
        StunnedPlayer = 4,
        CarrySupport = 5
    }

    [Header("References")]
    [SerializeField] private NetworkPlayer networkPlayer;
    [SerializeField] private ProceduralGrabArm proceduralGrabArm;
    [SerializeField] private SSAFYPlayTime.Character.CarryRig carryRig;
    [SerializeField] private HandGrabHandler leftHand;
    [SerializeField] private HandGrabHandler rightHand;

    [Header("Debug")]
    [SerializeField] private bool autoRefresh = true;

    [Header("Resolved State")]
    [SerializeField] private GrabActionState currentActionState;
    [SerializeField] private HoldVariant currentHoldVariant;
    [SerializeField] private HandHoldMode leftHandMode;
    [SerializeField] private HandHoldMode rightHandMode;

    private GrabActionState _previousActionState;
    private HoldVariant _previousHoldVariant;
    private bool _runtimeGrabRigEnsured;

    public GrabActionState CurrentActionState => currentActionState;
    public HoldVariant CurrentHoldVariant => currentHoldVariant;
    public HandHoldMode LeftHandMode => leftHandMode;
    public HandHoldMode RightHandMode => rightHandMode;
    public bool IsAnyReachActive => leftHandMode == HandHoldMode.Reach || rightHandMode == HandHoldMode.Reach;
    public bool IsAnyHoldActive => IsHoldingMode(leftHandMode) || IsHoldingMode(rightHandMode);
    public bool IsAnyStunnedHoldActive => IsStunnedHoldVariant(currentHoldVariant);
    public bool IsDualStunnedHoldActive => IsDualStunnedActionState(currentActionState);
    public bool IsGrabActionActive => IsActiveGrabActionState(currentActionState);
    public bool IsCarryState => ShouldUseCarryPresentationState(currentActionState, currentHoldVariant);
    public bool ShouldUseCarryPresentation => ShouldUseCarryPresentationState(currentActionState, currentHoldVariant);
    public bool ShouldUseGrabPresentation => ShouldUseGrabPresentationState(currentActionState, currentHoldVariant);
    public bool ShouldPreserveGrabPose => ShouldPreserveGrabPoseState(currentActionState, currentHoldVariant, IsAnyHoldActive);
    public bool ShouldLockFacingToHoldTarget => ShouldLockFacingToHoldTargetState(currentActionState, currentHoldVariant, IsAnyHoldActive);

    public static bool IsStunnedHoldVariant(HoldVariant holdVariant)
    {
        return holdVariant == HoldVariant.StunnedPlayer ||
               holdVariant == HoldVariant.FrontCarry ||
               holdVariant == HoldVariant.OverheadCarry ||
               holdVariant == HoldVariant.DualCarry;
    }

    public static bool IsDualStunnedActionState(GrabActionState actionState)
    {
        return actionState == GrabActionState.HoldTwoHandStunned ||
               actionState == GrabActionState.DualCarry;
    }

    public static bool IsActiveGrabActionState(GrabActionState actionState)
    {
        return actionState != GrabActionState.Idle &&
               actionState != GrabActionState.Struggle &&
               actionState != GrabActionState.RecoverFromCarry &&
               actionState != GrabActionState.Release &&
               actionState != GrabActionState.ThrowWindup;
    }

    public static bool ShouldUseCarryPresentationState(GrabActionState actionState, HoldVariant holdVariant)
    {
        return actionState == GrabActionState.FrontCarry ||
               actionState == GrabActionState.OverheadCarry ||
               actionState == GrabActionState.DualCarry ||
               holdVariant == HoldVariant.CarriedVictim;
    }

    public static bool ShouldUseGrabPresentationState(GrabActionState actionState, HoldVariant holdVariant)
    {
        return IsActiveGrabActionState(actionState) &&
               !ShouldUseCarryPresentationState(actionState, holdVariant);
    }

    public static bool ShouldPreserveGrabPoseState(
        GrabActionState actionState,
        HoldVariant holdVariant,
        bool hasAnyHold)
    {
        return hasAnyHold ||
               ShouldUseCarryPresentationState(actionState, holdVariant) ||
               IsActiveGrabActionState(actionState);
    }

    public static bool ShouldLockFacingToHoldTargetState(
        GrabActionState actionState,
        HoldVariant holdVariant,
        bool hasAnyHold)
    {
        return hasAnyHold ||
               ShouldUseCarryPresentationState(actionState, holdVariant);
    }

    public static HoldVariant ResolveCarryHoldVariant(SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode carryMode)
    {
        return carryMode switch
        {
            SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry => HoldVariant.OverheadCarry,
            SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedDualCarry => HoldVariant.DualCarry,
            SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim => HoldVariant.CarriedVictim,
            _ => HoldVariant.None
        };
    }

    public static GrabActionState ResolveCarryActionState(SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode carryMode)
    {
        return carryMode switch
        {
            SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry => GrabActionState.OverheadCarry,
            SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedDualCarry => GrabActionState.DualCarry,
            _ => GrabActionState.Idle
        };
    }

    public static bool IsThrowableObjectState(GrabActionState actionState, HoldVariant holdVariant)
    {
        if (holdVariant == HoldVariant.Object)
            return actionState == GrabActionState.HoldOneHandObject;

        if (!IsStunnedHoldVariant(holdVariant))
            return false;

        return actionState == GrabActionState.HoldOneHandStunned ||
               actionState == GrabActionState.HoldTwoHandStunned ||
               actionState == GrabActionState.FrontCarry ||
               actionState == GrabActionState.OverheadCarry ||
               actionState == GrabActionState.DualCarry;
    }

    public HandHoldMode GetHandMode(HandGrabHandler.HandSide side)
    {
        return side == HandGrabHandler.HandSide.Left ? leftHandMode : rightHandMode;
    }

    public bool IsHandHolding(HandGrabHandler.HandSide side)
    {
        return IsHoldingMode(GetHandMode(side));
    }

    public bool IsHandReaching(HandGrabHandler.HandSide side)
    {
        return GetHandMode(side) == HandHoldMode.Reach;
    }

    public bool HasAnyHold()
    {
        if (TryGetReplicatedResolvedState(out _, out _, out var replicatedLeftMode, out var replicatedRightMode))
            return IsHoldingMode(replicatedLeftMode) || IsHoldingMode(replicatedRightMode);

        ResolveReferences();
        return (leftHand != null && leftHand.IsHolding) ||
               (rightHand != null && rightHand.IsHolding);
    }

    public bool HasThrowableHold()
    {
        if (TryGetReplicatedResolvedState(out var replicatedActionState, out var replicatedHoldVariant, out _, out _))
            return IsThrowableObjectState(replicatedActionState, replicatedHoldVariant);

        ResolveReferences();
        return IsHoldingThrowableObject(leftHand) ||
               IsHoldingThrowableObject(rightHand);
    }

    public bool HasAnyStunnedHold()
    {
        if (TryGetReplicatedResolvedState(out _, out var replicatedHoldVariant, out _, out _))
        {
            return replicatedHoldVariant == HoldVariant.StunnedPlayer ||
                   replicatedHoldVariant == HoldVariant.FrontCarry ||
                   replicatedHoldVariant == HoldVariant.OverheadCarry ||
                   replicatedHoldVariant == HoldVariant.DualCarry;
        }

        ResolveReferences();
        return (leftHand != null && leftHand.IsHoldingStunnedPlayer) ||
               (rightHand != null && rightHand.IsHoldingStunnedPlayer);
    }

    public bool IsDualHandHoldingSameStunnedTarget()
    {
        if (TryGetReplicatedResolvedState(out var replicatedActionState, out _, out _, out _))
        {
            return replicatedActionState == GrabActionState.HoldTwoHandStunned ||
                   replicatedActionState == GrabActionState.DualCarry;
        }

        ResolveReferences();
        if (leftHand == null || rightHand == null)
            return false;

        return leftHand.IsHoldingStunnedPlayer &&
               rightHand.IsHoldingStunnedPlayer &&
               leftHand.GrabTargetRoot != null &&
               leftHand.GrabTargetRoot == rightHand.GrabTargetRoot;
    }

    public bool ShouldProcessGrabLoop()
    {
        if (networkPlayer == null)
            return false;

        return IsAnyReachActive || networkPlayer.IsGrabActive || currentActionState == GrabActionState.AttachPending;
    }

    public bool ShouldAttemptGrab(HandGrabHandler handler)
    {
        if (handler == null)
            return false;

        if (IsHandHolding(handler.Side))
            return false;

        var mode = GetHandMode(handler.Side);
        if (mode == HandHoldMode.Reach)
            return true;

        return networkPlayer != null && networkPlayer.IsHandGrabActive(handler.Side) && !handler.IsHolding;
    }

    public SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType ResolveGrabTargetType(
        NetworkPlayer targetPlayer,
        Rigidbody targetBody)
    {
        if (targetPlayer != null && !targetPlayer.IsActiveRagdoll)
            return SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.StunnedPlayer;

        if (targetPlayer != null)
            return SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.Player;

        return SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.Object;
    }

    public bool IsRecoveringTarget(NetworkPlayer targetPlayer)
    {
        return targetPlayer != null &&
               targetPlayer.GetPhysicalPhase() == NetworkPlayer.PhysicalPhase.Recovering;
    }

    public bool ShouldApplyVictimGrabState(SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType targetType)
    {
        return targetType != SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.Player;
    }

    public bool ShouldTrackVictimGrabRelation(NetworkPlayer targetPlayer)
    {
        return targetPlayer != null && !targetPlayer.IsDeadState;
    }

    public void ResolveAttachTarget(
        Rigidbody originalTargetBody,
        Vector3 originalAnchorWorld,
        SSAFYPlayTime.Character.GrabAnchorPoint attachedAnchorPoint,
        SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType targetType,
        NetworkPlayer targetPlayer,
        out Rigidbody jointTargetBody,
        out Vector3 jointAnchorWorld)
    {
        jointTargetBody = originalTargetBody;
        jointAnchorWorld = originalAnchorWorld;

        if (attachedAnchorPoint != null && attachedAnchorPoint.ParentBoneRigidbody != null)
        {
            jointTargetBody = attachedAnchorPoint.ParentBoneRigidbody;
            jointAnchorWorld = attachedAnchorPoint.GetGripWorldPosition();
            return;
        }

        if (targetType != SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.StunnedPlayer || targetPlayer == null)
            return;

        var puppet = targetPlayer.GetComponentInChildren<PuppetMaster>(true);
        if (puppet == null || puppet.muscles == null || puppet.muscles.Length == 0)
            return;

        var hipsJoint = puppet.muscles[0].joint;
        if (hipsJoint == null)
            return;

        var hipsBody = hipsJoint.GetComponent<Rigidbody>();
        if (hipsBody == null)
            return;

        jointTargetBody = hipsBody;
        jointAnchorWorld = hipsBody.worldCenterOfMass;
    }

    public void RefreshNow()
    {
        ResolveReferences();
        ComputeResolvedState();
    }

    private void Awake()
    {
        ResolveReferences();
        ComputeResolvedState();
    }

    private void Update()
    {
        if (!autoRefresh)
            return;

        ComputeResolvedState();
    }

    private void ResolveReferences()
    {
        if (networkPlayer == null)
            networkPlayer = GetComponent<NetworkPlayer>();

        if (proceduralGrabArm == null)
            proceduralGrabArm = GetComponentInChildren<ProceduralGrabArm>(true);

        if (carryRig == null && networkPlayer != null)
            carryRig = networkPlayer.GetCarryRig();

        if (leftHand == null || rightHand == null)
        {
            var handlers = GetComponentsInChildren<HandGrabHandler>(true);
            foreach (var handler in handlers)
            {
                if (handler == null)
                    continue;

                if (handler.Side == HandGrabHandler.HandSide.Left && leftHand == null)
                    leftHand = handler;
                else if (handler.Side == HandGrabHandler.HandSide.Right && rightHand == null)
                    rightHand = handler;
            }
        }

        if (!_runtimeGrabRigEnsured)
        {
            SSAFYPlayTime.Character.GrabRigRuntimeBootstrap.EnsureCharacterRig(transform.root, leftHand, rightHand);
            _runtimeGrabRigEnsured = true;
        }
    }

    private void ComputeResolvedState()
    {
        if (networkPlayer == null)
            return;

        _previousActionState = currentActionState;
        _previousHoldVariant = currentHoldVariant;

        if (networkPlayer.IsNetworkReady &&
            !networkPlayer.HasStateAuthority &&
            networkPlayer.TryGetReplicatedGrabControllerState(
                out var replicatedActionState,
                out var replicatedHoldVariant,
                out var replicatedLeftMode,
                out var replicatedRightMode))
        {
            currentActionState = replicatedActionState;
            currentHoldVariant = replicatedHoldVariant;
            leftHandMode = replicatedLeftMode;
            rightHandMode = replicatedRightMode;
            return;
        }

        var carryMode = networkPlayer.GetLocalCarryMode();
        leftHandMode = ResolveHandMode(leftHand, carryMode);
        rightHandMode = ResolveHandMode(rightHand, carryMode);
        currentHoldVariant = ResolveHoldVariant(carryMode);
        currentActionState = ResolveActionState(carryMode);
    }

    private HandHoldMode ResolveHandMode(
        HandGrabHandler handler,
        SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode carryMode)
    {
        if (handler == null)
            return HandHoldMode.None;

        if (handler.IsHoldingStunnedPlayer)
        {
            return carryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry ||
                   carryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedDualCarry
                ? HandHoldMode.CarrySupport
                : HandHoldMode.StunnedPlayer;
        }

        if (handler.IsHoldingConsciousPlayer)
            return HandHoldMode.ConsciousPlayer;

        if (handler.IsHolding)
            return HandHoldMode.Object;

        if (handler.IsReaching || networkPlayer.IsHandGrabActive(handler.Side))
            return HandHoldMode.Reach;

        return HandHoldMode.None;
    }

    private HoldVariant ResolveHoldVariant(SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode carryMode)
    {
        var carryHoldVariant = ResolveCarryHoldVariant(carryMode);
        return carryHoldVariant != HoldVariant.None
            ? carryHoldVariant
            : ResolveDirectHoldVariant();
    }

    private HoldVariant ResolveDirectHoldVariant()
    {
        var leftHolding = leftHand != null && leftHand.IsHolding;
        var rightHolding = rightHand != null && rightHand.IsHolding;
        if (!leftHolding && !rightHolding)
            return HoldVariant.None;

        if ((leftHand != null && leftHand.IsHoldingStunnedPlayer) ||
            (rightHand != null && rightHand.IsHoldingStunnedPlayer))
            return HoldVariant.StunnedPlayer;

        if ((leftHand != null && leftHand.IsHoldingConsciousPlayer) ||
            (rightHand != null && rightHand.IsHoldingConsciousPlayer))
            return HoldVariant.ConsciousPlayer;

        return HoldVariant.Object;
    }

    private GrabActionState ResolveActionState(SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode carryMode)
    {
        var phase = networkPlayer.GetPhysicalPhase();
        switch (phase)
        {
            case NetworkPlayer.PhysicalPhase.BeingGrabbed:
            case NetworkPlayer.PhysicalPhase.Dragged:
            case NetworkPlayer.PhysicalPhase.BeingCarriedStunned:
                return GrabActionState.Struggle;
            case NetworkPlayer.PhysicalPhase.Recovering:
                if (_previousActionState == GrabActionState.FrontCarry ||
                    _previousActionState == GrabActionState.OverheadCarry ||
                    _previousActionState == GrabActionState.DualCarry ||
                    _previousHoldVariant == HoldVariant.CarriedVictim)
                {
                    return GrabActionState.RecoverFromCarry;
                }
                break;
        }

        var carryActionState = ResolveCarryActionState(carryMode);
        if (carryActionState != GrabActionState.Idle)
            return carryActionState;

        var leftHolding = leftHand != null && leftHand.IsHolding;
        var rightHolding = rightHand != null && rightHand.IsHolding;
        var leftReaching = leftHand != null && leftHand.IsReaching;
        var rightReaching = rightHand != null && rightHand.IsReaching;
        var leftGrabIntent = leftHand != null && networkPlayer.IsHandGrabActive(leftHand.Side);
        var rightGrabIntent = rightHand != null && networkPlayer.IsHandGrabActive(rightHand.Side);

        if (leftHolding || rightHolding)
        {
            if ((leftHand != null && leftHand.IsHoldingStunnedPlayer) &&
                (rightHand != null && rightHand.IsHoldingStunnedPlayer))
                return GrabActionState.HoldTwoHandStunned;

            if ((leftHand != null && leftHand.IsHoldingStunnedPlayer) ||
                (rightHand != null && rightHand.IsHoldingStunnedPlayer))
                return GrabActionState.HoldOneHandStunned;

            if ((leftHand != null && leftHand.IsHoldingConsciousPlayer) ||
                (rightHand != null && rightHand.IsHoldingConsciousPlayer))
                return GrabActionState.HoldOneHandConscious;

            return GrabActionState.HoldOneHandObject;
        }

        if (leftReaching || rightReaching)
            return GrabActionState.AttachPending;

        if (leftGrabIntent && rightGrabIntent)
            return GrabActionState.ReachBoth;

        if (leftGrabIntent)
            return GrabActionState.ReachLeft;

        if (rightGrabIntent)
            return GrabActionState.ReachRight;

        return GrabActionState.Idle;
    }

    public string BuildSummary()
    {
        var phase = networkPlayer != null ? networkPlayer.GetPhysicalPhase().ToString() : "None";
        var carryMode = networkPlayer != null ? networkPlayer.GetLocalCarryMode().ToString() : "None";
        return $"phase={phase} carry={carryMode} action={currentActionState} variant={currentHoldVariant} " +
               $"left={leftHandMode} right={rightHandMode} rig={(carryRig != null)} arm={(proceduralGrabArm != null)}";
    }

    private static bool IsHoldingMode(HandHoldMode mode)
    {
        return mode == HandHoldMode.Object ||
               mode == HandHoldMode.ConsciousPlayer ||
               mode == HandHoldMode.StunnedPlayer ||
               mode == HandHoldMode.CarrySupport;
    }

    private static bool IsHoldingThrowableObject(HandGrabHandler handler)
    {
        return handler != null && handler.IsHoldingThrowableTarget;
    }

    private bool TryGetReplicatedResolvedState(
        out GrabActionState actionState,
        out HoldVariant holdVariant,
        out HandHoldMode leftMode,
        out HandHoldMode rightMode)
    {
        actionState = GrabActionState.Idle;
        holdVariant = HoldVariant.None;
        leftMode = HandHoldMode.None;
        rightMode = HandHoldMode.None;

        ResolveReferences();
        if (networkPlayer == null || !networkPlayer.IsNetworkReady || networkPlayer.HasStateAuthority)
            return false;

        return networkPlayer.TryGetReplicatedGrabControllerState(
                   out actionState,
                   out holdVariant,
                   out leftMode,
                   out rightMode);
    }

    private bool IsOverheadCarryPoseActive()
    {
        return proceduralGrabArm != null &&
               proceduralGrabArm.IsOverheadCarryPoseActive();
    }
}
