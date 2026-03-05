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
        [SerializeField] private KeyCode interactKey = KeyCode.Mouse1;
        [SerializeField] private KeyCode dropItemKey = KeyCode.F;

        [Header("테스트 설정")]
        [SerializeField] private string spawnItemId = ItemIds.BlackholeBomb;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog = true;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (!enableTestInput)
            {
                return;
            }

            if (Input.GetKeyDown(spawnBlackholeKey))
            {
                HandleSpawnInput();
            }

            if (Input.GetKeyDown(interactKey))
            {
                HandleInteractInput();
            }

            if (Input.GetKeyDown(dropItemKey))
            {
                HandleDropInput();
            }
        }

        private void HandleSpawnInput()
        {
            if (!ResolveReferences())
            {
                return;
            }

            if (fieldInteractionService.TrySpawnItemInFront(spawnItemId, out _, out var reason))
            {
                DebugLog($"Spawn request succeeded: {spawnItemId}");
                return;
            }

            DebugLog($"Spawn request failed: {reason}");
        }

        private void HandleInteractInput()
        {
            if (!ResolveReferences())
            {
                return;
            }

            if (fieldInteractionService.TryPickupNearest(out var pickedItemId, out var pickupReason))
            {
                DebugLog($"Pickup succeeded: {pickedItemId}");
                return;
            }

            if (fieldInteractionService.TryUseHeldItem(out var usedItemId, out var useReason))
            {
                DebugLog($"Use succeeded: {usedItemId}");
                return;
            }

            DebugLog($"Interact failed (pickup: {pickupReason}, use: {useReason})");
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
