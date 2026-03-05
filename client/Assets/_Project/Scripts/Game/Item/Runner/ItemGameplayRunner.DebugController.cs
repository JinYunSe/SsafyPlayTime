using System;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void TickLocalDebugController()
        {
            if (!enableLocalDebugController || targetRoot == null)
            {
                return;
            }

            EnsureLocalDebugControllerInitialized();
            HandleLocalDebugMovement();
            HandleLocalDebugCameraInput();

            // Space는 점프가 아니라 위치/상태 초기화 용도로 사용한다.
            if (Input.GetKeyDown(KeyCode.Space))
            {
                ResetLocalDebugState();
            }
        }

        private void LateUpdate()
        {
            UpdateLocalDebugCameraPose();
        }

        private void EnsureLocalDebugControllerInitialized()
        {
            if (_debugControlInitialized && _debugControlTarget == targetRoot)
            {
                return;
            }

            _debugControlTarget = targetRoot;
            _debugControlBody = targetRoot.GetComponent<Rigidbody>();
            _debugInitialPosition = targetRoot.position;
            _debugInitialRotation = targetRoot.rotation;
            _debugControlInitialized = true;

            if (_debugControlBody != null)
            {
                if (!_debugConstraintsCaptured)
                {
                    _debugOriginalConstraints = _debugControlBody.constraints;
                    _debugConstraintsCaptured = true;
                }

                // 오뚜기처럼 넘어지지 않게 X/Z 회전을 고정한다.
                _debugControlBody.constraints = _debugOriginalConstraints |
                                                RigidbodyConstraints.FreezeRotationX |
                                                RigidbodyConstraints.FreezeRotationZ;
            }

            ResolveDebugCamera();
            ResetDebugCameraState();
            ResolveDebugDummy();
            PlaceDummyInFrontOfPlayer();
        }

        private void HandleLocalDebugMovement()
        {
            var horizontal = Input.GetAxisRaw("Horizontal");
            var vertical = Input.GetAxisRaw("Vertical");
            var input = new Vector2(horizontal, vertical);
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            var moveDir = GetCameraRelativeMoveDirection(input);
            var hasMoveInput = moveDir.sqrMagnitude > 0.0001f;

            if (_debugControlBody != null && !_debugControlBody.isKinematic)
            {
                // 외력(블랙홀 흡입)을 살리기 위해 입력이 있을 때만 수평 속도를 덮어쓴다.
                if (hasMoveInput)
                {
                    var velocity = _debugControlBody.velocity;
                    velocity.x = moveDir.x * localMoveSpeed;
                    velocity.z = moveDir.z * localMoveSpeed;
                    _debugControlBody.velocity = velocity;
                }
            }
            else if (hasMoveInput)
            {
                targetRoot.position += moveDir * (localMoveSpeed * Time.deltaTime);
            }

            if (hasMoveInput)
            {
                var targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
                targetRoot.rotation = Quaternion.Slerp(targetRoot.rotation, targetRotation, localTurnLerp * Time.deltaTime);
            }
        }

        private Vector3 GetCameraRelativeMoveDirection(Vector2 input)
        {
            var cameraTransform = _debugRuntimeCamera != null ? _debugRuntimeCamera.transform : null;
            var cameraForward = cameraTransform != null ? cameraTransform.forward : targetRoot.forward;
            var cameraRight = cameraTransform != null ? cameraTransform.right : targetRoot.right;

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

            return moveDir;
        }

        private void HandleLocalDebugCameraInput()
        {
            if (_debugRuntimeCamera == null)
            {
                ResolveDebugCamera();
            }

            if (_debugRuntimeCamera == null)
            {
                return;
            }

            // 우클릭 없이 마우스 이동만으로 카메라를 회전한다.
            _debugYaw += Input.GetAxis("Mouse X") * localCameraMouseSensitivity;
            _debugPitch -= Input.GetAxis("Mouse Y") * localCameraMouseSensitivity;
            _debugPitch = Mathf.Clamp(_debugPitch, localCameraMinPitch, localCameraMaxPitch);

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                _debugDistance = Mathf.Clamp(
                    _debugDistance - scroll * localCameraZoomSpeed,
                    localCameraMinDistance,
                    localCameraMaxDistance);
            }
        }

        private void UpdateLocalDebugCameraPose()
        {
            if (!enableLocalDebugController || targetRoot == null)
            {
                return;
            }

            if (_debugRuntimeCamera == null)
            {
                ResolveDebugCamera();
                if (_debugRuntimeCamera == null)
                {
                    return;
                }
            }

            var pivot = targetRoot.position + Vector3.up * localCameraPivotHeight;
            var rotation = Quaternion.Euler(_debugPitch, _debugYaw, 0f);
            var cameraPos = pivot - (rotation * Vector3.forward * _debugDistance);

            _debugRuntimeCamera.transform.position = cameraPos;
            _debugRuntimeCamera.transform.rotation = rotation;
            _debugRuntimeCamera.transform.LookAt(pivot);
        }

        private void ResolveDebugCamera()
        {
            _debugRuntimeCamera = Camera.main;
            if (_debugRuntimeCamera != null)
            {
                return;
            }

            _debugRuntimeCamera = FindObjectOfType<Camera>();
            if (_debugRuntimeCamera == null)
            {
                var cameraObject = new GameObject("ItemRuntimeCamera");
                _debugRuntimeCamera = cameraObject.AddComponent<Camera>();
            }
        }

        private void ResetDebugCameraState()
        {
            if (targetRoot == null)
            {
                return;
            }

            _debugDefaultYaw = targetRoot.eulerAngles.y;
            _debugYaw = _debugDefaultYaw;
            _debugPitch = localCameraDefaultPitch;
            _debugDistance = Mathf.Clamp(localCameraDefaultDistance, localCameraMinDistance, localCameraMaxDistance);
        }

        private void ResetLocalDebugState()
        {
            if (targetRoot == null)
            {
                return;
            }

            targetRoot.position = _debugInitialPosition;
            targetRoot.rotation = _debugInitialRotation;

            if (_debugControlBody != null)
            {
                _debugControlBody.velocity = Vector3.zero;
                _debugControlBody.angularVelocity = Vector3.zero;
            }

            if (resetRuntimeStateOnSpace && itemRuntimeHost != null)
            {
                itemRuntimeHost.ResetRuntimeState();
            }

            StopAllBlackholeRoutines();

            StopFlamethrowerParticle();
            StopAllLoopingSfx();
            DestroyExistingBlackholeVisuals();
            PlaceDummyInFrontOfPlayer();
            ResetDebugCameraState();

            LogStatus("Space reset: player/item state restored.");
        }

        private void ResolveDebugDummy()
        {
            _debugDummyTarget = null;
            _debugDummyBody = null;
            if (string.IsNullOrWhiteSpace(localDebugDummyName))
            {
                return;
            }

            var dummy = GameObject.Find(localDebugDummyName);
            if (dummy == null)
            {
                return;
            }

            _debugDummyTarget = dummy.transform;
            _debugDummyBody = dummy.GetComponent<Rigidbody>();
        }

        private void PlaceDummyInFrontOfPlayer()
        {
            if (targetRoot == null)
            {
                return;
            }

            if (_debugDummyTarget == null)
            {
                ResolveDebugDummy();
                if (_debugDummyTarget == null)
                {
                    return;
                }
            }

            var forward = targetRoot.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            var spawnPos = targetRoot.position + forward * Mathf.Max(0.5f, localDebugDummyForwardOffset);
            spawnPos.y = localDebugDummyHeight;

            _debugDummyTarget.position = spawnPos;
            _debugDummyTarget.rotation = Quaternion.identity;

            if (_debugDummyBody != null && !_debugDummyBody.isKinematic)
            {
                _debugDummyBody.velocity = Vector3.zero;
                _debugDummyBody.angularVelocity = Vector3.zero;
            }
        }

        private void DestroyExistingBlackholeVisuals()
        {
            var visuals = FindObjectsOfType<Transform>(true);
            for (var i = 0; i < visuals.Length; i++)
            {
                var t = visuals[i];
                if (t == null)
                {
                    continue;
                }

                if (string.Equals(t.name, BlackholeVisualName, StringComparison.Ordinal) ||
                    string.Equals(t.name, "Item_BlackholeFx", StringComparison.Ordinal))
                {
                    Destroy(t.gameObject);
                }
            }
        }
    }
}
