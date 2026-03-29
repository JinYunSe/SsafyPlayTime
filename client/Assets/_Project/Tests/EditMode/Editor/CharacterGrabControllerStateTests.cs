using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class CharacterGrabControllerStateTests
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

    private static FieldInfo ResolveField(System.Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return field;
    }

    private static (bool handled, CharacterGrabController.GrabActionState actionState) InvokeTryResolvePhaseDrivenActionState(
        object phase,
        CharacterGrabController.GrabActionState previousActionState,
        CharacterGrabController.HoldVariant previousHoldVariant)
    {
        var method = typeof(CharacterGrabController).GetMethod(
            "TryResolvePhaseDrivenActionState",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        var parameters = new object[]
        {
            phase,
            previousActionState,
            previousHoldVariant,
            CharacterGrabController.GrabActionState.Idle
        };

        var handled = (bool)method.Invoke(null, parameters);
        return (handled, (CharacterGrabController.GrabActionState)parameters[3]);
    }

    [Test]
    public void CarryPresentationState_MatchesCarryVariantsAndVictim()
    {
        Assert.That(
            CharacterGrabController.ShouldUseCarryPresentationState(
                CharacterGrabController.GrabActionState.FrontCarry,
                CharacterGrabController.HoldVariant.None),
            Is.True);
        Assert.That(
            CharacterGrabController.ShouldUseCarryPresentationState(
                CharacterGrabController.GrabActionState.OverheadCarry,
                CharacterGrabController.HoldVariant.None),
            Is.True);
        Assert.That(
            CharacterGrabController.ShouldUseCarryPresentationState(
                CharacterGrabController.GrabActionState.Idle,
                CharacterGrabController.HoldVariant.CarriedVictim),
            Is.True);
        Assert.That(
            CharacterGrabController.ShouldUseCarryPresentationState(
                CharacterGrabController.GrabActionState.HoldOneHandObject,
                CharacterGrabController.HoldVariant.Object),
            Is.False);
    }

    [Test]
    public void GrabPresentationState_StaysOnObjectGrabButNotCarry()
    {
        Assert.That(
            CharacterGrabController.ShouldUseGrabPresentationState(
                CharacterGrabController.GrabActionState.HoldOneHandObject,
                CharacterGrabController.HoldVariant.Object),
            Is.True);
        Assert.That(
            CharacterGrabController.ShouldUseGrabPresentationState(
                CharacterGrabController.GrabActionState.FrontCarry,
                CharacterGrabController.HoldVariant.FrontCarry),
            Is.False);
    }

    [Test]
    public void PreserveGrabPoseState_CoversActionCarryAndConfirmedHold()
    {
        Assert.That(
            CharacterGrabController.ShouldPreserveGrabPoseState(
                CharacterGrabController.GrabActionState.AttachPending,
                CharacterGrabController.HoldVariant.None,
                false),
            Is.True);
        Assert.That(
            CharacterGrabController.ShouldPreserveGrabPoseState(
                CharacterGrabController.GrabActionState.Idle,
                CharacterGrabController.HoldVariant.None,
                true),
            Is.True);
        Assert.That(
            CharacterGrabController.ShouldPreserveGrabPoseState(
                CharacterGrabController.GrabActionState.Idle,
                CharacterGrabController.HoldVariant.None,
                false),
            Is.False);
    }

    [Test]
    public void FacingLockState_RequiresConfirmedHoldOrCarry()
    {
        Assert.That(
            CharacterGrabController.ShouldLockFacingToHoldTargetState(
                CharacterGrabController.GrabActionState.HoldOneHandObject,
                CharacterGrabController.HoldVariant.Object,
                true),
            Is.True);
        Assert.That(
            CharacterGrabController.ShouldLockFacingToHoldTargetState(
                CharacterGrabController.GrabActionState.FrontCarry,
                CharacterGrabController.HoldVariant.FrontCarry,
                false),
            Is.True);
        Assert.That(
            CharacterGrabController.ShouldLockFacingToHoldTargetState(
                CharacterGrabController.GrabActionState.ReachLeft,
                CharacterGrabController.HoldVariant.None,
                false),
            Is.False);
    }

    [Test]
    public void ThrowableObjectState_RequiresObjectOrCarryReadyStunnedHold()
    {
        Assert.That(
            CharacterGrabController.IsThrowableObjectState(
                CharacterGrabController.GrabActionState.HoldOneHandObject,
                CharacterGrabController.HoldVariant.Object),
            Is.True);
        Assert.That(
            CharacterGrabController.IsThrowableObjectState(
                CharacterGrabController.GrabActionState.HoldOneHandStunned,
                CharacterGrabController.HoldVariant.StunnedPlayer),
            Is.False);
        Assert.That(
            CharacterGrabController.IsThrowableObjectState(
                CharacterGrabController.GrabActionState.FrontCarry,
                CharacterGrabController.HoldVariant.FrontCarry),
            Is.True);
        Assert.That(
            CharacterGrabController.IsThrowableObjectState(
                CharacterGrabController.GrabActionState.DualCarry,
                CharacterGrabController.HoldVariant.DualCarry),
            Is.True);
        Assert.That(
            CharacterGrabController.IsThrowableObjectState(
                CharacterGrabController.GrabActionState.Idle,
                CharacterGrabController.HoldVariant.CarriedVictim),
            Is.False);
    }

    [Test]
    public void RefreshNow_TreatsStunnedSingleCarryAsDirectHold()
    {
        var playerRoot = new GameObject("SingleCarryController_PlayerRoot");
        var leftHandObject = new GameObject("SingleCarryController_LeftHand");
        var rightHandObject = new GameObject("SingleCarryController_RightHand");
        var targetRoot = new GameObject("SingleCarryController_TargetRoot");
        var targetBodyObject = new GameObject("SingleCarryController_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var controller = playerRoot.AddComponent<CharacterGrabController>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);

            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            var leftJoint = leftHandObject.AddComponent<FixedJoint>();
            leftJoint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(leftHandler, leftJoint);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(leftHandler, targetPlayer);

            ResolveField(typeof(NetworkPlayer), "_localCarryMode").SetValue(
                player,
                SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry);
            ResolveField(typeof(CharacterGrabController), "networkPlayer").SetValue(controller, player);
            ResolveField(typeof(CharacterGrabController), "leftHand").SetValue(controller, leftHandler);
            ResolveField(typeof(CharacterGrabController), "rightHand").SetValue(controller, rightHandler);
            ResolveField(typeof(CharacterGrabController), "_runtimeGrabRigEnsured").SetValue(controller, true);

            controller.RefreshNow();

            Assert.That(controller.CurrentActionState, Is.EqualTo(CharacterGrabController.GrabActionState.HoldOneHandStunned));
            Assert.That(controller.CurrentHoldVariant, Is.EqualTo(CharacterGrabController.HoldVariant.StunnedPlayer));
            Assert.That(controller.GetHandMode(HandGrabHandler.HandSide.Left), Is.EqualTo(CharacterGrabController.HandHoldMode.StunnedPlayer));
            Assert.That(controller.GetHandMode(HandGrabHandler.HandSide.Right), Is.EqualTo(CharacterGrabController.HandHoldMode.None));
            Assert.That(controller.ShouldUseCarryPresentation, Is.False);
            Assert.That(controller.HasThrowableHold(), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(playerRoot);
            Object.DestroyImmediate(targetRoot);
        }
    }

    [Test]
    public void RefreshNow_KeepsDualStunnedCarryAsCarrySupport()
    {
        var playerRoot = new GameObject("DualCarryController_PlayerRoot");
        var leftHandObject = new GameObject("DualCarryController_LeftHand");
        var rightHandObject = new GameObject("DualCarryController_RightHand");
        var targetRoot = new GameObject("DualCarryController_TargetRoot");
        var targetBodyObject = new GameObject("DualCarryController_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var controller = playerRoot.AddComponent<CharacterGrabController>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);

            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            var leftJoint = leftHandObject.AddComponent<FixedJoint>();
            var rightJoint = rightHandObject.AddComponent<FixedJoint>();
            leftJoint.connectedBody = targetBody;
            rightJoint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(leftHandler, leftJoint);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(rightHandler, rightJoint);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(leftHandler, targetPlayer);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(rightHandler, targetPlayer);

            ResolveField(typeof(NetworkPlayer), "_localCarryMode").SetValue(
                player,
                SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedDualCarry);
            ResolveField(typeof(CharacterGrabController), "networkPlayer").SetValue(controller, player);
            ResolveField(typeof(CharacterGrabController), "leftHand").SetValue(controller, leftHandler);
            ResolveField(typeof(CharacterGrabController), "rightHand").SetValue(controller, rightHandler);
            ResolveField(typeof(CharacterGrabController), "_runtimeGrabRigEnsured").SetValue(controller, true);

            controller.RefreshNow();

            Assert.That(controller.CurrentActionState, Is.EqualTo(CharacterGrabController.GrabActionState.DualCarry));
            Assert.That(controller.CurrentHoldVariant, Is.EqualTo(CharacterGrabController.HoldVariant.DualCarry));
            Assert.That(controller.GetHandMode(HandGrabHandler.HandSide.Left), Is.EqualTo(CharacterGrabController.HandHoldMode.CarrySupport));
            Assert.That(controller.GetHandMode(HandGrabHandler.HandSide.Right), Is.EqualTo(CharacterGrabController.HandHoldMode.CarrySupport));
            Assert.That(controller.ShouldUseCarryPresentation, Is.True);
            Assert.That(controller.HasThrowableHold(), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(playerRoot);
            Object.DestroyImmediate(targetRoot);
        }
    }

    [Test]
    public void ResolveAttachTarget_UsesStableRootBodyForConsciousPlayer()
    {
        var holderRoot = new GameObject("ConsciousAttach_ControllerRoot");
        var targetRoot = new GameObject("ConsciousAttach_TargetRoot");
        var targetLimb = new GameObject("ConsciousAttach_TargetLimb");
        try
        {
            var controller = holderRoot.AddComponent<CharacterGrabController>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            var stableBody = targetRoot.AddComponent<Rigidbody>();
            targetLimb.transform.SetParent(targetRoot.transform, false);
            var limbBody = targetLimb.AddComponent<Rigidbody>();

            controller.ResolveAttachTarget(
                limbBody,
                limbBody.worldCenterOfMass,
                null,
                SSAFYPlayTime.Character.GrabDriveProfile.GrabTargetType.Player,
                targetPlayer,
                out var jointTargetBody,
                out var jointAnchorWorld);

            Assert.That(jointTargetBody, Is.SameAs(stableBody));
            Assert.That(jointAnchorWorld, Is.EqualTo(stableBody.worldCenterOfMass));
        }
        finally
        {
            Object.DestroyImmediate(holderRoot);
            Object.DestroyImmediate(targetRoot);
        }
    }

    [Test]
    public void PhaseDrivenActionState_MapsDraggedStunnedToStruggle()
    {
        var result = InvokeTryResolvePhaseDrivenActionState(
            ResolvePhysicalPhase("DraggedStunned"),
            CharacterGrabController.GrabActionState.Idle,
            CharacterGrabController.HoldVariant.None);

        Assert.That(result.handled, Is.True);
        Assert.That(result.actionState, Is.EqualTo(CharacterGrabController.GrabActionState.Struggle));
    }

    [Test]
    public void PhaseDrivenActionState_MapsGrabVictimStunStatesToStruggle()
    {
        var grabbed = InvokeTryResolvePhaseDrivenActionState(
            ResolvePhysicalPhase("BeingGrabbed"),
            CharacterGrabController.GrabActionState.Idle,
            CharacterGrabController.HoldVariant.None);

        var dragged = InvokeTryResolvePhaseDrivenActionState(
            ResolvePhysicalPhase("Dragged"),
            CharacterGrabController.GrabActionState.Idle,
            CharacterGrabController.HoldVariant.None);

        var carriedStunned = InvokeTryResolvePhaseDrivenActionState(
            ResolvePhysicalPhase("BeingCarriedStunned"),
            CharacterGrabController.GrabActionState.Idle,
            CharacterGrabController.HoldVariant.None);

        Assert.That(grabbed.handled, Is.True);
        Assert.That(grabbed.actionState, Is.EqualTo(CharacterGrabController.GrabActionState.Struggle));
        Assert.That(dragged.handled, Is.True);
        Assert.That(dragged.actionState, Is.EqualTo(CharacterGrabController.GrabActionState.Struggle));
        Assert.That(carriedStunned.handled, Is.True);
        Assert.That(carriedStunned.actionState, Is.EqualTo(CharacterGrabController.GrabActionState.Struggle));
    }

    [Test]
    public void PhaseDrivenActionState_OnlyUsesCarryRecoveryWhenCarryContextStillExists()
    {
        var neutralRecovering = InvokeTryResolvePhaseDrivenActionState(
            ResolvePhysicalPhase("Recovering"),
            CharacterGrabController.GrabActionState.Idle,
            CharacterGrabController.HoldVariant.None);

        var carryRecovering = InvokeTryResolvePhaseDrivenActionState(
            ResolvePhysicalPhase("Recovering"),
            CharacterGrabController.GrabActionState.FrontCarry,
            CharacterGrabController.HoldVariant.CarriedVictim);

        Assert.That(neutralRecovering.handled, Is.False);
        Assert.That(neutralRecovering.actionState, Is.EqualTo(CharacterGrabController.GrabActionState.Idle));
        Assert.That(carryRecovering.handled, Is.True);
        Assert.That(carryRecovering.actionState, Is.EqualTo(CharacterGrabController.GrabActionState.RecoverFromCarry));
    }

    [Test]
    public void PhaseDrivenActionState_KeepsCarryRecoveryTransition()
    {
        var result = InvokeTryResolvePhaseDrivenActionState(
            ResolvePhysicalPhase("Recovering"),
            CharacterGrabController.GrabActionState.FrontCarry,
            CharacterGrabController.HoldVariant.CarriedVictim);

        Assert.That(result.handled, Is.True);
        Assert.That(result.actionState, Is.EqualTo(CharacterGrabController.GrabActionState.RecoverFromCarry));
    }
}
