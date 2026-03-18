using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private struct PhysicsPoseBinding
    {
        public Transform physics;
        public Transform visual;
        public Quaternion physicsRestLocalRotation;
        public Quaternion visualRestLocalRotation;
    }

    private bool ShouldDisablePhysicsAnimationSync =>
        useAnimatedVisualOnly && disablePhysicsAnimationSync && _animatedVisualRoot != null;
    private bool _hasAlternateVisualSwapTargets;
    private readonly List<PhysicsPoseBinding> _physicsPoseBindings = new();
    private bool _physicsPoseBindingsDirty = true;
    private bool _wasUsingPhysicsPresentation;
    private int _lastPhysicsPresentationSyncFrame = -1;
    private bool _pendingAnimatorDrivenPoseReset;
    private const float HitReactionBoneCopyThreshold = 0.58f;
    private const float HitReactionBoneCopyFullThreshold = 0.88f;

    /// <summary>
    /// 호스트 전용 로컬 래치: 회복 진입 시 켜지고, Stable로 돌아오면 꺼진다.
    /// 래치 활성 동안 PuppetMaster.mappingWeight를 0으로 내려 Map()이 target skeleton을
    /// 덮어쓰지 않게 하고, 물리 뼈→비주얼 복사도 건너뛴다.
    /// </summary>
    private bool _forceAnimatorVisualLatch;
    private float _savedMappingWeight = 1f;

    private void ConfigureAnimatedVisualMode()
    {
        if (!useAnimatedVisualOnly)
            return;

        _animatedVisualRoot = FindAnimatedVisualRoot();
        if (_animatedVisualRoot == null)
            return;

        MarkPhysicsPoseBindingsDirty();

        _hasAlternateVisualSwapTargets = HasAlternateVisibleRenderers(_animatedVisualRoot);

        var preferredAnimator = _animatedVisualRoot.GetComponent<Animator>()
            ?? _animatedVisualRoot.GetComponentInChildren<Animator>(true);
        if (preferredAnimator != null)
            animator = preferredAnimator;

        SetVisibleRendererState(_animatedVisualRoot);
        DisableNonVisualAnimators();
        SetSyncAnimationEnabledForAll(!disablePhysicsAnimationSync);
    }

    private Transform FindAnimatedVisualRoot()
    {
        if (_puppetMaster != null && _puppetMaster.targetRoot != null)
            return _puppetMaster.targetRoot;

        if (animator != null && animator.transform != transform)
            return animator.transform;

        var animationDriver = transform.Find("_AnimationDriver");
        if (animationDriver != null)
            return animationDriver;

        var model = transform.Find("Model");
        if (model != null)
        {
            for (var i = 0; i < model.childCount; i++)
            {
                var child = model.GetChild(i);
                if (IsAnimatedVisualRoot(child))
                    return child;
            }
        }

        var animators = GetComponentsInChildren<Animator>(true);
        for (var i = 0; i < animators.Length; i++)
        {
            var candidate = animators[i];
            if (candidate != null && IsAnimatedVisualRoot(candidate.transform))
                return candidate.transform;
        }

        if (animators.Length == 1 && animators[0] != null && animators[0].transform != transform)
            return animators[0].transform;

        return null;
    }

    private static bool IsAnimatedVisualRoot(Transform candidate)
    {
        if (candidate == null)
            return false;

        return candidate.name == "_AnimationDriver" || candidate.name.Contains("Animated");
    }

    private bool HasAlternateVisibleRenderers(Transform visibleRoot)
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var candidate = renderers[i];
            if (candidate != null && !IsUnderVisualRoot(candidate.transform, visibleRoot))
                return true;
        }

        return false;
    }

    private void SetVisibleRendererState(Transform visibleRoot)
    {
        var skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (var i = 0; i < skinnedMeshRenderers.Length; i++)
            skinnedMeshRenderers[i].enabled = IsUnderVisualRoot(skinnedMeshRenderers[i].transform, visibleRoot);

        var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        for (var i = 0; i < meshRenderers.Length; i++)
            meshRenderers[i].enabled = IsUnderVisualRoot(meshRenderers[i].transform, visibleRoot);
    }

    private static bool IsUnderVisualRoot(Transform target, Transform visibleRoot)
    {
        return target == visibleRoot || target.IsChildOf(visibleRoot);
    }

    private void DisableNonVisualAnimators()
    {
        if (animator == null)
            return;

        var animators = GetComponentsInChildren<Animator>(true);
        for (var i = 0; i < animators.Length; i++)
        {
            var candidate = animators[i];
            if (candidate == null || candidate == animator)
                continue;

            candidate.enabled = false;
        }
    }

    private void SetSyncAnimationEnabledForAll(bool enabled)
    {
        if (syncPhysicsObjects == null)
            return;

        for (var i = 0; i < syncPhysicsObjects.Length; i++)
        {
            if (syncPhysicsObjects[i] != null)
                syncPhysicsObjects[i].SetSyncAnimationEnabled(enabled);
        }
    }

    // ─── 기절/회복 비주얼 모드 전환 ───
    private void MarkPhysicsPoseBindingsDirty()
    {
        _physicsPoseBindingsDirty = true;
    }

    private void UpdatePhysicsDrivenVisualPose()
    {
        SynchronizePhysicsPresentationState();
        TickAuthorityAnimatorVisualLatch();

        if (_pendingAnimatorDrivenPoseReset)
            TryRestoreAnimatorDrivenPresentation();

        var useHardPhysicsPresentation = ShouldUseHardPhysicsPresentation();
        var hitReactionCopyWeight = useHardPhysicsPresentation ? 1f : ResolveHitReactionBoneCopyWeight();
        if (!useHardPhysicsPresentation && hitReactionCopyWeight <= 0f)
            return;

        // 호스트 래치 활성: 물리 뼈→비주얼 복사를 건너뛰어 animator pose를 유지
        if (_forceAnimatorVisualLatch && hitReactionCopyWeight <= 0f)
            return;

        var presentationRoot = GetPresentationRootTransform();
        if (presentationRoot == null || syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            return;

        EnsurePhysicsPoseBindings(presentationRoot);
        for (var i = 0; i < _physicsPoseBindings.Count; i++)
        {
            var binding = _physicsPoseBindings[i];
            if (binding.physics == null || binding.visual == null)
                continue;

            var physicsRotation = ResolveVisualLocalRotation(binding);
            binding.visual.localRotation = hitReactionCopyWeight >= 0.999f
                ? physicsRotation
                : Quaternion.Slerp(binding.visual.localRotation, physicsRotation, hitReactionCopyWeight);
        }
    }

    private float ResolveHitReactionBoneCopyWeight()
    {
        if (GetPhysicalPhase() != PhysicalPhase.Unstable)
            return 0f;

        var instability = GetPhysicalInstability();
        var instabilityWeight = Mathf.InverseLerp(
            HitReactionBoneCopyThreshold,
            HitReactionBoneCopyFullThreshold,
            instability);
        var hitWeight = 0f;
        if (IsInHitRecoil)
            hitWeight = 0.72f;
        else if (_hitInstabilityBoost > 0.01f)
            hitWeight = Mathf.Lerp(0.45f, 0.85f, Mathf.Clamp01(_hitInstabilityBoost / HitInstabilityBoostMax));

        return Mathf.Clamp01(Mathf.Max(instabilityWeight, hitWeight));
    }

    /// <summary>
    /// ForceRecover()에서 직접 호출: 호스트의 PuppetMaster.mappingWeight를 즉시 0으로 내리고 래치 활성화.
    /// LateUpdate의 전환 감지를 기다리지 않으므로 FixedUpdate→LateUpdate 사이에
    /// PuppetMaster.Map()이 물리 포즈를 target skeleton에 덮어쓰는 것을 방지.
    /// </summary>
    private void ActivateAuthorityAnimatorVisualLatch()
    {
        if (_puppetMaster == null || !HasStateAuthority || _forceAnimatorVisualLatch)
            return;

        _savedMappingWeight = _puppetMaster.mappingWeight;
        _puppetMaster.mappingWeight = 0f;
        _forceAnimatorVisualLatch = true;
        _pendingAnimatorDrivenPoseReset = true;
    }

    /// <summary>
    /// TriggerStun()에서 호출: 래치가 활성 상태면 해제하고 mappingWeight 복원.
    /// 기절 시 Map()이 래그돌 포즈를 target skeleton에 써야 하므로 mappingWeight가 정상이어야 한다.
    /// </summary>
    private void DeactivateAuthorityAnimatorVisualLatch()
    {
        if (!_forceAnimatorVisualLatch || _puppetMaster == null)
            return;

        _puppetMaster.mappingWeight = _savedMappingWeight;
        _forceAnimatorVisualLatch = false;
    }

    /// <summary>
    /// 호스트 래치를 매 프레임 틱: phase가 Recovering/Unstable을 벗어나면 해제하고 mappingWeight 복원.
    /// </summary>
    private void TickAuthorityAnimatorVisualLatch()
    {
        if (!_forceAnimatorVisualLatch)
            return;

        var phase = GetPhysicalPhase();
        if (phase != PhysicalPhase.Recovering && phase != PhysicalPhase.Unstable)
        {
            _forceAnimatorVisualLatch = false;
            if (_puppetMaster != null)
                _puppetMaster.mappingWeight = _savedMappingWeight;
        }
    }

    private void SynchronizePhysicsPresentationState()
    {
        if (_lastPhysicsPresentationSyncFrame == Time.frameCount)
            return;

        _lastPhysicsPresentationSyncFrame = Time.frameCount;

        var usingPhysicsPresentation = ShouldUseHardPhysicsPresentation();
        if (usingPhysicsPresentation == _wasUsingPhysicsPresentation)
            return;

        _wasUsingPhysicsPresentation = usingPhysicsPresentation;

        SyncPuppetMasterMode(usingPhysicsPresentation);

        SetPhysicsPresentationVisualMode(usingPhysicsPresentation);
        MarkPhysicsPoseBindingsDirty();
        MarkPresentationEffectsDirty();

        if (!usingPhysicsPresentation)
            _pendingAnimatorDrivenPoseReset = true;
    }

    /// <summary>
    /// PuppetMaster mode를 physics presentation 상태에 맞춰 전환한다.
    /// - 비호스트(proxy): mode를 Active/Disabled로 직접 전환.
    /// - 호스트(authority): mode는 건드리지 않되, 회복 시 로컬 비주얼 래치를 켜서
    ///   Map()이 visual pose를 덮어쓰지 않게 한다.
    /// </summary>
    private void SyncPuppetMasterMode(bool usingPhysicsPresentation)
    {
        if (_puppetMaster == null)
            return;

        if (!HasStateAuthority)
        {
            // 비호스트: physics presentation 전환 시 PuppetMaster mode 토글.
            // Active → Map()이 muscle→target 매핑 실행 (기절/잡힘 물리 포즈 필요)
            // Disabled → Map() 스킵, Animator가 target skeleton 단독 구동
            _puppetMaster.mode = usingPhysicsPresentation
                ? RootMotion.Dynamics.PuppetMaster.Mode.Active
                : RootMotion.Dynamics.PuppetMaster.Mode.Disabled;
        }
        else
        {
            // 호스트: PuppetMaster mode는 유지 (물리 시뮬레이션 책임).
            // 회복 진입 시 mappingWeight를 0으로 내려 Map()이 target skeleton을 덮어쓰지 않게 한다.
            if (!usingPhysicsPresentation && !_forceAnimatorVisualLatch)
            {
                _savedMappingWeight = _puppetMaster.mappingWeight;
                _puppetMaster.mappingWeight = 0f;
                _forceAnimatorVisualLatch = true;
            }
            else if (usingPhysicsPresentation && _forceAnimatorVisualLatch)
            {
                // 기절 재진입: 래치 해제하고 mappingWeight 복원
                _puppetMaster.mappingWeight = _savedMappingWeight;
                _forceAnimatorVisualLatch = false;
            }
        }
    }

    private void TryRestoreAnimatorDrivenPresentation()
    {
        if (!_pendingAnimatorDrivenPoseReset || ShouldUseHardPhysicsPresentation())
            return;

        _pendingAnimatorDrivenPoseReset = false;
        MarkPhysicsPoseBindingsDirty();
        MarkPresentationEffectsDirty();

        if (_externalAnimationDriver != null)
        {
            _externalAnimationDriver.RestoreAnimatorAfterPhysicsPresentation();
            return;
        }

        if (animator == null)
            return;

        animator.enabled = true;
        animator.Rebind();
        animator.Update(0f);
    }

    private void EnsurePhysicsPoseBindings(Transform presentationRoot)
    {
        if (!_physicsPoseBindingsDirty && _physicsPoseBindings.Count > 0)
            return;

        _physicsPoseBindingsDirty = false;
        _physicsPoseBindings.Clear();

        var visualByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        var visualTransforms = presentationRoot.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < visualTransforms.Length; i++)
        {
            var candidate = visualTransforms[i];
            if (candidate != null && !visualByName.ContainsKey(candidate.name))
                visualByName.Add(candidate.name, candidate);
        }

        for (var i = 0; i < syncPhysicsObjects.Length; i++)
        {
            var physicsTransform = syncPhysicsObjects[i] != null ? syncPhysicsObjects[i].transform : null;
            if (physicsTransform == null)
                continue;

            if (!visualByName.TryGetValue(physicsTransform.name, out var visualTransform))
                continue;

            if (visualTransform == physicsTransform)
                continue;

            _physicsPoseBindings.Add(new PhysicsPoseBinding
            {
                physics = physicsTransform,
                visual = visualTransform,
                physicsRestLocalRotation = physicsTransform.localRotation,
                visualRestLocalRotation = visualTransform.localRotation
            });
        }
    }

    private static Quaternion ResolveVisualLocalRotation(in PhysicsPoseBinding binding)
    {
        return ResolveRelativeLocalRotation(
            binding.physicsRestLocalRotation,
            binding.physics.localRotation,
            binding.visualRestLocalRotation);
    }

    private static Quaternion ResolveRelativeLocalRotation(
        Quaternion physicsRestLocalRotation,
        Quaternion currentPhysicsLocalRotation,
        Quaternion visualRestLocalRotation)
    {
        var localDelta = Quaternion.Inverse(physicsRestLocalRotation) * currentPhysicsLocalRotation;
        return visualRestLocalRotation * localDelta;
    }

    private bool _isStunVisualMode;

    /// <summary>
    /// 기절 시: PuppetMaster 타겟 스켈레톤(물리 매핑 대상)의 메시를 보여주고
    ///          애니메이션 비주얼 루트의 메시를 숨긴다.
    ///          → 보이는 모델이 래그돌 물리 결과를 따라감.
    /// 회복 시: 애니메이션 비주얼 루트의 메시를 복원.
    ///          → 보이는 모델이 애니메이터 구동으로 돌아감.
    /// </summary>
    private void SetPhysicsPresentationVisualMode(bool usePhysicsPresentation)
    {
        if (!ShouldDisablePhysicsAnimationSync || _animatedVisualRoot == null || !_hasAlternateVisualSwapTargets)
            return;

        if (_isStunVisualMode == usePhysicsPresentation)
            return;

        _isStunVisualMode = usePhysicsPresentation;

        if (usePhysicsPresentation)
        {
            // 물리 타겟 스켈레톤의 렌더러를 보이게, 애니메이션 비주얼 렌더러를 숨기기
            var skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var i = 0; i < skinnedMeshRenderers.Length; i++)
                skinnedMeshRenderers[i].enabled = !IsUnderVisualRoot(skinnedMeshRenderers[i].transform, _animatedVisualRoot);

            var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < meshRenderers.Length; i++)
                meshRenderers[i].enabled = !IsUnderVisualRoot(meshRenderers[i].transform, _animatedVisualRoot);
        }
        else
        {
            // 원래 상태 복원: 애니메이션 비주얼만 보이게
            SetVisibleRendererState(_animatedVisualRoot);
        }
    }

    private void SetStunVisualMode(bool stunned)
    {
        SetPhysicsPresentationVisualMode(stunned);
    }
}
