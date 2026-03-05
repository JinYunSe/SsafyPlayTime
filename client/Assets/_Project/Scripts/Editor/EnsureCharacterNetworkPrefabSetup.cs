using System;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEngine;

namespace SSAFYPlayTime.Editor
{
    public static class EnsureCharacterNetworkPrefabSetup
    {
        private const string SessionKey = "SSAFYPlayTime.CharacterNetworkPrefabsEnsured";

        private static readonly string[] CharacterPrefabPaths =
        {
            "Assets/_Project/Prefabs/Characters/AiJiCharacter.prefab",
            "Assets/_Project/Prefabs/Characters/PitCharacter.prefab",
            "Assets/_Project/Prefabs/Characters/SeuTatiCharacter.prefab",
            "Assets/_Project/Prefabs/Characters/WaiJeuCharacter.prefab"
        };

        [InitializeOnLoadMethod]
        private static void EnsureOnEditorLoad()
        {
            // Avoid repeated writes during the same editor session.
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    EnsurePrefabs();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NetworkPrefabSetup] Auto ensure failed: {e.Message}");
                }
            };
        }

        [MenuItem("Tools/Network/Ensure Character Network Prefabs")]
        private static void EnsurePrefabs()
        {
            var changedCount = 0;
            foreach (var path in CharacterPrefabPaths)
            {
                var prefabRoot = PrefabUtility.LoadPrefabContents(path);
                if (prefabRoot == null)
                {
                    continue;
                }

                var changed = false;

                if (prefabRoot.GetComponent<NetworkObject>() == null)
                {
                    prefabRoot.AddComponent<NetworkObject>();
                    changed = true;
                }

                if (prefabRoot.GetComponent<NetworkTransform>() == null)
                {
                    prefabRoot.AddComponent<NetworkTransform>();
                    changed = true;
                }

                // 자식 Rigidbody(래그돌 부위)에도 NetworkTransform 추가
                var childRigidbodies = prefabRoot.GetComponentsInChildren<Rigidbody>()
                    .Where(rb => rb.gameObject != prefabRoot)
                    .ToList();

                foreach (var rb in childRigidbodies)
                {
                    if (rb.GetComponent<NetworkTransform>() == null)
                    {
                        rb.gameObject.AddComponent<NetworkTransform>();
                        changed = true;
                    }
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    changedCount++;
                    Debug.Log($"[NetworkPrefabSetup] Updated: {path}");
                }

                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            if (changedCount > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[NetworkPrefabSetup] Completed. Updated {changedCount} prefab(s).");
            }
            else
            {
                Debug.Log("[NetworkPrefabSetup] Completed. No changes needed.");
            }
        }
    }
}
