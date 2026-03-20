using System;
using System.IO;
using UnityEngine;

internal static class MoveSyncDiagnostics
{
    private const KeyCode ToggleKey = KeyCode.F7;

    private static readonly object s_fileLock = new();
    private static bool s_initialized;
    private static bool s_enabled = true;
    private static string s_logPath;

    internal static bool Enabled
    {
        get
        {
            EnsureInitialized();
            return s_enabled;
        }
    }

    internal static void UpdateHotkey()
    {
        if (!Application.isPlaying)
            return;

        EnsureInitialized();

        if (!Input.GetKeyDown(ToggleKey))
            return;

        s_enabled = !s_enabled;
        WriteLine($"[MoveDiag:System] enabled={(s_enabled ? 1 : 0)}");
    }

    internal static void Emit(string message, UnityEngine.Object context = null, bool logToConsole = false)
    {
        EnsureInitialized();

        if (!s_enabled)
            return;

        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        WriteLine(line);

        if (logToConsole)
            Debug.Log(line, context);
    }

    internal static string FormatVector2(Vector2 value)
    {
        return $"({value.x:F2},{value.y:F2})";
    }

    private static void EnsureInitialized()
    {
        if (s_initialized)
            return;

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var logDirectory = Path.Combine(desktop, "MoveSyncLogs");
        Directory.CreateDirectory(logDirectory);

        s_logPath = Path.Combine(logDirectory, $"move-sync-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        s_initialized = true;

        WriteLine($"[MoveDiag:System] path={s_logPath}");
        WriteLine($"[MoveDiag:System] enabled={(s_enabled ? 1 : 0)}");
    }

    private static void WriteLine(string line)
    {
        if (string.IsNullOrEmpty(s_logPath))
            return;

        lock (s_fileLock)
        {
            File.AppendAllText(s_logPath, line + Environment.NewLine);
        }
    }
}
