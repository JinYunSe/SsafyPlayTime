using System.Reflection;
using NUnit.Framework;
using SSAFYPlayTime.Character;
using UnityEngine;

public sealed class NetworkPlayerInteractionDecisionTests
{
    private static System.Type ResolvePhysicalPhaseType()
    {
        var type = typeof(NetworkPlayer).GetNestedType("PhysicalPhase", BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return type;
    }

    private static object ResolvePhysicalPhase(string name)
    {
        return System.Enum.Parse(ResolvePhysicalPhaseType(), name);
    }

    private static System.Type ResolveStunPresentationPhaseType()
    {
        var type = typeof(NetworkPlayer).GetNestedType("StunPresentationPhase", BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return type;
    }

    private static object ResolveStunPresentationPhase(string name)
    {
        return System.Enum.Parse(ResolveStunPresentationPhaseType(), name);
    }

    private static System.Type ResolveAerialKickPresentationStateType()
    {
        var type = typeof(NetworkPlayer).GetNestedType("AerialKickPresentationState", BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return type;
    }

    private static object ResolveAerialKickPresentationState(string name)
    {
        return System.Enum.Parse(ResolveAerialKickPresentationStateType(), name);
    }

    private static bool InvokeShouldAllowKickFallback(bool anyHolding, bool hasHeldRuntimeItem)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldAllowKickFallback",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(null, new object[] { anyHolding, hasHeldRuntimeItem });
    }

    private static bool InvokeShouldUseProxyLocalSoftFlopPresentation(
        object phase,
        object presentationPhase,
        bool usesAnimatedVisualPresentationRig,
        bool hasStateAuthority)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var stunPresentationPhaseType = ResolveStunPresentationPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldUseProxyLocalSoftFlopPresentation",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
            null,
            new[]
            {
                physicalPhaseType,
                stunPresentationPhaseType,
                typeof(bool),
                typeof(bool)
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                phase,
                presentationPhase,
                usesAnimatedVisualPresentationRig,
                hasStateAuthority
            });
    }

    private static bool InvokeShouldUseAuthorityAnimatedPlainStunPresentation(
        object phase,
        object presentationPhase,
        bool usesAnimatedVisualPresentationRig,
        bool hasStateAuthority)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var stunPresentationPhaseType = ResolveStunPresentationPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldUseAuthorityAnimatedPlainStunPresentation",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
            null,
            new[]
            {
                physicalPhaseType,
                stunPresentationPhaseType,
                typeof(bool),
                typeof(bool)
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                phase,
                presentationPhase,
                usesAnimatedVisualPresentationRig,
                hasStateAuthority
            });
    }

    private static bool InvokeShouldAllowAerialKickDecision(
        bool isGrounded,
        bool anyHolding,
        bool hasHeldRuntimeItem,
        bool isGrabActive,
        bool hasReachPending,
        bool hasAttachPending,
        bool canPerformCombatActions,
        object phase)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldAllowAerialKickDecision",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                physicalPhaseType
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                isGrounded,
                anyHolding,
                hasHeldRuntimeItem,
                isGrabActive,
                hasReachPending,
                hasAttachPending,
                canPerformCombatActions,
                phase
            });
    }

    private static bool InvokeShouldAllowHeadbuttDecision(
        bool anyHolding,
        bool hasHeldRuntimeItem,
        bool isGrabActive,
        bool hasReachPending,
        bool hasAttachPending,
        bool canPerformHeadbuttActions,
        bool beingGrabbed,
        bool dragged,
        float instability,
        object phase)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldAllowHeadbuttDecision",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(float),
                physicalPhaseType
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                anyHolding,
                hasHeldRuntimeItem,
                isGrabActive,
                hasReachPending,
                hasAttachPending,
                canPerformHeadbuttActions,
                beingGrabbed,
                dragged,
                instability,
                phase
            });
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }

    private static string InvokeResolveAuthorityPhysicalPhaseCore(
        object currentPhase,
        float instability,
        bool isRecovering,
        bool isRecoverStabilizing,
        bool anyHolding,
        bool isHoldingStunnedPlayer,
        bool isGrabActive,
        bool hasHeldEquipment,
        bool isGroggy,
        bool beingGrabbed,
        bool dragged)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "ResolveAuthorityPhysicalPhaseCore",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                physicalPhaseType,
                typeof(float),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(bool)
            },
            null);

        Assert.That(method, Is.Not.Null);
        return method.Invoke(
            null,
            new object[]
            {
                currentPhase,
                instability,
                isRecovering,
                isRecoverStabilizing,
                anyHolding,
                isHoldingStunnedPlayer,
                isGrabActive,
                hasHeldEquipment,
                isGroggy,
                beingGrabbed,
                dragged
            }).ToString();
    }

    private static bool InvokeShouldStartCarryReleaseSettle(
        CarryPhysicsProfile.CarryMode previousMode,
        CarryPhysicsProfile.CarryMode newMode,
        bool suppressNextCarryReleaseSettle)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldStartCarryReleaseSettle",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[] { previousMode, newMode, suppressNextCarryReleaseSettle });
    }

    private static bool InvokeShouldEnterAerialKickBallisticFall(
        bool hasLeftGround,
        bool isGrounded,
        float verticalVelocity,
        bool nearGround,
        bool hasRecentGroundContact)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldEnterAerialKickBallisticFall",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                hasLeftGround,
                isGrounded,
                verticalVelocity,
                nearGround,
                hasRecentGroundContact
            });
    }

    private static bool InvokeShouldAllowAerialKickAirborneStart(
        bool isGrounded,
        float airborneElapsed,
        float coyoteTimeRemaining,
        bool feetClear,
        bool hasActiveAerialKickState)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldAllowAerialKickAirborneStart",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                isGrounded,
                airborneElapsed,
                coyoteTimeRemaining,
                feetClear,
                hasActiveAerialKickState
            });
    }

    private static bool InvokeShouldUseGroundedAerialKickMissPlop(
        bool isGrounded,
        bool rawGrounded,
        bool footLandingSignal,
        bool hasRecentGroundContact)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldUseGroundedAerialKickMissPlop",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                isGrounded,
                rawGrounded,
                footLandingSignal,
                hasRecentGroundContact
            });
    }

    private static bool InvokeHasConfirmedAerialKickLandingContact(
        bool rawGrounded,
        bool footLandingSignal,
        bool hasRecentGroundContact)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "HasConfirmedAerialKickLandingContact",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                rawGrounded,
                footLandingSignal,
                hasRecentGroundContact
            });
    }

    private static float InvokeResolveAerialKickMissPenaltyDuration(float configuredSelfStunDuration, bool groundedPlop)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ResolveAerialKickMissPenaltyDuration",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (float)method.Invoke(null, new object[] { configuredSelfStunDuration, groundedPlop });
    }

    private static bool InvokeShouldEndAerialKickProxyPresentation(
        bool isGrounded,
        object aerialKickPresentationState,
        float predictionAge,
        object phase)
    {
        var aerialKickPresentationStateType = ResolveAerialKickPresentationStateType();
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldEndAerialKickProxyPresentation",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                typeof(bool),
                aerialKickPresentationStateType,
                typeof(float),
                physicalPhaseType
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                isGrounded,
                aerialKickPresentationState,
                predictionAge,
                phase
            });
    }

    private static bool InvokeShouldApplyPlainStunEntryDamping(
        bool applyEntryDamping,
        bool plainStunEntry,
        bool suppressImplicitPlainStunDamping)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldApplyPlainStunEntryDamping",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                applyEntryDamping,
                plainStunEntry,
                suppressImplicitPlainStunDamping
            });
    }

    private static bool InvokeShouldUseGroundedPlainStunNoCollapseEntry(
        bool beingGrabbed,
        bool isGrounded,
        float rootPlanarSpeed,
        float rootVerticalSpeed,
        float rootAngularSpeed,
        float pelvisVerticalSpeed,
        bool forceGroundedStunCollapse)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldUseGroundedPlainStunNoCollapseEntry",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                typeof(bool),
                typeof(bool),
                typeof(float),
                typeof(float),
                typeof(float),
                typeof(float),
                typeof(bool)
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                beingGrabbed,
                isGrounded,
                rootPlanarSpeed,
                rootVerticalSpeed,
                rootAngularSpeed,
                pelvisVerticalSpeed,
                forceGroundedStunCollapse
            });
    }

    private static bool InvokeUsesPhysicsPosePresentation(object phase)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "UsesPhysicsPosePresentation",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { physicalPhaseType },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(null, new[] { phase });
    }

    private static bool InvokeShouldSuppressGroundedPlainStunUpwardRootCorrection(
        object phase,
        bool hasRecentGroundContact,
        bool isRecovering,
        bool isRecoverStabilizing,
        int beingGrabbedRefCount)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldSuppressGroundedPlainStunUpwardRootCorrection",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
            null,
            new[]
            {
                physicalPhaseType,
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(int)
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[]
            {
                phase,
                hasRecentGroundContact,
                isRecovering,
                isRecoverStabilizing,
                beingGrabbedRefCount
            });
    }

    private static bool InvokeIsPlainStunPhase(object phase)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "IsPlainStunPhase",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { physicalPhaseType },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(null, new[] { phase });
    }

    private static string InvokeResolveStunnedGrabTransportPhase(
        object previousPhase,
        int beingGrabbedRefCount,
        bool isGrounded,
        bool draggedTransitionQualified,
        bool carriedTransitionQualified)
    {
        var physicalPhaseType = ResolvePhysicalPhaseType();
        var method = typeof(NetworkPlayer).GetMethod(
            "ResolveStunnedGrabTransportPhase",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                physicalPhaseType,
                typeof(int),
                typeof(bool),
                typeof(bool),
                typeof(bool)
            },
            null);

        Assert.That(method, Is.Not.Null);
        var resolved = method.Invoke(
            null,
            new object[]
            {
                previousPhase,
                beingGrabbedRefCount,
                isGrounded,
                draggedTransitionQualified,
                carriedTransitionQualified
            });
        return resolved.ToString();
    }

    [Test]
    public void KickFallback_RequiresEmptyHands()
    {
        Assert.That(InvokeShouldAllowKickFallback(anyHolding: false, hasHeldRuntimeItem: false), Is.True);
        Assert.That(InvokeShouldAllowKickFallback(anyHolding: true, hasHeldRuntimeItem: false), Is.False);
        Assert.That(InvokeShouldAllowKickFallback(anyHolding: false, hasHeldRuntimeItem: true), Is.False);
        Assert.That(InvokeShouldAllowKickFallback(anyHolding: true, hasHeldRuntimeItem: true), Is.False);
    }

    [Test]
    public void CarryReleaseSettle_CanBeSuppressedForManualRelease()
    {
        Assert.That(
            InvokeShouldStartCarryReleaseSettle(
                CarryPhysicsProfile.CarryMode.StunnedSingleCarry,
                CarryPhysicsProfile.CarryMode.None,
                suppressNextCarryReleaseSettle: false),
            Is.True);

        Assert.That(
            InvokeShouldStartCarryReleaseSettle(
                CarryPhysicsProfile.CarryMode.StunnedSingleCarry,
                CarryPhysicsProfile.CarryMode.None,
                suppressNextCarryReleaseSettle: true),
            Is.False);

        Assert.That(
            InvokeShouldStartCarryReleaseSettle(
                CarryPhysicsProfile.CarryMode.None,
                CarryPhysicsProfile.CarryMode.None,
                suppressNextCarryReleaseSettle: false),
            Is.False);
    }

    [Test]
    public void AerialKickDecision_BlocksGroundGrabAndAttachConflicts()
    {
        Assert.That(
            InvokeShouldAllowAerialKickDecision(
                isGrounded: true,
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformCombatActions: true,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldAllowAerialKickDecision(
                isGrounded: false,
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: true,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformCombatActions: true,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldAllowAerialKickDecision(
                isGrounded: false,
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: true,
                hasAttachPending: false,
                canPerformCombatActions: true,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldAllowAerialKickDecision(
                isGrounded: false,
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: true,
                canPerformCombatActions: true,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);
    }

    [Test]
    public void AerialKickDecision_BlocksConflictingPhases()
    {
        Assert.That(
            InvokeShouldAllowAerialKickDecision(
                isGrounded: false,
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformCombatActions: true,
                phase: ResolvePhysicalPhase("GrabIntent")),
            Is.False);

        Assert.That(
            InvokeShouldAllowAerialKickDecision(
                isGrounded: false,
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformCombatActions: true,
                phase: ResolvePhysicalPhase("BeingGrabbed")),
            Is.False);
    }

    [Test]
    public void AerialKickAirborneStart_RequiresFeetClearAndStableAirborneWindow()
    {
        Assert.That(
            InvokeShouldAllowAerialKickAirborneStart(
                isGrounded: false,
                airborneElapsed: 0.12f,
                coyoteTimeRemaining: 0f,
                feetClear: true,
                hasActiveAerialKickState: false),
            Is.True);

        Assert.That(
            InvokeShouldAllowAerialKickAirborneStart(
                isGrounded: false,
                airborneElapsed: 0.12f,
                coyoteTimeRemaining: 0.02f,
                feetClear: true,
                hasActiveAerialKickState: false),
            Is.False);

        Assert.That(
            InvokeShouldAllowAerialKickAirborneStart(
                isGrounded: false,
                airborneElapsed: 0.12f,
                coyoteTimeRemaining: 0f,
                feetClear: false,
                hasActiveAerialKickState: false),
            Is.False);

        Assert.That(
            InvokeShouldAllowAerialKickAirborneStart(
                isGrounded: false,
                airborneElapsed: 0.05f,
                coyoteTimeRemaining: 0f,
                feetClear: true,
                hasActiveAerialKickState: false),
            Is.False);

        Assert.That(
            InvokeShouldAllowAerialKickAirborneStart(
                isGrounded: false,
                airborneElapsed: 0.12f,
                coyoteTimeRemaining: 0f,
                feetClear: true,
                hasActiveAerialKickState: true),
            Is.False);
    }

    [Test]
    public void HeadbuttDecision_RequiresStableFreeCombatState()
    {
        Assert.That(
            InvokeShouldAllowHeadbuttDecision(
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformHeadbuttActions: true,
                beingGrabbed: false,
                dragged: false,
                instability: 0.1f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.True);

        Assert.That(
            InvokeShouldAllowHeadbuttDecision(
                anyHolding: true,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformHeadbuttActions: true,
                beingGrabbed: false,
                dragged: false,
                instability: 0.1f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldAllowHeadbuttDecision(
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: true,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformHeadbuttActions: true,
                beingGrabbed: false,
                dragged: false,
                instability: 0.1f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);
    }

    [Test]
    public void HeadbuttDecision_BlocksGrabDragRecoverAndHighInstability()
    {
        Assert.That(
            InvokeShouldAllowHeadbuttDecision(
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformHeadbuttActions: true,
                beingGrabbed: true,
                dragged: false,
                instability: 0.1f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldAllowHeadbuttDecision(
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformHeadbuttActions: true,
                beingGrabbed: false,
                dragged: true,
                instability: 0.1f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldAllowHeadbuttDecision(
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformHeadbuttActions: true,
                beingGrabbed: false,
                dragged: false,
                instability: 0.6f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldAllowHeadbuttDecision(
                anyHolding: false,
                hasHeldRuntimeItem: false,
                isGrabActive: false,
                hasReachPending: false,
                hasAttachPending: false,
                canPerformHeadbuttActions: true,
                beingGrabbed: false,
                dragged: false,
                instability: 0.1f,
                phase: ResolvePhysicalPhase("Recovering")),
            Is.False);
    }

    [Test]
    public void PhysicalPhasePresentation_OnlyExpectedSoftFlopPhasesUsePhysicsPosePresentation()
    {
        foreach (var phaseName in new[]
                 {
                     "BeingGrabbed",
                     "Dragged",
                     "Unstable",
                     "StunnedCollapse",
                     "Stunned",
                     "SettledStunned",
                     "DraggedStunned",
                     "BeingCarriedStunned"
                 })
        {
            Assert.That(
                InvokeUsesPhysicsPosePresentation(ResolvePhysicalPhase(phaseName)),
                Is.True,
                phaseName);
        }

        foreach (var phaseName in new[]
                 {
                     "Stable",
                     "GrabIntent",
                     "Holding",
                     "Recovering",
                     "CarryingStunned",
                     "WeaponEquipped"
                 })
        {
            Assert.That(
                InvokeUsesPhysicsPosePresentation(ResolvePhysicalPhase(phaseName)),
                Is.False,
                phaseName);
        }
    }

    [Test]
    public void PlainStunPhase_IsLimitedToCollapseStunAndSettledVariants()
    {
        foreach (var phaseName in new[] { "StunnedCollapse", "Stunned", "SettledStunned" })
        {
            Assert.That(
                InvokeIsPlainStunPhase(ResolvePhysicalPhase(phaseName)),
                Is.True,
                phaseName);
        }

        foreach (var phaseName in new[]
                 {
                     "BeingGrabbed",
                     "Dragged",
                     "Unstable",
                     "Recovering",
                     "DraggedStunned",
                     "BeingCarriedStunned",
                     "CarryingStunned"
                 })
        {
            Assert.That(
                InvokeIsPlainStunPhase(ResolvePhysicalPhase(phaseName)),
                Is.False,
                phaseName);
        }
    }

    [Test]
    public void ProxyLocalSoftFlopPresentation_OnlyAppliesToAnimatedVisualPlainStunProxies()
    {
        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("StunnedCollapse"),
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.True);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("SettledStunned"),
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.True);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("DraggedStunned"),
                ResolveStunPresentationPhase("Active"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.False);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("Stunned"),
                ResolveStunPresentationPhase("RecoverStabilizing"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.False);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("Stunned"),
                ResolveStunPresentationPhase("Active"),
                usesAnimatedVisualPresentationRig: false,
                hasStateAuthority: false),
            Is.False);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("Stunned"),
                ResolveStunPresentationPhase("Active"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: true),
            Is.False);
    }

    [Test]
    public void AuthorityAnimatedPlainStunPresentation_OnlyAppliesToAnimatedVisualPlainStunAuthority()
    {
        Assert.That(
            InvokeShouldUseAuthorityAnimatedPlainStunPresentation(
                ResolvePhysicalPhase("StunnedCollapse"),
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: true),
            Is.False);

        Assert.That(
            InvokeShouldUseAuthorityAnimatedPlainStunPresentation(
                ResolvePhysicalPhase("Stunned"),
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: true),
            Is.True);

        Assert.That(
            InvokeShouldUseAuthorityAnimatedPlainStunPresentation(
                ResolvePhysicalPhase("SettledStunned"),
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: true),
            Is.True);

        Assert.That(
            InvokeShouldUseAuthorityAnimatedPlainStunPresentation(
                ResolvePhysicalPhase("DraggedStunned"),
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: true),
            Is.False);

        Assert.That(
            InvokeShouldUseAuthorityAnimatedPlainStunPresentation(
                ResolvePhysicalPhase("Stunned"),
                ResolveStunPresentationPhase("RecoverStabilizing"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: true),
            Is.False);

        Assert.That(
            InvokeShouldUseAuthorityAnimatedPlainStunPresentation(
                ResolvePhysicalPhase("Stunned"),
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: false,
                hasStateAuthority: true),
            Is.False);

        Assert.That(
            InvokeShouldUseAuthorityAnimatedPlainStunPresentation(
                ResolvePhysicalPhase("Stunned"),
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.False);
    }

    [Test]
    public void ProceduralHeadbutt_TryTriggerRequiresDriveTarget()
    {
        var root = new GameObject("ProceduralHeadbutt_NoDriveTarget");
        try
        {
            var headbutt = root.AddComponent<ProceduralHeadbutt>();

            Assert.That(headbutt.TryTriggerHeadbutt(Vector3.forward), Is.False);
            Assert.That(headbutt.IsHeadbutting, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ProceduralHeadbutt_TryTriggerStartsWhenHeadRigidbodyIsAvailable()
    {
        var root = new GameObject("ProceduralHeadbutt_WithHeadRigidbody");
        try
        {
            var headbutt = root.AddComponent<ProceduralHeadbutt>();
            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            var headRb = head.AddComponent<Rigidbody>();
            SetPrivateField(headbutt, "_headRb", headRb);

            Assert.That(headbutt.TryTriggerHeadbutt(Vector3.forward), Is.True);
            Assert.That(headbutt.IsHeadbutting, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PhysicalPhaseCore_PrioritizesExplicitHoldAndGrabStatesOverInstability()
    {
        Assert.That(
            InvokeResolveAuthorityPhysicalPhaseCore(
                currentPhase: ResolvePhysicalPhase("Stable"),
                instability: 1f,
                isRecovering: false,
                isRecoverStabilizing: false,
                anyHolding: true,
                isHoldingStunnedPlayer: false,
                isGrabActive: false,
                hasHeldEquipment: false,
                isGroggy: false,
                beingGrabbed: false,
                dragged: false),
            Is.EqualTo("Holding"));

        Assert.That(
            InvokeResolveAuthorityPhysicalPhaseCore(
                currentPhase: ResolvePhysicalPhase("Stable"),
                instability: 1f,
                isRecovering: false,
                isRecoverStabilizing: false,
                anyHolding: false,
                isHoldingStunnedPlayer: false,
                isGrabActive: false,
                hasHeldEquipment: false,
                isGroggy: false,
                beingGrabbed: false,
                dragged: false),
            Is.EqualTo("Unstable"));

        Assert.That(
            InvokeResolveAuthorityPhysicalPhaseCore(
                currentPhase: ResolvePhysicalPhase("Stable"),
                instability: 1f,
                isRecovering: false,
                isRecoverStabilizing: false,
                anyHolding: false,
                isHoldingStunnedPlayer: false,
                isGrabActive: true,
                hasHeldEquipment: false,
                isGroggy: false,
                beingGrabbed: false,
                dragged: false),
            Is.EqualTo("GrabIntent"));

        Assert.That(
            InvokeResolveAuthorityPhysicalPhaseCore(
                currentPhase: ResolvePhysicalPhase("Stable"),
                instability: 1f,
                isRecovering: false,
                isRecoverStabilizing: false,
                anyHolding: true,
                isHoldingStunnedPlayer: true,
                isGrabActive: false,
                hasHeldEquipment: false,
                isGroggy: false,
                beingGrabbed: false,
                dragged: false),
            Is.EqualTo("CarryingStunned"));

        Assert.That(
            InvokeResolveAuthorityPhysicalPhaseCore(
                currentPhase: ResolvePhysicalPhase("Stable"),
                instability: 1f,
                isRecovering: true,
                isRecoverStabilizing: false,
                anyHolding: true,
                isHoldingStunnedPlayer: true,
                isGrabActive: true,
                hasHeldEquipment: true,
                isGroggy: true,
                beingGrabbed: false,
                dragged: false),
            Is.EqualTo("Recovering"));

        Assert.That(
            InvokeResolveAuthorityPhysicalPhaseCore(
                currentPhase: ResolvePhysicalPhase("Stable"),
                instability: 1f,
                isRecovering: false,
                isRecoverStabilizing: true,
                anyHolding: false,
                isHoldingStunnedPlayer: false,
                isGrabActive: false,
                hasHeldEquipment: false,
                isGroggy: false,
                beingGrabbed: false,
                dragged: false),
            Is.EqualTo("Recovering"));
    }

    [Test]
    public void PhysicalPhaseCore_PrioritizesBeingGrabbedOverRecoveringWhenBothFlagsAreSet()
    {
        Assert.That(
            InvokeResolveAuthorityPhysicalPhaseCore(
                currentPhase: ResolvePhysicalPhase("Stable"),
                instability: 0f,
                isRecovering: true,
                isRecoverStabilizing: false,
                anyHolding: false,
                isHoldingStunnedPlayer: false,
                isGrabActive: false,
                hasHeldEquipment: false,
                isGroggy: false,
                beingGrabbed: true,
                dragged: false),
            Is.EqualTo("BeingGrabbed"));

        Assert.That(
            InvokeResolveAuthorityPhysicalPhaseCore(
                currentPhase: ResolvePhysicalPhase("Stable"),
                instability: 0f,
                isRecovering: true,
                isRecoverStabilizing: false,
                anyHolding: false,
                isHoldingStunnedPlayer: false,
                isGrabActive: false,
                hasHeldEquipment: false,
                isGroggy: false,
                beingGrabbed: true,
                dragged: true),
            Is.EqualTo("Dragged"));
    }

    [Test]
    public void AerialKickBallisticFall_RequiresAirborneDescentOrLandingSignal()
    {
        Assert.That(
            InvokeShouldEnterAerialKickBallisticFall(
                hasLeftGround: false,
                isGrounded: false,
                verticalVelocity: -0.1f,
                nearGround: false,
                hasRecentGroundContact: false),
            Is.False);

        Assert.That(
            InvokeShouldEnterAerialKickBallisticFall(
                hasLeftGround: true,
                isGrounded: true,
                verticalVelocity: -0.1f,
                nearGround: false,
                hasRecentGroundContact: false),
            Is.False);

        Assert.That(
            InvokeShouldEnterAerialKickBallisticFall(
                hasLeftGround: true,
                isGrounded: false,
                verticalVelocity: 0.2f,
                nearGround: false,
                hasRecentGroundContact: false),
            Is.False);

        Assert.That(
            InvokeShouldEnterAerialKickBallisticFall(
                hasLeftGround: true,
                isGrounded: false,
                verticalVelocity: -0.1f,
                nearGround: false,
                hasRecentGroundContact: false),
            Is.True);

        Assert.That(
            InvokeShouldEnterAerialKickBallisticFall(
                hasLeftGround: true,
                isGrounded: false,
                verticalVelocity: 0.2f,
                nearGround: true,
                hasRecentGroundContact: false),
            Is.True);

        Assert.That(
            InvokeShouldEnterAerialKickBallisticFall(
                hasLeftGround: true,
                isGrounded: false,
                verticalVelocity: 0.2f,
                nearGround: false,
                hasRecentGroundContact: true),
            Is.True);
    }

    [Test]
    public void AerialKickMissPenalty_UsesGroundedPlopWhenLandingSignalsExist()
    {
        Assert.That(
            InvokeShouldUseGroundedAerialKickMissPlop(
                isGrounded: true,
                rawGrounded: false,
                footLandingSignal: false,
                hasRecentGroundContact: false),
            Is.False);

        Assert.That(
            InvokeShouldUseGroundedAerialKickMissPlop(
                isGrounded: false,
                rawGrounded: true,
                footLandingSignal: false,
                hasRecentGroundContact: false),
            Is.True);

        Assert.That(
            InvokeShouldUseGroundedAerialKickMissPlop(
                isGrounded: false,
                rawGrounded: false,
                footLandingSignal: true,
                hasRecentGroundContact: false),
            Is.False);

        Assert.That(
            InvokeShouldUseGroundedAerialKickMissPlop(
                isGrounded: false,
                rawGrounded: false,
                footLandingSignal: false,
                hasRecentGroundContact: true),
            Is.False);

        Assert.That(
            InvokeShouldUseGroundedAerialKickMissPlop(
                isGrounded: true,
                rawGrounded: false,
                footLandingSignal: true,
                hasRecentGroundContact: true),
            Is.True);

        Assert.That(
            InvokeShouldUseGroundedAerialKickMissPlop(
                isGrounded: false,
                rawGrounded: false,
                footLandingSignal: false,
                hasRecentGroundContact: false),
            Is.False);
    }

    [Test]
    public void AerialKickLandingContact_RequiresRawGroundOrFootSignalWithRecentContact()
    {
        Assert.That(
            InvokeHasConfirmedAerialKickLandingContact(
                rawGrounded: true,
                footLandingSignal: false,
                hasRecentGroundContact: false),
            Is.True);

        Assert.That(
            InvokeHasConfirmedAerialKickLandingContact(
                rawGrounded: false,
                footLandingSignal: true,
                hasRecentGroundContact: true),
            Is.True);

        Assert.That(
            InvokeHasConfirmedAerialKickLandingContact(
                rawGrounded: false,
                footLandingSignal: true,
                hasRecentGroundContact: false),
            Is.False);

        Assert.That(
            InvokeHasConfirmedAerialKickLandingContact(
                rawGrounded: false,
                footLandingSignal: false,
                hasRecentGroundContact: true),
            Is.False);
    }

    [Test]
    public void AerialKickMissPenalty_ResolvesShorterFlopDurationThanConfiguredSelfStun()
    {
        Assert.That(
            InvokeResolveAerialKickMissPenaltyDuration(configuredSelfStunDuration: 0.4f, groundedPlop: true),
            Is.EqualTo(0.26f).Within(0.0001f));

        Assert.That(
            InvokeResolveAerialKickMissPenaltyDuration(configuredSelfStunDuration: 0.4f, groundedPlop: false),
            Is.EqualTo(0.20f).Within(0.0001f));

        Assert.That(
            InvokeResolveAerialKickMissPenaltyDuration(configuredSelfStunDuration: 1.0f, groundedPlop: true),
            Is.EqualTo(0.34f).Within(0.0001f));

        Assert.That(
            InvokeResolveAerialKickMissPenaltyDuration(configuredSelfStunDuration: 0.05f, groundedPlop: false),
            Is.EqualTo(0.16f).Within(0.0001f));
    }

    [Test]
    public void PlainStunEntryDamping_CanBeSuppressedForSoftFlopMissPenalties()
    {
        Assert.That(
            InvokeShouldApplyPlainStunEntryDamping(
                applyEntryDamping: false,
                plainStunEntry: true,
                suppressImplicitPlainStunDamping: false),
            Is.True);

        Assert.That(
            InvokeShouldApplyPlainStunEntryDamping(
                applyEntryDamping: false,
                plainStunEntry: true,
                suppressImplicitPlainStunDamping: true),
            Is.False);

        Assert.That(
            InvokeShouldApplyPlainStunEntryDamping(
                applyEntryDamping: true,
                plainStunEntry: true,
                suppressImplicitPlainStunDamping: true),
            Is.True);
    }

    [Test]
    public void GroundedPlainStunNoCollapseEntry_CanBeForcedOffForHeavyHitContexts()
    {
        Assert.That(
            InvokeShouldUseGroundedPlainStunNoCollapseEntry(
                beingGrabbed: false,
                isGrounded: true,
                rootPlanarSpeed: 0.2f,
                rootVerticalSpeed: 0.05f,
                rootAngularSpeed: 0.1f,
                pelvisVerticalSpeed: 0.05f,
                forceGroundedStunCollapse: false),
            Is.True);

        Assert.That(
            InvokeShouldUseGroundedPlainStunNoCollapseEntry(
                beingGrabbed: false,
                isGrounded: true,
                rootPlanarSpeed: 0.2f,
                rootVerticalSpeed: 0.05f,
                rootAngularSpeed: 0.1f,
                pelvisVerticalSpeed: 0.05f,
                forceGroundedStunCollapse: true),
            Is.False);

        Assert.That(
            InvokeShouldUseGroundedPlainStunNoCollapseEntry(
                beingGrabbed: true,
                isGrounded: true,
                rootPlanarSpeed: 0.2f,
                rootVerticalSpeed: 0.05f,
                rootAngularSpeed: 0.1f,
                pelvisVerticalSpeed: 0.05f,
                forceGroundedStunCollapse: false),
            Is.False);
    }

    [Test]
    public void GroundedPlainStunUpwardRootCorrection_IsOnlySuppressedForGroundedUngrabbedPlainStun()
    {
        Assert.That(
            InvokeShouldSuppressGroundedPlainStunUpwardRootCorrection(
                ResolvePhysicalPhase("Stunned"),
                hasRecentGroundContact: true,
                isRecovering: false,
                isRecoverStabilizing: false,
                beingGrabbedRefCount: 0),
            Is.True);

        Assert.That(
            InvokeShouldSuppressGroundedPlainStunUpwardRootCorrection(
                ResolvePhysicalPhase("SettledStunned"),
                hasRecentGroundContact: true,
                isRecovering: false,
                isRecoverStabilizing: false,
                beingGrabbedRefCount: 0),
            Is.True);

        Assert.That(
            InvokeShouldSuppressGroundedPlainStunUpwardRootCorrection(
                ResolvePhysicalPhase("DraggedStunned"),
                hasRecentGroundContact: true,
                isRecovering: false,
                isRecoverStabilizing: false,
                beingGrabbedRefCount: 1),
            Is.False);

        Assert.That(
            InvokeShouldSuppressGroundedPlainStunUpwardRootCorrection(
                ResolvePhysicalPhase("Stunned"),
                hasRecentGroundContact: false,
                isRecovering: false,
                isRecoverStabilizing: false,
                beingGrabbedRefCount: 0),
            Is.False);

        Assert.That(
            InvokeShouldSuppressGroundedPlainStunUpwardRootCorrection(
                ResolvePhysicalPhase("Stunned"),
                hasRecentGroundContact: true,
                isRecovering: true,
                isRecoverStabilizing: false,
                beingGrabbedRefCount: 0),
            Is.False);
    }

    [Test]
    public void StunnedGrabTransportPhase_UsesHysteresisWhenSwitchingBetweenDraggedAndCarried()
    {
        Assert.That(
            InvokeResolveStunnedGrabTransportPhase(
                ResolvePhysicalPhase("Stable"),
                beingGrabbedRefCount: 1,
                isGrounded: true,
                draggedTransitionQualified: false,
                carriedTransitionQualified: false),
            Is.EqualTo("BeingCarriedStunned"));

        Assert.That(
            InvokeResolveStunnedGrabTransportPhase(
                ResolvePhysicalPhase("DraggedStunned"),
                beingGrabbedRefCount: 1,
                isGrounded: false,
                draggedTransitionQualified: false,
                carriedTransitionQualified: false),
            Is.EqualTo("DraggedStunned"));

        Assert.That(
            InvokeResolveStunnedGrabTransportPhase(
                ResolvePhysicalPhase("DraggedStunned"),
                beingGrabbedRefCount: 1,
                isGrounded: false,
                draggedTransitionQualified: false,
                carriedTransitionQualified: true),
            Is.EqualTo("BeingCarriedStunned"));

        Assert.That(
            InvokeResolveStunnedGrabTransportPhase(
                ResolvePhysicalPhase("BeingCarriedStunned"),
                beingGrabbedRefCount: 1,
                isGrounded: true,
                draggedTransitionQualified: false,
                carriedTransitionQualified: false),
            Is.EqualTo("BeingCarriedStunned"));

        Assert.That(
            InvokeResolveStunnedGrabTransportPhase(
                ResolvePhysicalPhase("BeingCarriedStunned"),
                beingGrabbedRefCount: 1,
                isGrounded: true,
                draggedTransitionQualified: true,
                carriedTransitionQualified: false),
            Is.EqualTo("DraggedStunned"));

        Assert.That(
            InvokeResolveStunnedGrabTransportPhase(
                ResolvePhysicalPhase("Stable"),
                beingGrabbedRefCount: 2,
                isGrounded: true,
                draggedTransitionQualified: true,
                carriedTransitionQualified: false),
            Is.EqualTo("BeingCarriedStunned"));
    }

    [Test]
    public void ProxyLocalSoftFlopPresentation_OnlyEnablesForProxyPlainStunPhases()
    {
        var stunnedPresentation = ResolveStunPresentationPhase("Stunned");

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("StunnedCollapse"),
                stunnedPresentation,
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.True);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("Stunned"),
                stunnedPresentation,
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.True);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("SettledStunned"),
                stunnedPresentation,
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.True);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("DraggedStunned"),
                stunnedPresentation,
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.False);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                ResolvePhysicalPhase("BeingCarriedStunned"),
                stunnedPresentation,
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.False);
    }

    [Test]
    public void ProxyLocalSoftFlopPresentation_RequiresAnimatedVisualRigAndNonAuthorityStunnedPresentation()
    {
        var phase = ResolvePhysicalPhase("Stunned");

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                phase,
                ResolveStunPresentationPhase("Active"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.False);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                phase,
                ResolveStunPresentationPhase("RecoverStabilizing"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: false),
            Is.False);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                phase,
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: false,
                hasStateAuthority: false),
            Is.False);

        Assert.That(
            InvokeShouldUseProxyLocalSoftFlopPresentation(
                phase,
                ResolveStunPresentationPhase("Stunned"),
                usesAnimatedVisualPresentationRig: true,
                hasStateAuthority: true),
            Is.False);
    }

    [Test]
    public void AerialKickProxyPresentation_FallRemainsAirborneUntilRestoreOrLanding()
    {
        Assert.That(
            InvokeShouldEndAerialKickProxyPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("Fall"),
                predictionAge: 0.5f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldEndAerialKickProxyPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("Restoring"),
                predictionAge: 0.1f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.True);

        Assert.That(
            InvokeShouldEndAerialKickProxyPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("None"),
                predictionAge: 0.31f,
                phase: ResolvePhysicalPhase("Stable")),
            Is.True);
    }

    [Test]
    public void AerialKickProxyPresentation_EndsImmediatelyForConflictingGrabCarryPhases()
    {
        foreach (var phaseName in new[]
                 {
                     "GrabIntent",
                     "Holding",
                     "BeingGrabbed",
                     "Dragged",
                     "CarryingStunned",
                     "BeingCarriedStunned",
                     "WeaponEquipped"
                 })
        {
            Assert.That(
                InvokeShouldEndAerialKickProxyPresentation(
                    isGrounded: false,
                    aerialKickPresentationState: ResolveAerialKickPresentationState("Fall"),
                    predictionAge: 0.1f,
                    phase: ResolvePhysicalPhase(phaseName)),
                Is.True,
                phaseName);
        }
    }

}
