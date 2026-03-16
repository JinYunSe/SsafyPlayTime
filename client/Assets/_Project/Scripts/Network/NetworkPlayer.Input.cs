using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private Vector3 _lastSafePosition;
    private Quaternion _lastSafeRotation = Quaternion.identity;
    private bool _hasLastSafeTransform;
    private float _nextOutOfBoundsRecoverAt;

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
            CameraYaw = ResolveCameraYaw(),
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

        var spawnGroup = FindObjectOfType<SpawnPointGroup>();
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

    private void ClampOutOfBoundsCharacter()
    {
        if (transform.position.y >= -10)
            return;

        var now = Runner != null ? (float)Runner.SimulationTime : Time.time;
        if (now < _nextOutOfBoundsRecoverAt)
            return;

        _nextOutOfBoundsRecoverAt = now + 0.5f;

        if (!TryResolveRecoveryTransform(out var recoveryPosition, out var recoveryRotation))
            recoveryRotation = transform.rotation;

        rigidbody3D.position = recoveryPosition;
        rigidbody3D.rotation = recoveryRotation;
        transform.SetPositionAndRotation(recoveryPosition, recoveryRotation);
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity = Vector3.zero;
            rigidbody3D.angularVelocity = Vector3.zero;
        }
        RememberSafeTransform(recoveryPosition, recoveryRotation);
        ForceRecover();
    }
}
