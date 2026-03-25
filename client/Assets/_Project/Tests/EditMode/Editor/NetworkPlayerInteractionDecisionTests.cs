using System.Reflection;
using NUnit.Framework;
using SSAFYPlayTime.Character;

public sealed class NetworkPlayerInteractionDecisionTests
{
    private static bool InvokeShouldAllowKickFallback(bool anyHolding, bool hasHeldRuntimeItem)
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ShouldAllowKickFallback",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(null, new object[] { anyHolding, hasHeldRuntimeItem });
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
}
