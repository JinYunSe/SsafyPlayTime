using System.Reflection;
using NUnit.Framework;

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
    public void ThrowableObjectState_AllowsObjectsAndStunnedCarryStates()
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
            Is.True);
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
