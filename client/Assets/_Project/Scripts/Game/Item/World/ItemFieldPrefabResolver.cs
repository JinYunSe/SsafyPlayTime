/*
 * 파일 개요:
 * - ItemFieldPrefabResolver 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime.Gameplay.Items
{
    public interface IItemFieldPrefabResolver
    {
        GameObject Resolve(string prefabPath);
    }

    /// <summary>
    /// 아이템 테이블의 프리팹 경로를 실제 프리팹으로 변환한다.
    /// </summary>
    public sealed class DefaultItemFieldPrefabResolver : IItemFieldPrefabResolver
    {
        public GameObject Resolve(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                return null;
            }

#if UNITY_EDITOR
            if (prefabPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    return prefab;
                }
            }
#endif
            var resourcePath = NormalizeToResourcesPath(prefabPath);
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            return Resources.Load<GameObject>(resourcePath);
        }

        private static string NormalizeToResourcesPath(string path)
        {
            var normalized = path.Replace('\\', '/').Trim();
            const string assetsResourcesPrefix = "Assets/Resources/";
            if (normalized.StartsWith(assetsResourcesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(assetsResourcesPrefix.Length);
            }
            else if (normalized.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Resources/".Length);
            }
            else
            {
                return string.Empty;
            }

            var extension = Path.GetExtension(normalized);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                normalized = normalized.Substring(0, normalized.Length - extension.Length);
            }

            return normalized;
        }
    }
}

