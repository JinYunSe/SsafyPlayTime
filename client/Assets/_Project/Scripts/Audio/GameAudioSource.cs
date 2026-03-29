using UnityEngine;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class GameAudioSource : MonoBehaviour
    {
        [SerializeField] private GameAudioCategory category = GameAudioCategory.EffectSound;
        [SerializeField] private bool captureBaseVolumeOnEnable = true;

        private AudioSource _audioSource;
        private float _baseVolume = 1f;
        private bool _baseVolumeCaptured;

        public GameAudioCategory Category => category;
        public float BaseVolume => _baseVolume;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            // 아직 한 번도 캡처되지 않은 경우에만 재캡처를 허용한다.
            // 이미 캡처된 상태에서 리셋하면 service가 수정한 source.volume(= base * 0 = 0)을
            // baseVolume으로 잘못 캡처하는 버그가 발생한다.
            if (captureBaseVolumeOnEnable && !_baseVolumeCaptured)
                _baseVolumeCaptured = false;
            RegisterOrUpdate();
        }

        private void OnDisable()
        {
            if (_audioSource == null)
            {
                return;
            }

            GameAudioSettingsService.UnregisterSource(_audioSource);
        }

        public void SetCategory(GameAudioCategory value)
        {
            category = value;
            RegisterOrUpdate();
        }

        public void RefreshBaseVolumeFromCurrentSource()
        {
            _audioSource ??= GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                return;
            }

            _baseVolume = Mathf.Clamp01(_audioSource.volume);
            _baseVolumeCaptured = true;
            RegisterOrUpdate();
        }

        public void RegisterOrUpdate()
        {
            _audioSource ??= GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                return;
            }

            if (!_baseVolumeCaptured)
            {
                _baseVolume = Mathf.Clamp01(_audioSource.volume);
                _baseVolumeCaptured = true;
            }

            GameAudioSettingsService.RegisterSource(_audioSource, category, _baseVolume);
        }
    }
}
