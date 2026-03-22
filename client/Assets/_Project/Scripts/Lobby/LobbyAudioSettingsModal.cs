using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed class LobbyAudioSettingsModal : MonoBehaviour
    {
        [Header("슬라이더")]
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider backgroundSlider;
        [SerializeField] private Slider effectSlider;

        [Header("값 텍스트")]
        [SerializeField] private TMP_Text masterValueText;
        [SerializeField] private TMP_Text backgroundValueText;
        [SerializeField] private TMP_Text effectValueText;

        [Header("버튼")]
        [SerializeField] private Button confirmButton;

        private void Awake()
        {
            GameAudioSettingsService.EnsureInstance();

            BindSlider(masterSlider, masterValueText, GameAudioSettingsService.SetMasterVolume);
            BindSlider(backgroundSlider, backgroundValueText, GameAudioSettingsService.SetBackgroundVolume);
            BindSlider(effectSlider, effectValueText, GameAudioSettingsService.SetEffectVolume);

            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(CloseModal);
            }
        }

        private void OnEnable()
        {
            transform.SetAsLastSibling();
            RefreshFromSettings();
        }

        private void RefreshFromSettings()
        {
            SetSliderWithoutNotify(masterSlider, masterValueText, GameAudioSettingsService.MasterVolume);
            SetSliderWithoutNotify(effectSlider, effectValueText, GameAudioSettingsService.EffectVolume);
            SetSliderWithoutNotify(backgroundSlider, backgroundValueText, GameAudioSettingsService.BackgroundVolume);
        }

        private static void BindSlider(Slider slider, TMP_Text valueText, UnityEngine.Events.UnityAction<float> setter)
        {
            if (slider == null || valueText == null)
            {
                return;
            }

            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(value =>
            {
                setter(value);
                valueText.text = $"{Mathf.RoundToInt(value * 100f)}%";
            });
        }

        private static void SetSliderWithoutNotify(Slider slider, TMP_Text valueText, float value)
        {
            if (slider == null || valueText == null)
            {
                return;
            }

            slider.SetValueWithoutNotify(value);
            valueText.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        private void CloseModal()
        {
            gameObject.SetActive(false);
        }
    }
}
