using SSAFYPlayTime.Character;
using UnityEngine;

// NetworkPlayer LateUpdate(카메라 앵커 갱신) 이후 실행을 보장하기 위해 실행 순서를 뒤로 설정
[DefaultExecutionOrder(100)]
public class CameraRig : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Transform target;
    [SerializeField] private string autoFindPlayerTag = "Player";
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
    [SerializeField] private float yawClampBlendInSpeed = 18f;
    [SerializeField] private float yawClampBlendOutSpeed = 8f;

    [Header("Collision")]
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float collisionBuffer = 0.1f;
    [SerializeField] private float minDistance = 1.25f;

    [Header("Impact")]
    [SerializeField] private float impactPositionStrength = 0.22f;
    [SerializeField] private float impactRotationStrength = 2.8f;
    [SerializeField] private float impactDecay = 16f;

    private float _targetYaw;
    private float _targetPitch;
    private bool _initializedAngles;
    private Vector3 _impactPositionOffset;
    private Vector2 _impactRotationOffset;
    private Transform _resolvedContextTarget;
    private CameraFollowAnchorContext _targetAnchorContext;
    private NetworkPlayer _targetPlayer;
    private float _yawClampWeight;
    private float _yawClampCenterYaw;
    private float _yawClampHalfAngle = 180f;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        ResolveTargetContext(force: true);
        InitializeAngles(force: true);
    }

    public void AddImpactImpulse(Vector3 worldDirection, float intensity, bool receivedHit)
    {
        intensity = Mathf.Clamp01(intensity);
        if (intensity <= 0f)
            return;

        var direction = worldDirection.sqrMagnitude > 0.0001f
            ? worldDirection.normalized
            : transform.forward;
        var localDirection = Quaternion.Inverse(transform.rotation) * direction;
        var lateral = Mathf.Clamp(localDirection.x, -1f, 1f);
        var vertical = Mathf.Clamp(localDirection.y, -1f, 1f);

        var positionKick = new Vector3(
            -lateral * impactPositionStrength * (receivedHit ? 1.15f : 0.55f),
            impactPositionStrength * (0.12f + Mathf.Abs(vertical) * 0.18f),
            (receivedHit ? -1f : 0.35f) * impactPositionStrength) * intensity;
        var rotationKick = new Vector2(
            (receivedHit ? impactRotationStrength : -impactRotationStrength * 0.4f),
            -lateral * impactRotationStrength * 0.9f) * intensity;

        _impactPositionOffset = Vector3.ClampMagnitude(
            _impactPositionOffset + positionKick,
            impactPositionStrength * 3f);
        _impactRotationOffset = Vector2.ClampMagnitude(
            _impactRotationOffset + rotationKick,
            impactRotationStrength * 1.6f);
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

        ResolveTargetContext(force: false);
        UpdateLookInput();
        ApplyTargetYawClamp(Time.deltaTime);

        // 팔로우 위치: _cameraFollowAnchor(NetworkPlayer)가 이미 스무딩을 담당하므로
        // CameraRig에서 추가 SmoothDamp 없이 직접 사용 (이중 스무딩 제거)
        var pivot = target.position + pivotOffset;
        var rot = Quaternion.Euler(_targetPitch, _targetYaw, 0f);
        var desiredPos = pivot + rot * new Vector3(0f, height, -distance);
        var resolvedPos = ResolveCameraPosition(pivot, desiredPos);
        ApplyImpactOffsets(ref resolvedPos, ref rot);

        transform.position = resolvedPos;
        transform.rotation = rot;
    }

    private void TryAutoFindTarget()
    {
        if (!autoFindTargetWhenNull || target != null)
            return;

        // 태그 탐색만 사용 — FindObjectOfType<NetworkPlayer> 제거
        // (멀티플레이어에서 원격 플레이어를 잘못 잡는 문제 방지)
        var tagged = GameObject.FindGameObjectWithTag(autoFindPlayerTag);
        if (tagged != null)
            SetTarget(tagged.transform);
    }

    private void InitializeAngles(bool force)
    {
        if (target == null)
            return;

        if (!force && _initializedAngles)
            return;

        var initialYaw = Mathf.Approximately(yaw, 0f) ? transform.eulerAngles.y : yaw;
        var initialPitch = Mathf.Approximately(pitch, 0f) ? NormalizePitch(transform.eulerAngles.x) : pitch;

        _targetYaw = initialYaw;
        _targetPitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        _yawClampCenterYaw = _targetYaw;
        _yawClampHalfAngle = 180f;
        _yawClampWeight = 0f;

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

    private void ResolveTargetContext(bool force)
    {
        if (!force && _resolvedContextTarget == target)
            return;

        _resolvedContextTarget = target;
        _targetAnchorContext = target != null ? target.GetComponent<CameraFollowAnchorContext>() : null;
        _targetPlayer = _targetAnchorContext != null ? _targetAnchorContext.Owner : null;

        if (_targetPlayer == null && target != null)
            _targetPlayer = target.GetComponent<NetworkPlayer>();

        if (_targetPlayer == null && target != null)
            _targetPlayer = target.GetComponentInParent<NetworkPlayer>();
    }

    private void ApplyTargetYawClamp(float deltaTime)
    {
        var centerYaw = _yawClampCenterYaw;
        var halfAngle = _yawClampHalfAngle;
        var hasClamp = _targetPlayer != null && _targetPlayer.TryGetCameraYawClamp(out centerYaw, out halfAngle);
        var clampSpeed = hasClamp ? yawClampBlendInSpeed : yawClampBlendOutSpeed;
        var alpha = 1f - Mathf.Exp(-Mathf.Max(0.01f, clampSpeed) * deltaTime);

        if (hasClamp)
        {
            if (_yawClampWeight <= 0.001f)
            {
                _yawClampCenterYaw = centerYaw;
                _yawClampHalfAngle = halfAngle;
            }
            else
            {
                _yawClampCenterYaw = Mathf.LerpAngle(_yawClampCenterYaw, centerYaw, alpha);
                _yawClampHalfAngle = Mathf.Lerp(_yawClampHalfAngle, halfAngle, alpha);
            }
        }
        else
        {
            _yawClampHalfAngle = Mathf.Lerp(_yawClampHalfAngle, 180f, alpha);
        }

        _yawClampWeight = Mathf.Lerp(_yawClampWeight, hasClamp ? 1f : 0f, alpha);
        if (_yawClampWeight <= 0.001f)
            return;

        var effectiveHalfAngle = Mathf.Lerp(180f, Mathf.Clamp(_yawClampHalfAngle, 1f, 179f), _yawClampWeight);
        var deltaYaw = Mathf.DeltaAngle(_yawClampCenterYaw, _targetYaw);
        _targetYaw = _yawClampCenterYaw + Mathf.Clamp(deltaYaw, -effectiveHalfAngle, effectiveHalfAngle);
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

    private void ApplyImpactOffsets(ref Vector3 position, ref Quaternion rotation)
    {
        if (_impactPositionOffset.sqrMagnitude > 0.000001f)
            position += rotation * _impactPositionOffset;

        if (_impactRotationOffset.sqrMagnitude > 0.000001f)
            rotation *= Quaternion.Euler(_impactRotationOffset.x, _impactRotationOffset.y, 0f);

        var decay = 1f - Mathf.Exp(-impactDecay * Time.deltaTime);
        _impactPositionOffset = Vector3.Lerp(_impactPositionOffset, Vector3.zero, decay);
        _impactRotationOffset = Vector2.Lerp(_impactRotationOffset, Vector2.zero, decay);
    }

    private static float NormalizePitch(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}
