using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class NetworkPlayerHitFeedbackTests
{
    private static Type ResolvePhysicalPhaseType()
    {
        var type = typeof(NetworkPlayer).GetNestedType("PhysicalPhase", BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return type;
    }

    private static object ResolvePhysicalPhase(string name)
    {
        return System.Enum.Parse(ResolvePhysicalPhaseType(), name);
    }

    private static MethodInfo ResolveStaticMethod(string methodName)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static FieldInfo ResolveField(string fieldName)
    {
        var field = typeof(NetworkPlayer).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return field;
    }

    private static float InvokeNormalizePunchImpact(float punchForce)
    {
        return (float)ResolveStaticMethod("NormalizePunchImpact").Invoke(null, new object[] { punchForce });
    }

    private static bool InvokeIsDownedCloseCombatPhase(object phase)
    {
        return (bool)ResolveStaticMethod("IsDownedCloseCombatPhase").Invoke(null, new[] { phase });
    }

    private static bool InvokeShouldSuppressDownedRepeatHitLaunch(
        NetworkPlayer victim,
        bool wasAlreadyStunnedBeforeHit,
        object phaseAfterHit)
    {
        return (bool)ResolveStaticMethod("ShouldSuppressDownedRepeatHitLaunch").Invoke(
            null,
            new object[] { victim, wasAlreadyStunnedBeforeHit, phaseAfterHit });
    }

    private static bool InvokeShouldSuppressDuplicateHitImpactFeedback(
        float elapsedTime,
        float distanceSqr,
        float directionDot,
        float forceRatio)
    {
        return (bool)ResolveStaticMethod("ShouldSuppressDuplicateHitImpactFeedback").Invoke(
            null,
            new object[] { elapsedTime, distanceSqr, directionDot, forceRatio });
    }

    [Test]
    public void NormalizePunchImpact_ClampsAndInterpolatesTheFeedbackRange()
    {
        Assert.That(InvokeNormalizePunchImpact(4f), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(InvokeNormalizePunchImpact(8f), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(InvokeNormalizePunchImpact(13f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(InvokeNormalizePunchImpact(18f), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(InvokeNormalizePunchImpact(25f), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void IsDownedCloseCombatPhase_OnlyMatchesTheThreeDownedStunVariants()
    {
        Assert.That(InvokeIsDownedCloseCombatPhase(ResolvePhysicalPhase("Stunned")), Is.True);
        Assert.That(InvokeIsDownedCloseCombatPhase(ResolvePhysicalPhase("StunnedCollapse")), Is.True);
        Assert.That(InvokeIsDownedCloseCombatPhase(ResolvePhysicalPhase("SettledStunned")), Is.True);

        Assert.That(InvokeIsDownedCloseCombatPhase(ResolvePhysicalPhase("Stable")), Is.False);
        Assert.That(InvokeIsDownedCloseCombatPhase(ResolvePhysicalPhase("Recovering")), Is.False);
        Assert.That(InvokeIsDownedCloseCombatPhase(ResolvePhysicalPhase("DraggedStunned")), Is.False);
    }

    [Test]
    public void ShouldSuppressDownedRepeatHitLaunch_OnlySuppressesAlreadyDownedVictimsWithoutCarryContext()
    {
        var go = new GameObject("NetworkPlayerHitFeedbackTests_Victim");
        try
        {
            var victim = go.AddComponent<NetworkPlayer>();
            ResolveField("_isActiveRagdoll").SetValue(victim, false);
            ResolveField("_beingGrabbedRefCount").SetValue(victim, 0);

            Assert.That(
                InvokeShouldSuppressDownedRepeatHitLaunch(
                    victim,
                    wasAlreadyStunnedBeforeHit: true,
                    phaseAfterHit: ResolvePhysicalPhase("Stunned")),
                Is.True);

            Assert.That(
                InvokeShouldSuppressDownedRepeatHitLaunch(
                    victim,
                    wasAlreadyStunnedBeforeHit: false,
                    phaseAfterHit: ResolvePhysicalPhase("Stunned")),
                Is.False);

            Assert.That(
                InvokeShouldSuppressDownedRepeatHitLaunch(
                    victim,
                    wasAlreadyStunnedBeforeHit: true,
                    phaseAfterHit: ResolvePhysicalPhase("DraggedStunned")),
                Is.False);

            ResolveField("_beingGrabbedRefCount").SetValue(victim, 1);
            Assert.That(
                InvokeShouldSuppressDownedRepeatHitLaunch(
                    victim,
                    wasAlreadyStunnedBeforeHit: true,
                    phaseAfterHit: ResolvePhysicalPhase("StunnedCollapse")),
                Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ShouldSuppressDuplicateHitImpactFeedback_OnlySuppressesNearIdenticalEventsInsideTheShortWindow()
    {
        Assert.That(
            InvokeShouldSuppressDuplicateHitImpactFeedback(
                elapsedTime: 0.05f,
                distanceSqr: 0.25f,
                directionDot: 0.9f,
                forceRatio: 1.1f),
            Is.True);

        Assert.That(
            InvokeShouldSuppressDuplicateHitImpactFeedback(
                elapsedTime: 0.25f,
                distanceSqr: 0.25f,
                directionDot: 0.9f,
                forceRatio: 1.1f),
            Is.False);

        Assert.That(
            InvokeShouldSuppressDuplicateHitImpactFeedback(
                elapsedTime: 0.05f,
                distanceSqr: 4f,
                directionDot: 0.9f,
                forceRatio: 1.1f),
            Is.False);

        Assert.That(
            InvokeShouldSuppressDuplicateHitImpactFeedback(
                elapsedTime: 0.05f,
                distanceSqr: 0.25f,
                directionDot: 0.1f,
                forceRatio: 1.1f),
            Is.False);

        Assert.That(
            InvokeShouldSuppressDuplicateHitImpactFeedback(
                elapsedTime: 0.05f,
                distanceSqr: 0.25f,
                directionDot: 0.9f,
                forceRatio: 3f),
            Is.False);
    }
}
