#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PlayModeTimeSettingsGuard
{
    private const float DefaultTimeScale = 1f;
    private const float DefaultFixedDeltaTime = 0.02f;

    static PlayModeTimeSettingsGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode ||
            change == PlayModeStateChange.EnteredEditMode)
        {
            RestoreDefaults();
        }
    }

    private static void RestoreDefaults()
    {
        Time.timeScale = DefaultTimeScale;
        Time.fixedDeltaTime = DefaultFixedDeltaTime;
    }
}
#endif
