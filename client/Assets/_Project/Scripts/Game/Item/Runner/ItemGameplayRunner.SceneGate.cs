using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapItemSceneRunner()
        {
            if (!IsItemRuntimeScene(SceneManager.GetActiveScene()))
            {
                return;
            }

            var legacyRoot = GameObject.Find("ItemPrototypeHotkeyRunner");
            var root = GameObject.Find("ItemGameplayRunner");
            if (root == null && legacyRoot != null)
            {
                root = legacyRoot;
            }

            if (root == null)
            {
                root = new GameObject("ItemGameplayRunner");
            }

            DisableLegacyPrototypeRunner(legacyRoot);

            if (root.GetComponent<ItemRuntimeHost>() == null)
            {
                root.AddComponent<ItemRuntimeHost>();
            }

            if (root.GetComponent<ItemGameplayRunner>() == null)
            {
                root.AddComponent<ItemGameplayRunner>();
            }
        }

        private bool ShouldRunInCurrentScene()
        {
            return !runOnlyInItemScene || IsItemRuntimeScene(gameObject.scene);
        }

        private static bool IsItemRuntimeScene(Scene scene)
        {
            if (!scene.IsValid())
            {
                return false;
            }

            if (string.Equals(scene.name, RuntimeSceneName, StringComparison.Ordinal))
            {
                return true;
            }

            var scenePath = scene.path ?? string.Empty;
            return scenePath.EndsWith("/ItemScene.unity", StringComparison.OrdinalIgnoreCase) ||
                   scenePath.EndsWith("\\ItemScene.unity", StringComparison.OrdinalIgnoreCase);
        }

        private static void DisableLegacyPrototypeRunner(GameObject legacyRoot)
        {
            if (legacyRoot == null)
            {
                return;
            }

            var components = legacyRoot.GetComponents<MonoBehaviour>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (!string.Equals(component.GetType().Name, "ItemPrototypeHotkeyRunner", StringComparison.Ordinal))
                {
                    continue;
                }

                // ItemScene 전환 중 중복 입력/중복 스폰을 막기 위해 기존 프로토타입 러너를 비활성화한다.
                component.enabled = false;
                return;
            }
        }
    }
}
