using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 다른 팀원의 캐릭터 프리팹을 수정하지 않고 아이템 런타임을 자동 결합한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemCharacterAutoAttachBootstrap : MonoBehaviour
    {
        private const string BootstrapObjectName = "ItemCharacterAutoAttachBootstrap";

        [Header("동작")]
        [SerializeField] private bool runInItemScene = false;
        [SerializeField] private float bindRetryIntervalSec = 0.5f;
        [SerializeField] private float bindTimeoutSec = 15f;

        [Header("입력 연결")]
        [SerializeField] private bool enableLegacyPickupInput = true;
        [SerializeField] private bool enableLegacyUseInput = true;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog;

        private Coroutine _bindRoutine;
        private int _boundRootId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var existing = FindObjectOfType<ItemCharacterAutoAttachBootstrap>();
            if (existing != null)
            {
                return;
            }

            var go = new GameObject(BootstrapObjectName);
            DontDestroyOnLoad(go);
            go.AddComponent<ItemCharacterAutoAttachBootstrap>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            RestartBindRoutine();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _boundRootId = 0;
            RestartBindRoutine();
        }

        private void RestartBindRoutine()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
            }

            _bindRoutine = StartCoroutine(CoBindLocalCharacter());
        }

        private IEnumerator CoBindLocalCharacter()
        {
            var elapsed = 0f;
            while (elapsed < Mathf.Max(1f, bindTimeoutSec))
            {
                if (ShouldSkipCurrentScene())
                {
                    yield break;
                }

                if (TryFindLocalCharacterRoot(out var localCharacterRoot))
                {
                    BindToCharacter(localCharacterRoot);
                    yield break;
                }

                yield return new WaitForSeconds(Mathf.Max(0.05f, bindRetryIntervalSec));
                elapsed += Mathf.Max(0.05f, bindRetryIntervalSec);
            }
        }

        private bool ShouldSkipCurrentScene()
        {
            if (runInItemScene)
            {
                return false;
            }

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return false;
            }

            if (string.Equals(activeScene.name, "ItemScene", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var path = activeScene.path ?? string.Empty;
            return path.EndsWith("/ItemScene.unity", System.StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("\\ItemScene.unity", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindLocalCharacterRoot(out GameObject root)
        {
            root = null;
            var players = FindObjectsOfType<NetworkPlayer>(true);
            var bestScore = -1;
            for (var i = 0; i < players.Length; i++)
            {
                var candidate = players[i];
                if (candidate == null)
                {
                    continue;
                }

                var candidateRoot = candidate.gameObject;
                var networkObject = candidateRoot.GetComponent<NetworkObject>();
                var score = 0;
                if (networkObject == null)
                {
                    score = 1;
                }
                else if (networkObject.HasInputAuthority && networkObject.HasStateAuthority)
                {
                    score = 4;
                }
                else if (networkObject.HasInputAuthority)
                {
                    score = 3;
                }
                else if (networkObject.HasStateAuthority)
                {
                    score = 2;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    root = candidateRoot;
                }
            }

            return root != null && bestScore >= 1;
        }

        private void BindToCharacter(GameObject characterRootObject)
        {
            if (characterRootObject == null)
            {
                return;
            }

            if (_boundRootId == characterRootObject.GetInstanceID())
            {
                return;
            }

            _boundRootId = characterRootObject.GetInstanceID();

            var host = characterRootObject.GetComponent<ItemRuntimeHost>();
            if (host == null)
            {
                host = characterRootObject.AddComponent<ItemRuntimeHost>();
            }
            host.SetOwnerTransform(characterRootObject.transform);

            var pickup = characterRootObject.GetComponent<ItemFieldPickupInteractor>();
            if (pickup == null)
            {
                pickup = characterRootObject.AddComponent<ItemFieldPickupInteractor>();
            }
            pickup.SetRuntimeHost(host);
            pickup.SetInteractorRoot(characterRootObject.transform);
            pickup.SetUseLegacyInput(enableLegacyPickupInput);

            var useInteractor = characterRootObject.GetComponent<ItemCharacterUseInteractor>();
            if (useInteractor == null)
            {
                useInteractor = characterRootObject.AddComponent<ItemCharacterUseInteractor>();
            }
            useInteractor.SetRuntimeHost(host);
            useInteractor.SetOwnerRoot(characterRootObject.transform);
            useInteractor.SetUseLegacyInput(enableLegacyUseInput);

            var heldPresenter = characterRootObject.GetComponent<ItemCharacterHeldItemPresenter>();
            if (heldPresenter == null)
            {
                heldPresenter = characterRootObject.AddComponent<ItemCharacterHeldItemPresenter>();
            }
            heldPresenter.SetRuntimeHost(host);
            heldPresenter.SetCharacterRoot(characterRootObject.transform);

            var meleeSwingHandler = characterRootObject.GetComponent<ItemCharacterMeleeSwingHandler>();
            if (meleeSwingHandler == null)
            {
                meleeSwingHandler = characterRootObject.AddComponent<ItemCharacterMeleeSwingHandler>();
            }
            meleeSwingHandler.SetRuntimeHost(host);
            meleeSwingHandler.SetHeldItemPresenter(heldPresenter);
            meleeSwingHandler.SetOwnerRoot(characterRootObject.transform);

            var buffApplier = characterRootObject.GetComponent<ItemCharacterBuffApplier>();
            if (buffApplier == null)
            {
                buffApplier = characterRootObject.AddComponent<ItemCharacterBuffApplier>();
            }
            buffApplier.SetRuntimeHost(host);
            buffApplier.SetCharacterRoot(characterRootObject.transform);

            DebugLog($"Bound item runtime to character: {characterRootObject.name}");
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemCharacterAutoAttachBootstrap] {message}", this);
        }
    }
}
