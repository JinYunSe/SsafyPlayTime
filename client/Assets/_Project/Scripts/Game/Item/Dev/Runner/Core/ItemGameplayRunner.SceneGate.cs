/*
 * 파일 개요:
 * - ItemGameplayRunner.SceneGate 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Core 계층에서 ItemGameplayRunner의 생명주기, 입력, 이벤트 연결 같은 중심 흐름을 관리한다.
 * - 테스트용 실행 경로를 정리하는 파일이므로, 실게임 로직과 분리된 검증 허브라는 성격을 유지해야 한다.
 */
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

            if (root.GetComponent<ItemFieldSystemBootstrap>() == null)
            {
                root.AddComponent<ItemFieldSystemBootstrap>();
            }

            if (root.GetComponent<ItemFieldInteractionService>() == null)
            {
                root.AddComponent<ItemFieldInteractionService>();
            }

            if (root.GetComponent<ItemFieldDropDevTestInput>() == null)
            {
                root.AddComponent<ItemFieldDropDevTestInput>();
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

