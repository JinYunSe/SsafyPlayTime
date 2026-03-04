using UnityEngine;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed class PrototypeLocalPlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform targetRoot;
        [SerializeField] private ItemPrototypeHotkeyRunner itemRunner;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float turnLerp = 12f;

        [Header("Camera")]
        [SerializeField] private float cameraPivotHeight = 1.4f;
        [SerializeField] private float mouseSensitivity = 3f;
        [SerializeField] private float zoomSpeed = 2f;
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float defaultPitch = 20f;
        [SerializeField] private float minDistance = 2f;
        [SerializeField] private float maxDistance = 10f;
        [SerializeField] private float defaultDistance = 5f;

        private Camera _runtimeCamera;
        private Rigidbody _targetBody;

        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private float _defaultYaw;
        private float _yaw;
        private float _pitch;
        private float _distance;

        public void Configure(Transform target, ItemPrototypeHotkeyRunner runner)
        {
            targetRoot = target;
            itemRunner = runner;
        }

        private void Awake()
        {
            if (targetRoot == null)
            {
                targetRoot = transform;
            }
        }

        private void Start()
        {
            if (targetRoot == null)
            {
                return;
            }

            _targetBody = targetRoot.GetComponent<Rigidbody>();
            _initialPosition = targetRoot.position;
            _initialRotation = targetRoot.rotation;
            _defaultYaw = targetRoot.eulerAngles.y;

            ResolveCamera();
            ResetCameraState();
            UpdateCameraPose();
        }

        private void Update()
        {
            if (targetRoot == null)
            {
                return;
            }

            HandleMovement();
            HandleCameraInput();

            // Space 입력 시 위치와 아이템 상태를 동시에 초기화한다.
            if (Input.GetKeyDown(KeyCode.Space))
            {
                ResetTransformAndState();
            }
        }

        private void LateUpdate()
        {
            UpdateCameraPose();
        }

        private void HandleMovement()
        {
            var horizontal = Input.GetAxisRaw("Horizontal");
            var vertical = Input.GetAxisRaw("Vertical");
            var input = new Vector2(horizontal, vertical);
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            var cameraForward = _runtimeCamera != null ? _runtimeCamera.transform.forward : targetRoot.forward;
            var cameraRight = _runtimeCamera != null ? _runtimeCamera.transform.right : targetRoot.right;
            cameraForward.y = 0f;
            cameraRight.y = 0f;
            if (cameraForward.sqrMagnitude < 0.0001f)
            {
                cameraForward = Vector3.forward;
            }

            if (cameraRight.sqrMagnitude < 0.0001f)
            {
                cameraRight = Vector3.right;
            }

            cameraForward.Normalize();
            cameraRight.Normalize();

            var moveDir = (cameraForward * input.y) + (cameraRight * input.x);
            if (moveDir.sqrMagnitude > 1f)
            {
                moveDir.Normalize();
            }

            if (_targetBody != null && !_targetBody.isKinematic)
            {
                var velocity = _targetBody.velocity;
                velocity.x = moveDir.x * moveSpeed;
                velocity.z = moveDir.z * moveSpeed;
                _targetBody.velocity = velocity;
            }
            else
            {
                targetRoot.position += moveDir * (moveSpeed * Time.deltaTime);
            }

            if (moveDir.sqrMagnitude > 0.0001f)
            {
                var targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
                targetRoot.rotation = Quaternion.Slerp(targetRoot.rotation, targetRotation, turnLerp * Time.deltaTime);
            }
        }

        private void HandleCameraInput()
        {
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                _distance = Mathf.Clamp(_distance - scroll * zoomSpeed, minDistance, maxDistance);
            }
        }

        private void UpdateCameraPose()
        {
            if (targetRoot == null)
            {
                return;
            }

            if (_runtimeCamera == null)
            {
                ResolveCamera();
                if (_runtimeCamera == null)
                {
                    return;
                }
            }

            var pivot = targetRoot.position + Vector3.up * cameraPivotHeight;
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var cameraPos = pivot - (rotation * Vector3.forward * _distance);

            _runtimeCamera.transform.position = cameraPos;
            _runtimeCamera.transform.rotation = rotation;
            _runtimeCamera.transform.LookAt(pivot);
        }

        private void ResetTransformAndState()
        {
            targetRoot.position = _initialPosition;
            targetRoot.rotation = _initialRotation;

            if (_targetBody != null)
            {
                _targetBody.velocity = Vector3.zero;
                _targetBody.angularVelocity = Vector3.zero;
            }

            ResetCameraState();
            itemRunner?.ResetPrototypeStateFromExternal();
        }

        private void ResolveCamera()
        {
            _runtimeCamera = Camera.main;
            if (_runtimeCamera != null)
            {
                return;
            }

            _runtimeCamera = FindObjectOfType<Camera>();
            if (_runtimeCamera == null)
            {
                var cameraObject = new GameObject("PrototypeRuntimeCamera");
                _runtimeCamera = cameraObject.AddComponent<Camera>();
            }
        }

        private void ResetCameraState()
        {
            _yaw = _defaultYaw;
            _pitch = defaultPitch;
            _distance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
        }
    }
}
