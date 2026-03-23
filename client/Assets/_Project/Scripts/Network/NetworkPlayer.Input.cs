using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private static readonly Collider[] s_deathZoneProbeBuffer = new Collider[16];

    private Vector3 _lastSafePosition;
    private Quaternion _lastSafeRotation = Quaternion.identity;
    private bool _hasLastSafeTransform;
    private float _nextOutOfBoundsRecoverAt;
    private SpawnPointGroup _cachedSpawnPointGroup;

    private float ResolveCameraYaw()
    {
        return Camera.main != null ? Camera.main.transform.eulerAngles.y : transform.eulerAngles.y;
    }

    private void Update()
    {
        ApplyReplicatedHeldItemPresentation();
        ApplyReplicatedWorldItemEffects();
        UpdateReplicatedFlamethrowerVisualFollow();
        UpdateLocalDeathPresentation();

        if (Object != null && Object.IsValid && !HasInputAuthority)
            return;

        PollLocalInputState();
        RefreshRuntimeIntegrationIfNeeded();
    }

    private void FixedUpdate()
    {
        if (Runner != null)
            return;

        var input = BuildSandboxInput();
        DoPhysicsStep(input, Time.fixedDeltaTime);
        ResetOneShotLocalInput();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        // 사망 후에는 물리/동기화 연산 전부 스킵 → 네트워크 부하 최소화
        if (GetIsDeadState())
            return;

        // 타임 기반 타이머는 패킷 손실 여부와 무관하게 항상 실행
        // (기절·회복·HP 무적 타이머가 GetInput 실패 시 멈추는 문제 방지)
        TickAlwaysStates(Runner.DeltaTime);

        // 기절 중이 아닌 경우에만 입력 기반 물리(로코모션·상호작용) 처리
        if (_isActiveRagdoll && GetInput(out PlayerNetworkInput input))
        {
            TraceMoveAuthorityInput("FixedUpdateNetwork.Input", input);
            DoPhysicsStep(input, Runner.DeltaTime);
        }

        UpdateGrabHandlers();
        UpdatePhysicalPhaseState(Runner.DeltaTime);

        // 의식 있는 플레이어가 잡히거나 끌릴 때(BeingGrabbed/Dragged):
        // 잡기 조인트가 hips 뼈를 끌고 가지만 locomotion은 root를 별도로 구동하므로
        // root transform이 실제 물리 위치와 괴리가 생긴다.
        // GetInput() 성공 여부와 무관하게 항상 실행해야 모든 틱에서 올바른 위치를
        // NetworkTransform이 클라이언트에 전달할 수 있다.
        // (이전에는 DoPhysicsStep() 안에 있어 GetInput 실패 시 실행되지 않는 문제가 있었음)
        SyncInteractionRootPosition();

        SynchronizeNetworkSimulationState();
        TraceMoveAuthorityState("FixedUpdateNetwork.AfterPhase");
        TraceMovePublish("FixedUpdateNetwork.Publish");
        ClampOutOfBoundsCharacter();
    }

    private void PollLocalInputState()
    {
        if (GetIsDeadState())
        {
            ResetAllLocalInputState();
            return;
        }

        if (Runner == null)
        {
            _sandboxInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            if (Input.GetKeyDown(KeyCode.Space))
                _sandboxJump = true;
        }

        UpdatePrimaryClickState();
        UpdateSecondaryClickState();

        if (Input.GetKeyDown(KeyCode.F))
            _dropTriggered = true;

        // OwnerProxy does not run DoPhysicsStep locally.
        // Mirror the local grab intent here so animation sync stays correct.
        // This keeps PartyMonsterAnimationDriver.SyncGrabAnimation() in sync.
        if (Runner != null && HasInputAuthority && !HasStateAuthority)
        {
            var unifiedGrabHold = _leftMouseDown && _leftMouseConsumedAsGrab;
            _isLeftGrabActive = unifiedGrabHold;
            _isRightGrabActive = unifiedGrabHold;
            _isGrabActive = unifiedGrabHold;
        }
    }

    private void UpdatePrimaryClickState()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _leftMouseDown = true;
            _leftMouseDownTime = Time.time;
            _leftMouseConsumedAsGrab = false;
        }

        if (Input.GetMouseButton(0) && _leftMouseDown)
        {
            if (Time.time - _leftMouseDownTime >= GRAB_HOLD_THRESHOLD && !_leftMouseConsumedAsGrab)
                _leftMouseConsumedAsGrab = true;
        }

        if (!Input.GetMouseButtonUp(0))
            return;

        if (!_leftMouseConsumedAsGrab && Time.time - _leftMouseDownTime < GRAB_HOLD_THRESHOLD)
            _leftClickUseTriggered = true;

        _leftMouseDown = false;
    }

    private void UpdateSecondaryClickState()
    {
        // 우클릭 = 던지기 (잡고 있을 때)
        if (Input.GetMouseButtonDown(1))
            _throwTriggered = true;

    }

    private PlayerNetworkInput BuildSandboxInput()
    {
        return new PlayerNetworkInput
        {
            Move = _sandboxInput,
            CameraYaw = ResolveCameraYaw(),
            Jump = _sandboxJump,
            Punch = _leftClickUseTriggered,
            PrimaryUseHold = _leftMouseDown,
            Drop = _dropTriggered,
            Throw = _throwTriggered,
            LeftGrabHold = _leftMouseDown && _leftMouseConsumedAsGrab,
            Sprint = Input.GetKey(KeyCode.LeftShift)
        };
    }

    private void ResetOneShotLocalInput()
    {
        _sandboxJump = false;
        _leftClickUseTriggered = false;
        _throwTriggered = false;
    }

    private void ResetAllLocalInputState()
    {
        _sandboxInput = Vector2.zero;
        _sandboxJump = false;
        _dropTriggered = false;
        _leftClickUseTriggered = false;
        _leftMouseDown = false;
        _leftMouseConsumedAsGrab = false;
        _isLeftGrabActive = false;
        _isRightGrabActive = false;
        _isGrabActive = false;
    }

    private void SynchronizeNetworkSimulationState()
    {
        // FixedUpdateNetwork()가 HasStateAuthority에서만 실행되므로 이 함수도 host-only.
        if (syncPhysicsObjects != null)
        {
            for (int i = 0; i < syncPhysicsObjects.Length; i++)
            {
                if (syncPhysicsObjects[i] != null)
                    BoneRotations.Set(i, syncPhysicsObjects[i].transform.localRotation);
            }
        }

        // Hips(muscles[0]) 절대 위치 동기화 — 잡기/끌기 시 원격 위치 추적
        if (_puppetMaster != null && _puppetMaster.muscles != null && _puppetMaster.muscles.Length > 0)
        {
            var hipsMuscle = _puppetMaster.muscles[0];
            if (hipsMuscle.joint != null)
                NetworkedHipsPosition = hipsMuscle.joint.transform.position;
        }

        NetworkedIsActiveRagdoll = _isActiveRagdoll;
        NetworkedPhysicalPhase = (byte)_localPhysicalPhase;
        NetworkedInstability = _localInstability;
        NetworkedIsDragged = _localIsDragged;

        // ── CarrySolveFrame: carry anchor 네트워크 동기화 (carrier/victim 분리) ──
        NetworkedCarryMode = (byte)_localCarryMode;
        if (_localCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None && carryRig != null)
        {
            if (_localCarryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim)
            {
                // victim: 자신의 hips-chest 가중 평균 anchor 전송
                carryRig.UpdateVictimAnchor();
                if (carryRig.TryGetVictimAnchorWorld(out var vPos, out var vFwd))
                {
                    NetworkedVictimAnchorPosition = vPos;
                    NetworkedVictimAnchorForward = vFwd;
                    NetworkedVictimAnchorValid = true;
                }
                else
                {
                    NetworkedVictimAnchorValid = false;
                }

                if (_hasCarriedVictimRootOffset)
                {
                    NetworkedVictimRootOffset = _carriedVictimRootOffset;
                    NetworkedVictimRootOffsetValid = true;
                }
                else
                {
                    NetworkedVictimRootOffsetValid = false;
                }

                NetworkedCarrierAnchorValid = false;
            }
            else
            {
                // carrier: 자신의 carry rig anchor 전송
                if (carryRig.TryGetCarrierAnchorWorld(_localCarryMode, out var cPos, out var cFwd))
                {
                    NetworkedCarrierAnchorPosition = cPos;
                    NetworkedCarrierAnchorForward = cFwd;
                    NetworkedCarrierAnchorValid = true;
                }
                else
                {
                    NetworkedCarrierAnchorValid = false;
                }

                NetworkedVictimAnchorValid = false;
                NetworkedVictimRootOffsetValid = false;
            }
        }
        else
        {
            NetworkedVictimAnchorValid = false;
            NetworkedVictimRootOffsetValid = false;
            NetworkedCarrierAnchorValid = false;
        }

        SynchronizeStunPresentationPhase();
    }

    private void UpdateGrabHandlers()
    {
        foreach (var handler in _handGrabHandlers)
            handler.UpdateState();

        SyncGrabNetworkState();
    }

    private void SyncGrabNetworkState()
    {
        if (Runner == null || !Object.IsValid || !HasStateAuthority)
            return;

        bool leftHolding = false, rightHolding = false;
        foreach (var handler in _handGrabHandlers)
        {
            if (!handler.IsHolding) continue;
            if (handler.Side == HandGrabHandler.HandSide.Left)
                leftHolding = true;
            else
                rightHolding = true;
        }

        NetworkedLeftGrabHolding = leftHolding;
        NetworkedRightGrabHolding = rightHolding;
    }

    private void RememberSafeTransform(Vector3 position, Quaternion rotation)
    {
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
            return;
        if (position.y < -5f)
            return;

        _lastSafePosition = position;
        _lastSafeRotation = rotation;
        _hasLastSafeTransform = true;
    }

    private bool TryResolveRecoveryTransform(out Vector3 position, out Quaternion rotation)
    {
        if (_hasLastSafeTransform)
        {
            position = _lastSafePosition;
            rotation = _lastSafeRotation;
            return true;
        }

        _cachedSpawnPointGroup ??= FindObjectOfType<SpawnPointGroup>();
        var spawnGroup = _cachedSpawnPointGroup;
        if (spawnGroup != null && spawnGroup.transform.childCount > 0)
        {
            var index = Random.Range(0, spawnGroup.transform.childCount);
            var point = spawnGroup.transform.GetChild(index);
            if (point != null)
            {
                position = point.position;
                rotation = point.rotation;
                return true;
            }
        }

        position = new Vector3(transform.position.x, 1f, transform.position.z);
        rotation = transform.rotation;
        return false;
    }

    private bool IsInsideDeathZone()
    {
        var hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            0.2f,
            s_deathZoneProbeBuffer,
            ~0,
            QueryTriggerInteraction.Collide);

        for (var i = 0; i < hitCount; i++)
        {
            var hit = s_deathZoneProbeBuffer[i];
            if (hit == null)
                continue;

            if (hit.transform.IsChildOf(transform))
                continue;

            if (hit.GetComponentInParent<DeathZone>() != null)
                return true;
        }

        return false;
    }

    private void ClampOutOfBoundsCharacter()
    {
        if (GetIsDeadState())
            return;

        if (transform.position.y >= -10)
            return;

        var now = Runner != null ? (float)Runner.SimulationTime : Time.time;
        if (now < _nextOutOfBoundsRecoverAt)
            return;

        _nextOutOfBoundsRecoverAt = now + 0.5f;

        if (IsInsideDeathZone())
        {
            KillImmediately("DeathZoneOutOfBounds");
            return;
        }

        if (!TryResolveRecoveryTransform(out var recoveryPosition, out var recoveryRotation))
            recoveryRotation = transform.rotation;

        transform.SetPositionAndRotation(recoveryPosition, recoveryRotation);
        if (rigidbody3D != null)
        {
            rigidbody3D.position = recoveryPosition;
            rigidbody3D.rotation = recoveryRotation;
            if (!rigidbody3D.isKinematic)
            {
                rigidbody3D.velocity = Vector3.zero;
                rigidbody3D.angularVelocity = Vector3.zero;
            }
        }
        RememberSafeTransform(recoveryPosition, recoveryRotation);
        ForceRecover();
    }
}
