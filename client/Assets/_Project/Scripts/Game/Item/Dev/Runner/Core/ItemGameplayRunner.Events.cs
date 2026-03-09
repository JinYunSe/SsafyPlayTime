/*
 * 파일 개요:
 * - ItemGameplayRunner.Events 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Core 계층에서 ItemGameplayRunner의 생명주기, 입력, 이벤트 연결 같은 중심 흐름을 관리한다.
 * - 테스트용 실행 경로를 정리하는 파일이므로, 실게임 로직과 분리된 검증 허브라는 성격을 유지해야 한다.
 */
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
            itemRuntimeHost.SatelliteStrikeRequested += OnSatelliteStrikeRequested;
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
            itemRuntimeHost.SatelliteStrikeRequested -= OnSatelliteStrikeRequested;
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

        private void OnSatelliteStrikeRequested(SatelliteStrikeRequest request)
        {
            Coroutine routine = null;
            routine = StartCoroutine(CoSatelliteStrikeTracked(request, () =>
            {
                if (routine != null)
                {
                    _activeSatelliteStrikeRoutines.Remove(routine);
                }
            }));

            if (routine != null)
            {
                _activeSatelliteStrikeRoutines.Add(routine);
            }
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

