using System;
using System.IO;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace SSAFYPlayTime
{
    /// <summary>
    /// Central runtime logging switch.
    /// Keep runtime logging disabled to avoid log spam and the overlay/file-write overhead in play mode.
    /// </summary>
    public static class RuntimeLoggingSettings
    {
        public const bool EnableRuntimeLogs = false;
        public const bool ShowRuntimeLogOverlayOnStart = false;

        private static readonly object TargetedLogLock = new();
        private static string _runtimeLogPath;
        private static string _targetedLogPath;

        public static bool AreRuntimeLogsEnabled => EnableRuntimeLogs;

        public static string ResolveRuntimeLogPath()
        {
            if (!string.IsNullOrWhiteSpace(_runtimeLogPath))
                return _runtimeLogPath;

            var processId = Process.GetCurrentProcess().Id;
            _runtimeLogPath = Path.Combine(Application.persistentDataPath, $"runtime-log-{processId}.txt");
            return _runtimeLogPath;
        }

        public static void AppendTargetedRuntimeLine(string line)
        {
            if (!AreRuntimeLogsEnabled || string.IsNullOrWhiteSpace(line))
                return;

            lock (TargetedLogLock)
            {
                try
                {
                    _targetedLogPath ??= ResolveRuntimeLogPath();
                    File.AppendAllText(_targetedLogPath, line + Environment.NewLine);
                }
                catch
                {
                    // Keep targeted diagnostics best-effort only.
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (AreRuntimeLogsEnabled)
            {
                return;
            }

            Debug.unityLogger.logEnabled = false;
            Debug.developerConsoleEnabled = false;

            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.None);
        }
    }
}
