using System;
using System.Collections;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void BindRuntimeEvents()
        {
            if (_eventsBound || itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.BlackholeRequested += OnBlackholeRequested;
            itemRuntimeHost.FlamethrowerStarted += OnFlamethrowerStarted;
            itemRuntimeHost.FlamethrowerTicked += OnFlamethrowerTicked;
            itemRuntimeHost.FlamethrowerStopped += OnFlamethrowerStopped;
            itemRuntimeHost.SfxRequested += OnSfxRequested;
            _eventsBound = true;
        }

        private void UnbindRuntimeEvents()
        {
            if (!_eventsBound || itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.BlackholeRequested -= OnBlackholeRequested;
            itemRuntimeHost.FlamethrowerStarted -= OnFlamethrowerStarted;
            itemRuntimeHost.FlamethrowerTicked -= OnFlamethrowerTicked;
            itemRuntimeHost.FlamethrowerStopped -= OnFlamethrowerStopped;
            itemRuntimeHost.SfxRequested -= OnSfxRequested;
            _eventsBound = false;
        }

        private void OnBlackholeRequested(BlackholeSkillRequest request)
        {
            Coroutine routine = null;
            routine = StartCoroutine(CoBlackholeSkillTracked(request, () =>
            {
                if (routine != null)
                {
                    _activeBlackholeRoutines.Remove(routine);
                }
            }));

            if (routine != null)
            {
                _activeBlackholeRoutines.Add(routine);
            }
        }

        private void OnFlamethrowerStarted(string itemId, float endAtSec)
        {
            EnsureFlamethrowerParticle();
            LogStatus($"Flamethrower started: {itemId}");
        }

        private void OnFlamethrowerStopped(string itemId)
        {
            StopFlamethrowerParticle();
            StopAllLoopingSfx();
            LogStatus($"Flamethrower stopped: {itemId}");
        }

        private void OnFlamethrowerTicked(FlamethrowerTickRequest request)
        {
            TickFlamethrower(in request);
        }

        private IEnumerator CoBlackholeSkillTracked(BlackholeSkillRequest request, Action onFinished)
        {
            yield return CoBlackholeSkill(request);
            onFinished?.Invoke();
        }

        private void StopAllBlackholeRoutines()
        {
            if (_activeBlackholeRoutines.Count == 0)
            {
                return;
            }

            // 여러 블랙홀 코루틴을 한 번에 정리한다.
            foreach (var routine in _activeBlackholeRoutines)
            {
                if (routine == null)
                {
                    continue;
                }

                StopCoroutine(routine);
            }

            _activeBlackholeRoutines.Clear();
        }
    }
}
