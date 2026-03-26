using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class NetworkPlayerThrowGrabCooldownTests
{
    private sealed class GrabFilterTestContext
    {
        public GameObject PlayerRoot;
        public NetworkPlayer Player;
        public GameObject HandObject;
        public HandGrabHandler Handler;
    }

    private static MethodInfo ResolveBeginGrabDisableWindow()
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "BeginGrabDisableWindow",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveShouldBlockCollisionCarry()
    {
        var method = typeof(HandGrabHandler).GetMethod(
            "ShouldBlockCollisionCarry",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveRegisterThrownTargetRegrabIgnore()
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "RegisterThrownTargetRegrabIgnore",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveShouldIgnoreGrabTarget()
    {
        var method = typeof(HandGrabHandler).GetMethod(
            "ShouldIgnoreGrabTarget",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static FieldInfo ResolveField(System.Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return field;
    }

    private static GrabFilterTestContext CreateGrabFilterContext(string name)
    {
        var playerRoot = new GameObject(name + "_PlayerRoot");
        var player = playerRoot.AddComponent<NetworkPlayer>();

        var handObject = new GameObject(name + "_Hand");
        handObject.transform.SetParent(playerRoot.transform);
        var handBody = handObject.AddComponent<Rigidbody>();
        var handler = handObject.AddComponent<HandGrabHandler>();

        ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(handler, player);
        ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(handler, handBody);

        return new GrabFilterTestContext
        {
            PlayerRoot = playerRoot,
            Player = player,
            HandObject = handObject,
            Handler = handler
        };
    }

    private static Rigidbody CreateTargetBody(string name, bool addNetworkPlayer = false)
    {
        var targetRoot = new GameObject(name + "_Root");
        if (addNetworkPlayer)
            targetRoot.AddComponent<NetworkPlayer>();

        var targetBodyObject = new GameObject(name + "_Body");
        targetBodyObject.transform.SetParent(targetRoot.transform);
        return targetBodyObject.AddComponent<Rigidbody>();
    }

    private static void DestroyIfNotNull(Object target)
    {
        if (target != null)
            Object.DestroyImmediate(target);
    }

    [Test]
    public void BeginGrabDisableWindow_ClearsGrabFlagsImmediately()
    {
        var go = new GameObject("NetworkPlayerThrowGrabCooldownTests_Player");
        try
        {
            var player = go.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_isGrabActive").SetValue(player, true);

            ResolveBeginGrabDisableWindow().Invoke(player, new object[] { 0.5f });

            Assert.That(player.IsGrabActive, Is.False);
            Assert.That(player.IsHandGrabActive(HandGrabHandler.HandSide.Left), Is.False);
            Assert.That(player.IsHandGrabActive(HandGrabHandler.HandSide.Right), Is.False);

            var disabledUntil = (float)ResolveField(typeof(NetworkPlayer), "_grabDisabledUntilTime").GetValue(player);
            Assert.That(disabledUntil, Is.GreaterThan(Time.time));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CollisionCarry_IsBlockedWhileGrabTemporarilyDisabled()
    {
        var go = new GameObject("NetworkPlayerThrowGrabCooldownTests_Player");
        try
        {
            var player = go.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_grabDisabledUntilTime").SetValue(player, Time.time + 0.5f);

            var blocked = (bool)ResolveShouldBlockCollisionCarry().Invoke(
                null,
                new object[] { player, HandGrabHandler.HandSide.Left, false });

            Assert.That(blocked, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ShouldIgnoreGrabTarget_IgnoresRecentlyThrownSameTargetRootForSameThrower()
    {
        var context = CreateGrabFilterContext("IgnoreSameTarget");
        var targetBody = CreateTargetBody("IgnoreSameTarget", addNetworkPlayer: true);
        try
        {
            ResolveRegisterThrownTargetRegrabIgnore().Invoke(
                context.Player,
                new object[] { targetBody.transform.root, 0.25f });

            var ignored = (bool)ResolveShouldIgnoreGrabTarget().Invoke(
                context.Handler,
                new object[] { targetBody });

            Assert.That(ignored, Is.True);
        }
        finally
        {
            DestroyIfNotNull(targetBody.transform.root.gameObject);
            DestroyIfNotNull(context.PlayerRoot);
        }
    }

    [Test]
    public void ShouldIgnoreGrabTarget_DoesNotIgnoreDifferentTargetRootForSameThrower()
    {
        var context = CreateGrabFilterContext("IgnoreDifferentTarget");
        var registeredTargetBody = CreateTargetBody("RegisteredTarget", addNetworkPlayer: true);
        var otherTargetBody = CreateTargetBody("OtherTarget", addNetworkPlayer: true);
        try
        {
            ResolveRegisterThrownTargetRegrabIgnore().Invoke(
                context.Player,
                new object[] { registeredTargetBody.transform.root, 0.25f });

            var ignored = (bool)ResolveShouldIgnoreGrabTarget().Invoke(
                context.Handler,
                new object[] { otherTargetBody });

            Assert.That(ignored, Is.False);
        }
        finally
        {
            DestroyIfNotNull(otherTargetBody.transform.root.gameObject);
            DestroyIfNotNull(registeredTargetBody.transform.root.gameObject);
            DestroyIfNotNull(context.PlayerRoot);
        }
    }

    [Test]
    public void ShouldIgnoreGrabTarget_DoesNotIgnoreSameTargetRootForDifferentThrower()
    {
        var throwerContext = CreateGrabFilterContext("Thrower");
        var otherContext = CreateGrabFilterContext("OtherThrower");
        var targetBody = CreateTargetBody("SharedTarget", addNetworkPlayer: true);
        try
        {
            ResolveRegisterThrownTargetRegrabIgnore().Invoke(
                throwerContext.Player,
                new object[] { targetBody.transform.root, 0.25f });

            var ignored = (bool)ResolveShouldIgnoreGrabTarget().Invoke(
                otherContext.Handler,
                new object[] { targetBody });

            Assert.That(ignored, Is.False);
        }
        finally
        {
            DestroyIfNotNull(targetBody.transform.root.gameObject);
            DestroyIfNotNull(otherContext.PlayerRoot);
            DestroyIfNotNull(throwerContext.PlayerRoot);
        }
    }

    [Test]
    public void ShouldIgnoreGrabTarget_DoesNotIgnoreNonCharacterThrownTarget()
    {
        var context = CreateGrabFilterContext("IgnorePropTarget");
        var propBody = CreateTargetBody("PropTarget");
        try
        {
            ResolveRegisterThrownTargetRegrabIgnore().Invoke(
                context.Player,
                new object[] { propBody.transform.root, 0.25f });

            var ignored = (bool)ResolveShouldIgnoreGrabTarget().Invoke(
                context.Handler,
                new object[] { propBody });

            Assert.That(ignored, Is.False);
        }
        finally
        {
            DestroyIfNotNull(propBody.transform.root.gameObject);
            DestroyIfNotNull(context.PlayerRoot);
        }
    }
}
