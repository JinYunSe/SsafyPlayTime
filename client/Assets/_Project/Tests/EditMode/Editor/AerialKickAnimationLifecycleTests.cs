using System;
using System.Reflection;
using NUnit.Framework;

public sealed class AerialKickAnimationLifecycleTests
{
    private static Type ResolvePhysicalPhaseType()
    {
        var type = typeof(NetworkPlayer).GetNestedType("PhysicalPhase", BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return type;
    }

    private static object ResolvePhysicalPhase(string name)
    {
        return Enum.Parse(ResolvePhysicalPhaseType(), name);
    }

    private static Type ResolveAerialKickPresentationStateType()
    {
        var type = typeof(NetworkPlayer).GetNestedType("AerialKickPresentationState", BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return type;
    }

    private static object ResolveAerialKickPresentationState(string name)
    {
        return Enum.Parse(ResolveAerialKickPresentationStateType(), name);
    }

    private static bool InvokeShouldEndAerialKickLocalPresentation(
        bool isGrounded,
        object aerialKickPresentationState,
        object phase)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldEndAerialKickLocalPresentation",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                typeof(bool),
                ResolveAerialKickPresentationStateType(),
                ResolvePhysicalPhaseType()
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(null, new[] { (object)isGrounded, aerialKickPresentationState, phase });
    }

    private static bool InvokeShouldEndAerialKickProxyPresentation(
        bool isGrounded,
        object aerialKickPresentationState,
        float predictionAge,
        object phase)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldEndAerialKickProxyPresentation",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                typeof(bool),
                ResolveAerialKickPresentationStateType(),
                typeof(float),
                ResolvePhysicalPhaseType()
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(
            null,
            new object[] { isGrounded, aerialKickPresentationState, predictionAge, phase });
    }

    private static bool InvokeShouldContinueAerialKickPoseHold(
        float currentTime,
        float minHoldUntilTime,
        float maxHoldUntilTime,
        bool shouldEndPresentation)
    {
        var method = typeof(PartyMonsterAnimationDriver).GetMethod(
            "ShouldContinueAerialKickPoseHold",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
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
                currentTime,
                minHoldUntilTime,
                maxHoldUntilTime,
                shouldEndPresentation
            });
    }

    private static bool InvokeHasAerialKickAnimationReachedClipEnd(double clipTime, double clipLength)
    {
        var method = typeof(PartyMonsterAnimationDriver).GetMethod(
            "HasAerialKickAnimationReachedClipEnd",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[]
            {
                typeof(double),
                typeof(double)
            },
            null);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(null, new object[] { clipTime, clipLength });
    }

    [Test]
    public void LocalAerialKickPresentation_EndsForRestoreNoneAndConflictingPhases()
    {
        Assert.That(
            InvokeShouldEndAerialKickLocalPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("Restoring"),
                phase: ResolvePhysicalPhase("Stable")),
            Is.True);

        Assert.That(
            InvokeShouldEndAerialKickLocalPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("None"),
                phase: ResolvePhysicalPhase("Stable")),
            Is.True);

        Assert.That(
            InvokeShouldEndAerialKickLocalPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("Fall"),
                phase: ResolvePhysicalPhase("Stunned")),
            Is.True);

        Assert.That(
            InvokeShouldEndAerialKickLocalPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("Launch"),
                phase: ResolvePhysicalPhase("Holding")),
            Is.True);
    }

    [Test]
    public void LocalAerialKickPresentation_RemainsActiveDuringAirborneLaunchAndFall()
    {
        Assert.That(
            InvokeShouldEndAerialKickLocalPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("Launch"),
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);

        Assert.That(
            InvokeShouldEndAerialKickLocalPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("Fall"),
                phase: ResolvePhysicalPhase("Stable")),
            Is.False);
    }

    [Test]
    public void ProxyAerialKickPresentation_NoneStillEndsImmediatelyForConflictingPhases()
    {
        Assert.That(
            InvokeShouldEndAerialKickProxyPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("None"),
                predictionAge: 0.05f,
                phase: ResolvePhysicalPhase("Stunned")),
            Is.True);

        Assert.That(
            InvokeShouldEndAerialKickProxyPresentation(
                isGrounded: false,
                aerialKickPresentationState: ResolveAerialKickPresentationState("None"),
                predictionAge: 0.05f,
                phase: ResolvePhysicalPhase("BeingGrabbed")),
            Is.True);
    }

    [Test]
    public void AerialKickPoseHold_UsesMinHoldThenStopsOnEndSignalOrMaxHold()
    {
        Assert.That(
            InvokeShouldContinueAerialKickPoseHold(
                currentTime: 1.05f,
                minHoldUntilTime: 1.10f,
                maxHoldUntilTime: 2.00f,
                shouldEndPresentation: true),
            Is.True);

        Assert.That(
            InvokeShouldContinueAerialKickPoseHold(
                currentTime: 1.20f,
                minHoldUntilTime: 1.10f,
                maxHoldUntilTime: 2.00f,
                shouldEndPresentation: true),
            Is.False);

        Assert.That(
            InvokeShouldContinueAerialKickPoseHold(
                currentTime: 2.10f,
                minHoldUntilTime: 1.10f,
                maxHoldUntilTime: 2.00f,
                shouldEndPresentation: false),
            Is.False);
    }

    [Test]
    public void AerialKickClipEnd_DetectsLastFrameWithGraceWindow()
    {
        Assert.That(
            InvokeHasAerialKickAnimationReachedClipEnd(clipTime: 0.49d, clipLength: 0.50d),
            Is.False);

        Assert.That(
            InvokeHasAerialKickAnimationReachedClipEnd(clipTime: 0.492d, clipLength: 0.50d),
            Is.True);

        Assert.That(
            InvokeHasAerialKickAnimationReachedClipEnd(clipTime: 0.0d, clipLength: 0.0d),
            Is.True);
    }
}
