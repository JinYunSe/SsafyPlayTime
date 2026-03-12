using UnityEngine;

public class CameraRig : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Transform target;
    [SerializeField] private string autoFindPlayerTag = "Player";
    [SerializeField] private string autoFindTargetName = "Test";
    [SerializeField] private bool autoFindTargetWhenNull = true;

    [Header("Orbit")]
    [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private float distance = 8f;
    [SerializeField] private float height = 2.2f;
    [SerializeField] private float yaw = 180f;
    [SerializeField] private float pitch = 15f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 70f;
    [SerializeField] private float mouseSensitivity = 2.2f;
    [SerializeField] private bool invertY;
    [SerializeField] private bool lockCursor = true;
    [SerializeField] private float lookDeadZone = 0.01f;

    [Header("Smoothing")]
    [SerializeField] private float followSmooth = 14f;
    [SerializeField] private float rotateSmooth = 18f;

    [Header("Collision")]
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float collisionBuffer = 0.1f;
    [SerializeField] private float minDistance = 1.25f;

    private float _targetYaw;
    private float _targetPitch;
    private float _currentYaw;
    private float _currentPitch;
    private float _yawVelocity;
    private float _pitchVelocity;
    private Vector3 _currentPivot;
    private Vector3 _pivotVelocity;
    private bool _initializedAngles;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        InitializeAngles(force: true);
    }

    private void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        TryAutoFindTarget();
        InitializeAngles(force: false);
    }

    private void LateUpdate()
    {
        TryAutoFindTarget();

        if (target == null)
            return;

        UpdateLookInput();

        _currentYaw = Mathf.SmoothDampAngle(_currentYaw, _targetYaw, ref _yawVelocity, 1f / Mathf.Max(0.01f, rotateSmooth));
        _currentPitch = Mathf.SmoothDampAngle(_currentPitch, _targetPitch, ref _pitchVelocity, 1f / Mathf.Max(0.01f, rotateSmooth));

        var pivot = target.position + pivotOffset;
        _currentPivot = Vector3.SmoothDamp(_currentPivot, pivot, ref _pivotVelocity, 1f / Mathf.Max(0.01f, followSmooth));

        var desiredRot = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        var desiredPos = _currentPivot + desiredRot * new Vector3(0f, height, -distance);
        var resolvedPos = ResolveCameraPosition(_currentPivot, desiredPos);

        var followAlpha = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
        var rotateAlpha = 1f - Mathf.Exp(-rotateSmooth * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, resolvedPos, followAlpha);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotateAlpha);
    }

    private void TryAutoFindTarget()
    {
        if (!autoFindTargetWhenNull || target != null)
            return;

        var tagged = GameObject.FindGameObjectWithTag(autoFindPlayerTag);
        if (tagged != null)
        {
            SetTarget(tagged.transform);
            return;
        }

        if (!string.IsNullOrWhiteSpace(autoFindTargetName))
        {
            var namedTarget = GameObject.Find(autoFindTargetName);
            if (namedTarget != null)
            {
                SetTarget(namedTarget.transform);
                return;
            }
        }

        var networkPlayer = FindObjectOfType<NetworkPlayer>();
        if (networkPlayer != null)
            SetTarget(networkPlayer.transform);
    }

    private void InitializeAngles(bool force)
    {
        if (target == null)
            return;

        if (!force && _initializedAngles)
            return;

        if (force || !_initializedAngles)
        {
            var initialYaw = Mathf.Approximately(yaw, 0f) ? transform.eulerAngles.y : yaw;
            var initialPitch = Mathf.Approximately(pitch, 0f) ? NormalizePitch(transform.eulerAngles.x) : pitch;

            _targetYaw = initialYaw;
            _targetPitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
            _currentYaw = _targetYaw;
            _currentPitch = _targetPitch;
            _currentPivot = target.position + pivotOffset;
        }

        _initializedAngles = true;
    }

    private void UpdateLookInput()
    {
        var lookX = Input.GetAxis("Mouse X");
        var lookY = Input.GetAxis("Mouse Y");
        var look = new Vector2(lookX, lookY);

        if (look.sqrMagnitude < lookDeadZone * lookDeadZone)
            return;

        _targetYaw += look.x * mouseSensitivity;
        _targetPitch += (invertY ? look.y : -look.y) * mouseSensitivity;
        _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);
    }

    private Vector3 ResolveCameraPosition(Vector3 pivot, Vector3 desiredPos)
    {
        var toCamera = desiredPos - pivot;
        var distanceToCamera = toCamera.magnitude;
        if (distanceToCamera <= 0.001f)
            return desiredPos;

        var direction = toCamera / distanceToCamera;
        if (!Physics.SphereCast(pivot, collisionRadius, direction, out var hit, distanceToCamera, obstructionMask, QueryTriggerInteraction.Ignore))
            return desiredPos;

        var resolvedDistance = Mathf.Max(minDistance, hit.distance - collisionBuffer);
        return pivot + direction * resolvedDistance;
    }

    private static float NormalizePitch(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}
