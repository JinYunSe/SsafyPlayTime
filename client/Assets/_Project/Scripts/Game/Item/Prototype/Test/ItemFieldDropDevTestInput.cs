using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 개발 테스트 입력만 담당하며 실게임 로직은 서비스로 위임한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemFieldDropDevTestInput : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemFieldInteractionService fieldInteractionService;

        [Header("입력")]
        [SerializeField] private bool enableTestInput = true;
        [SerializeField] private KeyCode spawnBlackholeKey = KeyCode.Alpha1;
        [SerializeField] private KeyCode spawnSatelliteStrikeKey = KeyCode.Alpha2;
        [SerializeField] private KeyCode useItemKey = KeyCode.None;
        [SerializeField] private KeyCode dropItemKey = KeyCode.None;

        [Header("테스트 설정")]
        [SerializeField] private string spawnItemId = ItemIds.BlackholeBomb;
        [SerializeField] private string spawnSatelliteStrikeItemId = ItemIds.SatelliteStrike;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog = true;

        private void Awake()
        {
            ResolveReferences();
            DisableConflictingKeysForNetworkPlayer();
        }

        private void Update()
        {
            if (!enableTestInput)
            {
                return;
            }

            if (Input.GetKeyDown(spawnBlackholeKey))
            {
                HandleSpawnInput(spawnItemId);
            }

            if (Input.GetKeyDown(spawnSatelliteStrikeKey))
            {
                HandleSpawnInput(spawnSatelliteStrikeItemId);
            }

            // 실제 플레이어 입력과 충돌하지 않도록 기본값(None)일 때는 무시한다.
            if (useItemKey != KeyCode.None && Input.GetKeyDown(useItemKey))
            {
                HandleUseInput();
            }

            // 실제 플레이어 입력과 충돌하지 않도록 기본값(None)일 때는 무시한다.
            if (dropItemKey != KeyCode.None && Input.GetKeyDown(dropItemKey))
            {
                HandleDropInput();
            }
        }

        private void HandleSpawnInput(string itemId)
        {
            if (!ResolveReferences())
            {
                return;
            }

            if (fieldInteractionService.TrySpawnItemInFront(itemId, out _, out var reason))
            {
                DebugLog($"Spawn request succeeded: {itemId}");
                return;
            }

            DebugLog($"Spawn request failed: {reason}");
        }

        private void HandleUseInput()
        {
            if (!ResolveReferences())
            {
                return;
            }

            if (fieldInteractionService.TryUseHeldItem(out var usedItemId, out var useReason))
            {
                DebugLog($"Use succeeded: {usedItemId}");
                return;
            }

            DebugLog($"Use failed: {useReason}");
        }

        private void HandleDropInput()
        {
            if (!ResolveReferences())
            {
                return;
            }

            if (fieldInteractionService.TryDropHeldItem(out var droppedItemId, out var dropReason))
            {
                DebugLog($"Drop succeeded: {droppedItemId}");
                return;
            }

            DebugLog($"Drop failed: {dropReason}");
        }

        private bool ResolveReferences()
        {
            if (fieldInteractionService != null)
            {
                return true;
            }

            fieldInteractionService = GetComponent<ItemFieldInteractionService>();
            if (fieldInteractionService != null)
            {
                return true;
            }

            fieldInteractionService = FindObjectOfType<ItemFieldInteractionService>(true);
            return fieldInteractionService != null;
        }

        private void DisableConflictingKeysForNetworkPlayer()
        {
            // 네트워크 플레이어가 있는 씬에서는 사용/드롭 테스트 키를 강제로 비활성화한다.
            if (FindObjectOfType<NetworkPlayer>(true) == null)
            {
                return;
            }

            useItemKey = KeyCode.None;
            dropItemKey = KeyCode.None;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemFieldDropDevTestInput] {message}", this);
        }
    }
}
