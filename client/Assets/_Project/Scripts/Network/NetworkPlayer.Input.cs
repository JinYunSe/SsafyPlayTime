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

        if (GetInput(out PlayerNetworkInput input))
        {
            TraceMoveAuthorityInput("FixedUpdateNetwork.Input", input);
            DoPhysicsStep(input, Runner.DeltaTime);
        }

        UpdateGrabHandlers();
        UpdatePhysicalPhaseState(Runner.DeltaTime);
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
            var canSendGrabHold = Time.time >= _grabDisabledUntilTime && !HasHeldRuntimeItem();
            var leftGrabHold = _leftMouseDown && _leftMouseConsumedAsGrab && canSendGrabHold;
            var rightGrabHold = _rightMouseDown && _rightMouseConsumedAsGrab && canSendGrabHold;
            _isLeftGrabActive = leftGrabHold;
            _isRightGrabActive = rightGrabHold;
            _isGrabActive = leftGrabHold || rightGrabHold;
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
        {
            _rightMouseDown = true;
            _rightMouseDownTime = Time.time;
            _rightMouseConsumedAsGrab = false;
        }

        if (Input.GetMouseButton(1) && _rightMouseDown)
        {
            if (Time.time - _rightMouseDownTime >= GRAB_HOLD_THRESHOLD && !_rightMouseConsumedAsGrab)
                _rightMouseConsumedAsGrab = true;
        }

        if (!Input.GetMouseButtonUp(1))
            return;

        if (!_rightMouseConsumedAsGrab && Time.time - _rightMouseDownTime < GRAB_HOLD_THRESHOLD)
            _throwTriggered = true;

        _rightMouseDown = false;
    }

    private PlayerNetworkInput BuildSandboxInput()
    {
        var canSendGrabHold = Time.time >= _grabDisabledUntilTime && !HasHeldRuntimeItem();
        return new PlayerNetworkInput
        {
            Move = _sandboxInput,
            CameraYaw = ResolveCameraYaw(),
            Jump = _sandboxJump,
            Punch = _leftClickUseTriggered,
            PrimaryUseHold = _leftMouseDown,
            Drop = _dropTriggered,
            Throw = _throwTriggered,
            LeftGrabHold = _leftMouseDown && _leftMouseConsumedAsGrab && canSendGrabHold,
            RightGrabHold = _rightMouseDown && _rightMouseConsumedAsGrab && canSendGrabHold,
            Headbutt = Input.GetMouseButtonDown(2),
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
        // Sync absolute hips position for remote grab / drag presentation.
        _leftClickUseTriggered = false;
        _leftMouseDown = false;
        _leftMouseConsumedAsGrab = false;
        _rightMouseDown = false;
        _rightMouseConsumedAsGrab = false;
        _isLeftGrabActive = false;
        _isRightGrabActive = false;
        _isGrabActive = false;
    }

    private void SynchronizeNetworkSimulationState()
    {
        var previousNetworkedHipsPosition = NetworkedHipsPosition;

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

        var hipsUpdatedThisFrame = previousNetworkedHipsPosition != NetworkedHipsPosition;

        NetworkedIsActiveRagdoll = _isActiveRagdoll;
        NetworkedIsGrounded = _isGrounded;
        NetworkedPhysicalPhase = (byte)_localPhysicalPhase;
        NetworkedInstability = _localInstability;
        NetworkedIsDragged = _localIsDragged;
        var currentGrabActionState = CharacterGrabController.GrabActionState.Idle;
        var currentHoldVariant = CharacterGrabController.HoldVariant.None;
        var currentLeftHandHoldMode = CharacterGrabController.HandHoldMode.None;
        var currentRightHandHoldMode = CharacterGrabController.HandHoldMode.None;
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            currentGrabActionState = characterGrabController.CurrentActionState;
            currentHoldVariant = characterGrabController.CurrentHoldVariant;
            currentLeftHandHoldMode = characterGrabController.LeftHandMode;
            currentRightHandHoldMode = characterGrabController.RightHandMode;
        }

        // ── CarrySolveFrame: carry anchor 네트워크 동기화 (carrier/victim 분리) ──
        NetworkedCarryMode = (byte)_localCarryMode;
        if (_localCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None && carryRig != null)
        {
            if (_localCarryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim)
            {
                // victim: 자신의 hips-chest 가중 평균 anchor 전송
                carryRig.UpdateVictimAnchor();
                if (carryRig.TryGetVictimSupportFrameWorld(out var vPos, out var vFwd))
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

                var victimCarryRootPosition = transform.position;
                if (TryResolveCarrierOwnedVictimRootTarget(out var carrierOwnedVictimRootPosition, out _))
                    victimCarryRootPosition = carrierOwnedVictimRootPosition;

                NetworkedVictimCarryRootPosition = victimCarryRootPosition;
                NetworkedVictimCarryRootValid = true;
                NetworkedCarrierAnchorValid = false;
            }
            else
            {
                // carrier: 자신의 torso 기반 support frame 전송
                if (carryRig.TryGetCarrierSupportFrameWorld(_localCarryMode, currentHoldVariant, out var cPos, out var cFwd))
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
                NetworkedVictimCarryRootValid = false;
            }
        }
        else
        {
            NetworkedVictimAnchorValid = false;
            NetworkedVictimRootOffsetValid = false;
            NetworkedVictimCarryRootValid = false;
            NetworkedCarrierAnchorValid = false;
        }

        if (_localCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
        {
            var victimAnchorDetails = (bool)NetworkedVictimAnchorValid
                ? $" victimAnchor={FormatCarryDebugVector(NetworkedVictimAnchorPosition)}"
                : string.Empty;
            var victimRootOffsetDetails = (bool)NetworkedVictimRootOffsetValid
                ? $" victimRootOffset={FormatCarryDebugVector(NetworkedVictimRootOffset)}"
                : string.Empty;
            var victimCarryRootDetails = (bool)NetworkedVictimCarryRootValid
                ? $" victimCarryRoot={FormatCarryDebugVector(NetworkedVictimCarryRootPosition)}"
                : string.Empty;
            var carrierAnchorDetails = (bool)NetworkedCarrierAnchorValid
                ? $" carrierAnchor={FormatCarryDebugVector(NetworkedCarrierAnchorPosition)}"
                : string.Empty;
            TraceCarryDebugSample(
                "PublishCarryState",
                $"phase={GetPhysicalPhase()} hipsNet={FormatCarryDebugVector(NetworkedHipsPosition)} hipsUpdated={hipsUpdatedThisFrame} " +
                $"holdVariant={currentHoldVariant} " +
                $"victimAnchorValid={(bool)NetworkedVictimAnchorValid} " +
                $"victimRootOffsetValid={(bool)NetworkedVictimRootOffsetValid} " +
                $"victimCarryRootValid={(bool)NetworkedVictimCarryRootValid} " +
                $"carrierAnchorValid={(bool)NetworkedCarrierAnchorValid}" +
                victimAnchorDetails +
                victimRootOffsetDetails +
                victimCarryRootDetails +
                carrierAnchorDetails,
                forceSample: _localCarryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.CarriedVictim);
        }

        if (characterGrabController != null)
        {
            NetworkedGrabActionState = (byte)currentGrabActionState;
            NetworkedGrabHoldVariant = (byte)currentHoldVariant;
            NetworkedLeftHandHoldMode = (byte)currentLeftHandHoldMode;
            NetworkedRightHandHoldMode = (byte)currentRightHandHoldMode;
        }
        else
        {
            NetworkedGrabActionState = (byte)CharacterGrabController.GrabActionState.Idle;
            NetworkedGrabHoldVariant = (byte)CharacterGrabController.HoldVariant.None;
            NetworkedLeftHandHoldMode = (byte)CharacterGrabController.HandHoldMode.None;
            NetworkedRightHandHoldMode = (byte)CharacterGrabController.HandHoldMode.None;
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
