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

    private static MethodInfo ResolveTryProcessThrow()
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "TryProcessThrow",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveEvaluateStunnedThrowForceScale()
    {
        var method = typeof(HandGrabHandler).GetMethod(
            "EvaluateStunnedThrowForceScale",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveTryBuildThrowImpulse()
    {
        var method = typeof(HandGrabHandler).GetMethod(
            "TryBuildThrowImpulse",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveProxyCarriedVictimExitAnchor()
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "ResolveProxyCarriedVictimExitAnchor",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveTrySeedProxyCarriedVictimExitPresentation()
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "TrySeedProxyCarriedVictimExitPresentation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveAttachGrab()
    {
        var method = typeof(HandGrabHandler).GetMethod(
            "AttachGrab",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveCanStartStunnedDualReach()
    {
        var method = typeof(HandGrabHandler).GetMethod(
            "CanStartStunnedDualReach",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveCanAttachStunnedDualGrab()
    {
        var method = typeof(HandGrabHandler).GetMethod(
            "CanAttachStunnedDualGrab",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveRotateTowardInput()
    {
        var method = typeof(NetworkPlayer).GetMethod(
            "RotateTowardInput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static MethodInfo ResolveShouldSuppressTorsoFacingAssist()
    {
        var method = typeof(ProceduralGrabArm).GetMethod(
            "ShouldSuppressTorsoFacingAssist",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    private static object ResolvePhysicalPhase(string name)
    {
        var type = typeof(NetworkPlayer).GetNestedType("PhysicalPhase", BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return System.Enum.Parse(type, name);
    }

    private static FieldInfo ResolveField(System.Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return field;
    }

    private static Vector3 ResolveThrowImpulse(object throwData)
    {
        var impulseProperty = throwData.GetType().GetProperty(
            "Impulse",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(impulseProperty, Is.Not.Null);
        return (Vector3)impulseProperty.GetValue(throwData);
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

    [Test]
    public void EvaluateStunnedThrowForceScale_DropsToZeroWithoutMomentum()
    {
        var evaluate = ResolveEvaluateStunnedThrowForceScale();

        var stationary = (float)evaluate.Invoke(null, new object[] { 0f });
        var slow = (float)evaluate.Invoke(null, new object[] { 1f });
        var walking = (float)evaluate.Invoke(null, new object[] { 3.71f });
        var fast = (float)evaluate.Invoke(null, new object[] { 6.1f });

        Assert.That(stationary, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(slow, Is.LessThan(0.02f));
        Assert.That(walking, Is.LessThan(0.35f));
        Assert.That(fast, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void CanBeginDualHandStunnedReach_RequiresOtherHandGrabIntentForStunnedTarget()
    {
        var playerRoot = new GameObject("DualHandReachGate_PlayerRoot");
        var leftHandObject = new GameObject("DualHandReachGate_LeftHand");
        var rightHandObject = new GameObject("DualHandReachGate_RightHand");
        var targetRoot = new GameObject("DualHandReachGate_TargetRoot");
        var targetBodyObject = new GameObject("DualHandReachGate_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);

            var leftBody = leftHandObject.AddComponent<Rigidbody>();
            var rightBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);

            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);

            var blocked = (bool)ResolveCanStartStunnedDualReach().Invoke(leftHandler, new object[] { targetBody, targetPlayer });
            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, true);
            var allowed = (bool)ResolveCanStartStunnedDualReach().Invoke(leftHandler, new object[] { targetBody, targetPlayer });

            Assert.That(blocked, Is.False);
            Assert.That(allowed, Is.True);
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void TryGrab_DoesNotQueueStunnedReachWhenOnlyOneHandIsActive()
    {
        var playerRoot = new GameObject("SingleHandTryGrabGate_PlayerRoot");
        var leftHandObject = new GameObject("SingleHandTryGrabGate_LeftHand");
        var rightHandObject = new GameObject("SingleHandTryGrabGate_RightHand");
        var targetRoot = new GameObject("SingleHandTryGrabGate_TargetRoot");
        var targetBodyObject = new GameObject("SingleHandTryGrabGate_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);

            leftHandObject.transform.position = Vector3.zero;
            rightHandObject.transform.position = Vector3.zero;
            targetBodyObject.transform.position = new Vector3(0.2f, 0f, 0f);

            var leftBody = leftHandObject.AddComponent<Rigidbody>();
            var rightBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            targetBodyObject.AddComponent<SphereCollider>().radius = 0.35f;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });

            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);
            Physics.SyncTransforms();
            leftHandler.TryGrab();

            Assert.That(leftHandler.PendingReachTarget, Is.Null);
            Assert.That((bool)ResolveCanStartStunnedDualReach().Invoke(leftHandler, new object[] { targetBody, targetPlayer }), Is.False);

            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, true);
            leftHandler.TryGrab();

            Assert.That(leftHandler.PendingReachTarget, Is.SameAs(targetBody));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void AttachGrab_BlocksSingleHandStunnedTargetWithoutOtherHandTargetingSameRoot()
    {
        var playerRoot = new GameObject("SingleHandAttachGate_PlayerRoot");
        var leftHandObject = new GameObject("SingleHandAttachGate_LeftHand");
        var rightHandObject = new GameObject("SingleHandAttachGate_RightHand");
        var targetRoot = new GameObject("SingleHandAttachGate_TargetRoot");
        var targetBodyObject = new GameObject("SingleHandAttachGate_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);

            var leftBody = leftHandObject.AddComponent<Rigidbody>();
            var rightBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);

            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);

            ResolveAttachGrab().Invoke(leftHandler, new object[] { targetBody, targetBody.position });

            Assert.That(leftHandler.IsHolding, Is.False);
            Assert.That(leftHandler.IsHoldingStunnedPlayer, Is.False);
            Assert.That(leftHandler.GrabTargetRoot, Is.Null);
            Assert.That((bool)ResolveCanAttachStunnedDualGrab().Invoke(leftHandler, new object[] { targetBody, targetPlayer }), Is.False);
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void AttachGrab_AllowsStunnedTargetWhenOtherHandTargetsSameRoot()
    {
        var playerRoot = new GameObject("DualHandAttachGate_PlayerRoot");
        var leftHandObject = new GameObject("DualHandAttachGate_LeftHand");
        var rightHandObject = new GameObject("DualHandAttachGate_RightHand");
        var targetRoot = new GameObject("DualHandAttachGate_TargetRoot");
        var targetBodyObject = new GameObject("DualHandAttachGate_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);

            var leftBody = leftHandObject.AddComponent<Rigidbody>();
            var rightBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_pendingReachTarget").SetValue(rightHandler, targetBody);
            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, true);

            ResolveAttachGrab().Invoke(leftHandler, new object[] { targetBody, targetBody.position });

            Assert.That(leftHandler.IsHolding, Is.True);
            Assert.That(leftHandler.IsHoldingStunnedPlayer, Is.True);
            Assert.That((bool)ResolveCanAttachStunnedDualGrab().Invoke(rightHandler, new object[] { targetBody, targetPlayer }), Is.True);
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void UpdateState_ReleasesStunnedGrab_WhenDualHandCoordinationIsLost()
    {
        var playerRoot = new GameObject("DualHandHoldGate_PlayerRoot");
        var leftHandObject = new GameObject("DualHandHoldGate_LeftHand");
        var rightHandObject = new GameObject("DualHandHoldGate_RightHand");
        var targetRoot = new GameObject("DualHandHoldGate_TargetRoot");
        var targetBodyObject = new GameObject("DualHandHoldGate_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);

            var leftBody = leftHandObject.AddComponent<Rigidbody>();
            var rightBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_pendingReachTarget").SetValue(rightHandler, targetBody);
            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, true);

            ResolveAttachGrab().Invoke(leftHandler, new object[] { targetBody, targetBody.position });
            Assert.That(leftHandler.IsHoldingStunnedPlayer, Is.True);

            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, false);
            leftHandler.UpdateState();

            Assert.That(leftHandler.IsHolding, Is.False);
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void TryBuildThrowImpulse_UsesCharacterFacingInsteadOfVisualYaw()
    {
        var playerRoot = new GameObject("ThrowVisualYaw_PlayerRoot");
        var handObject = new GameObject("ThrowVisualYaw_Hand");
        var targetRoot = new GameObject("ThrowVisualYaw_TargetRoot");
        var targetBodyObject = new GameObject("ThrowVisualYaw_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            playerRoot.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            ResolveField(typeof(NetworkPlayer), "_localVisualYaw").SetValue(player, 0f);

            handObject.transform.SetParent(playerRoot.transform);
            var handBody = handObject.AddComponent<Rigidbody>();
            var handler = handObject.AddComponent<HandGrabHandler>();

            targetBodyObject.transform.SetParent(targetRoot.transform);
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            var joint = handObject.AddComponent<FixedJoint>();
            joint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(handler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(handler, handBody);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(handler, joint);

            var args = new object[] { null };
            var built = (bool)ResolveTryBuildThrowImpulse().Invoke(handler, args);

            Assert.That(built, Is.True);
            Assert.That(args[0], Is.Not.Null);

            var impulse = ResolveThrowImpulse(args[0]);
            var planarImpulse = Vector3.ProjectOnPlane(impulse, Vector3.up).normalized;

            Assert.That(Vector3.Dot(planarImpulse, playerRoot.transform.forward), Is.GreaterThan(0.95f));
            Assert.That(Vector3.Dot(planarImpulse, Vector3.forward), Is.LessThan(-0.95f));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void ResolveProxyCarriedVictimExitAnchor_PrefersFreshNetworkedCarryRootOverStaleCarrySnapshots()
    {
        var latestHips = new Vector3(1f, 2f, 3f);
        var lastCarryRoot = new Vector3(6f, 3f, -2f);
        var cachedCarryRoot = new Vector3(-4f, 2.5f, 9f);
        var networkedCarryRoot = new Vector3(8f, 1.5f, 4f);
        var networkedAnchor = new Vector3(-3f, 1f, -5f);
        var networkedRootOffset = new Vector3(0.5f, 0f, 0.25f);

        var resolved = (Vector3)ResolveProxyCarriedVictimExitAnchor().Invoke(
            null,
            new object[]
            {
                latestHips,
                lastCarryRoot,
                true,
                cachedCarryRoot,
                true,
                networkedCarryRoot,
                true,
                networkedAnchor,
                true,
                networkedRootOffset
            });

        Assert.That(resolved, Is.EqualTo(networkedCarryRoot));
    }

    [Test]
    public void ResolveProxyCarriedVictimExitAnchor_FallsBackToAnchorOffsetWhenCarryRootsAreMissing()
    {
        var latestHips = new Vector3(1f, 2f, 3f);
        var networkedAnchor = new Vector3(-3f, 1f, -5f);
        var networkedRootOffset = new Vector3(0.5f, 0f, 0.25f);

        var resolved = (Vector3)ResolveProxyCarriedVictimExitAnchor().Invoke(
            null,
            new object[]
            {
                latestHips,
                Vector3.zero,
                false,
                Vector3.zero,
                false,
                Vector3.zero,
                true,
                networkedAnchor,
                true,
                networkedRootOffset
            });

        Assert.That(resolved, Is.EqualTo(networkedAnchor + networkedRootOffset));
    }

    [Test]
    public void TrySeedProxyCarriedVictimExitPresentation_SeedsShortReleaseSettleFromLastCarryRoot()
    {
        var playerRoot = new GameObject("CarryExitSeed_PlayerRoot");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var latestHips = new Vector3(0.5f, 2f, -1f);
            var carryExitAnchor = new Vector3(3.5f, 2.2f, 4f);

            ResolveField(typeof(NetworkPlayer), "_lastObservedCarryMode").SetValue(
                player,
                SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim);
            ResolveField(typeof(NetworkPlayer), "_lastCarryAnchorPosition").SetValue(player, carryExitAnchor);

            var seeded = (bool)ResolveTrySeedProxyCarriedVictimExitPresentation().Invoke(
                player,
                new object[] { latestHips });

            Assert.That(seeded, Is.True);
            Assert.That(
                (Vector3)ResolveField(typeof(NetworkPlayer), "_carryExitSnapshotAnchor").GetValue(player),
                Is.EqualTo(carryExitAnchor));
            Assert.That(
                (Vector3)ResolveField(typeof(NetworkPlayer), "_hipsSnapshotTo").GetValue(player),
                Is.EqualTo(carryExitAnchor));

            var settleRemaining = (float)ResolveField(typeof(NetworkPlayer), "_carryReleaseSettleRemaining").GetValue(player);
            Assert.That(settleRemaining, Is.GreaterThan(0f));
            Assert.That(settleRemaining, Is.LessThanOrEqualTo(0.181f));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
        }
    }

    [Test]
    public void TryBuildThrowImpulse_UsesMinimumForwardTossForStationaryStunnedThrow()
    {
        var playerRoot = new GameObject("StationaryStunnedThrow_PlayerRoot");
        var handObject = new GameObject("StationaryStunnedThrow_Hand");
        var targetRoot = new GameObject("StationaryStunnedThrow_TargetRoot");
        var targetBodyObject = new GameObject("StationaryStunnedThrow_TargetBody");
        try
        {
            var playerBody = playerRoot.AddComponent<Rigidbody>();
            playerBody.velocity = Vector3.zero;
            var player = playerRoot.AddComponent<NetworkPlayer>();

            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            handObject.transform.SetParent(playerRoot.transform);
            var handBody = handObject.AddComponent<Rigidbody>();
            var handler = handObject.AddComponent<HandGrabHandler>();

            targetBodyObject.transform.SetParent(targetRoot.transform);
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            var joint = handObject.AddComponent<FixedJoint>();
            joint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(handler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(handler, handBody);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(handler, joint);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(handler, targetPlayer);

            var args = new object[] { null };
            var built = (bool)ResolveTryBuildThrowImpulse().Invoke(handler, args);

            Assert.That(built, Is.True);
            Assert.That(args[0], Is.Not.Null);

            var impulse = ResolveThrowImpulse(args[0]);
            var planarImpulse = Vector3.ProjectOnPlane(impulse, Vector3.up);
            var expectedForce = (CombatSettings.Instance != null ? CombatSettings.Instance.grabThrowForceStunned : 10f) * 0.2f;

            Assert.That(impulse.magnitude, Is.EqualTo(expectedForce).Within(0.001f));
            Assert.That(planarImpulse.magnitude, Is.GreaterThan(0.5f));
            Assert.That(Vector3.Dot(planarImpulse.normalized, playerRoot.transform.forward), Is.GreaterThan(0.95f));
            Assert.That(impulse.y, Is.GreaterThan(0f));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void TryBuildThrowImpulse_KeepsMomentumThrowsAboveStationaryFloor()
    {
        var playerRoot = new GameObject("MovingStunnedThrow_PlayerRoot");
        var handObject = new GameObject("MovingStunnedThrow_Hand");
        var targetRoot = new GameObject("MovingStunnedThrow_TargetRoot");
        var targetBodyObject = new GameObject("MovingStunnedThrow_TargetBody");
        try
        {
            var playerBody = playerRoot.AddComponent<Rigidbody>();
            var player = playerRoot.AddComponent<NetworkPlayer>();

            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            handObject.transform.SetParent(playerRoot.transform);
            var handBody = handObject.AddComponent<Rigidbody>();
            var handler = handObject.AddComponent<HandGrabHandler>();

            targetBodyObject.transform.SetParent(targetRoot.transform);
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            var joint = handObject.AddComponent<FixedJoint>();
            joint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(handler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(handler, handBody);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(handler, joint);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(handler, targetPlayer);

            playerBody.velocity = Vector3.zero;
            var stationaryArgs = new object[] { null };
            Assert.That((bool)ResolveTryBuildThrowImpulse().Invoke(handler, stationaryArgs), Is.True);
            var stationaryImpulse = ResolveThrowImpulse(stationaryArgs[0]);

            playerBody.velocity = playerRoot.transform.forward * 6.5f;
            var movingArgs = new object[] { null };
            Assert.That((bool)ResolveTryBuildThrowImpulse().Invoke(handler, movingArgs), Is.True);
            var movingImpulse = ResolveThrowImpulse(movingArgs[0]);
            var expectedForce = CombatSettings.Instance != null ? CombatSettings.Instance.grabThrowForceStunned : 10f;

            Assert.That(movingImpulse.magnitude, Is.GreaterThan(stationaryImpulse.magnitude + 1f));
            Assert.That(movingImpulse.magnitude, Is.EqualTo(expectedForce).Within(0.001f));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void TryGrab_DoesNotStartSingleHandStunnedReach()
    {
        var playerRoot = new GameObject("SingleHandStunnedReach_PlayerRoot");
        var leftHandObject = new GameObject("SingleHandStunnedReach_LeftHand");
        var rightHandObject = new GameObject("SingleHandStunnedReach_RightHand");
        var targetRoot = new GameObject("SingleHandStunnedReach_TargetRoot");
        var targetBodyObject = new GameObject("SingleHandStunnedReach_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            leftHandObject.transform.position = Vector3.zero;
            rightHandObject.transform.position = Vector3.right * 0.25f;
            targetRoot.transform.position = Vector3.forward * 0.45f;
            targetBodyObject.transform.SetParent(targetRoot.transform, false);

            var leftHandBody = leftHandObject.AddComponent<Rigidbody>();
            var rightHandBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            targetBodyObject.AddComponent<BoxCollider>();

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftHandBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightHandBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });
            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, false);
            ResolveField(typeof(NetworkPlayer), "_isGrabActive").SetValue(player, true);

            Physics.SyncTransforms();
            leftHandler.TryGrab();

            Assert.That(leftHandler.PendingReachTarget, Is.Null);
            Assert.That(rightHandler.PendingReachTarget, Is.Null);
            Assert.That(targetBody.transform.root.GetComponent<NetworkPlayer>(), Is.SameAs(targetPlayer));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void TryGrab_StartsStunnedReachWhenBothHandsGrabActive()
    {
        var playerRoot = new GameObject("DualHandStunnedReach_PlayerRoot");
        var leftHandObject = new GameObject("DualHandStunnedReach_LeftHand");
        var rightHandObject = new GameObject("DualHandStunnedReach_RightHand");
        var targetRoot = new GameObject("DualHandStunnedReach_TargetRoot");
        var targetBodyObject = new GameObject("DualHandStunnedReach_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            leftHandObject.transform.position = Vector3.zero;
            rightHandObject.transform.position = Vector3.right * 0.25f;
            targetRoot.transform.position = Vector3.forward * 0.45f;
            targetBodyObject.transform.SetParent(targetRoot.transform, false);

            var leftHandBody = leftHandObject.AddComponent<Rigidbody>();
            var rightHandBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            targetBodyObject.AddComponent<BoxCollider>();

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftHandBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightHandBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });
            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_isGrabActive").SetValue(player, true);

            Physics.SyncTransforms();
            leftHandler.TryGrab();
            rightHandler.TryGrab();

            Assert.That(leftHandler.PendingReachTarget, Is.SameAs(targetBody));
            Assert.That(rightHandler.PendingReachTarget, Is.SameAs(targetBody));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void AttachGrab_RequiresDualHandInputsAndSharedTargetForStunnedAttach()
    {
        var playerRoot = new GameObject("DualHandAttachGate_PlayerRoot");
        var leftHandObject = new GameObject("DualHandAttachGate_LeftHand");
        var rightHandObject = new GameObject("DualHandAttachGate_RightHand");
        var targetRoot = new GameObject("DualHandAttachGate_TargetRoot");
        var targetBodyObject = new GameObject("DualHandAttachGate_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform, false);

            var leftHandBody = leftHandObject.AddComponent<Rigidbody>();
            var rightHandBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            targetBodyObject.AddComponent<BoxCollider>();

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftHandBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightHandBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });

            ResolveAttachGrab().Invoke(leftHandler, new object[] { targetBody, targetBody.position });
            Assert.That(leftHandler.IsHolding, Is.False);

            ResolveField(typeof(HandGrabHandler), "_pendingReachTarget").SetValue(rightHandler, targetBody);
            ResolveAttachGrab().Invoke(leftHandler, new object[] { targetBody, targetBody.position });

            Assert.That(leftHandler.IsHolding, Is.False);

            ResolveField(typeof(NetworkPlayer), "_isLeftGrabActive").SetValue(player, true);
            ResolveField(typeof(NetworkPlayer), "_isRightGrabActive").SetValue(player, true);
            ResolveAttachGrab().Invoke(leftHandler, new object[] { targetBody, targetBody.position });

            Assert.That(leftHandler.IsHolding, Is.True);
            Assert.That(leftHandler.IsHoldingStunnedPlayer, Is.True);
            Assert.That(leftHandler.GrabTargetRoot, Is.EqualTo(targetRoot.transform));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void RotateTowardInput_DoesNotRotateRootDuringSingleHandStunnedHold()
    {
        var playerRoot = new GameObject("SingleHandFacingFreeze_PlayerRoot");
        var leftHandObject = new GameObject("SingleHandFacingFreeze_LeftHand");
        var rightHandObject = new GameObject("SingleHandFacingFreeze_RightHand");
        var targetRoot = new GameObject("SingleHandFacingFreeze_TargetRoot");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(leftHandler, targetPlayer);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });
            ResolveField(typeof(NetworkPlayer), "_targetRoot").SetValue(player, playerRoot.transform);

            var initialRotation = playerRoot.transform.rotation;
            ResolveRotateTowardInput().Invoke(player, new object[] { Vector3.right, 1f, 0.1f });

            Assert.That(Quaternion.Angle(initialRotation, playerRoot.transform.rotation), Is.LessThan(0.01f));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void RotateTowardInput_DoesNotAutoTurnTowardHeldAnchorDuringStunnedCarry()
    {
        var playerRoot = new GameObject("CarryFacingFreeze_PlayerRoot");
        var leftHandObject = new GameObject("CarryFacingFreeze_LeftHand");
        var rightHandObject = new GameObject("CarryFacingFreeze_RightHand");
        var targetRoot = new GameObject("CarryFacingFreeze_TargetRoot");
        var targetBodyObject = new GameObject("CarryFacingFreeze_TargetBody");
        try
        {
            targetRoot.transform.position = new Vector3(0f, 0f, -2f);

            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
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
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });
            ResolveField(typeof(NetworkPlayer), "_targetRoot").SetValue(player, playerRoot.transform);
            ResolveField(typeof(NetworkPlayer), "_localPhysicalPhase").SetValue(player, ResolvePhysicalPhase("CarryingStunned"));

            var initialRotation = playerRoot.transform.rotation;
            ResolveRotateTowardInput().Invoke(player, new object[] { Vector3.zero, 0f, 0.1f });

            Assert.That(Quaternion.Angle(initialRotation, playerRoot.transform.rotation), Is.LessThan(0.01f));
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void ProceduralGrabArm_SuppressesTorsoFacingAssist_OnlyForSingleHandStunnedHold()
    {
        var playerRoot = new GameObject("SingleHandTorsoAssist_PlayerRoot");
        var leftHandObject = new GameObject("SingleHandTorsoAssist_LeftHand");
        var rightHandObject = new GameObject("SingleHandTorsoAssist_RightHand");
        var targetRoot = new GameObject("SingleHandTorsoAssist_TargetRoot");
        var targetBodyObject = new GameObject("SingleHandTorsoAssist_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var arm = playerRoot.AddComponent<ProceduralGrabArm>();
            var grabController = playerRoot.AddComponent<CharacterGrabController>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, false);

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            targetBodyObject.transform.SetParent(targetRoot.transform);
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();
            var leftHandBody = leftHandObject.AddComponent<Rigidbody>();
            var rightHandBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();
            var leftJoint = leftHandObject.AddComponent<FixedJoint>();
            var rightJoint = rightHandObject.AddComponent<FixedJoint>();
            leftJoint.connectedBody = targetBody;
            rightJoint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftHandBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightHandBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(leftHandler, leftJoint);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(rightHandler, rightJoint);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(leftHandler, targetPlayer);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });
            ResolveField(typeof(ProceduralGrabArm), "_networkPlayer").SetValue(arm, player);
            ResolveField(typeof(ProceduralGrabArm), "_grabController").SetValue(arm, grabController);
            ResolveField(typeof(CharacterGrabController), "networkPlayer").SetValue(grabController, player);
            ResolveField(typeof(CharacterGrabController), "leftHand").SetValue(grabController, leftHandler);
            ResolveField(typeof(CharacterGrabController), "rightHand").SetValue(grabController, rightHandler);
            grabController.RefreshNow();

            Assert.That((bool)ResolveShouldSuppressTorsoFacingAssist().Invoke(arm, null), Is.True);

            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(rightHandler, targetPlayer);
            grabController.RefreshNow();

            Assert.That((bool)ResolveShouldSuppressTorsoFacingAssist().Invoke(arm, null), Is.False);
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void TryProcessThrow_DoesNothingWhenOnlyOneHandHoldsThrowableTarget()
    {
        var playerRoot = new GameObject("SingleHandThrowGate_PlayerRoot");
        var leftHandObject = new GameObject("SingleHandThrowGate_LeftHand");
        var rightHandObject = new GameObject("SingleHandThrowGate_RightHand");
        var targetRoot = new GameObject("SingleHandThrowGate_TargetRoot");
        var targetBodyObject = new GameObject("SingleHandThrowGate_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            var leftBody = leftHandObject.AddComponent<Rigidbody>();
            var rightBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();

            targetBodyObject.transform.SetParent(targetRoot.transform);
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();

            var leftJoint = leftHandObject.AddComponent<FixedJoint>();
            leftJoint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(leftHandler, leftJoint);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });

            var didThrow = (bool)ResolveTryProcessThrow().Invoke(player, null);

            Assert.That(didThrow, Is.False);
            Assert.That(leftHandler.IsHolding, Is.True);
            Assert.That(rightHandler.IsHolding, Is.False);
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void TryProcessThrow_ReleasesBothHandsWhenSharingSameThrowableTarget()
    {
        var playerRoot = new GameObject("AtomicThrow_PlayerRoot");
        var leftHandObject = new GameObject("AtomicThrow_LeftHand");
        var rightHandObject = new GameObject("AtomicThrow_RightHand");
        var targetRoot = new GameObject("AtomicThrow_TargetRoot");
        var targetBodyObject = new GameObject("AtomicThrow_TargetBody");
        try
        {
            var playerBody = playerRoot.AddComponent<Rigidbody>();
            playerBody.velocity = Vector3.zero;
            var player = playerRoot.AddComponent<NetworkPlayer>();

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            var leftBody = leftHandObject.AddComponent<Rigidbody>();
            var rightBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();

            targetBodyObject.transform.SetParent(targetRoot.transform);
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();

            var leftJoint = leftHandObject.AddComponent<FixedJoint>();
            leftJoint.connectedBody = targetBody;
            var rightJoint = rightHandObject.AddComponent<FixedJoint>();
            rightJoint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(leftHandler, leftJoint);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(rightHandler, rightJoint);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });

            Assert.That(leftHandler.IsHoldingThrowableTarget, Is.True);
            Assert.That(rightHandler.IsHoldingThrowableTarget, Is.True);
            Assert.That(leftHandler.GrabTarget, Is.SameAs(targetBody));
            Assert.That(rightHandler.GrabTarget, Is.SameAs(targetBody));

            var leftTryBuildArgs = new object[] { null };
            var rightTryBuildArgs = new object[] { null };
            Assert.That((bool)ResolveTryBuildThrowImpulse().Invoke(leftHandler, leftTryBuildArgs), Is.True);
            Assert.That((bool)ResolveTryBuildThrowImpulse().Invoke(rightHandler, rightTryBuildArgs), Is.True);

            var didThrow = (bool)ResolveTryProcessThrow().Invoke(player, null);
            Assert.That(didThrow, Is.True);
            Assert.That(leftHandler.IsHolding, Is.False);
            Assert.That(rightHandler.IsHolding, Is.False);
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }

    [Test]
    public void TryProcessThrow_DoesNothingWhenHeldPlayerHasRecovered()
    {
        var playerRoot = new GameObject("RecoveredTargetThrowGate_PlayerRoot");
        var leftHandObject = new GameObject("RecoveredTargetThrowGate_LeftHand");
        var rightHandObject = new GameObject("RecoveredTargetThrowGate_RightHand");
        var targetRoot = new GameObject("RecoveredTargetThrowGate_TargetRoot");
        var targetBodyObject = new GameObject("RecoveredTargetThrowGate_TargetBody");
        try
        {
            var player = playerRoot.AddComponent<NetworkPlayer>();
            var targetPlayer = targetRoot.AddComponent<NetworkPlayer>();
            ResolveField(typeof(NetworkPlayer), "_isActiveRagdoll").SetValue(targetPlayer, true);
            ResolveField(typeof(NetworkPlayer), "_isRecovering").SetValue(targetPlayer, true);
            ResolveField(typeof(NetworkPlayer), "_localPhysicalPhase").SetValue(targetPlayer, ResolvePhysicalPhase("Recovering"));

            leftHandObject.transform.SetParent(playerRoot.transform);
            rightHandObject.transform.SetParent(playerRoot.transform);
            var leftBody = leftHandObject.AddComponent<Rigidbody>();
            var rightBody = rightHandObject.AddComponent<Rigidbody>();
            var leftHandler = leftHandObject.AddComponent<HandGrabHandler>();
            var rightHandler = rightHandObject.AddComponent<HandGrabHandler>();

            targetBodyObject.transform.SetParent(targetRoot.transform);
            var targetBody = targetBodyObject.AddComponent<Rigidbody>();

            var leftJoint = leftHandObject.AddComponent<FixedJoint>();
            var rightJoint = rightHandObject.AddComponent<FixedJoint>();
            leftJoint.connectedBody = targetBody;
            rightJoint.connectedBody = targetBody;

            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(leftHandler, player);
            ResolveField(typeof(HandGrabHandler), "networkPlayer").SetValue(rightHandler, player);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(leftHandler, leftBody);
            ResolveField(typeof(HandGrabHandler), "rigidbody3D").SetValue(rightHandler, rightBody);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(leftHandler, HandGrabHandler.HandSide.Left);
            ResolveField(typeof(HandGrabHandler), "handSide").SetValue(rightHandler, HandGrabHandler.HandSide.Right);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(leftHandler, leftJoint);
            ResolveField(typeof(HandGrabHandler), "_fixedJoint").SetValue(rightHandler, rightJoint);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(leftHandler, targetPlayer);
            ResolveField(typeof(HandGrabHandler), "_grabbedPlayer").SetValue(rightHandler, targetPlayer);
            ResolveField(typeof(NetworkPlayer), "_handGrabHandlers").SetValue(player, new[] { leftHandler, rightHandler });

            Assert.That(leftHandler.IsHolding, Is.True);
            Assert.That(rightHandler.IsHolding, Is.True);
            Assert.That(leftHandler.IsHoldingThrowableTarget, Is.False);
            Assert.That(rightHandler.IsHoldingThrowableTarget, Is.False);

            var didThrow = (bool)ResolveTryProcessThrow().Invoke(player, null);

            Assert.That(didThrow, Is.False);
            Assert.That(leftHandler.IsHolding, Is.True);
            Assert.That(rightHandler.IsHolding, Is.True);
        }
        finally
        {
            DestroyIfNotNull(playerRoot);
            DestroyIfNotNull(targetRoot);
        }
    }
}
