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

    [Header("Collision")]
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float collisionBuffer = 0.1f;
    [SerializeField] private float minDistance = 1.25f;

    private float _targetYaw;
    private float _targetPitch;
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

        // 팔로우 위치: _cameraFollowAnchor(NetworkPlayer)가 이미 스무딩을 담당하므로
        // CameraRig에서 추가 SmoothDamp 없이 직접 사용 (이중 스무딩 제거)
        var pivot = target.position + pivotOffset;
        var rot = Quaternion.Euler(_targetPitch, _targetYaw, 0f);
        var desiredPos = pivot + rot * new Vector3(0f, height, -distance);
        var resolvedPos = ResolveCameraPosition(pivot, desiredPos);

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
