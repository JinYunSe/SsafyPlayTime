using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SSAFYPlayTime.EditorTools
{
    public static class SystemReportBuilder
    {
        [MenuItem("Tools/Report/Build System Markdown")]
        public static void Build()
        {
            const string root = "Assets/_Project/Scripts";
            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine("# 시스템 코드 리포트 (자동 생성)");
            sb.AppendLine();
            sb.AppendLine($"- 생성 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- 대상 루트: `{root}`");
            sb.AppendLine($"- 파일 수: {files.Length}");
            sb.AppendLine();
            sb.AppendLine("## 파일 목록");

            foreach (var file in files)
            {
                var rel = file.Replace('\\', '/');
                var lines = File.ReadAllLines(file).Length;
                sb.AppendLine($"- `{rel}` ({lines} lines)");
            }

            sb.AppendLine();
            sb.AppendLine("## 클래스/인터페이스 키워드 스캔");
            foreach (var file in files)
            {
                var rel = file.Replace('\\', '/');
                var text = File.ReadAllText(file);
                if (!text.Contains("class ") && !text.Contains("interface ") && !text.Contains("enum "))
                    continue;

                sb.AppendLine($"### `{rel}`");
                foreach (var line in File.ReadLines(file))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains(" class ") || trimmed.StartsWith("class ")
                        || trimmed.Contains(" interface ") || trimmed.StartsWith("interface ")
                        || trimmed.Contains(" enum ") || trimmed.StartsWith("enum "))
                    {
                        sb.AppendLine($"- `{trimmed}`");
                    }
                }
            }

            var output = "Assets/_Project/SystemReport.md";
            File.WriteAllText(output, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log($"[SystemReport] Generated: {output}");
        }
    }
}
