using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public sealed class NetworkedItemFieldDrop : NetworkBehaviour
    {
        private Rigidbody _rigidbody;
        private NetworkTransform _networkTransform;

        public override void Spawned()
        {
            CacheRigidbody();
            InitializeNetworkTransformState();
            ApplyAuthorityPhysicsState();
            Debug.Log(
                $"[NetworkedItemFieldDrop] Spawned name={name}, id={(Object != null ? Object.Id.Raw.ToString() : "none")}, pos={transform.position}, hasStateAuthority={HasStateAuthority}",
                this);
        }

        public override void FixedUpdateNetwork()
        {
            if (_rigidbody == null)
            {
                CacheRigidbody();
            }

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
                _rigidbody.isKinematic = true;
            }

            if (_rigidbody.useGravity)
            {
                _rigidbody.useGravity = false;
            }
        }

        private void CacheRigidbody()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
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
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;
                return;
            }

            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }
    }
}
