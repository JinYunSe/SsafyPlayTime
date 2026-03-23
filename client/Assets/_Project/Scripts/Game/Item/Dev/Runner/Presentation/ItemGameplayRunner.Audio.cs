/*
 * 파일 개요:
 * - ItemGameplayRunner.Audio 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Presentation 계층에서 오디오, 시각화, 데미지 표시 같은 테스트 연출 연결을 담당한다.
 * - 실제 스킬 판정과 분리된 표현 계층이므로, 연출 변경이 게임 규칙 변경으로 이어지지 않게 유지한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private bool IsRuntimeAudioOutputDisabled()
        {
            return disableRuntimeAudioOutput;
        }

        private void ApplyRuntimeAudioOutputPolicy()
        {
            if (!IsRuntimeAudioOutputDisabled())
            {
                return;
            }

            if (!_audioPolicyCaptured)
            {
                _previousAudioListenerPause = AudioListener.pause;
                _previousAudioListenerVolume = AudioListener.volume;
                _audioPolicyCaptured = true;
            }

            // ItemScene 임시 정책: 모든 런타임 오디오 출력을 강제로 차단한다.
            AudioListener.pause = true;
            AudioListener.volume = 0f;
            StopAllLoopingSfx();
        }

        private void RestoreRuntimeAudioOutputPolicy()
        {
            if (!IsRuntimeAudioOutputDisabled() || !_audioPolicyCaptured)
            {
                return;
            }

            AudioListener.pause = _previousAudioListenerPause;
            AudioListener.volume = _previousAudioListenerVolume;
            _audioPolicyCaptured = false;
        }

        private void EnsureAudioListenerGuard()
        {
            if (IsRuntimeAudioOutputDisabled())
            {
                return;
            }

            var listeners = FindObjectsOfType<AudioListener>(true);
            if (listeners == null || listeners.Length == 0)
            {
                CreateFallbackAudioListenerIfNeeded();
                UpdateFallbackAudioListenerTransform();
                return;
            }

            if (_fallbackAudioListener == null)
            {
                return;
            }

            // 외부 리스너가 생기면 fallback을 제거해 다중 리스너 경고를 예방한다.
            if (listeners.Length > 1 || listeners[0] != _fallbackAudioListener)
            {
                Destroy(_fallbackAudioListener.gameObject);
                _fallbackAudioListener = null;
            }
        }

        private void TickAudioListenerGuard()
        {
            ApplyRuntimeAudioOutputPolicy();

            if (Time.unscaledTime < _nextAudioListenerCheckTime)
            {
                if (_fallbackAudioListener != null)
                {
                    UpdateFallbackAudioListenerTransform();
                }

                return;
            }

            _nextAudioListenerCheckTime = Time.unscaledTime + AudioListenerCheckIntervalSec;
            EnsureAudioListenerGuard();
        }

        private void CreateFallbackAudioListenerIfNeeded()
        {
            if (_fallbackAudioListener != null)
            {
                return;
            }

            var root = new GameObject("Item_AudioListener");
            root.transform.SetParent(transform, false);
            _fallbackAudioListener = root.AddComponent<AudioListener>();
            LogStatus("AudioListener missing in scene. Created fallback listener.");
        }

        private void UpdateFallbackAudioListenerTransform()
        {
            if (_fallbackAudioListener == null)
            {
                return;
            }

            var target = targetRoot != null ? targetRoot : transform;
            _fallbackAudioListener.transform.position = target.position;
            _fallbackAudioListener.transform.rotation = target.rotation;
        }

        private void CleanupFallbackAudioListener()
        {
            if (_fallbackAudioListener == null)
            {
                return;
            }

            Destroy(_fallbackAudioListener.gameObject);
            _fallbackAudioListener = null;
        }
    }
}

