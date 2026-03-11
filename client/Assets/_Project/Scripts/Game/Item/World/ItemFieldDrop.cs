/*
 * 파일 개요:
 * - ItemFieldDrop 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
using System;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 필드에 떨어진 아이템 오브젝트의 식별 정보와 획득 상태를 관리한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemFieldDrop : MonoBehaviour
    {
        [SerializeField] private string itemId = string.Empty;
        [SerializeField] private string instanceId = string.Empty;
        [SerializeField] private bool destroyOnPickup = true;
        [SerializeField] private bool disableCollidersOnPickup = true;
        [SerializeField, HideInInspector] private bool runtimeInitialized;

        private bool _pickedUp;

        public string ItemId => itemId;
        public string InstanceId => instanceId;
        public bool IsPickedUp => _pickedUp;
        public event Action<ItemFieldDrop> PickedUp;

        private void Awake()
        {
            EnsureInstanceId();
            EnsureRuntimeSetup();
        }

        public void SetItemId(string value)
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(itemId, normalized, StringComparison.Ordinal))
            {
                return;
            }

            itemId = normalized;
            runtimeInitialized = false;
        }

        public void SetInstanceId(string value)
        {
            instanceId = string.IsNullOrWhiteSpace(value)
                ? BuildDeterministicInstanceId()
                : value;
        }

        public bool CanBePickedUp()
        {
            return !_pickedUp && !string.IsNullOrWhiteSpace(itemId);
        }

        internal void EnsureRuntimeSetup()
        {
            if (runtimeInitialized)
            {
                return;
            }

            ItemFieldDropFactory.ApplyFieldDropRuntimeSetup(gameObject, itemId);
            runtimeInitialized = true;
        }

        public void MarkPickedUp()
        {
            if (_pickedUp)
            {
                return;
            }

            _pickedUp = true;
            PickedUp?.Invoke(this);
            if (disableCollidersOnPickup)
            {
                var colliders = GetComponentsInChildren<Collider>(true);
                for (var i = 0; i < colliders.Length; i++)
                {
                    colliders[i].enabled = false;
                }
            }

            if (destroyOnPickup)
            {
                Destroy(gameObject);
                return;
            }

            gameObject.SetActive(false);
        }

        private void EnsureInstanceId()
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                return;
            }

            instanceId = BuildDeterministicInstanceId();
        }

        private string BuildDeterministicInstanceId()
        {
            var scenePath = gameObject.scene.IsValid() ? gameObject.scene.path : string.Empty;
            var hierarchyPath = BuildHierarchyPath(transform);
            if (!string.IsNullOrWhiteSpace(scenePath) && !string.IsNullOrWhiteSpace(hierarchyPath))
            {
                return $"{scenePath}:{hierarchyPath}";
            }

            return Guid.NewGuid().ToString("N");
        }

        private static string BuildHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            var path = target.name;
            var current = target.parent;
            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }
    }
}

