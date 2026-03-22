using NUnit.Framework;

public sealed class CharacterGrabControllerStateTests
{
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
    public void ThrowableObjectState_RejectsStunnedAndCarryStates()
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
            Is.False);
    }
}
