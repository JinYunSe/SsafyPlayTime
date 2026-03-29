using System.Collections.Generic;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    internal static class ItemFieldVisualPool
    {
        private static readonly Dictionary<string, Stack<GameObject>> Pools = new(System.StringComparer.Ordinal);
        private static Transform s_poolRoot;

        public static GameObject Acquire(string itemId, Transform parent)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            if (!Pools.TryGetValue(itemId, out var pool))
            {
                return null;
            }

            while (pool.Count > 0)
            {
                var instance = pool.Pop();
                if (instance == null)
                {
                    continue;
                }

                var handle = ItemFieldVisualHandle.GetOrAdd(instance);
                handle.RestoreForAttach(itemId, parent);
                return instance;
            }

            return null;
        }

        public static void RegisterNew(string itemId, GameObject visualRoot)
        {
            if (visualRoot == null || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            var handle = ItemFieldVisualHandle.GetOrAdd(visualRoot);
            handle.Initialize(itemId);
        }

        public static void Release(string itemId, GameObject visualRoot)
        {
            if (visualRoot == null || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (!Pools.TryGetValue(itemId, out var pool))
            {
                pool = new Stack<GameObject>();
                Pools[itemId] = pool;
            }

            var handle = ItemFieldVisualHandle.GetOrAdd(visualRoot);
            handle.Initialize(itemId);
            handle.DetachToPool(GetOrCreatePoolRoot());
            pool.Push(visualRoot);
        }

        private static Transform GetOrCreatePoolRoot()
        {
            if (s_poolRoot != null)
            {
                return s_poolRoot;
            }

            var poolRoot = new GameObject("[ItemFieldVisualPool]");
            poolRoot.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            s_poolRoot = poolRoot.transform;
            return s_poolRoot;
        }
    }

    [DisallowMultipleComponent]
    internal sealed class ItemFieldVisualHandle : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string pooledItemId = string.Empty;
        [SerializeField, HideInInspector] private Vector3 initialLocalPosition = Vector3.zero;
        [SerializeField, HideInInspector] private Vector3 initialLocalEulerAngles = Vector3.zero;
        [SerializeField, HideInInspector] private Vector3 initialLocalScale = Vector3.one;

        private Renderer[] _renderers;
        private Collider[] _colliders;
        private Rigidbody[] _rigidbodies;
        private ParticleSystem[] _particles;
        private TrailRenderer[] _trails;
        private bool _initialized;

        public static ItemFieldVisualHandle GetOrAdd(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            var handle = target.GetComponent<ItemFieldVisualHandle>();
            if (handle == null)
            {
                handle = target.AddComponent<ItemFieldVisualHandle>();
            }

            return handle;
        }

        public void Initialize(string itemId)
        {
            if (_initialized && string.Equals(pooledItemId, itemId, System.StringComparison.Ordinal))
            {
                return;
            }

            pooledItemId = itemId ?? string.Empty;
            initialLocalPosition = transform.localPosition;
            initialLocalEulerAngles = transform.localEulerAngles;
            initialLocalScale = transform.localScale;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            _rigidbodies = GetComponentsInChildren<Rigidbody>(true);
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            _trails = GetComponentsInChildren<TrailRenderer>(true);
            _initialized = true;
        }

        public void RestoreForAttach(string itemId, Transform parent)
        {
            Initialize(itemId);
            transform.SetParent(parent, false);
            gameObject.name = "Visual";
            transform.localPosition = initialLocalPosition;
            transform.localRotation = Quaternion.Euler(initialLocalEulerAngles);
            transform.localScale = initialLocalScale;
            gameObject.SetActive(true);

            for (var i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = true;
                }
            }

            for (var i = 0; i < _trails.Length; i++)
            {
                if (_trails[i] != null)
                {
                    _trails[i].Clear();
                }
            }

            for (var i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] != null)
                {
                    _particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    _particles[i].Play(true);
                }
            }
        }

        public void DetachToPool(Transform poolRoot)
        {
            if (poolRoot == null)
            {
                return;
            }

            for (var i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] != null)
                {
                    _particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            for (var i = 0; i < _trails.Length; i++)
            {
                if (_trails[i] != null)
                {
                    _trails[i].Clear();
                }
            }

            for (var i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                {
                    _colliders[i].enabled = false;
                }
            }

            for (var i = 0; i < _rigidbodies.Length; i++)
            {
                var body = _rigidbodies[i];
                if (body == null)
                {
                    continue;
                }

                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
                body.useGravity = false;
            }

            transform.SetParent(poolRoot, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            gameObject.SetActive(false);
        }
    }
}
