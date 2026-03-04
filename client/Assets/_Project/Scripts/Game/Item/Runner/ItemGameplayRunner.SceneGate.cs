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

            var root = GameObject.Find("ItemGameplayRunner");
            if (root == null)
            {
                root = new GameObject("ItemGameplayRunner");
            }

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
    }
}
