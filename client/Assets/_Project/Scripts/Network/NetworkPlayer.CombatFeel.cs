using System.Collections.Generic;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private const float PunchFollowThroughScale = 0.16f;
    private const float PunchFollowThroughMin = 1.4f;
    private const float PunchFollowThroughMax = 4.8f;
    private const float PunchFollowThroughTorqueScale = 0.1f;
    private const float AttackCameraKickMin = 0.28f;
    private const float AttackCameraKickMax = 0.52f;
    private const float KnockoutCameraKickMin = 0.5f;
    private const float KnockoutCameraKickMax = 0.82f;
    private const float VictimCameraKickMin = 0.42f;
    private const float VictimCameraKickMax = 0.9f;

    // ─── 히트 VFX ───
    private const string HitImpactVFXResourcePath = "_Project/Effects/HitImpactVFX";
    private const float HitVFXScaleMin = 0.6f;
    private const float HitVFXScaleMax = 1.2f;
    private const float HitVFXLifetime = 1.5f;
    private static GameObject s_hitVFXPrefab;
    private static readonly Stack<GameObject> s_hitVFXPool = new();
    private static Vector3 s_hitVFXBaseScale = Vector3.one;

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

    private void TriggerKnockoutConfirmCameraKick(Vector3 worldDirection, float impactForce)
    {
        var intensity = Mathf.Lerp(
            KnockoutCameraKickMin,
            KnockoutCameraKickMax,
            NormalizePunchImpact(impactForce));
        TriggerLocalCameraImpact(worldDirection, intensity, receivedHit: false);
    }

    private void TriggerKnockoutConfirm()
    {
        if (Runner != null && Object != null && Object.IsValid)
        {
            if (!HasStateAuthority)
                return;

            NetworkedKnockoutConfirmSequence = unchecked(NetworkedKnockoutConfirmSequence + 1);
            _lastConsumedKnockoutConfirmSequence = NetworkedKnockoutConfirmSequence;
        }

        TriggerLocalKnockoutConfirmFeedback();
    }

    private void ApplyReplicatedKnockoutConfirm()
    {
        if (Runner == null || Object == null || !Object.IsValid)
            return;

        if (_lastConsumedKnockoutConfirmSequence < 0)
        {
            _lastConsumedKnockoutConfirmSequence = NetworkedKnockoutConfirmSequence;
            return;
        }

        if (NetworkedKnockoutConfirmSequence == _lastConsumedKnockoutConfirmSequence)
            return;

        _lastConsumedKnockoutConfirmSequence = NetworkedKnockoutConfirmSequence;

        if (HasInputAuthority && !HasStateAuthority)
            TriggerLocalKnockoutConfirmFeedback();
    }

    private void TriggerLocalKnockoutConfirmFeedback()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasInputAuthority)
            return;

        TriggerKnockoutConfirmSlowMotion();
        TriggerKnockoutConfirmCameraKick(ResolveCombatForward(), ResolveKnockoutConfirmImpactForce());
    }

    private float ResolveKnockoutConfirmImpactForce()
    {
        var stat = CombatSettings.Instance?.GetAttackStat(PunchCombatStatId);
        var punchForce = stat.HasValue ? stat.Value.KnockbackForce : FallbackPunchKnockbackForce;
        return punchForce * 1.2f;
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

    // ─── 히트 VFX 스폰 ───

    private void SpawnHitImpactVFX(Vector3 hitPoint, Vector3 knockbackDir, float punchForce)
    {
        var prefab = LoadHitVFXPrefab();
        if (prefab == null)
            return;

        var rotation = knockbackDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(knockbackDir, Vector3.up)
            : Quaternion.identity;

        var normalizedImpact = NormalizePunchImpact(punchForce);
        var scale = Mathf.Lerp(HitVFXScaleMin, HitVFXScaleMax, normalizedImpact);

        var go = GetOrCreateHitVFX(prefab);
        go.transform.SetPositionAndRotation(hitPoint, rotation);
        go.transform.localScale = s_hitVFXBaseScale * scale;
        go.SetActive(true);

        var ps = go.GetComponentInChildren<ParticleSystem>(true);
        if (ps != null)
        {
            ps.Clear(true);
            ps.Play(true);
        }

        var handle = go.GetComponent<HitVFXPoolHandle>();
        if (handle != null)
            handle.pool = s_hitVFXPool;
    }

    private static GameObject LoadHitVFXPrefab()
    {
        if (s_hitVFXPrefab == null)
            s_hitVFXPrefab = Resources.Load<GameObject>(HitImpactVFXResourcePath);
        return s_hitVFXPrefab;
    }

    private static GameObject GetOrCreateHitVFX(GameObject prefab)
    {
        while (s_hitVFXPool.Count > 0)
        {
            var pooled = s_hitVFXPool.Pop();
            if (pooled != null)
                return pooled;
        }

        var go = Instantiate(prefab);
        s_hitVFXBaseScale = go.transform.localScale;
        go.AddComponent<HitVFXPoolHandle>();
        return go;
    }
}
