using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed class RuntimeLogOverlay : MonoBehaviour
    {
        private const int MaxLines = 150;
        private static RuntimeLogOverlay _instance;

        private readonly Queue<string> _lines = new();
        private string _logFilePath = string.Empty;

        private GameObject _panel;
        private TMP_Text _text;
        private ScrollRect _scrollRect;
        private RectTransform _textRect;
        private bool _uiDirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                return;
            }

            EnsureInstance();
        }

        public static void EnsureInstance()
        {
            if (!RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                return;
            }

            if (_instance != null || FindObjectOfType<RuntimeLogOverlay>() != null)
            {
                return;
            }

            var go = new GameObject("RuntimeLogOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<RuntimeLogOverlay>();
        }

        private void Awake()
        {
            if (!RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                Destroy(gameObject);
                return;
            }

            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureEventSystem();
            BuildOverlayUi();
            InitializeLogFile();
        }

        private void OnEnable()
        {
            if (!RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                return;
            }

            Application.logMessageReceived += OnUnityLogReceived;
            AppendLine("Runtime log overlay initialized. Press F6 to show/hide.");
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnUnityLogReceived;
        }

        private void Update()
        {
            EnsureEventSystem();

            if (Input.GetKeyDown(KeyCode.F6))
            {
                TogglePanel();
            }

            FlushPendingUi();
        }

        private void OnGUI()
        {
            if (_panel != null)
            {
                return;
            }

            var content = _lines.Count == 0 ? "Runtime log overlay fallback active." : string.Join("\n", _lines);
            GUI.Box(new Rect(10, 10, Screen.width - 20, 220), content);
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystemGo);
        }

        private void BuildOverlayUi()
        {
            TmpFontFallbackBootstrap.EnsureKoreanFallbackRegistered();

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            gameObject.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var panelRect = _panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.offsetMin = new Vector2(12f, 12f);
            panelRect.offsetMax = new Vector2(-12f, 252f);

            var panelImage = _panel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.7f);

            var scrollViewObject = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            scrollViewObject.transform.SetParent(_panel.transform, false);
            var scrollViewRect = scrollViewObject.GetComponent<RectTransform>();
            scrollViewRect.anchorMin = Vector2.zero;
            scrollViewRect.anchorMax = Vector2.one;
            scrollViewRect.offsetMin = new Vector2(10f, 10f);
            scrollViewRect.offsetMax = new Vector2(-10f, -46f);

            var scrollViewImage = scrollViewObject.GetComponent<Image>();
            scrollViewImage.color = new Color(0f, 0f, 0f, 0.15f);
            var scrollViewMask = scrollViewObject.GetComponent<Mask>();
            scrollViewMask.showMaskGraphic = false;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(scrollViewObject.transform, false);
            var contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(contentObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(0f, 0f);
            _textRect = textRect;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            var fallbackFont = TmpFontFallbackBootstrap.ActiveFallbackFont;
            if (fallbackFont != null)
            {
                text.font = fallbackFont;
            }
            text.fontSize = 18f;
            text.enableWordWrapping = false;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = new Color(0.9f, 0.95f, 1f, 1f);
            text.text = string.Empty;
            _text = text;

            _scrollRect = scrollViewObject.GetComponent<ScrollRect>();
            _scrollRect.content = contentRect;
            _scrollRect.viewport = scrollViewRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 28f;

            CreateTopRightButton("CloseButton", "X", new Vector2(-8f, -8f), 32f, ClosePanel);
            CreateTopRightButton("CopyButton", "Copy", new Vector2(-48f, -8f), 72f, CopyLogsToClipboard);
            CreateTopRightButton("ClearButton", "Clear", new Vector2(-126f, -8f), 72f, ClearLogs);

            _panel.SetActive(RuntimeLoggingSettings.ShowRuntimeLogOverlayOnStart);
        }

        private void CreateTopRightButton(string objectName, string labelText, Vector2 anchoredPosition, float width, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(_panel.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 1f);
            buttonRect.sizeDelta = new Vector2(width, 32f);
            buttonRect.anchoredPosition = anchoredPosition;

            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(1f, 1f, 1f, 0.16f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(onClick);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = labelText;
            label.fontSize = 16f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        private void InitializeLogFile()
        {
            try
            {
                _logFilePath = RuntimeLoggingSettings.ResolveRuntimeLogPath();
                File.WriteAllText(
                    _logFilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Runtime log started. pid={System.Diagnostics.Process.GetCurrentProcess().Id}{Environment.NewLine}");
                AppendLine($"Log file: {_logFilePath}");
            }
            catch (Exception e)
            {
                _logFilePath = string.Empty;
                AppendLine($"[WARN] Failed to initialize log file: {e.Message}");
            }
        }

        private void OnUnityLogReceived(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning && IsFontGlyphWarning(condition))
            {
                return;
            }

            var level = type switch
            {
                LogType.Warning => "WARN",
                LogType.Error => "ERROR",
                LogType.Exception => "EXCEPTION",
                LogType.Assert => "ASSERT",
                _ => "INFO"
            };

            var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {condition}";
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                var trace = (stackTrace ?? string.Empty).Split('\n').FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(trace))
                {
                    line = $"{line}\n  {trace.Trim()}";
                }
            }

            AppendLine(line);
        }

        private static bool IsFontGlyphWarning(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            return message.Contains("was not found in the [", StringComparison.Ordinal)
                   && message.Contains("font asset or any potential fallbacks", StringComparison.Ordinal);
        }

        private void AppendLine(string line)
        {
            if (!string.IsNullOrEmpty(_logFilePath))
            {
                try
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
                catch
                {
                }
            }

            _lines.Enqueue(line);
            while (_lines.Count > MaxLines)
            {
                _lines.Dequeue();
            }

            if (_text != null)
            {
                _uiDirty = true;
            }
        }

        private void FlushPendingUi()
        {
            if (!_uiDirty || _text == null)
            {
                return;
            }

            _text.text = string.Join("\n", _lines);
            if (_textRect != null && _scrollRect != null)
            {
                var viewportWidth = _scrollRect.viewport != null ? _scrollRect.viewport.rect.width : _textRect.rect.width;
                var preferredHeight = _text.GetPreferredValues(_text.text, viewportWidth, 0f).y;
                _textRect.sizeDelta = new Vector2(0f, preferredHeight);

                var contentRect = _scrollRect.content;
                if (contentRect != null)
                {
                    contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, preferredHeight);
                }

                Canvas.ForceUpdateCanvases();
                _scrollRect.verticalNormalizedPosition = 0f;
            }

            _uiDirty = false;
        }

        private void TogglePanel()
        {
            if (_panel == null)
            {
                return;
            }

            _panel.SetActive(!_panel.activeSelf);
            if (_panel.activeSelf)
            {
                AppendLine("Runtime log overlay shown.");
            }
        }

        private void ClosePanel()
        {
            if (_panel != null)
            {
                _panel.SetActive(false);
            }
        }

        private void CopyLogsToClipboard()
        {
            var content = _lines.Count == 0 ? string.Empty : string.Join("\n", _lines);
            GUIUtility.systemCopyBuffer = content;
            AppendLine($"[{DateTime.Now:HH:mm:ss}] [INFO] Logs copied to clipboard.");
        }

        private void ClearLogs()
        {
            _lines.Clear();
            if (_text != null)
            {
                _text.text = string.Empty;
            }

            if (!string.IsNullOrEmpty(_logFilePath))
            {
                try
                {
                    File.WriteAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Runtime log cleared.{Environment.NewLine}");
                }
                catch
                {
                }
            }

            AppendLine($"[{DateTime.Now:HH:mm:ss}] [INFO] Logs cleared.");
        }
    }
}
