using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private float ResolveCameraYaw()
    {
        return Camera.main != null ? Camera.main.transform.eulerAngles.y : transform.eulerAngles.y;
    }

    private void Update()
    {
        ApplyReplicatedHeldItemPresentation();
        ApplyReplicatedWorldItemEffects();
        UpdateReplicatedFlamethrowerVisualFollow();

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

        if (GetInput(out PlayerNetworkInput input))
            DoPhysicsStep(input, Runner.DeltaTime);

        SynchronizeNetworkSimulationState();
        UpdateGrabHandlers();
        ClampOutOfBoundsCharacter();
        TickFieldDropPositionSync();
    }

    private void PollLocalInputState()
    {
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
        return new PlayerNetworkInput
        {
            Move = _sandboxInput,
            CameraYaw = ResolveCameraYaw(),
            Jump = _sandboxJump,
            Punch = _leftClickUseTriggered,
            Drop = _dropTriggered,
            Throw = _throwTriggered,
            LeftGrabHold = _leftMouseDown && _leftMouseConsumedAsGrab,
            RightGrabHold = _rightMouseDown && _rightMouseConsumedAsGrab,
            Headbutt = Input.GetMouseButtonDown(2),
            Sprint = Input.GetKey(KeyCode.LeftShift)
        };
    }

    private void ResetOneShotLocalInput()
    {
        _sandboxJump = false;
        _leftClickUseTriggered = false;
    }

    private void SynchronizeNetworkSimulationState()
    {
        if (syncPhysicsObjects != null)
        {
            for (int i = 0; i < syncPhysicsObjects.Length; i++)
            {
                if (syncPhysicsObjects[i] != null)
                    BoneRotations.Set(i, syncPhysicsObjects[i].transform.localRotation);
            }
        }

        NetworkedIsActiveRagdoll = _isActiveRagdoll;
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

    private void ClampOutOfBoundsCharacter()
    {
        if (transform.position.y >= -10)
            return;

        rigidbody3D.position = Vector3.zero;
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity = Vector3.zero;
            rigidbody3D.angularVelocity = Vector3.zero;
        }
        ForceRecover();
    }
}
