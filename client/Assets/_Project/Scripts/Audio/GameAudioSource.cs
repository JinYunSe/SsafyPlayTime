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
            // OnEnable 시 재캡처 허용 여부에 따라 플래그를 리셋한다.
            // RegisterSceneAudioSources()에서 호출되는 RegisterOrUpdate()는
            // _baseVolumeCaptured = true이면 재캡처하지 않으므로 누적 감소가 방지된다.
            if (captureBaseVolumeOnEnable)
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
