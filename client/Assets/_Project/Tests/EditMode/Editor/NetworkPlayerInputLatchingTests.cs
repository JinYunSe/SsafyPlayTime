using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class NetworkPlayerInputLatchingTests
{
    private static FieldInfo ResolveField(string fieldName)
    {
        var field = typeof(NetworkPlayer).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return field;
    }

    private static MethodInfo ResolveMethod(string methodName)
    {
        var method = typeof(NetworkPlayer).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method;
    }

    [Test]
    public void BuildSandboxInput_UsesLatchedHeadbuttTriggerUntilReset()
    {
        var go = new GameObject("NetworkPlayerInputLatchingTests_Player");
        try
        {
            var player = go.AddComponent<NetworkPlayer>();
            ResolveField("_headbuttTriggered").SetValue(player, true);

            var buildSandboxInput = ResolveMethod("BuildSandboxInput");
            var resetOneShotLocalInput = ResolveMethod("ResetOneShotLocalInput");

            var firstInput = (PlayerNetworkInput)buildSandboxInput.Invoke(player, null);
            Assert.That((bool)firstInput.Headbutt, Is.True);

            resetOneShotLocalInput.Invoke(player, null);

            var secondInput = (PlayerNetworkInput)buildSandboxInput.Invoke(player, null);
            Assert.That((bool)secondInput.Headbutt, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
