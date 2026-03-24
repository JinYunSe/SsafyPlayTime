using UnityEngine;

internal static class MoveSyncDiagnostics
{
    internal static bool Enabled => false;

    internal static void UpdateHotkey()
    {
    }

    internal static void Emit(string message, UnityEngine.Object context = null, bool logToConsole = false)
    {
    }

    internal static string FormatVector2(Vector2 value)
    {
        return $"({value.x:F2},{value.y:F2})";
    }
}
