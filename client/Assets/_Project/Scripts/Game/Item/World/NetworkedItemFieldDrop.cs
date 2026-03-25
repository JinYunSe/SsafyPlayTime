using Fusion;
using UnityEngine;
using System.Collections.Generic;

namespace SSAFYPlayTime.Gameplay.Items
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ItemFieldDrop))]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public sealed class NetworkedItemFieldDrop : NetworkBehaviour
    {
        private static readonly Dictionary<string, ItemDefinition> s_definitionCache = new(System.StringComparer.Ordinal);

        [SerializeField] private bool enableDebugLog;

        [Networked] private NetworkString<_32> NetworkedItemId { get; set; }

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private readonly DefaultItemFieldPrefabResolver _prefabResolver = new();

        private Rigidbody _rigidbody;
        private NetworkTransform _networkTransform;
        private ItemFieldDrop _fieldDrop;
        private GameObject _visualRoot;
        private string _attachedVisualItemId = string.Empty;

        public void InitializeMetadata(string itemId)
        {
            NetworkedItemId = itemId ?? string.Empty;
            ApplyMetadataToFieldDrop();
        }

        public void ResetForSpawn(string itemId, Vector3 position, Quaternion rotation)
        {
            NetworkedItemId = itemId ?? string.Empty;
            _attachedVisualItemId = string.Empty;
            _visualRoot = null;

            CacheComponents();
            if (_fieldDrop != null)
            {
                _fieldDrop.SetItemId(itemId);
            }

            transform.SetPositionAndRotation(position, rotation);

            if (enableDebugLog)
            {
                Debug.Log(
                    $"[NetworkedItemFieldDrop] ResetForSpawn itemId={itemId}, pos={position}, rot={rotation.eulerAngles}",
                    this);
            }
        }

        public override void Spawned()
        {
            CacheComponents();
            ApplyMetadataToFieldDrop();
            TryEnsureVisual();
            InitializeNetworkTransformState();
            ApplyAuthorityPhysicsState();

            if (enableDebugLog)
            {
                Debug.Log(
                    $"[NetworkedItemFieldDrop] Spawned itemId={NetworkedItemId}, name={name}, id={(Object != null ? Object.Id.Raw.ToString() : "none")}, pos={transform.position}, hasStateAuthority={HasStateAuthority}",
                    this);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ResetForDespawn();
        }

        public override void FixedUpdateNetwork()
        {
            CacheComponents();
            ApplyAuthorityPhysicsState();
        }

        public override void Render()
        {
            CacheComponents();
            ApplyMetadataToFieldDrop();
            TryEnsureVisual();
        }

        public void ResetForDespawn()
        {
            CacheComponents();
            if (_fieldDrop != null)
            {
                _fieldDrop.ResetForDespawn();
            }

            if (enableDebugLog)
            {
                Debug.Log(
                    $"[NetworkedItemFieldDrop] ResetForDespawn itemId={_attachedVisualItemId}, pos={transform.position}",
                    this);
            }

            _visualRoot = null;
            _attachedVisualItemId = string.Empty;
        }

        private void CacheComponents()
        {
            if (_rigidbody == null)
            {
                _rigidbody = GetComponent<Rigidbody>();
            }

            if (_networkTransform == null)
            {
                _networkTransform = GetComponent<NetworkTransform>();
            }

            if (_fieldDrop == null)
            {
                _fieldDrop = GetComponent<ItemFieldDrop>();
            }
        }

        private void ApplyMetadataToFieldDrop()
        {
            if (_fieldDrop == null)
            {
                return;
            }

            var itemId = NetworkedItemId.ToString();
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (!string.Equals(_fieldDrop.ItemId, itemId, System.StringComparison.Ordinal))
            {
                _fieldDrop.SetItemId(itemId);
            }
        }

        private void TryEnsureVisual()
        {
            var itemId = NetworkedItemId.ToString();
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (_visualRoot != null && string.Equals(_attachedVisualItemId, itemId, System.StringComparison.Ordinal))
            {
                return;
            }

            if (_visualRoot != null && !string.Equals(_attachedVisualItemId, itemId, System.StringComparison.Ordinal))
            {
                ItemFieldDropFactory.ReleaseFieldVisual(gameObject, _attachedVisualItemId);
                _visualRoot = null;
                _attachedVisualItemId = string.Empty;
            }

            if (!TryGetItemDefinition(itemId, out var definition))
            {
                return;
            }

            _visualRoot = ItemFieldDropFactory.CreateFieldVisualInstance(definition, _prefabResolver, transform);
            _attachedVisualItemId = itemId;
            if (_fieldDrop != null)
            {
                _fieldDrop.SetItemId(itemId);
                _fieldDrop.EnsureRuntimeSetup();
            }

            if (enableDebugLog)
            {
                Debug.Log(
                    $"[NetworkedItemFieldDrop] Attached visual itemId={itemId}, visual={(_visualRoot != null ? _visualRoot.name : "null")}, pos={transform.position}",
                    this);
            }
        }

        private bool TryGetItemDefinition(string itemId, out ItemDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                definition = null;
                return false;
            }

            if (s_definitionCache.TryGetValue(itemId, out definition) && definition != null)
            {
                return true;
            }

            var options = ItemCatalogLoader.CreateDefaultOptions();
            if (!_catalogProvider.TryGetCatalog(options, out var catalog, out _))
            {
                definition = null;
                return false;
            }

            if (!catalog.TryGetDefinition(itemId, out definition) || definition == null)
            {
                return false;
            }

            s_definitionCache[itemId] = definition;
            return true;
        }

        private void InitializeNetworkTransformState()
        {
            if (!HasStateAuthority || _networkTransform == null)
            {
                return;
            }

            if (_rigidbody != null)
            {
                _rigidbody.position = transform.position;
                _rigidbody.rotation = transform.rotation;
                if (!_rigidbody.isKinematic)
                {
                    _rigidbody.velocity = Vector3.zero;
                    _rigidbody.angularVelocity = Vector3.zero;
                }
            }

            _networkTransform.Teleport(transform.position, transform.rotation);
        }

        private void ApplyAuthorityPhysicsState()
        {
            if (_rigidbody == null)
            {
                return;
            }

            if (HasStateAuthority)
            {
                if (_rigidbody.isKinematic)
                {
                    _rigidbody.isKinematic = false;
                }

                if (!_rigidbody.useGravity)
                {
                    _rigidbody.useGravity = true;
                }

                return;
            }

            if (!_rigidbody.isKinematic)
            {
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }
    }
}
