using UnityEngine;

public sealed partial class NetworkPlayer
{
    private bool ShouldDisablePhysicsAnimationSync =>
        useAnimatedVisualOnly && disablePhysicsAnimationSync && _animatedVisualRoot != null;

    private void ConfigureAnimatedVisualMode()
    {
        if (!useAnimatedVisualOnly)
            return;

        _animatedVisualRoot = FindAnimatedVisualRoot();
        if (_animatedVisualRoot == null)
            return;

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
        if (IsAnimatedVisualRoot(animator != null ? animator.transform : null))
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

        return null;
    }

    private static bool IsAnimatedVisualRoot(Transform candidate)
    {
        if (candidate == null)
            return false;

        return candidate.name == "_AnimationDriver" || candidate.name.Contains("Animated");
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
}
