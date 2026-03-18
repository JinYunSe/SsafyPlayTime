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

        if (_pendingAnimatorDrivenPoseReset)
            TryRestoreAnimatorDrivenPresentation();

        if (!ShouldUseHardPhysicsPresentation())
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

            binding.visual.localRotation = ResolveVisualLocalRotation(binding);
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

        // 비호스트: physics presentation 전환 시 PuppetMaster mode 토글.
        // Active → Map()이 muscle→target 매핑 실행 (기절/잡힘 물리 포즈 필요)
        // Disabled → Map() 스킵, Animator가 target skeleton 단독 구동
        if (_puppetMaster != null && !HasStateAuthority)
        {
            _puppetMaster.mode = usingPhysicsPresentation
                ? RootMotion.Dynamics.PuppetMaster.Mode.Active
                : RootMotion.Dynamics.PuppetMaster.Mode.Disabled;
        }

        SetPhysicsPresentationVisualMode(usingPhysicsPresentation);
        MarkPhysicsPoseBindingsDirty();
        MarkPresentationEffectsDirty();

        if (!usingPhysicsPresentation)
            _pendingAnimatorDrivenPoseReset = true;
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
