using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private void Update()
    {
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

        if (Input.GetKeyDown(KeyCode.F))
            _dropTriggered = true;
        if (Input.GetMouseButtonDown(1))
            _throwTriggered = true;
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

    private PlayerNetworkInput BuildSandboxInput()
    {
        return new PlayerNetworkInput
        {
            Move = _sandboxInput,
            Jump = _sandboxJump,
            Punch = _leftClickUseTriggered,
            Drop = _dropTriggered,
            Throw = _throwTriggered,
            GrabHold = _leftMouseDown && _leftMouseConsumedAsGrab,
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
        for (int i = 0; i < syncPhysicsObjects.Length; i++)
            BoneRotations.Set(i, syncPhysicsObjects[i].transform.localRotation);

        NetworkedIsActiveRagdoll = _isActiveRagdoll;
    }

    private void UpdateGrabHandlers()
    {
        foreach (var handler in _handGrabHandlers)
            handler.UpdateState();
    }

    private void ClampOutOfBoundsCharacter()
    {
        if (transform.position.y >= -10)
            return;

        rigidbody3D.position = Vector3.zero;
        rigidbody3D.velocity = Vector3.zero;
        rigidbody3D.angularVelocity = Vector3.zero;
        ForceRecover();
    }
}
