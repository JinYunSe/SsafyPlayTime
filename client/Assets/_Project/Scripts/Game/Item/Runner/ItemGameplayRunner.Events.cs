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
            itemRuntimeHost.VfxRequested += OnVfxRequested;
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
            itemRuntimeHost.VfxRequested -= OnVfxRequested;
            _eventsBound = false;
        }

        private void OnBlackholeRequested(BlackholeSkillRequest request)
        {
            if (_blackholeRoutine != null)
            {
                StopCoroutine(_blackholeRoutine);
            }

            _blackholeRoutine = StartCoroutine(CoBlackholeSkill(request));
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
    }
}
