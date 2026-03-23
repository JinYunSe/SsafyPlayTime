using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class GameHUD : MonoBehaviour
{
    [Header("Game Start Timer")]
    [SerializeField] private TMP_Text gameStartTimerText;

    [Header("Loading")]
    [SerializeField] private Image loadingOverlay;

    [Header("Death")]
    [SerializeField] private Image deathOverlay;
    [SerializeField] private Color deathOverlayColor = new(0.42f, 0.42f, 0.42f, 0.82f);

    [Header("Water")]
    [SerializeField] private Image waterOverlay;
    [SerializeField] private Color waterOverlayColor = new(0.0f, 0.25f, 0.75f, 0.38f);

    private const float FadeOutDuration = 0.5f;
    private const float PulseDuration = 0.25f;
    private const float DeathOverlayFadeDuration = 0.45f;
    private const float WaterOverlayFadeDuration = 0.35f;

    // 수중 오버레이 맥동 설정
    private const float WaterPulseMin = 0.28f;
    private const float WaterPulseMax = 0.44f;
    private const float WaterPulsePeriod = 2.0f;

    // 수중 URP Volume 설정
    private const float UnderwaterVolumeFadeDuration = 0.5f;

    private static GameHUD _instance;

    private Coroutine _pulseCoroutine;
    private Coroutine _deathOverlayCoroutine;
    private Coroutine _waterOverlayCoroutine;
    private Coroutine _waterPulseCoroutine;
    private Coroutine _underwaterVolumeCoroutine;

    private Volume _underwaterVolume;
    private VolumeProfile _underwaterProfile;

    public static GameHUD FindOrCreate()
    {
        if (_instance != null)
            return _instance;

        var existing = FindObjectOfType<GameHUD>();
        if (existing != null)
            return existing;

        var root = new GameObject("GameHUD");
        return root.AddComponent<GameHUD>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        EnsureRuntimeReferences();
        HideCountdown();
        HideDeathOverlayImmediate();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        if (_underwaterVolume != null)
            Destroy(_underwaterVolume.gameObject);
        if (_underwaterProfile != null)
            Destroy(_underwaterProfile);
    }

    public void HideLoadingAndShow(string text, Color color)
    {
        EnsureRuntimeReferences();
        if (loadingOverlay != null && loadingOverlay.gameObject.activeSelf)
            StartCoroutine(FadeOutThenShow(text, color));
        else
            ShowWithPulse(text, color);
    }

    public void ShowWithPulse(string text, Color color)
    {
        EnsureRuntimeReferences();
        StopPulse();
        _pulseCoroutine = StartCoroutine(PulseText(text, color));
    }

    public void HideCountdown()
    {
        EnsureRuntimeReferences();
        StopPulse();
        if (gameStartTimerText != null)
        {
            gameStartTimerText.transform.localScale = Vector3.one;
            gameStartTimerText.gameObject.SetActive(false);
        }
    }

    public void PlayDeathOverlay()
    {
        EnsureRuntimeReferences();
        if (deathOverlay == null)
            return;

        if (_deathOverlayCoroutine != null)
            StopCoroutine(_deathOverlayCoroutine);

        _deathOverlayCoroutine = StartCoroutine(FadeDeathOverlay());
    }

    public void ShowWaterOverlay()
    {
        EnsureRuntimeReferences();
        if (waterOverlay == null) return;
        if (_waterOverlayCoroutine != null) StopCoroutine(_waterOverlayCoroutine);
        _waterOverlayCoroutine = StartCoroutine(FadeWaterOverlay(show: true));

        if (_waterPulseCoroutine != null) StopCoroutine(_waterPulseCoroutine);
        _waterPulseCoroutine = StartCoroutine(PulseWaterOverlay());

        EnsureUnderwaterVolume();
        if (_underwaterVolumeCoroutine != null) StopCoroutine(_underwaterVolumeCoroutine);
        _underwaterVolumeCoroutine = StartCoroutine(FadeUnderwaterVolume(show: true));
    }

    public void HideWaterOverlay()
    {
        EnsureRuntimeReferences();
        if (waterOverlay == null) return;
        if (_waterOverlayCoroutine != null) StopCoroutine(_waterOverlayCoroutine);
        _waterOverlayCoroutine = StartCoroutine(FadeWaterOverlay(show: false));

        if (_waterPulseCoroutine != null)
        {
            StopCoroutine(_waterPulseCoroutine);
            _waterPulseCoroutine = null;
        }

        if (_underwaterVolumeCoroutine != null) StopCoroutine(_underwaterVolumeCoroutine);
        _underwaterVolumeCoroutine = StartCoroutine(FadeUnderwaterVolume(show: false));
    }

    public void HideDeathOverlayImmediate()
    {
        EnsureRuntimeReferences();
        if (deathOverlay == null)
            return;

        if (_deathOverlayCoroutine != null)
        {
            StopCoroutine(_deathOverlayCoroutine);
            _deathOverlayCoroutine = null;
        }

        deathOverlay.gameObject.SetActive(false);
        deathOverlay.color = WithAlpha(deathOverlayColor, 0f);
    }

    private IEnumerator FadeOutThenShow(string text, Color color)
    {
        float t = 0f;
        var baseColor = loadingOverlay.color;
        while (t < FadeOutDuration)
        {
            t += Time.deltaTime;
            loadingOverlay.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(1f - t / FadeOutDuration));
            yield return null;
        }

        loadingOverlay.gameObject.SetActive(false);
        _pulseCoroutine = StartCoroutine(PulseText(text, color));
    }

    private IEnumerator PulseText(string text, Color color)
    {
        gameStartTimerText.gameObject.SetActive(true);
        gameStartTimerText.text = text;
        gameStartTimerText.color = color;
        gameStartTimerText.transform.localScale = Vector3.one * 1.5f;

        float t = 0f;
        while (t < PulseDuration)
        {
            t += Time.deltaTime;
            var scale = Mathf.Lerp(1.5f, 1.0f, t / PulseDuration);
            gameStartTimerText.transform.localScale = Vector3.one * scale;
            yield return null;
        }

        gameStartTimerText.transform.localScale = Vector3.one;
        _pulseCoroutine = null;
    }

    private IEnumerator FadeWaterOverlay(bool show)
    {
        waterOverlay.gameObject.SetActive(true);
        var startColor = waterOverlay.color;
        var targetColor = WithAlpha(waterOverlayColor, show ? waterOverlayColor.a : 0f);

        float elapsed = 0f;
        while (elapsed < WaterOverlayFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            waterOverlay.color = Color.Lerp(startColor, targetColor, Mathf.Clamp01(elapsed / WaterOverlayFadeDuration));
            yield return null;
        }

        waterOverlay.color = targetColor;
        if (!show) waterOverlay.gameObject.SetActive(false);
        _waterOverlayCoroutine = null;
    }

    // 수면 아래에 있을 때 overlay alpha를 파동처럼 진동시켜 물속 느낌을 강조
    private IEnumerator PulseWaterOverlay()
    {
        yield return new WaitForSecondsRealtime(WaterOverlayFadeDuration);

        float time = 0f;
        while (true)
        {
            time += Time.unscaledDeltaTime;
            var t = (Mathf.Sin(time * (Mathf.PI * 2f / WaterPulsePeriod)) + 1f) * 0.5f;
            var alpha = Mathf.Lerp(WaterPulseMin, WaterPulseMax, t);
            if (waterOverlay != null && waterOverlay.gameObject.activeSelf)
            {
                waterOverlay.color = WithAlpha(waterOverlayColor, alpha);
            }
            yield return null;
        }
    }

    // URP Volume을 생성하고 수중 후처리 효과를 설정
    private void EnsureUnderwaterVolume()
    {
        if (_underwaterVolume != null) return;

        var volumeGo = new GameObject("UnderwaterVolume");
        volumeGo.transform.SetParent(null);
        DontDestroyOnLoad(volumeGo);

        _underwaterVolume = volumeGo.AddComponent<Volume>();
        _underwaterVolume.isGlobal = true;
        _underwaterVolume.priority = 50f;
        _underwaterVolume.weight = 0f;

        _underwaterProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _underwaterVolume.profile = _underwaterProfile;

        // 색상 보정: 파란 색조 + 채도 감소
        var colorAdj = _underwaterProfile.Add<ColorAdjustments>(true);
        colorAdj.active = true;
        colorAdj.colorFilter.overrideState = true;
        colorAdj.colorFilter.value = new Color(0.45f, 0.72f, 1.0f);
        colorAdj.saturation.overrideState = true;
        colorAdj.saturation.value = -18f;
        colorAdj.contrast.overrideState = true;
        colorAdj.contrast.value = 8f;

        // 비네트: 어두운 파란 테두리로 물속 깊이감 표현
        var vignette = _underwaterProfile.Add<Vignette>(true);
        vignette.active = true;
        vignette.color.overrideState = true;
        vignette.color.value = new Color(0f, 0.08f, 0.25f);
        vignette.intensity.overrideState = true;
        vignette.intensity.value = 0.52f;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.35f;
        vignette.rounded.overrideState = true;
        vignette.rounded.value = true;

        // 약한 색수차: 물속 굴절 암시
        var ca = _underwaterProfile.Add<ChromaticAberration>(true);
        ca.active = true;
        ca.intensity.overrideState = true;
        ca.intensity.value = 0.12f;

        // 피사계심도(Gaussian): 먼 거리 약간 흐릿하게
        var dof = _underwaterProfile.Add<DepthOfField>(true);
        dof.active = true;
        dof.mode.overrideState = true;
        dof.mode.value = DepthOfFieldMode.Gaussian;
        dof.gaussianStart.overrideState = true;
        dof.gaussianStart.value = 2f;
        dof.gaussianEnd.overrideState = true;
        dof.gaussianEnd.value = 12f;
        dof.gaussianMaxRadius.overrideState = true;
        dof.gaussianMaxRadius.value = 0.8f;
    }

    private IEnumerator FadeUnderwaterVolume(bool show)
    {
        if (_underwaterVolume == null) yield break;

        var startWeight = _underwaterVolume.weight;
        var targetWeight = show ? 1f : 0f;

        float elapsed = 0f;
        while (elapsed < UnderwaterVolumeFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _underwaterVolume.weight = Mathf.Lerp(startWeight, targetWeight, Mathf.Clamp01(elapsed / UnderwaterVolumeFadeDuration));
            yield return null;
        }

        _underwaterVolume.weight = targetWeight;
        _underwaterVolumeCoroutine = null;
    }

    private IEnumerator FadeDeathOverlay()
    {
        deathOverlay.gameObject.SetActive(true);
        var startColor = deathOverlay.color;
        var targetColor = deathOverlayColor;
        targetColor.a = Mathf.Clamp01(targetColor.a);

        float elapsed = 0f;
        while (elapsed < DeathOverlayFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            deathOverlay.color = Color.Lerp(startColor, targetColor, Mathf.Clamp01(elapsed / DeathOverlayFadeDuration));
            yield return null;
        }

        deathOverlay.color = targetColor;
        _deathOverlayCoroutine = null;
    }

    private void StopPulse()
    {
        if (_pulseCoroutine == null)
            return;

        StopCoroutine(_pulseCoroutine);
        _pulseCoroutine = null;
        if (gameStartTimerText != null)
            gameStartTimerText.transform.localScale = Vector3.one;
    }

    private void EnsureRuntimeReferences()
    {
        EnsureCanvas();

        if (loadingOverlay == null)
            loadingOverlay = CreateFullscreenImage("LoadingOverlay", new Color(0f, 0f, 0f, 0f), siblingIndex: 0);

        if (gameStartTimerText == null)
            gameStartTimerText = CreateCountdownText();

        if (deathOverlay == null)
            deathOverlay = CreateFullscreenImage("DeathOverlay", WithAlpha(deathOverlayColor, 0f), siblingIndex: 1);

        if (waterOverlay == null)
            waterOverlay = CreateFullscreenImage("WaterOverlay", WithAlpha(waterOverlayColor, 0f), siblingIndex: 2);
    }

    private void EnsureCanvas()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;

        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();
    }

    private TMP_Text CreateCountdownText()
    {
        var textObject = new GameObject("CountdownText");
        textObject.transform.SetParent(transform, false);

        var rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(900f, 260f);
        rect.anchoredPosition = Vector2.zero;

        var text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = string.Empty;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 120f;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        textObject.SetActive(false);
        return text;
    }

    private Image CreateFullscreenImage(string objectName, Color color, int siblingIndex)
    {
        var imageObject = new GameObject(objectName);
        imageObject.transform.SetParent(transform, false);
        imageObject.transform.SetSiblingIndex(siblingIndex);

        var rect = imageObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        imageObject.SetActive(false);
        return image;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
