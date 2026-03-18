using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private const string RunDustResourcePath = "_Project/Effects/CharacterRunDust";
    private const float WalkDustInterval = 0.22f;
    private const float SprintDustInterval = 0.15f;
    private const float FootDustProbeHeight = 0.25f;
    private const float FootDustProbeDistance = 0.9f;
    private const float FootDustSurfaceOffset = 0.02f;
    private const float FootDustFallbackOffset = 0.18f;

    // Object Pool — 씬 전체에서 공유하는 static 풀
    private static readonly Stack<GameObject> s_runDustPool = new();
    private static Vector3 s_runDustBaseScale = Vector3.one;
    private const float IdleUpperBodySwayMultiplier = 0.18f;
    private const float WalkUpperBodySwayMultiplier = 0.34f;
    private const float SprintUpperBodySwayMultiplier = 0.48f;
    private const float GrabUpperBodySwayBonus = 0.12f;

    private struct UpperBodySwayBinding
    {
        public Transform physics;
        public Transform visual;
        public float blendWeight;
        public Quaternion physicsRestLocalRotation;
        public Quaternion visualRestLocalRotation;
    }

    private readonly List<UpperBodySwayBinding> _upperBodySwayBindings = new();
    private bool _presentationEffectsDirty = true;
    private GameObject _runDustPrefab;
    private float _nextDustAt;
    private bool _nextDustLeftFoot = true;
    private int _lastPresentationEffectsFrame = -1;
    private Transform _leftFootVisual;
    private Transform _rightFootVisual;

    private void MarkPresentationEffectsDirty()
    {
        _presentationEffectsDirty = true;
    }

    private void UpdateCharacterPresentationEffects()
    {
        if (_lastPresentationEffectsFrame == Time.frameCount)
            return;

        _lastPresentationEffectsFrame = Time.frameCount;

        ApplyActiveUpperBodySway();
        TickRunDust();
    }

    private void EnsurePresentationEffectBindings()
    {
        if (!_presentationEffectsDirty)
            return;

        _presentationEffectsDirty = false;
        _upperBodySwayBindings.Clear();
        _leftFootVisual = null;
        _rightFootVisual = null;

        if (animator == null)
            return;

        if (animator.isHuman)
        {
            _leftFootVisual = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightFootVisual = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        var physicsByName = new Dictionary<string, Transform>(StringComparer.Ordinal);

        if (syncPhysicsObjects != null)
        {
            for (var i = 0; i < syncPhysicsObjects.Length; i++)
            {
                var syncObject = syncPhysicsObjects[i];
                if (syncObject == null)
                    continue;

                var physicsTransform = syncObject.transform;
                if (physicsTransform != null && !physicsByName.ContainsKey(physicsTransform.name))
                    physicsByName.Add(physicsTransform.name, physicsTransform);
            }
        }

        if (physicsByName.Count == 0 && _puppetMaster != null && _puppetMaster.muscles != null)
        {
            for (var i = 0; i < _puppetMaster.muscles.Length; i++)
            {
                var muscle = _puppetMaster.muscles[i];
                var physicsTransform = muscle.joint != null ? muscle.joint.transform : null;
                if (physicsTransform != null && !physicsByName.ContainsKey(physicsTransform.name))
                    physicsByName.Add(physicsTransform.name, physicsTransform);
            }
        }

        TryAddUpperBodySwayBinding(HumanBodyBones.Neck, 0.38f, physicsByName);
        TryAddUpperBodySwayBinding(HumanBodyBones.Head, 0.46f, physicsByName);
        TryAddUpperBodySwayBinding(HumanBodyBones.Chest, 0.12f, physicsByName);
        TryAddUpperBodySwayBinding(HumanBodyBones.LeftUpperArm, 0.42f, physicsByName);
        TryAddUpperBodySwayBinding(HumanBodyBones.LeftLowerArm, 0.52f, physicsByName);
        TryAddUpperBodySwayBinding(HumanBodyBones.LeftHand, 0.62f, physicsByName);
        TryAddUpperBodySwayBinding(HumanBodyBones.RightUpperArm, 0.42f, physicsByName);
        TryAddUpperBodySwayBinding(HumanBodyBones.RightLowerArm, 0.52f, physicsByName);
        TryAddUpperBodySwayBinding(HumanBodyBones.RightHand, 0.62f, physicsByName);
    }

    private void TryAddUpperBodySwayBinding(
        HumanBodyBones bone,
        float blendWeight,
        Dictionary<string, Transform> physicsByName)
    {
        if (animator == null || !animator.isHuman)
            return;

        var visual = animator.GetBoneTransform(bone);
        if (visual == null)
            return;

        if (!physicsByName.TryGetValue(visual.name, out var physics) || physics == null || physics == visual)
            return;

        _upperBodySwayBindings.Add(new UpperBodySwayBinding
        {
            physics = physics,
            visual = visual,
            blendWeight = blendWeight,
            physicsRestLocalRotation = physics.localRotation,
            visualRestLocalRotation = visual.localRotation
        });
    }

    private void ApplyActiveUpperBodySway()
    {
        if (!_isActiveRagdoll || ShouldUseHardPhysicsPresentation() || !useAnimatedVisualOnly)
            return;

        EnsurePresentationEffectBindings();
        if (_upperBodySwayBindings.Count == 0)
            return;

        var swayMultiplier = ResolveUpperBodySwayMultiplier();
        if (swayMultiplier <= 0f)
            return;

        for (var i = 0; i < _upperBodySwayBindings.Count; i++)
        {
            var binding = _upperBodySwayBindings[i];
            if (binding.visual == null || binding.physics == null)
                continue;

            var targetRotation = ResolveRelativeLocalRotation(
                binding.physicsRestLocalRotation,
                binding.physics.localRotation,
                binding.visualRestLocalRotation);
            binding.visual.localRotation = Quaternion.Slerp(
                binding.visual.localRotation,
                targetRotation,
                Mathf.Clamp01(binding.blendWeight * swayMultiplier));
        }
    }

    private float ResolveUpperBodySwayMultiplier()
    {
        var locomotionState = ResolvePresentationLocomotionState();
        var multiplier = locomotionState switch
        {
            PresentationLocomotionState.Sprint => SprintUpperBodySwayMultiplier,
            PresentationLocomotionState.Walk => WalkUpperBodySwayMultiplier,
            _ => IdleUpperBodySwayMultiplier
        };

        if (_isGrabActive || NetworkedIsBeingGrabbed)
            multiplier += GrabUpperBodySwayBonus;

        return Mathf.Clamp01(multiplier);
    }

    private void TickRunDust()
    {
        if (!_isActiveRagdoll || ShouldUseHardPhysicsPresentation())
        {
            ResetRunDustCadence();
            return;
        }

        var locomotionState = ResolvePresentationLocomotionState();
        if (locomotionState == PresentationLocomotionState.Idle)
        {
            ResetRunDustCadence();
            return;
        }

        if (Time.time < _nextDustAt)
            return;

        if (!TryResolveFootstepSpawnPose(_nextDustLeftFoot, out var position, out var rotation))
            return;

        var dustPrefab = LoadRunDustPrefab();
        if (dustPrefab == null)
            return;

        var scaleMultiplier = locomotionState == PresentationLocomotionState.Sprint ? 0.95f : 0.78f;
        RentRunDust(dustPrefab, position, rotation, scaleMultiplier);

        _nextDustLeftFoot = !_nextDustLeftFoot;
        _nextDustAt = Time.time + (locomotionState == PresentationLocomotionState.Sprint
            ? SprintDustInterval
            : WalkDustInterval);
    }

    private void ResetRunDustCadence()
    {
        _nextDustAt = Time.time + 0.05f;
        _nextDustLeftFoot = true;
    }

    private PresentationLocomotionState ResolvePresentationLocomotionState()
    {
        if (Runner != null && Object != null && Object.IsValid)
            return GetNetworkedLocomotionState();

        if (rigidbody3D != null)
        {
            var planarVelocity = rigidbody3D.velocity;
            planarVelocity.y = 0f;
            return ResolveLocomotionState(planarVelocity.magnitude * 0.4f, Input.GetKey(KeyCode.LeftShift));
        }

        return _localPresentationLocomotionState;
    }

    private bool TryResolveFootstepSpawnPose(bool useLeftFoot, out Vector3 position, out Quaternion rotation)
    {
        EnsurePresentationEffectBindings();

        var forwardReference = GetPresentationRootTransform();
        var forward = forwardReference != null ? forwardReference.forward : transform.forward;
        var foot = useLeftFoot ? _leftFootVisual : _rightFootVisual;
        var basePosition = foot != null
            ? foot.position
            : transform.position + (useLeftFoot ? -transform.right : transform.right) * FootDustFallbackOffset;

        var rayOrigin = basePosition + Vector3.up * FootDustProbeHeight;
        if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out var hit,
                FootDustProbeHeight + FootDustProbeDistance,
                ~0,
                QueryTriggerInteraction.Ignore))
        {
            position = default;
            rotation = default;
            return false;
        }

        if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
        {
            position = default;
            rotation = default;
            return false;
        }

        position = hit.point + hit.normal * FootDustSurfaceOffset;

        var tangent = Vector3.ProjectOnPlane(forward, hit.normal);
        if (tangent.sqrMagnitude <= 0.0001f)
            tangent = Vector3.ProjectOnPlane(transform.right, hit.normal);

        rotation = tangent.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(tangent.normalized, hit.normal)
            : Quaternion.FromToRotation(Vector3.up, hit.normal);
        return true;
    }

    private GameObject LoadRunDustPrefab()
    {
        if (_runDustPrefab == null)
            _runDustPrefab = Resources.Load<GameObject>(RunDustResourcePath);

        return _runDustPrefab;
    }

    // ── Object Pool ────────────────────────────────────────────────────────────

    private static void RentRunDust(GameObject prefab, Vector3 position, Quaternion rotation, float scaleMultiplier)
    {
        var go = GetOrCreateRunDust(prefab);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = s_runDustBaseScale * scaleMultiplier;
        go.SetActive(true);

        var ps = go.GetComponentInChildren<ParticleSystem>(true);
        if (ps != null)
        {
            ps.Clear(true);
            ps.Play(true);
        }

        var handle = go.GetComponent<RunDustPoolHandle>();
        if (handle != null)
            handle.pool = s_runDustPool;
    }

    private static GameObject GetOrCreateRunDust(GameObject prefab)
    {
        while (s_runDustPool.Count > 0)
        {
            var pooled = s_runDustPool.Pop();
            if (pooled != null)
                return pooled;
            // null이면 씬 언로드로 소멸된 것 — 새로 생성
        }

        var go = Instantiate(prefab);
        s_runDustBaseScale = go.transform.localScale;
        go.AddComponent<RunDustPoolHandle>();
        return go;
    }
}
