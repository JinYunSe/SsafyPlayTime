using System.Collections.Generic;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 한국어: 빌드 로그에서 아이템 흐름을 빠르게 추적하기 위한 공용 로그 유틸리티다.
    /// </summary>
    internal static class ItemRuntimeLog
    {
        private static readonly HashSet<string> LoggedKeys = new();

        internal static void Info(string category, string message, Object context = null)
        {
            if (!SSAFYPlayTime.RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                return;
            }

            Debug.Log(Format(category, message), context);
        }

        internal static void Warn(string category, string message, Object context = null)
        {
            if (!SSAFYPlayTime.RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                return;
            }

            Debug.LogWarning(Format(category, message), context);
        }

        internal static void Error(string category, string message, Object context = null)
        {
            if (!SSAFYPlayTime.RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                return;
            }

            Debug.LogError(Format(category, message), context);
        }

        internal static void InfoOnce(string key, string category, string message, Object context = null)
        {
            if (!SSAFYPlayTime.RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                return;
            }

            if (!LoggedKeys.Add($"I:{key}"))
            {
                return;
            }

            Info(category, message, context);
        }

        internal static void WarnOnce(string key, string category, string message, Object context = null)
        {
            if (!SSAFYPlayTime.RuntimeLoggingSettings.AreRuntimeLogsEnabled)
            {
                return;
            }

            if (!LoggedKeys.Add($"W:{key}"))
            {
                return;
            }

            Warn(category, message, context);
        }

        private static string Format(string category, string message)
        {
            var safeCategory = string.IsNullOrWhiteSpace(category) ? "General" : category;
            var safeMessage = string.IsNullOrWhiteSpace(message) ? "(empty)" : message;
            return $"[ItemRuntime][{safeCategory}] {safeMessage}";
        }
    }
}
