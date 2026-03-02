using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SSAFYPlayTime.EditorTools
{
    public static class MissingScriptScanner
    {
        [MenuItem("Tools/Diagnostics/Find Missing Scripts In Open Scenes")]
        public static void FindMissingScriptsInOpenScenes()
        {
            var totalMissing = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    totalMissing += ScanRecursive(root, scene.name);
                }
            }

            if (totalMissing == 0)
            {
                Debug.Log("[MissingScriptScanner] No missing scripts found in open scenes.");
            }
            else
            {
                Debug.LogWarning($"[MissingScriptScanner] Found {totalMissing} missing script components.");
            }
        }

        [MenuItem("Tools/Diagnostics/Remove Missing Scripts In Open Scenes")]
        public static void RemoveMissingScriptsInOpenScenes()
        {
            var removedTotal = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var removedInScene = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    removedInScene += RemoveRecursive(root);
                }

                if (removedInScene > 0)
                {
                    removedTotal += removedInScene;
                    EditorSceneManager.MarkSceneDirty(scene);
                    Debug.LogWarning($"[MissingScriptScanner] Removed {removedInScene} missing script components in scene '{scene.name}'.");
                }
            }

            if (removedTotal == 0)
            {
                Debug.Log("[MissingScriptScanner] No missing scripts to remove.");
            }
            else
            {
                Debug.LogWarning($"[MissingScriptScanner] Removed {removedTotal} missing script components in total.");
            }
        }

        private static int ScanRecursive(GameObject go, string sceneName)
        {
            var found = 0;
            var missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missingCount > 0)
            {
                found += missingCount;
                Debug.LogWarning(
                    $"[MissingScriptScanner] Scene={sceneName}, Object={BuildPath(go.transform)}, MissingCount={missingCount}",
                    go);
            }

            foreach (Transform child in go.transform)
            {
                found += ScanRecursive(child.gameObject, sceneName);
            }

            return found;
        }

        private static int RemoveRecursive(GameObject go)
        {
            var removed = 0;
            var missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missingCount > 0)
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                removed += missingCount;
            }

            foreach (Transform child in go.transform)
            {
                removed += RemoveRecursive(child.gameObject);
            }

            return removed;
        }

        private static string BuildPath(Transform transform)
        {
            var stack = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack);
        }
    }
}
