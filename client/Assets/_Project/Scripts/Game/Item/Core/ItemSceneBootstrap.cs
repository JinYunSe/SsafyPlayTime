/*
 * 파일 개요:
 * - ItemSceneBootstrap 스크립트가 들어 있는 파일이다.
 * - 다른 씬에서 아이템 시스템을 붙일 때 씬 공용 러너와 로컬 플레이어 런타임을 연결하는 책임을 맡는다.
 * - 플레이어별 ItemRuntimeHost 상태는 건드리지 않고, 씬 레벨 처리만 연결하는 허브로 유지해야 한다.
 */
using System.Collections;
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 씬에 배치한 아이템 프리팹이 다른 씬에서도 동작하도록 공용 러너를 연결한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemSceneBootstrap : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemGameplayRunner itemGameplayRunner;

        [Header("입력")]
        [SerializeField] private bool enableRightClickPickup = true;
        [SerializeField] private KeyCode pickupKey = KeyCode.Mouse1;

        [Header("바인딩")]
        [SerializeField] private float bindRetryIntervalSec = 0.5f;
        [SerializeField] private float bindTimeoutSec = 15f;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog;

        private Coroutine _bindRoutine;
        private int _boundRuntimeHostId;

        private void Awake()
        {
            EnsureReferences();
            ApplyRunnerDefaults();
        }

        private void OnEnable()
        {
            RestartBindRoutine();
        }

        private void OnDisable()
        {
            if (_bindRoutine == null)
            {
                return;
            }

            StopCoroutine(_bindRoutine);
            _bindRoutine = null;
        }

        public void SetGameplayRunner(ItemGameplayRunner gameplayRunner)
        {
            itemGameplayRunner = gameplayRunner;
            EnsureReferences();
            ApplyRunnerDefaults();
        }

        private void RestartBindRoutine()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
            }

            _bindRoutine = StartCoroutine(CoBindRuntimeHost());
        }

        private IEnumerator CoBindRuntimeHost()
        {
            var retrySec = Mathf.Max(0.05f, bindRetryIntervalSec);
            var elapsed = 0f;
            while (elapsed < Mathf.Max(1f, bindTimeoutSec))
            {
                EnsureReferences();
                ApplyRunnerDefaults();

                if (TryFindBestRuntimeHost(out var runtimeHost))
                {
                    BindRuntimeHost(runtimeHost);
                    yield break;
                }

                yield return new WaitForSeconds(retrySec);
                elapsed += retrySec;
            }

            DebugLog("Runtime host binding timed out.");
        }

        private void EnsureReferences()
        {
            if (itemGameplayRunner == null)
            {
                itemGameplayRunner = GetComponent<ItemGameplayRunner>();
            }

            if (itemGameplayRunner == null)
            {
                itemGameplayRunner = gameObject.AddComponent<ItemGameplayRunner>();
            }
        }

        private void ApplyRunnerDefaults()
        {
            if (itemGameplayRunner == null)
            {
                return;
            }

            // 한국어: 다른 씬에서도 동작해야 하므로 ItemScene 전용 제한을 끈다.
            itemGameplayRunner.SetRunOnlyInItemScene(false);
            // 한국어: 다른 씬에서는 개발용 더미 조작기를 꺼서 실제 플레이어 입력과 충돌하지 않게 한다.
            itemGameplayRunner.SetLocalDebugControllerEnabled(false);
            // 한국어: 비어 있는 손 상태에서는 우클릭으로 픽업, 들고 있을 때는 기존 던지기 입력을 유지한다.
            itemGameplayRunner.SetTemporaryPickupInput(enableRightClickPickup, pickupKey);
        }

        private void BindRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            if (runtimeHost == null)
            {
                return;
            }

            var hostId = runtimeHost.GetInstanceID();
            if (_boundRuntimeHostId == hostId)
            {
                return;
            }

            _boundRuntimeHostId = hostId;
            itemGameplayRunner.SetRuntimeHost(runtimeHost);
            DebugLog($"Bound runtime host: {runtimeHost.name}");
        }

        private static bool TryFindBestRuntimeHost(out ItemRuntimeHost runtimeHost)
        {
            runtimeHost = null;

            var bestScore = int.MinValue;
            var players = FindObjectsOfType<NetworkPlayer>(true);
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null)
                {
                    continue;
                }

                var host = player.GetComponent<ItemRuntimeHost>();
                if (host == null)
                {
                    continue;
                }

                var score = ScorePlayer(player);
                if (score > bestScore)
                {
                    bestScore = score;
                    runtimeHost = host;
                }
            }

            if (runtimeHost != null)
            {
                return true;
            }

            var hosts = FindObjectsOfType<ItemRuntimeHost>(true);
            for (var i = 0; i < hosts.Length; i++)
            {
                var candidate = hosts[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.GetComponent<ItemGameplayRunner>() != null)
                {
                    continue;
                }

                runtimeHost = candidate;
                return true;
            }

            return false;
        }

        private static int ScorePlayer(NetworkPlayer player)
        {
            var networkObject = player.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                return 1;
            }

            if (networkObject.HasInputAuthority && networkObject.HasStateAuthority)
            {
                return 4;
            }

            if (networkObject.HasInputAuthority)
            {
                return 3;
            }

            if (networkObject.HasStateAuthority)
            {
                return 2;
            }

            return 0;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemSceneBootstrap] {message}", this);
        }
    }
}
