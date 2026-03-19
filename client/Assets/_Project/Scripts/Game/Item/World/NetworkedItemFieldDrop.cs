using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ItemFieldDrop))]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public sealed class NetworkedItemFieldDrop : NetworkBehaviour
    {
        [SerializeField] private bool enableDebugLog;

        [Networked] private NetworkString<_32> NetworkedItemId { get; set; }

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private readonly DefaultItemFieldPrefabResolver _prefabResolver = new();

        private Rigidbody _rigidbody;
        private NetworkTransform _networkTransform;
        private ItemFieldDrop _fieldDrop;
        private GameObject _visualRoot;

        public void InitializeMetadata(string itemId)
        {
            NetworkedItemId = itemId ?? string.Empty;
            ApplyMetadataToFieldDrop();
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
            if (_visualRoot != null)
            {
                return;
            }

            var itemId = NetworkedItemId.ToString();
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (!TryGetItemDefinition(itemId, out var definition))
            {
                return;
            }

            _visualRoot = ItemFieldDropFactory.CreateFieldVisualInstance(definition, _prefabResolver, transform);
            if (_fieldDrop != null)
            {
                _fieldDrop.SetItemId(itemId);
                _fieldDrop.EnsureRuntimeSetup();
            }
        }

        private bool TryGetItemDefinition(string itemId, out ItemDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            var options = ItemCatalogLoader.CreateDefaultOptions();
            if (!_catalogProvider.TryGetCatalog(options, out var catalog, out _))
            {
                return false;
            }

            return catalog.TryGetDefinition(itemId, out definition);
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
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
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

            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }
    }
}
