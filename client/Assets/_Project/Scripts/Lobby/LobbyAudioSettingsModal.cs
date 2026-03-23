using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed class LobbyAudioSettingsModal : MonoBehaviour
    {
        private const string ContentRootName = "AudioSettingsContent";
        private const float RowWidth = 520f;
        private const float RowHeight = 42f;

        private bool _initialized;
        private Slider _masterSlider;
        private Slider _effectSlider;
        private Slider _backgroundSlider;
        private TMP_Text _masterValueText;
        private TMP_Text _effectValueText;
        private TMP_Text _backgroundValueText;
        private TMP_FontAsset _referenceFont;
        private Material _referenceFontMaterial;
        private Color _referenceTextColor = Color.white;

        public void InitializeIfNeeded()
        {
            if (_initialized)
            {
                return;
            }

            GameAudioSettingsService.EnsureInstance();

            var dialog = transform.Find("Dialog") as RectTransform;
            if (dialog == null)
            {
                Debug.LogWarning("[LobbyAudioSettingsModal] Dialog child not found.");
                return;
            }

            ConfigureStaticTexts(dialog);
            var contentRoot = EnsureContentRoot(dialog);
            CreateOrUpdateRows(contentRoot);
            BindBottomButtons(dialog);
            _initialized = true;
        }

        private void OnEnable()
        {
            InitializeIfNeeded();
            transform.SetAsLastSibling();
            RefreshFromSettings();
        }

        private void ConfigureStaticTexts(RectTransform dialog)
        {
            if (dialog.Find("Title")?.GetComponent<TMP_Text>() is { } title)
            {
                title.text = "\uC124\uC815";
                _referenceFont = title.font;
                _referenceFontMaterial = title.fontSharedMaterial;
                _referenceTextColor = title.color;
            }

            if (dialog.Find("Sound")?.GetComponent<TMP_Text>() is { } sound)
            {
                sound.gameObject.SetActive(false);
            }

            if (dialog.Find("Resolution") is { } resolution)
            {
                resolution.gameObject.SetActive(false);
            }
        }

        private RectTransform EnsureContentRoot(RectTransform dialog)
        {
            if (dialog.Find(ContentRootName) is RectTransform existing)
            {
                return existing;
            }

            var root = new GameObject(ContentRootName, typeof(RectTransform));
            root.transform.SetParent(dialog, false);

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 32f);
            rect.sizeDelta = new Vector2(560f, 150f);
            return rect;
        }

        private void CreateOrUpdateRows(RectTransform contentRoot)
        {
            (_masterSlider, _masterValueText) = EnsureRow(contentRoot, "MasterVolumeRow", "\uB9C8\uC2A4\uD130 \uBCFC\uB968", 42f);
            (_backgroundSlider, _backgroundValueText) = EnsureRow(contentRoot, "BackgroundVolumeRow", "\uBC30\uACBD\uC74C \uBCFC\uB968", 0f);
            (_effectSlider, _effectValueText) = EnsureRow(contentRoot, "EffectVolumeRow", "\uD6A8\uACFC\uC74C \uBCFC\uB968", -42f);

            BindSlider(_masterSlider, _masterValueText, GameAudioSettingsService.SetMasterVolume);
            BindSlider(_backgroundSlider, _backgroundValueText, GameAudioSettingsService.SetBackgroundVolume);
            BindSlider(_effectSlider, _effectValueText, GameAudioSettingsService.SetEffectVolume);
        }

        private (Slider slider, TMP_Text valueText) EnsureRow(RectTransform parent, string name, string label, float y)
        {
            if (parent.Find(name) is RectTransform existingRow)
            {
                return (
                    existingRow.Find("Slider")?.GetComponent<Slider>(),
                    existingRow.Find("Value")?.GetComponent<TMP_Text>());
            }

            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(0f, y);
            rowRect.sizeDelta = new Vector2(RowWidth, RowHeight);

            var labelText = CreateText(
                "Label",
                rowRect,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0f),
                new Vector2(140f, RowHeight),
                label,
                28f,
                TextAlignmentOptions.Left);
            ApplyReferenceTextStyle(labelText);

            var slider = CreateSlider(rowRect, new Vector2(160f, 0f), new Vector2(250f, 24f));
            slider.gameObject.name = "Slider";

            var valueText = CreateText(
                "Value",
                rowRect,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(0f, 0f),
                new Vector2(88f, RowHeight),
                "100%",
                24f,
                TextAlignmentOptions.Right);
            ApplyReferenceTextStyle(valueText);

            return (slider, valueText);
        }

        private static Slider CreateSlider(RectTransform parent, Vector2 anchoredPosition, Vector2 size)
        {
            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(parent, false);

            var sliderRect = sliderGo.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0.5f);
            sliderRect.anchorMax = new Vector2(0f, 0.5f);
            sliderRect.pivot = new Vector2(0f, 0.5f);
            sliderRect.anchoredPosition = anchoredPosition;
            sliderRect.sizeDelta = size;

            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;

            var background = CreateImage("Background", sliderRect, Vector2.zero, size, new Color(0.83f, 0.86f, 0.9f, 1f));
            background.type = Image.Type.Sliced;

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderRect, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.offsetMin = new Vector2(10f, 6f);
            fillAreaRect.offsetMax = new Vector2(-10f, -6f);

            var fill = CreateImage("Fill", fillAreaRect, Vector2.zero, Vector2.zero, new Color(1f, 0.82f, 0.2f, 1f));
            fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            fill.rectTransform.anchorMax = new Vector2(1f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            var handleSlideArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleSlideArea.transform.SetParent(sliderRect, false);
            var handleSlideAreaRect = handleSlideArea.GetComponent<RectTransform>();
            handleSlideAreaRect.anchorMin = new Vector2(0f, 0f);
            handleSlideAreaRect.anchorMax = new Vector2(1f, 1f);
            handleSlideAreaRect.offsetMin = new Vector2(10f, 0f);
            handleSlideAreaRect.offsetMax = new Vector2(-10f, 0f);

            var handle = CreateImage("Handle", handleSlideAreaRect, Vector2.zero, new Vector2(22f, 22f), new Color(0.18f, 0.2f, 0.26f, 1f));
            handle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        private static Image CreateImage(string name, RectTransform parent, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            var image = go.GetComponent<Image>();
            image.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        private static TMP_Text CreateText(
            string name,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            string text,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.enableWordWrapping = false;
            if (TMP_Settings.defaultFontAsset != null)
            {
                tmp.font = TMP_Settings.defaultFontAsset;
            }

            return tmp;
        }

        private void ApplyReferenceTextStyle(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            if (_referenceFont != null)
            {
                text.font = _referenceFont;
            }

            if (_referenceFontMaterial != null)
            {
                text.fontSharedMaterial = _referenceFontMaterial;
            }

            text.color = _referenceTextColor;
        }

        private void BindBottomButtons(RectTransform dialog)
        {
            var buttonGroup = dialog.Find("ButtonGroup");
            if (buttonGroup == null)
            {
                return;
            }

            if (buttonGroup is RectTransform buttonGroupRect)
            {
                buttonGroupRect.SetParent(dialog, false);
                buttonGroupRect.anchorMin = new Vector2(0.5f, 0f);
                buttonGroupRect.anchorMax = new Vector2(0.5f, 0f);
                buttonGroupRect.pivot = new Vector2(0.5f, 0f);
                buttonGroupRect.anchoredPosition = new Vector2(0f, 10f);
            }

            if (buttonGroup.Find("ConfirmButton")?.GetComponent<Button>() is { } confirmButton)
            {
                confirmButton.onClick.RemoveListener(CloseModal);
                confirmButton.onClick.AddListener(CloseModal);
                if (confirmButton.GetComponentInChildren<TMP_Text>(true) is { } text)
                {
                    text.text = "\uB2EB\uAE30";
                }
            }

            if (buttonGroup.Find("CancelButton")?.GetComponentInChildren<TMP_Text>(true) is { } cancelText)
            {
                cancelText.text = "\uCDE8\uC18C";
            }
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

        private void RefreshFromSettings()
        {
            if (!_initialized)
            {
                return;
            }

            SetSliderWithoutNotify(_masterSlider, _masterValueText, GameAudioSettingsService.MasterVolume);
            SetSliderWithoutNotify(_effectSlider, _effectValueText, GameAudioSettingsService.EffectVolume);
            SetSliderWithoutNotify(_backgroundSlider, _backgroundValueText, GameAudioSettingsService.BackgroundVolume);
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
