using UnityEngine;

public sealed partial class NetworkPlayer
{
    private const float PunchFollowThroughScale = 0.16f;
    private const float PunchFollowThroughMin = 1.4f;
    private const float PunchFollowThroughMax = 4.8f;
    private const float PunchFollowThroughTorqueScale = 0.1f;
    private const float AttackCameraKickMin = 0.28f;
    private const float AttackCameraKickMax = 0.52f;
    private const float VictimCameraKickMin = 0.42f;
    private const float VictimCameraKickMax = 0.9f;

    private void ApplyPunchFollowThrough(Vector3 knockbackDir, float punchForce)
    {
        if (rigidbody3D == null || rigidbody3D.isKinematic)
            return;

        var planarDirection = Vector3.ProjectOnPlane(knockbackDir, Vector3.up);
        if (planarDirection.sqrMagnitude < 0.0001f)
            planarDirection = Vector3.ProjectOnPlane(ResolveCombatForward(), Vector3.up);
        if (planarDirection.sqrMagnitude < 0.0001f)
            return;

        planarDirection.Normalize();
        var impulse = Mathf.Clamp(
            punchForce * PunchFollowThroughScale,
            PunchFollowThroughMin,
            PunchFollowThroughMax);

        rigidbody3D.AddForce(planarDirection * impulse, ForceMode.Impulse);
        rigidbody3D.AddTorque(
            Vector3.Cross(Vector3.up, planarDirection) * (impulse * PunchFollowThroughTorqueScale),
            ForceMode.Impulse);
    }

    private void TriggerAttackCameraKick(Vector3 worldDirection, float punchForce)
    {
        var intensity = Mathf.Lerp(
            AttackCameraKickMin,
            AttackCameraKickMax,
            NormalizePunchImpact(punchForce));
        TriggerLocalCameraImpact(worldDirection, intensity, receivedHit: false);
    }

    private void TriggerVictimCameraKick(Vector3 worldDirection, float punchForce)
    {
        var intensity = Mathf.Lerp(
            VictimCameraKickMin,
            VictimCameraKickMax,
            NormalizePunchImpact(punchForce));
        TriggerLocalCameraImpact(worldDirection, intensity, receivedHit: true);
    }

    private void TriggerLocalCameraImpact(Vector3 worldDirection, float intensity, bool receivedHit)
    {
        if (Runner != null && Object != null && Object.IsValid && !HasInputAuthority)
            return;

        CacheOwnedPresentationComponents();
        if (_ownedCameraRigsCache == null || _ownedCameraRigsCache.Length == 0)
            return;

        var direction = worldDirection.sqrMagnitude > 0.0001f
            ? worldDirection.normalized
            : ResolveCombatForward();

        for (var i = 0; i < _ownedCameraRigsCache.Length; i++)
        {
            var rig = _ownedCameraRigsCache[i];
            if (rig != null && rig.isActiveAndEnabled)
                rig.AddImpactImpulse(direction, intensity, receivedHit);
        }
    }

    private Vector3 ResolveCombatForward()
    {
        return _targetRoot != null ? _targetRoot.forward : transform.forward;
    }

    private static float NormalizePunchImpact(float punchForce)
    {
        return Mathf.InverseLerp(8f, 18f, punchForce);
    }
}
