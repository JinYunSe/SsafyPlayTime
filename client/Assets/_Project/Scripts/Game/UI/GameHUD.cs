using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameHUD : MonoBehaviour
{
    [Header("Game Start Timer")]
    [SerializeField] private TMP_Text gameStartTimerText;

    [Header("Loading")]
    [SerializeField] private Image loadingOverlay;

    private const float FadeOutDuration = 0.5f;
    private const float PulseDuration = 0.25f;

    private Coroutine _pulseCoroutine;

    private void Awake()
    {
        HideCountdown();
    }

    // 페이드아웃 완료 후 첫 번째 카운트다운 텍스트 표시
    public void HideLoadingAndShow(string text, Color color)
    {
        if (loadingOverlay != null && loadingOverlay.gameObject.activeSelf)
            StartCoroutine(FadeOutThenShow(text, color));
        else
            ShowWithPulse(text, color);
    }

    // 숫자/텍스트 변경 시 펄스 애니메이션
    public void ShowWithPulse(string text, Color color)
    {
        StopPulse();
        _pulseCoroutine = StartCoroutine(PulseText(text, color));
    }

    public void HideCountdown()
    {
        StopPulse();
        if (gameStartTimerText != null)
        {
            gameStartTimerText.transform.localScale = Vector3.one;
            gameStartTimerText.gameObject.SetActive(false);
        }
    }

    private IEnumerator FadeOutThenShow(string text, Color color)
    {
        float t = 0f;
        var c = loadingOverlay.color;
        while (t < FadeOutDuration)
        {
            t += Time.deltaTime;
            loadingOverlay.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(1f - t / FadeOutDuration));
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
            float scale = Mathf.Lerp(1.5f, 1.0f, t / PulseDuration);
            gameStartTimerText.transform.localScale = Vector3.one * scale;
            yield return null;
        }
        gameStartTimerText.transform.localScale = Vector3.one;
        _pulseCoroutine = null;
    }

    private void StopPulse()
    {
        if (_pulseCoroutine == null) return;
        StopCoroutine(_pulseCoroutine);
        _pulseCoroutine = null;
        if (gameStartTimerText != null)
            gameStartTimerText.transform.localScale = Vector3.one;
    }
}
